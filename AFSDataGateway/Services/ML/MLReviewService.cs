using AFSDataGateway.Models;
using AFSDataGateway.Services.Chat;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;

namespace AFSDataGateway.Services.ML
{
    // One row of ML.vReviewCandidates (ML Findings page).
    public sealed class MLReviewItem
    {
        public long ScoreAuditId { get; set; }
        public DateTime ScoringWeek { get; set; }
        public string SnapshotKey { get; set; }
        public string Decision { get; set; }
        public string CandidateRoute { get; set; }
        public string SamplingStratum { get; set; }
        public decimal? AEAnomalyPercentile { get; set; }
        public decimal? WeakFlagScore { get; set; }
        public decimal? ConfirmedExceptionProbability { get; set; }
        public int? EvidenceCount { get; set; }
        public string ReasonCodes { get; set; }
        public string PrimaryReasonCode { get; set; }
        public string ReasonSummary { get; set; }
        public string ExplanationText { get; set; }
        public string RecommendedAction { get; set; }
        public bool? AlreadyRuleFlagged { get; set; }
        public string RuleCodes { get; set; }
        public string ModelVersion { get; set; }
        public string Customer { get; set; }
        public string CustomerGroup { get; set; }
        public string Carrier { get; set; }
        public string JobNumber { get; set; }
        public string ConsignmentNote { get; set; }
        public string WorkCode { get; set; }
        public string FromLocation { get; set; }
        public string ToLocation { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? BuyNett { get; set; }
        public decimal? SellNett { get; set; }
        public decimal? Weight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public string AccountManager { get; set; }
        public string LoginName { get; set; }
        public long? FeedbackId { get; set; }
        public string ReviewOutcome { get; set; }
        public string OutcomeType { get; set; }
        public string ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string ReviewComments { get; set; }
        public int? OutcomeVersion { get; set; }
        public decimal? Margin { get { return SellNett.HasValue && BuyNett.HasValue ? SellNett - BuyNett : null; } }
        // Set after load from WebIVT ConsignmentForReview (not part of the view).
        public bool IsHeld { get; set; }
        public string HeldBy { get; set; }
        public string HoldReason { get; set; }
    }

    // A hold placed from ML Findings, returned so the controller can send the usual notifications.
    public sealed class MLHoldResult
    {
        public string Error { get; set; }
        public string Connote { get; set; }
        public string Customer { get; set; }
        public string Reason { get; set; }
        public string Comment { get; set; }
    }

    public sealed class MLReviewPage
    {
        public List<MLReviewItem> Items { get; set; }
        public List<DateTime> Weeks { get; set; }
        public DateTime? Week { get; set; }
        public string Status { get; set; }
        public string Stratum { get; set; }
        public string ScopeLabel { get; set; }
        public int Total { get; set; }
        public int Reviewed { get; set; }
        public int Confirmed { get; set; }
        public int Valid { get; set; }
        public string Message { get; set; }
        public bool MessageIsError { get; set; }
    }

    public sealed class MLReviewService
    {
        public static readonly Dictionary<string, string[]> OutcomeTypes = new Dictionary<string, string[]>
        {
            { "CONFIRMED_EXCEPTION", new[] { "IncorrectBuy", "IncorrectSell", "IncorrectDimensions", "DuplicateCharge", "MissingCharge" } },
            { "CONFIRMED_VALID", new[] { "ValidRateChange", "ValidTransaction" } },
            { "INSUFFICIENT_INFORMATION", new[] { "InsufficientInformation" } },
        };

        public const string HoldSource = "AM Hold From ML Findings";
        public const string OtherHoldReason = "Other";
        public static readonly string[] HoldReasons =
        {
            "Incorrect buy (carrier) charge", "Incorrect sell (customer) charge", "Incorrect weight / dimensions", "Duplicate charge",
            "Missing charge", "Margin outside expected range", "Awaiting carrier confirmation", "Awaiting customer confirmation", OtherHoldReason
        };

