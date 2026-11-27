/* V012: audit storage for the IVT Administration module (web).
   New schema and table only; no existing IVT table or procedure is changed.
   Application locks (sp_getapplock) used for run coordination need no table. */
IF SCHEMA_ID(N'IvtAdmin') IS NULL EXEC (N'CREATE SCHEMA IvtAdmin AUTHORIZATION dbo');
GO
IF OBJECT_ID(N'IvtAdmin.AuditLog', N'U') IS NULL
CREATE TABLE IvtAdmin.AuditLog (
    AuditId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IvtAdmin_AuditLog PRIMARY KEY,
    CorrelationId  UNIQUEIDENTIFIER NOT NULL,
    OccurredAtUtc  DATETIME2(3)   NOT NULL CONSTRAINT DF_IvtAdmin_AuditLog_At DEFAULT (SYSUTCDATETIME()),
    CompletedAtUtc DATETIME2(3)   NULL,
    Operator       NVARCHAR(128)  NOT NULL,
    Environment    NVARCHAR(20)   NULL,      -- production / test / azure
    Operation      NVARCHAR(80)   NOT NULL,  -- e.g. RUN.PRODUCTION, SETTINGS.RULES.UPDATE, CONNOTE.HOLD
    Target         NVARCHAR(400)  NULL,      -- record ids, connotes, file name
    AffectedCount  INT            NULL,
    InvoiceWeek    DATE           NULL,
    ExternalId     NVARCHAR(200)  NULL,      -- SQL Agent job name, ADF run id
    Outcome        NVARCHAR(20)   NOT NULL,
    Detail         NVARCHAR(MAX)  NULL,      -- JSON: stages, field changes, per-row results (no secrets)
    CONSTRAINT CK_IvtAdmin_AuditLog_Outcome CHECK (Outcome IN
        (N'STARTED', N'SUCCEEDED', N'FAILED', N'PARTIAL', N'SUBMITTED', N'AMBIGUOUS', N'REJECTED', N'SAVED_NOTIFY_FAILED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IvtAdmin_AuditLog_Operation' AND object_id = OBJECT_ID(N'IvtAdmin.AuditLog'))
CREATE INDEX IX_IvtAdmin_AuditLog_Operation ON IvtAdmin.AuditLog (Operation, AuditId DESC) INCLUDE (Outcome, ExternalId, OccurredAtUtc);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IvtAdmin_AuditLog_External' AND object_id = OBJECT_ID(N'IvtAdmin.AuditLog'))
CREATE INDEX IX_IvtAdmin_AuditLog_External ON IvtAdmin.AuditLog (ExternalId, OccurredAtUtc DESC) INCLUDE (Outcome);
GO
