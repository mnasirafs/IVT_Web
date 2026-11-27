using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    // Manual hold/release of queried connotes through WebIVT.sp_Hold_Connote / sp_Release_Connote.
    // Separate from file-based bulk hold/release (FileLoadService), which runs the SQL Agent loader.
    public sealed class ConnoteActionService
    {
        public const string HoldAllSubReason = "Custom Hold By IVT Admin";
        public const string ReleaseSingleSubReason = "Custom Release by IVT Admin";
        public const string ReleaseAllSubReason = "Custom Release By IVT Admin";
        public const int MaxRows = 2000;

        // Hold candidates: current IVT results (desktop hold screen source).
        public static readonly string[] HoldColumns = { "Id", "jno", "Connote", "Reason", "cons_date", "customer", "carrier", "Sub_Reason", "ErrCode", "ErrPriority", "ShortErrDesc", "HeldBy", "CurrentWeek" };
        // Release candidates come from dbo.vConsignmentForReview, the view IVT_GetConnoteFor_Review reads. That procedure's verified
        // result set lacks ErrCode/ErrPriority/ShortErrDesc/HeldBy/sub_reason/CurrentWeek, which sp_Release_Connote needs.
        public static readonly string[] ReleaseColumns = { "Id", "jno", "Connote", "Reason", "cons_date", "Customer", "Carrier", "Sub_Reason", "ErrCode", "ErrPriority", "ShortErrDesc", "HeldBy", "CurrentWeek", "Comment" };

        private const string ReleaseCandidatesSql = @"
SELECT TOP (@max) r.Id, r.jno, r.Connote, r.Reason, r.cons_date, r.Customer, r.Carrier, r.Sub_Reason, r.ErrCode, r.ErrPriority, r.ShortErrDesc,
       r.HeldBy, r.CurrentWeek, r.Comment, r.ActionedBy, r.ActionTime
  FROM dbo.vConsignmentForReview r
 WHERE r.IsDeleted = 0
   AND (@search = N'' OR r.jno LIKE @like OR r.Connote LIKE @like OR r.Customer LIKE @like OR r.Carrier LIKE @like OR r.ActionedBy LIKE @like OR r.ShortErrDesc LIKE @like)
 ORDER BY r.ActionTime DESC, r.Id DESC";

        private readonly IvtAdminRuntime rt;
        public ConnoteActionService(IvtAdminRuntime runtime) { rt = runtime; }

        public async Task<IvtTable> HoldCandidatesAsync(string search, CancellationToken cancellation)
        {
            var table = await rt.Database.QueryProcedureAsync(IvtDbTarget.Production, "WebIVT.sp_getIVTResultDetail",
                new[] { IvtParam.NVarChar("@searchtxt", 1000, (search ?? "").Trim()) }, cancellation).ConfigureAwait(false);
            table.RequireColumns("sp_getIVTResultDetail", HoldColumns);
            return table;
        }

        public async Task<IvtTable> ReleaseCandidatesAsync(string search, CancellationToken cancellation)
        {
            var s = (search ?? "").Trim();
            var like = "%" + s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
            var table = await rt.Database.QueryTextAsync(IvtDbTarget.Production, ReleaseCandidatesSql, new[]
            {
                new IvtParam("@max", System.Data.SqlDbType.Int, MaxRows + 1), IvtParam.NVarChar("@search", 500, s), IvtParam.NVarChar("@like", 1100, like)
            }, cancellation).ConfigureAwait(false);
            table.RequireColumns("vConsignmentForReview", ReleaseColumns);
            return table;
        }

        public sealed class ActionRequest
        {
            public string Search { get; set; }
            public bool All { get; set; }
            public List<long> Ids { get; set; } = new List<long>();
            public int ExpectedCount { get; set; }
            public string Text { get; set; }   // hold comment or release justification
        }

        public Task<IvtActionResult> HoldAsync(ActionRequest request, IvtOperator op, CancellationToken cancellation)
        {
            return ApplyAsync(true, request, op, cancellation);
        }

        public Task<IvtActionResult> ReleaseAsync(ActionRequest request, IvtOperator op, CancellationToken cancellation)
        {
            return ApplyAsync(false, request, op, cancellation);
        }

        private async Task<IvtActionResult> ApplyAsync(bool hold, ActionRequest request, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            var verb = hold ? "hold" : "release";
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            var text = (request.Text ?? "").Trim();
            if (hold && request.All && text.Length == 0) return IvtActionResult.Rejected("Enter the hold comment that will be recorded on every connote.", correlation);
            if (!hold && text.Length == 0) return IvtActionResult.Rejected("Enter the release justification.", correlation);
            if (text.Length > (hold ? 500 : 5000)) return IvtActionResult.Rejected("The " + (hold ? "comment" : "justification") + " is too long (max " + (hold ? 500 : 5000) + " characters).", correlation);
            if (!request.All && request.Ids.Count != 1) return IvtActionResult.Rejected("Choose one connote, or use the 'all displayed' action.", correlation);
            if (op.Login.Length > 50) return IvtActionResult.Rejected("Your login is longer than the 50-character ActionedBy field.", correlation);

            // Re-resolve the targets from the database with the same search the operator used.
            var table = hold ? await HoldCandidatesAsync(request.Search, cancellation).ConfigureAwait(false)
                             : await ReleaseCandidatesAsync(request.Search, cancellation).ConfigureAwait(false);
            var byId = new Dictionary<long, object[]>();
            foreach (var row in table.Rows) byId[Convert.ToInt64(table.Get(row, "Id"), CultureInfo.InvariantCulture)] = row;
            List<object[]> targets;
            if (request.All)
            {
                if (table.Rows.Count > MaxRows) return IvtActionResult.Rejected("More than " + MaxRows + " connotes match; narrow the search.", correlation);
                if (table.Rows.Count != request.ExpectedCount || request.Ids.Count != request.ExpectedCount || request.Ids.Any(id => !byId.ContainsKey(id)))
                    return IvtActionResult.Rejected("The displayed result changed (" + request.ExpectedCount + " shown, " + table.Rows.Count + " now). Refresh and confirm again. Nothing was changed.", correlation);
                targets = table.Rows;
            }
            else
            {
                object[] one;
                if (!byId.TryGetValue(request.Ids[0], out one)) return IvtActionResult.Rejected("That connote is no longer in the result for this search. Refresh. Nothing was changed.", correlation);
                targets = new List<object[]> { one };
            }
            if (targets.Count == 0) return IvtActionResult.Rejected("There are no connotes to " + verb + ".", correlation);

            var audited = await AuditedOperation.BeginAsync(rt.Audit, new IvtAuditEntry
            {
                CorrelationId = correlation, Operator = op.Login, Environment = "production", Operation = hold ? (request.All ? "CONNOTE.HOLD_ALL" : "CONNOTE.HOLD") : (request.All ? "CONNOTE.RELEASE_ALL" : "CONNOTE.RELEASE"),
                Target = string.Join(",", targets.Take(30).Select(r => table.GetString(r, "Connote"))), AffectedCount = targets.Count
            }, cancellation).ConfigureAwait(false);

            var outcomes = new List<object>();
            int ok = 0, failed = 0, notAttempted = 0;
            foreach (var row in targets)
            {
                var connote = table.GetString(row, "Connote");
                if (cancellation.IsCancellationRequested) { notAttempted++; outcomes.Add(new { connote, status = "Not attempted" }); continue; }
                string problem;
                var parameters = hold ? HoldParameters(table, row, request.All, text, op.Login, out problem) : ReleaseParameters(table, row, request.All, text, op.Login, out problem);
                if (problem != null) { failed++; outcomes.Add(new { connote, status = "Failed", message = problem }); continue; }
                try
                {
                    await rt.Database.ExecuteProcedureAsync(IvtDbTarget.Production, hold ? "WebIVT.sp_Hold_Connote" : "WebIVT.sp_Release_Connote", parameters, CancellationToken.None).ConfigureAwait(false);
                    ok++; outcomes.Add(new { connote, jno = table.GetString(row, "jno"), status = hold ? "Held" : "Released" });
                }
                catch (Exception ex) { failed++; outcomes.Add(new { connote, status = "Failed", message = IvtErrors.Describe(ex, correlation) }); }
            }
            var outcome = failed == 0 && notAttempted == 0 ? IvtOutcome.Succeeded : ok == 0 ? IvtOutcome.Failed : IvtOutcome.Partial;
            var result = new IvtActionResult
            {
                Ok = outcome == IvtOutcome.Succeeded, Outcome = outcome, CorrelationId = correlation, AuditId = audited.Entry.Id, Data = outcomes,
                Message = outcome == IvtOutcome.Succeeded ? ok + " connote(s) " + (hold ? "held" : "released") + "."
                        : ok + " " + (hold ? "held" : "released") + ", " + failed + " failed" + (notAttempted > 0 ? ", " + notAttempted + " not attempted (cancelled)" : "") + ". See the per-connote results."
            };
            result.Warning = await audited.CompleteAsync(outcome, ok, null, new { text, rows = outcomes }, cancellation).ConfigureAwait(false);
            return result;
        }

        private static long? ErrCode(IvtTable table, object[] row, out string problem)
        {
            problem = null;
            var raw = table.GetString(row, "ErrCode");
            long code;
            if (raw == null || !long.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out code)) { problem = "ErrCode '" + raw + "' is not a whole number."; return null; }
            return code;
        }

        private static DateTime? Week(IvtTable table, object[] row, out string problem)
        {
            problem = null;
            var value = table.Get(row, "CurrentWeek");
            if (!(value is DateTime)) { problem = "The row has no CurrentWeek (invoice week)."; return null; }
            return ((DateTime)value).Date;
        }

        // Legacy mapping: @ReleasedBy = row HeldBy, @ActionedBy = operator, @InvoiceWeek = row CurrentWeek.
        private static List<IvtParam> Common(IvtTable table, object[] row, string subReason, string comment, string login, out string problem)
        {
            string p1, p2;
            var code = ErrCode(table, row, out p1);
            var week = Week(table, row, out p2);
            problem = p1 ?? p2;
            if (problem != null) return null;
            return new List<IvtParam>
            {
                IvtParam.VarChar("@Jno", 200, table.GetString(row, "jno")),
                IvtParam.VarChar("@ErrPriority", 50, table.GetString(row, "ErrPriority")),
                new IvtParam("@ErrCode", System.Data.SqlDbType.BigInt, code.Value),
                IvtParam.VarChar("@Reason", 1000, table.GetString(row, "Reason")),
                IvtParam.VarChar("@ShortErrDesc", 200, table.GetString(row, "ShortErrDesc")),
                IvtParam.VarChar("@Connote", 200, table.GetString(row, "Connote")),
                IvtParam.VarChar("@cons_date", 20, table.GetString(row, "cons_date")),
                IvtParam.VarChar("@sub_reason", 500, subReason),
                IvtParam.VarChar("@Customer", 500, table.GetString(row, "Customer")),
                IvtParam.VarChar("@Carrier", 500, table.GetString(row, "Carrier")),
                IvtParam.VarChar("@ReleasedBy", 50, table.GetString(row, "HeldBy")),
                IvtParam.VarChar("@ActionedBy", 50, login),
                IvtParam.Date("@InvoiceWeek", week),
                IvtParam.VarChar("@Comment", 5000, comment),
            };
        }

        public static List<IvtParam> HoldParameters(IvtTable table, object[] row, bool all, string text, string login, out string problem)
        {
            // Single hold keeps the row's sub-reason; hold-all uses the admin sub-reason and the entered comment.
            // sp_getIVTResultDetail returns no Comment column (verified), so a single hold records the operator's optional comment.
            var list = Common(table, row, all ? HoldAllSubReason : table.GetString(row, "Sub_Reason"), text.Length == 0 ? null : text, login, out problem);
            if (list == null) return null;
            var comment = list.First(p => p.Name == "@Comment");
            list[list.IndexOf(comment)] = IvtParam.VarChar("@Comment", 500, comment.Value as string);
            return list;
        }

        public static List<IvtParam> ReleaseParameters(IvtTable table, object[] row, bool all, string justification, string login, out string problem)
        {
            var list = Common(table, row, all ? ReleaseAllSubReason : ReleaseSingleSubReason, table.GetString(row, "Comment"), login, out problem);
            if (list == null) return null;
            list.Add(IvtParam.VarChar("@Justification", 5000, justification));
            return list;
        }
    }
}
