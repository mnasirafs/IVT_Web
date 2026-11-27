using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    public sealed class IvtHealthItem
    {
        public string Area { get; set; }
        public string Environment { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }   // OK / Warning / Error
        public string Detail { get; set; }
    }

    // Read-only verification of the database contracts, jobs, folders and settings this module depends on.
    public sealed class ContractHealthService
    {
        private readonly IvtAdminRuntime rt;
        public ContractHealthService(IvtAdminRuntime runtime) { rt = runtime; }

        private static string SqlType(SqlDbType t)
        {
            switch (t)
            {
                case SqlDbType.VarChar: return "varchar"; case SqlDbType.NVarChar: return "nvarchar"; case SqlDbType.Int: return "int";
                case SqlDbType.BigInt: return "bigint"; case SqlDbType.Bit: return "bit"; case SqlDbType.Float: return "float";
                case SqlDbType.Money: return "money"; case SqlDbType.Decimal: return "decimal"; case SqlDbType.Date: return "date";
                default: return t.ToString().ToLowerInvariant();
            }
        }

        // Procedure -> parameters this module sends (name, type), per environment.
        public Dictionary<string, List<Tuple<string, string>>> ExpectedContracts(IvtEnvironment environment)
        {
            var map = new Dictionary<string, List<Tuple<string, string>>>(StringComparer.OrdinalIgnoreCase);
            Action<string, IEnumerable<IvtParamSpec>> add = (proc, ps) => map[proc] = ps.Select(x => Tuple.Create(x.Name, SqlType(x.Type))).ToList();
            Func<string, SqlDbType, IvtParamSpec> p = (n, t) => new IvtParamSpec { Name = n, Type = t };
            add("WebIVT.sp_FileloadControl", new[] { p("@InvoiceWeek", SqlDbType.Date), p("@Type", SqlDbType.VarChar) });
            add("WebIVT.sp_Get_IVT_Run_Time", new IvtParamSpec[0]);
            add("WebIVT.Rollback_Invoice_Week_Load", new IvtParamSpec[0]);
            add("WebIVT.Get_CC_EmailContactDetails", new IvtParamSpec[0]);
            add("WebIVT.sp_Update_CC_EmailContactDetails", new[] { p("@CC_Contact", SqlDbType.NVarChar), p("@Exec_Contact", SqlDbType.NVarChar), p("@Admin_Contact", SqlDbType.NVarChar) });
            foreach (var def in ReferenceCatalog.All.Where(d => d.Supports(environment)))
            {
                add(def.ReadProcedure, new[] { p("@searchtxt", SqlDbType.NVarChar) });
                add(def.UpdateProcedure, def.Fields.Where(f => f.Update != null).Select(f => p(f.ParameterName(f.Update, environment), f.Update.Type)));
                add(def.CreateProcedure, def.Fields.Where(f => f.Create != null).Select(f => p(f.ParameterName(f.Create, environment), f.Create.Type)));
            }
            if (environment == IvtEnvironment.Production)
            {
                foreach (var r in QueryReportService.Reports)
                    add(r.Procedure, r.Inputs.Select(i => i == IvtReportInput.Search ? p("@searchtxt", SqlDbType.NVarChar) : i == IvtReportInput.InvoiceWeek ? p("@InvoiceWeek", SqlDbType.Date)
                        : i == IvtReportInput.FromDate ? p("@From_Date", SqlDbType.Date) : i == IvtReportInput.ToDate ? p("@To_Date", SqlDbType.Date)
                        : i == IvtReportInput.Activity ? p("@activity", SqlDbType.NVarChar) : p("@am", SqlDbType.NVarChar)));
                add("WebIVT.sp_getIVTResultDetail", new[] { p("@searchtxt", SqlDbType.NVarChar) });
                var hold = new[] { "@Jno", "@ErrPriority", "@Reason", "@ShortErrDesc", "@Connote", "@cons_date", "@sub_reason", "@Customer", "@Carrier", "@ReleasedBy", "@ActionedBy", "@Comment" }
                    .Select(n => p(n, SqlDbType.VarChar)).Concat(new[] { p("@ErrCode", SqlDbType.BigInt), p("@InvoiceWeek", SqlDbType.Date) }).ToList();
                add("WebIVT.sp_Hold_Connote", hold);
                add("WebIVT.sp_Release_Connote", hold.Concat(new[] { p("@Justification", SqlDbType.VarChar) }));
                add("WebIVT.sp_GetConsignmentLog", new[] { p("@JobNumber", SqlDbType.NVarChar) });
                add(rt.Settings.Comments.InsertProcedure, new[] { p("@JobNumber", SqlDbType.VarChar), p("@LogType", SqlDbType.VarChar), p("@LogText", SqlDbType.VarChar), p("@CreatedBy", SqlDbType.VarChar) });
                add("WebIVT.sp_getAM_Activity_List", new IvtParamSpec[0]);
                add("WebIVT.sp_getAM_List", new IvtParamSpec[0]);
            }
            return map;
        }

        private const string ParametersSql = @"
SELECT pa.name, TYPE_NAME(pa.user_type_id) AS type_name, pa.is_output
  FROM sys.parameters pa WHERE pa.object_id = OBJECT_ID(@proc) ORDER BY pa.parameter_id;
";
        private const string ExistsSql = "SELECT CASE WHEN OBJECT_ID(@proc, N'P') IS NULL THEN 0 ELSE 1 END AS found";
        // Objects referenced by the procedure that do not resolve in this database (e.g. a dropped table).
        private const string MissingRefsSql = @"
SELECT DISTINCT ISNULL(d.referenced_database_name + N'.', N'') + ISNULL(d.referenced_schema_name + N'.', N'') + d.referenced_entity_name AS missing
  FROM sys.sql_expression_dependencies d
 WHERE d.referencing_id = OBJECT_ID(@proc) AND d.referenced_id IS NULL AND d.is_ambiguous = 0
   AND d.referenced_server_name IS NULL
   AND (d.referenced_database_name IS NULL OR d.referenced_database_name = DB_NAME())
   AND LEFT(d.referenced_entity_name, 1) <> N'#'
   AND OBJECT_ID(QUOTENAME(ISNULL(d.referenced_schema_name, N'dbo')) + N'.' + QUOTENAME(d.referenced_entity_name)) IS NULL
   AND TYPE_ID(d.referenced_entity_name) IS NULL";

        public async Task<List<IvtHealthItem>> CheckAsync(CancellationToken cancellation)
        {
            var items = new List<IvtHealthItem>();
            foreach (var missing in rt.Settings.MissingSettings())
                items.Add(new IvtHealthItem { Area = "Settings", Name = missing, Status = "Warning", Detail = "Not configured" });
            foreach (var environment in new[] { IvtEnvironment.Production, IvtEnvironment.Test })
            {
                var target = IvtAdminRuntime.Target(environment);
                foreach (var contract in ExpectedContracts(environment))
                {
                    var item = new IvtHealthItem { Area = "Database operation", Environment = environment.Label(), Name = IvtErrors.DisplayText(contract.Key) };
                    items.Add(item);
                    try
                    {
                        var procParam = new[] { IvtParam.NVarChar("@proc", 300, contract.Key) };
                        var exists = await rt.Database.QueryTextAsync(target, ExistsSql, procParam, cancellation).ConfigureAwait(false);
                        if (Convert.ToInt32(exists.Rows[0][0]) == 0) { item.Status = "Error"; item.Detail = "Database operation unavailable"; continue; }
                        var actual = await rt.Database.QueryTextAsync(target, ParametersSql, procParam, cancellation).ConfigureAwait(false);
                        var actualParams = actual.Rows.Select(r => Tuple.Create(actual.GetString(r, "name"), actual.GetString(r, "type_name"))).ToList();
                        var problems = new List<string>();
                        foreach (var expected in contract.Value)
                        {
                            var match = actualParams.FirstOrDefault(a => string.Equals(a.Item1, expected.Item1, StringComparison.OrdinalIgnoreCase));
                            if (match == null) problems.Add(expected.Item1 + " missing");
                            else if (!string.Equals(match.Item2, expected.Item2, StringComparison.OrdinalIgnoreCase)) problems.Add(expected.Item1 + " is " + match.Item2 + ", module sends " + expected.Item2);
                        }
                        foreach (var extra in actualParams.Where(a => contract.Value.All(e => !string.Equals(e.Item1, a.Item1, StringComparison.OrdinalIgnoreCase))))
                            problems.Add(extra.Item1 + " (" + extra.Item2 + ") exists but is not sent" + (extra.Item1.Equals("@MessageCode", StringComparison.OrdinalIgnoreCase) ? "" : " - relies on its default"));
                        var refs = await rt.Database.QueryTextAsync(target, MissingRefsSql, procParam, cancellation).ConfigureAwait(false);
                        var missingRefs = refs.Rows.Select(r => refs.GetString(r, "missing")).ToList();
                        // Reported as a warning: unresolved names can also be aliases resolved only at run time.
                        if (missingRefs.Count > 0) problems.Add("unresolved reference(s) - table may not exist: " + string.Join(", ", missingRefs));
                        item.Status = problems.Count == 0 ? "OK" : problems.Any(x => x.Contains("missing") || x.Contains("module sends")) ? "Error" : "Warning";
                        item.Detail = problems.Count == 0 ? "Parameters match" : string.Join("; ", problems);
                    }
                    catch (Exception ex) { item.Status = "Error"; item.Detail = IvtErrors.Describe(ex, Guid.NewGuid()); }
                }
            }
            var jobs = rt.Settings.Jobs;
            foreach (var job in new[] { jobs.ProductionEngine, jobs.SilentEngine, jobs.TestEngine, jobs.ReferenceDataLoad, jobs.AdminFileLoad }.Distinct())
            {
                var item = new IvtHealthItem { Area = "SQL Agent job", Name = job };
                items.Add(item);
                try
                {
                    var status = await rt.Agent.GetStatusAsync(job, cancellation).ConfigureAwait(false);
                    item.Status = !status.Exists ? "Error" : !status.Enabled ? "Warning" : "OK";
                    item.Detail = !status.Exists ? "Not found" : (status.Enabled ? "Enabled" : "Disabled") + "; " + status.Summary;
                }
                catch (Exception ex) { item.Status = "Error"; item.Detail = IvtErrors.Describe(ex, Guid.NewGuid()); }
            }
            foreach (IvtLandingFolder folder in Enum.GetValues(typeof(IvtLandingFolder)))
            {
                var item = new IvtHealthItem { Area = "Folder", Name = folder.ToString() };
                items.Add(item);
                try { var files = rt.Landing.List(folder, "*"); item.Status = "OK"; item.Detail = "Accessible; " + files.Count + " file(s)"; }
                catch (Exception ex) { item.Status = folder == IvtLandingFolder.TestRunOutput ? "Warning" : "Error"; item.Detail = ex.Message; }
            }
            var audit = new IvtHealthItem { Area = "Audit", Name = "IvtAdmin.AuditLog" };
            items.Add(audit);
            try { await rt.Audit.RecentAsync(null, 1, cancellation).ConfigureAwait(false); audit.Status = "OK"; audit.Detail = "Readable"; }
            catch (Exception ex) { audit.Status = "Error"; audit.Detail = "Audit storage unavailable - mutations are refused: " + IvtErrors.Describe(ex, Guid.NewGuid()); }
            var azure = rt.Azure.ConfigurationProblems();
            items.Add(new IvtHealthItem { Area = "Azure", Name = "Optional Azure execution", Status = rt.Settings.Azure.Enabled ? (azure.Count == 0 ? "OK" : "Error") : "OK",
                Detail = rt.Settings.Azure.Enabled ? (azure.Count == 0 ? "Configured" : string.Join("; ", azure)) : "Disabled (default)" });
            foreach (var item in items) item.Detail = IvtErrors.DisplayText(item.Detail);
            return items;
        }
    }
}
