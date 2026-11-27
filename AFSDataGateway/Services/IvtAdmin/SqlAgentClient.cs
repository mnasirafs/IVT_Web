using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    public sealed class IvtJobStep
    {
        public int StepId { get; set; }
        public string Name { get; set; }
        public string LastOutcome { get; set; }
        public DateTime? LastRun { get; set; }
        public string LastDuration { get; set; }
    }

    // State of a SQL Agent job. "Submitted" (sp_start_job accepted) is never reported as "completed".
    public sealed class IvtJobStatus
    {
        public string Name { get; set; }
        public bool Exists { get; set; }
        public bool Enabled { get; set; }
        public bool IsRunning { get; set; }
        public DateTime? RunningSince { get; set; }
        public int? CurrentStepId { get; set; }
        public string CurrentStepName { get; set; }
        public string LastOutcome { get; set; }          // Succeeded / Failed / Canceled / Retry / null (never run)
        public DateTime? LastRunStart { get; set; }
        public string LastRunDuration { get; set; }
        public string LastFailedStep { get; set; }
        public string LastMessage { get; set; }
        public List<IvtJobStep> Steps { get; set; } = new List<IvtJobStep>();

        public string Summary
        {
            get
            {
                if (!Exists) return "Job not found";
                if (IsRunning) return "Executing" + (CurrentStepName != null ? " (step " + CurrentStepId + ": " + CurrentStepName + ")" : "");
                if (LastOutcome == null) return "Idle - never run";
                return "Idle - last run " + LastOutcome + (LastOutcome == "Failed" && LastFailedStep != null ? " at step " + LastFailedStep : "");
            }
        }
    }

    public interface ISqlAgent
    {
        Task<IvtJobStatus> GetStatusAsync(string jobName, CancellationToken cancellation);
        // Submits the job. Throws IvtOperationException when the job does not exist or is disabled.
        Task StartAsync(string jobName, CancellationToken cancellation);
    }

    // SQL Agent adapter over msdb (parameterised; jobs are found by name, never by hard-coded id).
    public sealed class SqlAgentClient : ISqlAgent
    {
        private readonly IvtAdminSettings settings;
        public SqlAgentClient(IvtAdminSettings settings) { this.settings = settings; }

        private const string StatusSql = @"
DECLARE @id uniqueidentifier, @enabled tinyint;
SELECT @id = job_id, @enabled = enabled FROM msdb.dbo.sysjobs WHERE name = @name;
SELECT CASE WHEN @id IS NULL THEN 0 ELSE 1 END AS job_exists, @enabled AS enabled;
IF @id IS NULL RETURN;
SELECT TOP (1) a.start_execution_date, a.stop_execution_date, a.last_executed_step_id
  FROM msdb.dbo.sysjobactivity a
 WHERE a.job_id = @id AND a.session_id = (SELECT MAX(session_id) FROM msdb.dbo.syssessions)
 ORDER BY a.start_execution_date DESC;
SELECT TOP (1) h.instance_id, h.run_status, h.run_date, h.run_time, h.run_duration, h.message
  FROM msdb.dbo.sysjobhistory h WHERE h.job_id = @id AND h.step_id = 0 ORDER BY h.instance_id DESC;
SELECT s.step_id, s.step_name, s.last_run_outcome, s.last_run_date, s.last_run_time, s.last_run_duration
  FROM msdb.dbo.sysjobsteps s WHERE s.job_id = @id ORDER BY s.step_id;
SELECT TOP (1) h.step_id, h.step_name, h.message
  FROM msdb.dbo.sysjobhistory h
 WHERE h.job_id = @id AND h.step_id > 0 AND h.run_status = 0
   AND h.instance_id < ISNULL((SELECT MAX(instance_id) FROM msdb.dbo.sysjobhistory WHERE job_id = @id AND step_id = 0), 2147483647)
   AND h.instance_id > ISNULL((SELECT MAX(instance_id) FROM msdb.dbo.sysjobhistory WHERE job_id = @id AND step_id = 0
                               AND instance_id < (SELECT MAX(instance_id) FROM msdb.dbo.sysjobhistory WHERE job_id = @id AND step_id = 0)), 0)
 ORDER BY h.instance_id DESC;
-- Last outcome kept by SQL Agent even when job history has been purged.
SELECT s.last_run_outcome, s.last_run_date, s.last_run_time, s.last_run_duration, s.last_outcome_message
  FROM msdb.dbo.sysjobservers s WHERE s.job_id = @id;";

        public async Task<IvtJobStatus> GetStatusAsync(string jobName, CancellationToken cancellation)
        {
            var status = new IvtJobStatus { Name = jobName };
            using (var connection = new SqlConnection(settings.Require(settings.Connections.Msdb, "connections.msdb")))
            using (var command = new SqlCommand(StatusSql, connection) { CommandTimeout = 60 })
            {
                command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = jobName });
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false))
                {
                    await reader.ReadAsync(cancellation).ConfigureAwait(false);
                    status.Exists = reader.GetInt32(0) == 1;
                    if (!status.Exists) return status;
                    status.Enabled = !reader.IsDBNull(1) && reader.GetByte(1) == 1;
                    int? lastExecutedStep = null;
                    if (await reader.NextResultAsync(cancellation).ConfigureAwait(false) && await reader.ReadAsync(cancellation).ConfigureAwait(false))
                    {
                        var start = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0);
                        var stop = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                        status.IsRunning = start.HasValue && !stop.HasValue;
                        if (status.IsRunning) status.RunningSince = start;
                        lastExecutedStep = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                    }
                    if (await reader.NextResultAsync(cancellation).ConfigureAwait(false) && await reader.ReadAsync(cancellation).ConfigureAwait(false))
                    {
                        status.LastOutcome = Outcome(reader.GetInt32(1));
                        status.LastRunStart = AgentDate(reader.GetInt32(2), reader.GetInt32(3));
                        status.LastRunDuration = Duration(reader.GetInt32(4));
                        status.LastMessage = reader.IsDBNull(5) ? null : reader.GetString(5);
                    }
                    if (await reader.NextResultAsync(cancellation).ConfigureAwait(false))
                        while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
                            status.Steps.Add(new IvtJobStep
                            {
                                StepId = reader.GetInt32(0), Name = reader.GetString(1), LastOutcome = Outcome(reader.GetInt32(2)),
                                LastRun = AgentDate(reader.GetInt32(3), reader.GetInt32(4)), LastDuration = Duration(reader.GetInt32(5))
                            });
                    if (await reader.NextResultAsync(cancellation).ConfigureAwait(false) && await reader.ReadAsync(cancellation).ConfigureAwait(false)
                        && status.LastOutcome == "Failed")
                    {
                        status.LastFailedStep = reader.GetInt32(0) + " (" + reader.GetString(1) + ")";
                        if (!reader.IsDBNull(2)) status.LastMessage = reader.GetString(2);
                    }
                    if (await reader.NextResultAsync(cancellation).ConfigureAwait(false) && await reader.ReadAsync(cancellation).ConfigureAwait(false) && status.LastOutcome == null)
                    {
                        var code = Convert.ToInt32(reader.GetValue(0));
                        status.LastOutcome = code == 5 ? null : Outcome(code);
                        status.LastRunStart = AgentDate(reader.GetInt32(1), reader.GetInt32(2));
                        status.LastRunDuration = Duration(reader.GetInt32(3));
                        status.LastMessage = reader.IsDBNull(4) ? null : reader.GetString(4);
                        // "... The last step to run was step 2 (Send AM Reports)."
                        var m = System.Text.RegularExpressions.Regex.Match(status.LastMessage ?? "", @"last step to run was step (\d+) \(([^)]*)\)");
                        if (status.LastOutcome == "Failed" && m.Success) status.LastFailedStep = m.Groups[1].Value + " (" + m.Groups[2].Value + ")";
                    }
                    if (status.IsRunning)
                    {
                        // sysjobactivity records the last COMPLETED step; the executing step is the next one (approximate when steps branch).
                        status.CurrentStepId = (lastExecutedStep ?? 0) + 1;
                        var step = status.Steps.Find(s => s.StepId == status.CurrentStepId);
                        status.CurrentStepName = step == null ? null : step.Name;
                    }
                }
            }
            return status;
        }

        public async Task StartAsync(string jobName, CancellationToken cancellation)
        {
            var status = await GetStatusAsync(jobName, cancellation).ConfigureAwait(false);
            if (!status.Exists) throw new IvtOperationException("SQL Agent job '" + jobName + "' was not found on the configured Agent server. Nothing was started.");
            if (!status.Enabled) throw new IvtOperationException("SQL Agent job '" + jobName + "' is disabled. Nothing was started.");
            using (var connection = new SqlConnection(settings.Require(settings.Connections.Msdb, "connections.msdb")))
            using (var command = new SqlCommand("msdb.dbo.sp_start_job", connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 })
            {
                command.Parameters.Add(new SqlParameter("@job_name", SqlDbType.NVarChar, 128) { Value = jobName });
                var code = command.Parameters.Add(new SqlParameter("@RETURN_VALUE", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue });
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
                if (code.Value is int && (int)code.Value != 0)
                    throw new IvtOperationException("SQL Agent refused to start '" + jobName + "' (return code " + code.Value + ").");
            }
        }

        private static string Outcome(int code)
        {
            switch (code) { case 0: return "Failed"; case 1: return "Succeeded"; case 2: return "Retry"; case 3: return "Canceled"; case 4: return "In progress"; default: return null; }
        }

        private static DateTime? AgentDate(int date, int time)
        {
            if (date <= 0) return null;
            return new DateTime(date / 10000, date / 100 % 100, date % 100, time / 10000, time / 100 % 100, time % 100);
        }

        private static string Duration(int hhmmss)
        {
            return (hhmmss / 10000).ToString("00") + ":" + (hhmmss / 100 % 100).ToString("00") + ":" + (hhmmss % 100).ToString("00");
        }
    }
}
