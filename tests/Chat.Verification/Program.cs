using AFSDataGateway.Models;
using AFSDataGateway.Services.Chat;
using AFSDataGateway.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Principal;

class Program
{
    static void VerifyCompanyIdentities()
    {
        var emails = Environment.GetEnvironmentVariable("IVT_USER_EMAILS");
        var aliases = Environment.GetEnvironmentVariable("IVT_USER_ALIASES");
        try
        {
            Environment.SetEnvironmentVariable("IVT_USER_EMAILS", "jsmith=john.smith@efmlogistics.com.au;admin=admin@afslogistics.com.au");
            Environment.SetEnvironmentVariable("IVT_USER_ALIASES", @"EFM\John.Smith=jsmith");
            Check(UserIdentity.NormalizeLogin(@"AFS\jsmith") == "JSMITH", "Existing Windows login must retain its IVT account");
            Check(UserIdentity.NormalizeLogin(@"EFM\John.Smith") == "JSMITH", "Changed Windows login must map explicitly");
            Check(UserIdentity.NormalizeLogin("john.smith@EFMLOGISTICS.COM.AU") == "JSMITH", "Migrated email must retain its IVT account");
            Check(UserIdentity.NormalizeLogin("jsmith@afslogistics.com.au") == "JSMITH", "Old company email must still map");
            Check(UserIdentity.NormalizeLogin("other@efmlogistics.com.au") == "OTHER", "EFM UPNs must be supported");
            Check(UserIdentity.NormalizeLogin("jsmith@unrelated.example") == null, "Unrelated email domains must be refused");
            Check(UserIdentity.NormalizeLogin("jsmith@efmlogistics.com.au.evil.example") == null, "Domain suffix spoofing must be refused");
            Check(UserIdentity.EmailFor("JSMITH") == "john.smith@efmlogistics.com.au", "Notifications must use the migrated mailbox");
            Check(UserIdentity.EmailFor("other@efmlogistics.com.au") == "other@efmlogistics.com.au", "Full company emails must not get a second suffix");
            var users = new[] {
                new vIVTUser { Id = 1, LoginName = "JSMITH", IsActive = 1, UserType = 0 },
                new vIVTUser { Id = 2, LoginName = "ADMIN", IsActive = 1, UserType = 1 }
            }.AsQueryable();
            var scope = ChatAccessScope.Resolve(new GenericPrincipal(new GenericIdentity("john.smith@efmlogistics.com.au"), new string[0]), null, users);
            Check(scope.LoginName == "JSMITH" && !scope.IsAdmin, "Migration must retain the existing role and portfolio");
            Check(ChatReportService.RecipientFor(scope) == "john.smith@efmlogistics.com.au", "Reports must use the migrated mailbox");
            var admin = ChatAccessScope.Resolve(new GenericPrincipal(new GenericIdentity("admin@afslogistics.com.au"), new string[0]), "JSMITH", users);
            Check(ChatReportService.RecipientFor(admin) == "admin@afslogistics.com.au", "Impersonated reports must still go to their requesting admin");
            users.First().IsActive = 0;
            try
            {
                ChatAccessScope.Resolve(new GenericPrincipal(new GenericIdentity("john.smith@efmlogistics.com.au"), new string[0]), null, users);
                throw new Exception("Migrated inactive user was accepted");
            }
            catch (ChatAccessException) { }
            Console.WriteLine("PASS: AFS/EFM identity mapping, mailbox routing, domain validation, permissions and impersonation recipients.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IVT_USER_EMAILS", emails);
            Environment.SetEnvironmentVariable("IVT_USER_ALIASES", aliases);
        }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static ChatAccessScope Scope(bool admin = false)
    {
        return ChatAccessScope.Resolve(new GenericPrincipal(new GenericIdentity("AMONE"), new string[0]), null,
            new[] { new vIVTUser { Id = 1, LoginName = "AMONE", IsActive = 1, UserType = admin ? 1 : 0 } }.AsQueryable());
    }
    static ConsignmentForReview Hold(long id, string reason = "FORCE HOLD review", string owner = "AMONE", string job = null)
    {
        return new ConsignmentForReview { Id = id, jno = job ?? "J" + id, Connote = "C" + id, Reason = reason, ActionedBy = owner };
    }
    static void Reject(Action action)
    {
        try { action(); } catch (ChatQueryException) { return; }
        throw new Exception("Expected query rejection");
    }
    static int Main()
    {
        try
        {
            VerifyCompanyIdentities();
            using (var db = new MemoryDb())
            {
                var records = new List<ConsignmentForReview> { Hold(1), Hold(2, "Other FORCE HOLD"), Hold(3, owner: "OTHER"), Hold(4) };
                records[3].IsDeleted = true;
                db.ConsignmentForReview = new MemorySet<ConsignmentForReview>(records);
                var service = new ChatActionService(db, Scope());
                ChatPendingAction staged = null;
                var result = JObject.FromObject(service.ProposeBulkRelease("FORCE HOLD", "Approved after review", p => staged = p));
                Check((string)result["status"] == "awaiting_user_confirmation", "Batch must require confirmation");
                Check(staged.HoldIds.SequenceEqual(new long[] { 1 }), "Prefix, owner and deleted filters must all apply");
                Check(!records[0].IsDeleted, "Proposal must not change records");
                Check(staged.Summary.Contains("J1") && staged.Summary.Contains("Approved after review"), "Preview must show jobs and shared justification");
                Check((string)JObject.FromObject(service.ProposeBulkRelease("FORCE HOLD", "short", p => { throw new Exception("Staged invalid reason"); }))["status"] == "rejected", "Reject short justification");
                var admin = new ChatActionService(db, Scope(true));
                admin.ProposeBulkRelease("FORCE HOLD", "Approved after review", p => staged = p);
                Check(staged.HoldIds.Count == 2, "Admin may select other owners");
                records.Add(Hold(5, "Other reason", job: "J1"));
                Check((string)JObject.FromObject(service.ProposeBulkRelease("FORCE HOLD", "Approved after review", p => { throw new Exception("Staged partial job"); }))["status"] == "rejected", "Reject partial duplicate job");
                records.Clear(); records.AddRange(Enumerable.Range(1, 101).Select(i => Hold(i)));
                Check((string)JObject.FromObject(service.ProposeBulkRelease("FORCE HOLD", "Approved after review", p => { throw new Exception("Staged oversized batch"); }))["status"] == "rejected", "Never silently truncate a batch");
                records.RemoveAt(100);
                service.ProposeBulkRelease("FORCE HOLD", "Approved after review", p => staged = p);
                Check(staged.HoldIds.Count == 100, "Allow exactly 100 hold rows");
                staged.ScopeFingerprint = "changed";
                try { service.ExecuteAsync(staged).GetAwaiter().GetResult(); throw new Exception("Scope change was accepted"); }
                catch (ChatAccessException) { }
            }
            var source = new RecordingData();
            var history = ChatDatasets.All["tradingHistory"];
            Func<string, string, ChatFilter> period = (op, value) => new ChatFilter { Field = "reportingWeek", Op = op, Values = new List<string> { value } };
            var q = new ChatDatasetQuery();
            Reject(() => history.Run(source, Scope(), q));
            q.Filters.Add(period("gte", "2026-04-01"));
            Reject(() => history.Run(source, Scope(), q));
            q.Filters.Add(period("lte", "2026-10-01"));
            Reject(() => history.Run(source, Scope(), q));
            q.Filters[1] = period("lt", "2026-07-01");
            q.Filters.Add(new ChatFilter { Field = "workCode", Op = "equals", Values = new List<string> { "DG'; DROP TABLE test;--" } });
            q.Metrics.Add(new ChatMetric { Fn = "sum", Field = "sellTotalNett" });
            history.Run(source, Scope(), q);
            Check(source.Sql.Contains("[vTradingDataHistory]") && source.Sql.Contains("[LoginName] = @p0") && source.Sql.Contains("[CurrentWeek] >=") && source.Sql.Contains("[CurrentWeek] <"), "History SQL must constrain source, ownership and both dates");
            Check(!source.Sql.Contains("DROP TABLE") && source.Parameters.Any(p => Convert.ToString(p.Value).Contains("DROP TABLE")), "Values must remain SQL parameters");
            Check(source.Parameters.Any(p => Equals(p.Value, "AMONE")), "Scope must be parameterised");
            var tools = AzureOpenAiChatClient.Tools().Cast<JObject>();
            var bulk = tools.Single(t => (string)t["function"]["name"] == "ProposeBulkRelease");
            Check(bulk["function"]["parameters"]["required"].Count() == 2, "Bulk tool must require prefix and justification");
            Console.WriteLine("PASS: bulk proposal permissions, prefix matching, confirmation, justification, duplicate jobs, batch limits, scope changes, history bounds and SQL parameterisation.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    class MemoryDb : IVTEntities { }
    class MemorySet<T> : DbSet<T>, IQueryable<T> where T : class
    {
        readonly IQueryable<T> query;
        public MemorySet(List<T> records) { query = records.AsQueryable(); }
        Type IQueryable.ElementType { get { return query.ElementType; } }
        Expression IQueryable.Expression { get { return query.Expression; } }
        IQueryProvider IQueryable.Provider { get { return query.Provider; } }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return query.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return query.GetEnumerator(); }
    }
    class RecordingData : IChatDataSource
    {
        public string Sql; public List<SqlParameter> Parameters;
        public List<object[]> Query(string sql, IEnumerable<SqlParameter> parameters)
        { Sql = sql; Parameters = parameters.ToList(); return new List<object[]> { new object[] { 12m } }; }
        public IQueryable<vDash_Exception_Summary> Summaries { get { throw new NotSupportedException(); } }
        public IQueryable<vDash_CustomerCounts> Customers { get { throw new NotSupportedException(); } }
        public IQueryable<vIVT_Result> Exceptions { get { throw new NotSupportedException(); } }
        public IQueryable<Dash_LastIVTRun> Runs { get { throw new NotSupportedException(); } }
        public IQueryable<vDash_CustomerCarrierCounts> Trading { get { throw new NotSupportedException(); } }
    }
}
