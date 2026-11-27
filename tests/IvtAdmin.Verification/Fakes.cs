using AFSDataGateway.Services.IvtAdmin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IvtAdmin.Verification
{
    // Records every side effect so tests can assert exactly what would have reached SQL Server, SQL Agent, mail, Azure, files.
    public sealed class CallLog
    {
        public readonly List<string> Events = new List<string>();
        public void Add(string e) { lock (Events) Events.Add(e); }
        public int IndexOf(string prefix) { return Events.FindIndex(e => e.StartsWith(prefix, StringComparison.Ordinal)); }
        public int Count(string prefix) { return Events.Count(e => e.StartsWith(prefix, StringComparison.Ordinal)); }
    }

    public sealed class FakeDb : IIvtDatabase
    {
        private readonly CallLog log;
        public readonly List<Tuple<IvtDbTarget, string, List<IvtParam>>> Executed = new List<Tuple<IvtDbTarget, string, List<IvtParam>>>();
        public readonly Dictionary<string, Func<IvtDbTarget, List<IvtParam>, IvtTable>> Queries = new Dictionary<string, Func<IvtDbTarget, List<IvtParam>, IvtTable>>(StringComparer.OrdinalIgnoreCase);
        public Func<IvtDbTarget, string, List<IvtParam>, Exception> FailExecute = (t, p, ps) => null;
        public FakeDb(CallLog log) { this.log = log; }

        public Task<IvtTable> QueryProcedureAsync(IvtDbTarget target, string procedure, IEnumerable<IvtParam> parameters, CancellationToken cancellation)
        {
            var list = parameters.ToList();
            log.Add("QUERY " + target + " " + procedure);
            Func<IvtDbTarget, List<IvtParam>, IvtTable> handler;
            if (Queries.TryGetValue(procedure, out handler)) return Task.FromResult(handler(target, list));
            // Insert-style procedures queried for their result (e.g. sp_Insert_ConsignmentLogBook) count as executions.
            Executed.Add(Tuple.Create(target, procedure, list));
            log.Add("EXEC " + target + " " + procedure);
            var ex = FailExecute(target, procedure, list);
            if (ex != null) throw ex;
            return Task.FromResult(new IvtTable());
        }

        public Task<int> ExecuteProcedureAsync(IvtDbTarget target, string procedure, IEnumerable<IvtParam> parameters, CancellationToken cancellation)
        {
            var list = parameters.ToList();
            Executed.Add(Tuple.Create(target, procedure, list));
            log.Add("EXEC " + target + " " + procedure);
            var ex = FailExecute(target, procedure, list);
            if (ex != null) throw ex;
            return Task.FromResult(1);
        }

        public Task<IvtTable> QueryTextAsync(IvtDbTarget target, string sql, IEnumerable<IvtParam> parameters, CancellationToken cancellation)
        {
            log.Add("SQLTEXT " + target);
            Func<IvtDbTarget, List<IvtParam>, IvtTable> handler;
            var key = sql.Contains("vConsignmentForReview") ? "#release" : sql.Contains("IVT_Run_Control") ? "#contacts" : "#text";
            if (Queries.TryGetValue(key, out handler)) return Task.FromResult(handler(target, parameters.ToList()));
            return Task.FromResult(new IvtTable());
        }

        public static IvtTable Table(string[] columns, params object[][] rows)
        {
            var t = new IvtTable();
            foreach (var c in columns) t.Columns.Add(new IvtColumn { Name = c });
            foreach (var r in rows) t.Rows.Add(r);
            return t;
        }
    }

    public sealed class FakeAgent : ISqlAgent
    {
        private readonly CallLog log;
        public readonly Dictionary<string, IvtJobStatus> Jobs = new Dictionary<string, IvtJobStatus>(StringComparer.OrdinalIgnoreCase);
        public Exception StartFailure;
        public bool RunAfterStart = true;
        public FakeAgent(CallLog log, params string[] existing)
        {
            this.log = log;
            foreach (var j in existing) Jobs[j] = new IvtJobStatus { Name = j, Exists = true, Enabled = true, LastOutcome = "Succeeded", LastRunStart = DateTime.Now.AddDays(-1) };
        }
        public Task<IvtJobStatus> GetStatusAsync(string jobName, CancellationToken cancellation)
        {
            IvtJobStatus s;
            return Task.FromResult(Jobs.TryGetValue(jobName, out s) ? s : new IvtJobStatus { Name = jobName, Exists = false });
        }
        public Task StartAsync(string jobName, CancellationToken cancellation)
        {
            log.Add("START " + jobName);
            if (StartFailure != null) throw StartFailure;
            IvtJobStatus s;
            if (!Jobs.TryGetValue(jobName, out s) || !s.Exists) throw new IvtOperationException("SQL Agent job '" + jobName + "' was not found.");
            if (RunAfterStart) { s.IsRunning = true; s.RunningSince = DateTime.Now; s.CurrentStepId = 1; s.CurrentStepName = "Load"; }
            return Task.FromResult(0);
        }
    }

    public sealed class FakeAudit : IIvtAudit
    {
        public readonly List<IvtAuditEntry> Entries = new List<IvtAuditEntry>();
        public bool FailWrites;
        private long next = 1;
        public Task<long> WriteAsync(IvtAuditEntry entry, CancellationToken cancellation)
        {
            if (FailWrites) throw new InvalidOperationException("audit store down");
            entry.Id = next++; entry.OccurredAtUtc = DateTime.UtcNow;
            Entries.Add(new IvtAuditEntry { Id = entry.Id, CorrelationId = entry.CorrelationId, OccurredAtUtc = entry.OccurredAtUtc, Operator = entry.Operator, Environment = entry.Environment,
                Operation = entry.Operation, Target = entry.Target, AffectedCount = entry.AffectedCount, InvoiceWeek = entry.InvoiceWeek, ExternalId = entry.ExternalId, Outcome = entry.Outcome, Detail = entry.Detail });
            return Task.FromResult(entry.Id);
        }
        public Task UpdateAsync(long id, string outcome, int? affectedCount, string externalId, string detail, CancellationToken cancellation)
        {
            var e = Entries.First(x => x.Id == id);
            e.Outcome = outcome; if (affectedCount.HasValue) e.AffectedCount = affectedCount; if (externalId != null) e.ExternalId = externalId; if (detail != null) e.Detail = detail;
            return Task.FromResult(0);
        }
        public Task<IvtAuditEntry> GetAsync(long id, CancellationToken cancellation) { return Task.FromResult(Entries.FirstOrDefault(x => x.Id == id)); }
        public Task<List<IvtAuditEntry>> RecentAsync(string operationPrefix, int max, CancellationToken cancellation)
        {
            return Task.FromResult(Entries.Where(e => operationPrefix == null || e.Operation.StartsWith(operationPrefix)).OrderByDescending(e => e.Id).Take(max).ToList());
        }
        public Task<IvtAuditEntry> LatestSubmissionAsync(string jobName, DateTime sinceUtc, CancellationToken cancellation)
        {
            return Task.FromResult(Entries.Where(e => e.ExternalId == jobName && e.OccurredAtUtc >= sinceUtc && (e.Outcome == "SUBMITTED" || e.Outcome == "AMBIGUOUS" || e.Outcome == "STARTED"))
                .OrderByDescending(e => e.Id).FirstOrDefault());
        }
    }

    public sealed class FakeCoordinator : IIvtCoordinator
    {
        public readonly HashSet<string> Held = new HashSet<string>();
        public Task<IDisposable> TryAcquireAsync(string resource, CancellationToken cancellation)
        {
            lock (Held)
            {
                if (!Held.Add(resource)) return Task.FromResult<IDisposable>(null);
                return Task.FromResult<IDisposable>(new Release(this, resource));
            }
        }
        private sealed class Release : IDisposable
        {
            private readonly FakeCoordinator owner; private readonly string resource;
            public Release(FakeCoordinator owner, string resource) { this.owner = owner; this.resource = resource; }
            public void Dispose() { lock (owner.Held) owner.Held.Remove(resource); }
        }
    }

    public sealed class FakeLanding : ILandingStore
    {
        private readonly CallLog log;
        public readonly Dictionary<IvtLandingFolder, List<IvtLandingFile>> Files = new Dictionary<IvtLandingFolder, List<IvtLandingFile>>();
        public FakeLanding(CallLog log) { this.log = log; }
        public string PathOf(IvtLandingFolder folder) { return @"\\fake\" + folder; }
        public IReadOnlyList<IvtLandingFile> List(IvtLandingFolder folder, string pattern)
        {
            List<IvtLandingFile> files; return Files.TryGetValue(folder, out files) ? files : new List<IvtLandingFile>();
        }
        public void Publish(IvtLandingFolder folder, string fileName, byte[] content)
        {
            log.Add("PUBLISH " + folder + " " + fileName);
            if (!Files.ContainsKey(folder)) Files[folder] = new List<IvtLandingFile>();
            Files[folder].Add(new IvtLandingFile { Name = fileName, Size = content.Length, Modified = DateTime.Now });
        }
        public Stream OpenRead(IvtLandingFolder folder, string fileName) { throw new NotSupportedException(); }
        public IReadOnlyList<KeyValuePair<string, string>> ErrorFiles(IvtLandingFolder folder) { return new List<KeyValuePair<string, string>>(); }
    }

    public sealed class FakeMail : INotificationTransport
    {
        private readonly CallLog log;
        public bool Fail;
        public readonly List<Tuple<IReadOnlyList<string>, IReadOnlyList<string>, string, string>> Sent = new List<Tuple<IReadOnlyList<string>, IReadOnlyList<string>, string, string>>();
        public FakeMail(CallLog log) { this.log = log; }
        public Task SendAsync(IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string html, CancellationToken cancellation)
        {
            log.Add("MAIL " + subject);
            if (Fail) throw new System.Net.Mail.SmtpException("SMTP unavailable");
            Sent.Add(Tuple.Create(to, cc, subject, html));
            return Task.FromResult(0);
        }
    }

    public sealed class FakeApi : ILegacyReportApi
    {
        private readonly CallLog log;
        public FakeApi(CallLog log) { this.log = log; }
        public Task<string> CallAsync(string operation, CancellationToken cancellation) { log.Add("API " + operation); return Task.FromResult("ok " + operation); }
    }

    public sealed class FakeProcess : IProcessRunner
    {
        public IvtProcessResult Result = new IvtProcessResult { ExitCode = 0, Output = "done" };
        public Task<IvtProcessResult> RunAsync(string executable, string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellation) { return Task.FromResult(Result); }
    }

    public sealed class FakeAzure : IAzureIvt
    {
        private readonly CallLog log;
        public List<string> Problems = new List<string>();
        public FakeAzure(CallLog log) { this.log = log; }
        public IReadOnlyList<string> ConfigurationProblems() { return Problems; }
        public Task<IReadOnlyList<string>> UploadFolderAsync(string localDirectory, string blobPrefix, CancellationToken cancellation) { log.Add("AZURE UPLOAD " + blobPrefix); return Task.FromResult<IReadOnlyList<string>>(new List<string> { blobPrefix + "a.csv" }); }
        public Task<string> StartPipelineAsync(CancellationToken cancellation) { log.Add("AZURE PIPELINE"); return Task.FromResult("run-123"); }
        public Task<string> PipelineRunStatusAsync(string runId, CancellationToken cancellation) { return Task.FromResult("InProgress"); }
    }
}
