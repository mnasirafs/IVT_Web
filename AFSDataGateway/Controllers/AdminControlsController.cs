using AFSDataGateway.Models;
using AFSDataGateway.Services.Chat;
using AFSDataGateway.Services.ML;
using System;
using System.Data.Entity;
using System.Diagnostics;
using System.Web.Mvc;

namespace AFSDataGateway.Controllers
{
    // Admin Controls: operational tasks for IVT administrators (ML scoring now; more options later).
    // Only real administrators (UserType 1/2, not impersonating an AM) can use it.
    [Authorize]
    public sealed class AdminControlsController : Controller
    {
        public ActionResult Index(string notice, bool? error)
        {
            ChatAccessScope scope;
            if (!IsAdmin(out scope)) return Denied();
            ViewBag.DisplayForSuperAdmin = "visible";
            ViewBag.Notice = notice;
            ViewBag.NoticeIsError = error == true;
            ViewBag.AssistantConfigured = AzureOpenAiChatClient.Configured;
            try { ViewBag.AssistantSwitch = AssistantSwitch.Read(); }
            catch (Exception ex) { Trace.TraceError("Assistant switch read failed: {0}", ex.GetType().Name); ViewBag.AssistantSwitch = null; }
            return View();
        }

        // Turns Vera (IVT AI Assistant) on or off for everyone; takes effect within ~30 seconds on every server.
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult SetAssistantEnabled(bool enabled)
        {
            ChatAccessScope scope;
            if (!IsAdmin(out scope)) return Denied();
            try
            {
                AssistantSwitch.Set(enabled, scope.Actor.ToLowerInvariant());
                return RedirectToAction("Index", new { notice = enabled ? "Vera is now enabled." : "Vera is now disabled for all users." });
            }
            catch (Exception ex)
            {
                Trace.TraceError("Assistant switch update failed: {0}", ex.GetType().Name);
                return RedirectToAction("Index", new { notice = "The setting could not be saved. Make sure migration V013 (ML.AppSetting) has been run.", error = true });
            }
        }

        public ActionResult MLScoring(string notice, bool? error)
        {
            ChatAccessScope scope;
            if (!IsAdmin(out scope)) return Denied();
            using (var db = new IVTEntities())
            {
                var status = new MLJobRunner(db).Status();
                status.Message = notice;
                status.MessageIsError = error == true;
                ViewBag.DisplayForSuperAdmin = "visible";
                return View(status);
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult RunMLScoring(DateTime? week, bool allowCandidate = true)
        {
            ChatAccessScope scope;
            if (!IsAdmin(out scope)) return Denied();
            string problem;
            try
            {
                using (var db = new IVTEntities())
                {
                    var runner = new MLJobRunner(db);
                    var target = week ?? runner.Status().CurrentWeek;
                    problem = target.HasValue ? runner.StartScoring(target.Value, allowCandidate, scope.Actor.ToLowerInvariant())
                                              : "No current invoice week was found in dbo.Sell_Side.";
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("ML scoring launch failed: {0}", ex.GetType().Name);
                problem = "The scoring run could not be started (" + ex.GetType().Name + "). Check the server configuration.";
            }
            return RedirectToAction("MLScoring", new
            {
                notice = problem ?? "Scoring started. It usually takes 5 to 15 minutes; refresh this page to follow progress. Results appear on ML Findings when it succeeds.",
                error = problem != null
            });
        }

        // Assistant knowledge: answer feedback from users plus the glossary injected into every assistant request.
        public ActionResult AssistantKnowledge(string notice, bool? error, string feedbackStatus = "NEW")
        {
            ChatAccessScope scope;
            if (!IsAdmin(out scope)) return Denied();
            using (var db = new IVTEntities())
            {
                var service = new ChatKnowledgeService(db);
                ViewBag.Notice = notice;
                ViewBag.NoticeIsError = error == true;
                ViewBag.FeedbackStatus = feedbackStatus;
                ViewBag.Feedback = service.Feedback(feedbackStatus == "ALL" ? null : feedbackStatus);
                ViewBag.DisplayForSuperAdmin = "visible";
                return View(service.AllTerms());
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult AddGlossaryTerm(string term, string meaning, string kind, long? feedbackId)
        {
            ChatAccessScope scope;
            if (!IsAdmin(out scope)) return Denied();
            return Knowledge(db =>
            {
                var service = new ChatKnowledgeService(db);
                service.AddTerm(scope.Actor.ToLowerInvariant(), true, term, meaning, kind);
                if (feedbackId.HasValue) service.ReviewFeedback(feedbackId.Value, "ACTIONED", "Added glossary term: " + term, scope.Actor.ToLowerInvariant());
                return "Term added for everyone.";
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ReviewGlossaryTerm(int id, string decision)
        {
            ChatAccessScope scope;
            if (!IsAdmin(out scope)) return Denied();
            return Knowledge(db => { new ChatKnowledgeService(db).ReviewTerm(id, decision, scope.Actor.ToLowerInvariant()); return "Term updated."; });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ReviewChatFeedback(long id, string status, string note)
        {
            ChatAccessScope scope;
            if (!IsAdmin(out scope)) return Denied();
            return Knowledge(db => { new ChatKnowledgeService(db).ReviewFeedback(id, status, note, scope.Actor.ToLowerInvariant()); return "Feedback updated."; });
        }

        private ActionResult Knowledge(Func<IVTEntities, string> change)
        {
            string notice; var failed = false;
            try { using (var db = new IVTEntities()) notice = change(db); }
            catch (ArgumentException ex) { notice = ex.Message; failed = true; }
            catch (Exception ex) { Trace.TraceError("Assistant knowledge update failed: {0}", ex.GetType().Name); notice = "The change could not be saved."; failed = true; }
            return RedirectToAction("AssistantKnowledge", new { notice, error = failed });
        }

        private bool IsAdmin(out ChatAccessScope scope)
        {
            scope = null;
            try
            {
                using (var db = new IVTEntities())
                {
                    db.Database.CommandTimeout = 30;
                    // Impersonation is ignored on purpose: admin tasks run under the admin's own identity only.
                    scope = ChatAccessScope.Resolve(User, null, db.vIVTUser.AsNoTracking());
                    return scope.IsAdmin;
                }
            }
            catch (ChatAccessException) { return false; }
        }

        private ActionResult Denied()
        {
            return new HttpStatusCodeResult(403, "Admin Controls are available to IVT administrators only.");
        }
    }
}