        private readonly IVTEntities db;
        private readonly ChatAccessScope scope;

        public MLReviewService(IVTEntities db, ChatAccessScope scope)
        {
            this.db = db;
            this.scope = scope;
            db.Database.CommandTimeout = 60;
        }

        // Same visibility rule as the assistant: an AM sees rows routed to their login; admins see all.
        private string ScopeSql(List<object> parameters)
        {
            return scope.SqlFilter("[LoginName]", value =>
            {
                parameters.Add(new SqlParameter("@login", value));
                return "@login";
            });
        }

        public bool Available()
        {
            return db.Database.SqlQuery<int?>("SELECT CASE WHEN OBJECT_ID('ML.vReviewCandidates') IS NULL THEN 0 ELSE 1 END").FirstOrDefault() == 1;
        }

        public MLReviewPage Load(DateTime? week, string status, string stratum)
        {
            var page = new MLReviewPage { Status = status ?? "pending", Stratum = stratum ?? "all", ScopeLabel = scope.Label, Items = new List<MLReviewItem>(), Weeks = new List<DateTime>() };
            if (!Available()) return page;
            var p = new List<object>();
            var scopeSql = ScopeSql(p);
            page.Weeks = db.Database.SqlQuery<DateTime>("SELECT DISTINCT ScoringWeek FROM ML.vReviewCandidates WHERE " + scopeSql + " ORDER BY ScoringWeek DESC",
                p.Select(Clone).ToArray()).ToList();
            page.Week = week.HasValue && page.Weeks.Contains(week.Value.Date) ? week.Value.Date : page.Weeks.Cast<DateTime?>().FirstOrDefault();
            if (!page.Week.HasValue) return page;

            var sql = "SELECT * FROM ML.vReviewCandidates WHERE " + scopeSql + " AND ScoringWeek = @week";
            var args = p.Select(Clone).ToList();
            args.Add(new SqlParameter("@week", page.Week.Value));
            var all = db.Database.SqlQuery<MLReviewItem>(sql, args.ToArray()).ToList();
            page.Total = all.Count;
            page.Reviewed = all.Count(i => i.ReviewOutcome != null);
            page.Confirmed = all.Count(i => i.ReviewOutcome == "CONFIRMED_EXCEPTION");
            page.Valid = all.Count(i => i.ReviewOutcome == "CONFIRMED_VALID");
            IEnumerable<MLReviewItem> items = all;
            if (page.Status == "pending") items = items.Where(i => i.ReviewOutcome == null);
            else if (page.Status == "reviewed") items = items.Where(i => i.ReviewOutcome != null);
            if (page.Stratum == "candidates") items = items.Where(i => i.SamplingStratum != "RANDOM_CHECK");
            else if (page.Stratum == "random") items = items.Where(i => i.SamplingStratum == "RANDOM_CHECK");
            // Most unusual first; random checks are interleaved by their own score.
            page.Items = items.OrderByDescending(i => i.ConfirmedExceptionProbability ?? 0).ThenByDescending(i => i.AEAnomalyPercentile ?? 0).ToList();
            MarkHeld(page.Items);
            return page;
        }

        private void MarkHeld(List<MLReviewItem> items)
        {
            var jobs = items.Where(i => !string.IsNullOrEmpty(i.JobNumber)).Select(i => i.JobNumber).Distinct().ToList();
            if (jobs.Count == 0) return;
            var holds = db.ConsignmentForReview.AsNoTracking()
                .Where(h => !h.IsDeleted && jobs.Contains(h.jno))
                .Select(h => new { h.jno, h.ActionedBy, h.Sub_Reason, h.ActionTime })
                .ToList()
                .GroupBy(h => h.jno)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.ActionTime).First());
            foreach (var i in items)
            {
                if (i.JobNumber == null || !holds.ContainsKey(i.JobNumber)) continue;
                var h = holds[i.JobNumber];
                i.IsHeld = true;
                i.HeldBy = h.ActionedBy;
                i.HoldReason = h.Sub_Reason;
            }
        }

