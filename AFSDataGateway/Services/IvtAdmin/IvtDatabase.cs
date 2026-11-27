using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    // Which configured database a command runs against. Never derived from request data.
    public enum IvtDbTarget { Production, Test, AzureIvt }

    // A stored-procedure parameter with an explicit SQL type that matches the verified procedure contract.
    public sealed class IvtParam
    {
        public string Name { get; private set; }
        public SqlDbType Type { get; private set; }
        public int Size { get; private set; }
        public byte Precision { get; private set; }
        public byte Scale { get; private set; }
        public object Value { get; private set; }

        public IvtParam(string name, SqlDbType type, object value, int size = 0, byte precision = 0, byte scale = 0)
        {
            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("@")) throw new ArgumentException("Parameter names start with @.");
            Name = name; Type = type; Value = value; Size = size; Precision = precision; Scale = scale;
        }

        public static IvtParam VarChar(string name, int size, string value) { return new IvtParam(name, SqlDbType.VarChar, value, size); }
        public static IvtParam NVarChar(string name, int size, string value) { return new IvtParam(name, SqlDbType.NVarChar, value, size); }
        public static IvtParam Date(string name, DateTime? value) { return new IvtParam(name, SqlDbType.Date, value.HasValue ? (object)value.Value.Date : null); }

        public SqlParameter ToSqlParameter()
        {
            var p = new SqlParameter(Name, Type) { Value = Value ?? DBNull.Value };
            if (Size != 0) p.Size = Size;
            if (Precision != 0) { p.Precision = Precision; p.Scale = Scale; }
            return p;
        }

        // For audit records: the value as text, truncated (no secrets are ever passed as procedure parameters).
        public override string ToString()
        {
            var text = Value == null ? "NULL" : Value is DateTime ? ((DateTime)Value).ToString("yyyy-MM-dd") : Convert.ToString(Value, System.Globalization.CultureInfo.InvariantCulture);
            return Name + "=" + (text.Length > 200 ? text.Substring(0, 200) + "..." : text);
        }
    }

    public sealed class IvtColumn
    {
        public string Name { get; set; }
        public string SqlType { get; set; }
        public string ClrType { get; set; }
    }

    // A result set. Null database values stay null (never converted to 0, false or "").
    public sealed class IvtTable
    {
        public List<IvtColumn> Columns { get; } = new List<IvtColumn>();
        public List<object[]> Rows { get; } = new List<object[]>();

        public int IndexOf(string column)
        {
            for (var i = 0; i < Columns.Count; i++)
                if (string.Equals(Columns[i].Name, column, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public bool Has(string column) { return IndexOf(column) >= 0; }

        // Missing required columns are a contract error, not a default value.
        public void RequireColumns(string source, IEnumerable<string> columns)
        {
            var missing = columns.Where(c => !Has(c)).ToList();
            if (missing.Count > 0)
                throw new IvtContractException(source + " did not return required column(s): " + string.Join(", ", missing) +
                    ". The database contract differs from the one this module was built for.");
        }

        public object Get(object[] row, string column)
        {
            var i = IndexOf(column);
            if (i < 0) throw new IvtContractException("Result column '" + column + "' is missing.");
            return row[i];
        }

        public string GetString(object[] row, string column)
        {
            var value = Get(row, column);
            return value == null ? null : value is DateTime ? ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public sealed class IvtContractException : Exception
    {
        public IvtContractException(string message) : base(message) { }
    }

    public interface IIvtDatabase
    {
        Task<IvtTable> QueryProcedureAsync(IvtDbTarget target, string procedure, IEnumerable<IvtParam> parameters, CancellationToken cancellation);
        Task<int> ExecuteProcedureAsync(IvtDbTarget target, string procedure, IEnumerable<IvtParam> parameters, CancellationToken cancellation);
        // Fixed, read-only SQL text defined in this module (never built from request data).
        Task<IvtTable> QueryTextAsync(IvtDbTarget target, string sql, IEnumerable<IvtParam> parameters, CancellationToken cancellation);
    }

    public sealed class SqlIvtDatabase : IIvtDatabase
    {
        private static readonly Regex ProcedureName = new Regex(@"^(\[?[A-Za-z_][A-Za-z0-9_]*\]?\.)?\[?[A-Za-z_][A-Za-z0-9_]*\]?$");
        private readonly IvtAdminSettings settings;
        public SqlIvtDatabase(IvtAdminSettings settings) { this.settings = settings; }

        private string ConnectionString(IvtDbTarget target)
        {
            switch (target)
            {
                case IvtDbTarget.Production: return settings.ConnectionFor(IvtEnvironment.Production);
                case IvtDbTarget.Test: return settings.ConnectionFor(IvtEnvironment.Test);
                default: return settings.Require(settings.Connections.AzureIvt, "connections.azureIvt");
            }
        }

        public static string QuoteProcedure(string procedure)
        {
            if (!ProcedureName.IsMatch(procedure ?? "")) throw new ArgumentException("Invalid procedure name.");
            return string.Join(".", procedure.Split('.').Select(p => "[" + p.Trim('[', ']') + "]"));
        }

        public Task<IvtTable> QueryProcedureAsync(IvtDbTarget target, string procedure, IEnumerable<IvtParam> parameters, CancellationToken cancellation)
        {
            return QueryAsync(target, QuoteProcedure(procedure), CommandType.StoredProcedure, parameters, cancellation);
        }

        public Task<IvtTable> QueryTextAsync(IvtDbTarget target, string sql, IEnumerable<IvtParam> parameters, CancellationToken cancellation)
        {
            return QueryAsync(target, sql, CommandType.Text, parameters, cancellation);
        }

        public async Task<int> ExecuteProcedureAsync(IvtDbTarget target, string procedure, IEnumerable<IvtParam> parameters, CancellationToken cancellation)
        {
            using (var connection = new SqlConnection(ConnectionString(target)))
            using (var command = Command(connection, QuoteProcedure(procedure), CommandType.StoredProcedure, parameters))
            {
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                return await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
            }
        }

        private async Task<IvtTable> QueryAsync(IvtDbTarget target, string text, CommandType type, IEnumerable<IvtParam> parameters, CancellationToken cancellation)
        {
            using (var connection = new SqlConnection(ConnectionString(target)))
            using (var command = Command(connection, text, type, parameters))
            {
                await connection.OpenAsync(cancellation).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false))
                {
                    var table = new IvtTable();
                    // Procedures that only modify data return no result set (FieldCount 0); that is an empty table.
                    for (var i = 0; i < reader.FieldCount; i++)
                        table.Columns.Add(new IvtColumn { Name = reader.GetName(i), SqlType = reader.GetDataTypeName(i), ClrType = reader.GetFieldType(i).Name });
                    while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
                    {
                        var row = new object[reader.FieldCount];
                        reader.GetValues(row);
                        for (var i = 0; i < row.Length; i++) if (row[i] is DBNull) row[i] = null;
                        table.Rows.Add(row);
                    }
                    return table;
                }
            }
        }

        private SqlCommand Command(SqlConnection connection, string text, CommandType type, IEnumerable<IvtParam> parameters)
        {
            var command = new SqlCommand(text, connection) { CommandType = type, CommandTimeout = settings.SqlCommandTimeoutSeconds };
            foreach (var p in parameters ?? Enumerable.Empty<IvtParam>()) command.Parameters.Add(p.ToSqlParameter());
            return command;
        }
    }
}
