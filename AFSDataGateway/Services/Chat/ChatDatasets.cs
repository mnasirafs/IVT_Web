using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;

namespace AFSDataGateway.Services.Chat
{
    // Catalog of approved datasets for the QueryDataset tool. Each dataset fixes the view, the ownership column used by
    // the matching IVT screen, and a whitelist of fields. The model can only combine these fields with fixed operators;
    // it never supplies SQL, column names outside the whitelist, or the ownership filter.
    public static class ChatDatasets
    {
        public static readonly IReadOnlyDictionary<string, IChatDataset> All = Build();

        private static IReadOnlyDictionary<string, IChatDataset> Build()
        {
            var list = new List<IChatDataset>
            {
                new ChatDataset<vConsignmentForReview>("heldConsignments",
                    "Consignments currently HELD for review (the Held Consignments screen). For an Account Manager these are the holds they actioned ('I am holding').",
                    x => x.ActionedBy)
                    .Key("connote").Recency(x => x.ActionTime)
                    .Text("connote", x => x.Connote, "consignment note")
                    .Text("jobNumber", x => x.jno, "BMS job number")
                    .Text("customer", x => x.Customer, "customer code, e.g. CHWI")
                    .Text("customerGroup", x => x.FinanceGroup, "finance/customer group, e.g. CH2")
                    .AnyText("customerOrGroup", "matches customer OR customer group; use when the user just says 'customer X'", x => x.Customer, x => x.FinanceGroup)
                    .Text("carrier", x => x.Carrier, "carrier code")
                    .Text("reason", x => x.Reason, "hold reason")
                    .Text("subReason", x => x.Sub_Reason, "hold sub-reason")
                    .Text("ruleCode", x => x.ErrCode, "exception rule code")
                    .Text("ruleName", x => x.ShortErrDesc, "short rule name, e.g. Negative Gross Margin")
                    .Text("rulePriority", x => x.ErrPriority, "rule priority")
                    .Text("ruleType", x => x.RuleType, "rule type")
                    .AnyText("reasonText", "matches reason, sub-reason, rule name or comment; best for 'held for <reason>'", x => x.Reason, x => x.Sub_Reason, x => x.ShortErrDesc, x => x.Comment)
                    .Text("comment", x => x.Comment, "hold comment")
                    .Text("heldBy", x => x.HeldBy, "login that held it")
                    .Text("accountManager", x => x.AccountManager, "account manager code")
                    .Text("workCode", x => x.work_code, "work code")
                    .Text("from", x => x.from, "origin ZONE code (e.g. MEL, NZ) - NOT a postcode")
                    .Text("to", x => x.to, "destination ZONE code - NOT a postcode")
                    .Text("pickupPostcode", x => x.col_post, "sender/pickup postcode")
                    .Text("deliveryPostcode", x => x.del_post, "receiver/delivery postcode")
                    .Number("autoHeld", x => x.AutoHeld, "1 = held automatically, 0 = held manually")
                    .Date("heldAt", x => x.ActionTime, "when it was held")
                    .Date("invoiceWeek", x => x.InvoiceWeek, "invoice week")
                    .Date("reportingWeek", x => x.CurrentWeek, "IVT reporting week"),

                new ChatDataset<vReleasedConsignment>("releasedConsignments",
                    "Consignments RELEASED after review, with who released them and the justification. Scoped to releases the Account Manager actioned.",
                    x => x.ActionedBy)
                    .Key("connote").Recency(x => x.ActionTime)
                    .Text("connote", x => x.Connote, "consignment note")
                    .Text("jobNumber", x => x.jno, "BMS job number")
                    .Text("customer", x => x.Customer, "customer code")
                    .Text("carrier", x => x.Carrier, "carrier code")
                    .Text("reason", x => x.Reason, "original hold reason")
                    .Text("ruleCode", x => x.ErrCode, "exception rule code")
                    .Text("ruleName", x => x.ShortErrDesc, "short rule name")
                    .AnyText("reasonText", "matches reason, sub-reason, rule name, comment or justification", x => x.Reason, x => x.Sub_Reason, x => x.ShortErrDesc, x => x.Comment, x => x.Justification)
                    .Text("releasedBy", x => x.ReleasedBy, "login that released it")
                    .Text("justification", x => x.Justification, "release justification")
                    .Text("comment", x => x.Comment, "comment")
                    .Text("workCode", x => x.work_code, "work code")
                    .Text("pickupPostcode", x => x.col_post, "sender/pickup postcode")
                    .Text("deliveryPostcode", x => x.del_post, "receiver/delivery postcode")
                    .Date("releasedAt", x => x.ActionTime, "when it was released")
                    .Date("invoiceWeek", x => x.InvoiceWeek, "invoice week")
                    .Date("reportingWeek", x => x.CurrentWeek, "IVT reporting week"),

                new ChatDataset<vIVT_Result>("currentExceptions",
                    "Exceptions raised in the CURRENT IVT run (IVT Results screen). One row per exception; a connote can have several.",
                    x => x.LoginName)
                    .WhereSql("[IsDeleted] IS NULL OR [IsDeleted] = 0")
                    .Key("connote").Recency(x => x.CurrentWeek)
                    .Text("connote", x => x.con_note, "consignment note")
                    .Text("jobNumber", x => x.jno, "BMS job number")
                    .Text("customer", x => x.customer, "customer code")
                    .Text("customerGroup", x => x.FinanceGroup, "finance/customer group")
                    .AnyText("customerOrGroup", "matches customer, customer code OR customer group", x => x.customer, x => x.CustomerCode, x => x.FinanceGroup)
                    .Text("carrier", x => x.carrier, "carrier code")
                    .Text("ruleCode", x => x.ErrCode, "exception rule code")
                    .Text("ruleName", x => x.ShortErrDesc, "short rule name, e.g. Negative Gross Margin")
                    .Text("ruleDescription", x => x.ErrDesc, "full rule description")
                    .Text("rulePriority", x => x.ErrPriority, "rule priority")
                    .Text("ruleType", x => x.RuleType, "rule type")
                    .AnyText("reasonText", "matches rule name, description, reason or sub-reason", x => x.ShortErrDesc, x => x.ErrDesc, x => x.Reason, x => x.Sub_Reason)
                    .Text("heldBy", x => x.HeldBy, "login that held it, blank if not held")
                    .Text("beneficiary", x => x.Beneficiary, "rule beneficiary")
                    .Text("accountManager", x => x.AccountManager, "account manager code")
                    .Text("workCode", x => x.work_code, "work code")
                    .Text("from", x => x.from_loc, "origin ZONE code (e.g. MEL, NZ) - NOT a postcode")
                    .Text("to", x => x.to, "destination ZONE code - NOT a postcode")
                    .Text("pickupPostcode", x => x.col_post, "sender/pickup postcode")
                    .Text("deliveryPostcode", x => x.del_post, "receiver/delivery postcode")
                    .Number("sellTotalNett", x => x.total_nett, "sell total nett ($)")
                    .Number("grossMargin", x => x.GrossMargin, "gross margin ($); negative = loss")
                    .Date("reportingWeek", x => x.CurrentWeek, "IVT reporting week"),

                new ChatDataset<vIVT_ResultHistory>("exceptionHistory",
                    "Exceptions from PAST IVT runs (Exception History screen). A reportingWeek filter is REQUIRED (keep ranges to a few months); queries can take up to a minute.",
                    x => x.LoginName)
                    .Key("connote").Recency(x => x.CurrentWeek).Requires("reportingWeek")
                    .Text("connote", x => x.con_note, "consignment note")
                    .Text("jobNumber", x => x.jno, "BMS job number")
                    .Text("customer", x => x.customer, "customer code")
                    .Text("customerGroup", x => x.FinanceGroup, "finance/customer group")
                    .AnyText("customerOrGroup", "matches customer OR customer group", x => x.customer, x => x.FinanceGroup)
                    .Text("carrier", x => x.carrier, "carrier code")
                    .Text("ruleCode", x => x.ErrCode, "exception rule code")
                    .Text("ruleName", x => x.ShortErrDesc, "short rule name")
                    .Text("ruleDescription", x => x.ErrDesc, "full rule description")
                    .Text("rulePriority", x => x.ErrPriority, "rule priority")
                    .AnyText("reasonText", "matches rule name, description, reason or sub-reason", x => x.ShortErrDesc, x => x.ErrDesc, x => x.Reason, x => x.Sub_Reason)
                    .Text("heldBy", x => x.HeldBy, "login that held it")
                    .Text("workCode", x => x.work_code, "work code")
                    .Text("pickupPostcode", x => x.col_post, "sender/pickup postcode")
                    .Text("deliveryPostcode", x => x.del_post, "receiver/delivery postcode")
                    .Number("grossMargin", x => x.GrossMargin, "gross margin ($)")
                    .Date("reportingWeek", x => x.CurrentWeek, "IVT reporting week"),

                new ChatDataset<vAllCurrentDetails>("currentTrading",
                    "Every consignment TRADED in the current IVT week (Trading Data screen), with sell/buy values and MoveIT details. One row per consignment line.",
                    x => x.LoginName)
                    .Key("connote").Recency(x => x.Cons_Date)
                    .Text("connote", x => x.ConsignmentNote, "consignment note")
                    .Text("jobNumber", x => x.JobNumber, "BMS job number")
                    .Text("customer", x => x.Customer, "customer code")
                    .Text("customerGroup", x => x.CustomerGroup, "customer group")
                    .Text("financeGroup", x => x.FinanceGroup, "finance group (as on the dashboard)")
                    .AnyText("customerOrGroup", "matches customer, customer group OR finance group", x => x.Customer, x => x.CustomerGroup, x => x.FinanceGroup)
                    .Text("carrier", x => x.Carrier, "carrier code")
                    .Text("carrierGroup", x => x.CarrierGroup, "carrier group")
                    .AnyText("carrierOrGroup", "matches carrier OR carrier group", x => x.Carrier, x => x.CarrierGroup)
                    .Text("inMoveit", x => x.InMoveit, "whether found in MoveIT")
                    .Text("freightStatus", x => x.Moveit_FreightStatus, "MoveIT freight status")
                    .Text("lineOfBusiness", x => x.Moveit_LineOfBusiness, "MoveIT line of business")
                    .Text("workCode", x => x.work_code, "work code")
                    .Text("ancillary", x => x.AncillaryDescription, "ancillary description")
                    .Text("from", x => x.from, "origin ZONE code (e.g. MEL, NZ) - NOT a postcode")
                    .Text("to", x => x.to, "destination ZONE code - NOT a postcode")
                    .Text("pickupPostcode", x => x.col_post, "sender/pickup postcode as billed (use for 'sender postcode')")
                    .Text("deliveryPostcode", x => x.del_post, "receiver/delivery postcode as billed (use for 'receiver postcode')")
                    .Text("moveitPickupPostcode", x => x.Moveit_PickupPostcode, "pickup postcode recorded in MoveIT")
                    .Text("moveitDeliveryPostcode", x => x.Moveit_DeliveryPostcode, "delivery postcode recorded in MoveIT")
                    .Text("pickupState", x => x.Moveit_PickupState, "pickup state")
                    .Text("deliveryState", x => x.Moveit_DeliveryState, "delivery state")
                    .Number("sellTotalNett", x => x.Sell_Total_Nett, "sell total nett ($)")
                    .Number("buyTotalNett", x => x.Buy_Total_Nett, "buy total nett ($)")
                    .Number("fuelLevyVariance", x => x.FSL___Var, "fuel surcharge variance")
                    .Number("weightKg", x => x.Moveit_TotWeightKg, "MoveIT weight (kg)")
                    .Number("cubicM3", x => x.Moveit_TotVolM, "MoveIT volume (m3)")
                    .Date("consignmentDate", x => x.Cons_Date, "consignment date")
                    .Date("despatchDate", x => x.Moveit_DespatchDate, "MoveIT despatch date")
                    .Date("reportingWeek", x => x.CurrentWeek, "IVT reporting week"),

                // Use the populated history source, not the empty vIVT_TradingDataHistory snapshot.
                // Every query must include both period bounds; the SQL engine pushes these and ownership into the view.
                new ChatDataset<vTradingDataHistory>("tradingHistory",
                    "Historical trading lines, NOT exceptions-only. Requires a bounded reportingWeek range of at most 26 weeks. " +
                    "Use sums of sell and buy for gross margin. Work codes are available, but ancillary classification/descriptions and currency are not; " +
                    "only report historical ancillary volumes for explicitly identified work codes. Missing weeks may indicate unavailable data, not zero activity.",
                    x => x.LoginName)
                    .Key("connote").Recency(x => x.CurrentWeek).BoundedPeriod("reportingWeek", 182)
                    .Text("connote", x => x.ConsignmentNote, "consignment note")
                    .Text("jobNumber", x => x.JobNumber, "job number")
                    .Text("customer", x => x.Customer, "customer code")
                    .Text("customerGroup", x => x.CustomerGroup, "customer group")
                    .AnyText("customerOrGroup", "matches customer OR customer group", x => x.Customer, x => x.CustomerGroup)
                    .Text("carrier", x => x.Carrier, "carrier code")
                    .Text("carrierGroup", x => x.CarrierGroup, "carrier group")
                    .AnyText("carrierOrGroup", "matches carrier OR carrier group", x => x.Carrier, x => x.CarrierGroup)
                    .Text("workCode", x => x.work_code, "work code; not every work code is an ancillary")
                    .Text("from", x => x.from, "origin zone, not country of the business")
                    .Text("to", x => x.to, "destination zone, not country of the business")
                    .Number("sellTotalNett", x => x.Sell_Total_Nett, "sell total nett; currency unspecified")
                    .Number("buyTotalNett", x => x.Buy_Total_Nett, "buy total nett; currency unspecified")
                    .Date("consignmentDate", x => x.Cons_Date, "consignment date")
                    .Date("reportingWeek", x => x.CurrentWeek, "loaded IVT reporting week"),

                new ChatDataset<vAllCurrentAncillarySummary>("ancillaries",
                    "Current-week ancillary charges by customer, carrier and work code versus the 13-week average (Ancillary Summary screen).",
                    x => x.LoginName)
                    .Text("customer", x => x.CustomerCode, "customer code")
                    .Text("customerGroup", x => x.FinanceGroup, "finance/customer group")
                    .AnyText("customerOrGroup", "matches customer OR customer group", x => x.CustomerCode, x => x.FinanceGroup)
                    .Text("carrier", x => x.Carrier, "carrier code")
                    .Text("workCode", x => x.WorkCode, "work code")
                    .Text("ancillary", x => x.AncillaryDesc, "ancillary description")
                    .Number("currentAvgAncillaries", x => x.CurrentWeekAvgTotalAncillaries, "current week average ancillaries")
                    .Number("currentAvgSell", x => x.CurrentWeekAvgSellTotalNett, "current week average sell ($)")
                    .Number("currentAvgBuy", x => x.CurrentWeekAvgBuyTotalNett, "current week average buy ($)")
                    .Number("currentGrossMargin", x => x.CurrentGrossMargin, "current gross margin")
                    .Number("avg13WeekAncillaries", x => x.C13WeekAvgTotalAncillaries, "13-week average ancillaries")
                    .Number("avg13WeekGrossMargin", x => x.AvgGrossMargin, "13-week average gross margin"),

                new ChatDataset<vMoveitMismatch>("moveitMismatches",
                    "Consignments where MoveIT and BMS weight/cubic disagree (MoveIT mismatch report).",
                    x => x.LoginName)
                    .Key("connote").Recency(x => x.Moveit_DespatchDate)
                    .Text("connote", x => x.ConsignmentNote, "consignment note")
                    .Text("jobNumber", x => x.JobNumber, "BMS job number")
                    .Text("customer", x => x.Customer, "customer code")
                    .Text("customerGroup", x => x.CustomerGroup, "customer group")
                    .AnyText("customerOrGroup", "matches customer OR customer group", x => x.Customer, x => x.CustomerGroup)
                    .Text("carrier", x => x.Carrier, "carrier code")
                    .Text("carrierGroup", x => x.CarrierGroup, "carrier group")
                    .AnyText("carrierOrGroup", "matches carrier OR carrier group", x => x.Carrier, x => x.CarrierGroup)
                    .Text("status", x => x.FinalStatus, "mismatch status")
                    .Number("deadWeightDifference", x => x.DeadWeight_Difference, "dead weight difference (kg)")
                    .Number("cubicDifference", x => x.Cubic_Difference, "cubic difference")
                    .Number("chargeWeightDifference", x => x.ChargeWeight_Difference, "charge weight difference")
                    .Number("chargeWeightMismatchPct", x => x.ChargeWeight_PercentageMismatch, "charge weight mismatch %")
                    .Number("cubicMismatchPct", x => x.Cubic_PercentageMismatch, "cubic mismatch %")
                    .Date("despatchDate", x => x.Moveit_DespatchDate, "MoveIT despatch date")
                    .Date("reportingWeek", x => x.CurrentWeek, "IVT reporting week"),

                new ChatDataset<AFSDataGateway.Services.ML.MLReviewItem>("mlFindings",
                    "Consignments the ML anomaly-detection model found unusual (the ML Findings page): latest scoring run per week, with the plain-language explanation, " +
                    "unusualness percentile (NOT a probability) and any account-manager assessment. 'Random check' rows are randomly sampled, NOT flagged by the model.",
                    x => x.LoginName, "ML.vReviewCandidates")
                    .Key("connote").Recency(x => x.ScoringWeek)
                    .Text("connote", x => x.ConsignmentNote, "consignment note")
                    .Text("jobNumber", x => x.JobNumber, "BMS job number")
                    .Text("customer", x => x.Customer, "customer code")
                    .Text("customerGroup", x => x.CustomerGroup, "customer group")
                    .AnyText("customerOrGroup", "matches customer OR customer group", x => x.Customer, x => x.CustomerGroup)
                    .Text("carrier", x => x.Carrier, "carrier code")
                    .Text("reason", x => x.PrimaryReasonCode, "primary ML reason: ML_HIGH_BUY_COST, ML_UNUSUAL_MARGIN, ML_WEIGHT_MISMATCH, ML_UNUSUAL_FUEL_RATIO, ML_BUY_SELL_MISMATCH, ML_UNUSUAL_CHARGE_COMBINATION")
                    .Text("allReasons", x => x.ReasonCodes, "all ML reason codes (comma separated)")
                    .Text("summary", x => x.ReasonSummary, "short reason summary")
                    .Text("explanation", x => x.ExplanationText, "plain-language explanation")
                    .Text("suggestedCheck", x => x.RecommendedAction, "suggested check")
                    .Text("selection", x => x.SamplingStratum, "MODEL_CANDIDATE | AE_CANDIDATE (flagged by the model) | RANDOM_CHECK (random sample, not flagged)")
                    .Text("decision", x => x.Decision, "publication decision (shadow mode: SHADOW_ONLY etc.)")
                    .Text("ruleCodes", x => x.RuleCodes, "IVT rule codes if also flagged by an IVT rule")
                    .Text("reviewOutcome", x => x.ReviewOutcome, "assessment: CONFIRMED_EXCEPTION | CONFIRMED_VALID | INSUFFICIENT_INFORMATION; blank = not assessed yet")
                    .Text("outcomeType", x => x.OutcomeType, "assessment detail, e.g. IncorrectBuy, ValidRateChange")
                    .Text("reviewedBy", x => x.ReviewedBy, "who assessed it")
                    .Text("workCode", x => x.WorkCode, "work code")
                    .Number("unusualnessPercentile", x => x.AEAnomalyPercentile, "share of reference transactions that are less unusual (0-100); not a probability")
                    .Number("evidenceCount", x => x.EvidenceCount, "independent evidence signals")
                    .Number("alreadyRuleFlagged", x => x.AlreadyRuleFlagged, "1 if an IVT rule also flagged it")
                    .Number("buyNett", x => x.BuyNett, "buy nett ($)")
                    .Number("sellNett", x => x.SellNett, "sell nett ($)")
                    .Date("scoringWeek", x => x.ScoringWeek, "invoice week scored")
                    .Date("reviewedAt", x => x.ReviewedAt, "when it was assessed"),

                new ChatDataset<vDash_CustomerTotalMeasures>("customerMeasures",
                    "Current-week KPI totals per customer group / carrier (dashboard measures): connotes, exceptions, held, ancillaries, sell/buy, MoveIT match. Rates are already percentages: use avg/min/max for rates, never sum.",
                    x => x.LoginName)
                    .Text("customerGroup", x => x.FinanceGroup, "finance/customer group")
                    .Text("customer", x => x.CustomerCode, "customer code")
                    .AnyText("customerOrGroup", "matches customer OR customer group", x => x.CustomerCode, x => x.FinanceGroup)
                    .Text("carrier", x => x.Carrier, "carrier code")
                    .Number("totalConnotes", x => x.TotalAllConnotes, "connotes traded")
                    .Number("totalExceptions", x => x.TotalExceptions, "exceptions")
                    .Number("exceptionRate", x => x.ExceptionRate, "exception rate %")
                    .Number("heldCount", x => x.HeldCount, "held consignments")
                    .Number("totalAncillaries", x => x.TotalAncillaries, "ancillaries")
                    .Number("ancillaryRate", x => x.AncillariesRate, "ancillary rate %")
                    .Number("sellTotalNett", x => x.SellTotalNett, "sell total nett ($)")
                    .Number("buyTotalNett", x => x.BuyTotalNett, "buy total nett ($)")
                    .Number("notInMoveit", x => x.TotalNotInMoveit, "connotes not in MoveIT")
                    .Number("manualEntries", x => x.TotalManualEntry, "manual entries")
                    .Number("moveitMatched", x => x.MoveitMatchedCount, "MoveIT matched connotes")
                    .Number("cubicMismatches", x => x.CubicMismatchCount, "cubic mismatches")
                    .Number("deadWeightMismatches", x => x.DeadWeightMismatchCount, "dead weight mismatches"),
            };
            return list.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }

