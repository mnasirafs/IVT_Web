using AFSDataGateway.Controllers;
using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.Chat
{
    // A hold/release prepared by the assistant. Nothing is written until the user clicks Confirm in the chat panel;
    // the request is then validated again against current data before the existing IVT workflow is applied.
    [Serializable]
    public sealed class ChatPendingAction
    {
        public string Id { get; set; }
        public string Kind { get; set; }            // HOLD | RELEASE
        public string JobNumber { get; set; }
        public string Connote { get; set; }
        public string Customer { get; set; }
        public string Carrier { get; set; }
        public string Text { get; set; }            // hold reason or release justification
        public string ScopeFingerprint { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string Summary { get; set; }
        public string ReportSpecJson { get; set; }  // EMAIL: validated QueryDataset arguments, re-run on confirm
        public List<long> HoldIds { get; set; }      // BULK_RELEASE: exact reviewed snapshot, never reselect by prefix on confirm
        public string ReasonPrefix { get; set; }
        public string HoldSnapshotJson { get; set; }
        public bool Expired { get { return DateTime.UtcNow - CreatedUtc > TimeSpan.FromMinutes(10); } }
    }

    public sealed class ChatActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public sealed class ChatActionService
    {
        public const int MinTextLength = 10, MaxTextLength = 1000;
        private readonly IVTEntities db;
        private readonly ChatAccessScope scope;

        public ChatActionService(IVTEntities db, ChatAccessScope scope)
        {
            this.db = db;
            this.scope = scope;
        }

        // Login recorded as ActionedBy: same rule as the IVT screens (the effective/impersonated AM, or the admin).
        private string ActingLogin { get { return (scope.IsAdmin ? scope.Actor : scope.LoginName).ToLowerInvariant(); } }

        // ---------- proposals (validation only, no writes) ----------

        public object ProposeHold(string reference, string reason, Action<ChatPendingAction> stage)
        {
            string problem;
            var text = CleanText(reason, "hold reason", out problem);
            if (problem != null) return Rejected(problem);
            var job = ResolveCurrentJob(reference, out problem);
            if (job == null) return Rejected(problem);

            var active = ActiveHolds(job.JobNumber).ToList();
            if (active.Any())
            {
                var h = active.OrderByDescending(x => x.ActionTime).First();
                return Rejected("Connote " + job.ConsignmentNote + " (job " + job.JobNumber + ") is already on hold since " + Format(h.ActionTime) +
                                " by " + h.ActionedBy + " (" + (h.ShortErrDesc ?? h.Reason) + "). It cannot be held twice.");
            }
            var pending = Stage(stage, "HOLD", job, text,
                "Hold connote " + job.ConsignmentNote + " (job " + job.JobNumber + ", " + job.Customer + " via " + job.Carrier + ", week " + Format(job.CurrentWeek) + ")" +
                (ExceptionFor(job.JobNumber) != null ? " against its current IVT exception" : " as an AM custom hold") + ". Reason: " + text);
            return Ready(pending);
        }

        public object ProposeRelease(string reference, string justification, Action<ChatPendingAction> stage)
        {
            string problem;
            var text = CleanText(justification, "release justification", out problem);
            if (problem != null) return Rejected(problem);
            var key = Normalize(reference);
            if (key == null) return Rejected("Please give the connote or job number to release.");

            var holds = ScopedActiveHolds().Where(h => h.Connote == key || h.jno == key).ToList();
            var jobs = holds.Select(h => h.jno).Distinct().ToList();
            if (jobs.Count > 1)
                return Rejected("More than one held job matches " + key + ": " + string.Join(", ", jobs.Take(5)) + ". Ask the user which job number to release.");
            if (jobs.Count == 0)
            {
                var lastRelease = db.ReleasedConsignment.AsNoTracking().Where(r => r.Connote == key || r.jno == key)
                    .OrderByDescending(r => r.ActionTime).FirstOrDefault();
                if (lastRelease != null && !ActiveHoldsAnyUser(lastRelease.jno).Any())
                    return Rejected("Connote " + lastRelease.Connote + " (job " + lastRelease.jno + ") is not on hold: it was already released on " +
                                    Format(lastRelease.ActionTime) + " by " + lastRelease.ActionedBy + ".");
                return Rejected("No active hold for " + key + " was found under your account. Only consignments currently on hold that you actioned can be released.");
            }
            var hold = holds.OrderByDescending(h => h.ActionTime).First();
            var pending = new ChatPendingAction
            {
                Id = Guid.NewGuid().ToString("N"), Kind = "RELEASE", JobNumber = hold.jno, Connote = hold.Connote, Customer = hold.Customer,
                Carrier = hold.Carrier, Text = text, ScopeFingerprint = scope.Fingerprint, CreatedUtc = DateTime.UtcNow,
                Summary = "Release connote " + hold.Connote + " (job " + hold.jno + ", " + hold.Customer + " via " + hold.Carrier + "), held since " +
                          Format(hold.ActionTime) + " for '" + (hold.ShortErrDesc ?? hold.Reason) + "'. Justification: " + text
            };
            stage(pending);
            return Ready(pending);
        }

        public object ProposeBulkRelease(string reasonPrefix, string justification, Action<ChatPendingAction> stage)
        {
            string problem;
            var text = CleanText(justification, "release justification", out problem);
            if (problem != null) return Rejected(problem);
            var prefix = (reasonPrefix ?? "").Trim();
            if (prefix.Length < 3 || prefix.Length > 100)
                return Rejected("Give a hold reason prefix of 3 to 100 characters, for example FORCE HOLD.");
            var holds = ScopedActiveHolds().Where(h => h.Reason.StartsWith(prefix)).OrderBy(h => h.Id).Take(101).ToList();
            if (holds.Count == 0) return Rejected("No active holds with that reason prefix were found in your permitted scope.");
            if (holds.Count > 100) return Rejected("More than 100 hold rows match. Narrow the reason prefix or use the Held Consignments screen to select up to 100 rows. No batch was prepared.");
            // Do not close part of a duplicated job's hold history.
            var jobs = holds.Select(h => h.jno).Distinct().ToList();
            if (jobs.Any(string.IsNullOrWhiteSpace))
                return Rejected("A matching hold has no job number. Review it in Held Consignments before bulk releasing.");
            if (ScopedActiveHolds().Count(h => jobs.Contains(h.jno)) != holds.Count)
                return Rejected("A matching job also has another active hold with a different reason. Review that job in Held Consignments before bulk releasing.");
            var pending = new ChatPendingAction
            {
                Id = Guid.NewGuid().ToString("N"), Kind = "BULK_RELEASE", Text = text, ReasonPrefix = prefix,
                HoldIds = holds.Select(h => h.Id).ToList(), HoldSnapshotJson = HoldSnapshot(holds), ScopeFingerprint = scope.Fingerprint, CreatedUtc = DateTime.UtcNow,
                Summary = "Release " + jobs.Count + " job(s) (" + holds.Count + " hold rows) whose reason starts with '" + prefix + "'. " +
                    "Jobs: " + string.Join(", ", jobs) + ". Shared justification: " + text + ". This updates IVT only."
            };
            stage(pending);
            return Ready(pending);
        }

        // Vetting completion: the message is optional (same as the "Vetting Completed" button on the IVT screens).
        public object ProposeVettingCompletion(string message, Action<ChatPendingAction> stage)
        {
            var text = (message ?? "").Trim();
            if (text.Length > MaxTextLength) return Rejected("The message is too long (maximum " + MaxTextLength + " characters).");
            var login = ActingLogin;
            var week = db.Database.SqlQuery<DateTime?>("SELECT TOP 1 InvoiceWeek FROM WebIVT.FileLoadControl").FirstOrDefault();
            var previous = db.Database.SqlQuery<DateTime?>(
                "SELECT MAX(ActivityTime) FROM dbo.AM_Activity WHERE LoginName = @p0 AND Activity = 'Vetting Completed' AND InvoiceWeek = (SELECT TOP 1 InvoiceWeek FROM WebIVT.FileLoadControl)",
                login).FirstOrDefault();
            var openHolds = db.ConsignmentForReview.Count(h => !h.IsDeleted && h.ActionedBy == login);
            var summary = "Mark vetting as completed for invoice week " + (week.HasValue ? week.Value.ToString("dd MMM yyyy") : "(unknown)") +
                          " and notify the IVT admins. Message: " + (text.Length == 0 ? "(none)" : text) + ".";
            if (previous.HasValue) summary += " Note: you already completed vetting this week on " + Format(previous) + "; this sends another notification.";
            if (openHolds > 0) summary += " You currently have " + openHolds + " connote(s) on hold.";
            var pending = new ChatPendingAction
            {
                Id = Guid.NewGuid().ToString("N"), Kind = "VETTING", Text = text.Length == 0 ? null : text, ScopeFingerprint = scope.Fingerprint,
                CreatedUtc = DateTime.UtcNow, Summary = summary
            };
            stage(pending);
            return new { status = "awaiting_user_confirmation", action = "VETTING", summary, alreadyCompletedThisWeek = previous.HasValue, openHolds,
                         note = "NOT done yet. Tell the user to review the summary and press Confirm in the chat panel. Never say it has been completed." };
        }

        // "Email me <report>": validates the query now (scope + filters) and reports the row count; the report is
        // re-run and emailed to the user's own mailbox only after Confirm.
        public object ProposeEmailReport(string title, Newtonsoft.Json.Linq.JObject report, Action<ChatPendingAction> stage)
        {
            var name = (title ?? "").Trim();
            if (name.Length < 3 || name.Length > 120) return Rejected("Give the report a short title (3 to 120 characters).");
            if (report == null) return Rejected("Describe which data the report should contain.");
            object result;
            try { result = new ChatQueryService(new ChatDataSource(db), scope).ExportDataset(report); }
            catch (ChatQueryException ex) { return Rejected("The report query is not valid: " + ex.Message); }
            var tables = ChatReportService.Tables(Newtonsoft.Json.Linq.JObject.FromObject(result), "report");
            var rows = tables.Sum(t => t.Value.Count);
            var to = ChatReportService.RecipientFor(scope);
            var pending = new ChatPendingAction
            {
                Id = Guid.NewGuid().ToString("N"), Kind = "EMAIL", Text = name, ScopeFingerprint = scope.Fingerprint, CreatedUtc = DateTime.UtcNow,
                ReportSpecJson = report.ToString(Newtonsoft.Json.Formatting.None),
                Summary = "Email the report '" + name + "' to " + to + " (" + rows + " row(s) as a CSV attachment" +
                          (rows >= ChatDataset<object>.MaxExportRows ? ", truncated at " + ChatDataset<object>.MaxExportRows : "") + ")."
            };
            stage(pending);
            return new { status = "awaiting_user_confirmation", action = "EMAIL", summary = pending.Summary, rows, recipient = to,
                         note = "NOT sent yet. Tell the user to press Confirm in the chat panel. Reports can only be sent to the user's own mailbox." };
        }

        private async Task<ChatActionResult> SendReportAsync(ChatPendingAction pending)
        {
            var spec = Newtonsoft.Json.Linq.JObject.Parse(pending.ReportSpecJson);
            var result = new ChatQueryService(new ChatDataSource(db), scope).ExportDataset(spec);  // fresh data at send time
            var json = Newtonsoft.Json.Linq.JObject.FromObject(result);
            var tables = ChatReportService.Tables(json, "report");
            var filters = json["result"] != null && json["result"]["appliedFilters"] != null ? json["result"]["appliedFilters"].ToString(Newtonsoft.Json.Formatting.None) : "none";
            var to = ChatReportService.RecipientFor(scope);
            await ChatReportService.SendAsync(to, "IVT report: " + pending.Text,
                "<p>Hi,</p><p>Here is the report you requested from the IVT assistant: <b>" + ChatReportService.Html(pending.Text) + "</b>.</p>" +
                "<p>Dataset: " + ChatReportService.Html((string)spec["dataset"]) + "<br/>Filters: " + ChatReportService.Html(filters) +
                "<br/>Reporting week: " + ChatReportService.Html((string)json["reportingWeek"]) + "</p>", tables, scope.Label);
            return new ChatActionResult { Success = true, Message = "The report '" + pending.Text + "' has been emailed to " + to + "." };
        }

        // ---------- execution (after the user confirms) ----------

        public async Task<ChatActionResult> ExecuteAsync(ChatPendingAction pending)
        {
            if (pending.ScopeFingerprint != scope.Fingerprint) throw new ChatAccessException();
            if (pending.Kind == "VETTING") return await CompleteVettingAsync(pending.Text);
            if (pending.Kind == "EMAIL") return await SendReportAsync(pending);
            if (pending.Kind == "BULK_RELEASE") return await ReleaseBatchAsync(pending);
            if (pending.Kind != "HOLD" && pending.Kind != "RELEASE") return Fail("Unsupported action. Nothing was changed.");
            string emailBody, activity;
            using (var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                if (pending.Kind == "HOLD")
                {
                    // Re-validate: the job must still be in the user's current week and not on hold.
                    string problem;
                    var job = ResolveCurrentJob(pending.JobNumber, out problem);
                    if (job == null) return Fail(problem);
                    if (ActiveHolds(job.JobNumber).Any()) return Fail("Connote " + job.ConsignmentNote + " was put on hold by someone else in the meantime. Nothing was changed.");
                    db.ConsignmentForReview.Add(BuildHold(job, pending.Text));
                    db.SaveChanges();
                    tx.Commit();
                    emailBody = "The following connote has been <b>HELD</b>:<br><br>Connote: " + Html(job.ConsignmentNote) + ",  Held By: " + ActingLogin.ToUpperInvariant() +
                                ",  Customer: " + Html(job.Customer) + ",  Comment: " + Html(pending.Text) + " (via IVT assistant)<br>";
                    activity = "Held Details - Connote: " + job.ConsignmentNote + ",  Held By: " + ActingLogin.ToUpperInvariant() + ",  Customer: " + job.Customer + ",  Comment: " + pending.Text;
                }
                else
                {
                    var holds = ScopedActiveHolds().Where(h => h.jno == pending.JobNumber).ToList();
                    if (!holds.Any()) return Fail("Connote " + pending.Connote + " is no longer on hold (it may have been released already). Nothing was changed.");
                    var latest = holds.OrderByDescending(h => h.ActionTime).First();
                    foreach (var h in holds) h.IsDeleted = true;  // also closes duplicate hold rows for the job
                    db.ReleasedConsignment.Add(new ReleasedConsignment
                    {
                        Connote = latest.Connote, jno = latest.jno, Reason = latest.Reason, cons_date = latest.cons_date, Customer = latest.Customer,
                        Carrier = latest.Carrier, Sub_Reason = latest.Sub_Reason, ErrCode = latest.ErrCode, ErrPriority = latest.ErrPriority,
                        ShortErrDesc = latest.ShortErrDesc, ReleasedBy = latest.ReleasedBy, InvoiceWeek = latest.InvoiceWeek, Comment = latest.Comment,
                        Justification = pending.Text, ActionedBy = ActingLogin, ActionTime = DateTime.Now, IsDeleted = false
                    });
                    db.SaveChanges();
                    tx.Commit();
                    emailBody = "The following connote has been <b>RELEASED</b>:<br><br>Connote: " + Html(latest.Connote) + ",  ReleasedBy: " + ActingLogin.ToUpperInvariant() +
                                ",  Customer: " + Html(latest.Customer) + ",  Justification: " + Html(pending.Text) + " (via IVT assistant)<br>";
                    activity = "Released Details - Connote: " + latest.Connote + ",  Released By: " + ActingLogin.ToUpperInvariant() + ",  Customer: " + latest.Customer + ",  Justification: " + pending.Text;
                }
            }
            var notified = await AfterCommitAsync(pending.Kind, emailBody, activity);
            return new ChatActionResult
            {
                Success = true,
                Message = "Connote " + pending.Connote + " has been " + (pending.Kind == "HOLD" ? "held" : "released") +
                          " in IVT. " + (notified ? "IVT admins have been notified." : "The admin email could not be sent; please let the IVT admins know.")
            };
        }

        private async Task<ChatActionResult> ReleaseBatchAsync(ChatPendingAction pending)
        {
            string problem;
            CleanText(pending.Text, "release justification", out problem);
            if (problem != null || pending.HoldIds == null || pending.HoldIds.Count == 0 || pending.HoldIds.Count > 100)
                return Fail("Invalid release batch. Ask Vera to prepare it again.");
            var details = new List<string>();
            using (var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var holds = ScopedActiveHolds().Where(h => pending.HoldIds.Contains(h.Id)).ToList();
                var jobs = holds.Select(h => h.jno).Distinct().ToList();
                if (holds.Count != pending.HoldIds.Count || HoldSnapshot(holds) != pending.HoldSnapshotJson ||
                    ScopedActiveHolds().Count(h => jobs.Contains(h.jno)) != holds.Count)
                    return Fail("The held records changed since this batch was prepared. Nothing was changed. Ask Vera to prepare a fresh batch.");
                foreach (var group in holds.GroupBy(h => h.jno))
                {
                    var h = group.OrderByDescending(x => x.ActionTime).First();
                    db.ReleasedConsignment.Add(new ReleasedConsignment
                    {
                        Connote = h.Connote, jno = h.jno, Reason = h.Reason, cons_date = h.cons_date, Customer = h.Customer,
                        Carrier = h.Carrier, Sub_Reason = h.Sub_Reason, ErrCode = h.ErrCode, ErrPriority = h.ErrPriority,
                        ShortErrDesc = h.ShortErrDesc, ReleasedBy = h.ReleasedBy, InvoiceWeek = h.InvoiceWeek, Comment = h.Comment,
                        Justification = pending.Text, ActionedBy = ActingLogin, ActionTime = DateTime.Now, IsDeleted = false
                    });
                    details.Add("Connote: " + Html(h.Connote) + ", Job: " + Html(h.jno) + ", Customer: " + Html(h.Customer));
                }
                foreach (var h in holds) h.IsDeleted = true;
                db.SaveChanges();
                tx.Commit();
            }
            var detail = string.Join("<br>", details) + "<br>Shared justification: " + Html(pending.Text) + " (via IVT assistant)";
            var notified = await AfterCommitAsync("RELEASE", detail, "Bulk release via IVT assistant: " + string.Join(",", pending.HoldIds) + ". Justification: " + pending.Text);
            return new ChatActionResult { Success = true, Message = details.Count + " job(s) released in IVT. " +
                (notified ? "IVT admins have been notified." : "The admin email could not be sent; please let the IVT admins know.") };
        }

        private static string HoldSnapshot(IEnumerable<ConsignmentForReview> holds)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(holds.OrderBy(h => h.Id).Select(h => new
            {
                h.Id, h.jno, h.Connote, h.Reason, h.Sub_Reason, h.ShortErrDesc, h.Comment, h.ActionTime, h.ActionedBy,
                h.Customer, h.Carrier, h.InvoiceWeek, h.ErrCode, h.ErrPriority, h.ReleasedBy, h.cons_date
            }));
        }

        // Same as IVTController.VettingCompleted: WebIVT.sp_New_AM_Activity + admin notification.
        private async Task<ChatActionResult> CompleteVettingAsync(string message)
        {
            var who = ActingLogin.ToUpperInvariant();
            await db.Database.ExecuteSqlCommandAsync("EXEC [WebIVT].[sp_New_AM_Activity] @Activity, @LoginName, @Comment",
                new System.Data.SqlClient.SqlParameter("@Activity", "Vetting Completed"),
                new System.Data.SqlClient.SqlParameter("@LoginName", who),
                new System.Data.SqlClient.SqlParameter("@Comment", (object)message ?? DBNull.Value));
            var body = "Hi IVT Admins,<br><br>" + who + " has <b>Completed</b> the vetting process.<br><br>" +
                       "<br>Account Manager Comment: " + Html(message ?? "") + " (via IVT assistant).<br><br><br>" +
                       "This is an Automated System Notification.<br><br>Invoice Vetting Tool (IVT2.0) <br> ";
            try
            {
                await EmailNotification.SendNotificationEmailAsync("Vetting Completetion Notification - " + who, body, ActingLogin);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("IVT chat vetting notification failed: {0}", ex.GetType().Name);
                return new ChatActionResult { Success = true, Message = "Vetting completion was recorded, but the admin email could not be sent. Please let the IVT admins know." };
            }
            return new ChatActionResult { Success = true, Message = "Vetting has been marked as completed and the IVT admins have been notified." };
        }

        // Same side effects as the IVT screens; a notification failure never undoes the committed change.
        private async Task<bool> AfterCommitAsync(string kind, string detail, string activity)
        {
            var who = ActingLogin.ToUpperInvariant();
            var body = "Hi IVT Admins,<br><br>" + who + " has <b>" + (kind == "HOLD" ? "Held" : "RELEASED") + "</b> Connotes <br><br>" + detail +
                       "<br><br>Please make sure to reflect the changes on billing system and <b>ALWAYS</b> check  the complete list in IVT2.0 System.<br><br>" +
                       "This is an Automated System Notification.<br><br>Invoice Vetting Tool (IVT2.0) <br> ";
            var notified = false;
            try
            {
                await EmailNotification.SendNotificationEmailAsync("IVT " + (kind == "HOLD" ? "Hold" : "Release") + " Notification - " + who, body, ActingLogin);
                notified = true;
            }
            catch (Exception ex) { Trace.TraceWarning("IVT chat notification failed: {0}", ex.GetType().Name); }
            try
            {
                await EmailNotification.AM_Activity(activity, kind == "HOLD" ? "Held Connote" : "Released Connote", who);
            }
            catch (Exception ex) { Trace.TraceWarning("IVT chat activity failed: {0}", ex.GetType().Name); }
            try
            {
                if (kind == "HOLD") await db.Database.ExecuteSqlCommandAsync("EXEC [WebIVT].[sp_IVTDashboardRefresh_TotalMeasure]");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("IVT chat action follow-up failed: {0}", ex.GetType().Name);
            }
            return notified;
        }

        // ---------- helpers ----------

        private ConsignmentForReview BuildHold(vAllCurrentDetails job, string text)
        {
            var exception = ExceptionFor(job.JobNumber);
            if (exception != null)  // same mapping as IVTController.HoldConsignment (exception screen)
                return new ConsignmentForReview
                {
                    Connote = exception.Connote, jno = exception.jno, Reason = exception.Reason, cons_date = exception.cons_date, Customer = exception.customer,
                    Carrier = exception.carrier, Sub_Reason = exception.Sub_Reason, ErrCode = exception.ErrCode, ErrPriority = exception.ErrPriority,
                    ShortErrDesc = exception.ShortErrDesc, ReleasedBy = exception.HeldBy, InvoiceWeek = exception.CurrentWeek, Comment = text,
                    ActionedBy = ActingLogin, ActionTime = DateTime.Now
                };
            return new ConsignmentForReview  // same mapping as IVTController.HoldTradingConsignment (trading data screen)
            {
                Connote = job.ConsignmentNote, jno = job.JobNumber, Reason = "AM Custom Hold From Trading Data", cons_date = job.Cons_Date.ToString(),
                Customer = job.Customer, Carrier = job.Carrier, Sub_Reason = "AM Custom Hold From Trading Data", ErrCode = "9999", ErrPriority = "P9999",
                ShortErrDesc = "Custom Rule", ReleasedBy = job.AccountManager, InvoiceWeek = job.CurrentWeek, Comment = text,
                ActionedBy = ActingLogin, ActionTime = DateTime.Now
            };
        }

        private vAllCurrentDetails ResolveCurrentJob(string reference, out string problem)
        {
            problem = null;
            var key = Normalize(reference);
            if (key == null) { problem = "Please give the connote or job number."; return null; }
            var matches = scope.Apply(db.vAllCurrentDetails.AsNoTracking(), x => x.LoginName)
                .Where(x => x.ConsignmentNote == key || x.JobNumber == key).Take(20).ToList();
            var jobs = matches.GroupBy(x => x.JobNumber).ToList();
            if (jobs.Count == 0)
            {
                problem = "Connote or job " + key + " was not found in your current-week trading data, so it cannot be held. Check the number, or the consignment may belong to another account manager or week.";
                return null;
            }
            if (jobs.Count > 1)
            {
                problem = "Connote " + key + " matches several jobs: " + string.Join("; ", jobs.Take(5).Select(g => g.Key + " (" + g.First().Customer + " via " + g.First().Carrier + ")")) +
                          ". Ask the user which job number they mean.";
                return null;
            }
            return jobs[0].First();
        }

        private vIVT_Result ExceptionFor(string jobNumber)
        {
            return scope.Apply(db.vIVT_Result.AsNoTracking(), x => x.LoginName)
                .Where(x => x.jno == jobNumber && x.IsDeleted != true).OrderBy(x => x.Id).FirstOrDefault();
        }

        private IQueryable<ConsignmentForReview> ActiveHolds(string jobNumber)
        {
            return db.ConsignmentForReview.Where(h => h.jno == jobNumber && !h.IsDeleted);
        }

        private IQueryable<ConsignmentForReview> ActiveHoldsAnyUser(string jobNumber)
        {
            return db.ConsignmentForReview.AsNoTracking().Where(h => h.jno == jobNumber && !h.IsDeleted);
        }

        // Account managers release holds they actioned (as on the Held Consignments screen); admins any hold.
        private IQueryable<ConsignmentForReview> ScopedActiveHolds()
        {
            return scope.Apply(db.ConsignmentForReview.Where(h => !h.IsDeleted), h => h.ActionedBy);
        }

        private ChatPendingAction Stage(Action<ChatPendingAction> stage, string kind, vAllCurrentDetails job, string text, string summary)
        {
            var pending = new ChatPendingAction
            {
                Id = Guid.NewGuid().ToString("N"), Kind = kind, JobNumber = job.JobNumber, Connote = job.ConsignmentNote, Customer = job.Customer,
                Carrier = job.Carrier, Text = text, ScopeFingerprint = scope.Fingerprint, CreatedUtc = DateTime.UtcNow, Summary = summary
            };
            stage(pending);
            return pending;
        }

        private static string CleanText(string value, string label, out string problem)
        {
            problem = null;
            var text = (value ?? "").Trim();
            if (text.Length < MinTextLength)
                problem = "A " + label + " of at least " + MinTextLength + " characters is required. Ask the user for it; never make one up.";
            else if (text.Length > MaxTextLength)
                problem = "The " + label + " is too long (maximum " + MaxTextLength + " characters).";
            return text;
        }

        private static string Normalize(string value)
        {
            var text = (value ?? "").Trim().ToUpperInvariant();
            return text.Length == 0 || text.Length > 50 ? null : text;
        }

        private static object Rejected(string reason)
        {
            return new { status = "rejected", reason, note = "Nothing was changed. Explain the reason to the user." };
        }

        private static object Ready(ChatPendingAction p)
        {
            return new { status = "awaiting_user_confirmation", action = p.Kind, connote = p.Connote, jobNumber = p.JobNumber, summary = p.Summary,
                         note = "NOT done yet. Tell the user to review the summary and press Confirm in the chat panel. Never say it has been completed." };
        }

        private static ChatActionResult Fail(string message) { return new ChatActionResult { Success = false, Message = message }; }
        private static string Format(DateTime? value) { return value.HasValue ? value.Value.ToString("dd MMM yyyy HH:mm") : "unknown time"; }
        private static string Html(string value) { return System.Web.HttpUtility.HtmlEncode(value ?? ""); }
    }
}
