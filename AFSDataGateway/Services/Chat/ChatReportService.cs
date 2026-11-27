using AFSDataGateway.Controllers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace AFSDataGateway.Services.Chat
{
    // Builds and sends "email me" reports from the IVT assistant. Reports always go to the requesting user's own
    // company mailbox, using the same mapping as IVT notifications: recipients cannot be chosen,
    // so permitted data cannot be sent to anyone else.
    public static class ChatReportService
    {

        public static string RecipientFor(ChatAccessScope scope)
        {
            // The person who is signed in (an admin impersonating an AM still receives it in their own mailbox).
            return UserIdentity.EmailFor(scope.ActorIdentity ?? scope.Actor);
        }

        // Every table (array of objects) found in a tool result, keyed by a readable name.
        public static List<KeyValuePair<string, JArray>> Tables(JToken result, string prefix)
        {
            var tables = new List<KeyValuePair<string, JArray>>();
            Collect(result, prefix, tables);
            return tables;
        }

        private static void Collect(JToken token, string path, List<KeyValuePair<string, JArray>> tables)
        {
            var obj = token as JObject;
            if (obj == null) return;
            foreach (var p in obj.Properties())
            {
                if (p.Name == "appliedFilters") continue;  // described in the email body, not a data table
                var arr = p.Value as JArray;
                if (arr != null && arr.Count > 0 && arr.All(x => x is JObject))
                    tables.Add(new KeyValuePair<string, JArray>(path + "_" + p.Name, arr));
                else if (p.Value is JObject)
                    Collect(p.Value, path, tables);
            }
        }

        public static byte[] Csv(JArray rows)
        {
            var columns = new List<string>();
            foreach (JObject row in rows)
                foreach (var p in row.Properties())
                    if (!columns.Contains(p.Name)) columns.Add(p.Name);
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", columns.Select(Escape)));
            foreach (JObject row in rows)
                sb.AppendLine(string.Join(",", columns.Select(c => Escape(Cell(row[c])))));
            return new UTF8Encoding(true).GetBytes(sb.ToString());  // BOM so Excel opens UTF-8 correctly
        }

        private static string Cell(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return "";
            if (value.Type == JTokenType.Array) return string.Join("; ", value.Select(v => v.ToString()));
            return value.Type == JTokenType.Object ? value.ToString(Newtonsoft.Json.Formatting.None) : value.ToString();
        }

        private static string Escape(string value)
        {
            value = value ?? "";
            // CSV/formula injection guard: a leading = + - @ would be executed by Excel.
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0 && !IsNumber(value)) value = "'" + value;
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static bool IsNumber(string value)
        {
            decimal d;
            return decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
        }

        public static async Task SendAsync(string to, string subject, string introHtml, List<KeyValuePair<string, JArray>> tables, string scopeLabel)
        {
            var body = new StringBuilder();
            body.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2937\">");
            body.Append(introHtml);
            body.Append("<p style=\"color:#6b7280;font-size:12px\">Scope: " + HttpUtility.HtmlEncode(scopeLabel) + ". Generated " + DateTime.Now.ToString("dd MMM yyyy HH:mm") +
                        " by the IVT assistant at your request. ");
            body.Append(tables.Count == 0 ? "No rows matched." : "Attached: " + string.Join(", ", tables.Select(t => HttpUtility.HtmlEncode(t.Key) + ".csv (" + t.Value.Count + " rows)")) + ".");
            body.Append(" AI-written summaries can be imperfect; the attached figures come directly from IVT.</p></div>");
            var attachments = tables.Select(t => new Attachment(new MemoryStream(Csv(t.Value)), Safe(t.Key) + ".csv", "text/csv")).ToList();
            try { await EmailNotification.SendToUserAsync(to, subject, body.ToString(), attachments); }
            finally { foreach (var a in attachments) a.Dispose(); }
        }

        private static string Safe(string name)
        {
            var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
            return cleaned.Length > 60 ? cleaned.Substring(0, 60) : cleaned;
        }

        public static string Html(string text)
        {
            return HttpUtility.HtmlEncode(text ?? "").Replace("\n", "<br/>");
        }
    }
}