        // Compact catalog for the tool description, so the model knows each dataset's fields.
        public static string Describe()
        {
            var text = new StringBuilder();
            foreach (var dataset in All.Values)
            {
                text.Append(dataset.Name).Append(": ").Append(dataset.Description).Append(" Fields: ");
                text.Append(string.Join(", ", dataset.Fields.Select(f => f.Name + " (" + f.Kind.ToString().ToLowerInvariant() +
                    (f.Groupable ? "" : ", filter only") + "; " + f.Description + ")")));
                text.Append(".\n");
            }
            return text.ToString();
        }
    }

    public enum ChatFieldKind { Text, Number, Date }

    public sealed class ChatField
    {
        public string Name { get; set; }
        public ChatFieldKind Kind { get; set; }
        public string Description { get; set; }
        public string[] Columns { get; set; }
        // Fields spanning several columns can be filtered on but not grouped, aggregated or listed.
        public bool Groupable { get { return Columns.Length == 1; } }
    }

    public sealed class ChatFilter
    {
        public string Field { get; set; }
        public string Op { get; set; }
        public List<string> Values { get; set; }
    }

    public sealed class ChatMetric
    {
        public string Fn { get; set; }
        public string Field { get; set; }
        public string Label { get { return Field == null ? Fn : Fn + "(" + Field + ")"; } }
    }

