/* V013: runtime feature switches that IVT admins change from Admin Controls (no redeploy / IIS restart needed).
   ML.AppSetting - one row per switch. ASSISTANT_ENABLED = 'true' | 'false' turns Vera (IVT AI Assistant) on or off for everyone.
   A missing table or row means "on", so Vera still depends only on its Azure OpenAI configuration until an admin turns it off. */
IF OBJECT_ID(N'ML.AppSetting', N'U') IS NULL
CREATE TABLE ML.AppSetting (
    SettingKey   NVARCHAR(100)  NOT NULL CONSTRAINT PK_ML_AppSetting PRIMARY KEY,
    SettingValue NVARCHAR(500)  NOT NULL,
    UpdatedBy    NVARCHAR(128)  NOT NULL,
    UpdatedAt    DATETIME2(3)   NOT NULL CONSTRAINT DF_ML_AppSetting_UpdatedAt DEFAULT (SYSUTCDATETIME())
);
GO
IF NOT EXISTS (SELECT 1 FROM ML.AppSetting WHERE SettingKey = N'ASSISTANT_ENABLED')
INSERT INTO ML.AppSetting (SettingKey, SettingValue, UpdatedBy) VALUES (N'ASSISTANT_ENABLED', N'true', N'migration V013');
GO
