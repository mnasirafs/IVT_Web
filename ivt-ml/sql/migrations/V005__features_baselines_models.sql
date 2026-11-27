/* V005: feature snapshots, point-in-time peer baselines, model registry. */
IF OBJECT_ID(N'ML.FeatureSnapshot', N'U') IS NULL
CREATE TABLE ML.FeatureSnapshot (
    SnapshotKey      NVARCHAR(64)  NOT NULL CONSTRAINT FK_ML_FeatureSnapshot_Snapshot REFERENCES ML.TradingSnapshot (SnapshotKey),
    FeatureVersion   NVARCHAR(20)  NOT NULL,
    BaselineVersion  NVARCHAR(40)  NOT NULL,
    RunId            NVARCHAR(60)  NOT NULL CONSTRAINT FK_ML_FeatureSnapshot_Run REFERENCES ML.Run (RunId),
    FeatureJson      NVARCHAR(MAX) NOT NULL,
    FeatureChecksum  NVARCHAR(64)  NOT NULL,
    CreatedAt        DATETIME2(3)  NOT NULL CONSTRAINT DF_ML_FeatureSnapshot_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ML_FeatureSnapshot PRIMARY KEY (SnapshotKey, FeatureVersion, BaselineVersion)
);
GO

IF OBJECT_ID(N'ML.PeerBaseline', N'U') IS NULL
CREATE TABLE ML.PeerBaseline (
    PeerBaselineId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_PeerBaseline PRIMARY KEY,
    BaselineVersion  NVARCHAR(40)  NOT NULL,
    BaselineCutoff   DATE          NOT NULL,   -- observations strictly before this week only
    WindowStart      DATE          NOT NULL,
    WindowEnd        DATE          NOT NULL,
    FallbackLevel    TINYINT       NOT NULL,
    PeerGroupKey     NVARCHAR(400) NOT NULL,
    MetricName       NVARCHAR(50)  NOT NULL,
    ObservationCount INT           NOT NULL,
    Median           DECIMAL(19,6) NULL,
    MAD              DECIMAL(19,6) NULL,
    P10              DECIMAL(19,6) NULL,
    P90              DECIMAL(19,6) NULL,
    RunId            NVARCHAR(60)  NOT NULL CONSTRAINT FK_ML_PeerBaseline_Run REFERENCES ML.Run (RunId),
    CONSTRAINT CK_ML_PeerBaseline_Window CHECK (WindowEnd < BaselineCutoff AND WindowStart <= WindowEnd),
    CONSTRAINT UQ_ML_PeerBaseline UNIQUE (BaselineVersion, BaselineCutoff, FallbackLevel, PeerGroupKey, MetricName)
);
GO

IF OBJECT_ID(N'ML.ModelRegistry', N'U') IS NULL
CREATE TABLE ML.ModelRegistry (
    ModelVersion         NVARCHAR(60)   NOT NULL CONSTRAINT PK_ML_ModelRegistry PRIMARY KEY,
    ModelKind            NVARCHAR(40)   NOT NULL,   -- AUTOENCODER | ISOLATION_FOREST | CLASSIFIER_CONFIRMED | CLASSIFIER_WEAK_EXPERIMENTAL
    Status               NVARCHAR(20)   NOT NULL,   -- CANDIDATE | APPROVED | RETIRED | REJECTED
    FeatureVersion       NVARCHAR(20)   NOT NULL,
    PreprocessingVersion NVARCHAR(40)   NOT NULL,
    TrainingWindowStart  DATE           NULL,
    TrainingWindowEnd    DATE           NULL,
    LabelSetVersion      NVARCHAR(40)   NULL,
    ArtifactUri          NVARCHAR(1000) NOT NULL,   -- durable file location; binaries are never stored in SQL
    ArtifactSha256       NVARCHAR(64)   NOT NULL,
    DependenciesJson     NVARCHAR(MAX)  NOT NULL,
    MetricsJson          NVARCHAR(MAX)  NULL,
    LimitationsText      NVARCHAR(MAX)  NULL,
    ProvidesCalibratedProbability BIT   NOT NULL CONSTRAINT DF_ML_ModelRegistry_Calibrated DEFAULT (0),
    IsSyntheticOnly      BIT            NOT NULL CONSTRAINT DF_ML_ModelRegistry_Synthetic DEFAULT (0),
    ParentModelVersion   NVARCHAR(60)   NULL,
    RegisteredAt         DATETIME2(3)   NOT NULL CONSTRAINT DF_ML_ModelRegistry_RegisteredAt DEFAULT (SYSUTCDATETIME()),
    ApprovedBy           NVARCHAR(128)  NULL,
    ApprovedAt           DATETIME2(3)   NULL,
    CONSTRAINT CK_ML_ModelRegistry_Kind CHECK (ModelKind IN (N'AUTOENCODER', N'ISOLATION_FOREST', N'PEER_BASELINE', N'CLASSIFIER_CONFIRMED', N'CLASSIFIER_WEAK_EXPERIMENTAL')),
    CONSTRAINT CK_ML_ModelRegistry_Status CHECK (Status IN (N'CANDIDATE', N'APPROVED', N'RETIRED', N'REJECTED')),
    /* Only confirmed-label classifiers may claim calibrated confirmed-exception probability. */
    CONSTRAINT CK_ML_ModelRegistry_ProbabilityClaim CHECK (ProvidesCalibratedProbability = 0 OR ModelKind = N'CLASSIFIER_CONFIRMED'),
    CONSTRAINT CK_ML_ModelRegistry_Approval CHECK (Status <> N'APPROVED' OR (ApprovedBy IS NOT NULL AND ApprovedAt IS NOT NULL AND IsSyntheticOnly = 0))
);
GO
