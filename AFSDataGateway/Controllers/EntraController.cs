using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using AFSDataGateway.Models;
using AFSDataGateway.Services;
using AFSDataGateway.Services.Entra;

namespace AFSDataGateway.Controllers
{
    // Microsoft Entra ID sign-in for IVT. Disabled unless IVT_ENTRA_ENABLED=true and the app registration settings are present.
    [AllowAnonymous]
    public class EntraController : Controller
    {
        public ActionResult SignIn(string returnUrl, bool switchAccount = false)
        {
            if (!EntraAuth.Enabled) return HttpNotFound();
            if (switchAccount)
            {
                // Drop the current IVT session first so a cancelled picker doesn't leave the old account half signed in.
                EntraAuth.ClearCookie(HttpContext);
                Session["LoggedAs"] = null;
                Session["LoginName"] = null;
            }
            return Redirect(EntraAuth.BuildAuthorizeUrl(HttpContext, returnUrl, switchAccount));
        }

        // Redirect URI (/signin-oidc is routed here).
        public async Task<ActionResult> Callback(string code, string state, string error, string error_description)
        {
            if (!EntraAuth.Enabled) return HttpNotFound();
            if (!string.IsNullOrEmpty(error)) return Problem("Microsoft sign-in failed: " + error + " - " + EntraAuth.FirstLine(error_description));
            try
            {
                var result = await EntraAuth.CompleteSignInAsync(HttpContext, code, state);
                var login = UserIdentity.NormalizeLogin(result.Email);
                if (string.IsNullOrEmpty(login))
                    return Problem(result.Email + " is not an approved company account (allowed domains: " + string.Join(", ", UserIdentity.CompanyDomains) + ").");
                using (var db = new IVTEntities())
                {
                    var user = db.vIVTUser.FirstOrDefault(a => a.LoginName.ToUpper() == login);
                    if (user == null || user.IsActive != 1 || user.IsDeleted != 0)
                        return Problem(result.Email + " (IVT login " + login + ") has no active IVT account. Ask an IVT admin to add it or map it in IVT_USER_EMAILS.");
                }
                Session["LoggedAs"] = null;
                Session["LoginName"] = null;
                EntraAuth.IssueCookie(HttpContext, result.Email);
                return Redirect(result.ReturnUrl ?? Url.Action("Dashboard", "Home"));
            }
            catch (EntraException ex)
            {
                return Problem(ex.Message);
            }
        }

        public ActionResult SignOut()
        {
            EntraAuth.ClearCookie(HttpContext);
            Session.Clear();
            if (!EntraAuth.Enabled) return Redirect("~/");
            // Land on /Entra/SignedOut (an open path) rather than "~/", which would bounce straight back into sign-in.
            var signedOut = Request.Url.GetLeftPart(UriPartial.Authority) + Url.Action("SignedOut", "Entra");
            return Redirect(EntraAuth.BuildLogoutUrl(signedOut));
        }

        public ActionResult SignedOut()
        {
            if (!EntraAuth.Enabled) return Redirect("~/");
            var signIn = Url.Action("SignIn", "Entra", new { switchAccount = true });
            return Content("<!doctype html><title>Signed out - IVT</title><body style=\"font-family:Segoe UI,sans-serif;padding:2rem\"><h2>You have signed out of IVT</h2>" +
                "<p>Your Microsoft session has ended. You can close this tab, or sign in again with the same or a different account.</p>" +
                "<p><a href=\"" + HttpUtility.HtmlAttributeEncode(signIn) + "\" style=\"display:inline-block;padding:.6rem 1.2rem;background:#0f6cbd;color:#fff;border-radius:4px;text-decoration:none\">Sign in with Microsoft</a></p></body>", "text/html");
        }

        // Diagnostics for testing: who IVT thinks you are and what is configured (no secrets).
        public ActionResult Status()
        {
            return Json(new
            {
                enabled = EntraAuth.Enabled,
                homeTenant = EntraAuth.HomeTenant,
                clientId = EntraAuth.ClientId,
                allowedTenants = EntraAuth.AllowedTenants,
                redirectUri = EntraAuth.RedirectUri(Request),
                secretConfigured = !string.IsNullOrEmpty(EntraAuth.Setting("IVT_ENTRA_CLIENT_SECRET")),
                identity = User != null && User.Identity != null ? User.Identity.Name : null,
                authenticationType = User != null && User.Identity != null ? User.Identity.AuthenticationType : null,
                ivtLogin = SafeLogin(),
                canSendAsUser = Session[EntraAuth.SessionRefreshToken] != null,
                approvedSenders = GraphMail.ApprovedSenders
            }, JsonRequestBehavior.AllowGet);
        }

        // Test Graph mail. Only ever sends to the signed-in Entra user's own address.
        // mode=user: from the signed-in user. mode=app: from the approved sender mailbox (AFSReports) with an app-only token.
        public async Task<ActionResult> TestMail(string mode)
        {
            if (!EntraAuth.Enabled || !EntraAuth.IsEntraUser(HttpContext)) return new HttpStatusCodeResult(403, "Sign in with Microsoft first.");
            var me = User.Identity.Name;
            var subject = "IVT Graph mail test (" + (mode == "app" ? "app-only" : "signed-in user") + ")";
            var body = "<p>This is a test email from IVT sent through Microsoft Graph at " + HttpUtility.HtmlEncode(DateTime.Now.ToString("u")) + ".</p>";
            try
            {
                if (mode == "app") await GraphMail.SendAsAppAsync(GraphMail.ApprovedSenders[0], new[] { me }, subject, body);
                else await GraphMail.SendAsSignedInUserAsync(HttpContext, new[] { me }, subject, body);
                return Json(new { ok = true, sentTo = me, mode = mode == "app" ? "app" : "user" }, JsonRequestBehavior.AllowGet);
            }
            catch (EntraException ex)
            {
                Response.StatusCode = 502;
                Response.TrySkipIisCustomErrors = true;
                return Json(new { ok = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private string SafeLogin()
        {
            try { return UserIdentity.CurrentLogin(); } catch (Exception ex) { return "error: " + ex.Message; }
        }

        private ActionResult Problem(string message)
        {
            Response.StatusCode = 400;
            Response.TrySkipIisCustomErrors = true;
            return Content("<!doctype html><title>IVT sign-in</title><body style=\"font-family:Segoe UI,sans-serif;padding:2rem\"><h2>Microsoft sign-in</h2><p>" +
                HttpUtility.HtmlEncode(message) + "</p><p><a href=\"" + Url.Content("~/") + "\">Back to IVT</a></p></body>", "text/html");
        }
    }
}
