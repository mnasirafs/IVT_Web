using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace AFSDataGateway.Services.IvtAdmin
{
    public enum IvtFieldKind { Text, Integer, Boolean, Float, Money, Decimal }

    // Parameter of one procedure for one field. Types/sizes are the VERIFIED sys.parameters contract (IVT, 2026-10-09).
    public sealed class IvtParamSpec
    {
        public string Name { get; set; }
        public SqlDbType Type { get; set; }
        public int Size { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
    }

    public sealed class IvtFieldDef
    {
        public string Column { get; set; }        // result column returned by the read procedure
        public string Label { get; set; }
        public string Section { get; set; }
        public IvtFieldKind Kind { get; set; }
        public bool ReadOnly { get; set; }        // database-owned (Id, timestamps)
        public bool Required { get; set; }
        public bool Toggle { get; set; }          // may be flipped with a one-click action (IsDeleted, OnOff, ...)
        public string Help { get; set; }
        public IvtParamSpec Update { get; set; }  // null = not sent on update
        public IvtParamSpec Create { get; set; }  // null = not sent on create
        public object CreateFixedValue { get; set; } // value always sent on create (e.g. sp_NewRuleExclusion @IsDeleted = 0)
        public bool HasCreateFixedValue { get; set; }
        // Verified per-environment difference: the parameter has another name in IVT_Test (same type and value).
        public string TestParameterName { get; set; }

        public string ParameterName(IvtParamSpec spec, IvtEnvironment environment)
        {
            return environment == IvtEnvironment.Test && TestParameterName != null ? TestParameterName : spec.Name;
        }
    }

    public sealed class IvtEntityDef
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ReadProcedure { get; set; }
        public string UpdateProcedure { get; set; }
        public string CreateProcedure { get; set; }
        public IvtEnvironment[] Environments { get; set; }
        public List<IvtFieldDef> Fields { get; set; } = new List<IvtFieldDef>();
        public string Note { get; set; }
        public IvtFieldDef Id { get { return Fields.First(f => f.Column == "Id"); } }
        public IvtFieldDef Deleted { get { return Fields.FirstOrDefault(f => f.Column == "IsDeleted"); } }
        public bool Supports(IvtEnvironment environment) { return Environments.Contains(environment); }
        public IEnumerable<string> RequiredColumns { get { return Fields.Where(f => !f.ReadOnly || f.Column == "Id").Select(f => f.Column); } }
    }

    public static class ReferenceCatalog
    {
        private static readonly IvtEnvironment[] Both = { IvtEnvironment.Production, IvtEnvironment.Test };

        private static IvtParamSpec P(string name, SqlDbType type, int size = 0, byte precision = 0, byte scale = 0)
        {
            return new IvtParamSpec { Name = name, Type = type, Size = size, Precision = precision, Scale = scale };
        }
        private static IvtParamSpec Vc(string name, int size) { return P(name, SqlDbType.VarChar, size); }

        private static IvtFieldDef Id() { return new IvtFieldDef { Column = "Id", Label = "Id", Kind = IvtFieldKind.Integer, ReadOnly = true, Update = P("@Id", SqlDbType.BigInt), Section = "Identity" }; }
        private static IvtFieldDef Stamp(string column, string label) { return new IvtFieldDef { Column = column, Label = label, Kind = IvtFieldKind.Text, ReadOnly = true, Section = "Identity" }; }
        private static IvtFieldDef Text(string column, string label, string section, int updateSize, int createSize, bool required = false, string help = null)
        {
            return new IvtFieldDef { Column = column, Label = label, Section = section, Kind = IvtFieldKind.Text, Required = required, Help = help,
                Update = updateSize > 0 ? Vc("@" + column, updateSize) : null, Create = createSize > 0 ? Vc("@" + column, createSize) : null };
        }
        private static IvtFieldDef Bit(string column, string label, string section, bool create = true, bool toggle = false)
        {
            return new IvtFieldDef { Column = column, Label = label, Section = section, Kind = IvtFieldKind.Boolean, Toggle = toggle,
                Update = P("@" + column, SqlDbType.Bit), Create = create ? P("@" + column, SqlDbType.Bit) : null };
        }
        private static IvtFieldDef Flt(string column, string label, string section, string help = null)
        {
            return new IvtFieldDef { Column = column, Label = label, Section = section, Kind = IvtFieldKind.Float, Help = help,
                Update = P("@" + column, SqlDbType.Float), Create = P("@" + column, SqlDbType.Float) };
        }
        private static IvtFieldDef Deleted(bool create, SqlDbType type = SqlDbType.Bit)
        {
            return new IvtFieldDef { Column = "IsDeleted", Label = "Deleted", Section = "Identity", Kind = IvtFieldKind.Boolean, Toggle = true,
                Update = P("@IsDeleted", type), Help = "Soft delete: the record is kept but no longer applied." };
        }

        public static readonly IReadOnlyList<IvtEntityDef> All = new List<IvtEntityDef>
        {
            new IvtEntityDef
            {
                Key = "account-managers", Title = "Account manager portfolios", Environments = Both,
                Description = "Customer code to account manager mapping, delegates and login used for routing exceptions.",
                ReadProcedure = "WebIVT.GetAccountManagerDetails", UpdateProcedure = "WebIVT.sp_UpdateAMPortfolio", CreateProcedure = "WebIVT.sp_NewAMPortfolio",
                Fields =
                {
                    Id(),
                    Text("CustomerCode", "Customer code", "Portfolio", 500, 500, true),
                    Text("AccountManager", "Account manager", "Portfolio", 100, 100, true),
                    Text("LoginName", "Login name", "Portfolio", 100, 100, true),
                    Text("Delegate_Login", "Delegate login", "Delegate", 100, 500),
                    Text("Delegate_Email", "Delegate email", "Delegate", 100, 500),
                    // IsDeleted is INT in both the result and @IsDeleted of sp_UpdateAMPortfolio (verified); shown as a checkbox, sent as 0/1.
                    new IvtFieldDef { Column = "IsDeleted", Label = "Disabled", Section = "Identity", Kind = IvtFieldKind.Boolean, Toggle = true,
                        Update = P("@IsDeleted", SqlDbType.Int), Help = "Disable / reactivate the mapping (INT flag 0/1)." },
                    Stamp("ActionTime", "Last changed"),
                }
            },
            new IvtEntityDef
            {
                Key = "rules", Title = "Rules", Environments = Both,
                Description = "Exception rules. On/Off disables a rule; Deleted soft-deletes it. They are different.",
                ReadProcedure = "WebIVT.GetRuleDetails", UpdateProcedure = "WebIVT.sp_UpdateRule", CreateProcedure = "WebIVT.sp_NewRule",
                Fields =
                {
                    Id(),
                    new IvtFieldDef { Column = "ErrCode", Label = "Rule code", Section = "Rule", Kind = IvtFieldKind.Integer, Required = true,
                        Update = P("@ErrCode", SqlDbType.BigInt), Create = P("@ErrCode", SqlDbType.BigInt), Help = "BIGINT parameter (column is INT)." },
                    Text("ErrPriority", "Priority", "Rule", 200, 200, true),
                    Text("ShortErrDesc", "Short description", "Rule", 200, 200, true),
                    Text("ErrDesc", "Description", "Rule", 200, 200),
                    // IVT_Test's sp_UpdateRule / sp_NewRule name this parameter @VCode (verified 2026-10-09).
                    new IvtFieldDef { Column = "RuleType", Label = "Rule type", Section = "Rule", Kind = IvtFieldKind.Text,
                        Update = Vc("@RuleType", 20), Create = Vc("@RuleType", 20), TestParameterName = "@VCode" },
                    // Verified contract quirk: sp_UpdateRule @OnOff INT / @Mandatory BIT, sp_NewRule @OnOff BIT / @Mandatory INT.
                    new IvtFieldDef { Column = "OnOff", Label = "On", Section = "Rule", Kind = IvtFieldKind.Boolean, Toggle = true,
                        Update = P("@OnOff", SqlDbType.Int), Create = P("@OnOff", SqlDbType.Bit), Help = "Enables or disables the rule." },
                    new IvtFieldDef { Column = "Mandatory", Label = "Mandatory", Section = "Rule", Kind = IvtFieldKind.Boolean, Toggle = true,
                        Update = P("@Mandatory", SqlDbType.Bit), Create = P("@Mandatory", SqlDbType.Int) },
                    Text("Customer", "Customer", "Scope", 500, 500),
                    Text("Carrier", "Carrier", "Scope", 500, 500),
                    Text("Beneficiary", "Beneficiary", "Beneficiary", 500, 500),
                    Text("BeneficiaryContact", "Beneficiary contact", "Beneficiary", 500, 500),
                    Deleted(false),
                    Stamp("ActionTime", "Last changed"),
                }
            },
            new IvtEntityDef
            {
                Key = "rule-options", Title = "Rule options", Environments = Both,
                Description = "Per-rule thresholds and options. Values are text: not every option is numeric.",
                ReadProcedure = "WebIVT.sp_getRuleOption", UpdateProcedure = "WebIVT.sp_UpdateRuleOption", CreateProcedure = "WebIVT.sp_NewRuleOption",
                Fields =
                {
                    Id(),
                    new IvtFieldDef { Column = "RuleCode", Label = "Rule code", Section = "Option", Kind = IvtFieldKind.Integer, Required = true,
                        Update = P("@RuleCode", SqlDbType.BigInt), Create = P("@RuleCode", SqlDbType.BigInt) },
                    Text("OptionDesc", "Description", "Option", 500, 500),
                    Text("OptionValue", "Value", "Option", 50, 50),
                    Text("OptionValue_Min", "Min value", "Option", 50, 50),
                    Text("OptionValue_Max", "Max value", "Option", 50, 50),
                    Text("Customer", "Customer", "Scope", 50, 50),
                    Text("Carrier", "Carrier", "Scope", 50, 50),
                    new IvtFieldDef { Column = "Moveit_Customer_Id", Label = "MoveIT customer id", Section = "Scope", Kind = IvtFieldKind.Integer,
                        Update = P("@Moveit_Customer_Id", SqlDbType.Int), Create = P("@Moveit_Customer_Id", SqlDbType.Int) },
                    Deleted(false),
                }
            },
            new IvtEntityDef
            {
                Key = "rule-exclusions", Title = "Rule exclusions", Environments = Both,
                Description = "Customer/carrier combinations excluded from a rule, with the business justification.",
                ReadProcedure = "WebIVT.sp_getRuleExclusion", UpdateProcedure = "WebIVT.sp_UpdateRuleExclusion", CreateProcedure = "WebIVT.sp_NewRuleExclusion",
                Fields =
                {
                    Id(),
                    new IvtFieldDef { Column = "RuleCode", Label = "Rule code", Section = "Exclusion", Kind = IvtFieldKind.Integer, Required = true,
                        Update = P("@RuleCode", SqlDbType.BigInt), Create = P("@RuleCode", SqlDbType.BigInt) },
                    Text("Justification", "Justification", "Exclusion", 500, 500, true, "Up to 500 characters."),
                    Text("Customer", "Customer", "Scope", 50, 50),
                    Text("Carrier", "Carrier", "Scope", 50, 50),
                    new IvtFieldDef { Column = "IsDeleted", Label = "Deleted", Section = "Identity", Kind = IvtFieldKind.Boolean, Toggle = true,
                        Update = P("@IsDeleted", SqlDbType.Bit), Create = P("@IsDeleted", SqlDbType.Bit), CreateFixedValue = false, HasCreateFixedValue = true },
                }
            },
            new IvtEntityDef
            {
                Key = "customer-carrier", Title = "Customer / carrier settings", Environments = Both,
                Description = "Prefix-level vetting thresholds per customer and carrier.",
                Note = "Customer / carrier settings are unavailable in production because required reference data is missing. Test settings are available.",
                ReadProcedure = "WebIVT.sp_getPrefixes", UpdateProcedure = "WebIVT.sp_UpdatePrefix", CreateProcedure = "WebIVT.sp_NewPrefix",
                Fields =
                {
                    Id(),
                    Text("Prefix", "Prefix", "Identity", 50, 50, true),
                    Text("Customercode", "Customer code", "Identity", 100, 100),
                    Text("Carrier", "Carrier", "Identity", 100, 100),
                    Bit("DeliveryComment", "Delivery comment check", "Delivery comments and references"),
                    Bit("CustomerReference", "Customer reference check", "Delivery comments and references"),
                    Text("DEL_COM_Format", "Delivery comment format", "Delivery comments and references", 500, 500),
                    Flt("CusRef_Digits", "Customer reference digits", "Delivery comments and references"),
                    Flt("Old_Connote_Month", "Old connote (months)", "Age and duplicate checks"),
                    Flt("No_Jobs_Prefixed", "Jobs prefixed", "Age and duplicate checks"),
                    Flt("GM_Below", "GM below", "Gross margin"),
                    Flt("GM_Above", "GM above", "Gross margin"),
                    Flt("GM_Movement", "GM movement", "Gross margin"),
                    Flt("GM_Month", "GM months", "Gross margin"),
                    Flt("FuelDiff_Above", "Fuel diff above", "Fuel checks"),
                    Flt("FuelDiff_Below", "Fuel diff below", "Fuel checks"),
                    Flt("Fuel_Freight_Ratio", "Fuel / freight ratio", "Fuel checks"),
                    new IvtFieldDef { Column = "TotalSell_Threshold", Label = "Total sell threshold", Section = "Sell, weight, cubic and pallet thresholds", Kind = IvtFieldKind.Money,
                        Update = P("@TotalSell_Threshold", SqlDbType.Money), Create = P("@TotalSell_Threshold", SqlDbType.Money) },
                    Flt("Weight_Threshold", "Weight threshold", "Sell, weight, cubic and pallet thresholds"),
                    Flt("Cubic_Threshold", "Cubic threshold", "Sell, weight, cubic and pallet thresholds"),
                    Flt("Pallet_Threshold", "Pallet threshold", "Sell, weight, cubic and pallet thresholds"),
                    Flt("ChgWeight_Weight_Diff", "Charge weight vs weight diff", "Sell, weight, cubic and pallet thresholds"),
                    Flt("International_Check", "International check", "MoveIT and international checks", "Numeric value (FLOAT), not a yes/no flag."),
                    Flt("Moveit_Dups_Check", "MoveIT duplicates check", "MoveIT and international checks", "Numeric value (FLOAT)."),
                    Flt("Moveit_Customer_Check", "MoveIT customer check", "MoveIT and international checks", "Numeric value (FLOAT)."),
                    Bit("SurchargeCheck", "Surcharge check", "Surcharges"),
                    Text("SurchargeType", "Surcharge type", "Surcharges", 500, 500),
                    Deleted(false),
                    Stamp("ActionTime", "Last changed"),
                }
            },
            new IvtEntityDef
            {
                Key = "fuel", Title = "Fuel surcharges", Environments = Both,
                Description = "Reference fuel surcharge and buy discount per customer and carrier (DECIMAL(18,6), stored as entered).",
                Note = "Fuel settings are unavailable in production because required reference data is missing. Test settings are available.",
                ReadProcedure = "WebIVT.sp_getFuel", UpdateProcedure = "WebIVT.sp_UpdateFuel", CreateProcedure = "WebIVT.sp_NewFuel",
                Fields =
                {
                    Id(),
                    Text("Customer", "Customer", "Scope", 50, 50, true),
                    Text("Carrier", "Carrier", "Scope", 100, 100, true),
                    new IvtFieldDef { Column = "RefFuelSurch", Label = "Reference fuel surcharge", Section = "Values", Kind = IvtFieldKind.Decimal,
                        Update = P("@RefFuelSurch", SqlDbType.Decimal, 0, 18, 6), Create = P("@RefFuelSurch", SqlDbType.Decimal, 0, 18, 6), Help = "Stored exactly as entered (no percentage conversion)." },
                    new IvtFieldDef { Column = "BuyDiscount", Label = "Buy discount", Section = "Values", Kind = IvtFieldKind.Decimal,
                        Update = P("@BuyDiscount", SqlDbType.Decimal, 0, 18, 6), Create = P("@BuyDiscount", SqlDbType.Decimal, 0, 18, 6) },
                    Deleted(false),
                    Stamp("UpdatedDate", "Updated"),
                }
            },
            new IvtEntityDef
            {
                Key = "consolidation", Title = "Consolidation checks", Environments = Both,
                Description = "Which fields are compared when checking consolidated consignments per customer.",
                Note = "Consolidation settings are unavailable in production. Only the test environment supports this screen.",
                ReadProcedure = "WebIVT.sp_getConsolodition", UpdateProcedure = "WebIVT.sp_UpdateConsolidation", CreateProcedure = "WebIVT.sp_NewConsolidation",
                Fields =
                {
                    Id(),
                    Text("Customer", "Customer", "Identity", 50, 50, true),
                    Bit("Check_Connote", "Connote", "Checks"),
                    Bit("Check_Reciever", "Receiver", "Checks"),
                    Bit("Check_Suburb", "Suburb", "Checks"),
                    Bit("Check_ConDate", "Connote date", "Checks"),
                    Bit("Check_FreightCharge", "Freight charge", "Checks"),
                    Bit("Check_Carrier", "Carrier", "Checks"),
                    Deleted(false),
                }
            },
            new IvtEntityDef
            {
                Key = "email-control", Title = "Email control", Environments = Both,
                Description = "Which account managers receive the IVT emails.",
                ReadProcedure = "WebIVT.GetEmailControlDetails", UpdateProcedure = "WebIVT.sp_UpdateEmailControl", CreateProcedure = "WebIVT.sp_NewEmailControl",
                Fields =
                {
                    Id(),
                    Text("AccountManager", "Account manager", "Recipient", 100, 100, true),
                    Bit("IsReceivingEmail", "Receives email", "Recipient", true, true),
                    Stamp("ActionTime", "Last changed"),   // returned by IVT_Test only
                }
            },
        };

        public static IvtEntityDef Find(string key)
        {
            var def = All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
            if (def == null) throw new IvtOperationException("Unknown settings screen.");
            return def;
        }
    }
}