        // Holds the finding's consignment the same way the Trading Data page does (WebIVT ConsignmentForReview).
        // Findings from earlier weeks can be held too.
        public MLHoldResult Hold(string snapshotKey, string reason, string comments)
        {
            if (string.IsNullOrWhiteSpace(snapshotKey)) return new MLHoldResult { Error = "This finding is not available for your account." };
            if (reason == null || !HoldReasons.Contains(reason)) return new MLHoldResult { Error = "Please choose a reason for holding." };
            comments = (comments ?? "").Trim();
            if (comments.Length > 2000) return new MLHoldResult { Error = "Comments are limited to 2,000 characters." };
            if (reason == OtherHoldReason && comments.Length == 0) return new MLHoldResult { Error = "Please describe the reason for holding when choosing \"Other\"." };

            var p = new List<object>();
            var scopeSql = ScopeSql(p);
            p.Add(new SqlParameter("@key", snapshotKey));
            var item = db.Database.SqlQuery<MLReviewItem>("SELECT TOP 1 * FROM ML.vReviewCandidates WHERE " + scopeSql + " AND SnapshotKey = @key", p.ToArray()).FirstOrDefault();
            if (item == null || string.IsNullOrEmpty(item.JobNumber)) return new MLHoldResult { Error = "This finding is not available for your account." };

            if (db.ConsignmentForReview.Any(h => h.jno == item.JobNumber && !h.IsDeleted))
                return new MLHoldResult { Error = "Connote " + item.ConsignmentNote + " is already held." };

            // Current-week trading data when available; otherwise (earlier weeks) the finding's own snapshot.
            var current = db.vAllCurrentDetails.AsNoTracking().FirstOrDefault(c => c.JobNumber == item.JobNumber);
            var actor = scope.IsAdmin ? scope.Actor : scope.LoginName;
            db.ConsignmentForReview.Add(new ConsignmentForReview
            {
                Connote = current != null ? current.ConsignmentNote : item.ConsignmentNote,
                jno = item.JobNumber,
                cons_date = current != null ? current.Cons_Date.ToString() : null,
                Customer = current != null ? current.Customer : item.Customer,
                Carrier = current != null ? current.Carrier : item.Carrier,
                Reason = HoldSource,
                Sub_Reason = reason,
                ErrCode = "9999",
                ErrPriority = "P9999",
                ShortErrDesc = "Custom Rule",
                ReleasedBy = current != null ? current.AccountManager : item.AccountManager,
                InvoiceWeek = current != null ? current.CurrentWeek : item.ScoringWeek,
                Comment = comments.Length == 0 ? reason : comments,
                ActionedBy = actor,
                ActionTime = DateTime.Now,
            });
            db.SaveChanges();
            return new MLHoldResult { Connote = item.ConsignmentNote, Customer = current != null ? current.Customer : item.Customer, Reason = reason, Comment = comments };
        }