    public sealed class ChatDatasetQuery
    {
        public List<ChatFilter> Filters { get; set; } = new List<ChatFilter>();
        public List<string> GroupBy { get; set; } = new List<string>();
        public List<ChatMetric> Metrics { get; set; } = new List<ChatMetric>();
        public int SortMetric { get; set; }
        public bool Ascending { get; set; }
        public int Limit { get; set; } = 20;
        public bool ListRecords { get; set; }
        public int ExportRowCap { get; set; }  // > 0 only for emailed reports (server-side, never set by the model)
        public List<string> Columns { get; set; } = new List<string>();
    }

    public interface IChatDataset
    {
        string Name { get; }
        string Description { get; }
        IReadOnlyList<ChatField> Fields { get; }
        ChatField Field(string name);
        object Run(IChatDataSource data, ChatAccessScope scope, ChatDatasetQuery query);
    }

    // Builds one parameterised SQL statement per request. Identifiers (view and column names) come only from the
    // compiled catalog lambdas and are validated; every user-supplied value is a SqlParameter.
    public sealed class ChatDataset<T> : IChatDataset where T : class
    {
        public const int MaxFilters = 8, MaxValues = 20, MaxGroups = 2, MaxMetrics = 4, MaxLimit = 50, MaxRecords = 25, MaxExportRows = 5000;
        public static readonly string[] Ops = { "equals", "notEquals", "contains", "notContains", "startsWith", "gt", "gte", "lt", "lte", "between", "isBlank", "notBlank" };
        public static readonly string[] Functions = { "count", "countDistinct", "sum", "avg", "min", "max" };
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        private readonly List<ChatField> fields = new List<ChatField>();
        private readonly string view;
        private readonly string ownerColumn;
        private string baseSql;
        private string recencyColumn;
        private string recordKey;
        private string requiredFilter;
        private string boundedPeriod;
        private int maxPeriodDays;

