using AFSDataGateway.Services.IvtAdmin;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IvtAdmin.Verification
{
    // Side-effect-free verification of the IVT Administration services against recording fakes.
    // Run: tests\IvtAdmin.Verification\run.ps1   (exit code 0 = all passed)
    public static class Program
    {
        private static readonly List<Tuple<string, Func<Task>>> Tests = new List<Tuple<string, Func<Task>>>();
        private static void Test(string name, Func<Task> body) { Tests.Add(Tuple.Create(name, body)); }
        private static void True(bool condition, string message) { if (!condition) throw new Exception("Assertion failed: " + message); }
        private static void Equal(object expected, object actual, string what)
        {
            if (!Equals(expected, actual)) throw new Exception("Assertion failed: " + what + " expected <" + expected + "> but was <" + actual + ">");
        }

        private static readonly IvtOperator Admin = new IvtOperator { Login = "ADMINUSER", IsAdmin = true, CanRead = true };
        private static readonly IvtOperator Reader = new IvtOperator { Login = "READER", IsAdmin = false, CanRead = true };
        private static readonly CancellationToken None = CancellationToken.None;

        private sealed class World
        {
            public CallLog Log = new CallLog();
            public FakeDb Db; public FakeAgent Agent; public FakeAudit Audit = new FakeAudit(); public FakeCoordinator Locks = new FakeCoordinator();
            public FakeLanding Landing; public FakeMail Mail; public FakeAzure Azure; public FakeProcess Process = new FakeProcess();
            public IvtAdminRuntime Rt;
            public World(DateTime? utcNow = null)
            {
                Db = new FakeDb(Log); Agent = new FakeAgent(Log, "IVT_Engine", "IVT_Engine_Silent", "IVT_Engine_Test_Run", "IVT_Engine_Execs_Reports", "IVT_REF_DATA_LOAD", "IVT_Hold_Release_Check");
                Landing = new FakeLanding(Log); Mail = new FakeMail(Log); Azure = new FakeAzure(Log);
                var settings = new IvtAdminSettings();
                settings.Connections.Production = "fake"; settings.Connections.Test = "fake"; settings.Connections.Msdb = "fake"; settings.Connections.Audit = "fake";
                settings.JobStartObserveSeconds = 0;
                var now = utcNow ?? new DateTime(2026, 10, 9, 2, 0, 0, DateTimeKind.Utc);   // Friday 9 Oct 2026, 13:00 Sydney
                Rt = new IvtAdminRuntime { Settings = settings, Database = Db, Agent = Agent, Audit = Audit, Coordinator = Locks, Landing = Landing,
                    ReportApi = new FakeApi(Log), Processes = Process, Mail = Mail, Azure = Azure, UtcNow = () => now, Delay = (t, c) => Task.FromResult(0) };
            }
            public int Mutations { get { return Db.Executed.Count + Log.Count("START ") + Log.Count("PUBLISH ") + Log.Count("MAIL ") + Log.Count("API ") + Log.Count("AZURE "); } }
        }

        private static IvtTable HoldTable(params object[][] rows) { return FakeDb.Table(ConnoteActionService.HoldColumns, rows); }
        private static object[] HoldRow(long id, string connote, string heldBy = "AMONE", string errCode = "12", string sub = "Orig sub")
        {
            // Id, jno, Connote, Reason, cons_date, customer, carrier, Sub_Reason, ErrCode, ErrPriority, ShortErrDesc, HeldBy, CurrentWeek
            return new object[] { id, "J" + id, connote, "Reason " + id, "2026-09-20", "CUST", "CARR", sub, errCode, "P1", "Short", heldBy, new DateTime(2026, 9, 25) };
        }
        private static object Param(List<IvtParam> ps, string name) { var p = ps.First(x => x.Name == name); return p.Value; }

        public static int Main()
        {
            // ------------------------------------------------ 1. authorisation
            Test("Non-admin operators cannot mutate anything", async () =>
            {
                var w = new World();
                var rc = new RunControlService(w.Rt);
                var results = new List<IvtActionResult>
                {
                    await rc.StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Reader, None),
                    await rc.RollbackAsync(IvtEnvironment.Test, "ROLLBACK TEST", Reader, None),
                    await new ReferenceDataService(w.Rt).CreateAsync(ReferenceCatalog.Find("rules"), IvtEnvironment.Production, new JObject(), Reader, None),
                    await new ReferenceDataService(w.Rt).UpdateContactsAsync(IvtEnvironment.Production, "a@b.com", "", "", Reader, None),
                    await new ConnoteActionService(w.Rt).HoldAsync(new ConnoteActionService.ActionRequest { Ids = { 1 } }, Reader, None),
                    await new CommentService(w.Rt).AddAsync("123", "hello", Reader, None),
                    await new FileLoadService(w.Rt).UploadAsync(IvtFileLoadKind.BulkHold, "a.csv", Encoding.UTF8.GetBytes("jno,Connote,Comment\n1,C,x"), Reader, None),
                    await new FileLoadService(w.Rt).StartLoadAsync(IvtFileLoadKind.BulkHold, true, Reader, None),
                    await new NotificationActionService(w.Rt).CallLegacyAsync("notifyam", true, Reader, None),
                    await new NotificationActionService(w.Rt).RunTestNotifierAsync(Reader, None),
                };
                True(results.All(r => !r.Ok && r.Outcome == IvtOutcome.Rejected), "all rejected");
                Equal(0, w.Mutations, "side effects");
            });

            // ------------------------------------------------ 2. environment routing
            Test("Test run prepares IVT_Test only and starts the test job", async () =>
            {
                var w = new World();
                var r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Test, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Submitted, r.Outcome, "outcome");
                True(w.Db.Executed.All(e => e.Item1 == IvtDbTarget.Test), "only IVT_Test touched");
                Equal(1, w.Db.Executed.Count, "one preparation");
                True(w.Log.Events.Contains("START IVT_Engine_Test_Run") && w.Log.Count("START ") == 1, "test job started");
            });
            Test("Production run prepares IVT then IVT_Test, then starts IVT_Engine", async () =>
            {
                var w = new World();
                var r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Submitted, r.Outcome, "outcome");
                var prod = w.Log.IndexOf("EXEC Production WebIVT.sp_FileloadControl");
                var test = w.Log.IndexOf("EXEC Test WebIVT.sp_FileloadControl");
                var start = w.Log.IndexOf("START IVT_Engine");
                True(prod >= 0 && test > prod && start > test, "order IVT -> IVT_Test -> job: " + string.Join(" | ", w.Log.Events));
                var p = w.Db.Executed[0].Item3;
                Equal(new DateTime(2026, 10, 2), Param(p, "@InvoiceWeek"), "@InvoiceWeek");
                Equal("All", Param(p, "@Type"), "@Type");
                Equal(System.Data.SqlDbType.Date, p.First(x => x.Name == "@InvoiceWeek").Type, "@InvoiceWeek type");
            });
            Test("productionRunPreparesTest=false prepares IVT only", async () =>
            {
                var w = new World(); w.Rt.Settings.ProductionRunPreparesTest = false;
                await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Silent, new DateTime(2026, 10, 2), true, Admin, None);
                True(w.Db.Executed.All(e => e.Item1 == IvtDbTarget.Production), "IVT only");
                True(w.Log.Events.Contains("START IVT_Engine_Silent"), "silent job");
            });
            Test("Settings updates go to the selected environment's database", async () =>
            {
                var w = new World();
                w.Db.Queries["WebIVT.GetEmailControlDetails"] = (t, p) => FakeDb.Table(new[] { "Id", "AccountManager", "IsReceivingEmail" }, new object[] { 5L, "AMONE", true });
                var r = await new ReferenceDataService(w.Rt).SetFlagAsync(ReferenceCatalog.Find("email-control"), IvtEnvironment.Test, "", 5, "IsReceivingEmail", false, Admin, None);
                Equal(IvtOutcome.Succeeded, r.Outcome, "outcome");
                Equal(IvtDbTarget.Test, w.Db.Executed.Single().Item1, "target");
                Equal(false, Param(w.Db.Executed.Single().Item3, "@IsReceivingEmail"), "toggled value");
                Equal("AMONE", Param(w.Db.Executed.Single().Item3, "@AccountManager"), "other fields from database");
            });

            // ------------------------------------------------ 3. invalid dates / missing confirmations
            Test("Missing, invalid or future invoice week and missing landing confirmation cause no mutation", async () =>
            {
                var w = new World();
                var rc = new RunControlService(w.Rt);
                var a = await rc.StartRunAsync(IvtRunKind.Production, null, true, Admin, None);
                var b = await rc.StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 16), true, Admin, None);
                var c = await rc.StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), false, Admin, None);
                var d = await rc.RollbackAsync(IvtEnvironment.Production, "rollback production", Admin, None);
                var e = await new FileLoadService(w.Rt).StartLoadAsync(IvtFileLoadKind.BmsCheck, false, Admin, None);
                True(new[] { a, b, c, d, e }.All(r => r.Outcome == IvtOutcome.Rejected), "all rejected");
                True(b.Message.Contains("future"), "future message");
                Equal(0, w.Mutations, "side effects");
            });
            Test("Default invoice week is the most recent Friday strictly before today (business time zone)", () =>
            {
                Equal(new DateTime(2026, 10, 2), new RunControlService(new World(new DateTime(2026, 10, 9, 2, 0, 0, DateTimeKind.Utc)).Rt).DefaultInvoiceWeek(), "on a Friday");
                Equal(new DateTime(2026, 10, 9), new RunControlService(new World(new DateTime(2026, 10, 10, 2, 0, 0, DateTimeKind.Utc)).Rt).DefaultInvoiceWeek(), "on Saturday (legacy chose 2 Oct)");
                Equal(new DateTime(2026, 10, 9), new RunControlService(new World(new DateTime(2026, 10, 9, 14, 0, 0, DateTimeKind.Utc)).Rt).DefaultInvoiceWeek(), "Saturday 01:00 Sydney while UTC is still Friday");
                return Task.FromResult(0);
            });

            // ------------------------------------------------ 4. duplicate / conflicting starts
            Test("A held start lock rejects a concurrent start", async () =>
            {
                var w = new World(); w.Locks.Held.Add(RunControlService.EngineLock);
                var r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Rejected, r.Outcome, "outcome"); Equal(0, w.Mutations, "side effects");
            });
            Test("A running conflicting job rejects the start before any preparation", async () =>
            {
                var w = new World(); w.Agent.Jobs["IVT_Engine_Test_Run"].IsRunning = true;
                var r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Rejected, r.Outcome, "outcome");
                True(r.Message.Contains("IVT_Engine_Test_Run"), "names the running job");
                Equal(0, w.Mutations, "side effects");
            });
            Test("A second start right after a submission that is not yet visible is rejected", async () =>
            {
                var w = new World(); w.Agent.RunAfterStart = false;
                var rc = new RunControlService(w.Rt);
                var first = await rc.StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Submitted, first.Outcome, "first");
                var second = await rc.StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Rejected, second.Outcome, "second");
                Equal(1, w.Log.Count("START "), "started once");
            });
            Test("Rollback is refused while a job is running", async () =>
            {
                var w = new World(); w.Agent.Jobs["IVT_Hold_Release_Check"].IsRunning = true;
                var r = await new RunControlService(w.Rt).RollbackAsync(IvtEnvironment.Production, "ROLLBACK PRODUCTION", Admin, None);
                Equal(IvtOutcome.Rejected, r.Outcome, "outcome"); Equal(0, w.Mutations, "side effects");
            });
            Test("Rollback runs the procedure on the selected environment only after typed confirmation", async () =>
            {
                var w = new World();
                var r = await new RunControlService(w.Rt).RollbackAsync(IvtEnvironment.Test, "ROLLBACK TEST", Admin, None);
                Equal(IvtOutcome.Succeeded, r.Outcome, "outcome");
                Equal("WebIVT.Rollback_Invoice_Week_Load", w.Db.Executed.Single().Item2, "procedure");
                Equal(IvtDbTarget.Test, w.Db.Executed.Single().Item1, "target");
                Equal(0, w.Db.Executed.Single().Item3.Count, "no parameters");
            });

            // ------------------------------------------------ 5. missing jobs
            Test("A missing engine job is an error and nothing is prepared", async () =>
            {
                var w = new World(); w.Agent.Jobs.Remove("IVT_Engine");
                var r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                True(!r.Ok && r.Message.Contains("not found"), "error: " + r.Message);
                Equal(0, w.Mutations, "side effects");
            });
            Test("A failed sp_start_job after preparation is FAILED, not submitted", async () =>
            {
                var w = new World(); w.Agent.StartFailure = new IvtOperationException("SQL Agent refused");
                var r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Test, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Failed, r.Outcome, "outcome");
                True(!r.Ok, "not ok");
            });
            Test("A lost start response is AMBIGUOUS and warns not to start again", async () =>
            {
                var w = new World(); w.Agent.StartFailure = new TimeoutException("network");
                var r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Test, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Ambiguous, r.Outcome, "outcome");
                True(r.Message.Contains("Do NOT start"), "warning");
            });

            // ------------------------------------------------ 6/7/13. order, SQL-only, submission != completion
            Test("Preparation failure stops before the job; SQL-only mode never calls Azure; start is reported as submitted", async () =>
            {
                var w = new World();
                w.Db.FailExecute = (t, p, ps) => t == IvtDbTarget.Test ? new InvalidOperationException("boom") : null;
                var failed = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(IvtOutcome.Failed, failed.Outcome, "prep failure");
                Equal(0, w.Log.Count("START "), "job not started");
                var w2 = new World();
                var ok = await new RunControlService(w2.Rt).StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                Equal(0, w2.Log.Count("AZURE"), "no Azure calls");
                True(ok.Message.Contains("not completion"), "not presented as completion");
                Equal("SUBMITTED", w2.Audit.Entries.Last().Outcome, "audited as SUBMITTED");
            });
            Test("Explicit Azure mirroring reports stages separately and records the run id", async () =>
            {
                var w = new World(); w.Rt.Settings.ProductionRunMirrorsToAzure = true; w.Rt.Settings.Connections.AzureIvt = "fake";
                var r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Production, new DateTime(2026, 10, 2), true, Admin, None);
                True(w.Log.IndexOf("AZURE PIPELINE") > w.Log.IndexOf("START IVT_Engine"), "azure after SQL Agent start");
                True(w.Db.Executed.Any(e => e.Item1 == IvtDbTarget.AzureIvt), "Azure IVT prepared");
                True(r.Steps.Any(s => s.Name == "Azure: start pipeline" && s.Message.Contains("run-123")), "run id reported");
            });

            // ------------------------------------------------ 8. procedure names and parameter mapping
            Test("Single hold maps legacy fields: ReleasedBy=HeldBy, ActionedBy=operator, InvoiceWeek=CurrentWeek, row sub-reason", async () =>
            {
                var w = new World();
                w.Db.Queries["WebIVT.sp_getIVTResultDetail"] = (t, p) => HoldTable(HoldRow(7, "CN7", "AMSEVEN"), HoldRow(8, "CN8"));
                var r = await new ConnoteActionService(w.Rt).HoldAsync(new ConnoteActionService.ActionRequest { Search = "CN", Ids = { 7 }, Text = "" }, Admin, None);
                Equal(IvtOutcome.Succeeded, r.Outcome, "outcome");
                var call = w.Db.Executed.Single();
                Equal("WebIVT.sp_Hold_Connote", call.Item2, "procedure");
                var p = call.Item3;
                Equal("AMSEVEN", Param(p, "@ReleasedBy"), "@ReleasedBy"); Equal("ADMINUSER", Param(p, "@ActionedBy"), "@ActionedBy");
                Equal(new DateTime(2026, 9, 25), Param(p, "@InvoiceWeek"), "@InvoiceWeek"); Equal("Orig sub", Param(p, "@sub_reason"), "@sub_reason");
                Equal(12L, Param(p, "@ErrCode"), "@ErrCode bigint"); Equal("J7", Param(p, "@Jno"), "@Jno"); Equal(14, p.Count, "14 parameters");
            });
            Test("Hold-all uses the admin sub-reason and comment; release uses justification and the admin sub-reasons", async () =>
            {
                var w = new World();
                w.Db.Queries["WebIVT.sp_getIVTResultDetail"] = (t, p) => HoldTable(HoldRow(1, "A"), HoldRow(2, "B"));
                var r = await new ConnoteActionService(w.Rt).HoldAsync(new ConnoteActionService.ActionRequest { Search = "", All = true, Ids = { 1, 2 }, ExpectedCount = 2, Text = "Bulk check" }, Admin, None);
                Equal(IvtOutcome.Succeeded, r.Outcome, "hold all");
                True(w.Db.Executed.All(e => (string)Param(e.Item3, "@sub_reason") == "Custom Hold By IVT Admin" && (string)Param(e.Item3, "@Comment") == "Bulk check"), "hold-all values");
                var cols = ConnoteActionService.ReleaseColumns;
                var w2 = new World();
                w2.Db.Queries["#release"] = (t, p) => FakeDb.Table(cols, new object[] { 3L, "J3", "C3", "R", "2026-09-01", "CUST", "CARR", "s", "9", "P2", "Sh", "HELDBY", new DateTime(2026, 9, 18), "held comment" });
                var rel = await new ConnoteActionService(w2.Rt).ReleaseAsync(new ConnoteActionService.ActionRequest { Search = "C3", Ids = { 3 }, Text = "Checked with carrier" }, Admin, None);
                Equal(IvtOutcome.Succeeded, rel.Outcome, "release");
                var rp = w2.Db.Executed.Single().Item3;
                Equal("WebIVT.sp_Release_Connote", w2.Db.Executed.Single().Item2, "procedure");
                Equal("Custom Release by IVT Admin", Param(rp, "@sub_reason"), "single release sub-reason");
                Equal("Checked with carrier", Param(rp, "@Justification"), "@Justification"); Equal("held comment", Param(rp, "@Comment"), "@Comment from row");
                Equal("HELDBY", Param(rp, "@ReleasedBy"), "@ReleasedBy"); Equal(15, rp.Count, "15 parameters");
            });
            Test("Client-supplied ids outside the server result, or a changed result set, are refused", async () =>
            {
                var w = new World();
                w.Db.Queries["WebIVT.sp_getIVTResultDetail"] = (t, p) => HoldTable(HoldRow(1, "A"), HoldRow(2, "B"), HoldRow(3, "C"));
                var svc = new ConnoteActionService(w.Rt);
                var a = await svc.HoldAsync(new ConnoteActionService.ActionRequest { Ids = { 99 } }, Admin, None);
                var b = await svc.HoldAsync(new ConnoteActionService.ActionRequest { All = true, Ids = { 1, 2 }, ExpectedCount = 2, Text = "x" }, Admin, None);
                True(a.Outcome == IvtOutcome.Rejected && b.Outcome == IvtOutcome.Rejected, "both rejected");
                Equal(0, w.Db.Executed.Count, "no writes");
            });
            Test("Release requires a justification", async () =>
            {
                var w = new World();
                var r = await new ConnoteActionService(w.Rt).ReleaseAsync(new ConnoteActionService.ActionRequest { Ids = { 1 }, Text = "  " }, Admin, None);
                Equal(IvtOutcome.Rejected, r.Outcome, "rejected"); Equal(0, w.Mutations, "side effects");
            });
            Test("Reference catalog uses the verified procedure names, spellings and types", () =>
            {
                var cons = ReferenceCatalog.Find("consolidation");
                Equal("WebIVT.sp_getConsolodition", cons.ReadProcedure, "consolidation read spelling");
                True(cons.Fields.Any(f => f.Update != null && f.Update.Name == "@Check_Reciever"), "@Check_Reciever spelling");
                var rules = ReferenceCatalog.Find("rules");
                var onOff = rules.Fields.First(f => f.Column == "OnOff"); var mandatory = rules.Fields.First(f => f.Column == "Mandatory");
                Equal(System.Data.SqlDbType.Int, onOff.Update.Type, "sp_UpdateRule @OnOff int"); Equal(System.Data.SqlDbType.Bit, onOff.Create.Type, "sp_NewRule @OnOff bit");
                Equal(System.Data.SqlDbType.Bit, mandatory.Update.Type, "sp_UpdateRule @Mandatory bit"); Equal(System.Data.SqlDbType.Int, mandatory.Create.Type, "sp_NewRule @Mandatory int");
                Equal(System.Data.SqlDbType.BigInt, rules.Fields.First(f => f.Column == "ErrCode").Update.Type, "@ErrCode bigint");
                Equal(System.Data.SqlDbType.Int, ReferenceCatalog.Find("account-managers").Fields.First(f => f.Column == "IsDeleted").Update.Type, "AM IsDeleted int");
                var prefix = ReferenceCatalog.Find("customer-carrier");
                Equal(IvtFieldKind.Float, prefix.Fields.First(f => f.Column == "International_Check").Kind, "International_Check numeric, not boolean");
                Equal(System.Data.SqlDbType.Money, prefix.Fields.First(f => f.Column == "TotalSell_Threshold").Update.Type, "money");
                Equal(26, prefix.Fields.Count(f => f.Create != null), "26 sp_NewPrefix business fields");
                var fuel = ReferenceCatalog.Find("fuel").Fields.First(f => f.Column == "RefFuelSurch").Update;
                Equal((byte)18, fuel.Precision, "fuel precision"); Equal((byte)6, fuel.Scale, "fuel scale");
                var excl = ReferenceCatalog.Find("rule-exclusions").Fields.First(f => f.Column == "IsDeleted");
                True(excl.HasCreateFixedValue && Equals(excl.CreateFixedValue, false), "sp_NewRuleExclusion @IsDeleted = false");
                return Task.FromResult(0);
            });
            Test("Rule create sends exactly the sp_NewRule parameters with their own types", async () =>
            {
                var w = new World();
                var values = JObject.Parse("{ErrCode:'103', ErrPriority:'High', ShortErrDesc:'GM', ErrDesc:'Gross', RuleType:'Sell', OnOff:true, Mandatory:false, Customer:'', Carrier:'', Beneficiary:'', BeneficiaryContact:''}");
                var r = await new ReferenceDataService(w.Rt).CreateAsync(ReferenceCatalog.Find("rules"), IvtEnvironment.Production, values, Admin, None);
                Equal(IvtOutcome.Succeeded, r.Outcome, "created");
                var p = w.Db.Executed.Single().Item3;
                Equal("WebIVT.sp_NewRule", w.Db.Executed.Single().Item2, "procedure");
                True(!p.Any(x => x.Name == "@Id" || x.Name == "@IsDeleted"), "no Id/IsDeleted on create");
                Equal(103L, Param(p, "@ErrCode"), "bigint"); Equal(true, Param(p, "@OnOff"), "OnOff as bit"); Equal(0, Param(p, "@Mandatory"), "Mandatory as int");
                Equal(null, Param(p, "@Customer"), "blank optional text is NULL");
            });
            Test("Rule update on IVT_Test sends @VCode (verified test contract); production sends @RuleType", async () =>
            {
                foreach (var env in new[] { IvtEnvironment.Test, IvtEnvironment.Production })
                {
                    var w = new World();
                    w.Db.Queries["WebIVT.GetRuleDetails"] = (t, p) => FakeDb.Table(new[] { "Id", "ErrPriority", "ErrCode", "ErrDesc", "ShortErrDesc", "OnOff", "Mandatory", "RuleType", "Customer", "Carrier", "Beneficiary", "BeneficiaryContact", "IsDeleted", "ActionTime" },
                        new object[] { 4L, "H", 103, "d", "s", true, false, "Sell", null, null, null, null, false, DateTime.Now });
                    var r = await new ReferenceDataService(w.Rt).UpdateAsync(ReferenceCatalog.Find("rules"), env, "", JObject.Parse("{Id:4, RuleType:'Buy'}"), Admin, None);
                    Equal(IvtOutcome.Succeeded, r.Outcome, "updated " + env);
                    var p = w.Db.Executed.Single().Item3;
                    var expected = env == IvtEnvironment.Test ? "@VCode" : "@RuleType";
                    Equal("Buy", Param(p, expected), expected);
                    True(!p.Any(x => x.Name == (env == IvtEnvironment.Test ? "@RuleType" : "@VCode")), "other name absent");
                    Equal(1, Param(p, "@OnOff"), "unchanged OnOff carried as INT 1");
                    Equal(13, p.Count, "13 parameters");
                }
            });
            Test("Invalid numbers are rejected, never silently converted", async () =>
            {
                var w = new World();
                var bad = new[] { "{ErrCode:'12a', ErrPriority:'H', ShortErrDesc:'x', OnOff:true, Mandatory:true}", "{ErrCode:'99999999999999999999', ErrPriority:'H', ShortErrDesc:'x', OnOff:true, Mandatory:true}" };
                foreach (var json in bad)
                {
                    var r = await new ReferenceDataService(w.Rt).CreateAsync(ReferenceCatalog.Find("rules"), IvtEnvironment.Production, JObject.Parse(json), Admin, None);
                    Equal(IvtOutcome.Rejected, r.Outcome, "rejected " + json);
                }
                var fuel = await new ReferenceDataService(w.Rt).CreateAsync(ReferenceCatalog.Find("fuel"), IvtEnvironment.Production, JObject.Parse("{Customer:'C', Carrier:'X', RefFuelSurch:'0.1234567', BuyDiscount:'1'}"), Admin, None);
                Equal(IvtOutcome.Rejected, fuel.Outcome, "more than 6 decimals rejected");
                Equal(0, w.Db.Executed.Count, "no writes");
            });

            // ------------------------------------------------ 9. bulk updates
            Test("Update-all reports partial success accurately and writes only changed rows", async () =>
            {
                var w = new World();
                w.Db.Queries["WebIVT.sp_getRuleExclusion"] = (t, p) => FakeDb.Table(new[] { "Id", "RuleCode", "Justification", "Carrier", "Customer", "IsDeleted" },
                    new object[] { 1L, 5L, "a", "X", "C1", false }, new object[] { 2L, 5L, "b", "X", "C2", false }, new object[] { 3L, 5L, "c", "X", "C3", false });
                w.Db.FailExecute = (t, proc, ps) => (long)Param(ps, "@Id") == 2 ? new InvalidOperationException("row 2 fails") : null;
                var rows = JArray.Parse("[{Id:1, RuleCode:'5', Justification:'a2', Carrier:'X', Customer:'C1', IsDeleted:false},{Id:2, RuleCode:'5', Justification:'b2', Carrier:'X', Customer:'C2', IsDeleted:false},{Id:3, RuleCode:'5', Justification:'c', Carrier:'X', Customer:'C3', IsDeleted:false}]");
                var r = await new ReferenceDataService(w.Rt).UpdateAllAsync(ReferenceCatalog.Find("rule-exclusions"), IvtEnvironment.Production, "5", 3, rows, Admin, None);
                Equal(IvtOutcome.Partial, r.Outcome, "partial"); True(!r.Ok, "not ok");
                Equal(2, w.Db.Executed.Count, "row 3 unchanged, not written");
                True(r.Message.Contains("1 saved") && r.Message.Contains("1 FAILED"), "counts: " + r.Message);
                Equal("PARTIAL", w.Audit.Entries.Last().Outcome, "audited partial");
            });
            Test("Update-all refuses when the displayed result set changed or a row is outside it", async () =>
            {
                var w = new World();
                w.Db.Queries["WebIVT.sp_getRuleOption"] = (t, p) => FakeDb.Table(new[] { "Id", "RuleCode", "OptionDesc", "OptionValue", "OptionValue_Min", "OptionValue_Max", "Carrier", "Customer", "Moveit_Customer_Id", "IsDeleted" },
                    new object[] { 1L, 5L, "d", "10", null, null, null, null, null, false });
                var svc = new ReferenceDataService(w.Rt);
                var a = await svc.UpdateAllAsync(ReferenceCatalog.Find("rule-options"), IvtEnvironment.Production, "", 2, JArray.Parse("[{Id:1, OptionValue:'11'},{Id:2, OptionValue:'1'}]"), Admin, None);
                var b = await svc.UpdateAsync(ReferenceCatalog.Find("rule-options"), IvtEnvironment.Production, "", JObject.Parse("{Id:77, OptionValue:'1'}"), Admin, None);
                True(a.Outcome == IvtOutcome.Rejected && b.Outcome == IvtOutcome.Rejected, "rejected");
                Equal(0, w.Db.Executed.Count, "no writes");
                var c = await svc.UpdateAsync(ReferenceCatalog.Find("rule-options"), IvtEnvironment.Production, "", JObject.Parse("{Id:1, OptionValue:'abc'}"), Admin, None);
                Equal(IvtOutcome.Succeeded, c.Outcome, "text option value accepted");
                Equal("abc", Param(w.Db.Executed.Single().Item3, "@OptionValue"), "option value kept as text");
                Equal(null, Param(w.Db.Executed.Single().Item3, "@Moveit_Customer_Id"), "NULL stays NULL");
            });

            // ------------------------------------------------ 10. manual vs file-based
            Test("File-based bulk hold publishes the file and starts the loader job, never sp_Hold_Connote", async () =>
            {
                var w = new World();
                var svc = new FileLoadService(w.Rt);
                var up = await svc.UploadAsync(IvtFileLoadKind.BulkHold, "hold.csv", Encoding.UTF8.GetBytes("jno,Connote,Comment\r\n123,CN1,\"x, y\"\r\n"), Admin, None);
                Equal(IvtOutcome.Succeeded, up.Outcome, "upload");
                var start = await svc.StartLoadAsync(IvtFileLoadKind.BulkHold, true, Admin, None);
                Equal(IvtOutcome.Submitted, start.Outcome, "submitted");
                True(w.Log.Events.Contains("START IVT_Hold_Release_Check"), "loader job");
                Equal(0, w.Db.Executed.Count, "no hold procedure");
            });
            Test("Template validation rejects wrong headers, non-integer jno and unsafe names", () =>
            {
                var hold = FileLoadService.Template(IvtFileLoadKind.BulkHold);
                True(FileLoadService.Validate(hold, "a.csv", Encoding.UTF8.GetBytes("JNO,Connote,Comment\n1,a,b"), 1000000).Any(p => p.Contains("Missing")), "case-sensitive header");
                True(FileLoadService.Validate(hold, "a.csv", Encoding.UTF8.GetBytes("jno,Connote,Comment\n12x,a,b"), 1000000).Any(p => p.Contains("whole number")), "integer jno");
                True(FileLoadService.Validate(hold, "..\\evil.csv", Encoding.UTF8.GetBytes("jno,Connote,Comment\n1,a,b"), 1000000).Count > 0, "path traversal");
                True(FileLoadService.Validate(hold, "a.txt", Encoding.UTF8.GetBytes("jno,Connote,Comment\n1,a,b"), 1000000).Count > 0, "extension");
                True(FileLoadService.Validate(FileLoadService.Template(IvtFileLoadKind.ReferenceData), "a.csv", Encoding.UTF8.GetBytes("x\n1"), 1000000).Count > 0, "unverified template blocked");
                Equal(0, FileLoadService.Validate(hold, "ok file.csv", Encoding.UTF8.GetBytes("﻿jno,Connote,Comment\n1,a,\"b\nc\"\n,,skip\n"), 1000000).Count, "valid file");
                return Task.FromResult(0);
            });
            Test("A load with no waiting file is refused", async () =>
            {
                var w = new World();
                var r = await new FileLoadService(w.Rt).StartLoadAsync(IvtFileLoadKind.BmsCheck, true, Admin, None);
                Equal(IvtOutcome.Rejected, r.Outcome, "rejected"); Equal(0, w.Mutations, "side effects");
            });

            // ------------------------------------------------ 11. comment notification failure
            Test("Notification failure keeps the saved comment; retry sends without inserting again", async () =>
            {
                var w = new World(); w.Mail.Fail = true;
                w.Db.Queries["WebIVT.sp_GetConsignmentLog"] = (t, p) => FakeDb.Table(new[] { "JobNumber", "LogType", "LogText", "CreatedBy", "Connote", "Customer" }, new object[] { "123", "Hold", "x", "A", "CN<1>", "CUST&CO" });
                w.Db.Queries["#contacts"] = (t, p) => FakeDb.Table(new[] { "ADMIN_Contact", "Finance_Contact" }, new object[] { "admin1@x.com; admin2@x.com", "fin@x.com" });
                w.Rt.Settings.Comments.EmailDomain = "example.com";
                var svc = new CommentService(w.Rt);
                var r = await svc.AddAsync("123", "Please <b>check</b>", Admin, None);
                Equal(IvtOutcome.SavedNotifyFailed, r.Outcome, "saved, notify failed"); True(r.Ok, "comment saved");
                Equal(1, w.Db.Executed.Count(e => e.Item2 == "dbo.sp_Insert_ConsignmentLogBook"), "inserted once");
                var insert = w.Db.Executed.Single().Item3;
                Equal("Hold", Param(insert, "@LogType"), "LogType"); Equal("ADMINUSER", Param(insert, "@CreatedBy"), "CreatedBy = operator");
                w.Mail.Fail = false;
                var retry = await svc.RetryNotificationAsync(r.AuditId.Value, Admin, None);
                Equal(IvtOutcome.Succeeded, retry.Outcome, "retry sent");
                Equal(1, w.Db.Executed.Count(e => e.Item2 == "dbo.sp_Insert_ConsignmentLogBook"), "still one insert");
                var mail = w.Mail.Sent.Single();
                True(mail.Item1.Count == 2 && mail.Item2.Contains("fin@x.com") && mail.Item2.Contains("adminuser@example.com"), "To admins, CC finance + operator");
                True(mail.Item4.Contains("CN&lt;1&gt;") && mail.Item4.Contains("CUST&amp;CO") && mail.Item4.Contains("&lt;b&gt;check"), "HTML-encoded");
                var again = await svc.RetryNotificationAsync(r.AuditId.Value, Admin, None);
                Equal(IvtOutcome.Rejected, again.Outcome, "no second send once delivered");
            });
            Test("Empty comments are rejected", async () =>
            {
                var w = new World();
                var r = await new CommentService(w.Rt).AddAsync("123", "   ", Admin, None);
                Equal(IvtOutcome.Rejected, r.Outcome, "rejected"); Equal(0, w.Mutations, "side effects");
            });

            // ------------------------------------------------ 12. CSV
            Test("CSV export escapes quotes, commas and newlines and neutralises formulas", () =>
            {
                var t = FakeDb.Table(new[] { "A", "B", "C", "D", "E" }, new object[] { "x,\"y\"", "line1\nline2", "=HYPERLINK(\"http://evil\")", "-12.50", null });
                var csv = Encoding.UTF8.GetString(IvtCsv.Build(t, new[] { "Search: a,b" })).TrimStart('﻿');
                var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.None);
                Equal("\"# Search: a,b\"", lines[0], "preamble quoted");
                Equal("A,B,C,D,E", lines[1], "header");
                True(csv.Contains("\"x,\"\"y\"\"\""), "quotes doubled");
                True(csv.Contains("\"line1\nline2\""), "newline quoted");
                True(csv.Contains("'=HYPERLINK"), "formula neutralised");
                True(csv.Contains(",-12.50,"), "negative number intact");
                True(csv.EndsWith(",\r\n"), "null is empty");
                var parsed = IvtCsv.Parse("a,\"b,c\",\"d\"\"e\"\r\n1,2,3");
                Equal("b,c", parsed[0][1], "parser comma"); Equal("d\"e", parsed[0][2], "parser quote");
                return Task.FromResult(0);
            });

            // ------------------------------------------------ 14. audit failure / notifications / reports
            Test("If the audit log cannot be written, nothing is changed", async () =>
            {
                var w = new World(); w.Audit.FailWrites = true;
                IvtActionResult r = null; Exception error = null;
                try { r = await new RunControlService(w.Rt).StartRunAsync(IvtRunKind.Test, new DateTime(2026, 10, 2), true, Admin, None); }
                catch (IvtOperationException ex) { error = ex; }
                True(error != null && error.Message.Contains("nothing was changed"), "refused");
                Equal(0, w.Mutations, "side effects");
            });
            Test("Repeated production notification requires explicit confirmation", async () =>
            {
                var w = new World();
                var svc = new NotificationActionService(w.Rt);
                Equal(IvtOutcome.Succeeded, (await svc.CallLegacyAsync("notifyam", false, Admin, None)).Outcome, "first");
                var second = await svc.CallLegacyAsync("notifyam", false, Admin, None);
                Equal(IvtOutcome.Rejected, second.Outcome, "repeat needs confirmation"); Equal("confirm-repeat", second.Data, "flag");
                Equal(IvtOutcome.Succeeded, (await svc.CallLegacyAsync("notifyam", true, Admin, None)).Outcome, "confirmed repeat");
                Equal(2, w.Log.Count("API notifyam"), "two calls");
                Equal(IvtOutcome.Rejected, (await svc.CallLegacyAsync("dropdatabase", true, Admin, None)).Outcome, "unknown operation");
            });
            Test("Test notifier success requires exit code 0; non-zero or timeout is not success", async () =>
            {
                var w = new World(); w.Rt.Settings.TestNotifier.Enabled = true; w.Rt.Settings.TestNotifier.Executable = "python.exe";
                var svc = new NotificationActionService(w.Rt);
                Equal(IvtOutcome.Succeeded, (await svc.RunTestNotifierAsync(Admin, None)).Outcome, "exit 0");
                w.Process.Result = new IvtProcessResult { ExitCode = 1, Error = "Traceback" };
                Equal(IvtOutcome.Failed, (await svc.RunTestNotifierAsync(Admin, None)).Outcome, "exit 1");
                w.Process.Result = new IvtProcessResult { TimedOut = true };
                Equal(IvtOutcome.Ambiguous, (await svc.RunTestNotifierAsync(Admin, None)).Outcome, "timeout");
            });
            Test("Report parameters: ALL keyword, required dates, parameterless reports send nothing", () =>
            {
                var svc = new QueryReportService(new World().Rt);
                var errors = new List<string>();
                var p = svc.Parameters(QueryReportService.Find("released"), new IvtReportFilters { Search = " ", InvoiceWeek = new DateTime(2026, 9, 25), From = new DateTime(2026, 9, 1), To = new DateTime(2026, 9, 30) }, errors);
                Equal(0, errors.Count, "valid"); Equal("ALL", Param(p, "@searchtxt"), "blank -> ALL"); Equal(4, p.Count, "4 params");
                errors.Clear();
                svc.Parameters(QueryReportService.Find("released"), new IvtReportFilters { From = new DateTime(2026, 9, 30), To = new DateTime(2026, 9, 1) }, errors);
                True(errors.Any(e => e.Contains("invoice week")) && errors.Any(e => e.Contains("before")), "missing week and reversed dates");
                errors.Clear();
                Equal(0, svc.Parameters(QueryReportService.Find("held-not-in-ivt"), new IvtReportFilters { Search = "x", InvoiceWeek = DateTime.Today }, errors).Count, "no parameters");
                var review = svc.Parameters(QueryReportService.Find("review"), new IvtReportFilters { From = DateTime.Today, To = DateTime.Today }, errors);
                True(!review.Any(x => x.Name == "@InvoiceWeek"), "review does not send @InvoiceWeek");
                var act = svc.Parameters(QueryReportService.Find("am-activity"), new IvtReportFilters { AccountManager = "JSMITH", From = DateTime.Today, To = DateTime.Today }, errors);
                Equal("ALL", Param(act, "@activity"), "activity ALL"); Equal("JSMITH", Param(act, "@am"), "am login");
                return Task.FromResult(0);
            });
            Test("Contacts accept address lists and reject invalid addresses", async () =>
            {
                var w = new World();
                w.Db.Queries["WebIVT.Get_CC_EmailContactDetails"] = (t, p) => FakeDb.Table(new[] { "CC_Contact", "EXEC_Contact", "ADMIN_Contact" }, new object[] { "a@x.com", "b@x.com", "c@x.com" });
                var svc = new ReferenceDataService(w.Rt);
                Equal(IvtOutcome.Rejected, (await svc.UpdateContactsAsync(IvtEnvironment.Production, "a@x.com; not-an-email", "", "", Admin, None)).Outcome, "invalid rejected");
                var ok = await svc.UpdateContactsAsync(IvtEnvironment.Test, "a@x.com,b@x.com ; c@x.com", "e@x.com", "f@x.com", Admin, None);
                Equal(IvtOutcome.Succeeded, ok.Outcome, "saved");
                Equal("a@x.com; b@x.com; c@x.com", Param(w.Db.Executed.Single().Item3, "@CC_Contact"), "normalised list");
                Equal(IvtDbTarget.Test, w.Db.Executed.Single().Item1, "test database");
            });

            // ------------------------------------------------ run
            var failed = 0;
            foreach (var t in Tests)
            {
                try { t.Item2().GetAwaiter().GetResult(); Console.WriteLine("PASS  " + t.Item1); }
                catch (Exception ex) { failed++; Console.WriteLine("FAIL  " + t.Item1 + "\n      " + ex.GetType().Name + ": " + ex.Message); }
            }
            Console.WriteLine();
            Console.WriteLine((Tests.Count - failed) + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
