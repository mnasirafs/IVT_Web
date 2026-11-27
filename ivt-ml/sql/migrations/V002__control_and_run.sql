/* V002: configuration and run control. */
IF OBJECT_ID(N'ML.ControlParameter', N'U') IS NULL
CREATE TABLE ML.ControlParameter (
    ControlParameterId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_ControlParameter PRIMARY KEY,
    ConfigVersion   NVARCHAR(50)   NOT NULL,
    ScopeType       NVARCHAR(30)   NOT NULL,
    ScopeKey        NVARCHAR(200)  NOT NULL CONSTRAINT DF_ML_ControlParameter_ScopeKey DEFAULT (N''),
    ParameterName   NVARCHAR(100)  NOT NULL,
    NumericValue    DECIMAL(19,6)  NULL,
    TextValue       NVARCHAR(400)  NULL,
    EffectiveFrom   DATE           NOT NULL,
    EffectiveTo     DATE           NULL,
    IsEnabled       BIT            NOT NULL CONSTRAINT DF_ML_ControlParameter_IsEnabled DEFAULT (1),
    ApprovedBy      NVARCHAR(128)  NULL,
    ApprovedAt      DATETIME2(3)   NULL,
    Notes           NVARCHAR(1000) NULL,
    CreatedAt       DATETIME2(3)   NOT NULL CONSTRAINT DF_ML_ControlParameter_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_ML_ControlParameter_Scope CHECK (ScopeType IN (N'GLOBAL', N'CURRENCY', N'CARRIER_GROUP', N'CUSTOMER_CARRIER')),
    CONSTRAINT CK_ML_ControlParameter_Value CHECK (NumericValue IS NOT NULL OR TextValue IS NOT NULL),
    CONSTRAINT CK_ML_ControlParameter_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo > EffectiveFrom)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_ControlParameter_Lookup' AND object_id = OBJECT_ID(N'ML.ControlParameter'))
CREATE INDEX IX_ML_ControlParameter_Lookup ON ML.ControlParameter (ConfigVersion, ParameterName, ScopeType, ScopeKey, EffectiveFrom);
GO
/* Reject overlapping active values for the same version/scope/parameter (the loader also validates). */
IF OBJECT_ID(N'ML.trg_ControlParameter_NoOverlap', N'TR') IS NULL
EXEC (N'CREATE TRIGGER ML.trg_ControlParameter_NoOverlap ON ML.ControlParameter AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN ML.ControlParameter c ON c.ConfigVersion = i.ConfigVersion AND c.ScopeType = i.ScopeType AND c.ScopeKey = i.ScopeKey
             AND c.ParameterName = i.ParameterName AND c.ControlParameterId <> i.ControlParameterId
             AND c.IsEnabled = 1 AND i.IsEnabled = 1
             AND c.EffectiveFrom < ISNULL(i.EffectiveTo, ''99991231'') AND i.EffectiveFrom < ISNULL(c.EffectiveTo, ''99991231''))
    BEGIN
        RAISERROR (N''Overlapping active ML.ControlParameter values for the same ConfigVersion/Scope/Parameter.'', 16, 1);
        ROLLBACK TRANSACTION;
    END
END');
GO

IF OBJECT_ID(N'ML.Run', N'U') IS NULL
CREATE TABLE ML.Run (
    RunId              NVARCHAR(60)   NOT NULL CONSTRAINT PK_ML_Run PRIMARY KEY,
    RunType            NVARCHAR(30)   NOT NULL,
    ScoringWeek        DATE           NULL,
    Status             NVARCHAR(20)   NOT NULL,
    IsDryRun           BIT            NOT NULL CONSTRAINT DF_ML_Run_IsDryRun DEFAULT (0),
    IsShadow           BIT            NOT NULL CONSTRAINT DF_ML_Run_IsShadow DEFAULT (1),
    ConfigVersion      NVARCHAR(50)   NULL,
    ConfigSnapshotJson NVARCHAR(MAX)  NULL,
    ConfigChecksum     NVARCHAR(64)   NULL,
    ModelVersion       NVARCHAR(60)   NULL,
    FeatureVersion     NVARCHAR(20)   NULL,
    KeyVersion         NVARCHAR(20)   NULL,
    SourceWatermarkJson NVARCHAR(MAX) NULL,
    MetricsJson        NVARCHAR(MAX)  NULL,
    ErrorText          NVARCHAR(4000) NULL,
    HostName           NVARCHAR(128)  NULL,
    StartedAt          DATETIME2(3)   NOT NULL CONSTRAINT DF_ML_Run_StartedAt DEFAULT (SYSUTCDATETIME()),
    CompletedAt        DATETIME2(3)   NULL,
    CONSTRAINT CK_ML_Run_Status CHECK (Status IN (N'STARTED', N'SUCCEEDED', N'FAILED', N'PUBLISHED', N'ABANDONED')),
    CONSTRAINT CK_ML_Run_Type CHECK (RunType IN (N'PROFILE', N'SNAPSHOT', N'TRAIN', N'BACKTEST', N'SCORE', N'PUBLISH', N'FEEDBACK', N'MONITOR'))
);
GO
/* Only one in-flight run per type and scoring week: concurrent runs fail on insert. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ML_Run_OneActivePerWeek' AND object_id = OBJECT_ID(N'ML.Run'))
CREATE UNIQUE INDEX UX_ML_Run_OneActivePerWeek ON ML.Run (RunType, ScoringWeek) WHERE Status = N'STARTED';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_Run_Week' AND object_id = OBJECT_ID(N'ML.Run'))
CREATE INDEX IX_ML_Run_Week ON ML.Run (ScoringWeek, RunType, Status);
GO