        // Appends a new outcome version (history is never overwritten). Returns an error message or null.
        public string SaveFeedback(string snapshotKey, string outcome, string outcomeType, decimal? correctedBuy, decimal? correctedSell,
                                   decimal? financialImpact, string comments)
        {
            string[] allowed;
            if (string.IsNullOrWhiteSpace(snapshotKey) || outcome == null || !OutcomeTypes.TryGetValue(outcome, out allowed))
                return "Please choose an assessment.";
            if (!allowed.Contains(outcomeType))
                return "Please choose a matching outcome type.";
            comments = (comments ?? "").Trim();
            if (comments.Length > 2000) return "Comments are limited to 2,000 characters.";
            if (outcome == "CONFIRMED_EXCEPTION" && comments.Length == 0 && !correctedBuy.HasValue && !correctedSell.HasValue)
                return "For a confirmed exception, add a comment or the corrected amount so the finding can be followed up.";

            var p = new List<object>();
            var scopeSql = ScopeSql(p);
            p.Add(new SqlParameter("@key", snapshotKey));
            var item = db.Database.SqlQuery<MLReviewItem>("SELECT TOP 1 * FROM ML.vReviewCandidates WHERE " + scopeSql + " AND SnapshotKey = @key", p.ToArray()).FirstOrDefault();
            if (item == null) return "This finding is not available for your account.";

            var reviewer = (scope.IsAdmin ? scope.Actor : scope.LoginName).ToLowerInvariant();
            using (var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                var current = db.Database.SqlQuery<CurrentFeedback>(
                    "SELECT FeedbackId, OutcomeVersion FROM ML.ReviewFeedback WITH (UPDLOCK, HOLDLOCK) WHERE SnapshotKey = @key AND IsCurrent = 1",
                    new SqlParameter("@key", snapshotKey)).FirstOrDefault();
                var version = db.Database.SqlQuery<int>("SELECT ISNULL(MAX(OutcomeVersion), 0) FROM ML.ReviewFeedback WHERE SnapshotKey = @key",
                    new SqlParameter("@key", snapshotKey)).First() + 1;
                if (current != null)
                    db.Database.ExecuteSqlCommand("UPDATE ML.ReviewFeedback SET IsCurrent = 0 WHERE FeedbackId = @id", new SqlParameter("@id", current.FeedbackId));
                var exceptionId = db.Database.SqlQuery<long?>("SELECT TOP 1 ExceptionId FROM ML.DetectedException WHERE SnapshotKey = @key AND IsActive = 1",
                    new SqlParameter("@key", snapshotKey)).FirstOrDefault();
                db.Database.ExecuteSqlCommand(
                    @"INSERT INTO ML.ReviewFeedback (ExceptionId, SnapshotKey, ReviewOutcome, OutcomeType, ReviewedBy, ReviewedAt, CorrectedBuyAmount, CorrectedSellAmount,
                          ConfirmedFinancialImpact, CurrencyCode, Comments, OutcomeVersion, SupersedesFeedbackId, IsCurrent, SamplingStratum, SamplingProbability)
                      SELECT @exceptionId, @key, @outcome, @type, @reviewer, SYSUTCDATETIME(), @buy, @sell, @impact, s.CurrencyCode, @comments, @version, @supersedes, 1,
                             a.SamplingStratum, a.SamplingProbability
                        FROM ML.TradingSnapshot s
                        LEFT JOIN ML.ScoreAudit a ON a.ScoreAuditId = @auditId
                       WHERE s.SnapshotKey = @key",
                    new SqlParameter("@exceptionId", (object)exceptionId ?? DBNull.Value), new SqlParameter("@key", snapshotKey),
                    new SqlParameter("@outcome", outcome), new SqlParameter("@type", outcomeType), new SqlParameter("@reviewer", reviewer),
                    new SqlParameter("@buy", (object)correctedBuy ?? DBNull.Value), new SqlParameter("@sell", (object)correctedSell ?? DBNull.Value),
                    new SqlParameter("@impact", (object)financialImpact ?? DBNull.Value), new SqlParameter("@comments", comments.Length == 0 ? (object)DBNull.Value : comments),
                    new SqlParameter("@version", version), new SqlParameter("@supersedes", current == null ? (object)DBNull.Value : current.FeedbackId),
                    new SqlParameter("@auditId", item.ScoreAuditId));
                if (exceptionId.HasValue)
                    db.Database.ExecuteSqlCommand("UPDATE ML.DetectedException SET ReviewStatus = @status WHERE ExceptionId = @id",
                        new SqlParameter("@status", outcome), new SqlParameter("@id", exceptionId.Value));
                tx.Commit();
            }
            return null;
        }

        private static object Clone(object p)
        {
            var s = (SqlParameter)p;
            return new SqlParameter(s.ParameterName, s.Value);
        }

        public sealed class CurrentFeedback  // public: EF materialises it via SqlQuery
        {
            public long FeedbackId { get; set; }
            public int OutcomeVersion { get; set; }
        }
    }
}
