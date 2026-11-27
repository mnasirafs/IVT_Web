/* V008: business-approved reference data (approved by mnasir, 2026-10-06) and development configuration defaults. */
-- Currency: all IVT activity is AUD (business confirmation 2026-10-06). New carrier codes need a row here before publication.
INSERT INTO ML.CurrencyMap (ScopeType, ScopeKey, CurrencyCode, ApprovedBy, ApprovedAt)
SELECT N'CARRIER', c.CarrierCode, N'AUD', N'mnasir', SYSUTCDATETIME()
FROM (SELECT LTRIM(RTRIM(CarrierCode)) COLLATE DATABASE_DEFAULT AS CarrierCode FROM AFSXchange.dbo.Carriers WHERE CarrierCode IS NOT NULL AND LTRIM(CarrierCode) <> ''
      UNION SELECT LTRIM(RTRIM(carrier)) COLLATE DATABASE_DEFAULT FROM dbo.Sell_Side WHERE carrier IS NOT NULL AND LTRIM(carrier) <> '') c
WHERE NOT EXISTS (SELECT 1 FROM ML.CurrencyMap m WHERE m.ScopeType = N'CARRIER' AND m.ScopeKey = c.CarrierCode);
GO
-- Release justification -> outcome patterns (weak labels), approved 2026-10-06.
IF NOT EXISTS (SELECT 1 FROM ML.OutcomeTextMap WHERE LikePattern = N'%not an exception%')
    INSERT INTO ML.OutcomeTextMap (LikePattern, OutcomeType, ProposedLabel, LabelQuality, Priority, ApprovedBy, ApprovedAt)
    VALUES (N'%not an exception%', N'ValidRateChange', 0, N'WEAK', 10, N'mnasir', SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM ML.OutcomeTextMap WHERE LikePattern = N'%credit%received%')
    INSERT INTO ML.OutcomeTextMap (LikePattern, OutcomeType, ProposedLabel, LabelQuality, Priority, ApprovedBy, ApprovedAt)
    VALUES (N'%credit%received%', N'IncorrectBuy', 1, N'WEAK', 20, N'mnasir', SYSUTCDATETIME());
GO
/* Development defaults for ConfigVersion 'dev-defaults'. NOT production-approved values. */
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'SupervisedProbabilityThreshold')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'SupervisedProbabilityThreshold', 0.95, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'AEPercentileThreshold')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'AEPercentileThreshold', 99.5, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'TargetPublishedPrecision')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'TargetPublishedPrecision', 0.95, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'PeerLookbackWeeks')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'PeerLookbackWeeks', 26.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'MinimumPeerCount')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'MinimumPeerCount', 50.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'MinimumEvidenceCount')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'MinimumEvidenceCount', 2.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'NoveltyPublicationEnabled')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'NoveltyPublicationEnabled', 0.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'WeeklyAlertCap')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'WeeklyAlertCap', 200.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'PerAccountManagerAlertCap')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'PerAccountManagerAlertCap', 25.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'MinimumMaterialityAmount')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'MinimumMaterialityAmount', 50.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'MinimumCalibrationSample')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'MinimumCalibrationSample', 200.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'MinimumReviewedValidationSample')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'MinimumReviewedValidationSample', 300.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'MinimumPrecisionLowerBound')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'MinimumPrecisionLowerBound', 0.9, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'MaximumModelAgeDays')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'MaximumModelAgeDays', 120.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'AllowedDetectionRoutes')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'AllowedDetectionRoutes', NULL, N'CONFIRMED_PATTERN', '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'ModelVersion')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'ModelVersion', NULL, N'', '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'FeatureVersion')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'FeatureVersion', NULL, N'f1', '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'PublicationEnabled')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'PublicationEnabled', 0.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'ShadowMode')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'ShadowMode', 1.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'RetrainingLookbackWeeks')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'RetrainingLookbackWeeks', 52.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'DataQualityFailureTolerance')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'DataQualityFailureTolerance', 0.05, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'RobustZEvidenceThreshold')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'RobustZEvidenceThreshold', 3.5, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'UseMoveitFeaturesForTraining')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'UseMoveitFeaturesForTraining', 0.0, NULL, '2000-01-01', N'Development default');
IF NOT EXISTS (SELECT 1 FROM ML.ControlParameter WHERE ConfigVersion = N'dev-defaults' AND ScopeType = N'GLOBAL' AND ParameterName = N'RequireKnownCurrencyForPublication')
    INSERT INTO ML.ControlParameter (ConfigVersion, ScopeType, ScopeKey, ParameterName, NumericValue, TextValue, EffectiveFrom, Notes)
    VALUES (N'dev-defaults', N'GLOBAL', N'', N'RequireKnownCurrencyForPublication', 1.0, NULL, '2000-01-01', N'Development default');

GO
