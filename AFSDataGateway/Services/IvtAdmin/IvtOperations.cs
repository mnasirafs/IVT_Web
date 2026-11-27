using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    // The authenticated operator, resolved on the server from the Windows/Entra identity and IVT user table.
    public sealed class IvtOperator
    {
        public string Login { get; set; }
        public bool IsAdmin { get; set; }
        public bool CanRead { get; set; }
    }

    public sealed class IvtStep
    {
        public string Name { get; set; }
        public string Status { get; set; }   // Done / Failed / Skipped / Submitted / Not attempted / Ambiguous
        public string Message { get; set; }
    }

    public sealed class IvtActionResult
    {
        public bool Ok { get; set; }
        public string Outcome { get; set; }
        public string Message { get; set; }
        public List<IvtStep> Steps { get; set; } = new List<IvtStep>();
        public Guid CorrelationId { get; set; }
        public long? AuditId { get; set; }
        public string Warning { get; set; }
        public object Data { get; set; }

        public static IvtActionResult Rejected(string message, Guid correlation)
        {
            return new IvtActionResult { Ok = false, Outcome = IvtOutcome.Rejected, Message = message, CorrelationId = correlation };
        }
    }

    // Composition root: real adapters for production use; tests construct services with fakes.
    public sealed class IvtAdminRuntime
    {
        public IvtAdminSettings Settings { get; set; }
        public IIvtDatabase Database { get; set; }
        public ISqlAgent Agent { get; set; }
        public IIvtAudit Audit { get; set; }
        public IIvtCoordinator Coordinator { get; set; }
        public ILandingStore Landing { get; set; }
        public ILegacyReportApi ReportApi { get; set; }
        public IProcessRunner Processes { get; set; }
        public INotificationTransport Mail { get; set; }
        public IAzureIvt Azure { get; set; }
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
        public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (t, c) => Task.Delay(t, c);

        public static IvtAdminRuntime Create()
        {
            var settings = IvtAdminSettings.Load();
            return new IvtAdminRuntime
            {
                Settings = settings, Database = new SqlIvtDatabase(settings), Agent = new SqlAgentClient(settings),
                Audit = new SqlIvtAudit(settings), Coordinator = new SqlIvtCoordinator(settings), Landing = new FileLandingStore(settings),
                ReportApi = new LegacyReportApi(settings), Processes = new ProcessRunner(), Mail = new GraphNotificationTransport(),
                Azure = new AzureIvtClient(settings)
            };
        }

        public DateTime BusinessToday() { return TimeZoneInfo.ConvertTimeFromUtc(UtcNow(), Settings.TimeZone()).Date; }
        public DateTime BusinessNow() { return TimeZoneInfo.ConvertTimeFromUtc(UtcNow(), Settings.TimeZone()); }

        public static IvtDbTarget Target(IvtEnvironment environment) { return environment == IvtEnvironment.Test ? IvtDbTarget.Test : IvtDbTarget.Production; }
    }

    // Writes STARTED before a mutation and the final outcome afterwards. If the first write fails nothing is changed.
    // If the final write fails the caller still reports the real outcome (never "not started").
    public sealed class AuditedOperation
    {
        private readonly IIvtAudit audit;
        public IvtAuditEntry Entry { get; private set; }
        private AuditedOperation(IIvtAudit audit, IvtAuditEntry entry) { this.audit = audit; Entry = entry; }

        public static async Task<AuditedOperation> BeginAsync(IIvtAudit audit, IvtAuditEntry entry, CancellationToken cancellation)
        {
            entry.Outcome = IvtOutcome.Started;
            try { await audit.WriteAsync(entry, cancellation).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Trace.TraceError("IVT admin audit write failed ({0}) for {1}: {2}", entry.CorrelationId, entry.Operation, ex.GetType().Name);
                throw new IvtOperationException("The audit log could not be written, so nothing was changed. Try again later or check the audit connection. Reference: " + entry.CorrelationId);
            }
            return new AuditedOperation(audit, entry);
        }

        // Returns a warning to show the operator when the outcome could not be recorded.
        public async Task<string> CompleteAsync(string outcome, int? affected, string externalId, object detail, CancellationToken cancellation)
        {
            try
            {
                await audit.UpdateAsync(Entry.Id, outcome, affected, externalId, detail == null ? null : JsonConvert.SerializeObject(detail), CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                Trace.TraceError("IVT admin audit completion failed ({0}) audit {1}: {2}", Entry.CorrelationId, Entry.Id, ex.GetType().Name);
                return "The outcome above is accurate, but it could not be recorded in the audit log (audit #" + Entry.Id + " still shows STARTED). Reference: " + Entry.CorrelationId;
            }
        }

        // Rejections are recorded on a best-effort basis; nothing was changed either way.
        public static async Task RecordRejectionAsync(IIvtAudit audit, IvtAuditEntry entry, string reason)
        {
            entry.Outcome = IvtOutcome.Rejected;
            entry.Detail = JsonConvert.SerializeObject(new { reason });
            try { await audit.WriteAsync(entry, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { Trace.TraceWarning("IVT admin rejection not audited ({0}): {1}", entry.CorrelationId, ex.GetType().Name); }
        }
    }

    public static class IvtErrors
    {
        // Presentation only: retain technical identifiers in database calls and persisted audit records.
        public static string DisplayText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var entity in ReferenceCatalog.All)
            {
                text = ReplaceOperation(text, entity.ReadProcedure, entity.Title + " lookup");
                text = ReplaceOperation(text, entity.UpdateProcedure, entity.Title + " update");
                text = ReplaceOperation(text, entity.CreateProcedure, entity.Title + " creation");
            }
            foreach (var report in QueryReportService.Reports)
                text = ReplaceOperation(text, report.Procedure, report.Title);
            text = ReplaceOperation(text, "WebIVT.sp_FileloadControl", "Invoice-week preparation");
            text = ReplaceOperation(text, "WebIVT.sp_Get_IVT_Run_Time", "Run history");
            text = ReplaceOperation(text, "WebIVT.Rollback_Invoice_Week_Load", "Invoice-week rollback");
            text = ReplaceOperation(text, "WebIVT.Get_CC_EmailContactDetails", "Shared contacts lookup");
            text = ReplaceOperation(text, "WebIVT.sp_Update_CC_EmailContactDetails", "Shared contacts update");
            return System.Text.RegularExpressions.Regex.Replace(text,
                @"\b(?:(?:WebIVT|dbo|msdb\.dbo)\.)?sp_[A-Za-z0-9_]+\b", "Database operation",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string ReplaceOperation(string text, string name, string label)
        {
            if (string.IsNullOrWhiteSpace(name)) return text;
            var unqualified = name.Substring(name.LastIndexOf('.') + 1);
            return System.Text.RegularExpressions.Regex.Replace(text,
                @"(?<![\w.])(?:WebIVT\.)?" + System.Text.RegularExpressions.Regex.Escape(unqualified) + @"\b",
                match => label, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // Message safe to show for an unexpected failure, with a reference that appears in the server log.
        public static string Describe(Exception ex, Guid correlation)
        {
            if (ex is IvtOperationException || ex is IvtConfigurationException || ex is IvtContractException) return DisplayText(ex.Message);
            var sql = ex as System.Data.SqlClient.SqlException;
            Trace.TraceError("IVT admin failure {0}: {1}: {2}", correlation, ex.GetType().Name, ex.Message);
            if (sql != null)
            {
                if (sql.Number == 229 || sql.Number == 230 || sql.Number == 297 || sql.Number == 300)
                    return "The database refused the request (permission denied, SQL error " + sql.Number + "). Ask a DBA to grant the required permission. Reference: " + correlation;
                if (sql.Number == 2812) return "A required database operation is unavailable in the selected environment (SQL error 2812). Reference: " + correlation;
                if (sql.Number == 208) return "Required reference data is unavailable in the selected environment (SQL error 208). Reference: " + correlation;
                if (sql.Number == -2) return "The database did not respond in time (timeout). The operation may still complete on the server; refresh before retrying. Reference: " + correlation;
                return "Database error " + sql.Number + ". Ask your administrator to check the server log. Reference: " + correlation;
            }
            if (ex is TaskCanceledException || ex is OperationCanceledException) return "The operation timed out. Refresh to check its state before retrying. Reference: " + correlation;
            return "Unexpected error (" + ex.GetType().Name + "). Reference: " + correlation;
        }
    }
}