        public ChatDataset(string name, string description, Expression<Func<T, string>> owner)
            : this(name, description, owner, typeof(T).Name) { }

        // viewName may be schema-qualified ("ML.vReviewCandidates"); each part is validated as an identifier.
        public ChatDataset(string name, string description, Expression<Func<T, string>> owner, string viewName)
        {
            Name = name;
            Description = description;
            view = string.Join(".", viewName.Split('.').Select(Quote));
            ownerColumn = Quote(Column(owner));
        }

        public string Name { get; private set; }
        public string Description { get; private set; }
        public IReadOnlyList<ChatField> Fields { get { return fields; } }

        // Fixed SQL written in this catalog (never model input), e.g. excluding soft-deleted rows.
        public ChatDataset<T> WhereSql(string sql) { baseSql = sql; return this; }
        public ChatDataset<T> Key(string field) { recordKey = field; return this; }
        // Large/slow views: refuse queries without a narrowing filter on this field.
        public ChatDataset<T> Requires(string field) { requiredFilter = field; return this; }
        public ChatDataset<T> BoundedPeriod(string field, int days) { boundedPeriod = field; maxPeriodDays = days; return this; }
        public ChatDataset<T> Recency<TD>(Expression<Func<T, TD>> selector) { recencyColumn = Quote(Column(selector)); return this; }

