/* V001: ML schema + migration ledger. SQL Server 2012+ compatible, idempotent. Review before applying. */
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'ML')
    EXEC (N'CREATE SCHEMA [ML] AUTHORIZATION [dbo]');
GO

IF OBJECT_ID(N'ML.SchemaVersion', N'U') IS NULL
CREATE TABLE ML.SchemaVersion (
    Version       NVARCHAR(20)  NOT NULL CONSTRAINT PK_ML_SchemaVersion PRIMARY KEY,
    ScriptName    NVARCHAR(200) NOT NULL,
    Checksum      NVARCHAR(64)  NOT NULL,
    AppliedAt     DATETIME2(3)  NOT NULL CONSTRAINT DF_ML_SchemaVersion_AppliedAt DEFAULT (SYSUTCDATETIME()),
    AppliedBy     NVARCHAR(128) NOT NULL CONSTRAINT DF_ML_SchemaVersion_AppliedBy DEFAULT (SUSER_SNAME())
);
GO
