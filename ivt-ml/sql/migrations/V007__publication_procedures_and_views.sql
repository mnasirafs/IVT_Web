/* V007: atomic activation / rollback of staged publications, and read-only integration views. */
IF OBJECT_ID(N'ML.usp_ActivateRun', N'P') IS NULL
EXEC (N'CREATE PROCEDURE ML.usp_ActivateRun AS RETURN 0;');
GO
/* Activates every STAGED exception of one run in a single transaction. Readers see either the previous
   active set or the new one, never a partial set. Safe to call twice (second call finds nothing staged). */
ALTER PROCEDURE ML.usp_ActivateRun @RunId NVARCHAR(60)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @lock INT, @status NVARCHAR(20);
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC @lock = sp_getapplock @Resource = N'ML.DetectedException.Publish', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 60000;
        IF @lock < 0 RAISERROR (N'Could not acquire the ML publication lock.', 16, 1);

        SELECT @status = Status FROM ML.Run WITH (UPDLOCK, HOLDLOCK) WHERE RunId = @RunId;
        IF @status IS NULL RAISERROR (N'Unknown RunId.', 16, 1);
        IF @status NOT IN (N'SUCCEEDED', N'PUBLISHED') RAISERROR (N'Only SUCCEEDED runs can be published (status %s).', 16, 1, @status);

        UPDATE prior
           SET IsActive = 0, PublicationState = N'SUPERSEDED', SupersededByExceptionId = staged.ExceptionId
          FROM ML.DetectedException prior
          JOIN ML.DetectedException staged ON staged.SnapshotKey = prior.SnapshotKey AND staged.RunId = @RunId AND staged.PublicationState = N'STAGED'
         WHERE prior.IsActive = 1;

        UPDATE ML.DetectedException
           SET PublicationState = N'ACTIVE', IsActive = 1, PublishedAt = SYSUTCDATETIME()
         WHERE RunId = @RunId AND PublicationState = N'STAGED';

        UPDATE ML.Run SET Status = N'PUBLISHED', CompletedAt = SYSUTCDATETIME() WHERE RunId = @RunId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

IF OBJECT_ID(N'ML.usp_DiscardStagedRun', N'P') IS NULL
EXEC (N'CREATE PROCEDURE ML.usp_DiscardStagedRun AS RETURN 0;');
GO
/* Marks a failed run and discards anything it staged; active results from earlier runs are untouched. */
ALTER PROCEDURE ML.usp_DiscardStagedRun @RunId NVARCHAR(60), @ErrorText NVARCHAR(4000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    UPDATE ML.DetectedException SET PublicationState = N'DISCARDED' WHERE RunId = @RunId AND PublicationState = N'STAGED';
    UPDATE ML.Run SET Status = N'FAILED', ErrorText = LEFT(ISNULL(@ErrorText, ErrorText), 4000), CompletedAt = SYSUTCDATETIME()
     WHERE RunId = @RunId AND Status IN (N'STARTED', N'SUCCEEDED');
    COMMIT TRANSACTION;
END
GO

IF OBJECT_ID(N'ML.usp_RollbackPublication', N'P') IS NULL
EXEC (N'CREATE PROCEDURE ML.usp_RollbackPublication AS RETURN 0;');
GO
/* Withdraws a published run and re-activates the results it superseded (if they were not superseded since). */
ALTER PROCEDURE ML.usp_RollbackPublication @RunId NVARCHAR(60), @Reason NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @lock INT;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC @lock = sp_getapplock @Resource = N'ML.DetectedException.Publish', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 60000;
        IF @lock < 0 RAISERROR (N'Could not acquire the ML publication lock.', 16, 1);

        DECLARE @withdrawn TABLE (ExceptionId BIGINT PRIMARY KEY);
        UPDATE ML.DetectedException SET IsActive = 0, PublicationState = N'DISCARDED'
        OUTPUT inserted.ExceptionId INTO @withdrawn
         WHERE RunId = @RunId AND PublicationState = N'ACTIVE';

        UPDATE prior SET IsActive = 1, PublicationState = N'ACTIVE', SupersededByExceptionId = NULL
          FROM ML.DetectedException prior JOIN @withdrawn w ON w.ExceptionId = prior.SupersededByExceptionId
         WHERE prior.PublicationState = N'SUPERSEDED'
           AND NOT EXISTS (SELECT 1 FROM ML.DetectedException a WHERE a.SnapshotKey = prior.SnapshotKey AND a.IsActive = 1);

        UPDATE ML.Run SET Status = N'ABANDONED', ErrorText = LEFT(N'Rolled back: ' + @Reason, 4000), CompletedAt = SYSUTCDATETIME() WHERE RunId = @RunId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

IF OBJECT_ID(N'ML.vActiveDetectedException', N'V') IS NULL
EXEC (N'CREATE VIEW ML.vActiveDetectedException AS SELECT 1 AS Placeholder;');
GO
ALTER VIEW ML.vActiveDetectedException
AS
SELECT d.* FROM ML.DetectedException d WHERE d.IsActive = 1 AND d.IsSynthetic = 0;
GO

/* Account-manager task list: excludes items already flagged by an IVT rule (they are investigated through the
   existing workflow), so ML never creates a second task for the same consignment. */
IF OBJECT_ID(N'ML.vReviewQueue', N'V') IS NULL
EXEC (N'CREATE VIEW ML.vReviewQueue AS SELECT 1 AS Placeholder;');
GO
ALTER VIEW ML.vReviewQueue
AS
SELECT d.ExceptionId, d.CurrentWeek, d.AccountManager, d.Customer, d.CustomerGroup, d.Carrier, d.JobNumber, d.ConsignmentNote,
       d.PrimaryReasonCode, d.ReasonSummary, d.ExplanationText, d.RecommendedAction, d.ConfirmedExceptionProbability,
       d.AEAnomalyPercentile, d.CurrencyCode, d.BuyNett, d.SellNett, d.MarginAmount, d.ReviewStatus, d.PublishedAt
  FROM ML.DetectedException d
 WHERE d.IsActive = 1 AND d.IsSynthetic = 0 AND d.AlreadyRuleFlagged = 0 AND d.ReviewStatus IN (N'PENDING', N'IN_REVIEW');
GO

/* Read-only export linking ML evidence to the existing IVT rule result for rule-flagged consignments.
   It does not add or change any IVT workflow field. */
IF OBJECT_ID(N'ML.vRuleOverlapExport', N'V') IS NULL
EXEC (N'CREATE VIEW ML.vRuleOverlapExport AS SELECT 1 AS Placeholder;');
GO
ALTER VIEW ML.vRuleOverlapExport
AS
SELECT d.ExceptionId, d.CurrentWeek, d.JobNumber, d.ConsignmentNote, d.Customer, d.Carrier, d.RuleCodes,
       d.ExistingRuleResultId, r.ErrCode AS ExistingRuleCode, r.ShortErrDesc AS ExistingRuleName, r.HeldBy AS ExistingHeldBy,
       d.PrimaryReasonCode AS MLReasonCode, d.ExplanationText AS MLExplanation, d.EvidenceJson AS MLEvidenceJson
  FROM ML.DetectedException d
  LEFT JOIN tmp.CurrentIVTResult r ON r.Id = d.ExistingRuleResultId
 WHERE d.IsActive = 1 AND d.IsSynthetic = 0 AND d.AlreadyRuleFlagged = 1;
GO
