using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AFSDataGateway.Services.Chat
{
    public sealed class ChatQueryService
    {
        private readonly IChatDataSource data;
        private readonly ChatAccessScope access;
        public ChatQueryService(IChatDataSource data, ChatAccessScope access) { this.data = data; this.access = access; }

        public object Execute(string name, JObject args)
        {
            if (args == null) throw new ChatQueryException("Tool arguments are required.");
            var allowed = new HashSet<string>(new[] { "accountManager" });
            switch (name)
            {
                case "GetExceptionSummary": case "CompareExceptionPeriods": break;
                case "GetTopExceptionCustomers": allowed.UnionWith(new[] { "sortBy", "limit" }); break;
                case "GetConsignmentExceptions": allowed.UnionWith(new[] { "connote", "limit" }); break;
                case "GetExceptionRuleDefinition": allowed.Add("ruleCode"); break;
                case "GetTradingVolume": allowed.UnionWith(new[] { "financeGroup", "carrier", "sortBy", "limit" }); break;
                case "QueryDataset": allowed.UnionWith(new[] { "dataset", "filters", "groupBy", "metrics", "sortByMetric", "ascending", "limit", "listRecords", "columns" }); break;
                case "LookupCodes": allowed.UnionWith(new[] { "kind", "text" }); break;
                default: throw new ChatQueryException("Unsupported query. Choose one of the approved functions.");
            }
            if (args.Properties().Any(x => !allowed.Contains(x.Name)))
                throw new ChatQueryException("Unsupported filter. This release supports the dashboard reporting periods and current consignment exceptions only.");
            var scope = access.ForAccountManager(Text(args, "accountManager", false, 100));
            object result;
            switch (name)
            {
                case "GetExceptionSummary": result = Summary(scope); break;
                case "CompareExceptionPeriods": result = Compare(scope); break;
                case "GetTopExceptionCustomers": result = TopCustomers(scope, Text(args, "sortBy", false, 20) ?? "count", Limit(args, 10)); break;
                case "GetConsignmentExceptions": result = Consignment(scope, Text(args, "connote", true, 100), Limit(args, 20)); break;
                case "QueryDataset": result = Dataset(scope, args); break;
                case "LookupCodes": result = Lookup(scope, Text(args, "kind", true, 20), Text(args, "text", true, 100)); break;
                case "GetTradingVolume":
                    var group = Text(args, "financeGroup", false, 100);
                    result = (group == null ? null : NotInPortfolio(scope, new[] { group }))
                             ?? Trading(scope, group, Text(args, "carrier", false, 100), Text(args, "sortBy", false, 20) ?? "volume", Limit(args, 10));
                    break;
                default: result = Rule(scope, Text(args, "ruleCode", true, 20)); break;
            }
            var runs = scope.Apply(data.Runs, x => x.LoginName);
            return new
            {
                scope = scope.Label,
                reportingWeek = runs.Max(x => x.CurrentWeek),
                refreshedFrom = runs.Min(x => (DateTime?)x.ActionTime),
                refreshedTo = runs.Max(x => (DateTime?)x.ActionTime),
                refreshTimeZone = "Database local time; timezone unspecified",
                source = name == "QueryDataset" ? "IVT dataset " + (string)args["dataset"] :
                    name == "GetConsignmentExceptions" ? "Current IVT exception records" :
                    name == "GetTradingVolume" ? "IVT dashboard Trading Volume Analysis" :
                    name == "LookupCodes" ? "AFSXchange carrier/customer reference data" : "IVT dashboard summaries",
                periodNote = "Current and previous week mean the loaded dashboard reporting periods, not calendar weeks. Refresh times describe dashboard runs; current records may have changed since that run.",
                result
            };
        }

        private object Summary(ChatAccessScope scope)
        {
            var rows = RuleCounts(scope);
            return new
            {
                totalExceptionOccurrences = rows.Sum(x => x.Current),
                ruleCount = rows.Count,
                rules = rows.OrderByDescending(x => x.Current).ThenBy(x => x.Code).Take(50).ToList(),
                truncated = rows.Count > 50,
                metric = "Exception occurrences from the dashboard; a consignment can have multiple exceptions. Not a count of distinct consignments."
            };
        }

        private object Compare(ChatAccessScope scope)
        {
            var rows = RuleCounts(scope);
            long current = rows.Sum(x => x.Current), previous = rows.Sum(x => x.Previous);
            return new
            {
                current, previous, change = current - previous, percentageChange = PercentageChange(current, previous),
                metric = "Dashboard exception occurrences; percentage change is undefined when the previous count is zero.",
                byRule = rows.OrderByDescending(x => Math.Abs(x.Current - x.Previous)).ThenBy(x => x.Code).Take(50)
                    .Select(x => new { x.Code, x.Description, x.Current, x.Previous, change = x.Current - x.Previous, percentageChange = PercentageChange(x.Current, x.Previous) }).ToList(),
                truncated = rows.Count > 50
            };
        }

        private List<RuleCount> RuleCounts(ChatAccessScope scope)
        {
            // Group and calculate in SQL. The model never receives unscoped rows.
            return scope.Apply(data.Summaries, x => x.LoginName)
                .GroupBy(x => new { x.ErrCode, x.ErrDesc })
                .Select(g => new RuleCount { Code = g.Key.ErrCode, Description = g.Key.ErrDesc,
                    Current = g.Sum(x => (long?)x.CurrentWeekCount) ?? 0,
                    Previous = g.Sum(x => (long?)x.LastWeekCount) ?? 0 }).ToList();
        }

        private object TopCustomers(ChatAccessScope scope, string sortBy, int limit)
        {
            if (sortBy != "count" && sortBy != "increase" && sortBy != "decrease")
                throw new ChatQueryException("sortBy must be count, increase or decrease.");
            var grouped = scope.Apply(data.Customers, x => x.LoginName).GroupBy(x => x.Customer)
                .Select(g => new CustomerCount { Customer = g.Key,
                    Current = g.Sum(x => (long?)x.CurrentWeekCount) ?? 0,
                    Previous = g.Sum(x => (long?)x.LastWeekCount) ?? 0 });
            if (sortBy == "increase") grouped = grouped.Where(x => x.Current > x.Previous);
            if (sortBy == "decrease") grouped = grouped.Where(x => x.Current < x.Previous);
            var sorted = sortBy == "increase" ? grouped.OrderByDescending(x => x.Current - x.Previous) :
                sortBy == "decrease" ? grouped.OrderBy(x => x.Current - x.Previous) : grouped.OrderByDescending(x => x.Current);
            var rows = sorted.ThenBy(x => x.Customer).Take(limit + 1).ToList();
            return new { sortBy, truncated = rows.Count > limit,
                customers = rows.Take(limit).Select(x => new { x.Customer, x.Current, x.Previous,
                    change = x.Current - x.Previous, percentageChange = PercentageChange(x.Current, x.Previous) }).ToList(),
                metric = "Customer exception counts from the dashboard; not distinct consignments." };
        }

        private object Consignment(ChatAccessScope scope, string connote, int limit)
        {
            var query = scope.Apply(data.Exceptions, x => x.LoginName)
                .Where(x => (x.con_note == connote || x.Connote == connote) && x.IsDeleted != true);
            var total = query.LongCount();
            var rows = query.OrderBy(x => x.customer).ThenBy(x => x.Id).ThenBy(x => x.ErrCode).Take(limit)
                .Select(x => new { recordId = x.Id, connote = x.con_note ?? x.Connote, customer = x.customer,
                    carrier = x.carrier, reportingWeek = x.CurrentWeek, ruleCode = x.ErrCode,
                    description = x.ErrDesc, priority = x.ErrPriority, reason = x.Sub_Reason }).ToList();
            return new { total, truncated = total > limit, exceptions = rows,
                note = "An empty result means no current exceptions were found in the permitted scope; it does not establish whether a consignment exists elsewhere." };
        }

        private object Rule(ChatAccessScope scope, string ruleCode)
        {
            int code;
            if (!int.TryParse(ruleCode, out code) || code < 0) throw new ChatQueryException("Use the numeric exception rule code.");
            var definitions = scope.Apply(data.Summaries, x => x.LoginName).Where(x => x.ErrCode == code)
                .Select(x => new { code = x.ErrCode, description = x.ErrDesc, priority = x.ErrPriority, type = x.RuleType })
                .Distinct().Take(20).ToList();
            return new { definitions, note = "Definitions visible in this user's dashboard. No resolution procedure or root cause is inferred." };
        }

        // Emailed reports: same validation and scope as QueryDataset, but up to MaxExportRows records.
        // Without groupBy/metrics the report lists records; otherwise it is the aggregated table.
        public object ExportDataset(JObject args)
        {
            if (args == null) throw new ChatQueryException("Report arguments are required.");
            var allowed = new HashSet<string>(new[] { "accountManager", "dataset", "filters", "groupBy", "metrics", "sortByMetric", "ascending", "limit", "listRecords", "columns" });
            if (args.Properties().Any(x => !allowed.Contains(x.Name))) throw new ChatQueryException("Unsupported report option.");
            var scope = access.ForAccountManager(Text(args, "accountManager", false, 100));
            return Dataset(scope, args, ChatDataset<object>.MaxExportRows);
        }

        private object Dataset(ChatAccessScope scope, JObject args, int exportRowCap = 0)
        {
            IChatDataset dataset;
            var name = Text(args, "dataset", true, 50);
            if (!ChatDatasets.All.TryGetValue(name, out dataset))
                throw new ChatQueryException("Unknown dataset. Use one of: " + string.Join(", ", ChatDatasets.All.Keys) + ".");
            var query = new ChatDatasetQuery
            {
                SortMetric = Integer(args, "sortByMetric", 0, 0, 3),
                Ascending = Flag(args, "ascending"),
                ListRecords = Flag(args, "listRecords") || (exportRowCap > 0 && args["groupBy"] == null && args["metrics"] == null),
                ExportRowCap = exportRowCap,
                Limit = Integer(args, "limit", 20, 1, 50),
                GroupBy = Strings(args["groupBy"], "groupBy", 2),
                Columns = Strings(args["columns"], "columns", 30)
            };
            foreach (var item in Objects(args["filters"], "filters", 8))
            {
                if (item.Properties().Any(p => p.Name != "field" && p.Name != "op" && p.Name != "values"))
                    throw new ChatQueryException("Each filter has only field, op and values.");
                query.Filters.Add(new ChatFilter { Field = Text(item, "field", true, 50), Op = Text(item, "op", true, 20),
                    Values = Strings(item["values"], "values", 20) });
            }
            foreach (var item in Objects(args["metrics"], "metrics", 4))
            {
                if (item.Properties().Any(p => p.Name != "fn" && p.Name != "field"))
                    throw new ChatQueryException("Each metric has only fn and field.");
                query.Metrics.Add(new ChatMetric { Fn = Text(item, "fn", true, 20), Field = Text(item, "field", false, 50) });
            }
            // Fast answer when the user names a customer that is not in their portfolio: no data query is run.
            var notOwned = NotInPortfolio(scope, query.Filters.Where(f => CustomerFields.Contains(f.Field) && string.Equals(f.Op, "equals", StringComparison.OrdinalIgnoreCase))
                                                             .SelectMany(f => f.Values ?? new List<string>()));
            if (notOwned != null) return notOwned;
            try { return dataset.Run(data, scope, query); }
            catch (Exception ex) when (IsSqlTimeout(ex))
            {
                throw new ChatQueryException("The query took too long. Add filters (customer, carrier or a reportingWeek range) and try again.");
            }
        }

        private static bool IsSqlTimeout(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var sql = e as System.Data.SqlClient.SqlException;
                if (sql != null && sql.Number == -2) return true;
            }
            return false;
        }

        private static readonly HashSet<string> CustomerFields = new HashSet<string>(
            new[] { "customer", "customerGroup", "customerOrGroup", "financeGroup" }, StringComparer.OrdinalIgnoreCase);
        private HashSet<string> portfolio;  // loaded once per request

        // Customer codes, customer codes and finance groups routed to the AM (same mapping IVT uses for routing).
        // Returns null for admins, or when the portfolio cannot be determined (then the normal scoped query runs).
        private HashSet<string> Portfolio(ChatAccessScope scope)
        {
            if (scope.IsAdmin) return null;
            if (portfolio != null) return portfolio.Count == 0 ? null : portfolio;
            portfolio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = data.Query(
                    @"SELECT c.CustomerSubCode, c.CustomerCode, c.FinanceGroup
                        FROM dbo.vAccountManager am
                        JOIN AFSXchange.dbo.Customers c ON c.CustomerCode COLLATE DATABASE_DEFAULT = am.CustomerCode COLLATE DATABASE_DEFAULT
                       WHERE am.LoginName = @login",
                    new[] { new System.Data.SqlClient.SqlParameter("@login", scope.LoginName) });
                foreach (var row in rows)
                    foreach (var value in row)
                        if (value != null && !string.IsNullOrWhiteSpace(value.ToString())) portfolio.Add(value.ToString().Trim());
            }
            catch (Exception)
            {
                portfolio.Clear();  // fail open to the normal (still scoped) query
            }
            return portfolio.Count == 0 ? null : portfolio;
        }

        // Maps names to codes (e.g. "BlueStar" -> carrier group BLUESTAR with codes BLU, BLUE, BSTARGL) from AFSXchange.
        // Carriers are reference data; customers are limited to the AM's own portfolio.
        private object Lookup(ChatAccessScope scope, string kind, string text)
        {
            var like = new System.Data.SqlClient.SqlParameter("@text", System.Data.SqlDbType.NVarChar, 120)
                { Value = "%" + text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%" };
            if (string.Equals(kind, "carrier", StringComparison.OrdinalIgnoreCase))
            {
                var rows = data.Query(
                    @"SELECT TOP 40 c.CarrierCode, c.CarrierName, c.CarrierGroup
                        FROM AFSXchange.dbo.Carriers c
                       WHERE c.CarrierCode LIKE @text OR c.CarrierName LIKE @text OR c.CarrierGroup LIKE @text
                          OR c.CarrierGroup IN (SELECT g.CarrierGroup FROM AFSXchange.dbo.Carriers g WHERE g.CarrierCode LIKE @text OR g.CarrierName LIKE @text)
                       ORDER BY c.CarrierGroup, c.CarrierCode", new[] { like });
                return new
                {
                    carriers = rows.Select(r => new { carrierCode = r[0], carrierName = r[1], carrierGroup = r[2] }).ToList(),
                    note = "Filter datasets with carrierGroup (or carrierOrGroup) = the group to include all its carrier codes, or carrier = a code for one."
                };
            }
            if (!string.Equals(kind, "customer", StringComparison.OrdinalIgnoreCase))
                throw new ChatQueryException("kind must be carrier or customer.");
            var sql = @"SELECT TOP 40 c.CustomerSubCode, c.CustomerCode, c.CustomerName, c.FinanceGroup
                          FROM AFSXchange.dbo.Customers c
                         WHERE (c.CustomerSubCode LIKE @text OR c.CustomerCode LIKE @text OR c.CustomerName LIKE @text OR c.FinanceGroup LIKE @text)";
            var parameters = new List<System.Data.SqlClient.SqlParameter> { like };
            if (!scope.IsAdmin)
            {
                sql += @" AND c.CustomerCode COLLATE DATABASE_DEFAULT IN (SELECT am.CustomerCode COLLATE DATABASE_DEFAULT FROM dbo.vAccountManager am WHERE am.LoginName = @login)";
                parameters.Add(new System.Data.SqlClient.SqlParameter("@login", scope.LoginName));
            }
            var customers = data.Query(sql + " ORDER BY c.FinanceGroup, c.CustomerCode", parameters);
            return new
            {
                customers = customers.Select(r => new { customerSubCode = r[0], customerCode = r[1], customerName = r[2], financeGroup = r[3] }).ToList(),
                note = scope.IsAdmin ? "Use customerOrGroup with the code or finance group." :
                    "Only the user's own customers are searched; if none match, the customer may not be in their portfolio."
            };
        }

        private object NotInPortfolio(ChatAccessScope scope, IEnumerable<string> customers)
        {
            var requested = customers.Select(c => (c ?? "").Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (requested.Count == 0) return null;
            var mine = Portfolio(scope);
            if (mine == null) return null;
            var foreign = requested.Where(c => !mine.Contains(c)).ToList();
            if (foreign.Count < requested.Count) return null;  // at least one requested customer is theirs: run the query
            return new
            {
                status = "not_your_account",
                customers = foreign,
                message = string.Join(", ", foreign) + (foreign.Count == 1 ? " is" : " are") +
                          " not in your customer portfolio, so its data is not available to you. No query was run.",
                note = "Tell the user plainly that this customer/group is not one of their accounts (do NOT say 0 results, and never say who manages it). " +
                       "Suggest checking the customer code, or asking an IVT admin if they think it should be theirs."
            };
        }

        private static List<string> Strings(JToken token, string key, int maximum)
        {
            if (token == null || token.Type == JTokenType.Null) return new List<string>();
            var array = token as JArray;
            if (array == null || array.Count > maximum || array.Any(x => x.Type != JTokenType.String && x.Type != JTokenType.Integer && x.Type != JTokenType.Float))
                throw new ChatQueryException(key + " must be a list of at most " + maximum + " values.");
            return array.Select(x => ((string)x ?? "").Trim()).ToList();
        }

        private static List<JObject> Objects(JToken token, string key, int maximum)
        {
            if (token == null || token.Type == JTokenType.Null) return new List<JObject>();
            var array = token as JArray;
            if (array == null || array.Count > maximum || array.Any(x => x.Type != JTokenType.Object))
                throw new ChatQueryException(key + " must be a list of at most " + maximum + " objects.");
            return array.Cast<JObject>().ToList();
        }

        private static int Integer(JObject args, string key, int fallback, int minimum, int maximum)
        {
            var token = args[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            int value;
            if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out value) || value < minimum || value > maximum)
                throw new ChatQueryException(key + " must be an integer from " + minimum + " to " + maximum + ".");
            return value;
        }

        private static bool Flag(JObject args, string key)
        {
            var token = args[key];
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type != JTokenType.Boolean) throw new ChatQueryException(key + " must be true or false.");
            return (bool)token;
        }

        private object Trading(ChatAccessScope scope, string financeGroup, string carrier, string sortBy, int limit)
        {
            if (sortBy != "volume" && sortBy != "increase" && sortBy != "decrease")
                throw new ChatQueryException("sortBy must be volume, increase or decrease.");
            // Exact names first (case-insensitive collation); fall back to partial matches if nothing matches exactly.
            var scoped = scope.Apply(data.Trading, x => x.LoginName);
            var exact = scoped;
            if (financeGroup != null) exact = exact.Where(x => x.FinanceGroup.Trim() == financeGroup);
            if (carrier != null) exact = exact.Where(x => x.Carrier.Trim() == carrier);
            var partial = (financeGroup != null || carrier != null) && !exact.Any();
            var filtered = scoped;
            if (partial)
            {
                if (financeGroup != null) filtered = filtered.Where(x => x.FinanceGroup.Contains(financeGroup));
                if (carrier != null) filtered = filtered.Where(x => x.Carrier.Contains(carrier));
            }
            else filtered = exact;

            // Same grouping and sums as the dashboard's Trading Volume Analysis panel.
            var grouped = filtered.GroupBy(x => new { x.FinanceGroup, x.Carrier })
                .Select(g => new TradingRow { FinanceGroup = g.Key.FinanceGroup, Carrier = g.Key.Carrier,
                    Current = g.Sum(x => (long?)x.CurrentWeekTradingCount) ?? 0,
                    LastWeek = g.Sum(x => (long?)x.LastWeekTradingCount) ?? 0,
                    Average13 = g.Sum(x => (long?)x.Last13WeeksTradingCountAVG) ?? 0 });
            var total = grouped.Count();
            var sorted = sortBy == "increase" ? grouped.OrderByDescending(x => x.Current - x.Average13) :
                sortBy == "decrease" ? grouped.OrderBy(x => x.Current - x.Average13) : grouped.OrderByDescending(x => x.Current);
            var rows = sorted.ThenBy(x => x.FinanceGroup).ThenBy(x => x.Carrier).Take(limit).ToList();
            return new
            {
                sortBy, matchType = partial ? "partial" : "exact", totalGroups = total, truncated = total > limit,
                totalCurrentWeekVolume = rows.Count == total ? rows.Sum(x => x.Current) : (long?)null,
                groups = rows.Select(x => new { financeGroup = x.FinanceGroup, carrier = x.Carrier,
                    currentWeekVolume = x.Current, lastWeekVolume = x.LastWeek, average13WeeksVolume = x.Average13,
                    varianceVsAverage = x.Current - x.Average13, variancePercentVsAverage = PercentageChange(x.Current, x.Average13) }).ToList(),
                metric = "Trading volume = connotes traded in the loaded dashboard week, grouped by finance (customer) group and carrier group, as shown in the dashboard's Trading Volume Analysis. Variance is against the 13-week average.",
                note = partial ? "No exact name match; results use partial name matches, so confirm the intended group with the user." : null
            };
        }

        public static decimal? PercentageChange(long current, long previous)
        {
            return previous == 0 ? (decimal?)null : Math.Round((current - previous) * 100m / previous, 2);
        }

        private static string Text(JObject args, string key, bool required, int maximum)
        {
            var value = args[key];
            if (value == null || value.Type == JTokenType.Null)
            {
                if (required) throw new ChatQueryException(key + " is required.");
                return null;
            }
            if (value.Type != JTokenType.String) throw new ChatQueryException(key + " must be text.");
            var text = ((string)value).Trim();
            if (text.Length > maximum || (required && text.Length == 0)) throw new ChatQueryException(key + " has an invalid length.");
            return text.Length == 0 ? null : text;
        }

        private static int Limit(JObject args, int fallback)
        {
            var token = args["limit"];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            int value;
            if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out value) || value < 1 || value > 50)
                throw new ChatQueryException("limit must be an integer from 1 to 50.");
            return value;
        }

        private sealed class RuleCount
        {
            public int? Code { get; set; }
            public string Description { get; set; }
            public long Current { get; set; }
            public long Previous { get; set; }
        }
        private sealed class TradingRow
        {
            public string FinanceGroup { get; set; }
            public string Carrier { get; set; }
            public long Current { get; set; }
            public long LastWeek { get; set; }
            public long Average13 { get; set; }
        }
        private sealed class CustomerCount
        {
            public string Customer { get; set; }
            public long Current { get; set; }
            public long Previous { get; set; }
        }
    }

    public sealed class ChatQueryException : Exception
    {
        public ChatQueryException(string message) : base(message) { }
    }
}
