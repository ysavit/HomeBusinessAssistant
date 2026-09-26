# V1 acceptance evidence

This is the line-item evidence map for all 122 rows in `acceptance-test-matrix.md`. Totals: **120 automated, 2 manual-only, 0 deferred**. “Automated” includes release commands or focused integration tests over production boundaries. Live external services and real workstation sleep are additional opt-in confidence checks, not requirements for deterministic V1 correctness.

## Repository and startup

| ID | Status | Evidence |
|---|---|---|
| RS-1 | Automated | Required restore/build/test/format release commands in this checklist and ExecPlan. |
| RS-2 | Automated | `PersistenceIntegrationTests.InitializationAppliesMigrationPragmasIndexesAndSeedDataIdempotently`; `FounderScoutPersistenceTests.FreshMigrationCreatesSeparateSchemaIndexesAndRequiredPragmas`. |
| RS-3 | Automated | `FounderScoutPersistenceTests.ReinitializationPreservesCompatibleExistingFounderData` plus central/founder migration upgrade tests. |
| RS-4 | Automated | `HostRuntimeIntegrationTests.BootstrapValidationRejectsEscapingRunnerAndUnboundedIntervals`; `HostApplicationTests.UrlPolicyRejectsNonLoopbackBinding`. |
| RS-5 | Automated | `DesktopHostAdapterTests.SecondaryCoordinatorSignalsTheCurrentUserPrimary`; published smoke launches a secondary Host. |
| RS-6 | Automated | `HostApplicationTests.HealthEndpointsAndDashboardAreAvailableOnlyOnIpv4Loopback`. |

## Configuration and audit

| ID | Status | Evidence |
|---|---|---|
| CA-1 | Automated | `PersistenceIntegrationTests.ConfigurationSaveIsCanonicalImmutableIdempotentAndConcurrencySafe`. |
| CA-2 | Automated | Configuration validator and persistence rollback cases in `PersistenceIntegrationTests`/`ConfigurationAndRedactionTests`. |
| CA-3 | Automated | `PersistenceIntegrationTests.ConfigurationSaveIsCanonicalImmutableIdempotentAndConcurrencySafe`. |
| CA-4 | Automated | `SchedulingIntegrationTests.ManualRunsCaptureTriggerRevisionPauseBypassAndQueueOne`. |
| CA-5 | Automated | `WindowsCurrentUserSecretStoreTests.DpapiStoreRoundTripsEncryptedFileAndAuditsSetAndDelete`; configuration secret-reference tests. |
| CA-6 | Automated | `ManagementUiIntegrationTests.RealPersistedManagementPagesRenderAndDiagnosticsExcludeSecretMaterial`; `RunnerProcessIntegrationTests.StderrIsRedactedAndBounded`. |
| CA-7 | Automated | Management UI configuration-history integration assertions and redacted diff projection tests. |

## Scheduling and occurrences

| ID | Status | Evidence |
|---|---|---|
| SO-1 | Automated | `SchedulingTests.CalendarAndIntervalCalculationsAreBoundedAndUtc`. |
| SO-2 | Automated | `SchedulingTests.DstGapAdvancesToFirstValidSecondAndAmbiguityChoosesEarlierUtcInstant`. |
| SO-3 | Automated | Same DST test plus `WakeAndPowerTests.UtcBoundaryRepresentsBothDstOverlapInstantsWithoutAmbiguity`. |
| SO-4 | Automated | `SchedulingTests.CalendarAndIntervalCalculationsAreBoundedAndUtc`. |
| SO-5 | Automated | `SchedulingTests.FixedDelayDoesNotCalculateCadenceFromScheduledSlots`; fixed-delay integration tests. |
| SO-6 | Automated | Schedule validation/control and planning cases in `SchedulingIntegrationTests`. |
| SO-7 | Automated | `SchedulingIntegrationTests.MisfireGraceAndConcurrencyPoliciesPersistExplicitDecisions`. |
| SO-8 | Automated | `SchedulingIntegrationTests.MisfireGraceAndConcurrencyPoliciesPersistExplicitDecisions`. |
| SO-9 | Automated | `SchedulingIntegrationTests.PlannerLimitUniqueIdentityAndLeaseContentionAreDurable`. |
| SO-10 | Automated | `SchedulingIntegrationTests.ManualRunsCaptureTriggerRevisionPauseBypassAndQueueOne`. |

## Runner

