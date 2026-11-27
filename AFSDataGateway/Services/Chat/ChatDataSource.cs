using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;

namespace AFSDataGateway.Services.Chat
{
    public interface IChatDataSource
    {
        IQueryable<vDash_Exception_Summary> Summaries { get; }
        IQueryable<vDash_CustomerCounts> Customers { get; }
        IQueryable<vIVT_Result> Exceptions { get; }
        IQueryable<Dash_LastIVTRun> Runs { get; }
        IQueryable<vDash_CustomerCarrierCounts> Trading { get; }
        // Read-only, parameterised SQL built by ChatDataset from catalog identifiers; returns raw rows (DBNull -> null).
        List<object[]> Query(string sql, IEnumerable<SqlParameter> parameters);
    }

    public sealed class ChatDataSource : IChatDataSource
    {
        private const int QueryTimeoutSeconds = 60;
        private readonly IVTEntities db;
        public ChatDataSource(IVTEntities db) { this.db = db; db.Database.CommandTimeout = QueryTimeoutSeconds; }
        public IQueryable<vDash_Exception_Summary> Summaries { get { return db.vDash_Exception_Summary.AsNoTracking(); } }
        public IQueryable<vDash_CustomerCounts> Customers { get { return db.vDash_CustomerCounts.AsNoTracking(); } }
        public IQueryable<vIVT_Result> Exceptions { get { return db.vIVT_Result.AsNoTracking(); } }
        public IQueryable<Dash_LastIVTRun> Runs { get { return db.Dash_LastIVTRun.AsNoTracking(); } }
        public IQueryable<vDash_CustomerCarrierCounts> Trading { get { return db.vDash_CustomerCarrierCounts.AsNoTracking(); } }

        public List<object[]> Query(string sql, IEnumerable<SqlParameter> parameters)
        {
            var connection = db.Database.Connection;
            var opened = connection.State != ConnectionState.Open;
            if (opened) connection.Open();
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.CommandTimeout = QueryTimeoutSeconds;
                    // Parameters are cloned so the same list can be used by more than one command.
                    foreach (var p in parameters)
                        command.Parameters.Add(new SqlParameter(p.ParameterName, p.SqlDbType) { Size = p.Size, Value = p.Value });
                    var rows = new List<object[]>();
                    using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        while (reader.Read())
                        {
                            var row = new object[reader.FieldCount];
                            reader.GetValues(row);
                            for (var i = 0; i < row.Length; i++) if (row[i] is DBNull) row[i] = null;
                            rows.Add(row);
                        }
                    }
                    return rows;
                }
            }
            catch (SqlException ex) when (ex.Number == -2)
            {
                throw new ChatQueryException("The IVT data query timed out. No results were returned. Try a shorter period or a specific customer, carrier or work code; historical queries may require database tuning.");
            }
            finally
            {
                if (opened) connection.Close();
            }
        }
    }
}
