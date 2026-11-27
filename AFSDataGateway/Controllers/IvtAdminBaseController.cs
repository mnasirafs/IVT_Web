using AFSDataGateway.Models;
using AFSDataGateway.Services.Chat;
using AFSDataGateway.Services.IvtAdmin;
using Newtonsoft.Json;
using System;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Web;
using System.Web.Mvc;

namespace AFSDataGateway.Controllers
{
    public enum IvtAccessLevel { Read, Admin }

    // Enforces IvtRead / IvtAdmin on the server for every action (navigation visibility is not a control).
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class IvtAccessAttribute : ActionFilterAttribute
    {
        public IvtAccessLevel Level { get; private set; }
        public IvtAccessAttribute(IvtAccessLevel level) { Level = level; Order = 10; }
    }

    [Authorize]
    public abstract class IvtAdminBaseController : Controller
    {
        protected IvtAdminRuntime Runtime { get; private set; }
        protected IvtOperator Operator { get; private set; }
        protected CancellationToken RequestAborted { get { return Response.ClientDisconnectedToken; } }

        public static IvtOperator ResolveOperator(IPrincipal user, IvtAdminSettings settings)
        {
            string login = null; var admin = false;
            try
            {
                using (var db = new IVTEntities())
                {
                    db.Database.CommandTimeout = 30;
                    // The real signed-in identity only: "Login As" impersonation never confers or narrows admin rights here.
                    var scope = ChatAccessScope.Resolve(user, null, db.vIVTUser.AsNoTracking());
                    login = scope.Actor; admin = scope.IsAdmin;
                }
            }
            catch (ChatAccessException) { }
            if (login == null)
            {
                var name = user == null || user.Identity == null ? null : user.Identity.Name;
                if (string.IsNullOrWhiteSpace(name)) return null;
                login = name.Split('\\').Last().Split('@').First().Trim().ToUpperInvariant();
            }
            var reader = admin || settings.Access.ReaderLogins.Any(l => string.Equals(l, login, StringComparison.OrdinalIgnoreCase))
                               || settings.Access.ReaderGroups.Any(g => !string.IsNullOrWhiteSpace(g) && user.IsInRole(g));
            return new IvtOperator { Login = login, IsAdmin = admin, CanRead = reader };
        }

        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            var required = (filterContext.ActionDescriptor.GetCustomAttributes(typeof(IvtAccessAttribute), true).FirstOrDefault()
                         ?? filterContext.ActionDescriptor.ControllerDescriptor.GetCustomAttributes(typeof(IvtAccessAttribute), true).FirstOrDefault()) as IvtAccessAttribute;
            var level = required == null ? IvtAccessLevel.Admin : required.Level;   // secure default
            try
            {
                Runtime = IvtAdminRuntime.Create();
            }
            catch (IvtConfigurationException ex)
            {
                filterContext.Result = Failure(503, ex.Message, "NotConfigured");
                return;
            }
            Operator = ResolveOperator(User, Runtime.Settings);
            var allowed = Operator != null && (level == IvtAccessLevel.Read ? Operator.CanRead : Operator.IsAdmin);
            // Every POST is a state change and requires IvtAdmin (plus the anti-forgery token, checked by the action attribute).
            if (allowed && Request.HttpMethod == "POST" && !Operator.IsAdmin) allowed = false;
            if (!allowed)
            {
                Trace.TraceWarning("IVT admin access denied: user={0}; action={1}; level={2}", Operator == null ? "?" : Operator.Login, filterContext.ActionDescriptor.ActionName, level);
                filterContext.Result = Failure(403, level == IvtAccessLevel.Read ? "IVT Administration reports are available to IVT administrators and approved readers only."
                                                                                  : "This action requires IVT administrator permission.", "Denied");
                return;
            }
            ViewBag.IvtOperator = Operator;
            ViewBag.DisplayForSuperAdmin = Operator.IsAdmin ? "visible" : null;
            base.OnActionExecuting(filterContext);
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            if (filterContext.ExceptionHandled) return;
            if (filterContext.Exception is HttpAntiForgeryException)
            {
                filterContext.Result = Failure(403, "The page's security token is missing or expired. Refresh the page and try again. Nothing was changed.", "Denied");
                filterContext.ExceptionHandled = true;
                return;
            }
            var correlation = Guid.NewGuid();
            var message = IvtErrors.Describe(filterContext.Exception, correlation);
            var status = filterContext.Exception is IvtOperationException || filterContext.Exception is IvtContractException ? 409 : 500;
            filterContext.Result = Failure(status, message, "Error");
            filterContext.ExceptionHandled = true;
        }

        private bool WantsJson()
        {
            return Request.HttpMethod == "POST" || Request.AcceptTypes != null && Request.AcceptTypes.Any(a => a.Contains("application/json"))
                   || string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }

        protected ActionResult Failure(int status, string message, string view)
        {
            message = IvtErrors.DisplayText(message);
            Response.StatusCode = status;
            Response.TrySkipIisCustomErrors = true;
            if (WantsJson()) return Content(JsonConvert.SerializeObject(new { ok = false, outcome = "ERROR", message }), "application/json");
            ViewBag.Problem = message;
            return View("~/Views/IvtAdmin/Problem.cshtml");
        }

        protected ActionResult JsonData(object value)
        {
            return Content(JsonConvert.SerializeObject(value, new JsonSerializerSettings { DateFormatString = "yyyy-MM-dd HH:mm:ss" }), "application/json");
        }

        // Tables are returned with column metadata and raw values (null stays null).
        protected ActionResult TableJson(IvtTable table, object extra = null)
        {
            return JsonData(new { ok = true, columns = table.Columns.Select(c => new { name = c.Name, type = c.SqlType }), rows = table.Rows, count = table.Rows.Count, extra });
        }

        protected IvtEnvironment Env(string env)
        {
            IvtEnvironment environment;
            if (!IvtEnvironments.TryParse(env ?? "production", out environment)) throw new IvtOperationException("Unknown environment.");
            ViewBag.IvtEnvironment = environment;
            return environment;
        }

        protected static DateTime? ParseDate(string value)
        {
            DateTime d;
            return DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }
    }
}
