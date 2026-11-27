using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AFSDataGateway.Services.ML
{
    public sealed class MLRunInfo
    {
        public string RunId { get; set; }
        public string RunType { get; set; }
        public DateTime? ScoringWeek { get; set; }
        public string Status { get; set; }
        public bool IsDryRun { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string ModelVersion { get; set; }
        public string ErrorText { get; set; }
        public string MetricsJson { get; set; }
    }

    public sealed class MLScoringStatus
    {
        public bool Configured { get; set; }
        public List<string> ConfigProblems { get; set; } = new List<string>();
        public DateTime? CurrentWeek { get; set; }
        public List<MLRunInfo> Runs { get; set; } = new List<MLRunInfo>();
        public MLRunInfo Running { get; set; }
        public string LatestLogName { get; set; }
        public string LatestLogTail { get; set; }
        public int? ReviewRowsCurrentWeek { get; set; }
        public string Message { get; set; }
        public bool MessageIsError { get; set; }
    }

    // Starts the Python ML pipeline (ivt-ml) on the web server in the background and reports its status from ML.Run.
    // Settings (Web.config appSettings or environment variables):
    //   IVT_ML_PYTHON         full path to python.exe inside the ivt-ml virtual environment
    //   IVT_ML_WORKDIR        the ivt-ml folder (contains src\ and output\)
    //   IVT_ML_ARTIFACT_DIR   model artifact folder (registry.json + models)
    //   IVT_ML_CONFIG_VERSION ML.ControlParameter version (default dev-defaults)
    //   IVT_ML_ODBC_DRIVER    ODBC driver name (default "ODBC Driver 17 for SQL Server")
    public sealed class MLJobRunner
    {
        private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);
        private readonly IVTEntities db;

        public MLJobRunner(IVTEntities db)
        {
            this.db = db;
            db.Database.CommandTimeout = 60;
        }

        private static string Setting(string name, string fallback = null)
        {
            foreach (var value in new[] { Environment.GetEnvironmentVariable(name), ConfigurationManager.AppSettings[name] })
                if (!string.IsNullOrWhiteSpace(value) && value.Trim() != "SET_ON_SERVER") return value.Trim();
            return fallback;
        }

        public MLScoringStatus Status()
        {
            var status = new MLScoringStatus();
            var python = Setting("IVT_ML_PYTHON");
            var workdir = Setting("IVT_ML_WORKDIR");
            var artifacts = Setting("IVT_ML_ARTIFACT_DIR");
            if (python == null || !File.Exists(python)) status.ConfigProblems.Add("IVT_ML_PYTHON is not set or python.exe was not found on the server.");
            if (workdir == null || !Directory.Exists(workdir)) status.ConfigProblems.Add("IVT_ML_WORKDIR is not set or the ivt-ml folder was not found on the server.");
            if (artifacts == null || !File.Exists(Path.Combine(artifacts, "registry.json"))) status.ConfigProblems.Add("IVT_ML_ARTIFACT_DIR is not set or contains no trained models (registry.json).");
            status.Configured = status.ConfigProblems.Count == 0;

            status.CurrentWeek = db.Database.SqlQuery<DateTime?>("SELECT MAX(CurrentWeek) FROM dbo.Sell_Side WITH (NOLOCK)").FirstOrDefault();
            if (db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID('ML.Run') IS NULL THEN 0 ELSE 1 END").First() == 1)
            {
                status.Runs = db.Database.SqlQuery<MLRunInfo>(
                    "SELECT TOP 12 RunId, RunType, ScoringWeek, Status, IsDryRun, StartedAt, CompletedAt, ModelVersion, ErrorText, MetricsJson FROM ML.Run ORDER BY StartedAt DESC").ToList();
                status.Running = status.Runs.FirstOrDefault(r => r.RunType == "SCORE" && r.Status == "STARTED" && DateTime.UtcNow - r.StartedAt < StaleAfter);
                if (status.CurrentWeek.HasValue && db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID('ML.vReviewCandidates') IS NULL THEN 0 ELSE 1 END").First() == 1)
                    status.ReviewRowsCurrentWeek = db.Database.SqlQuery<int>("SELECT COUNT(*) FROM ML.vReviewCandidates WHERE ScoringWeek = @w",
                        new SqlParameter("@w", status.CurrentWeek.Value)).First();
            }
            if (workdir != null)
            {
                var logDir = new DirectoryInfo(Path.Combine(workdir, "output", "web"));
                var latest = logDir.Exists ? logDir.GetFiles("score_*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
                if (latest != null)
                {
                    status.LatestLogName = latest.Name;
                    status.LatestLogTail = Tail(latest.FullName, 25);
                }
            }
            return status;
        }

        // Returns an error message, or null when the run was started.
        public string StartScoring(DateTime week, bool allowCandidate, string requestedBy)
        {
            var status = Status();
            if (!status.Configured) return "ML scoring is not configured on this server: " + string.Join(" ", status.ConfigProblems);
            if (status.Running != null) return "A scoring run is already in progress (" + status.Running.RunId + "). Wait for it to finish.";
            var available = db.Database.SqlQuery<int>("SELECT COUNT(*) FROM dbo.Sell_Side WITH (NOLOCK) WHERE CurrentWeek = @w", new SqlParameter("@w", week.Date)).First()
                          + db.Database.SqlQuery<int>("SELECT COUNT(*) FROM (SELECT TOP 1 1 AS x FROM dbo.Sell_Side_History WITH (NOLOCK) WHERE CurrentWeek = @w) h", new SqlParameter("@w", week.Date)).First();
            if (available == 0) return "There is no trading data for the week of " + week.ToString("dd MMM yyyy") + ".";

            var workdir = Setting("IVT_ML_WORKDIR");
            var logDir = Path.Combine(workdir, "output", "web");
            Directory.CreateDirectory(logDir);
            var log = Path.Combine(logDir, "score_" + week.ToString("yyyyMMdd") + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".log");
            var args = "-m ivt_ml.cli score --current-week " + week.ToString("yyyy-MM-dd") + " --store sql --config-version \"" + Setting("IVT_ML_CONFIG_VERSION", "dev-defaults") +
                       "\" --output-dir \"" + Path.Combine(workdir, "output", "web") + "\" --artifact-dir \"" + Setting("IVT_ML_ARTIFACT_DIR") + "\"" + (allowCandidate ? " --allow-candidate" : "");
            var info = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                // cmd only redirects output to the log file; the command line contains no credentials.
                Arguments = "/c \"\"" + Setting("IVT_ML_PYTHON") + "\" " + args + " >> \"" + log + "\" 2>&1\"",  // append: keeps the "Requested by" header
                WorkingDirectory = workdir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // The database connection is handed over in memory only (never written to disk or the command line).
            info.EnvironmentVariables["IVT_ML_DB_CONN"] = OdbcConnectionString();
            info.EnvironmentVariables["PYTHONWARNINGS"] = "ignore";
            info.EnvironmentVariables["IVT_ML_LOG_LEVEL"] = "INFO";
            info.EnvironmentVariables["IVT_ML_REQUESTED_BY"] = requestedBy;
            File.WriteAllText(log, "Requested by " + requestedBy + " at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " for week " + week.ToString("yyyy-MM-dd") +
                                   (allowCandidate ? " (candidate models allowed, shadow)" : "") + Environment.NewLine);
            using (Process.Start(info)) { }
            Trace.TraceInformation("ML scoring started by {0} for week {1}", requestedBy, week.ToString("yyyy-MM-dd"));
            return null;
        }

        private string OdbcConnectionString()
        {
            // Read from configuration: an opened EF connection no longer exposes the password.
            var ef = new System.Data.Entity.Core.EntityClient.EntityConnectionStringBuilder(ConfigurationManager.ConnectionStrings["IVTEntities"].ConnectionString);
            var b = new SqlConnectionStringBuilder(ef.ProviderConnectionString);
            var driver = Setting("IVT_ML_ODBC_DRIVER", "ODBC Driver 17 for SQL Server");
            var auth = b.IntegratedSecurity ? "Trusted_Connection=yes;" : "UID=" + b.UserID + ";PWD={" + b.Password.Replace("}", "}}") + "};";
            return "Driver={" + driver + "};Server=" + b.DataSource + ";Database=" + b.InitialCatalog + ";" + auth;
        }

        private static string Tail(string path, int lines)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    var all = reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)));
                }
            }
            catch (IOException) { return "(log is being written)"; }
        }
    }
}
