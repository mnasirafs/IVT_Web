/* V006: score audit, published exceptions (staged then activated atomically), review feedback. */
IF OBJECT_ID(N'ML.ScoreAudit', N'U') IS NULL
CREATE TABLE ML.ScoreAudit (
    ScoreAuditId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_ScoreAudit PRIMARY KEY,
    RunId                 NVARCHAR(60)   NOT NULL CONSTRAINT FK_ML_ScoreAudit_Run REFERENCES ML.Run (RunId),
    SnapshotKey           NVARCHAR(64)   NOT NULL CONSTRAINT FK_ML_ScoreAudit_Snapshot REFERENCES ML.TradingSnapshot (SnapshotKey),
    TransactionKey        NVARCHAR(64)   NOT NULL,
    ScoringWeek           DATE           NOT NULL,
    Decision              NVARCHAR(30)   NOT NULL,
    DecisionReasonCodes   NVARCHAR(400)  NULL,
    CandidateRoute        NVARCHAR(30)   NULL,
    RankingScore          DECIMAL(19,6)  NULL,   -- ranking policy score; NOT a probability
    ConfirmedExceptionProbability DECIMAL(9,6) NULL,
    WeakFlagScore         DECIMAL(9,6)   NULL,   -- experimental weak-label model output; never shown as confirmed probability
    AEReconstructionError DECIMAL(19,6)  NULL,
    AEAnomalyPercentile   DECIMAL(9,4)   NULL,
    EvidenceCount         INT            NULL,
    ReasonCodes           NVARCHAR(400)  NULL,
    PeerFallbackLevel     TINYINT        NULL,
    PeerCount             INT            NULL,
    ModelVersion          NVARCHAR(60)   NULL,
    ClassifierVersion     NVARCHAR(60)   NULL,
    FeatureVersion        NVARCHAR(20)   NOT NULL,
    ConfigVersion         NVARCHAR(50)   NOT NULL,
    ScoredAt              DATETIME2(3)   NOT NULL,
    CONSTRAINT CK_ML_ScoreAudit_Decision CHECK (Decision IN (N'PUBLISHED', N'REJECTED_THRESHOLD', N'REJECTED_QUALITY', N'UNSUPPORTED_SEGMENT',
        N'INSUFFICIENT_EVIDENCE', N'REJECTED_MATERIALITY', N'DEFERRED_CAPACITY', N'SHADOW_ONLY', N'NOT_CANDIDATE'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_ScoreAudit_RunDecision' AND object_id = OBJECT_ID(N'ML.ScoreAudit'))
CREATE INDEX IX_ML_ScoreAudit_RunDecision ON ML.ScoreAudit (RunId, Decision) INCLUDE (SnapshotKey, AEAnomalyPercentile, RankingScore);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_ScoreAudit_Week' AND object_id = OBJECT_ID(N'ML.ScoreAudit'))
CREATE INDEX IX_ML_ScoreAudit_Week ON ML.ScoreAudit (ScoringWeek, ModelVersion);
GO

IF OBJECT_ID(N'ML.DetectedException', N'U') IS NULL
CREATE TABLE ML.DetectedException (
    ExceptionId              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_DetectedException PRIMARY KEY,
    PublicationKey           NVARCHAR(64)   NOT NULL,   -- hash(SnapshotKey, ModelVersion, FeatureVersion, ConfigVersion): idempotent reruns
    TransactionKey           NVARCHAR(64)   NOT NULL,
    SnapshotKey              NVARCHAR(64)   NOT NULL CONSTRAINT FK_ML_DetectedException_Snapshot REFERENCES ML.TradingSnapshot (SnapshotKey),
    CurrentWeek              DATE           NOT NULL,
    ConsignmentNote          NVARCHAR(50)   NULL,
    JobNumber                NVARCHAR(50)   NOT NULL,
    SupplierRef              NVARCHAR(50)   NULL,
    Customer                 NVARCHAR(50)   NOT NULL,
    CustomerGroup            NVARCHAR(320)  NULL,
    Carrier                  NVARCHAR(50)   NOT NULL,
    CarrierGroup             NVARCHAR(50)   NULL,
    FinanceGroup             NVARCHAR(320)  NULL,
    AccountManager           NVARCHAR(2000) NULL,
    DetectionRoute           NVARCHAR(30)   NOT NULL,
    PrimaryReasonCode        NVARCHAR(50)   NOT NULL,
    ReasonSummary            NVARCHAR(400)  NOT NULL,
    ExplanationText          NVARCHAR(4000) NOT NULL,
    RecommendedAction        NVARCHAR(1000) NOT NULL,
    ConfirmedExceptionProbability DECIMAL(9,6) NULL,
    AEReconstructionError    DECIMAL(19,6)  NULL,
    AEAnomalyPercentile      DECIMAL(9,4)   NULL,
    EvidenceJson             NVARCHAR(MAX)  NOT NULL,
    PeerGroupKey             NVARCHAR(400)  NULL,
    PeerCount                INT            NULL,
    BaselineCutoff           DATE           NULL,
    EvidenceCount            INT            NOT NULL,
    CurrencyCode             NCHAR(3)       NULL,
    BuyNett                  DECIMAL(18,4)  NULL,
    SellNett                 DECIMAL(18,4)  NULL,
    MarginAmount             DECIMAL(18,4)  NULL,
    MarginRatio              DECIMAL(19,6)  NULL,
    PotentialExposureAmount  DECIMAL(18,4)  NULL,
    ExposureCalculationMethod NVARCHAR(200) NULL,
    AlreadyRuleFlagged       BIT            NOT NULL,
    RuleCodes                NVARCHAR(400)  NULL,
    ExistingRuleResultId     BIGINT         NULL,
    RunId                    NVARCHAR(60)   NOT NULL CONSTRAINT FK_ML_DetectedException_Run REFERENCES ML.Run (RunId),
    ModelVersion             NVARCHAR(60)   NOT NULL,
    FeatureVersion           NVARCHAR(20)   NOT NULL,
    ConfigVersion            NVARCHAR(50)   NOT NULL,
    ScoredAt                 DATETIME2(3)   NOT NULL,
    PublishedAt              DATETIME2(3)   NULL,
    PublicationState         NVARCHAR(20)   NOT NULL,   -- STAGED | ACTIVE | SUPERSEDED | DISCARDED
    ReviewStatus             NVARCHAR(20)   NOT NULL CONSTRAINT DF_ML_DetectedException_ReviewStatus DEFAULT (N'PENDING'),
    IsActive                 BIT            NOT NULL CONSTRAINT DF_ML_DetectedException_IsActive DEFAULT (0),
    SupersededByExceptionId  BIGINT         NULL CONSTRAINT FK_ML_DetectedException_Supersede REFERENCES ML.DetectedException (ExceptionId),
    IsSynthetic              BIT            NOT NULL CONSTRAINT DF_ML_DetectedException_IsSynthetic DEFAULT (0),
    CONSTRAINT CK_ML_DetectedException_Route CHECK (DetectionRoute IN (N'CONFIRMED_PATTERN', N'NOVEL_PATTERN')),
    CONSTRAINT CK_ML_DetectedException_State CHECK (PublicationState IN (N'STAGED', N'ACTIVE', N'SUPERSEDED', N'DISCARDED')),
    CONSTRAINT CK_ML_DetectedException_Review CHECK (ReviewStatus IN (N'PENDING', N'IN_REVIEW', N'CONFIRMED_EXCEPTION', N'CONFIRMED_VALID', N'INSUFFICIENT_INFORMATION', N'ROUTED_TO_RULE_WORKFLOW')),
    CONSTRAINT CK_ML_DetectedException_Active CHECK (IsActive = 0 OR PublicationState = N'ACTIVE'),
    /* A probability may only be published on the confirmed-pattern route. */
    CONSTRAINT CK_ML_DetectedException_Probability CHECK (ConfirmedExceptionProbability IS NULL OR DetectionRoute = N'CONFIRMED_PATTERN'),
    CONSTRAINT CK_ML_DetectedException_Evidence CHECK (EvidenceCount >= 1),
    CONSTRAINT CK_ML_DetectedException_Exposure CHECK (PotentialExposureAmount IS NULL OR ExposureCalculationMethod IS NOT NULL)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ML_DetectedException_PublicationKey' AND object_id = OBJECT_ID(N'ML.DetectedException'))
CREATE UNIQUE INDEX UX_ML_DetectedException_PublicationKey ON ML.DetectedException (PublicationKey) WHERE PublicationState IN (N'STAGED', N'ACTIVE');
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ML_DetectedException_OneActive' AND object_id = OBJECT_ID(N'ML.DetectedException'))
CREATE UNIQUE INDEX UX_ML_DetectedException_OneActive ON ML.DetectedException (SnapshotKey) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_DetectedException_Queue' AND object_id = OBJECT_ID(N'ML.DetectedException'))
CREATE INDEX IX_ML_DetectedException_Queue ON ML.DetectedException (IsActive, ReviewStatus, CurrentWeek) INCLUDE (AccountManager, PrimaryReasonCode, AlreadyRuleFlagged);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_DetectedException_Run' AND object_id = OBJECT_ID(N'ML.DetectedException'))
CREATE INDEX IX_ML_DetectedException_Run ON ML.DetectedException (RunId, PublicationState);
GO

IF OBJECT_ID(N'ML.ReviewFeedback', N'U') IS NULL
CREATE TABLE ML.ReviewFeedback (
    FeedbackId               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_ReviewFeedback PRIMARY KEY,
    ExceptionId              BIGINT         NULL CONSTRAINT FK_ML_ReviewFeedback_Exception REFERENCES ML.DetectedException (ExceptionId),
    SnapshotKey              NVARCHAR(64)   NOT NULL CONSTRAINT FK_ML_ReviewFeedback_Snapshot REFERENCES ML.TradingSnapshot (SnapshotKey),
    ReviewOutcome            NVARCHAR(30)   NOT NULL,
    OutcomeType              NVARCHAR(40)   NOT NULL,
    ReviewedBy               NVARCHAR(128)  NOT NULL,
    ReviewedAt               DATETIME2(3)   NOT NULL,
    CorrectedBuyAmount       DECIMAL(18,4)  NULL,
    CorrectedSellAmount      DECIMAL(18,4)  NULL,
    ConfirmedFinancialImpact DECIMAL(18,4)  NULL,
    CurrencyCode             NCHAR(3)       NULL,
    Comments                 NVARCHAR(2000) NULL,
    OutcomeVersion           INT            NOT NULL,
    SupersedesFeedbackId     BIGINT         NULL CONSTRAINT FK_ML_ReviewFeedback_Supersedes REFERENCES ML.ReviewFeedback (FeedbackId),
    IsCurrent                BIT            NOT NULL CONSTRAINT DF_ML_ReviewFeedback_IsCurrent DEFAULT (1),
    SamplingStratum          NVARCHAR(40)   NULL,   -- set when the record came from a review sample (bias analysis)
    SamplingProbability      DECIMAL(9,6)   NULL,
    RecordedAt               DATETIME2(3)   NOT NULL CONSTRAINT DF_ML_ReviewFeedback_RecordedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_ML_ReviewFeedback_Outcome CHECK (ReviewOutcome IN (N'CONFIRMED_EXCEPTION', N'CONFIRMED_VALID', N'INSUFFICIENT_INFORMATION')),
    CONSTRAINT CK_ML_ReviewFeedback_Type CHECK (OutcomeType IN (N'IncorrectBuy', N'IncorrectSell', N'IncorrectDimensions', N'DuplicateCharge',
        N'MissingCharge', N'ValidRateChange', N'ValidTransaction', N'InsufficientInformation')),
    CONSTRAINT CK_ML_ReviewFeedback_Consistent CHECK (
        (ReviewOutcome = N'CONFIRMED_EXCEPTION' AND OutcomeType IN (N'IncorrectBuy', N'IncorrectSell', N'IncorrectDimensions', N'DuplicateCharge', N'MissingCharge'))
     OR (ReviewOutcome = N'CONFIRMED_VALID' AND OutcomeType IN (N'ValidRateChange', N'ValidTransaction'))
     OR (ReviewOutcome = N'INSUFFICIENT_INFORMATION' AND OutcomeType = N'InsufficientInformation')),
    CONSTRAINT UQ_ML_ReviewFeedback_Version UNIQUE (SnapshotKey, OutcomeVersion)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ML_ReviewFeedback_Current' AND object_id = OBJECT_ID(N'ML.ReviewFeedback'))
CREATE UNIQUE INDEX UX_ML_ReviewFeedback_Current ON ML.ReviewFeedback (SnapshotKey) WHERE IsCurrent = 1;
GO
/* Outcome history is append-only: no deletes; the only permitted update is retiring the current flag. */
IF OBJECT_ID(N'ML.trg_ReviewFeedback_AppendOnly', N'TR') IS NULL
EXEC (N'CREATE TRIGGER ML.trg_ReviewFeedback_AppendOnly ON ML.ReviewFeedback AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        RAISERROR (N''ML.ReviewFeedback is append-only; record a new OutcomeVersion instead of deleting.'', 16, 1);
        ROLLBACK TRANSACTION; RETURN;
    END
    IF UPDATE(ReviewOutcome) OR UPDATE(OutcomeType) OR UPDATE(ReviewedBy) OR UPDATE(ReviewedAt) OR UPDATE(CorrectedBuyAmount)
       OR UPDATE(CorrectedSellAmount) OR UPDATE(ConfirmedFinancialImpact) OR UPDATE(Comments) OR UPDATE(OutcomeVersion) OR UPDATE(SnapshotKey)
       OR EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.FeedbackId = i.FeedbackId WHERE d.IsCurrent = 0 AND i.IsCurrent = 1)
    BEGIN
        RAISERROR (N''ML.ReviewFeedback outcomes cannot be overwritten; record a new OutcomeVersion.'', 16, 1);
        ROLLBACK TRANSACTION;
    END
END');
GO
