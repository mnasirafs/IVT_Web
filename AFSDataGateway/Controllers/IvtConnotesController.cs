using AFSDataGateway.Services.IvtAdmin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace AFSDataGateway.Controllers
{
    // Operational queries (review, accepted, released, BMS reconciliation, AM activity), manual hold/release and comments.
    // All against production IVT, as in the desktop tool.
    public sealed class IvtConnotesController : IvtAdminBaseController
    {
        private IvtReportFilters Filters(string search, string invoiceWeek, string from, string to, string activity, string am)
        {
            return new IvtReportFilters { Search = search, InvoiceWeek = ParseDate(invoiceWeek), From = ParseDate(from), To = ParseDate(to), Activity = activity, AccountManager = am };
        }

        [HttpGet, IvtAccess(IvtAccessLevel.Read)]
        public ActionResult Report(string key)
        {
            var def = QueryReportService.Find(key);
            var week = new RunControlService(Runtime).DefaultInvoiceWeek();
            ViewBag.DefaultWeek = week.ToString("yyyy-MM-dd");
            ViewBag.DefaultFrom = Runtime.BusinessToday().AddDays(-7).ToString("yyyy-MM-dd");
            ViewBag.DefaultTo = Runtime.BusinessToday().ToString("yyyy-MM-dd");
            return View(def);
        }

        [HttpGet, IvtAccess(IvtAccessLevel.Read)]
        public async Task<ActionResult> ReportData(string key, string search, string invoiceWeek, string from, string to, string activity, string am)
        {
            var def = QueryReportService.Find(key);
            var filters = Filters(search, invoiceWeek, from, to, activity, am);
            var table = await new QueryReportService(Runtime).RunAsync(def, filters, RequestAborted);
            // Some reports return 100k+ rows; the page shows the first rows and the true total. Export CSV returns everything.
            const int maxDisplayRows = 2000;
            var total = table.Rows.Count;
            if (total > maxDisplayRows) table.Rows.RemoveRange(maxDisplayRows, total - maxDisplayRows);
            return TableJson(table, new { filters = QueryReportService.Describe(def, filters), total, truncated = total > maxDisplayRows });
        }

        [HttpGet, IvtAccess(IvtAccessLevel.Read)]
        public async Task<ActionResult> Export(string key, string search, string invoiceWeek, string from, string to, string activity, string am)
        {
            var def = QueryReportService.Find(key);
            var filters = Filters(search, invoiceWeek, from, to, activity, am);
            var table = await new QueryReportService(Runtime).RunAsync(def, filters, RequestAborted);
            var preamble = QueryReportService.Describe(def, filters).Concat(new[]
                { "Rows: " + table.Rows.Count, "Generated: " + Runtime.BusinessNow().ToString("yyyy-MM-dd HH:mm") + " by " + Operator.Login }).ToList();
            var name = def.Key + "_" + Runtime.BusinessNow().ToString("yyyyMMdd_HHmm") + ".csv";
            return File(IvtCsv.Build(table, preamble), "text/csv", name);
        }

        [HttpGet, IvtAccess(IvtAccessLevel.Read)]
        public async Task<ActionResult> Lists()
        {
            var service = new QueryReportService(Runtime);
            var activities = await service.ActivitiesAsync(RequestAborted);
            var managers = await service.AccountManagersAsync(RequestAborted);
            return JsonData(new { ok = true, activities, managers = managers.Select(m => new { login = m.Key, name = m.Value }) });
        }

        // ---------------- manual hold / release
        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public ActionResult HoldRelease() { return View(); }

        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Candidates(string mode, string search)
        {
            var service = new ConnoteActionService(Runtime);
            var table = mode == "release" ? await service.ReleaseCandidatesAsync(search, RequestAborted) : await service.HoldCandidatesAsync(search, RequestAborted);
            return TableJson(table, new { truncated = table.Rows.Count > ConnoteActionService.MaxRows });
        }

        private static List<long> Ids(string ids)
        {
            var list = new List<long>();
            foreach (var part in (ids ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                long id;
                if (!long.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id)) throw new IvtOperationException("Invalid selection.");
                list.Add(id);
            }
            return list;
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Hold(string search, bool all, string ids, int expectedCount, string text)
        {
            var request = new ConnoteActionService.ActionRequest { Search = search, All = all, Ids = Ids(ids), ExpectedCount = expectedCount, Text = text };
            return JsonData(await new ConnoteActionService(Runtime).HoldAsync(request, Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Release(string search, bool all, string ids, int expectedCount, string text)
        {
            var request = new ConnoteActionService.ActionRequest { Search = search, All = all, Ids = Ids(ids), ExpectedCount = expectedCount, Text = text };
            return JsonData(await new ConnoteActionService(Runtime).ReleaseAsync(request, Operator, RequestAborted));
        }

        // ---------------- comments
        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public ActionResult Comments(string job) { ViewBag.Job = job; return View(); }

        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Log(string job)
        {
            var table = await new CommentService(Runtime).LogAsync(job, RequestAborted);
            var pending = (await Runtime.Audit.RecentAsync("COMMENT.ADD", 50, RequestAborted))
                .Where(e => e.Outcome == IvtOutcome.SavedNotifyFailed && (e.Target ?? "") == "Job " + CommentService.ValidateJobNumber(job))
                .Select(e => new { e.Id, when = TimeZoneInfo.ConvertTimeFromUtc(e.OccurredAtUtc, Runtime.Settings.TimeZone()), e.Operator });
            return TableJson(table, new { pendingNotifications = pending });
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> AddComment(string job, string text)
        {
            return JsonData(await new CommentService(Runtime).AddAsync(job, text, Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> RetryNotification(long auditId)
        {
            return JsonData(await new CommentService(Runtime).RetryNotificationAsync(auditId, Operator, RequestAborted));
        }
    }
}
