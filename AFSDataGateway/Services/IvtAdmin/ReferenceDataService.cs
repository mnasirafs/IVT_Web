using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    public sealed class IvtRowOutcome
    {
        public long? Id { get; set; }
        public string Status { get; set; }   // Updated / Unchanged / Failed / Created
        public string Message { get; set; }
    }

    // Search / create / update / update-all / soft delete for the reference settings, through the existing procedures only.
    public sealed class ReferenceDataService
    {
        public const int MaxBatch = 1000;
        private readonly IvtAdminRuntime rt;
        public ReferenceDataService(IvtAdminRuntime runtime) { rt = runtime; }

        // Blank search means "all" for these procedures (they test LEN(@searchtxt) > 0).
        public static string NormalizeSearch(string search) { return (search ?? "").Trim(); }

        private static void RequireEnvironment(IvtEntityDef def, IvtEnvironment environment)
        {
            if (!def.Supports(environment)) throw new IvtOperationException(def.Title + " is not available for " + environment.Label() + ".");
        }

        public async Task<IvtTable> SearchAsync(IvtEntityDef def, IvtEnvironment environment, string search, CancellationToken cancellation)
        {
            RequireEnvironment(def, environment);
            var table = await rt.Database.QueryProcedureAsync(IvtAdminRuntime.Target(environment), def.ReadProcedure,
                new[] { IvtParam.NVarChar("@searchtxt", 1000, NormalizeSearch(search)) }, cancellation).ConfigureAwait(false);
            table.RequireColumns(def.ReadProcedure, def.RequiredColumns);
            return table;
        }

        // ---------------- value parsing (strict; never substitutes defaults)
        public static object ParseValue(IvtFieldDef field, IvtParamSpec spec, JToken token, List<string> errors)
        {
            var raw = token == null || token.Type == JTokenType.Null ? null : token.Type == JTokenType.Boolean ? ((bool)token ? "true" : "false") : token.ToString(Newtonsoft.Json.Formatting.None).Trim('"').Trim();
            if (token != null && token.Type == JTokenType.String) raw = ((string)token).Trim();
            if (string.IsNullOrEmpty(raw))
            {
                if (field.Kind == IvtFieldKind.Boolean) { errors.Add(field.Label + ": choose yes or no."); return null; }
                if (field.Required) { errors.Add(field.Label + " is required."); return null; }
                return null;
            }
            switch (field.Kind)
            {
                case IvtFieldKind.Text:
                    if (spec.Size > 0 && raw.Length > spec.Size) { errors.Add(field.Label + " is longer than " + spec.Size + " characters."); return null; }
                    return raw;
                case IvtFieldKind.Boolean:
                    bool b;
                    if (raw == "1") b = true; else if (raw == "0") b = false;
                    else if (!bool.TryParse(raw, out b)) { errors.Add(field.Label + " must be yes or no."); return null; }
                    return spec.Type == SqlDbType.Int ? (object)(b ? 1 : 0) : b;
                case IvtFieldKind.Integer:
                    long l;
                    if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) { errors.Add(field.Label + " must be a whole number."); return null; }
                    if (spec.Type == SqlDbType.Int && (l < int.MinValue || l > int.MaxValue)) { errors.Add(field.Label + " is out of range."); return null; }
                    return spec.Type == SqlDbType.Int ? (object)(int)l : l;
                case IvtFieldKind.Float:
                    double d;
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || double.IsNaN(d) || double.IsInfinity(d)) { errors.Add(field.Label + " must be a number."); return null; }
                    return d;
                case IvtFieldKind.Money:
                case IvtFieldKind.Decimal:
                    decimal m;
                    if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out m)) { errors.Add(field.Label + " must be a number."); return null; }
                    var scale = field.Kind == IvtFieldKind.Money ? 4 : spec.Scale;
                    var precision = field.Kind == IvtFieldKind.Money ? 19 : spec.Precision;
                    if (decimal.Round(m, scale) != m) { errors.Add(field.Label + " allows at most " + scale + " decimal places."); return null; }
                    if (Math.Abs(decimal.Truncate(m)) >= (decimal)Math.Pow(10, precision - scale)) { errors.Add(field.Label + " is too large."); return null; }
                    return m;
            }
            errors.Add(field.Label + ": unsupported field type.");
            return null;
        }

        // Typed value of a database cell for comparison/carry-over (null stays null).
        private static object FromDatabase(IvtFieldDef field, IvtParamSpec spec, object value)
        {
            if (value == null) return null;
            switch (field.Kind)
            {
                case IvtFieldKind.Boolean:
                    var b = value is bool ? (bool)value : Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
                    return spec.Type == SqlDbType.Int ? (object)(b ? 1 : 0) : b;
                case IvtFieldKind.Integer: return spec.Type == SqlDbType.Int ? (object)Convert.ToInt32(value, CultureInfo.InvariantCulture) : Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case IvtFieldKind.Float: return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case IvtFieldKind.Money: case IvtFieldKind.Decimal: return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static bool SameValue(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is bool && b is bool) return (bool)a == (bool)b;
            // Input is trimmed, so surrounding whitespace already in the database is not treated as an edit.
            if (a is string || b is string) return string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture).Trim(), Convert.ToString(b, CultureInfo.InvariantCulture).Trim(), StringComparison.Ordinal);
            return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture);
        }

        private static string Show(object v) { return v == null ? null : v is bool ? ((bool)v ? "yes" : "no") : Convert.ToString(v, CultureInfo.InvariantCulture); }

        // Builds update parameters from submitted values; fields not submitted keep their current database value.
        private static List<IvtParam> UpdateParameters(IvtEntityDef def, IvtEnvironment environment, object[] current, IvtTable table, JObject submitted, List<string> errors, Dictionary<string, object> changes)
        {
            var parameters = new List<IvtParam>();
            foreach (var field in def.Fields.Where(f => f.Update != null))
            {
                var dbValue = FromDatabase(field, field.Update, table.Get(current, field.Column));
                object value = dbValue;
                if (!field.ReadOnly && submitted != null && submitted.Property(field.Column) != null)
                {
                    var fieldErrors = new List<string>();
                    value = ParseValue(field, field.Update, submitted[field.Column], fieldErrors);
                    // An empty text box cannot express NULL vs '': an existing empty string stays an empty string.
                    if (value == null && dbValue is string && ((string)dbValue).Trim().Length == 0) value = dbValue;
                    errors.AddRange(fieldErrors);
                    if (fieldErrors.Count == 0 && !SameValue(dbValue, value)) changes[field.Column] = new { from = Show(dbValue), to = Show(value) };
                }
                parameters.Add(new IvtParam(field.ParameterName(field.Update, environment), field.Update.Type, value, field.Update.Size, field.Update.Precision, field.Update.Scale));
            }
            return parameters;
        }

        private static long ParseId(JToken token)
        {
            long id;
            if (token == null || !long.TryParse(token.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0)
                throw new IvtOperationException("A valid record id is required.");
            return id;
        }

        // Re-reads the displayed result set on the server; refuses when it no longer matches what the operator confirmed.
        private async Task<Dictionary<long, object[]>> ConfirmedSetAsync(IvtEntityDef def, IvtEnvironment environment, string search, int? expectedCount, CancellationToken cancellation, IvtTable tableOut)
        {
            var table = await SearchAsync(def, environment, search, cancellation).ConfigureAwait(false);
            tableOut.Columns.AddRange(table.Columns);
            if (expectedCount.HasValue && table.Rows.Count != expectedCount.Value)
                throw new IvtOperationException("The result set changed since it was displayed (" + expectedCount + " rows shown, " + table.Rows.Count + " now). Refresh and review before saving. Nothing was changed.");
            var map = new Dictionary<long, object[]>();
            foreach (var row in table.Rows)
            {
                var id = table.Get(row, "Id");
                if (id != null) map[Convert.ToInt64(id, CultureInfo.InvariantCulture)] = row;
            }
            return map;
        }

        public Task<IvtActionResult> UpdateAsync(IvtEntityDef def, IvtEnvironment environment, string search, JObject row, IvtOperator op, CancellationToken cancellation)
        {
            return UpdateManyAsync(def, environment, search, null, new JArray(row), op, "UPDATE", cancellation);
        }

        public Task<IvtActionResult> UpdateAllAsync(IvtEntityDef def, IvtEnvironment environment, string search, int expectedCount, JArray rows, IvtOperator op, CancellationToken cancellation)
        {
            return UpdateManyAsync(def, environment, search, expectedCount, rows, op, "UPDATE_ALL", cancellation);
        }

        // Soft delete / reactivate or another toggle (On/Off, Mandatory, Receives email) for one record, using database values for all other fields.
        public Task<IvtActionResult> SetFlagAsync(IvtEntityDef def, IvtEnvironment environment, string search, long id, string column, bool value, IvtOperator op, CancellationToken cancellation)
        {
            var field = def.Fields.FirstOrDefault(f => f.Toggle && string.Equals(f.Column, column, StringComparison.OrdinalIgnoreCase));
            if (field == null) throw new IvtOperationException("That field cannot be toggled.");
            var row = new JObject { ["Id"] = id, [field.Column] = value };
            return UpdateManyAsync(def, environment, search, null, new JArray(row), op, field.Column == "IsDeleted" ? (value ? "DELETE" : "REACTIVATE") : "TOGGLE_" + field.Column.ToUpperInvariant(), cancellation);
        }

        private async Task<IvtActionResult> UpdateManyAsync(IvtEntityDef def, IvtEnvironment environment, string search, int? expectedCount, JArray rows, IvtOperator op, string verb, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            RequireEnvironment(def, environment);
            if (rows == null || rows.Count == 0) return IvtActionResult.Rejected("There are no rows to save.", correlation);
            if (rows.Count > MaxBatch) return IvtActionResult.Rejected("At most " + MaxBatch + " rows can be saved at once.", correlation);

            var table = new IvtTable();
            Dictionary<long, object[]> current;
            try { current = await ConfirmedSetAsync(def, environment, search, expectedCount, cancellation, table).ConfigureAwait(false); }
            catch (IvtOperationException ex) { return IvtActionResult.Rejected(ex.Message, correlation); }
            table.Rows.AddRange(current.Values);
            if (expectedCount.HasValue && rows.Count != expectedCount.Value)
                return IvtActionResult.Rejected("Update-all must include every displayed row (" + expectedCount + "); " + rows.Count + " were submitted. Nothing was changed.", correlation);

            // Validate everything first; nothing is written if any row is invalid or outside the confirmed set.
            var plans = new List<Tuple<long, List<IvtParam>, Dictionary<string, object>>>();
            var errors = new List<string>();
            var seen = new HashSet<long>();
            foreach (var token in rows)
            {
                var obj = token as JObject;
                long id;
                try { id = ParseId(obj == null ? null : obj["Id"]); } catch (IvtOperationException ex) { errors.Add(ex.Message); continue; }
                if (!seen.Add(id)) { errors.Add("Row " + id + " was submitted twice."); continue; }
                object[] dbRow;
                if (!current.TryGetValue(id, out dbRow)) { errors.Add("Row " + id + " is not in the current result for this search. Refresh first."); continue; }
                var rowErrors = new List<string>();
                var changes = new Dictionary<string, object>();
                var parameters = UpdateParameters(def, environment, dbRow, table, obj, rowErrors, changes);
                if (rowErrors.Count > 0) { errors.AddRange(rowErrors.Select(e => "Row " + id + ": " + e)); continue; }
                plans.Add(Tuple.Create(id, parameters, changes));
            }
            if (errors.Count > 0) return IvtActionResult.Rejected("Nothing was saved. " + string.Join(" ", errors.Take(20)) + (errors.Count > 20 ? " (+" + (errors.Count - 20) + " more)" : ""), correlation);

            var toWrite = plans.Where(p => p.Item3.Count > 0).ToList();
            var outcomes = plans.Where(p => p.Item3.Count == 0).Select(p => new IvtRowOutcome { Id = p.Item1, Status = "Unchanged" }).ToList();
            if (toWrite.Count == 0)
                return new IvtActionResult { Ok = true, Outcome = IvtOutcome.Succeeded, Message = "No changes to save.", CorrelationId = correlation, Data = outcomes };

            var audited = await AuditedOperation.BeginAsync(rt.Audit, new IvtAuditEntry
            {
                CorrelationId = correlation, Operator = op.Login, Environment = environment.Key(), Operation = "SETTINGS." + def.Key.ToUpperInvariant() + "." + verb,
                Target = "Id " + string.Join(",", toWrite.Select(p => p.Item1).Take(40)), AffectedCount = toWrite.Count
            }, cancellation).ConfigureAwait(false);
            var log = new List<object>();
            foreach (var plan in toWrite)
            {
                try
                {
                    await rt.Database.ExecuteProcedureAsync(IvtAdminRuntime.Target(environment), def.UpdateProcedure, plan.Item2, cancellation).ConfigureAwait(false);
                    outcomes.Add(new IvtRowOutcome { Id = plan.Item1, Status = "Updated" });
                    log.Add(new { id = plan.Item1, status = "Updated", changes = plan.Item3 });
                }
                catch (Exception ex)
                {
                    var message = IvtErrors.Describe(ex, correlation);
                    outcomes.Add(new IvtRowOutcome { Id = plan.Item1, Status = "Failed", Message = message });
                    log.Add(new { id = plan.Item1, status = "Failed", error = message, changes = plan.Item3 });
                }
            }
            var updated = outcomes.Count(o => o.Status == "Updated");
            var failed = outcomes.Count(o => o.Status == "Failed");
            var outcome = failed == 0 ? IvtOutcome.Succeeded : updated == 0 ? IvtOutcome.Failed : IvtOutcome.Partial;
            var result = new IvtActionResult
            {
                Ok = failed == 0, Outcome = outcome, CorrelationId = correlation, AuditId = audited.Entry.Id, Data = outcomes,
                Message = failed == 0 ? updated + " record(s) saved" + (outcomes.Count(o => o.Status == "Unchanged") > 0 ? ", " + outcomes.Count(o => o.Status == "Unchanged") + " unchanged." : ".")
                        : updated + " saved, " + failed + " FAILED. Failed rows were not changed; see the row messages."
            };
            result.Warning = await audited.CompleteAsync(outcome, updated, null, log, cancellation).ConfigureAwait(false);
            return result;
        }

        public async Task<IvtActionResult> CreateAsync(IvtEntityDef def, IvtEnvironment environment, JObject values, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            RequireEnvironment(def, environment);
            var errors = new List<string>();
            var parameters = new List<IvtParam>();
            var shown = new Dictionary<string, string>();
            foreach (var field in def.Fields.Where(f => f.Create != null))
            {
                object value;
                if (field.HasCreateFixedValue) value = field.CreateFixedValue;
                else
                {
                    value = ParseValue(field, field.Create, values == null ? null : values[field.Column], errors);
                    shown[field.Column] = Show(value);
                }
                parameters.Add(new IvtParam(field.ParameterName(field.Create, environment), field.Create.Type, value, field.Create.Size, field.Create.Precision, field.Create.Scale));
            }
            if (errors.Count > 0) return IvtActionResult.Rejected("Nothing was created. " + string.Join(" ", errors), correlation);
            var audited = await AuditedOperation.BeginAsync(rt.Audit, new IvtAuditEntry
            {
                CorrelationId = correlation, Operator = op.Login, Environment = environment.Key(), Operation = "SETTINGS." + def.Key.ToUpperInvariant() + ".CREATE", AffectedCount = 1
            }, cancellation).ConfigureAwait(false);
            try
            {
                await rt.Database.ExecuteProcedureAsync(IvtAdminRuntime.Target(environment), def.CreateProcedure, parameters, cancellation).ConfigureAwait(false);
                var result = new IvtActionResult { Ok = true, Outcome = IvtOutcome.Succeeded, Message = "Record created.", CorrelationId = correlation, AuditId = audited.Entry.Id };
                result.Warning = await audited.CompleteAsync(IvtOutcome.Succeeded, 1, null, new { values = shown }, cancellation).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                var message = IvtErrors.Describe(ex, correlation);
                var result = new IvtActionResult { Ok = false, Outcome = IvtOutcome.Failed, Message = "The record was not created: " + message, CorrelationId = correlation, AuditId = audited.Entry.Id };
                result.Warning = await audited.CompleteAsync(IvtOutcome.Failed, 0, null, new { values = shown, error = message }, cancellation).ConfigureAwait(false);
                return result;
            }
        }

        // ---------------- shared email contacts (IVT_Run_Control via the existing procedures)
        public async Task<IvtTable> ContactsAsync(IvtEnvironment environment, CancellationToken cancellation)
        {
            var table = await rt.Database.QueryProcedureAsync(IvtAdminRuntime.Target(environment), "WebIVT.Get_CC_EmailContactDetails", new IvtParam[0], cancellation).ConfigureAwait(false);
            table.RequireColumns("Get_CC_EmailContactDetails", new[] { "CC_Contact", "EXEC_Contact", "ADMIN_Contact" });
            return table;
        }

        public async Task<IvtActionResult> UpdateContactsAsync(IvtEnvironment environment, string cc, string exec, string admin, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            var errors = new List<string>();
            foreach (var pair in new[] { Tuple.Create("CC contacts", cc), Tuple.Create("Executive contacts", exec), Tuple.Create("Admin contacts", admin) })
            {
                var invalid = IvtEmail.InvalidAddresses(pair.Item2);
                if (invalid.Count > 0) errors.Add(pair.Item1 + ": not valid email address(es): " + string.Join(", ", invalid) + ".");
                // Column is VARCHAR(1000) although the parameter is NVARCHAR(1000/2000): enforce the column limit.
                if ((pair.Item2 ?? "").Length > 1000) errors.Add(pair.Item1 + " is longer than 1000 characters.");
            }
            if (errors.Count > 0) return IvtActionResult.Rejected("Nothing was saved. " + string.Join(" ", errors), correlation);
            Func<string, string> norm = v => string.Join("; ", IvtEmail.SplitAddresses(v));
            var before = await ContactsAsync(environment, cancellation).ConfigureAwait(false);
            var audited = await AuditedOperation.BeginAsync(rt.Audit, new IvtAuditEntry
                { CorrelationId = correlation, Operator = op.Login, Environment = environment.Key(), Operation = "SETTINGS.CONTACTS.UPDATE", AffectedCount = 1 }, cancellation).ConfigureAwait(false);
            try
            {
                await rt.Database.ExecuteProcedureAsync(IvtAdminRuntime.Target(environment), "WebIVT.sp_Update_CC_EmailContactDetails", new[]
                {
                    IvtParam.NVarChar("@CC_Contact", 2000, norm(cc)), IvtParam.NVarChar("@Exec_Contact", 2000, norm(exec)), IvtParam.NVarChar("@Admin_Contact", 2000, norm(admin))
                }, cancellation).ConfigureAwait(false);
                var result = new IvtActionResult { Ok = true, Outcome = IvtOutcome.Succeeded, Message = "Shared contacts saved.", CorrelationId = correlation, AuditId = audited.Entry.Id };
                var old = before.Rows.FirstOrDefault();
                result.Warning = await audited.CompleteAsync(IvtOutcome.Succeeded, 1, null, new
                {
                    cc = new { from = old == null ? null : before.GetString(old, "CC_Contact"), to = norm(cc) },
                    exec = new { from = old == null ? null : before.GetString(old, "EXEC_Contact"), to = norm(exec) },
                    admin = new { from = old == null ? null : before.GetString(old, "ADMIN_Contact"), to = norm(admin) }
                }, cancellation).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                var message = IvtErrors.Describe(ex, correlation);
                var result = new IvtActionResult { Ok = false, Outcome = IvtOutcome.Failed, Message = "Contacts were not saved: " + message, CorrelationId = correlation, AuditId = audited.Entry.Id };
                result.Warning = await audited.CompleteAsync(IvtOutcome.Failed, 0, null, new { error = message }, cancellation).ConfigureAwait(false);
                return result;
            }
        }
    }
}
