using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AFSDataGateway.Services.Entra
{
    // Microsoft Graph sendMail. Delegated = as the signed-in Entra user; app-only = from an approved sender mailbox
    // (e.g. AFSReports) with no user present. Not yet wired into Helper.cs; SMTP stays the live transport for now.
    public static class GraphMail
    {
        // Mailboxes the app may send from with app-only tokens. Exchange should enforce the same list.
        public static string[] ApprovedSenders
        {
            get
            {
                var value = EntraAuth.Setting("IVT_GRAPH_SENDERS");
                if (value.Length == 0) value = "AFSReports@afslogistics.com.au";
                return value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            }
        }

        public static async Task SendAsSignedInUserAsync(HttpContextBase context, IEnumerable<string> to, string subject, string htmlBody)
        {
            var refresh = context.Session[EntraAuth.SessionRefreshToken] as string;
            var tenant = context.Session[EntraAuth.SessionTenant] as string;
            if (string.IsNullOrEmpty(refresh) || string.IsNullOrEmpty(tenant))
                throw new EntraException("Sign in with Microsoft again to send email as yourself.");
            var token = await EntraAuth.RequestTokenAsync(tenant, new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refresh },
                { "scope", EntraAuth.DelegatedScopes }
            });
            if (token["refresh_token"] != null) context.Session[EntraAuth.SessionRefreshToken] = (string)token["refresh_token"];
            await PostAsync("https://graph.microsoft.com/v1.0/me/sendMail", (string)token["access_token"], to, subject, htmlBody);
        }

        // App-only Graph sending needs only the app registration settings and secret, not interactive sign-in.
        public static bool AppSendConfigured
        {
            get
            {
                return !string.IsNullOrEmpty(EntraAuth.ClientId) && !string.IsNullOrEmpty(EntraAuth.HomeTenant) &&
                       !string.IsNullOrEmpty(EntraAuth.Setting("IVT_ENTRA_CLIENT_SECRET"));
            }
        }

        public sealed class MailAttachment
        {
            public string Name;
            public string ContentType;
            public byte[] Content;
        }

        public static Task SendAsAppAsync(string sender, IEnumerable<string> to, string subject, string htmlBody)
        {
            return SendAsAppAsync(sender, to, null, subject, htmlBody, null);
        }

        public static async Task SendAsAppAsync(string sender, IEnumerable<string> to, IEnumerable<string> cc, string subject, string htmlBody,
            IEnumerable<MailAttachment> attachments)
        {
            if (!ApprovedSenders.Contains(sender, StringComparer.OrdinalIgnoreCase))
                throw new EntraException(sender + " is not an approved IVT sender mailbox.");
            var token = await GetAppTokenAsync().ConfigureAwait(false);
            await PostAsync("https://graph.microsoft.com/v1.0/users/" + Uri.EscapeDataString(sender) + "/sendMail",
                token, to, subject, htmlBody, cc, attachments).ConfigureAwait(false);
        }

        // App-only tokens last about an hour; reuse one instead of requesting a token per email.
        private static string cachedToken;
        private static DateTime cachedTokenExpiresUtc;
        private static readonly System.Threading.SemaphoreSlim TokenLock = new System.Threading.SemaphoreSlim(1, 1);

        private static async Task<string> GetAppTokenAsync()
        {
            await TokenLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (cachedToken != null && DateTime.UtcNow < cachedTokenExpiresUtc) return cachedToken;
                var token = await EntraAuth.RequestTokenAsync(EntraAuth.HomeTenant, new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "scope", "https://graph.microsoft.com/.default" }
                }).ConfigureAwait(false);
                cachedToken = (string)token["access_token"];
                cachedTokenExpiresUtc = DateTime.UtcNow.AddSeconds(((int?)token["expires_in"] ?? 3600) - 300);
                return cachedToken;
            }
            finally { TokenLock.Release(); }
        }

        private static object[] Recipients(IEnumerable<string> addresses)
        {
            return (addresses ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => (object)new { emailAddress = new { address = x } }).ToArray();
        }

        private static async Task PostAsync(string url, string accessToken, IEnumerable<string> to, string subject, string htmlBody,
            IEnumerable<string> cc = null, IEnumerable<MailAttachment> attachments = null)
        {
            var recipients = Recipients(to);
            if (recipients.Length == 0) throw new EntraException("No recipients.");
            var files = (attachments ?? Enumerable.Empty<MailAttachment>()).Select(a => new Dictionary<string, object>
            {
                { "@odata.type", "#microsoft.graph.fileAttachment" },
                { "name", a.Name },
                { "contentType", string.IsNullOrEmpty(a.ContentType) ? "application/octet-stream" : a.ContentType },
                { "contentBytes", Convert.ToBase64String(a.Content) }
            }).ToArray();
            var payload = JsonConvert.SerializeObject(new
            {
                message = new
                {
                    subject,
                    body = new { contentType = "HTML", content = htmlBody },
                    toRecipients = recipients,
                    ccRecipients = Recipients(cc),
                    attachments = files
                },
                saveToSentItems = true
            });
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using (var response = await EntraAuth.Http.SendAsync(request).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode) return;
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string detail;
                    try { detail = (string)JObject.Parse(body)["error"]["message"]; } catch (Exception) { detail = body; }
                    throw new EntraException("Graph sendMail failed (" + (int)response.StatusCode + "): " + EntraAuth.FirstLine(detail));
                }
            }
        }
    }
}