| ID | Status | Evidence |
|---|---|---|
| RUN-1 | Automated | `PersistenceIntegrationTests.CompetingOccurrenceClaimsAndLeasesHaveOneWinner`. |
| RUN-2 | Automated | `RunnerProcessIntegrationTests.CompetingExecutorsLaunchOccurrenceOnlyOnce`. |
| RUN-3 | Automated | `RunnerProcessIntegrationTests.AllEventTypesAndMetricArePersisted`; artifact integration test. |
| RUN-4 | Automated | malformed/oversized protocol cases in `RunnerProcessIntegrationTests`. |
| RUN-5 | Automated | `RunnerProcessIntegrationTests.NonzeroExitIsPersistedAsFailure`. |
| RUN-6 | Automated | `RunnerProcessIntegrationTests.TimeoutKillsTheEntireFixtureProcessTree`. |
| RUN-7 | Automated | `RunnerProcessIntegrationTests.PersistedCancellationIsDistinctFromTimeoutAndReleasesProcess`. |
| RUN-8 | Automated | `RecoveryIntegrationTests.RecoveryAbandonsExpiredClaimAndOrphanedStartingRunAndCleansStaleTemp`. |
| RUN-9 | Automated | `RunnerProcessIntegrationTests.FatalProtocolScenariosFailWithoutCrashingRunner` traversal case. |
| RUN-10 | Automated | occurrence terminal-state and competing/repeat execution tests. |

## Windows wake bridge

| ID | Status | Evidence |
|---|---|---|
| WB-1 | Automated | `WakeAndPowerTests.XmlContainsExactUtcWakeRunnerAndEscapedUnicodePolicy`. |
| WB-2 | Automated | schtasks fake executor asserts argument-list registration in `WakeAndPowerTests`. |
| WB-3 | Automated | `WakeReconciliationIntegrationTests.ReconciliationIsIdempotentReplacesEarliestRemovesEmptyAndHonorsLease`. |
| WB-4 | Automated | Same wake reconciliation integration test. |
| WB-5 | Automated | `WakeReconciliationIntegrationTests.PermissionFailureReturnsStructuredErrorAndWritesFailedAudit`. |
| WB-6 | Automated | Runner power-lifetime success/failure/timeout/cancellation tests and `WakeAndPowerTests.NestedPowerHandlesKeepCombinedFlagsUntilFinalRelease`. |
| WB-7 | Automated | Windows tests use generated XML/fakes/read-only diagnostics only; no sleep or permanent-policy API exists. |

## Wake & Remote agent

| ID | Status | Evidence |
|---|---|---|
| WR-1 | Automated | readiness probe and workflow immediate-ready paths. |
| WR-2 | Automated | `WakeRemoteWorkflowTests.NetworkRetriesThenReadyAndActiveWindowReleasePower`. |
| WR-3 | Automated | `WakeRemoteWorkflowTests.NetworkTimeoutReturnsFailureAndReleasesPower`. |
| WR-4 | Automated | `WakeRemoteWorkflowTests.RequiredProviderFailureReleasesPower`. |
| WR-5 | Automated | `WakeRemoteWorkflowTests.OptionalProviderWarningContinuesUntilDeadline`. |
| WR-6 | Automated | active-window workflow plus real Runner diagnostic-window integration. |
| WR-7 | Automated | `WakeRemoteWorkflowTests.CancellationDuringNetworkWaitReleasesPower`. |
| WR-8 | Automated | configuration rejects forced sleep; source inventory contains no force-sleep path. |

## Tray and local UI

| ID | Status | Evidence |
|---|---|---|
| UI-1 | Manual | On the installed RC, double-click the tray icon and select **Open dashboard**; verify the same owner-authenticated loopback dashboard opens. Repeat each menu action and **Exit** confirmation; verify one Host process remains until exit, then none. Controller/IPC are automated, but native `NotifyIcon` interaction requires an interactive desktop. |
| UI-2 | Automated | `DesktopControlTests` and global schedule control/audit integration tests. |
| UI-3 | Automated | `TrayRunnerIntegrationTests.TrayDiagnosticCreatesOccurrenceAndRunsRealWakeRemoteThroughRunner`; desktop dispatch tests. |
| UI-4 | Automated | seeded management UI integration and rendered synthetic browser fixture cover status variants. |
| UI-5 | Automated | Host integration posts without tokens and asserts HTTP 400; malicious Origin is rejected. |
| UI-6 | Automated | management run-detail integration renders encoded hostile diagnostics and all bounded projections. |
| UI-7 | Automated | Razor confirmation form assertions plus uninstall/restore exact confirmation-token tests. |

## Founder Scout capture/discovery

