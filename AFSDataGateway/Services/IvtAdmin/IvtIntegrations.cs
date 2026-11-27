using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    // ---------------------------------------------------------------- landing folders
    public enum IvtLandingFolder { Landing, Buy, Sell, ReferenceData, HoldConnotes, ReleaseConnotes, BmsCheck, AdminLoadRoot, TestRunOutput }

    public sealed class IvtLandingFile
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
    }

    public interface ILandingStore
    {
        string PathOf(IvtLandingFolder folder);
        IReadOnlyList<IvtLandingFile> List(IvtLandingFolder folder, string pattern);
        // Publishes a validated upload atomically: written under a temporary name the loaders ignore, then renamed.
        void Publish(IvtLandingFolder folder, string fileName, byte[] content);
        Stream OpenRead(IvtLandingFolder folder, string fileName);
        // Text of loader error files (error_*.txt) in a folder, newest first.
        IReadOnlyList<KeyValuePair<string, string>> ErrorFiles(IvtLandingFolder folder);
    }

    public sealed class FileLandingStore : ILandingStore
    {
        private static readonly Regex SafeName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _.()\-]{0,150}$");
        private readonly IvtAdminSettings settings;
        public FileLandingStore(IvtAdminSettings settings) { this.settings = settings; }

        public string PathOf(IvtLandingFolder folder)
        {
            var p = settings.Paths;
            switch (folder)
            {
                case IvtLandingFolder.Landing: return settings.Require(p.LandingRoot, "paths.landingRoot");
                case IvtLandingFolder.Buy: return settings.Require(p.Buy, "paths.buy");
                case IvtLandingFolder.Sell: return settings.Require(p.Sell, "paths.sell");
                case IvtLandingFolder.ReferenceData: return settings.Require(p.ReferenceData, "paths.referenceData");
                case IvtLandingFolder.HoldConnotes: return settings.Require(p.HoldConnotes, "paths.holdConnotes");
                case IvtLandingFolder.ReleaseConnotes: return settings.Require(p.ReleaseConnotes, "paths.releaseConnotes");
                case IvtLandingFolder.BmsCheck: return settings.Require(p.BmsCheck, "paths.bmsCheck");
                case IvtLandingFolder.AdminLoadRoot: return settings.Require(p.AdminLoadRoot, "paths.adminLoadRoot");
                default: return settings.Require(p.TestRunOutput, "paths.testRunOutput");
            }
        }

        public IReadOnlyList<IvtLandingFile> List(IvtLandingFolder folder, string pattern)
        {
            var path = PathOf(folder);
            try
            {
                return new DirectoryInfo(path).GetFiles(string.IsNullOrEmpty(pattern) ? "*" : pattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(f => new IvtLandingFile { Name = f.Name, Size = f.Length, Modified = f.LastWriteTime }).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new IvtOperationException("The " + folder + " folder is not accessible from the web server (" + ex.GetType().Name +
                    "). Check the share path and that the application pool identity has access.");
            }
        }

        public static string ValidateFileName(string fileName, string requiredExtension)
        {
            var name = (fileName ?? "").Trim();
            if (name != Path.GetFileName(name) || !SafeName.IsMatch(name) || name.Contains(".."))
                throw new IvtOperationException("File names may contain only letters, digits, spaces, '_', '-', '.', '(' and ')'.");
            if (requiredExtension != null && !name.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
                throw new IvtOperationException("Only " + requiredExtension + " files are accepted.");
            return name;
        }

        public void Publish(IvtLandingFolder folder, string fileName, byte[] content)
        {
            var name = ValidateFileName(fileName, null);
            var directory = PathOf(folder);
            var final = Path.Combine(directory, name);
            // Defence in depth: the combined path must stay inside the configured folder.
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(final)).TrimEnd('\\'), Path.GetFullPath(directory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new IvtOperationException("Invalid destination.");
            if (File.Exists(final)) throw new IvtOperationException("A file named '" + name + "' is already waiting in the folder. Remove or rename it first.");
            // ".uploading" does not match the loaders' patterns (*.csv / *[.csv]), so a partial file is never consumed.
            var temp = Path.Combine(directory, name + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".uploading");
            try
            {
                File.WriteAllBytes(temp, content);
                File.Move(temp, final);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw new IvtOperationException("The file could not be written to the landing folder (" + ex.GetType().Name + "). Nothing was published.");
            }
        }

        public Stream OpenRead(IvtLandingFolder folder, string fileName)
        {
            var name = ValidateFileName(fileName, null);
            var path = Path.Combine(PathOf(folder), name);
            if (!File.Exists(path)) throw new IvtOperationException("File not found.");
            return File.OpenRead(path);
        }

        public IReadOnlyList<KeyValuePair<string, string>> ErrorFiles(IvtLandingFolder folder)
        {
            return List(folder, "error*.txt").Take(10).Select(f =>
            {
                string text;
                try { text = File.ReadAllText(Path.Combine(PathOf(folder), f.Name)); }
                catch (IOException ex) { text = "(could not read: " + ex.GetType().Name + ")"; }
                return new KeyValuePair<string, string>(f.Name + " (" + f.Modified.ToString("dd MMM HH:mm") + ")", text.Length > 4000 ? text.Substring(0, 4000) : text);
            }).ToList();
        }
    }

    // ---------------------------------------------------------------- CSV (templates and exports)
    public static class IvtCsv
    {
        // RFC 4180 parser for uploaded templates (quoted fields, embedded commas/quotes/newlines).
        public static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, any = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = false; }
                    else field.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; any = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); any = true; break;
                    case '\r': break;
                    case '\n': row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); any = false; break;
                    default: field.Append(c); any = true; break;
                }
            }
            if (quoted) throw new IvtOperationException("The CSV file has an unterminated quoted field.");
            if (any || field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }

        // Export cell: quotes as needed and neutralises spreadsheet formulas in text values.
        public static string Cell(object value)
        {
            if (value == null) return "";
            string text;
            if (value is DateTime) text = ((DateTime)value).TimeOfDay == TimeSpan.Zero ? ((DateTime)value).ToString("yyyy-MM-dd") : ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss");
            else if (value is bool) text = (bool)value ? "true" : "false";
            else if (value is IFormattable) text = ((IFormattable)value).ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            else
            {
                text = value.ToString();
                // Text that looks like a formula is prefixed; plain numbers held as text (e.g. "-12.50") are left intact.
                double number;
                if (text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0 &&
                    !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
                    text = "'" + text;
            }
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }

        public static byte[] Build(IvtTable table, IEnumerable<string> preamble)
        {
            var sb = new StringBuilder();
            foreach (var line in preamble ?? Enumerable.Empty<string>()) sb.Append(Cell("# " + line)).Append("\r\n");
            sb.Append(string.Join(",", table.Columns.Select(c => Cell(c.Name)))).Append("\r\n");
            foreach (var row in table.Rows) sb.Append(string.Join(",", row.Select(Cell))).Append("\r\n");
            return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }
    }

    // ---------------------------------------------------------------- legacy report/notification HTTP service
    public interface ILegacyReportApi
    {
        // GET {baseUrl}{operation}; returns the JSON "result" message. Throws on HTTP failure or unexpected body.
        Task<string> CallAsync(string operation, CancellationToken cancellation);
    }

    public sealed class LegacyReportApi : ILegacyReportApi
    {
        public static readonly string[] Operations = { "attachfiles", "notifyam", "notifyexecs" };
        private readonly IvtAdminSettings settings;
        public LegacyReportApi(IvtAdminSettings settings) { this.settings = settings; }

        public async Task<string> CallAsync(string operation, CancellationToken cancellation)
        {
            if (!Operations.Contains(operation)) throw new ArgumentException("Unknown operation.");
            var baseUrl = settings.Require(settings.LegacyReportApi.BaseUrl, "legacyReportApi.baseUrl");
            // Windows credentials of the application pool identity, matching the desktop's UseDefaultCredentials.
            using (var handler = new HttpClientHandler { UseDefaultCredentials = true })
            using (var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(settings.LegacyReportApi.TimeoutSeconds) })
            using (var response = await client.GetAsync(operation, cancellation).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new IvtOperationException("The report service returned HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                JToken result;
                try { result = JObject.Parse(body)["result"]; }
                catch (Newtonsoft.Json.JsonException) { throw new IvtOperationException("The report service returned a response that is not the expected JSON."); }
                if (result == null) throw new IvtOperationException("The report service response has no 'result' message.");
                return result.ToString();
            }
        }
    }

    // ---------------------------------------------------------------- external process (test notifications)
    public sealed class IvtProcessResult
    {
        public int? ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public bool Succeeded { get { return !TimedOut && ExitCode == 0; } }
    }

    public interface IProcessRunner
    {
        Task<IvtProcessResult> RunAsync(string executable, string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellation);
    }

    public sealed class ProcessRunner : IProcessRunner
    {
        public Task<IvtProcessResult> RunAsync(string executable, string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellation)
        {
            return Task.Run(() =>
            {
                var info = new ProcessStartInfo(executable, arguments ?? "")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Path.GetDirectoryName(executable) : workingDirectory
                };
                var output = new StringBuilder(); var error = new StringBuilder();
                using (var process = new Process { StartInfo = info })
                {
                    process.OutputDataReceived += (s, e) => { if (e.Data != null && output.Length < 200000) output.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null && error.Length < 200000) error.AppendLine(e.Data); };
                    process.Start();
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    var finished = process.WaitForExit((int)timeout.TotalMilliseconds);
                    if (!finished)
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        return new IvtProcessResult { TimedOut = true, Output = output.ToString(), Error = error.ToString() };
                    }
                    process.WaitForExit();
                    return new IvtProcessResult { ExitCode = process.ExitCode, Output = output.ToString(), Error = error.ToString() };
                }
            }, cancellation);
        }
    }

    // ---------------------------------------------------------------- email
    public interface INotificationTransport
    {
        Task SendAsync(IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string html, CancellationToken cancellation);
    }

    // IVT Administration notifications use the shared IVT mail transport (Microsoft Graph from AFSReports).
    public sealed class GraphNotificationTransport : INotificationTransport
    {
        public async Task SendAsync(IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string html, CancellationToken cancellation)
        {
            if (to == null || to.Count == 0) throw new IvtOperationException("No notification recipients are configured.");
            await global::AFSDataGateway.Controllers.EmailNotification.SendHtmlAsync(to, cc, subject, html).ConfigureAwait(false);
        }
    }

    public static class IvtEmail
    {
        private static readonly Regex Address = new Regex(@"^[^@\s;,]+@[^@\s;,]+\.[^@\s;,]+$");

        // Contact fields may hold several addresses separated by ';' or ','.
        public static List<string> SplitAddresses(string value)
        {
            return (value ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<string> InvalidAddresses(string value) { return SplitAddresses(value).Where(a => !Address.IsMatch(a)).ToList(); }
        public static bool IsValid(string address) { return Address.IsMatch(address ?? ""); }
    }
}
