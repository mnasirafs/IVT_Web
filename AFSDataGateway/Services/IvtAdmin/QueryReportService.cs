using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    public enum IvtReportInput { Search, InvoiceWeek, FromDate, ToDate, Activity, AccountManager }

    public sealed class IvtReportDef
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Group { get; set; }
        public string Description { get; set; }
        public string Procedure { get; set; }
        public IvtReportInput[] Inputs { get; set; }
        // Verified: these procedures treat @searchtxt = 'ALL' as "everything"; blank input is sent as 'ALL'.
        public bool SearchAllKeyword { get; set; }
        public string LegacyExportFolder { get; set; }
    }

    public sealed class IvtReportFilters
    {
        public string Search { get; set; }
        public DateTime? InvoiceWeek { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string Activity { get; set; }
        public string AccountManager { get; set; }
    }

    public sealed class QueryReportService
    {
        private static readonly IvtReportInput[] SearchWeekDates = { IvtReportInput.Search, IvtReportInput.InvoiceWeek, IvtReportInput.FromDate, IvtReportInput.ToDate };

        public static readonly IReadOnlyList<IvtReportDef> Reports = new List<IvtReportDef>
        {
            new IvtReportDef { Key = "review", Title = "Connotes for review", Group = "Connotes", Procedure = "WebIVT.IVT_GetConnoteFor_Review", SearchAllKeyword = true,
                Inputs = new[] { IvtReportInput.Search, IvtReportInput.FromDate, IvtReportInput.ToDate }, LegacyExportFolder = "Connote_For_Review",
                Description = "Connotes currently held for review, across all invoice weeks." },
            new IvtReportDef { Key = "accepted", Title = "Accepted connotes", Group = "Connotes", Procedure = "WebIVT.IVT_Get_Accepted_Connote", SearchAllKeyword = true,
                Inputs = new[] { IvtReportInput.Search, IvtReportInput.InvoiceWeek }, LegacyExportFolder = "Accepted_Connotes",
                Description = "Connotes accepted for invoicing in an invoice week." },
            new IvtReportDef { Key = "released", Title = "Released connote history", Group = "Connotes", Procedure = "WebIVT.IVT_Get_Released_Connote", SearchAllKeyword = true,
                Inputs = SearchWeekDates, LegacyExportFolder = "Released_Connotes", Description = "Released connotes with justification." },
            new IvtReportDef { Key = "held-not-in-bms", Title = "Held jobs not in BMS", Group = "BMS reconciliation", Procedure = "WebIVT.IVT_Get_Held_Not_IN_BMS", SearchAllKeyword = true,
                Inputs = SearchWeekDates, LegacyExportFolder = "IVT_BMS_Check", Description = "Jobs held in IVT that are not present in the last BMS file load." },
            new IvtReportDef { Key = "released-not-in-bms", Title = "Released jobs not in BMS", Group = "BMS reconciliation", Procedure = "WebIVT.IVT_Get_Release_Not_IN_BMS", SearchAllKeyword = true,
                Inputs = SearchWeekDates, LegacyExportFolder = "IVT_BMS_Check", Description = "Jobs released in IVT that are not present in the last BMS file load." },
            new IvtReportDef { Key = "held-not-in-ivt", Title = "Held jobs not in IVT", Group = "BMS reconciliation", Procedure = "WebIVT.IVT_Get_Hold_Not_IN_IVT",
                Inputs = new IvtReportInput[0], LegacyExportFolder = "IVT_BMS_Check", Description = "Jobs in the last BMS file load that are not held in IVT. No filters apply to this report." },
            new IvtReportDef { Key = "released-not-in-ivt", Title = "Released jobs not in IVT", Group = "BMS reconciliation", Procedure = "WebIVT.IVT_Get_Release_Not_IN_IVT",
                Inputs = new IvtReportInput[0], LegacyExportFolder = "IVT_BMS_Check", Description = "Jobs in the last BMS file load that are not released in IVT. No filters apply to this report." },
            new IvtReportDef { Key = "am-activity", Title = "Account manager activity", Group = "Activity", Procedure = "WebIVT.IVT_Get_AM_Activity",
                Inputs = new[] { IvtReportInput.Activity, IvtReportInput.AccountManager, IvtReportInput.FromDate, IvtReportInput.ToDate }, LegacyExportFolder = "AM_Activity",
                Description = "Account manager actions (vetting completed, holds, releases, ...)." },
        };

        private readonly IvtAdminRuntime rt;
        public QueryReportService(IvtAdminRuntime runtime) { rt = runtime; }

        public static IvtReportDef Find(string key)
        {
            var def = Reports.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (def == null) throw new IvtOperationException("Unknown report.");
            return def;
        }

        // Validates filters and builds the exact parameters. Returns errors instead of guessing missing values.
        public List<IvtParam> Parameters(IvtReportDef def, IvtReportFilters f, List<string> errors)
        {
            var list = new List<IvtParam>();
            foreach (var input in def.Inputs)
            {
                switch (input)
                {
                    case IvtReportInput.Search:
                        var search = (f.Search ?? "").Trim();
                        if (search.Length > 500) errors.Add("Search text is too long.");
                        list.Add(IvtParam.NVarChar("@searchtxt", 1000, search.Length == 0 && def.SearchAllKeyword ? "ALL" : search));
                        break;
                    case IvtReportInput.InvoiceWeek:
                        if (!f.InvoiceWeek.HasValue) errors.Add("Choose an invoice week.");
                        list.Add(IvtParam.Date("@InvoiceWeek", f.InvoiceWeek));
                        break;
                    case IvtReportInput.FromDate:
                        if (!f.From.HasValue) errors.Add("Choose a from date.");
                        list.Add(IvtParam.Date("@From_Date", f.From));
                        break;
                    case IvtReportInput.ToDate:
                        if (!f.To.HasValue) errors.Add("Choose a to date.");
                        else if (f.From.HasValue && f.To.Value < f.From.Value) errors.Add("The to date is before the from date.");
                        list.Add(IvtParam.Date("@To_Date", f.To));
                        break;
                    case IvtReportInput.Activity:
                        list.Add(IvtParam.NVarChar("@activity", 1000, string.IsNullOrWhiteSpace(f.Activity) ? "ALL" : f.Activity.Trim()));
                        break;
                    case IvtReportInput.AccountManager:
                        // Login name is the filter value; the display name is only shown in the picker.
                        list.Add(IvtParam.NVarChar("@am", 1000, string.IsNullOrWhiteSpace(f.AccountManager) ? "ALL" : f.AccountManager.Trim()));
                        break;
                }
            }
            return list;
        }

        public async Task<IvtTable> RunAsync(IvtReportDef def, IvtReportFilters filters, CancellationToken cancellation)
        {
            var errors = new List<string>();
            var parameters = Parameters(def, filters, errors);
            if (errors.Count > 0) throw new IvtOperationException(string.Join(" ", errors));
            return await rt.Database.QueryProcedureAsync(IvtDbTarget.Production, def.Procedure, parameters, cancellation).ConfigureAwait(false);
        }

        public static IEnumerable<string> Describe(IvtReportDef def, IvtReportFilters f)
        {
            yield return "Report: " + def.Title + " - PRODUCTION (IVT)";
            foreach (var input in def.Inputs)
            {
                switch (input)
                {
                    case IvtReportInput.Search: yield return "Search: " + (string.IsNullOrWhiteSpace(f.Search) ? "ALL" : f.Search.Trim()); break;
                    case IvtReportInput.InvoiceWeek: yield return "Invoice week: " + (f.InvoiceWeek.HasValue ? f.InvoiceWeek.Value.ToString("yyyy-MM-dd") : ""); break;
                    case IvtReportInput.FromDate: yield return "From: " + (f.From.HasValue ? f.From.Value.ToString("yyyy-MM-dd") : ""); break;
                    case IvtReportInput.ToDate: yield return "To: " + (f.To.HasValue ? f.To.Value.ToString("yyyy-MM-dd") : ""); break;
                    case IvtReportInput.Activity: yield return "Activity: " + (string.IsNullOrWhiteSpace(f.Activity) ? "ALL" : f.Activity); break;
                    case IvtReportInput.AccountManager: yield return "Account manager: " + (string.IsNullOrWhiteSpace(f.AccountManager) ? "ALL" : f.AccountManager); break;
                }
            }
            if (def.Inputs.Length == 0) yield return "Filters: none (this report has no parameters)";
        }

        public async Task<List<string>> ActivitiesAsync(CancellationToken cancellation)
        {
            var table = await rt.Database.QueryProcedureAsync(IvtDbTarget.Production, "WebIVT.sp_getAM_Activity_List", new IvtParam[0], cancellation).ConfigureAwait(false);
            table.RequireColumns("sp_getAM_Activity_List", new[] { "Activity" });
            return table.Rows.Select(r => table.GetString(r, "Activity")).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        }

        public async Task<List<KeyValuePair<string, string>>> AccountManagersAsync(CancellationToken cancellation)
        {
            var table = await rt.Database.QueryProcedureAsync(IvtDbTarget.Production, "WebIVT.sp_getAM_List", new IvtParam[0], cancellation).ConfigureAwait(false);
            table.RequireColumns("sp_getAM_List", new[] { "AccountManager", "LoginName" });
            return table.Rows.Where(r => !string.IsNullOrWhiteSpace(table.GetString(r, "LoginName")))
                .Select(r => new KeyValuePair<string, string>(table.GetString(r, "LoginName"), table.GetString(r, "AccountManager"))).ToList();
        }
    }
}
