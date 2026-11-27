/* V003: immutable canonical snapshots, source lineage and data-quality issues. */
IF OBJECT_ID(N'ML.TradingSnapshot', N'U') IS NULL
CREATE TABLE ML.TradingSnapshot (
    SnapshotKey          NVARCHAR(64)   NOT NULL CONSTRAINT PK_ML_TradingSnapshot PRIMARY KEY,
    TransactionKey       NVARCHAR(64)   NOT NULL,
    KeyVersion           NVARCHAR(20)   NOT NULL,
    RunId                NVARCHAR(60)   NOT NULL CONSTRAINT FK_ML_TradingSnapshot_Run REFERENCES ML.Run (RunId),
    SnapshotWeek         DATE           NOT NULL,
    SnapshotCapturedAt   DATETIME2(3)   NOT NULL,
    SourceSystem         NVARCHAR(40)   NOT NULL,           -- CURRENT_TRADING | BUY_SELL_HISTORY | SYNTHETIC
    Customer             NVARCHAR(50)   NOT NULL,
    CustomerGroup        NVARCHAR(320)  NULL,
    Carrier              NVARCHAR(50)   NOT NULL,
    CarrierGroup         NVARCHAR(50)   NULL,
    FinanceGroup         NVARCHAR(320)  NULL,
    JobNumber            NVARCHAR(50)   NOT NULL,           -- normalised (trimmed, upper case, leading zeros kept)
    JobNumberRaw         NVARCHAR(50)   NULL,
    ConsignmentNote      NVARCHAR(50)   NULL,
    ConsignmentNoteRaw   NVARCHAR(50)   NULL,
    SupplierRef          NVARCHAR(50)   NULL,
    WorkCode             NVARCHAR(50)   NULL,
    OptionCode           NVARCHAR(50)   NULL,
    FromLocation         NVARCHAR(50)   NULL,
    ToLocation           NVARCHAR(50)   NULL,
    PickupPostcode       NVARCHAR(20)   NULL,
    DeliveryPostcode     NVARCHAR(20)   NULL,
    ConsignmentDate      DATE           NULL,
    CurrencyCode         NCHAR(3)       NULL,
    CurrencyStatus       NVARCHAR(20)   NOT NULL,           -- MAPPED | UNKNOWN
    Segment              NVARCHAR(20)   NOT NULL,           -- STANDARD | CREDIT_REVERSAL | ZERO_VALUE
    SellFreight          DECIMAL(18,4)  NULL,
    SellOther            DECIMAL(18,4)  NULL,
    SellFuel             DECIMAL(18,4)  NULL,
    SellNett             DECIMAL(18,4)  NULL,
    BuyFreight           DECIMAL(18,4)  NULL,
    BuyOther             DECIMAL(18,4)  NULL,
    BuyFuel              DECIMAL(18,4)  NULL,
    BuyNett              DECIMAL(18,4)  NULL,
    SellLineCount        INT            NOT NULL,
    BuyLineCount         INT            NOT NULL,
    AncillaryChargeCount INT            NULL,
    Weight               DECIMAL(18,4)  NULL,
    ChargeableWeight     DECIMAL(18,4)  NULL,
    Cubic                DECIMAL(18,6)  NULL,
    Pallet               DECIMAL(18,4)  NULL,
    Quantity             DECIMAL(18,4)  NULL,
    Items                DECIMAL(18,4)  NULL,
    Hours                DECIMAL(18,4)  NULL,
    Lift                 DECIMAL(18,4)  NULL,
    Space                DECIMAL(18,4)  NULL,
    BuyWeight            DECIMAL(18,4)  NULL,
    BuyChargeableWeight  DECIMAL(18,4)  NULL,
    BuyCubic             DECIMAL(18,6)  NULL,
    MeasurementConflict  BIT            NOT NULL,
    MoveitMatchStatus    NVARCHAR(20)   NOT NULL,
    MoveitWeightKg       DECIMAL(18,5)  NULL,
    MoveitCubic          DECIMAL(18,6)  NULL,
    MoveitPickupPostcode NVARCHAR(20)   NULL,
    MoveitDeliveryPostcode NVARCHAR(20) NULL,
    MoveitDespatchDate   DATE           NULL,
    BuySellMatchStatus   NVARCHAR(20)   NOT NULL,
    InvalidDateFlag      BIT            NOT NULL,
    AccountManager       NVARCHAR(2000) NULL,               -- routing only; never a model input
    LoginName            NVARCHAR(500)  NULL,               -- routing only
    IsSynthetic          BIT            NOT NULL CONSTRAINT DF_ML_TradingSnapshot_IsSynthetic DEFAULT (0),
    CONSTRAINT CK_ML_TradingSnapshot_Match CHECK (BuySellMatchStatus IN (N'EXACT', N'MAPPED', N'AMBIGUOUS', N'UNMATCHED', N'NOT_APPLICABLE')),
    CONSTRAINT CK_ML_TradingSnapshot_Moveit CHECK (MoveitMatchStatus IN (N'EXACT', N'MAPPED', N'AMBIGUOUS', N'UNMATCHED', N'NOT_APPLICABLE')),
    CONSTRAINT CK_ML_TradingSnapshot_Segment CHECK (Segment IN (N'STANDARD', N'CREDIT_REVERSAL', N'ZERO_VALUE')),
    CONSTRAINT CK_ML_TradingSnapshot_Currency CHECK (CurrencyStatus IN (N'MAPPED', N'UNKNOWN'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_TradingSnapshot_Week' AND object_id = OBJECT_ID(N'ML.TradingSnapshot'))
CREATE INDEX IX_ML_TradingSnapshot_Week ON ML.TradingSnapshot (SnapshotWeek, CarrierGroup, WorkCode) INCLUDE (TransactionKey, Segment, BuySellMatchStatus);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_TradingSnapshot_Transaction' AND object_id = OBJECT_ID(N'ML.TradingSnapshot'))
CREATE INDEX IX_ML_TradingSnapshot_Transaction ON ML.TradingSnapshot (TransactionKey, SnapshotWeek);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_TradingSnapshot_Job' AND object_id = OBJECT_ID(N'ML.TradingSnapshot'))
CREATE INDEX IX_ML_TradingSnapshot_Job ON ML.TradingSnapshot (JobNumber, SnapshotWeek);
GO

IF OBJECT_ID(N'ML.SourceRowMap', N'U') IS NULL
CREATE TABLE ML.SourceRowMap (
    SourceRowMapId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_SourceRowMap PRIMARY KEY,
    SnapshotKey      NVARCHAR(64)  NOT NULL CONSTRAINT FK_ML_SourceRowMap_Snapshot REFERENCES ML.TradingSnapshot (SnapshotKey),
    SourceTable      NVARCHAR(128) NOT NULL,
    SourceRowId      NVARCHAR(100) NULL,      -- PrimaryKeyField where the source has one (not assumed stable across refreshes)
    SourceRowHash    NVARCHAR(64)  NOT NULL,  -- hash of the raw source row, for repeated-snapshot detection
    Side             NVARCHAR(10)  NOT NULL,
    LineAmountNett   DECIMAL(18,4) NULL,
    MatchStatus      NVARCHAR(20)  NOT NULL,
    CONSTRAINT CK_ML_SourceRowMap_Side CHECK (Side IN (N'SELL', N'BUY', N'MOVEIT', N'TRADING'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_SourceRowMap_Snapshot' AND object_id = OBJECT_ID(N'ML.SourceRowMap'))
CREATE INDEX IX_ML_SourceRowMap_Snapshot ON ML.SourceRowMap (SnapshotKey, Side);
GO

IF OBJECT_ID(N'ML.DataQualityIssue', N'U') IS NULL
CREATE TABLE ML.DataQualityIssue (
    DataQualityIssueId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_DataQualityIssue PRIMARY KEY,
    RunId          NVARCHAR(60)   NOT NULL CONSTRAINT FK_ML_DataQualityIssue_Run REFERENCES ML.Run (RunId),
    SnapshotKey    NVARCHAR(64)   NULL,
    SourceTable    NVARCHAR(128)  NULL,
    IssueCode      NVARCHAR(50)   NOT NULL,
    Severity       NVARCHAR(10)   NOT NULL,
    Detail         NVARCHAR(1000) NULL,
    AffectedRows   INT            NULL,
    CreatedAt      DATETIME2(3)   NOT NULL CONSTRAINT DF_ML_DataQualityIssue_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_ML_DataQualityIssue_Severity CHECK (Severity IN (N'INFO', N'WARN', N'ERROR', N'BLOCKER'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_DataQualityIssue_Run' AND object_id = OBJECT_ID(N'ML.DataQualityIssue'))
CREATE INDEX IX_ML_DataQualityIssue_Run ON ML.DataQualityIssue (RunId, IssueCode);
GO

/* Reference: currency per carrier group / customer. CurrentTradingData has no currency column, so
   amounts are UNKNOWN currency until the business confirms this mapping. */
IF OBJECT_ID(N'ML.CurrencyMap', N'U') IS NULL
CREATE TABLE ML.CurrencyMap (
    CurrencyMapId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_CurrencyMap PRIMARY KEY,
    ScopeType      NVARCHAR(20)  NOT NULL,      -- CARRIER | CARRIER_GROUP | CUSTOMER
    ScopeKey       NVARCHAR(320) NOT NULL,
    CurrencyCode   NCHAR(3)      NOT NULL,
    ApprovedBy     NVARCHAR(128) NULL,
    ApprovedAt     DATETIME2(3)  NULL,
    CONSTRAINT CK_ML_CurrencyMap_Scope CHECK (ScopeType IN (N'CARRIER', N'CARRIER_GROUP', N'CUSTOMER')),
    CONSTRAINT CK_ML_CurrencyMap_Code CHECK (CurrencyCode IN (N'AUD', N'NZD')),
    CONSTRAINT UQ_ML_CurrencyMap UNIQUE (ScopeType, ScopeKey)
);
GO
