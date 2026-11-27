using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    public enum IvtRunKind { Production, Silent, Test }

    // Engine kickoff, status, history, rollback and the optional Azure path. The engine itself is the existing
    // SQL Agent jobs and IVT procedures; this service only prepares, starts, observes and records.
    public sealed class RunControlService
    {
        public const string EngineLock = "engine";
        private const string PrepareProcedure = "WebIVT.sp_FileloadControl";
        private const string HistoryProcedure = "WebIVT.sp_Get_IVT_Run_Time";
        private const string RollbackProcedure = "WebIVT.Rollback_Invoice_Week_Load";
        private readonly IvtAdminRuntime rt;
        public RunControlService(IvtAdminRuntime runtime) { rt = runtime; }

        // Most recent Friday strictly before today in the business time zone (corrects the legacy calculation).
        public DateTime DefaultInvoiceWeek()
        {
            var day = rt.BusinessToday().AddDays(-1);
            while (day.DayOfWeek != DayOfWeek.Friday) day = day.AddDays(-1);
            return day;
        }

        // Returns an error message, or null when valid. A non-Friday date is allowed (IVT has used other weekdays) but warned about.
        public string ValidateInvoiceWeek(DateTime? week, out string warning)
        {
            warning = null;
            if (!week.HasValue) return "Choose an invoice week.";
            if (week.Value.Date > rt.BusinessToday()) return "The invoice week cannot be in the future.";
            if (week.Value.Date < rt.BusinessToday().AddYears(-1)) return "The invoice week is more than a year ago; check the date.";
            if (week.Value.DayOfWeek != DayOfWeek.Friday) warning = week.Value.ToString("dddd d MMM yyyy") + " is not a Friday.";
            return null;
        }

        public string JobFor(IvtRunKind kind)
        {
            var jobs = rt.Settings.Jobs;
            return kind == IvtRunKind.Production ? jobs.ProductionEngine : kind == IvtRunKind.Silent ? jobs.SilentEngine : jobs.TestEngine;
        }

        // The ordered steps a run will perform, shown to the operator before confirming.
        public List<string> Plan(IvtRunKind kind)
        {
            var steps = new List<string>();
            if (kind == IvtRunKind.Test) steps.Add("Prepare invoice week on IVT_Test");
            else
            {
                steps.Add("Prepare invoice week on IVT");
                if (rt.Settings.ProductionRunPreparesTest) steps.Add("Prepare invoice week on IVT_Test (legacy behaviour, setting productionRunPreparesTest)");
            }
            steps.Add("Start SQL Agent job " + JobFor(kind) + " (submission only - completion is shown by Refresh)");
            if (kind != IvtRunKind.Test && rt.Settings.ProductionRunMirrorsToAzure) steps.Add("Azure mirror: prepare Azure IVT, upload Buy/Sell files, start pipeline " + rt.Settings.Azure.Pipeline);
            return steps;
        }

        public async Task<IvtActionResult> StartRunAsync(IvtRunKind kind, DateTime? invoiceWeek, bool landingConfirmed, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            var environment = kind == IvtRunKind.Test ? IvtEnvironment.Test : IvtEnvironment.Production;
            var job = JobFor(kind);
            var entry = new IvtAuditEntry { CorrelationId = correlation, Operator = op.Login, Environment = environment.Key(), Operation = "RUN." + kind.ToString().ToUpperInvariant(),
                Target = job, InvoiceWeek = invoiceWeek, ExternalId = job };
            string warning;
            var invalid = !op.IsAdmin ? "IVT administrator permission is required." :
                          !landingConfirmed ? "Confirm that the landing-zone files for this invoice week are in place." :
                          ValidateInvoiceWeek(invoiceWeek, out warning);
            if (invalid != null) { await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, invalid); return IvtActionResult.Rejected(invalid, correlation); }
            var week = invoiceWeek.Value.Date;

            using (var lease = await rt.Coordinator.TryAcquireAsync(EngineLock, cancellation).ConfigureAwait(false))
            {
                if (lease == null)
                {
                    const string busy = "Another run, rollback or load is being started right now. Wait a moment and refresh.";
                    await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, busy);
                    return IvtActionResult.Rejected(busy, correlation);
                }
                var conflict = await CheckConflictsAsync(job, true, cancellation).ConfigureAwait(false);
                if (conflict != null) { await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, conflict); return IvtActionResult.Rejected(conflict, correlation); }

                var audited = await AuditedOperation.BeginAsync(rt.Audit, entry, cancellation).ConfigureAwait(false);
                var result = new IvtActionResult { CorrelationId = correlation, AuditId = audited.Entry.Id };
                var submittedAt = rt.UtcNow();
                try
                {
                    // 1-2. Invoice-week preparation (IVT, then IVT_Test when configured; test runs prepare IVT_Test only).
                    var targets = kind == IvtRunKind.Test ? new[] { IvtDbTarget.Test } :
                        rt.Settings.ProductionRunPreparesTest ? new[] { IvtDbTarget.Production, IvtDbTarget.Test } : new[] { IvtDbTarget.Production };
                    foreach (var target in targets)
                    {
                        if (!await PrepareAsync(target, week, result, cancellation).ConfigureAwait(false))
                        {
                            result.Steps.Add(new IvtStep { Name = "Start " + job, Status = "Not attempted" });
                            return await Finish(audited, result, IvtOutcome.Failed, "Invoice-week preparation failed; the engine was not started.", cancellation);
                        }
                    }
                    // 3. Submit the job and observe it starting.
                    var started = await SubmitJobAsync(job, submittedAt, result, cancellation).ConfigureAwait(false);
                    if (started == IvtOutcome.Failed)
                        return await Finish(audited, result, IvtOutcome.Failed, "The SQL Agent job was not started. The invoice week was prepared; fix the problem and start again.", cancellation);
                    if (started == IvtOutcome.Ambiguous)
                        return await Finish(audited, result, IvtOutcome.Ambiguous, "It is not certain whether " + job + " started. Do NOT start it again: refresh the status and check SQL Agent first.", cancellation);
                    // 4. Optional legacy Azure mirror, reported separately (never hides failures).
                    if (kind != IvtRunKind.Test && rt.Settings.ProductionRunMirrorsToAzure)
                        await AzureSequenceAsync(week, false, result, cancellation).ConfigureAwait(false);
                    var azureProblem = result.Steps.Any(s => s.Name.StartsWith("Azure") && s.Status != "Done" && s.Status != "Submitted");
                    var message = job + " was submitted for invoice week " + week.ToString("dd MMM yyyy") + ". It is running in SQL Agent - use Refresh to follow it; this is not completion." +
                        (azureProblem ? " The Azure mirror did not complete; see its steps." : "");
                    var finished = await Finish(audited, result, azureProblem ? IvtOutcome.Partial : IvtOutcome.Submitted, message, cancellation);
                    finished.Ok = true;
                    return finished;
                }
                catch (Exception ex)
                {
                    result.Steps.Add(new IvtStep { Name = "Unexpected failure", Status = "Failed", Message = IvtErrors.Describe(ex, correlation) });
                    return await Finish(audited, result, IvtOutcome.Failed, "The run did not complete its start sequence. Check the steps below before retrying.", cancellation);
                }
            }
        }

        // Azure-only sequence (desktop "Azure" button): prepare IVT, IVT_Test and Azure IVT, upload Buy/Sell, start the pipeline.
        public async Task<IvtActionResult> StartAzureRunAsync(DateTime? invoiceWeek, bool landingConfirmed, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            var entry = new IvtAuditEntry { CorrelationId = correlation, Operator = op.Login, Environment = "azure", Operation = "RUN.AZURE",
                Target = rt.Settings.Azure.Pipeline, InvoiceWeek = invoiceWeek };
            string warning;
            var problems = rt.Azure.ConfigurationProblems();
            var invalid = !op.IsAdmin ? "IVT administrator permission is required." :
                          problems.Count > 0 ? "Azure execution is not available: " + string.Join("; ", problems) + "." :
                          string.IsNullOrWhiteSpace(rt.Settings.Connections.AzureIvt) ? "Azure execution is not available: connections.azureIvt is not set." :
                          !landingConfirmed ? "Confirm that the landing-zone files for this invoice week are in place." :
                          ValidateInvoiceWeek(invoiceWeek, out warning);
            if (invalid != null) { await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, invalid); return IvtActionResult.Rejected(invalid, correlation); }

            using (var lease = await rt.Coordinator.TryAcquireAsync(EngineLock, cancellation).ConfigureAwait(false))
            {
                if (lease == null) return IvtActionResult.Rejected("Another run, rollback or load is being started right now.", correlation);
                var conflict = await CheckConflictsAsync(null, false, cancellation).ConfigureAwait(false);
                if (conflict != null) { await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, conflict); return IvtActionResult.Rejected(conflict, correlation); }
                var audited = await AuditedOperation.BeginAsync(rt.Audit, entry, cancellation).ConfigureAwait(false);
                var result = new IvtActionResult { CorrelationId = correlation, AuditId = audited.Entry.Id };
                await AzureSequenceAsync(invoiceWeek.Value.Date, true, result, cancellation).ConfigureAwait(false);
                var run = result.Steps.FirstOrDefault(s => s.Name == "Azure: start pipeline");
                var outcome = run == null || run.Status == "Failed" || run.Status == "Not attempted" ? IvtOutcome.Failed : run.Status == "Ambiguous" ? IvtOutcome.Ambiguous : IvtOutcome.Submitted;
                var message = outcome == IvtOutcome.Submitted ? "The Azure pipeline run was submitted (run id " + result.Data + "). Use 'Check Azure run' for its status." :
                              outcome == IvtOutcome.Ambiguous ? "The pipeline start response was lost; the run may exist. Do NOT start again - check Data Factory monitoring first." :
                              "The Azure sequence stopped; completed stages are listed below and were not rolled back.";
                var done = await Finish(audited, result, outcome, message, cancellation, result.Data as string);
                done.Ok = outcome == IvtOutcome.Submitted;
                return done;
            }
        }

        public async Task<string> AzureRunStatusAsync(string runId, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(runId) || runId.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(runId, "^[A-Za-z0-9-]+$"))
                throw new IvtOperationException("Enter a valid pipeline run id.");
            return await rt.Azure.PipelineRunStatusAsync(runId, cancellation).ConfigureAwait(false);
        }

        private async Task AzureSequenceAsync(DateTime week, bool prepareOnPremises, IvtActionResult result, CancellationToken cancellation)
        {
            var problems = rt.Azure.ConfigurationProblems();
            if (problems.Count > 0 || string.IsNullOrWhiteSpace(rt.Settings.Connections.AzureIvt))
            {
                result.Steps.Add(new IvtStep { Name = "Azure mirror", Status = "Skipped", Message = "Not configured: " + string.Join("; ", problems.Concat(string.IsNullOrWhiteSpace(rt.Settings.Connections.AzureIvt) ? new[] { "connections.azureIvt" } : new string[0])) });
                return;
            }
            if (prepareOnPremises)
                foreach (var target in new[] { IvtDbTarget.Production, IvtDbTarget.Test })
                    if (!await PrepareAsync(target, week, result, cancellation).ConfigureAwait(false)) { NotAttempted(result, "Azure: prepare Azure IVT", "Azure: upload Buy files", "Azure: upload Sell files", "Azure: start pipeline"); return; }
            if (!await PrepareAsync(IvtDbTarget.AzureIvt, week, result, cancellation).ConfigureAwait(false)) { NotAttempted(result, "Azure: upload Buy files", "Azure: upload Sell files", "Azure: start pipeline"); return; }
            foreach (var pair in new[] { Tuple.Create("Buy", IvtLandingFolder.Buy, rt.Settings.Azure.BuyPrefix), Tuple.Create("Sell", IvtLandingFolder.Sell, rt.Settings.Azure.SellPrefix) })
            {
                try
                {
                    var uploaded = await rt.Azure.UploadFolderAsync(rt.Landing.PathOf(pair.Item2), pair.Item3, cancellation).ConfigureAwait(false);
                    result.Steps.Add(new IvtStep { Name = "Azure: upload " + pair.Item1 + " files", Status = "Done", Message = uploaded.Count + " file(s) uploaded to " + pair.Item3 });
                }
                catch (Exception ex)
                {
                    result.Steps.Add(new IvtStep { Name = "Azure: upload " + pair.Item1 + " files", Status = "Failed", Message = IvtErrors.Describe(ex, result.CorrelationId) });
                    NotAttempted(result, pair.Item1 == "Buy" ? new[] { "Azure: upload Sell files", "Azure: start pipeline" } : new[] { "Azure: start pipeline" });
                    return;
                }
            }
            try
            {
                var runId = await rt.Azure.StartPipelineAsync(cancellation).ConfigureAwait(false);
                result.Data = runId;
                result.Steps.Add(new IvtStep { Name = "Azure: start pipeline", Status = "Submitted", Message = "Run id " + runId });
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                // The request may have reached Data Factory: never retried automatically.
                result.Steps.Add(new IvtStep { Name = "Azure: start pipeline", Status = "Ambiguous", Message = "No response from Data Factory (" + ex.GetType().Name + "). A run may have been created." });
            }
            catch (Exception ex)
            {
                result.Steps.Add(new IvtStep { Name = "Azure: start pipeline", Status = "Failed", Message = IvtErrors.Describe(ex, result.CorrelationId) });
            }
        }

        private static void NotAttempted(IvtActionResult result, params string[] names)
        {
            foreach (var name in names) result.Steps.Add(new IvtStep { Name = name, Status = "Not attempted" });
        }

        private async Task<bool> PrepareAsync(IvtDbTarget target, DateTime week, IvtActionResult result, CancellationToken cancellation)
        {
            var label = target == IvtDbTarget.Production ? "IVT" : target == IvtDbTarget.Test ? "IVT_Test" : "Azure IVT";
            var name = (target == IvtDbTarget.AzureIvt ? "Azure: prepare " : "Prepare ") + label;
            try
            {
                await rt.Database.ExecuteProcedureAsync(target, PrepareProcedure, new[]
                {
                    IvtParam.Date("@InvoiceWeek", week), IvtParam.VarChar("@Type", 50, "All")
                }, cancellation).ConfigureAwait(false);
                result.Steps.Add(new IvtStep { Name = name, Status = "Done", Message = "Prepared invoice week " + week.ToString("yyyy-MM-dd") });
                return true;
            }
            catch (Exception ex)
            {
                result.Steps.Add(new IvtStep { Name = name, Status = "Failed", Message = IvtErrors.Describe(ex, result.CorrelationId) });
                return false;
            }
        }

        // Starts the job and waits briefly to see it executing. Returns Submitted, Failed (definitely not started) or Ambiguous.
        private async Task<string> SubmitJobAsync(string job, DateTime submittedAtUtc, IvtActionResult result, CancellationToken cancellation)
        {
            var name = "Start " + job;
            try
            {
                await rt.Agent.StartAsync(job, cancellation).ConfigureAwait(false);
            }
            catch (IvtOperationException ex)
            {
                result.Steps.Add(new IvtStep { Name = name, Status = "Failed", Message = ex.Message });
                return IvtOutcome.Failed;
            }
            catch (System.Data.SqlClient.SqlException ex) when (ex.Number != -2)
            {
                // SQL Server answered with an error: sp_start_job did not start the job.
                result.Steps.Add(new IvtStep { Name = name, Status = "Failed", Message = IvtErrors.Describe(ex, result.CorrelationId) });
                return IvtOutcome.Failed;
            }
            catch (Exception ex)
            {
                result.Steps.Add(new IvtStep { Name = name, Status = "Ambiguous", Message = IvtErrors.Describe(ex, result.CorrelationId) });
                return IvtOutcome.Ambiguous;
            }
            var deadline = rt.UtcNow().AddSeconds(rt.Settings.JobStartObserveSeconds);
            IvtJobStatus status = null;
            while (true)
            {
                try { status = await rt.Agent.GetStatusAsync(job, cancellation).ConfigureAwait(false); }
                catch (Exception) { status = null; }
                if (status != null && status.IsRunning) break;
                if (rt.UtcNow() >= deadline) break;
                await rt.Delay(TimeSpan.FromSeconds(2), cancellation).ConfigureAwait(false);
            }
            result.Steps.Add(new IvtStep
            {
                Name = name, Status = "Submitted",
                Message = status != null && status.IsRunning ? "SQL Agent accepted the job and it is executing" + (status.CurrentStepName != null ? " (step " + status.CurrentStepId + ": " + status.CurrentStepName + ")" : "") + "."
                        : status != null && status.LastRunStart.HasValue && status.LastRunStart.Value >= TimeZoneInfo.ConvertTimeFromUtc(submittedAtUtc, rt.Settings.TimeZone()).AddMinutes(-1)
                            ? "SQL Agent accepted the job and it has already finished (" + status.LastOutcome + ")."
                            : "SQL Agent accepted the job; it was not yet visible as executing. Refresh to follow it."
            });
            return IvtOutcome.Submitted;
        }

        // Null when all conflicting jobs are idle, the target job exists, and no recent submission is unaccounted for.
        public async Task<string> CheckConflictsAsync(string targetJob, bool checkRecentSubmission, CancellationToken cancellation)
        {
            var names = rt.Settings.ConflictingJobs.Concat(targetJob == null ? new string[0] : new[] { targetJob }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var running = new List<string>();
            foreach (var name in names)
            {
                var status = await rt.Agent.GetStatusAsync(name, cancellation).ConfigureAwait(false);
                if (string.Equals(name, targetJob, StringComparison.OrdinalIgnoreCase))
                {
                    if (!status.Exists) return "SQL Agent job '" + name + "' was not found on the configured Agent server. Nothing was changed.";
                    if (!status.Enabled) return "SQL Agent job '" + name + "' is disabled. Nothing was changed.";
                }
                if (status.IsRunning) running.Add(name + (status.CurrentStepName != null ? " (step " + status.CurrentStepName + ")" : ""));
                if (checkRecentSubmission && string.Equals(name, targetJob, StringComparison.OrdinalIgnoreCase) && !status.IsRunning)
                {
                    var since = rt.UtcNow().AddMinutes(-rt.Settings.RecentSubmissionBlockMinutes);
                    var recent = await rt.Audit.LatestSubmissionAsync(name, since, cancellation).ConfigureAwait(false);
                    if (recent != null)
                    {
                        var submittedLocal = TimeZoneInfo.ConvertTimeFromUtc(recent.OccurredAtUtc, rt.Settings.TimeZone()).AddMinutes(-1);
                        if (!status.LastRunStart.HasValue || status.LastRunStart.Value < submittedLocal)
                            return name + " was submitted by " + recent.Operator + " at " + submittedLocal.AddMinutes(1).ToString("HH:mm") +
                                   " and has not been seen running yet (audit #" + recent.Id + "). Refresh in a minute; do not start it twice.";
                    }
                }
            }
            if (running.Count > 0) return "Conflicting SQL Agent job(s) are executing: " + string.Join(", ", running) + ". Wait for them to finish. Nothing was changed.";
            return null;
        }

        public async Task<IvtActionResult> RollbackAsync(IvtEnvironment environment, string confirmation, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            var entry = new IvtAuditEntry { CorrelationId = correlation, Operator = op.Login, Environment = environment.Key(), Operation = "RUN.ROLLBACK", Target = RollbackProcedure };
            var expected = "ROLLBACK " + environment.Key().ToUpperInvariant();
            var invalid = !op.IsAdmin ? "IVT administrator permission is required." :
                          !string.Equals((confirmation ?? "").Trim(), expected, StringComparison.Ordinal) ? "Type " + expected + " exactly to confirm the rollback." : null;
            if (invalid != null) { await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, invalid); return IvtActionResult.Rejected(invalid, correlation); }
            using (var lease = await rt.Coordinator.TryAcquireAsync(EngineLock, cancellation).ConfigureAwait(false))
            {
                if (lease == null) return IvtActionResult.Rejected("Another run, rollback or load is being started right now.", correlation);
                var conflict = await CheckConflictsAsync(null, false, cancellation).ConfigureAwait(false);
                if (conflict != null) { await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, conflict); return IvtActionResult.Rejected(conflict, correlation); }
                var audited = await AuditedOperation.BeginAsync(rt.Audit, entry, cancellation).ConfigureAwait(false);
                var result = new IvtActionResult { CorrelationId = correlation, AuditId = audited.Entry.Id };
                try
                {
                    await rt.Database.ExecuteProcedureAsync(RuntimeTarget(environment), RollbackProcedure, new IvtParam[0], cancellation).ConfigureAwait(false);
                    result.Steps.Add(new IvtStep { Name = "Rollback last invoice-week load on " + environment.Label(), Status = "Done" });
                    var done = await Finish(audited, result, IvtOutcome.Succeeded, "The last invoice-week load was rolled back on " + environment.Label() + ".", cancellation);
                    done.Ok = true;
                    return done;
                }
                catch (Exception ex)
                {
                    result.Steps.Add(new IvtStep { Name = "Rollback on " + environment.Label(), Status = "Failed", Message = IvtErrors.Describe(ex, correlation) });
                    return await Finish(audited, result, IvtOutcome.Failed, "The rollback failed. The procedure's own transaction handling determines whether any part was applied; check run history.", cancellation);
                }
            }
        }

        public Task<IvtTable> HistoryAsync(IvtEnvironment environment, CancellationToken cancellation)
        {
            return rt.Database.QueryProcedureAsync(RuntimeTarget(environment), HistoryProcedure, new IvtParam[0], cancellation);
        }

        public async Task<List<IvtJobStatus>> JobStatusesAsync(IvtEnvironment environment, CancellationToken cancellation)
        {
            var jobs = rt.Settings.Jobs;
            var names = environment == IvtEnvironment.Test ? new[] { jobs.TestEngine }
                : new[] { jobs.ProductionEngine, jobs.SilentEngine, jobs.TestEngine };
            var list = new List<IvtJobStatus>();
            foreach (var name in names) list.Add(await rt.Agent.GetStatusAsync(name, cancellation).ConfigureAwait(false));
            return list;
        }

        private static IvtDbTarget RuntimeTarget(IvtEnvironment environment) { return IvtAdminRuntime.Target(environment); }

        private static async Task<IvtActionResult> Finish(AuditedOperation audited, IvtActionResult result, string outcome, string message, CancellationToken cancellation, string externalId = null)
        {
            result.Outcome = outcome;
            result.Message = message;
            result.Warning = await audited.CompleteAsync(outcome, null, externalId, new { steps = result.Steps }, cancellation).ConfigureAwait(false);
            return result;
        }
    }
}
