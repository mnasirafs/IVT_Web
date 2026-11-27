using AFSDataGateway.Services.IvtAdmin;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace AFSDataGateway.Controllers
{
    // Reference settings (account managers, rules, options, exclusions, customer/carrier, fuel, consolidation,
    // email control) and shared contacts, for production IVT or IVT_Test.
    public sealed class IvtSettingsController : IvtAdminBaseController
    {
        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public ActionResult Entity(string key, string env)
        {
            var def = ReferenceCatalog.Find(key);
            var environment = Env(env);
            if (!def.Supports(environment)) return Failure(404, def.Title + " is not available for " + environment.Label() + ".", "Error");
            return View(def);
        }

        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Search(string key, string env, string search)
        {
            var def = ReferenceCatalog.Find(key);
            var table = await new ReferenceDataService(Runtime).SearchAsync(def, Env(env), search, RequestAborted);
            return TableJson(table, new { search = ReferenceDataService.NormalizeSearch(search) });
        }

        private static JObject Parse(string json)
        {
            try { return JObject.Parse(json ?? "{}"); }
            catch (Newtonsoft.Json.JsonException) { throw new IvtOperationException("The submitted data is not valid."); }
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Update(string key, string env, string search, string row)
        {
            return JsonData(await new ReferenceDataService(Runtime).UpdateAsync(ReferenceCatalog.Find(key), Env(env), search, Parse(row), Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> UpdateAll(string key, string env, string search, int expectedCount, string rows)
        {
            JArray array;
            try { array = JArray.Parse(rows ?? "[]"); } catch (Newtonsoft.Json.JsonException) { return Failure(400, "The submitted rows are not valid.", "Error"); }
            return JsonData(await new ReferenceDataService(Runtime).UpdateAllAsync(ReferenceCatalog.Find(key), Env(env), search, expectedCount, array, Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Create(string key, string env, string values)
        {
            return JsonData(await new ReferenceDataService(Runtime).CreateAsync(ReferenceCatalog.Find(key), Env(env), Parse(values), Operator, RequestAborted));
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Toggle(string key, string env, string search, long id, string field, bool value)
        {
            return JsonData(await new ReferenceDataService(Runtime).SetFlagAsync(ReferenceCatalog.Find(key), Env(env), search, id, field, value, Operator, RequestAborted));
        }

        [HttpGet, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> Contacts(string env)
        {
            var environment = Env(env);
            var table = await new ReferenceDataService(Runtime).ContactsAsync(environment, RequestAborted);
            var row = table.Rows.FirstOrDefault();
            ViewBag.Cc = row == null ? null : table.GetString(row, "CC_Contact");
            ViewBag.Exec = row == null ? null : table.GetString(row, "EXEC_Contact");
            ViewBag.Admin = row == null ? null : table.GetString(row, "ADMIN_Contact");
            ViewBag.Rows = table.Rows.Count;
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken, IvtAccess(IvtAccessLevel.Admin)]
        public async Task<ActionResult> SaveContacts(string env, string cc, string exec, string admin)
        {
            return JsonData(await new ReferenceDataService(Runtime).UpdateContactsAsync(Env(env), cc, exec, admin, Operator, RequestAborted));
        }
    }
}
