/* V009: store explanations and sampling metadata for review rows, and expose the ML Findings review queue. */
IF COL_LENGTH(N'ML.ScoreAudit', N'IsReviewCandidate') IS NULL
    ALTER TABLE ML.ScoreAudit ADD
        IsReviewCandidate   BIT            NOT NULL CONSTRAINT DF_ML_ScoreAudit_IsReviewCandidate DEFAULT (0),
        SamplingStratum     NVARCHAR(40)   NULL,   -- MODEL_CANDIDATE | AE_CANDIDATE | RANDOM_CHECK
        SamplingProbability DECIMAL(9,6)   NULL,   -- 1 for candidates; n/N for the random check (bias correction)
        PrimaryReasonCode   NVARCHAR(50)   NULL,
        ReasonSummary       NVARCHAR(400)  NULL,
        ExplanationText     NVARCHAR(4000) NULL,
        RecommendedAction   NVARCHAR(1000) NULL,
        EvidenceJson        NVARCHAR(MAX)  NULL,
        AlreadyRuleFlagged  BIT            NULL,
        RuleCodes           NVARCHAR(400)  NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_ScoreAudit_Review' AND object_id = OBJECT_ID(N'ML.ScoreAudit'))
CREATE INDEX IX_ML_ScoreAudit_Review ON ML.ScoreAudit (ScoringWeek, IsReviewCandidate) INCLUDE (RunId, SnapshotKey, AEAnomalyPercentile, SamplingStratum);
GO

/* Review queue: review rows of the latest successful (non dry-run) SCORE run per week, with the current outcome. */
IF OBJECT_ID(N'ML.vReviewCandidates', N'V') IS NULL
EXEC (N'CREATE VIEW ML.vReviewCandidates AS SELECT 1 AS Placeholder;');
GO
ALTER VIEW ML.vReviewCandidates
AS
WITH LatestRun AS (
    SELECT r.RunId, r.ScoringWeek,
           ROW_NUMBER() OVER (PARTITION BY r.ScoringWeek ORDER BY r.StartedAt DESC) AS rn
      FROM ML.Run r
     WHERE r.RunType = N'SCORE' AND r.Status IN (N'SUCCEEDED', N'PUBLISHED') AND r.IsDryRun = 0
)
SELECT a.ScoreAuditId, a.RunId, a.ScoringWeek, a.SnapshotKey, a.TransactionKey, a.Decision, a.CandidateRoute,
       a.SamplingStratum, a.SamplingProbability, a.AEAnomalyPercentile, a.WeakFlagScore, a.ConfirmedExceptionProbability,
       a.EvidenceCount, a.ReasonCodes, a.PrimaryReasonCode, a.ReasonSummary, a.ExplanationText, a.RecommendedAction, a.EvidenceJson,
       a.AlreadyRuleFlagged, a.RuleCodes, a.ModelVersion, a.ClassifierVersion,
       s.Customer, s.CustomerGroup, s.Carrier, s.CarrierGroup, s.JobNumber, s.ConsignmentNote, s.SupplierRef, s.WorkCode,
       s.FromLocation, s.ToLocation, s.CurrencyCode, s.BuyNett, s.SellNett, s.Weight, s.ChargeableWeight, s.Cubic,
       s.AccountManager, s.LoginName,
       f.FeedbackId, f.ReviewOutcome, f.OutcomeType, f.ReviewedBy, f.ReviewedAt, f.Comments AS ReviewComments, f.OutcomeVersion
  FROM ML.ScoreAudit a
  JOIN LatestRun lr ON lr.RunId = a.RunId AND lr.rn = 1
  JOIN ML.TradingSnapshot s ON s.SnapshotKey = a.SnapshotKey
  LEFT JOIN ML.ReviewFeedback f ON f.SnapshotKey = a.SnapshotKey AND f.IsCurrent = 1
 WHERE a.IsReviewCandidate = 1;
GO
