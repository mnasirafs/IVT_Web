using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Security;
using Newtonsoft.Json.Linq;

namespace AFSDataGateway.Services.Entra
{
    // Microsoft Entra ID sign-in (OIDC authorization code + PKCE) for a confidential web client, without OWIN.
    // The ID token comes straight from the token endpoint over TLS, which OIDC Core 3.1.3.7 accepts in place of
    // a signature check; issuer, audience, tenant, nonce and expiry are still validated.
    public static class EntraAuth
    {
        public const string CookieName = "IVT.Entra";
        public const string AuthType = "Entra";
        private const string SessionState = "Entra.State";
        private const string SessionNonce = "Entra.Nonce";
        private const string SessionVerifier = "Entra.Verifier";
        private const string SessionReturn = "Entra.Return";
        internal const string SessionRefreshToken = "Entra.RefreshToken";
        internal const string SessionTenant = "Entra.Tenant";

        internal static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        public const string DelegatedScopes = "openid profile email offline_access https://graph.microsoft.com/User.Read https://graph.microsoft.com/Mail.Send";

        internal static string Setting(string key)
        {
            return (Environment.GetEnvironmentVariable(key) ?? ConfigurationManager.AppSettings[key] ?? "").Trim();
        }

        public static bool Enabled
        {
            get { return Setting("IVT_ENTRA_ENABLED").Equals("true", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ClientId); }
        }

        // Azure App Service: no Windows/AD login is possible, so every page needs a Microsoft sign-in.
        public static bool Required
        {
            get { return Enabled && Setting("IVT_ENTRA_REQUIRED").Equals("true", StringComparison.OrdinalIgnoreCase); }
        }

        private static readonly string[] OpenPaths = { "/entra/", "/signin-oidc", "/content/", "/scripts/", "/fonts/", "/bundles/", "/images/", "/favicon.ico" };

        // Called from Global.asax after ApplyPrincipal: sends unauthenticated requests to Microsoft sign-in.
        public static void EnforceSignIn(HttpContext context)
        {
            if (!Required || IsEntraUser(new HttpContextWrapper(context))) return;
            var request = context.Request;
            var path = (request.AppRelativeCurrentExecutionFilePath ?? "~/").Substring(1).ToLowerInvariant();
            if (OpenPaths.Any(p => path.StartsWith(p))) return;
            var ajax = request.Headers["X-Requested-With"] == "XMLHttpRequest" || (request.AcceptTypes ?? new string[0]).Any(a => a.Contains("json"));
            if (ajax || request.HttpMethod != "GET")
            {
                context.Response.StatusCode = 401;
                context.Response.SuppressFormsAuthenticationRedirect = true;
                context.Response.TrySkipIisCustomErrors = true;
            }
            else
            {
                context.Response.Redirect(VirtualPathUtility.ToAbsolute("~/Entra/SignIn") + "?returnUrl=" + HttpUtility.UrlEncode(request.RawUrl), false);
            }
            context.ApplicationInstance.CompleteRequest();
        }

        // Tenant ID of the app registration (used for app-only tokens).
        public static string HomeTenant { get { return Setting("IVT_ENTRA_TENANT_ID"); } }
        public static string ClientId { get { return Setting("IVT_ENTRA_CLIENT_ID"); } }
        internal static string ClientSecret { get { return Setting("IVT_ENTRA_CLIENT_SECRET"); } }

        // Tenants whose users may sign in. Defaults to the home tenant; add the EFM tenant ID for a multi-tenant app.
        public static string[] AllowedTenants
        {
            get
            {
                var list = Setting("IVT_ENTRA_ALLOWED_TENANTS").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).ToList();
                if (list.Count == 0 && HomeTenant.Length > 0) list.Add(HomeTenant.ToLowerInvariant());
                return list.ToArray();
            }
        }

        // Single-tenant apps sign in against their own tenant; more than one allowed tenant needs the multi-tenant endpoint.
        private static string SignInAuthority
        {
            get { return AllowedTenants.Length > 1 ? "organizations" : HomeTenant; }
        }

