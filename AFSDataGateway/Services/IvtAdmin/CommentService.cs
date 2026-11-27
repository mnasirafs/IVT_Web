using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    // Consignment log comments (production IVT only: sp_GetConsignmentLog does not exist in IVT_Test).
    // Saving the comment and notifying are separate outcomes; a notification retry never inserts the comment again.
    public sealed class CommentService
    {
        private const string ContactsSql = "SELECT ADMIN_Contact, Finance_Contact FROM WebIVT.IVT_Run_Control";
        private readonly IvtAdminRuntime rt;
        public CommentService(IvtAdminRuntime runtime) { rt = runtime; }

        public static string ValidateJobNumber(string jobNumber)
        {
            var job = (jobNumber ?? "").Trim();
            if (job.Length == 0 || job.Length > 50) throw new IvtOperationException("Enter a job number (up to 50 characters).");
            return job;
        }

        public async Task<IvtTable> LogAsync(string jobNumber, CancellationToken cancellation)
        {
            var table = await rt.Database.QueryProcedureAsync(IvtDbTarget.Production, "WebIVT.sp_GetConsignmentLog",
                new[] { IvtParam.NVarChar("@JobNumber", 1000, ValidateJobNumber(jobNumber)) }, cancellation).ConfigureAwait(false);
            table.RequireColumns("sp_GetConsignmentLog", new[] { "JobNumber", "LogType", "LogText", "CreatedBy", "Connote", "Customer" });
            return table;
        }

        public string OperatorEmail(string login)
        {
            string mapped;
            if (rt.Settings.Comments.EmailMap.TryGetValue(login, out mapped) && IvtEmail.IsValid(mapped)) return mapped;
            if (!string.IsNullOrWhiteSpace(rt.Settings.Comments.EmailDomain)) return login.ToLowerInvariant() + "@" + rt.Settings.Comments.EmailDomain.Trim().TrimStart('@');
            return null;
        }

        public async Task<IvtActionResult> AddAsync(string jobNumber, string text, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            string job;
            try { job = ValidateJobNumber(jobNumber); } catch (IvtOperationException ex) { return IvtActionResult.Rejected(ex.Message, correlation); }
            var comment = (text ?? "").Trim();
            if (comment.Length == 0) return IvtActionResult.Rejected("Enter a comment before saving.", correlation);
            if (comment.Length > 8000) return IvtActionResult.Rejected("The comment is too long (max 8000 characters).", correlation);
            if (op.Login.Length > 50) return IvtActionResult.Rejected("Your login is longer than the 50-character CreatedBy field.", correlation);
            var logType = rt.Settings.Comments.LogType;

            var audited = await AuditedOperation.BeginAsync(rt.Audit, new IvtAuditEntry
            {
                CorrelationId = correlation, Operator = op.Login, Environment = "production", Operation = "COMMENT.ADD", Target = "Job " + job, AffectedCount = 1,
                Detail = JsonConvert.SerializeObject(new { jobNumber = job, logType, text = comment })
            }, cancellation).ConfigureAwait(false);
            var result = new IvtActionResult { CorrelationId = correlation, AuditId = audited.Entry.Id };
            try
            {
                await rt.Database.QueryProcedureAsync(IvtDbTarget.Production, rt.Settings.Comments.InsertProcedure, new[]
                {
                    IvtParam.VarChar("@JobNumber", 50, job), IvtParam.VarChar("@LogType", 50, logType),
                    IvtParam.VarChar("@LogText", -1, comment), IvtParam.VarChar("@CreatedBy", 50, op.Login)
                }, cancellation).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Outcome = IvtOutcome.Failed;
                result.Message = "The comment was NOT saved: " + IvtErrors.Describe(ex, correlation);
                result.Warning = await audited.CompleteAsync(IvtOutcome.Failed, 0, null, new { jobNumber = job, logType, text = comment, error = result.Message }, cancellation).ConfigureAwait(false);
                return result;
            }
            result.Steps.Add(new IvtStep { Name = "Save comment", Status = "Done" });
            var notify = await NotifyAsync(job, logType, comment, op.Login, correlation, cancellation).ConfigureAwait(false);
            result.Steps.Add(notify);
            var notified = notify.Status == "Done";
            result.Ok = true;
            result.Outcome = notified ? IvtOutcome.Succeeded : IvtOutcome.SavedNotifyFailed;
            result.Message = notified ? "Comment saved and notification sent." : "Comment saved; the notification was NOT sent (" + notify.Message + "). Use 'Retry notification' - the comment will not be duplicated.";
            result.Warning = await audited.CompleteAsync(result.Outcome, 1, null, new { jobNumber = job, logType, text = comment, notification = notify }, cancellation).ConfigureAwait(false);
            return result;
        }

        public async Task<IvtActionResult> RetryNotificationAsync(long auditId, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            using (var lease = await rt.Coordinator.TryAcquireAsync("comment-notify:" + auditId, cancellation).ConfigureAwait(false))
            {
                if (lease == null) return IvtActionResult.Rejected("A retry for this comment is already in progress.", correlation);
                return await RetryLockedAsync(auditId, op, correlation, cancellation).ConfigureAwait(false);
            }
        }

        private async Task<IvtActionResult> RetryLockedAsync(long auditId, IvtOperator op, Guid correlation, CancellationToken cancellation)
        {
            var entry = await rt.Audit.GetAsync(auditId, cancellation).ConfigureAwait(false);
            if (entry == null || entry.Operation != "COMMENT.ADD" || entry.Outcome != IvtOutcome.SavedNotifyFailed)
                return IvtActionResult.Rejected("There is no saved comment awaiting notification with that reference.", correlation);
            var detail = JObject.Parse(entry.Detail ?? "{}");
            var job = (string)detail["jobNumber"]; var logType = (string)detail["logType"]; var text = (string)detail["text"];
            if (job == null || text == null) return IvtActionResult.Rejected("The audit record does not contain the comment details.", correlation);
            var step = await NotifyAsync(job, logType, text, entry.Operator, correlation, cancellation).ConfigureAwait(false);
            var result = new IvtActionResult { CorrelationId = correlation, AuditId = auditId, Steps = { step } };
            if (step.Status == "Done")
            {
                result.Ok = true; result.Outcome = IvtOutcome.Succeeded; result.Message = "Notification sent.";
                detail["notification"] = JObject.FromObject(step);
                detail["retriedBy"] = op.Login;
                result.Warning = await new AuditCompletion(rt.Audit).CompleteAsync(auditId, IvtOutcome.Succeeded, detail.ToString(Formatting.None));
            }
            else { result.Outcome = IvtOutcome.Failed; result.Message = "The notification still could not be sent: " + step.Message; }
            return result;
        }

        private async Task<IvtStep> NotifyAsync(string job, string logType, string comment, string login, Guid correlation, CancellationToken cancellation)
        {
            try
            {
                var contacts = await rt.Database.QueryTextAsync(IvtDbTarget.Production, ContactsSql, new IvtParam[0], cancellation).ConfigureAwait(false);
                var row = contacts.Rows.FirstOrDefault();
                var to = IvtEmail.SplitAddresses(row == null ? null : contacts.GetString(row, "ADMIN_Contact"));
                var cc = IvtEmail.SplitAddresses(row == null ? null : contacts.GetString(row, "Finance_Contact"));
                var mine = OperatorEmail(login);
                if (mine != null && !cc.Contains(mine, StringComparer.OrdinalIgnoreCase)) cc.Add(mine);
                if (to.Count == 0) return new IvtStep { Name = "Send notification", Status = "Failed", Message = "IVT_Run_Control.ADMIN_Contact is empty" };
                var log = await LogAsync(job, cancellation).ConfigureAwait(false);
                var first = log.Rows.FirstOrDefault();
                var connote = first == null ? "" : log.GetString(first, "Connote");
                var customer = first == null ? "" : log.GetString(first, "Customer");
                var html = Body(job, connote, customer, logType, comment, login);
                await rt.Mail.SendAsync(to, cc, "IVT Log Notification - " + login.ToUpperInvariant(), html, cancellation).ConfigureAwait(false);
                return new IvtStep { Name = "Send notification", Status = "Done", Message = "To " + to.Count + " admin contact(s), CC " + cc.Count };
            }
            catch (Exception ex)
            {
                return new IvtStep { Name = "Send notification", Status = "Failed", Message = IvtErrors.Describe(ex, correlation) };
            }
        }

        public static string Body(string job, string connote, string customer, string logType, string comment, string login)
        {
            Func<string, string> e = WebUtility.HtmlEncode;
            const string cell = "border:1px solid #dddddd;text-align:left;padding:8px;";
            return "Hi Team,<br><br>" + e(login.ToUpperInvariant()) + " has added a <b>" + e(logType) + "</b> log for the following consignment:<br><br>" +
                   "<table style='border-collapse:collapse;width:100%;'><tr style='background-color:#f2f2f2;'>" +
                   string.Join("", new[] { "Job Number", "Connote", "Customer", "Log Type", "Comment" }.Select(h => "<th style='" + cell + "'>" + h + "</th>")) + "</tr><tr>" +
                   string.Join("", new[] { job, connote, customer, logType, comment }.Select(v => "<td style='" + cell + "'>" + e(v ?? "").Replace("\n", "<br>") + "</td>")) +
                   "</tr></table><br><br>Please check the complete details in IVT2.0 System.<br><br>This is an Automated System Notification.<br><br>Invoice Vetting Tool (IVT2.0)<br>";
        }

        private sealed class AuditCompletion
        {
            private readonly IIvtAudit audit;
            public AuditCompletion(IIvtAudit audit) { this.audit = audit; }
            public async Task<string> CompleteAsync(long id, string outcome, string detail)
            {
                try { await audit.UpdateAsync(id, outcome, null, null, detail, CancellationToken.None).ConfigureAwait(false); return null; }
                catch (Exception) { return "The notification was sent but the audit record could not be updated (audit #" + id + ")."; }
            }
        }
    }
}