        public ChatDataset<T> Text(string name, Expression<Func<T, string>> selector, string description)
        {
            return Add(name, ChatFieldKind.Text, description, selector);
        }

        public ChatDataset<T> AnyText(string name, string description, params Expression<Func<T, string>>[] selectors)
        {
            return Add(name, ChatFieldKind.Text, description, selectors);
        }

        public ChatDataset<T> Number<TN>(string name, Expression<Func<T, TN>> selector, string description)
        {
            return Add(name, ChatFieldKind.Number, description, selector);
        }

        public ChatDataset<T> Date<TD>(string name, Expression<Func<T, TD>> selector, string description)
        {
            return Add(name, ChatFieldKind.Date, description, selector);
        }

        private ChatDataset<T> Add(string name, ChatFieldKind kind, string description, params LambdaExpression[] selectors)
        {
            fields.Add(new ChatField { Name = name, Kind = kind, Description = description, Columns = selectors.Select(s => Quote(Column(s))).ToArray() });
            return this;
        }

        private static string Column(LambdaExpression selector)
        {
            var body = selector.Body;
            while (body is UnaryExpression) body = ((UnaryExpression)body).Operand;
            var member = body as MemberExpression;
            if (member == null || member.Expression != selector.Parameters[0])
                throw new InvalidOperationException("Chat catalog selectors must be simple column accessors.");
            return member.Member.Name;
        }

