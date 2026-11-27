using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    public static class IvtOutcome
    {
        public const string Started = "STARTED";            // recorded before a mutation begins
        public const string Succeeded = "SUCCEEDED";
        public const string Failed = "FAILED";
        public const string Partial = "PARTIAL";
        public const string Submitted = "SUBMITTED";        // job/pipeline accepted; completion is tracked separately
        public const string Ambiguous = "AMBIGUOUS";        // the start may or may not have happened; operator must reconcile
        public const string Rejected = "REJECTED";          // validation/conflict; nothing changed
        public const string SavedNotifyFailed = "SAVED_NOTIFY_FAILED";
    }

    public sealed class IvtAuditEntry
    {
        public long Id { get; set; }
        public Guid CorrelationId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public string Operator { get; set; }
        public string Environment { get; set; }
        public string Operation { get; set; }
        public string Target { get; set; }
        public int? AffectedCount { get; set; }
        public DateTime? InvoiceWeek { get; set; }
        public string ExternalId { get; set; }
        public string Outcome { get; set; }
        public string Detail { get; set; }
    }

    public interface IIvtAudit
    {
        Task<long> WriteAsync(IvtAuditEntry entry, CancellationToken cancellation);
        Task UpdateAsync(long id, string outcome, int? affectedCount, string externalId, string detail, CancellationToken cancellation);
        Task<IvtAuditEntry> GetAsync(long id, CancellationToken cancellation);
        Task<List<IvtAuditEntry>> RecentAsync(string operationPrefix, int max, CancellationToken cancellation);
        // Latest job submission for a job since a time (used to block a second start while the first is not yet visible as running).
        Task<IvtAuditEntry> LatestSubmissionAsync(string jobName, DateTime sinceUtc, CancellationToken cancellation);
    }

    // Shared coordination across application instances: SQL Server application locks held for the
    // critical section (conflict check -> preparation -> job start -> start observed).
    public interface IIvtCoordinator
    {
        // Returns null when another operator/instance holds the lock.
        Task<IDisposable> TryAcquireAsync(string resource, CancellationToken cancellation);
    }

    // Audit rows in IvtAdmin.AuditLog (migration V012). Values never include secrets or connection strings.
    public sealed class SqlIvtAudit : IIvtAudit
    {
        private readonly IvtAdminSettings settings;
        public SqlIvtAudit(IvtAdminSettings settings) { this.settings = settings; }
        private SqlConnection Open() { return new SqlConnection(settings.Require(settings.Connections.Audit, "connections.audit")); }

        public async Task<long> WriteAsync(IvtAuditEntry e, CancellationToken cancellation)
        {
            using (var connection = Open())
            using (var command = new SqlCommand(@"INSERT INTO IvtAdmin.AuditLog
                (CorrelationId, Operator, Environment, Operation, Target, AffectedCount, InvoiceWeek, ExternalId, Outcome, Detail)
                OUTPUT INSERTED.AuditId VALUES (@c, @op, @env, @operation, @target, @count, @week, @ext, @outcome, @detail)", connection))
            {
                command.Parameters.Add(new SqlParameter("@c", SqlDbType.UniqueIdentifier) { Value = e.CorrelationId });
                command.Parameters.Add(new SqlParameter("@op", SqlDbType.NVarChar, 128) { Value = e.Operator });
                command.Parameters.Add(new SqlParameter("@env", SqlDbType.NVarChar, 20) { Value = (object)e.Environment ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@operation", SqlDbType.NVarChar, 80) { Value = e.Operation });
                command.Parameters.Add(new SqlParameter("@target", SqlDbType.NVarChar, 400) { Value = (object)Clip(e.Target, 400) ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@count", SqlDbType.Int) { Value = (object)e.AffectedCount ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@week", SqlDbType.Date) { Value = (object)e.InvoiceWeek ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@ext", SqlDbType.NVarChar, 200) { Value = (object)e.ExternalId ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@outcome", SqlDbType.NVarChar, 20) { Value = e.Outcome });
                command.Parameters.Add(new SqlParameter("@detail", SqlDbType.NVarChar, -1) { Value = (object)e.Detail ?? DBNull.Value });
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                var id = (long)await command.ExecuteScalarAsync(cancellation).ConfigureAwait(false);
                e.Id = id;
                return id;
            }
        }

        public async Task UpdateAsync(long id, string outcome, int? affectedCount, string externalId, string detail, CancellationToken cancellation)
        {
            using (var connection = Open())
            using (var command = new SqlCommand(@"UPDATE IvtAdmin.AuditLog SET Outcome = @outcome, CompletedAtUtc = SYSUTCDATETIME(),
                AffectedCount = COALESCE(@count, AffectedCount), ExternalId = COALESCE(@ext, ExternalId),
                Detail = CASE WHEN @detail IS NULL THEN Detail ELSE @detail END WHERE AuditId = @id", connection))
            {
                command.Parameters.Add(new SqlParameter("@id", SqlDbType.BigInt) { Value = id });
                command.Parameters.Add(new SqlParameter("@outcome", SqlDbType.NVarChar, 20) { Value = outcome });
                command.Parameters.Add(new SqlParameter("@count", SqlDbType.Int) { Value = (object)affectedCount ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@ext", SqlDbType.NVarChar, 200) { Value = (object)externalId ?? DBNull.Value });
                command.Parameters.Add(new SqlParameter("@detail", SqlDbType.NVarChar, -1) { Value = (object)detail ?? DBNull.Value });
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
            }
        }

        public async Task<IvtAuditEntry> GetAsync(long id, CancellationToken cancellation)
        {
            var list = await Read("SELECT TOP (1) * FROM IvtAdmin.AuditLog WHERE AuditId = @p", new SqlParameter("@p", SqlDbType.BigInt) { Value = id }, cancellation).ConfigureAwait(false);
            return list.Count == 0 ? null : list[0];
        }

        public Task<List<IvtAuditEntry>> RecentAsync(string operationPrefix, int max, CancellationToken cancellation)
        {
            return Read("SELECT TOP (" + Math.Max(1, Math.Min(max, 500)) + ") * FROM IvtAdmin.AuditLog WHERE @p IS NULL OR Operation LIKE @p + '%' ORDER BY AuditId DESC",
                new SqlParameter("@p", SqlDbType.NVarChar, 80) { Value = (object)operationPrefix ?? DBNull.Value }, cancellation);
        }

        public async Task<IvtAuditEntry> LatestSubmissionAsync(string jobName, DateTime sinceUtc, CancellationToken cancellation)
        {
            using (var connection = Open())
            using (var command = new SqlCommand(@"SELECT TOP (1) * FROM IvtAdmin.AuditLog WHERE ExternalId = @job AND OccurredAtUtc >= @since
                AND Outcome IN (N'SUBMITTED', N'AMBIGUOUS', N'STARTED') ORDER BY AuditId DESC", connection))
            {
                command.Parameters.Add(new SqlParameter("@job", SqlDbType.NVarChar, 200) { Value = jobName });
                command.Parameters.Add(new SqlParameter("@since", SqlDbType.DateTime2) { Value = sinceUtc });
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false))
                    return await reader.ReadAsync(cancellation).ConfigureAwait(false) ? Map(reader) : null;
            }
        }

        private async Task<List<IvtAuditEntry>> Read(string sql, SqlParameter parameter, CancellationToken cancellation)
        {
            var list = new List<IvtAuditEntry>();
            using (var connection = Open())
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add(parameter);
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false))
                    while (await reader.ReadAsync(cancellation).ConfigureAwait(false)) list.Add(Map(reader));
            }
            return list;
        }

        private static IvtAuditEntry Map(SqlDataReader r)
        {
            return new IvtAuditEntry
            {
                Id = (long)r["AuditId"], CorrelationId = (Guid)r["CorrelationId"], OccurredAtUtc = (DateTime)r["OccurredAtUtc"],
                Operator = (string)r["Operator"], Environment = r["Environment"] as string, Operation = (string)r["Operation"],
                Target = r["Target"] as string, AffectedCount = r["AffectedCount"] as int?, InvoiceWeek = r["InvoiceWeek"] as DateTime?,
                ExternalId = r["ExternalId"] as string, Outcome = (string)r["Outcome"], Detail = r["Detail"] as string
            };
        }

        private static string Clip(string value, int max) { return value == null || value.Length <= max ? value : value.Substring(0, max); }
    }

    public sealed class SqlIvtCoordinator : IIvtCoordinator
    {
        private readonly IvtAdminSettings settings;
        public SqlIvtCoordinator(IvtAdminSettings settings) { this.settings = settings; }

        public async Task<IDisposable> TryAcquireAsync(string resource, CancellationToken cancellation)
        {
            var connection = new SqlConnection(settings.Require(settings.Connections.Audit, "connections.audit"));
            try
            {
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                using (var command = new SqlCommand("sp_getapplock", connection) { CommandType = CommandType.StoredProcedure })
                {
                    command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = "IVTAdmin:" + resource });
                    command.Parameters.Add(new SqlParameter("@LockMode", SqlDbType.VarChar, 32) { Value = "Exclusive" });
                    command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = "Session" });
                    command.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = 0 });
                    var result = command.Parameters.Add(new SqlParameter("@r", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue });
                    await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
                    if ((int)result.Value < 0) { connection.Dispose(); return null; }
                }
                return new Lease(connection, "IVTAdmin:" + resource);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private sealed class Lease : IDisposable
        {
            private SqlConnection connection;
            private readonly string resource;
            public Lease(SqlConnection connection, string resource) { this.connection = connection; this.resource = resource; }
            public void Dispose()
            {
                if (connection == null) return;
                try
                {
                    using (var command = new SqlCommand("sp_releaseapplock", connection) { CommandType = CommandType.StoredProcedure })
                    {
                        command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = resource });
                        command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = "Session" });
                        command.ExecuteNonQuery();
                    }
                }
                catch (SqlException) { /* closing the session releases the lock anyway */ }
                finally { connection.Dispose(); connection = null; }
            }
        }
    }
}
