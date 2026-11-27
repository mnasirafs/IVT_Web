using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web.Hosting;

namespace AFSDataGateway.Services.IvtAdmin
{
    // The IVT database the operator is working on. Resolved on the server from a fixed route value;
    // connection strings and database names never come from requests.
    public enum IvtEnvironment { Production, Test }

    public static class IvtEnvironments
    {
        public static bool TryParse(string value, out IvtEnvironment environment)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "production": case "prod": environment = IvtEnvironment.Production; return true;
                case "test": environment = IvtEnvironment.Test; return true;
                default: environment = IvtEnvironment.Production; return false;
            }
        }

        public static string Key(this IvtEnvironment environment) { return environment == IvtEnvironment.Test ? "test" : "production"; }
        public static string Label(this IvtEnvironment environment) { return environment == IvtEnvironment.Test ? "TEST (IVT_Test)" : "PRODUCTION (IVT)"; }
    }

    // Settings for the IVT Administration module, loaded from a git-ignored JSON file
    // (App_Data\ivt-admin.settings.json, or the path in appSetting IVT_ADMIN_SETTINGS_PATH).
    // There are no built-in connection defaults: a missing IVT connection is a configuration error, never a fallback.
    public sealed class IvtAdminSettings
    {
        public ConnectionSettings Connections { get; set; } = new ConnectionSettings();
        public int SqlCommandTimeoutSeconds { get; set; } = 900;
        public string BusinessTimeZone { get; set; } = "AUS Eastern Standard Time";
        public JobSettings Jobs { get; set; } = new JobSettings();
        // Jobs that must be idle before invoice-week preparation, kickoff, rollback or an administrative load.
        public List<string> ConflictingJobs { get; set; } = new List<string>
            { "IVT_Engine", "IVT_Engine_Silent", "IVT_Engine_Test_Run", "IVT_Engine_Execs_Reports", "IVT_REF_DATA_LOAD", "IVT_Hold_Release_Check" };
        // Legacy desktop behaviour: a production or silent run also prepares IVT_Test for the same invoice week (IVT first, then IVT_Test).
        public bool ProductionRunPreparesTest { get; set; } = true;
        // Legacy desktop behaviour: a production run also attempted the Azure sequence. Off by default; Azure is a separate action.
        public bool ProductionRunMirrorsToAzure { get; set; }
        // After sp_start_job, wait up to this long to observe the job executing before releasing the start lock.
        public int JobStartObserveSeconds { get; set; } = 20;
        // A submission newer than this that has not been observed running blocks another start (ambiguous state).
        public int RecentSubmissionBlockMinutes { get; set; } = 10;
        public PathSettings Paths { get; set; } = new PathSettings();
        public PortalSettings Portal { get; set; } = new PortalSettings();
        public LegacyApiSettings LegacyReportApi { get; set; } = new LegacyApiSettings();
        public ProcessSettings TestNotifier { get; set; } = new ProcessSettings();
        public CommentSettings Comments { get; set; } = new CommentSettings();
        public AccessSettings Access { get; set; } = new AccessSettings();
        public AzureSettings Azure { get; set; } = new AzureSettings();
        public SmtpSettings Smtp { get; set; } = new SmtpSettings();
        public int MaxUploadBytes { get; set; } = 20 * 1024 * 1024;

        // Comment notifications. When host is empty the application's existing IVT mail transport is used.
        public sealed class SmtpSettings
        {
            public string Host { get; set; }
            public int Port { get; set; } = 587;
            public bool EnableSsl { get; set; } = true;
            public string Sender { get; set; }
            public string UserName { get; set; }
            public string PasswordEnvironmentVariable { get; set; } = "IVT_ADMIN_SMTP_PASSWORD";
        }

        public sealed class ConnectionSettings
        {
            public string Production { get; set; }
            public string Test { get; set; }
            public string Msdb { get; set; }
            public string Audit { get; set; }
            public string AzureIvt { get; set; }
        }

        public sealed class JobSettings
        {
            public string ProductionEngine { get; set; } = "IVT_Engine";
            public string SilentEngine { get; set; } = "IVT_Engine_Silent";
            public string TestEngine { get; set; } = "IVT_Engine_Test_Run";
            public string ReferenceDataLoad { get; set; } = "IVT_REF_DATA_LOAD";
            public string AdminFileLoad { get; set; } = "IVT_Hold_Release_Check";
        }

        public sealed class PathSettings
        {
            public string LandingRoot { get; set; }
            public string Buy { get; set; }
            public string Sell { get; set; }
            public string ReferenceData { get; set; }
            public string AdminLoadRoot { get; set; }
            public string HoldConnotes { get; set; }
            public string ReleaseConnotes { get; set; }
            public string BmsCheck { get; set; }
            public string OutputRoot { get; set; }
            public string TestRunOutput { get; set; }
            public Dictionary<string, string> Exports { get; set; } = new Dictionary<string, string>();
            // Unverified: the SSIS package's flat-file names are not exposed as parameters. Restrict when confirmed.
            public string ReferenceDataFilePattern { get; set; } = "*.csv";
        }

        public sealed class PortalSettings
        {
            public string Production { get; set; }
            public string Test { get; set; }
        }

        public sealed class LegacyApiSettings
        {
            public string BaseUrl { get; set; }
            public int TimeoutSeconds { get; set; } = 300;
        }

        public sealed class ProcessSettings
        {
            public bool Enabled { get; set; }
            public string Executable { get; set; }
            public string Arguments { get; set; }
            public string WorkingDirectory { get; set; }
            public int TimeoutSeconds { get; set; } = 900;
        }

        public sealed class CommentSettings
        {
            public string InsertProcedure { get; set; } = "dbo.sp_Insert_ConsignmentLogBook";
            public string LogType { get; set; } = "Hold";
            // Operator email: explicit map first, then login@EmailDomain (legacy convention) when a domain is configured.
            public Dictionary<string, string> EmailMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string EmailDomain { get; set; }
        }

        public sealed class AccessSettings
        {
            // IvtRead: IVT administrators always; additionally these logins (e.g. "JSMITH") or Windows groups.
            public List<string> ReaderLogins { get; set; } = new List<string>();
            public List<string> ReaderGroups { get; set; } = new List<string>();
        }

        public sealed class AzureSettings
        {
            public bool Enabled { get; set; }
            public string StorageAccountUrl { get; set; }
            public string Container { get; set; } = "ivt";
            public string BuyPrefix { get; set; } = "Sapphire/Buy/";
            public string SellPrefix { get; set; } = "Sapphire/Sell/";
            public string TenantId { get; set; }
            public string ClientId { get; set; }
            // Name of the environment variable holding the client secret; empty = use managed identity.
            public string ClientSecretEnvironmentVariable { get; set; } = "IVT_AZURE_CLIENT_SECRET";
            public string SubscriptionId { get; set; }
            public string ResourceGroup { get; set; }
            public string Factory { get; set; }
            public string Pipeline { get; set; } = "PL_Run_IVT_Engine";
        }

        public string ConnectionFor(IvtEnvironment environment)
        {
            var value = environment == IvtEnvironment.Test ? Connections.Test : Connections.Production;
            if (string.IsNullOrWhiteSpace(value))
                throw new IvtConfigurationException("The " + environment.Label() + " IVT connection is not configured (connections." + environment.Key() + ").");
            return value;
        }

        public string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new IvtConfigurationException("Setting '" + name + "' is not configured.");
            return value;
        }

        public TimeZoneInfo TimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZone); }
            catch (Exception) { throw new IvtConfigurationException("businessTimeZone '" + BusinessTimeZone + "' is not a valid Windows time zone id."); }
        }

        // ---- loading ----
        private static readonly object Gate = new object();
        private static IvtAdminSettings cached;
        private static DateTime cachedStamp;
        private static string cachedPath;

        public static string SettingsPath()
        {
            var configured = ConfigurationManager.AppSettings["IVT_ADMIN_SETTINGS_PATH"];
            if (!string.IsNullOrWhiteSpace(configured) && configured.Trim() != "SET_ON_SERVER") return configured.Trim();
            var root = HostingEnvironment.MapPath("~/App_Data") ?? AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(root, "ivt-admin.settings.json");
        }

        // Reloads automatically when the file changes. Throws IvtConfigurationException when missing or invalid.
        public static IvtAdminSettings Load()
        {
            var path = SettingsPath();
            lock (Gate)
            {
                if (!File.Exists(path))
                    throw new IvtConfigurationException("IVT Administration is not configured: settings file not found (" + Path.GetFileName(path) +
                        "). Copy ivt-admin.settings.example.json and fill in the values.");
                var stamp = File.GetLastWriteTimeUtc(path);
                if (cached != null && cachedPath == path && cachedStamp == stamp) return cached;
                IvtAdminSettings settings;
                try { settings = Parse(File.ReadAllText(path)); }
                catch (JsonException ex) { throw new IvtConfigurationException("The IVT Administration settings file is not valid JSON: " + ex.Message); }
                cached = settings; cachedStamp = stamp; cachedPath = path;
                return settings;
            }
        }

        public static IvtAdminSettings Parse(string json)
        {
            var settings = JsonConvert.DeserializeObject<IvtAdminSettings>(json, new JsonSerializerSettings
                { MissingMemberHandling = MissingMemberHandling.Error, ObjectCreationHandling = ObjectCreationHandling.Replace });
            if (settings == null) throw new IvtConfigurationException("The settings file is empty.");
            if (settings.Comments.EmailMap == null) settings.Comments.EmailMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            else settings.Comments.EmailMap = new Dictionary<string, string>(settings.Comments.EmailMap, StringComparer.OrdinalIgnoreCase);
            if (settings.SqlCommandTimeoutSeconds < 30) throw new IvtConfigurationException("sqlCommandTimeoutSeconds must be at least 30.");
            return settings;
        }

        // Names of settings that are not filled in, for the configuration status page (values are never shown).
        public IEnumerable<string> MissingSettings()
        {
            var checks = new Dictionary<string, string>
            {
                { "connections.production", Connections.Production }, { "connections.test", Connections.Test },
                { "connections.msdb", Connections.Msdb }, { "connections.audit", Connections.Audit },
                { "paths.landingRoot", Paths.LandingRoot }, { "paths.holdConnotes", Paths.HoldConnotes },
                { "paths.releaseConnotes", Paths.ReleaseConnotes }, { "paths.bmsCheck", Paths.BmsCheck },
                { "paths.referenceData", Paths.ReferenceData }, { "legacyReportApi.baseUrl", LegacyReportApi.BaseUrl },
            };
            return checks.Where(x => string.IsNullOrWhiteSpace(x.Value) || x.Value.Contains("<")).Select(x => x.Key);
        }
    }

    public sealed class IvtConfigurationException : Exception
    {
        public IvtConfigurationException(string message) : base(message) { }
    }

    // A business-rule or validation failure whose message is safe and useful to show to the operator.
    public sealed class IvtOperationException : Exception
    {
        public IvtOperationException(string message) : base(message) { }
    }
}