        private static string Quote(string identifier)
        {
            if (!Identifier.IsMatch(identifier)) throw new InvalidOperationException("Invalid identifier in chat catalog: " + identifier);
            return "[" + identifier + "]";
        }

        public ChatField Field(string name)
        {
            var field = fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (field == null)
                throw new ChatQueryException("Unknown field '" + name + "' for dataset " + Name + ". Valid fields: " + string.Join(", ", fields.Select(f => f.Name)) + ".");
            return field;
        }

        public object Run(IChatDataSource data, ChatAccessScope scope, ChatDatasetQuery q)
        {
            if (q.Filters.Count > MaxFilters) throw new ChatQueryException("Use at most " + MaxFilters + " filters.");
            if (boundedPeriod != null) ValidatePeriod(q);
            if (requiredFilter != null && !q.Filters.Any(f => string.Equals(f.Field, requiredFilter, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(f.Op, "isBlank", StringComparison.OrdinalIgnoreCase) && !string.Equals(f.Op, "notBlank", StringComparison.OrdinalIgnoreCase)))
                throw new ChatQueryException("Dataset " + Name + " needs a " + requiredFilter + " filter (for example gte/between with yyyy-MM-dd dates). Ask the user for a period if none was given.");

            var parameters = new List<SqlParameter>();
            Func<object, SqlDbType, string> parameter = (value, type) =>
            {
                var p = new SqlParameter("@p" + parameters.Count, type) { Value = value ?? DBNull.Value };
                if (type == SqlDbType.NVarChar) p.Size = 400;
                parameters.Add(p);
                return p.ParameterName;
            };
            // Ownership first, then the catalog's fixed condition, then the model's filters, all ANDed.
            var where = new List<string> { scope.SqlFilter(ownerColumn, v => parameter(v, SqlDbType.NVarChar)) };
            if (baseSql != null) where.Add("(" + baseSql + ")");
            foreach (var filter in q.Filters) where.Add(FilterSql(filter, parameter));
            var whereSql = " FROM " + view + " WHERE " + string.Join(" AND ", where);

            object result;
            long? matched = null;
            if (q.ListRecords)
            {
                matched = Convert.ToInt64(data.Query("SELECT COUNT_BIG(*)" + whereSql, parameters)[0][0]);
                result = List(data, q, whereSql, parameters);
            }
            else result = Aggregate(data, q, whereSql, parameters);
            return new
            {
                dataset = Name,
                appliedFilters = q.Filters.Select(f => new { f.Field, f.Op, f.Values }).ToList(),
                matchedRows = matched,
                rowMeaning = recordKey == null ? "One row per summary line." : "One row per record line; use countDistinct(" + recordKey + ") for distinct consignments.",
                result
            };
        }

        // ---------- filters ----------

        private void ValidatePeriod(ChatDatasetQuery q)
        {
            DateTime? lower = null, upper = null;
            var field = Field(boundedPeriod);
            foreach (var f in q.Filters.Where(f => string.Equals(f.Field, boundedPeriod, StringComparison.OrdinalIgnoreCase)))
            {
                var values = f.Values ?? new List<string>();
                var op = (f.Op ?? "").ToLowerInvariant();
                DateTime? lo = null, hi = null;
                if (op == "between" && values.Count == 2) { lo = ParseDate(field, values[0]); hi = ParseDate(field, values[1]); }
                else if (op == "equals" && values.Count > 0) { var dates = values.Select(v => ParseDate(field, v)).ToList(); lo = dates.Min(); hi = dates.Max(); }
                else if (values.Count == 1)
                {
                    if (op == "gte" || op == "gt") lo = ParseDate(field, values[0]).AddDays(op == "gt" ? 1 : 0);
                    if (op == "lte" || op == "lt") hi = ParseDate(field, values[0]).AddDays(op == "lt" ? -1 : 0);
                }
                if (lo.HasValue && (!lower.HasValue || lo > lower)) lower = lo;
                if (hi.HasValue && (!upper.HasValue || hi < upper)) upper = hi;
            }
            if (!lower.HasValue || !upper.HasValue || upper < lower || (upper.Value - lower.Value).TotalDays >= maxPeriodDays)
                throw new ChatQueryException("Dataset " + Name + " requires both reportingWeek bounds, covering at most " + maxPeriodDays + " days. Use between or gte plus lte with yyyy-MM-dd dates.");
        }

        private string FilterSql(ChatFilter filter, Func<object, SqlDbType, string> parameter)
        {
            var field = Field(filter.Field);
            var op = Ops.FirstOrDefault(o => string.Equals(o, filter.Op, StringComparison.OrdinalIgnoreCase));
            if (op == null) throw new ChatQueryException("Unknown operator '" + filter.Op + "'. Use: " + string.Join(", ", Ops) + ".");
            var values = filter.Values ?? new List<string>();
            if (values.Count > MaxValues) throw new ChatQueryException("Use at most " + MaxValues + " values per filter.");
            if (op != "isBlank" && op != "notBlank" && values.Count == 0) throw new ChatQueryException("Filter on " + field.Name + " needs a value.");
            if (op == "between" && values.Count != 2) throw new ChatQueryException("between needs exactly two values.");
            filter.Op = op;
            filter.Field = field.Name;

            // Negative operators must hold for every column of a multi-column field; positive ones for any column.
            var negative = op == "notEquals" || op == "notContains" || op == "isBlank";
            var parts = field.Columns.Select(column => Condition(field, column, op, values, parameter)).ToList();
            return "(" + string.Join(negative ? " AND " : " OR ", parts) + ")";
        }

