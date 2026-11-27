/* V004: label events (raw, weak and confirmed) and point-in-time training labels. */
IF OBJECT_ID(N'ML.LabelEvent', N'U') IS NULL
CREATE TABLE ML.LabelEvent (
    LabelEventId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_LabelEvent PRIMARY KEY,
    EventSource      NVARCHAR(40)   NOT NULL,   -- RULE_RESULT | HOLD | RELEASE | ML_REVIEW
    SourceRecordId   NVARCHAR(100)  NOT NULL,
    TransactionKey   NVARCHAR(64)   NULL,       -- NULL when the event could not be matched unambiguously
    SnapshotKey      NVARCHAR(64)   NULL,
    MatchStatus      NVARCHAR(20)   NOT NULL,
    EventType        NVARCHAR(40)   NOT NULL,   -- RULE_FLAG | HOLD_MANUAL | HOLD_RULE | RELEASE | REVIEW_OUTCOME
    ProposedLabel    TINYINT        NULL,       -- 1 / 0 / NULL (no label implied)
    LabelQuality     NVARCHAR(20)   NOT NULL,   -- CONFIRMED | WEAK | EVIDENCE_ONLY
    OutcomeType      NVARCHAR(40)   NULL,
    RuleCode         NVARCHAR(50)   NULL,
    EventTime        DATETIME2(3)   NOT NULL,
    LabelAvailableAt DATETIME2(3)   NOT NULL,
    Reviewer         NVARCHAR(128)  NULL,
    Notes            NVARCHAR(1000) NULL,
    RunId            NVARCHAR(60)   NOT NULL CONSTRAINT FK_ML_LabelEvent_Run REFERENCES ML.Run (RunId),
    CONSTRAINT CK_ML_LabelEvent_Match CHECK (MatchStatus IN (N'EXACT', N'MAPPED', N'AMBIGUOUS', N'UNMATCHED', N'NOT_APPLICABLE')),
    CONSTRAINT CK_ML_LabelEvent_Quality CHECK (LabelQuality IN (N'CONFIRMED', N'WEAK', N'EVIDENCE_ONLY')),
    CONSTRAINT CK_ML_LabelEvent_Label CHECK (ProposedLabel IS NULL OR ProposedLabel IN (0, 1)),
    CONSTRAINT CK_ML_LabelEvent_Outcome CHECK (OutcomeType IS NULL OR OutcomeType IN (N'IncorrectBuy', N'IncorrectSell', N'IncorrectDimensions',
        N'DuplicateCharge', N'MissingCharge', N'ValidRateChange', N'ValidTransaction', N'InsufficientInformation')),
    CONSTRAINT UQ_ML_LabelEvent_Source UNIQUE (EventSource, SourceRecordId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_LabelEvent_Transaction' AND object_id = OBJECT_ID(N'ML.LabelEvent'))
CREATE INDEX IX_ML_LabelEvent_Transaction ON ML.LabelEvent (TransactionKey, LabelAvailableAt) INCLUDE (ProposedLabel, LabelQuality, EventSource);
GO

/* Reviewed mapping from free-text release justifications to outcomes. Starts empty: every pattern
   must be approved by the business. Unmapped text produces no label. */
IF OBJECT_ID(N'ML.OutcomeTextMap', N'U') IS NULL
CREATE TABLE ML.OutcomeTextMap (
    OutcomeTextMapId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_OutcomeTextMap PRIMARY KEY,
    LikePattern      NVARCHAR(200) NOT NULL,
    OutcomeType      NVARCHAR(40)  NOT NULL,
    ProposedLabel    TINYINT       NULL,
    LabelQuality     NVARCHAR(20)  NOT NULL CONSTRAINT DF_ML_OutcomeTextMap_Quality DEFAULT (N'WEAK'),
    Priority         INT           NOT NULL CONSTRAINT DF_ML_OutcomeTextMap_Priority DEFAULT (100),
    ApprovedBy       NVARCHAR(128) NULL,
    ApprovedAt       DATETIME2(3)  NULL,
    CONSTRAINT CK_ML_OutcomeTextMap_Quality CHECK (LabelQuality IN (N'WEAK', N'EVIDENCE_ONLY'))
);
GO

IF OBJECT_ID(N'ML.TrainingLabel', N'U') IS NULL
CREATE TABLE ML.TrainingLabel (
    TrainingLabelId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_TrainingLabel PRIMARY KEY,
    LabelSetVersion  NVARCHAR(40)   NOT NULL,
    TrainingCutoff   DATETIME2(3)   NOT NULL,
    TransactionKey   NVARCHAR(64)   NOT NULL,
    SnapshotKey      NVARCHAR(64)   NOT NULL CONSTRAINT FK_ML_TrainingLabel_Snapshot REFERENCES ML.TradingSnapshot (SnapshotKey),
    Label            TINYINT        NULL,
    LabelSource      NVARCHAR(40)   NOT NULL,
    LabelQuality     NVARCHAR(20)   NOT NULL,
    OutcomeType      NVARCHAR(40)   NULL,
    EventTime        DATETIME2(3)   NOT NULL,
    LabelAvailableAt DATETIME2(3)   NOT NULL,
    Reviewer         NVARCHAR(128)  NULL,
    SourceRecordId   NVARCHAR(100)  NOT NULL,
    MatchStatus      NVARCHAR(20)   NOT NULL,
    ResolutionNotes  NVARCHAR(1000) NULL,
    RunId            NVARCHAR(60)   NOT NULL CONSTRAINT FK_ML_TrainingLabel_Run REFERENCES ML.Run (RunId),
    CONSTRAINT CK_ML_TrainingLabel_Label CHECK (Label IS NULL OR Label IN (0, 1)),
    CONSTRAINT CK_ML_TrainingLabel_Quality CHECK (LabelQuality IN (N'CONFIRMED', N'WEAK')),
    CONSTRAINT CK_ML_TrainingLabel_Available CHECK (LabelAvailableAt <= TrainingCutoff),
    CONSTRAINT UQ_ML_TrainingLabel UNIQUE (LabelSetVersion, TrainingCutoff, SnapshotKey)
);
GO
