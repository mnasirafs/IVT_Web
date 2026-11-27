using AFSDataGateway.Services.IvtAdmin;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace AFSDataGateway.Controllers
{
    // IVT Administration: overview/health, run control (production, silent, test, Azure), rollback,
    // file-based loads, notifications and the audit log.
    public sealed class IvtAdminController : IvtAdminBaseController
    {
        [HttpGet, IvtAccess(IvtAccessLevel.Read)]
        public ActionResult Index() { return View(Runtime.Settings); }

        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Health()
        {
            return JsonData(new { ok = true, items = await new ContractHealthService(Runtime).CheckAsync(RequestAborted) });
        }

        // ---------------- run control
        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public ActionResult RunControl(string env)
        {
            var environment = Env(env);
            var service = new RunControlService(Runtime);
            ViewBag.DefaultWeek = service.DefaultInvoiceWeek().ToString("yyyy-MM-dd");
            ViewBag.Today = Runtime.BusinessToday().ToString("yyyy-MM-dd");
            ViewBag.Service = service;
            return View(Runtime.Settings);
        }

        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> RunStatus(string env)
        {
            var environment = Env(env);
            var service = new RunControlService(Runtime);
            var jobs = await service.JobStatusesAsync(environment, RequestAborted);
            object history; string historyError = null;
            try
            {
                var table = await service.HistoryAsync(environment, RequestAborted);
                history = new { columns = table.Columns.Select(c => c.Name), rows = table.Rows };
            }
            catch (Exception ex) { history = null; historyError = IvtErrors.Describe(ex, Guid.NewGuid()); }
            var recent = await Runtime.Audit.RecentAsync("RUN.", 10, RequestAborted);
            return JsonData(new
            {
                ok = true, environment = environment.Label(), jobs, history, historyError,
                recent = recent.Select(r => new { r.Id, when = TimeZoneInfo.ConvertTimeFromUtc(r.OccurredAtUtc, Runtime.Settings.TimeZone()), r.Operator, r.Operation, r.Environment, r.InvoiceWeek, r.Outcome, r.ExternalId, r.CorrelationId })
            });
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> StartRun(string env, string kind, string invoiceWeek, bool landingConfirmed = false)
        {
            var environment = Env(env);
            IvtRunKind runKind;
            if (!Enum.TryParse(kind, true, out runKind)) return Failure(400, "Unknown run type.", "Error");
            // The environment selected on the page must match the run type (test runs only from the Test page and vice versa).
            if ((runKind == IvtRunKind.Test) != (environment == IvtEnvironment.Test)) return Failure(400, "This run type is not available for " + environment.Label() + ".", "Error");
            return JsonData(await new RunControlService(Runtime).StartRunAsync(runKind, ParseDate(invoiceWeek), landingConfirmed, Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> StartAzureRun(string invoiceWeek, bool landingConfirmed = false)
        {
            return JsonData(await new RunControlService(Runtime).StartAzureRunAsync(ParseDate(invoiceWeek), landingConfirmed, Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> AzureRunStatus(string runId)
        {
            return JsonData(new { ok = true, message = await new RunControlService(Runtime).AzureRunStatusAsync(runId, RequestAborted) });
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Rollback(string env, string confirmation)
        {
            return JsonData(await new RunControlService(Runtime).RollbackAsync(Env(env), confirmation, Operator, RequestAborted));
        }

        // ---------------- file loads
        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public ActionResult Files() { return View(Runtime.Settings); }

        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> FileStatus()
        {
            var service = new FileLoadService(Runtime);
            var folders = FileLoadService.Templates.Select(t =>
            {
                try { return new { kind = t.Kind.ToString(), files = Runtime.Landing.List(t.Folder, service.Pattern(t.Kind)), error = (string)null }; }
                catch (IvtOperationException ex) { return new { kind = t.Kind.ToString(), files = (System.Collections.Generic.IReadOnlyList<IvtLandingFile>)null, error = ex.Message }; }
            }).ToList();
            var adminJob = await Runtime.Agent.GetStatusAsync(Runtime.Settings.Jobs.AdminFileLoad, RequestAborted);
            var refJob = await Runtime.Agent.GetStatusAsync(Runtime.Settings.Jobs.ReferenceDataLoad, RequestAborted);
            object errors;
            try { errors = Runtime.Landing.ErrorFiles(IvtLandingFolder.AdminLoadRoot).Select(e => new { name = e.Key, text = e.Value }); }
            catch (Exception ex) { errors = new[] { new { name = "Error files", text = ex.Message } }; }
            var recent = await Runtime.Audit.RecentAsync("FILE.", 15, RequestAborted);
            return JsonData(new { ok = true, folders, adminJob, refJob, loaderErrors = errors,
                recent = recent.Select(r => new { r.Id, when = TimeZoneInfo.ConvertTimeFromUtc(r.OccurredAtUtc, Runtime.Settings.TimeZone()), r.Operator, r.Operation, r.Target, r.Outcome }) });
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Upload(string kind)
        {
            IvtFileLoadKind loadKind;
            if (!Enum.TryParse(kind, true, out loadKind)) return Failure(400, "Unknown load type.", "Error");
            var file = Request.Files.Count == 1 ? Request.Files[0] : null;
            if (file == null || file.ContentLength == 0) return Failure(400, "Choose one file to upload.", "Error");
            if (file.ContentLength > Runtime.Settings.MaxUploadBytes) return Failure(400, "The file is too large.", "Error");
            byte[] content;
            using (var ms = new MemoryStream()) { file.InputStream.CopyTo(ms); content = ms.ToArray(); }
            // Only the bare name is used; client paths (e.g. C:\fakepath\x.csv) are stripped and then validated.
            var name = Path.GetFileName(file.FileName ?? "");
            return JsonData(await new FileLoadService(Runtime).UploadAsync(loadKind, name, content, Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> StartLoad(string kind, bool confirmed = false)
        {
            IvtFileLoadKind loadKind;
            if (!Enum.TryParse(kind, true, out loadKind)) return Failure(400, "Unknown load type.", "Error");
            return JsonData(await new FileLoadService(Runtime).StartLoadAsync(loadKind, confirmed, Operator, RequestAborted));
        }

        // Download a waiting landing file or a template sample (read-only).
        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public ActionResult LandingFile(string kind, string name)
        {
            IvtFileLoadKind loadKind;
            if (!Enum.TryParse(kind, true, out loadKind)) return Failure(400, "Unknown load type.", "Error");
            var stream = Runtime.Landing.OpenRead(FileLoadService.Template(loadKind).Folder, name);
            return File(stream, "text/csv", FileLandingStore.ValidateFileName(name, null));
        }

        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public ActionResult Template(string kind)
        {
            IvtFileLoadKind loadKind;
            if (!Enum.TryParse(kind, true, out loadKind)) return Failure(400, "Unknown load type.", "Error");
            var t = FileLoadService.Template(loadKind);
            if (t.RequiredColumns == null) return Failure(404, "No verified template for this load.", "Error");
            var csv = string.Join(",", t.RequiredColumns) + "\r\n";
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", t.Kind + "_template.csv");
        }

        // ---------------- notifications
        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Notifications()
        {
            ViewBag.Recent = await Runtime.Audit.RecentAsync("NOTIFY.", 20, RequestAborted);
            return View(Runtime.Settings);
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Notify(string operation, bool sendAgain = false)
        {
            return JsonData(await new NotificationActionService(Runtime).CallLegacyAsync(operation, sendAgain, Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> NotifyTest()
        {
            return JsonData(await new NotificationActionService(Runtime).RunTestNotifierAsync(Operator, RequestAborted));
        }

        // ---------------- audit
        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Audit(string prefix)
        {
            ViewBag.Prefix = prefix;
            var entries = await Runtime.Audit.RecentAsync(string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim().ToUpperInvariant(), 300, RequestAborted);
            return View(entries);
        }
    }
}