        private static string Condition(ChatField field, string column, string op, List<string> values, Func<object, SqlDbType, string> parameter)
        {
            if (op == "isBlank" || op == "notBlank")
            {
                var blank = field.Kind == ChatFieldKind.Text ? "(" + column + " IS NULL OR LTRIM(" + column + ") = '')" : column + " IS NULL";
                return op == "isBlank" ? blank : "NOT " + blank;
            }
            switch (field.Kind)
            {
                case ChatFieldKind.Text:
                    var texts = values.Select(v => (v ?? "").Trim()).ToList();
                    if (texts.Any(v => v.Length == 0 || v.Length > 100)) throw new ChatQueryException("Values for " + field.Name + " must be 1 to 100 characters.");
                    switch (op)
                    {
                        case "equals": return column + " IN (" + string.Join(", ", texts.Select(v => parameter(v, SqlDbType.NVarChar))) + ")";
                        case "notEquals": return "(" + column + " IS NULL OR " + column + " NOT IN (" + string.Join(", ", texts.Select(v => parameter(v, SqlDbType.NVarChar))) + "))";
                        case "contains": return "(" + string.Join(" OR ", texts.Select(v => column + " LIKE " + parameter("%" + EscapeLike(v) + "%", SqlDbType.NVarChar) + " ESCAPE '\\'")) + ")";
                        case "startsWith": return "(" + string.Join(" OR ", texts.Select(v => column + " LIKE " + parameter(EscapeLike(v) + "%", SqlDbType.NVarChar) + " ESCAPE '\\'")) + ")";
                        case "notContains": return "(" + column + " IS NULL OR (" + string.Join(" AND ", texts.Select(v => column + " NOT LIKE " + parameter("%" + EscapeLike(v) + "%", SqlDbType.NVarChar) + " ESCAPE '\\'")) + "))";
                        default: throw new ChatQueryException(op + " cannot be used on text field " + field.Name + ".");
                    }
                case ChatFieldKind.Number:
                    var numbers = values.Select(v => ParseNumber(field, v)).ToList();
                    var number = "CAST(" + column + " AS float)";
                    Func<double, string> n = v => parameter(v, SqlDbType.Float);
                    switch (op)
                    {
                        case "equals": return number + " IN (" + string.Join(", ", numbers.Select(n)) + ")";
                        case "notEquals": return number + " NOT IN (" + string.Join(", ", numbers.Select(n)) + ")";
                        case "gt": return number + " > " + n(numbers[0]);
                        case "gte": return number + " >= " + n(numbers[0]);
                        case "lt": return number + " < " + n(numbers[0]);
                        case "lte": return number + " <= " + n(numbers[0]);
                        case "between": return number + " BETWEEN " + n(numbers[0]) + " AND " + n(numbers[1]);
                        default: throw new ChatQueryException(op + " cannot be used on number field " + field.Name + ".");
                    }
                default:
                    // Dates compare by whole day: "lte 2026-01-09" includes all of 9 January.
                    var dates = values.Select(v => ParseDate(field, v)).ToList();
                    Func<DateTime, string> d = v => parameter(v, SqlDbType.DateTime2);
                    Func<DateTime, string> day = v => "(" + column + " >= " + d(v) + " AND " + column + " < " + d(v.AddDays(1)) + ")";
                    switch (op)
                    {
                        case "equals": return "(" + string.Join(" OR ", dates.Select(day)) + ")";
                        case "notEquals": return "NOT (" + string.Join(" OR ", dates.Select(day)) + ")";
                        case "gt": return column + " >= " + d(dates[0].AddDays(1));
                        case "gte": return column + " >= " + d(dates[0]);
                        case "lt": return column + " < " + d(dates[0]);
                        case "lte": return column + " < " + d(dates[0].AddDays(1));
                        case "between": return "(" + column + " >= " + d(dates[0]) + " AND " + column + " < " + d(dates[1].AddDays(1)) + ")";
                        default: throw new ChatQueryException(op + " cannot be used on date field " + field.Name + ".");
                    }
            }
        }

        private static string EscapeLike(string value)
        {
            return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        }

        private static double ParseNumber(ChatField field, string value)
        {
            double number;
            if (!double.TryParse((value ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) || double.IsNaN(number) || double.IsInfinity(number))
                throw new ChatQueryException(field.Name + " needs a number, got '" + value + "'.");
            return number;
        }

        private static DateTime ParseDate(ChatField field, string value)
        {
            DateTime date;
            if (!DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) || date.Year < 2000 || date.Year > 2100)
                throw new ChatQueryException(field.Name + " needs a date as yyyy-MM-dd, got '" + value + "'.");
            return date;
        }

        // ---------- aggregation ----------

        private object Aggregate(IChatDataSource data, ChatDatasetQuery q, string whereSql, List<SqlParameter> parameters)
        {
            if (q.GroupBy.Count > MaxGroups) throw new ChatQueryException("Group by at most " + MaxGroups + " fields.");
            var groupFields = q.GroupBy.Select(Field).ToList();
            if (groupFields.Any(f => !f.Groupable || f.Kind == ChatFieldKind.Number))
                throw new ChatQueryException("Group by single text or date fields only (not multi-column or number fields).");
            var metrics = q.Metrics.Count > 0 ? q.Metrics : DefaultMetrics();
            if (metrics.Count > MaxMetrics) throw new ChatQueryException("Use at most " + MaxMetrics + " metrics.");
            if (q.SortMetric < 0 || q.SortMetric >= metrics.Count) throw new ChatQueryException("sortByMetric must refer to one of the requested metrics.");

