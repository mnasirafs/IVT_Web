/* V010: ML.DetectedException.ReviewStatus was NVARCHAR(20) but must hold 'ROUTED_TO_RULE_WORKFLOW' (23 chars).
   The dependent index, CHECK and DEFAULT constraints are dropped and recreated around the ALTER. */
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'ML.DetectedException') AND name = N'ReviewStatus' AND max_length < 60)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ML_DetectedException_Queue' AND object_id = OBJECT_ID(N'ML.DetectedException'))
        DROP INDEX IX_ML_DetectedException_Queue ON ML.DetectedException;
    ALTER TABLE ML.DetectedException DROP CONSTRAINT CK_ML_DetectedException_Review;
    ALTER TABLE ML.DetectedException DROP CONSTRAINT DF_ML_DetectedException_ReviewStatus;
    ALTER TABLE ML.DetectedException ALTER COLUMN ReviewStatus NVARCHAR(30) NOT NULL;
    ALTER TABLE ML.DetectedException ADD CONSTRAINT DF_ML_DetectedException_ReviewStatus DEFAULT (N'PENDING') FOR ReviewStatus;
    ALTER TABLE ML.DetectedException ADD CONSTRAINT CK_ML_DetectedException_Review CHECK (ReviewStatus IN (N'PENDING', N'IN_REVIEW', N'CONFIRMED_EXCEPTION',
        N'CONFIRMED_VALID', N'INSUFFICIENT_INFORMATION', N'ROUTED_TO_RULE_WORKFLOW'));
    CREATE INDEX IX_ML_DetectedException_Queue ON ML.DetectedException (IsActive, ReviewStatus, CurrentWeek) INCLUDE (AccountManager, PrimaryReasonCode, AlreadyRuleFlagged);
END
GO
