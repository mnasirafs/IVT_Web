using AFSDataGateway.Models;
using AFSDataGateway.Services.Chat;
using Newtonsoft.Json;
using System;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace AFSDataGateway.Controllers
{
    [ChatAuthorize]
    public sealed class ChatController : Controller
    {
        private const string SessionKey = "IVT.Chat.Conversation";

        // Deployment diagnostics: reports only yes/no flags, never setting values or data.
        [HttpGet, AllowAnonymous]
        public ActionResult Status()
        {
            NoCache();
            var status = AzureOpenAiChatClient.Diagnose();
            bool? userMapped = null;
            if (Request.IsAuthenticated)
            {
                try { using (var db = new IVTEntities()) { Resolve(db); userMapped = true; } }
                catch (ChatAccessException) { userMapped = false; }
                catch (Exception ex) { LogFailure(ex); }
            }
            return Json(new
            {
                requestIsAuthenticated = Request.IsAuthenticated,
                authenticationType = Request.IsAuthenticated ? User.Identity.AuthenticationType : null,
                endpointSet = status.EndpointSet, endpointValid = status.EndpointValid,
                apiKeySet = status.ApiKeySet, deploymentSet = status.DeploymentSet,
                killSwitchOff = status.KillSwitchOff, adminSwitchOn = status.AdminSwitchOn, chatEnabled = AzureOpenAiChatClient.Enabled,
                userMappedInIVT = userMapped,
                widgetVisible = Request.IsAuthenticated && AzureOpenAiChatClient.Enabled
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult Context()
        {
            NoCache();
            if (!AzureOpenAiChatClient.Enabled) return Error(503, "Chat is not enabled yet.");
            try
            {
                using (var db = new IVTEntities())
                {
                    var scope = Resolve(db);
                    var state = Session[SessionKey] as ChatConversation;
                    if (state == null || state.ScopeFingerprint != scope.Fingerprint)
                        Session[SessionKey] = state = new ChatConversation(scope.Fingerprint);
                    // History lets the widget restore the conversation after the user moves to another page.
                    return Json(new { token = state.Token, scope = scope.Label,
                        history = state.Turns.Select(t => new { id = t.Id, question = t.Question, answer = t.Answer, rating = t.Rating }),
                        pendingAction = state.Pending == null || state.Pending.Expired ? null : new { id = state.Pending.Id, kind = state.Pending.Kind, summary = state.Pending.Summary }
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (ChatAccessException) { Session.Remove(SessionKey); return Error(403, "Chat is not available for this account or selected Account Manager."); }
            catch (Exception ex) { LogFailure(ex); return Error(503, "Chat could not load your access permissions. Please try again later."); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reset()
        {
            NoCache();
            Session.Remove(SessionKey);
            return Json(new { reset = true });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Ask(string question, string token)
        {
            NoCache();
            if (!AzureOpenAiChatClient.Enabled) return Error(503, "Chat is not enabled yet.");
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(question) || question.Length > 2000)
                return Error(400, "Please enter a question of 1 to 2,000 characters.");
            var requestId = Guid.NewGuid().ToString("N");
            try
            {
                using (var db = new IVTEntities())
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(150)))
                {
                    var scope = Resolve(db);
                    var state = Session[SessionKey] as ChatConversation;
                    if (state == null || !state.Matches(scope, token))
                    {
                        Session.Remove(SessionKey);
                        return Error(409, "Your account scope or conversation changed. Start a new conversation.");
                    }
                    if (!ChatRateLimit.TryAcquire(scope.Actor)) return Error(429, "Please wait a moment before asking another question.");
                    var client = new AzureOpenAiChatClient();
                    var queries = new ChatQueryService(new ChatDataSource(db), scope);
                    var actions = new ChatActionService(db, scope);
                    ChatPendingAction proposed = null;
                    var glossary = new ChatKnowledgeService(db).PromptGlossary(scope.Actor);
                    var answer = await client.AnswerAsync(question.Trim(), state.Turns.ToList(), glossary, scope.Label, (name, args) =>
                    {
                        // Re-resolve roles for every tool invocation, including after model/network latency.
                        var current = Resolve(db);
                        if (current.Fingerprint != scope.Fingerprint) throw new ChatAccessException();
                        // Hold/release tools only validate and prepare; the user confirms separately (ConfirmAction).
                        object result;
                        if (name == "ProposeHoldConnote")
                            result = actions.ProposeHold((string)args["connote"], (string)args["reason"], p => proposed = p);
                        else if (name == "ProposeEmailReport")
                            result = actions.ProposeEmailReport((string)args["title"], args["report"] as Newtonsoft.Json.Linq.JObject, p => proposed = p);
                        else if (name == "ProposeVettingCompletion")
                            result = actions.ProposeVettingCompletion((string)args["message"], p => proposed = p);
                        else if (name == "ProposeReleaseConnote")
                            result = actions.ProposeRelease((string)args["connote"], (string)args["justification"], p => proposed = p);
                        else if (name == "ProposeBulkRelease")
                            result = actions.ProposeBulkRelease((string)args["reasonPrefix"], (string)args["justification"], p => proposed = p);
                        else
                            result = queries.Execute(name, args);
                        Trace.TraceInformation("IVT chat {0}: actor={1}; scope={2}; tool={3}", requestId, scope.Actor, scope.Label, name);
                        return result;
                    }, cancellation.Token);
                    if (!state.Matches(Resolve(db), token))
                    {
                        Session.Remove(SessionKey);
                        return Error(409, "Your access changed. Start a new conversation.");
                    }
                    var turn = state.Remember(question.Trim(), answer.Text, answer.ToolCalls.Count == 0 ? null : answer.ToolCalls.ToString(Formatting.None));
                    if (proposed != null) state.Pending = proposed;
                    var dataEvidence = answer.Evidence.Where(e => !e.Function.StartsWith("Propose", StringComparison.Ordinal)).ToList();
                    state.LastQuestion = question.Trim();
                    state.LastAnswerText = answer.Text;
                    state.LastEvidenceJson = dataEvidence.Count == 0 ? null : JsonConvert.SerializeObject(dataEvidence);
                    Session[SessionKey] = state;
                    // Recent questions/answers are kept per conversation; the conversation is discarded whenever
                    // the access scope changes, and the model is told to re-fetch data for every number.
                    return Content(JsonConvert.SerializeObject(new { answer = answer.Text, turnId = turn.Id,
                        evidence = dataEvidence, canEmail = true,
                        pendingAction = proposed == null ? null : new { id = proposed.Id, kind = proposed.Kind, summary = proposed.Summary },
                        scope = scope.Label, token = state.Token, requestId }), "application/json");
                }
            }
            catch (ChatAccessException) { Session.Remove(SessionKey); return Error(403, "Your access changed or the request is outside your permitted scope."); }
            catch (ChatUnavailableException ex) { return Error(503, ex.Message); }
            catch (OperationCanceledException) { return Error(504, "The assistant took too long. Please try a narrower question."); }
            catch (Exception ex) { LogFailure(ex); return Error(503, "The assistant is temporarily unavailable. Reference: " + requestId); }
        }

        // Applies a prepared hold/release after the user pressed Confirm. Re-validates scope, token, expiry and
        // current hold state before writing through the existing IVT workflow.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> ConfirmAction(string token, string actionId)
        {
            NoCache();
            if (!AzureOpenAiChatClient.Enabled) return Error(503, "Chat is not enabled yet.");
            try
            {
                using (var db = new IVTEntities())
                {
                    var scope = Resolve(db);
                    var state = Session[SessionKey] as ChatConversation;
                    if (state == null || !state.Matches(scope, token))
                    {
                        Session.Remove(SessionKey);
                        return Error(409, "Your account scope or conversation changed. Start a new conversation.");
                    }
                    var pending = state.Pending;
                    if (pending == null || pending.Id != actionId)
                        return Error(409, "This request is no longer pending. Ask the assistant again.");
                    state.Pending = null;  // one use only, even if execution fails
                    Session[SessionKey] = state;
                    if (pending.Expired) return Error(409, "This request expired after 10 minutes. Ask the assistant again.");
                    if (!ChatRateLimit.TryAcquire(scope.Actor)) return Error(429, "Please wait a moment and try again.");
                    var result = await new ChatActionService(db, scope).ExecuteAsync(pending);
                    Trace.TraceInformation("IVT chat action: actor={0}; scope={1}; kind={2}; job={3}; success={4}", scope.Actor, scope.Label, pending.Kind, pending.JobNumber, result.Success);
                    if (!result.Success) return Error(409, result.Message);
                    return Json(new { message = result.Message, kind = pending.Kind });
                }
            }
            catch (ChatAccessException) { Session.Remove(SessionKey); return Error(403, "Your access changed or the request is outside your permitted scope."); }
            catch (Exception ex) { LogFailure(ex); return Error(503, "The change could not be applied. Nothing was changed. Please try again or use the IVT screens."); }
        }

        // "Email this answer": sends the latest answer and its source data (CSV) to the signed-in user's own mailbox.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> EmailAnswer(string token)
        {
            NoCache();
            if (!AzureOpenAiChatClient.Enabled) return Error(503, "Chat is not enabled yet.");
            try
            {
                using (var db = new IVTEntities())
                {
                    var scope = Resolve(db);
                    var state = Session[SessionKey] as ChatConversation;
                    if (state == null || !state.Matches(scope, token)) return Error(409, "Your conversation changed. Ask again, then email the answer.");
                    if (state.LastAnswerText == null) return Error(409, "There is no answer to email yet.");
                    if (!ChatRateLimit.TryAcquire(scope.Actor)) return Error(429, "Please wait a moment and try again.");
                    var tables = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Newtonsoft.Json.Linq.JArray>>();
                    if (state.LastEvidenceJson != null)
                    {
                        var i = 0;
                        foreach (Newtonsoft.Json.Linq.JObject e in Newtonsoft.Json.Linq.JArray.Parse(state.LastEvidenceJson))
                            tables.AddRange(ChatReportService.Tables(e["Data"], (++i) + "_" + (string)e["Function"]));
                    }
                    var to = ChatReportService.RecipientFor(scope);
                    await ChatReportService.SendAsync(to, "IVT assistant: " + Truncate(state.LastQuestion, 80),
                        "<p>Hi,</p><p><b>Your question:</b> " + ChatReportService.Html(state.LastQuestion) + "</p><p><b>Answer:</b><br/>" +
                        ChatReportService.Html(state.LastAnswerText) + "</p>", tables, scope.Label);
                    Trace.TraceInformation("IVT chat email: actor={0}; tables={1}", scope.Actor, tables.Count);
                    return Json(new { message = "Emailed to " + to + (tables.Count > 0 ? " with " + tables.Count + " CSV attachment(s)." : ".") });
                }
            }
            catch (ChatAccessException) { Session.Remove(SessionKey); return Error(403, "Your access changed. Please start a new conversation."); }
            catch (Exception ex) { LogFailure(ex); return Error(503, "The email could not be sent. Please try again later."); }
        }

        // Thumbs up/down (+ optional comment) on one answer of the current conversation.
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Feedback(string token, string turnId, int rating, string comment)
        {
            NoCache();
            if (rating != 1 && rating != -1) return Error(400, "Invalid rating.");
            if (comment != null && comment.Length > 2000) return Error(400, "Please keep comments under 2,000 characters.");
            try
            {
                using (var db = new IVTEntities())
                {
                    var scope = Resolve(db);
                    var state = Session[SessionKey] as ChatConversation;
                    var turn = state != null && state.Matches(scope, token) ? state.Find(turnId) : null;
                    if (turn == null) return Error(409, "This answer is no longer in your conversation.");
                    new ChatKnowledgeService(db).SaveFeedback(scope.Actor, scope.Label, turn, rating, comment);
                    turn.Rating = rating;
                    Session[SessionKey] = state;
                    return Json(new { message = "Thanks - your feedback helps improve the assistant." });
                }
            }
            catch (ChatAccessException) { return Error(403, "Your access changed. Please start a new conversation."); }
            catch (Exception ex) { LogFailure(ex); return Error(503, "Feedback could not be saved. Please try again later."); }
        }

        // "Teach the assistant": the user's own terms apply to their chats immediately; admins approve them for everyone.
        [HttpGet]
        public ActionResult Terms()
        {
            NoCache();
            try
            {
                using (var db = new IVTEntities())
                {
                    var scope = Resolve(db);
                    var terms = new ChatKnowledgeService(db).MyTerms(scope.Actor)
                        .Select(t => new { id = t.GlossaryId, term = t.Term, meaning = t.Meaning, kind = t.Kind, status = t.Status });
                    return Json(new { terms }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (ChatAccessException) { return Error(403, "Chat is not available for this account."); }
            catch (Exception ex) { LogFailure(ex); return Error(503, "Terms could not be loaded."); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult AddTerm(string term, string meaning, string kind)
        {
            NoCache();
            if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(meaning) || term.Length > 100 || meaning.Length > 500)
                return Error(400, "Enter a term (up to 100 characters) and its meaning (up to 500).");
            try
            {
                using (var db = new IVTEntities())
                {
                    var scope = Resolve(db);
                    // Suggestions are always user-scoped from the chat; admins publish globally from Admin Controls.
                    new ChatKnowledgeService(db).AddTerm(scope.Actor, false, term, meaning, kind);
                    return Json(new { message = "Saved. The assistant uses it in your chats now; an admin will review it for everyone." });
                }
            }
            catch (ChatAccessException) { return Error(403, "Chat is not available for this account."); }
            catch (Exception ex) { LogFailure(ex); return Error(503, "The term could not be saved."); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DeleteTerm(int id)
        {
            NoCache();
            try
            {
                using (var db = new IVTEntities())
                {
                    new ChatKnowledgeService(db).DeleteOwnTerm(Resolve(db).Actor, id);
                    return Json(new { deleted = true });
                }
            }
            catch (ChatAccessException) { return Error(403, "Chat is not available for this account."); }
            catch (Exception ex) { LogFailure(ex); return Error(503, "The term could not be deleted."); }
        }

        private static string Truncate(string value, int max)
        {
            value = value ?? "";
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CancelAction(string token)
        {
            NoCache();
            var state = Session[SessionKey] as ChatConversation;
            if (state != null && state.Token == token)
            {
                state.Pending = null;
                Session[SessionKey] = state;
            }
            return Json(new { cancelled = true });
        }

        private ChatAccessScope Resolve(IVTEntities db)
        {
            db.Database.CommandTimeout = 20;
            return ChatAccessScope.Resolve(User, Session["LoggedAs"] as string, db.vIVTUser.AsNoTracking());
        }

        private void NoCache()
        {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
        }

        private ActionResult Error(int status, string message)
        {
            Response.StatusCode = status;
            Response.TrySkipIisCustomErrors = true;
            return Json(new { error = message }, JsonRequestBehavior.AllowGet);
        }

        private static void LogFailure(Exception ex)
        {
            // Never log prompts, retrieved records, credentials, connection strings or upstream bodies.
            Trace.TraceError("IVT chat failed: {0}", ex.GetType().Name);
        }
    }

    public sealed class ChatAuthorizeAttribute : AuthorizeAttribute
    {
        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            filterContext.HttpContext.Response.SuppressFormsAuthenticationRedirect = true;
            filterContext.HttpContext.Response.TrySkipIisCustomErrors = true;
            filterContext.HttpContext.Response.Cache.SetNoStore();
            filterContext.HttpContext.Response.StatusCode = 401;
            filterContext.Result = new JsonResult { Data = new { error = "Please sign in again to use chat." }, JsonRequestBehavior = JsonRequestBehavior.AllowGet };
        }
    }

    internal static class ChatRateLimit
    {
        private static readonly object Gate = new object();
        private sealed class Window { public DateTime Start; public DateTime Last; public int Count; }
        public static bool TryAcquire(string actor)
        {
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                var key = "IVT.Chat.Rate." + actor;
                var window = HttpRuntime.Cache[key] as Window;
                if (window == null)
                {
                    window = new Window { Start = now };
                    HttpRuntime.Cache.Insert(key, window, null, now.AddMinutes(1), System.Web.Caching.Cache.NoSlidingExpiration);
                }
                if (window.Count >= 12 || now - window.Last < TimeSpan.FromSeconds(2)) return false;
                window.Count++;
                window.Last = now;
                return true;
            }
        }
    }
}