            // Dates group by day so timestamps (e.g. heldAt) do not split into per-second groups.
            var groupSql = groupFields.Select(f => f.Kind == ChatFieldKind.Date ? "CAST(" + f.Columns[0] + " AS date)" : f.Columns[0]).ToList();
            var metricSql = metrics.Select(MetricSql).ToList();
            var limit = Math.Min(Math.Max(q.Limit, 1), MaxLimit);
            var select = new StringBuilder("SELECT TOP (" + (limit + 1) + ") ");
            select.Append(string.Join(", ", groupSql.Select((g, i) => g + " AS G" + i).Concat(metricSql.Select((m, i) => m + " AS M" + i))));
            select.Append(whereSql);
            if (groupSql.Count > 0)
            {
                select.Append(" GROUP BY ").Append(string.Join(", ", groupSql));
                select.Append(" ORDER BY M").Append(q.SortMetric).Append(q.Ascending ? " ASC" : " DESC");
                foreach (var i in Enumerable.Range(0, groupSql.Count)) select.Append(", G").Append(i);
            }
            var rows = data.Query(select.ToString(), parameters);
            var truncated = rows.Count > limit;
            return new
            {
                groupBy = groupFields.Select(f => f.Name).ToList(),
                metrics = metrics.Select(m => m.Label).ToList(),
                groupsShown = Math.Min(rows.Count, limit),
                truncated,
                truncationNote = truncated ? "More groups exist beyond those shown (sorted by the chosen metric); the total number of groups was not counted." : null,
                groups = rows.Take(limit).Select(row =>
                {
                    var item = new Dictionary<string, object>();
                    for (var i = 0; i < groupFields.Count; i++) item[groupFields[i].Name] = Value(row[i], true);
                    for (var i = 0; i < metrics.Count; i++)
                    {
                        var value = row[groupFields.Count + i];
                        item[metrics[i].Label] = value == null ? (double?)null : Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), 2);
                    }
                    return item;
                }).ToList()
            };
        }

        private List<ChatMetric> DefaultMetrics()
        {
            var metrics = new List<ChatMetric> { new ChatMetric { Fn = "count" } };
            if (recordKey != null) metrics.Add(new ChatMetric { Fn = "countDistinct", Field = recordKey });
            return metrics;
        }

        private string MetricSql(ChatMetric metric)
        {
            var fn = Functions.FirstOrDefault(f => string.Equals(f, metric.Fn, StringComparison.OrdinalIgnoreCase));
            if (fn == null) throw new ChatQueryException("Unknown metric '" + metric.Fn + "'. Use: " + string.Join(", ", Functions) + ".");
            metric.Fn = fn;
            if (fn == "count") { metric.Field = null; return "COUNT_BIG(*)"; }
            if (string.IsNullOrWhiteSpace(metric.Field)) throw new ChatQueryException(fn + " needs a field.");
            var field = Field(metric.Field);
            metric.Field = field.Name;
            if (!field.Groupable) throw new ChatQueryException(field.Name + " spans several columns and cannot be aggregated.");
            var column = field.Columns[0];
            if (fn == "countDistinct") return "COUNT_BIG(DISTINCT " + column + ")";
            if (field.Kind != ChatFieldKind.Number) throw new ChatQueryException(fn + " needs a number field; " + field.Name + " is " + field.Kind.ToString().ToLowerInvariant() + ".");
            return fn.ToUpperInvariant() + "(CAST(" + column + " AS float))";
        }

        // ---------- record listing ----------

        private object List(IChatDataSource data, ChatDatasetQuery q, string whereSql, List<SqlParameter> parameters)
        {
            var columns = (q.Columns.Count > 0 ? q.Columns.Select(Field) : fields.Where(f => f.Groupable)).ToList();
            if (columns.Any(f => !f.Groupable)) throw new ChatQueryException("Multi-column fields cannot be listed; choose single fields.");
            // Chat answers are capped at MaxRecords; emailed reports may export up to ExportRowCap rows.
            var limit = q.ExportRowCap > 0 ? Math.Min(q.ExportRowCap, MaxExportRows) : Math.Min(Math.Max(q.Limit, 1), MaxRecords);
            var sql = "SELECT TOP (" + (limit + 1) + ") " + string.Join(", ", columns.Select(f => f.Columns[0])) + whereSql +
                (recencyColumn == null ? "" : " ORDER BY " + recencyColumn + " DESC");
            var rows = data.Query(sql, parameters);
            return new
            {
                truncated = rows.Count > limit,
                order = recencyColumn == null ? "unspecified" : "most recent first",
                records = rows.Take(limit).Select(row =>
                {
                    var item = new Dictionary<string, object>();
                    for (var i = 0; i < columns.Count; i++)
                    {
                        var value = Value(row[i], false);
                        if (value == null || (value is string && ((string)value).Length == 0)) continue;
                        item[columns[i].Name] = value;
                    }
                    return item;
                }).ToList()
            };
        }

        private static object Value(object value, bool dateOnly)
        {
            if (value == null) return null;
            if (value is DateTime) return ((DateTime)value).ToString(dateOnly || ((DateTime)value).TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var text = value as string;
            return text != null ? text.Trim() : value;
        }
    }
}
