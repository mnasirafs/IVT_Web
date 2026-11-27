using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    // Operator-triggered report/notification actions (legacy HTTP service) and the test notification process.
    public sealed class NotificationActionService
    {
        public static readonly string[][] LegacyActions =
        {
            new[] { "attachfiles", "Email reports", "Emails the generated AM report files (production)." },
            new[] { "notifyam", "Notify account managers", "Sends the 'IVT results ready' notification to account managers (production)." },
            new[] { "notifyexecs", "Notify executives", "Sends the executive summary notification (production)." },
        };
        public const int DuplicateWindowMinutes = 15;
        private readonly IvtAdminRuntime rt;
        public NotificationActionService(IvtAdminRuntime runtime) { rt = runtime; }

        // Production notifications. A repeat within the duplicate window requires sendAgain = true.
        public async Task<IvtActionResult> CallLegacyAsync(string operation, bool sendAgain, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            var action = LegacyActions.FirstOrDefault(a => a[0] == operation);
            if (action == null) return IvtActionResult.Rejected("Unknown notification action.", correlation);
            using (var lease = await rt.Coordinator.TryAcquireAsync("notify:" + operation, cancellation).ConfigureAwait(false))
            {
                if (lease == null) return IvtActionResult.Rejected(action[1] + " is already being sent by another request.", correlation);
                var recent = (await rt.Audit.RecentAsync("NOTIFY." + operation.ToUpperInvariant(), 5, cancellation).ConfigureAwait(false))
                    .FirstOrDefault(e => e.OccurredAtUtc >= rt.UtcNow().AddMinutes(-DuplicateWindowMinutes) && e.Outcome != IvtOutcome.Rejected && e.Outcome != IvtOutcome.Failed);
                if (recent != null && !sendAgain)
                    return new IvtActionResult { Outcome = IvtOutcome.Rejected, CorrelationId = correlation, Data = "confirm-repeat",
                        Message = action[1] + " was already requested by " + recent.Operator + " at " + TimeZoneInfo.ConvertTimeFromUtc(recent.OccurredAtUtc, rt.Settings.TimeZone()).ToString("HH:mm") +
                                  " (" + recent.Outcome + "). Confirm 'send again' to repeat it." };
                var audited = await AuditedOperation.BeginAsync(rt.Audit, new IvtAuditEntry
                    { CorrelationId = correlation, Operator = op.Login, Environment = "production", Operation = "NOTIFY." + operation.ToUpperInvariant(), Target = operation }, cancellation).ConfigureAwait(false);
                var result = new IvtActionResult { CorrelationId = correlation, AuditId = audited.Entry.Id };
                try
                {
                    var message = await rt.ReportApi.CallAsync(operation, cancellation).ConfigureAwait(false);
                    result.Ok = true; result.Outcome = IvtOutcome.Succeeded; result.Message = action[1] + ": " + message;
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex is System.Net.Http.HttpRequestException)
                {
                    result.Outcome = IvtOutcome.Ambiguous;
                    result.Message = action[1] + ": no complete response from the report service (" + ex.GetType().Name + "). It may still have sent the emails - check before repeating.";
                }
                catch (Exception ex) { result.Outcome = IvtOutcome.Failed; result.Message = action[1] + " failed: " + IvtErrors.Describe(ex, correlation); }
                result.Warning = await audited.CompleteAsync(result.Outcome, null, null, new { operation, message = result.Message }, cancellation).ConfigureAwait(false);
                return result;
            }
        }

        // Test notifications only (configured test script). Success requires exit code 0 within the timeout.
        public async Task<IvtActionResult> RunTestNotifierAsync(IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            var cfg = rt.Settings.TestNotifier;
            if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.Executable))
                return IvtActionResult.Rejected("The test notification process is not enabled in the settings (testNotifier).", correlation);
            using (var lease = await rt.Coordinator.TryAcquireAsync("notify:test", cancellation).ConfigureAwait(false))
            {
                if (lease == null) return IvtActionResult.Rejected("Test notifications are already running.", correlation);
                var audited = await AuditedOperation.BeginAsync(rt.Audit, new IvtAuditEntry
                    { CorrelationId = correlation, Operator = op.Login, Environment = "test", Operation = "NOTIFY.TEST", Target = cfg.Arguments }, cancellation).ConfigureAwait(false);
                var result = new IvtActionResult { CorrelationId = correlation, AuditId = audited.Entry.Id };
                IvtProcessResult run = null;
                try
                {
                    run = await rt.Processes.RunAsync(cfg.Executable, cfg.Arguments, cfg.WorkingDirectory, TimeSpan.FromSeconds(cfg.TimeoutSeconds), cancellation).ConfigureAwait(false);
                    result.Ok = run.Succeeded;
                    result.Outcome = run.Succeeded ? IvtOutcome.Succeeded : run.TimedOut ? IvtOutcome.Ambiguous : IvtOutcome.Failed;
                    result.Message = run.TimedOut ? "The test notifier did not finish within " + cfg.TimeoutSeconds + " s and was stopped; some emails may have been sent."
                                   : run.Succeeded ? "Test notifications finished (exit code 0)." : "The test notifier failed with exit code " + run.ExitCode + ".";
                    result.Data = new { exitCode = run.ExitCode, timedOut = run.TimedOut, output = Tail(run.Output), error = Tail(run.Error) };
                }
                catch (Exception ex) { result.Outcome = IvtOutcome.Failed; result.Message = "The test notifier could not be started: " + IvtErrors.Describe(ex, correlation); }
                result.Warning = await audited.CompleteAsync(result.Outcome, null, null, result.Data ?? (object)new { error = result.Message }, cancellation).ConfigureAwait(false);
                return result;
            }
        }

        private static string Tail(string text)
        {
            text = text ?? "";
            return text.Length <= 6000 ? text : "..." + text.Substring(text.Length - 6000);
        }
    }
}