        public static string RedirectUri(HttpRequestBase request)
        {
            var configured = Setting("IVT_ENTRA_REDIRECT_URI");
            if (configured.Length > 0) return configured;
            return request.Url.GetLeftPart(UriPartial.Authority) + request.ApplicationPath.TrimEnd('/') + "/signin-oidc";
        }

        public static string BuildAuthorizeUrl(HttpContextBase context, string returnUrl, bool selectAccount = false)
        {
            var state = RandomToken();
            var nonce = RandomToken();
            var verifier = RandomToken() + RandomToken();
            context.Session[SessionState] = state;
            context.Session[SessionNonce] = nonce;
            context.Session[SessionVerifier] = verifier;
            context.Session[SessionReturn] = IsLocalUrl(returnUrl) ? returnUrl : null;
            string challenge;
            using (var sha = SHA256.Create())
                challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

            var query = new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "response_type", "code" },
                { "response_mode", "query" }, // a top-level GET keeps the SameSite=Lax session cookie
                { "redirect_uri", RedirectUri(context.Request) },
                { "scope", DelegatedScopes },
                { "state", state },
                { "nonce", nonce },
                { "code_challenge", challenge },
                { "code_challenge_method", "S256" }
                // No prompt by default: an existing Microsoft session signs the user straight back in without the account picker.
            };
            if (selectAccount) query["prompt"] = "select_account"; // "Switch account" / after sign-out: always show the picker
            return "https://login.microsoftonline.com/" + Uri.EscapeDataString(SignInAuthority) + "/oauth2/v2.0/authorize?" +
                string.Join("&", query.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
        }

        // Ends the Microsoft session too, then lands on IVT's signed-out page (which does not auto sign back in).
        public static string BuildLogoutUrl(string postLogoutRedirectUri)
        {
            var authority = HomeTenant.Length > 0 && AllowedTenants.Length <= 1 ? HomeTenant : "common";
            return "https://login.microsoftonline.com/" + Uri.EscapeDataString(authority) + "/oauth2/v2.0/logout?post_logout_redirect_uri=" + Uri.EscapeDataString(postLogoutRedirectUri);
        }

        public sealed class SignInResult
        {
            public string Email;
            public string TenantId;
            public string ObjectId;
            public string Name;
            public string ReturnUrl;
        }

        public static async Task<SignInResult> CompleteSignInAsync(HttpContextBase context, string code, string state)
        {
            var session = context.Session;
            var expectedState = session[SessionState] as string;
            var nonce = session[SessionNonce] as string;
            var verifier = session[SessionVerifier] as string;
            var returnUrl = session[SessionReturn] as string;
            session.Remove(SessionState); session.Remove(SessionNonce); session.Remove(SessionVerifier); session.Remove(SessionReturn);
            if (string.IsNullOrEmpty(expectedState) || !FixedEquals(expectedState, state ?? ""))
                throw new EntraException("The sign-in request expired or did not start here. Please try again.");
            if (string.IsNullOrEmpty(code)) throw new EntraException("Microsoft did not return an authorization code.");

            var token = await RequestTokenAsync(SignInAuthority, new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", RedirectUri(context.Request) },
                { "code_verifier", verifier },
                { "scope", DelegatedScopes }
            });

            var claims = ReadJwtPayload((string)token["id_token"]);
            var tid = ((string)claims["tid"] ?? "").ToLowerInvariant();
            if (!AllowedTenants.Contains(tid)) throw new EntraException("Your organisation is not allowed to sign in to IVT.");
            if ((string)claims["iss"] != "https://login.microsoftonline.com/" + tid + "/v2.0") throw new EntraException("Unexpected token issuer.");
            if (!string.Equals((string)claims["aud"], ClientId, StringComparison.OrdinalIgnoreCase)) throw new EntraException("Unexpected token audience.");
            if (!FixedEquals((string)claims["nonce"] ?? "", nonce ?? "")) throw new EntraException("Sign-in nonce mismatch. Please try again.");
            var exp = (long?)claims["exp"] ?? 0;
            if (DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow.AddMinutes(-5)) throw new EntraException("The sign-in token has expired.");

            // Keep the refresh token server-side (in-proc session) for "send as signed-in user".
            session[SessionRefreshToken] = (string)token["refresh_token"];
            session[SessionTenant] = tid;

            return new SignInResult
            {
                Email = (string)claims["preferred_username"] ?? (string)claims["email"],
                TenantId = tid,
                ObjectId = (string)claims["oid"],
                Name = (string)claims["name"],
                ReturnUrl = returnUrl
            };
        }

        internal static async Task<JObject> RequestTokenAsync(string tenant, Dictionary<string, string> form)
        {
            if (string.IsNullOrEmpty(ClientSecret)) throw new EntraException("IVT_ENTRA_CLIENT_SECRET is not configured.");
            form["client_id"] = ClientId;
            form["client_secret"] = ClientSecret;
            using (var response = await Http.PostAsync("https://login.microsoftonline.com/" + Uri.EscapeDataString(tenant) + "/oauth2/v2.0/token",
                new FormUrlEncodedContent(form)).ConfigureAwait(false))
            {
                var body = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (!response.IsSuccessStatusCode)
                    throw new EntraException("Microsoft token request failed: " + (string)body["error"] + " - " + FirstLine((string)body["error_description"]));
                return body;
            }
        }

        // ---- IVT session cookie for Entra users ----

        public static void IssueCookie(HttpContextBase context, string email)
        {
            var ticket = new FormsAuthenticationTicket(1, email, DateTime.Now, DateTime.Now.AddHours(10), false, AuthType);
            context.Response.Cookies.Add(new HttpCookie(CookieName, FormsAuthentication.Encrypt(ticket))
            {
                HttpOnly = true,
                Secure = context.Request.IsSecureConnection,
                SameSite = SameSiteMode.Lax,
                Path = context.Request.ApplicationPath
            });
        }

        public static void ClearCookie(HttpContextBase context)
        {
            context.Response.Cookies.Add(new HttpCookie(CookieName, "") { Expires = DateTime.Now.AddDays(-1), Path = context.Request.ApplicationPath, HttpOnly = true });
            context.Session.Remove(SessionRefreshToken);
            context.Session.Remove(SessionTenant);
        }

        // Called from Global.asax: a valid Entra cookie replaces the Windows principal, so UserIdentity.CurrentLogin()
        // (used throughout IVT) resolves the Entra email through IVT_USER_EMAILS to the existing IVT LoginName.
        public static void ApplyPrincipal(HttpContext context)
        {
            var cookie = context.Request.Cookies[CookieName];
            if (cookie == null || string.IsNullOrEmpty(cookie.Value) || !Enabled) return;
            FormsAuthenticationTicket ticket;
            try { ticket = FormsAuthentication.Decrypt(cookie.Value); }
            catch (Exception) { return; }
            if (ticket == null || ticket.Expired || ticket.UserData != AuthType || !UserIdentity.IsCompanyEmail(ticket.Name)) return;
            context.User = new GenericPrincipal(new GenericIdentity(ticket.Name, AuthType), new string[0]);
            // Sliding expiry: renew once more than an hour has passed, so active users are never signed out mid-day.
            if (DateTime.Now - ticket.IssueDate > TimeSpan.FromHours(1) && context.Request.HttpMethod == "GET")
                IssueCookie(new HttpContextWrapper(context), ticket.Name);
        }

        public static bool IsEntraUser(HttpContextBase context)
        {
            return context.User != null && context.User.Identity != null && context.User.Identity.AuthenticationType == AuthType;
        }

        // ---- helpers ----

        private static JObject ReadJwtPayload(string jwt)
        {
            var parts = (jwt ?? "").Split('.');
            if (parts.Length != 3) throw new EntraException("Microsoft did not return a valid ID token.");
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        }

        private static string RandomToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool FixedEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static bool IsLocalUrl(string url)
        {
            return !string.IsNullOrEmpty(url) && url.StartsWith("/") && !url.StartsWith("//") && !url.StartsWith("/\\");
        }

        internal static string FirstLine(string text)
        {
            return (text ?? "").Split('\r', '\n')[0];
        }
    }

    public class EntraException : Exception
    {
        public EntraException(string message) : base(message) { }
    }
}