| ID | Status | Evidence |
|---|---|---|
| FSD-1 | Automated | `FounderScoutImportTests.FixtureImportPersistsArtifactsBeforeQueueingAndIsIdempotent`. |
| FSD-2 | Automated | browser profile path/root and lease tests in `FounderScoutBrowserTests`. |
| FSD-3 | Automated | `FounderScoutBrowserTests.DetectorMapsAuthenticationAndChallengeFixturesWithoutFailover`. |
| FSD-4 | Automated | enforcement fixture cases in `FounderScoutRunnerIntegrationTests.BrowserEnforcementFixturesStopOneAccountWithoutFailover`. |
| FSD-5 | Automated | parser failure cases in browser/processing tests stop and pause affected work. |
| FSD-6 | Automated | 40-profile Stage 16 workflow repeat/cache assertions. |
| FSD-7 | Automated | Stage 16 workflow imports one changed profile and asserts 41 snapshots for 40 candidates. |
| FSD-8 | Automated | strong-alias/cross-account persistence and processing identity tests. |
| FSD-9 | Automated | daily/runtime/known/batch limit cases in `FounderScoutBrowserTests`. |
| FSD-10 | Automated | domain rejects automatic send; E2E summaries assert `invitationsSent:0`. |

## Founder Scout processing

| ID | Status | Evidence |
|---|---|---|
| FSP-1 | Automated | `FounderScoutProcessingTests.ParserNormalizesEveryFounderFieldAndExcludesProtectedContent`. |
| FSP-2 | Automated | `FounderScoutProcessingTests.CanonicalHashesAreStableForUnicodeWhitespaceSetsAndIgnoreRawHtml`. |
| FSP-3 | Automated | `FounderScoutProcessingTests.ParserHealthAndIdentitySignalsRemainDeterministicAndConservative`. |
| FSP-4 | Automated | parser/redaction and evaluator-input protected-marker tests. |
| FSP-5 | Automated | `FounderScoutProcessingTests.ScreeningProducesGroundedVersionedOutcomesAndMissingEvidence`. |
| FSP-6 | Automated | same screening test. |
| FSP-7 | Automated | durable idempotent processing batch and one-owner claim tests. |
| FSP-8 | Automated | canonical hash tests and ADR-0009 semantics. |
| FSP-9 | Automated | `FounderScoutProcessingTests.ProcessingBatchIsDurableIdempotentRedactedAndRequeuesOnlyRelevantChanges`. |
| FSP-10 | Automated | same processing batch test verifies bounded field-only diff and one requeue. |
| FSP-11 | Automated | `FounderScoutProcessingTests.StrongFingerprintMergesAndWeakAmbiguityCreatesVisibleConflict`. |
| FSP-12 | Automated | processing claim expiry/owner tests plus independent analysis claim tests. |
| FSP-13 | Automated | `FounderScoutProcessingTests.RepeatedParserFailuresStopBatchAndPauseAccountSegment`. |
| FSP-14 | Automated | `FounderScoutProcessingTests.ProcessingClaimsHaveOneOwnerExpireAndManualOverrideIsAudited`. |
| FSP-15 | Automated | `FounderScoutRunnerIntegrationTests.RequiredFixtureSmokeRunsThroughRunnerAndCorrelatesSeparateDatabases`. |

## AI evaluation and scoring

| ID | Status | Evidence |
|---|---|---|
| AI-1 | Automated | strict schema and valid typed evaluation tests in `FounderEvaluationTests`. |
| AI-2 | Automated | `FounderEvaluationTests.DeterministicProviderRepairsInvalidSchemaOnce`. |
| AI-3 | Automated | fake-provider transient/permanent response scenarios in evaluation and 20-profile smoke tests. |
| AI-4 | Automated | `FounderEvaluationTests.ValidationRequiresExactKeysBoundsEvidenceAndExplicitUnknowns`. |
| AI-5 | Automated | `FounderEvaluationTests.DeterministicScoringSeparatesArithmeticRiskConfidenceActivityAndPriority`. |
| AI-6 | Automated | evaluation evidence validation tests. |
| AI-7 | Automated | explicit-unknown/missing-evidence validation tests. |
| AI-8 | Automated | `FounderEvaluationTests.CacheKeyChangesForEveryBehaviorVersionFamily`. |
| AI-9 | Automated | cache-key and 20-profile cache-reuse smoke assertions. |
| AI-10 | Automated | `FounderEvaluationTests.SyntheticLiveProviderEvaluationIsOptIn` is explicitly gated/skipped by default. |
| AI-11 | Automated | exact key and bound assertions in `ValidationRequiresExactKeysBoundsEvidenceAndExplicitUnknowns`. |
| AI-12 | Automated | `FounderEvaluationTests.FounderQualityNormalizationExcludesCtoFit`. |
| AI-13 | Automated | deterministic scoring separation plus persistence projection tests. |
| AI-14 | Automated | `RiskWithoutEvidenceIsNotAppliedByCalculator`; fabricated-number rejection. |
| AI-15 | Automated | `FounderEvaluationTests.LowConfidenceForcesManualReview`. |
| AI-16 | Automated | missing-secret permanent failure and bounded transient-retry smoke cases. |
| AI-17 | Automated | `FounderScoutRunnerIntegrationTests.RequiredDeepEvaluationSmokeRunsTwentySyntheticCandidatesThroughRunner`. |

