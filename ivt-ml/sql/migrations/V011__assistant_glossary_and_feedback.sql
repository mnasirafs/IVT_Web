/* V011: IVT assistant knowledge loop.
   ML.ChatGlossary  - business synonyms, name->code mappings and guidance injected into every assistant request.
                      Account managers suggest terms (Scope USER, Status PENDING: applied to their own chats immediately);
                      admins approve them (Scope GLOBAL, Status APPROVED: applied for everyone).
   ML.ChatFeedback  - thumbs up/down and comments on assistant answers, reviewed by admins to improve the glossary/tools. */
IF OBJECT_ID(N'ML.ChatGlossary', N'U') IS NULL
CREATE TABLE ML.ChatGlossary (
    GlossaryId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_ChatGlossary PRIMARY KEY,
    Term         NVARCHAR(100)  NOT NULL,
    Meaning      NVARCHAR(500)  NOT NULL,
    Kind         NVARCHAR(20)   NOT NULL,   -- SYNONYM | CODE | DEFINITION | GUIDANCE
    Scope        NVARCHAR(10)   NOT NULL,   -- USER | GLOBAL
    OwnerLogin   NVARCHAR(128)  NULL,       -- for USER scope
    Status       NVARCHAR(10)   NOT NULL,   -- PENDING | APPROVED | REJECTED
    CreatedBy    NVARCHAR(128)  NOT NULL,
    CreatedAt    DATETIME2(3)   NOT NULL CONSTRAINT DF_ML_ChatGlossary_CreatedAt DEFAULT (SYSUTCDATETIME()),
    ReviewedBy   NVARCHAR(128)  NULL,
    ReviewedAt   DATETIME2(3)   NULL,
    SourceFeedbackId BIGINT     NULL,
    CONSTRAINT CK_ML_ChatGlossary_Kind CHECK (Kind IN (N'SYNONYM', N'CODE', N'DEFINITION', N'GUIDANCE')),
    CONSTRAINT CK_ML_ChatGlossary_Scope CHECK (Scope IN (N'USER', N'GLOBAL') AND (Scope = N'GLOBAL' OR OwnerLogin IS NOT NULL)),
    CONSTRAINT CK_ML_ChatGlossary_Status CHECK (Status IN (N'PENDING', N'APPROVED', N'REJECTED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_ChatGlossary_Active' AND object_id = OBJECT_ID(N'ML.ChatGlossary'))
CREATE INDEX IX_ML_ChatGlossary_Active ON ML.ChatGlossary (Status, Scope, OwnerLogin) INCLUDE (Term, Meaning, Kind);
GO

IF OBJECT_ID(N'ML.ChatFeedback', N'U') IS NULL
CREATE TABLE ML.ChatFeedback (
    FeedbackId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ML_ChatFeedback PRIMARY KEY,
    LoginName    NVARCHAR(128)  NOT NULL,
    ScopeLabel   NVARCHAR(200)  NULL,
    Question     NVARCHAR(2000) NOT NULL,
    Answer       NVARCHAR(MAX)  NOT NULL,
    ToolsJson    NVARCHAR(MAX)  NULL,      -- which tools/arguments produced the answer (no result data)
    Rating       SMALLINT       NOT NULL,  -- 1 helpful, -1 not helpful
    Comment      NVARCHAR(2000) NULL,
    Status       NVARCHAR(12)   NOT NULL CONSTRAINT DF_ML_ChatFeedback_Status DEFAULT (N'NEW'),
    AdminNote    NVARCHAR(1000) NULL,
    ReviewedBy   NVARCHAR(128)  NULL,
    ReviewedAt   DATETIME2(3)   NULL,
    CreatedAt    DATETIME2(3)   NOT NULL CONSTRAINT DF_ML_ChatFeedback_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_ML_ChatFeedback_Rating CHECK (Rating IN (-1, 1)),
    CONSTRAINT CK_ML_ChatFeedback_Status CHECK (Status IN (N'NEW', N'REVIEWED', N'ACTIONED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_ChatFeedback_Review' AND object_id = OBJECT_ID(N'ML.ChatFeedback'))
CREATE INDEX IX_ML_ChatFeedback_Review ON ML.ChatFeedback (Status, Rating, CreatedAt);
GO

/* Seed glossary from tester feedback (2026-10-09); admins can edit or remove these on Admin Controls -> Assistant knowledge. */
IF NOT EXISTS (SELECT 1 FROM ML.ChatGlossary WHERE Term = N'revenue' AND Scope = N'GLOBAL')
INSERT INTO ML.ChatGlossary (Term, Meaning, Kind, Scope, Status, CreatedBy, ReviewedBy, ReviewedAt) VALUES
 (N'revenue', N'Sell value charged to the customer = sum of sellTotalNett. Also called sales, turnover, trading value, sell $.', N'SYNONYM', N'GLOBAL', N'APPROVED', N'system', N'mnasir', SYSUTCDATETIME()),
 (N'trading value', N'Same as revenue: sum of sellTotalNett (not trading volume, which is a count of connotes).', N'SYNONYM', N'GLOBAL', N'APPROVED', N'system', N'mnasir', SYSUTCDATETIME()),
 (N'cost', N'Carrier (buy) charge = sum of buyTotalNett. Also called buy, buy $, carrier cost.', N'SYNONYM', N'GLOBAL', N'APPROVED', N'system', N'mnasir', SYSUTCDATETIME()),
 (N'margin', N'Gross margin = sell - buy (sellTotalNett - buyTotalNett); GP% = margin / sell. Negative gross margin = buy higher than sell.', N'DEFINITION', N'GLOBAL', N'APPROVED', N'system', N'mnasir', SYSUTCDATETIME()),
 (N'sender postcode', N'Pickup postcode = pickupPostcode (col_post). The from/to fields are ZONE codes, not postcodes.', N'SYNONYM', N'GLOBAL', N'APPROVED', N'system', N'mnasir', SYSUTCDATETIME()),
 (N'receiver postcode', N'Delivery postcode = deliveryPostcode (del_post). The from/to fields are ZONE codes, not postcodes.', N'SYNONYM', N'GLOBAL', N'APPROVED', N'system', N'mnasir', SYSUTCDATETIME()),
 (N'BlueStar', N'Carrier group BLUESTAR (several carrier codes, e.g. BLU, BLUE, BSTARGL). Use LookupCodes and the carrierGroup / carrierOrGroup fields.', N'CODE', N'GLOBAL', N'APPROVED', N'system', N'mnasir', SYSUTCDATETIME());
GO