## Introduction drafts

| ID | Status | Evidence |
|---|---|---|
| DRAFT-1 | Automated | deep-evaluation smoke asserts one draft per evaluated candidate and both variants. |
| DRAFT-2 | Automated | invitation grounding validation tests. |
| DRAFT-3 | Automated | invitation validation and prompt asset policy tests. |
| DRAFT-4 | Automated | `FounderEvaluationTests.InvitationValidationRejectsUnsafeOrUnhelpfulDrafts`. |
| DRAFT-5 | Automated | configurable length validation cases. |
| DRAFT-6 | Automated | near-duplicate repair/manual-review fake-provider scenario. |
| DRAFT-7 | Automated | no send adapter/domain transition; all runner summaries report zero sends. |
| DRAFT-8 | Automated | invalid draft retained as review-needed in deep-evaluation smoke. |
| DRAFT-9 | Automated | invitation similarity repository/service tests exclude same-candidate superseded drafts. |

## Candidate ranking/UI/reports

| ID | Status | Evidence |
|---|---|---|
| RPT-1 | Automated | scoring/persistence/results tests retain separate dimensions. |
| RPT-2 | Automated | deterministic scoring and 10,000-candidate stable-order tests. |
| RPT-3 | Automated | `FounderScoutResultsTests.TenThousandCandidateQueryRemainsPagedStableAndBounded`. |
| RPT-4 | Automated | queue window size, primary/reserve capacity, and UI integration tests. |
| RPT-5 | Automated | explicit `MarkDraftReviewed`/`RecordOutcome` persistence tests and Stage 16 workflow. |
| RPT-6 | Automated | `FounderScoutResultsTests.AllReportsShareCanonicalOrderAndEncodeHostileContent`. |
| RPT-7 | Automated | same report test plus management secret/path exclusion assertions. |
| RPT-8 | Automated | `FounderScoutResultsTests.DraftQueueOutcomeAndRawRetentionRemainExplicitAndAudited`. |

## Recovery, backup, and performance

| ID | Status | Evidence |
|---|---|---|
| REC-1 | Automated | `DatabaseBackupServiceTests.CreateValidatesBothDatabasesAndExcludesSensitiveDirectories`; active-write test. |
| REC-2 | Automated | `DatabaseBackupServiceTests.RestoreRequiresConfirmationAndRollsBothDatabasesBackToTheSet`. |
| REC-3 | Automated | `DatabaseBackupServiceTests.BackupRemainsConsistentDuringActiveSourceWritesAndIgnoresInterruptedSet`. |
| REC-4 | Automated | `RecoveryIntegrationTests` plus Host runtime startup/reconciliation tests. |
| REC-5 | Automated | `FounderScoutResultsTests.TenThousandCandidateQueryRemainsPagedStableAndBounded`. |
| REC-6 | Automated | `ManagementScaleTests.OneHundredThousandRunEventsUseTheRunSequenceIndexAndReturnABoundedDetailPage`. |
| REC-7 | Automated | operational and Founder Scout retention tests exclude active data and write audit. |
| REC-8 | Manual | On a clean Windows 11 x64 VM/user profile, verify the ZIP SHA-256, extract to a path containing spaces/Unicode, run `install.ps1 -SkipStartupTask`, then bundled `smoke-test.ps1`, `repair.ps1`, and `uninstall.ps1`; verify no product process or synthetic data remains. The same script is automated on the build workstation, but “clean machine” requires a separate environment. |

## Optional external/manual confidence checks

These do not change the 122-row totals: native tray interaction (UI-1), clean-machine smoke (REC-8), real Task Scheduler/hardware wake, live Startup School authentication/selectors, a live AI provider, real remote access, Authenticode/SmartScreen, and enterprise deployment policy. Record them as executed only when performed on the intended release environment.
