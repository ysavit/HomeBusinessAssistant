using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FounderScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFounderScoutPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrowserAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BrowserProfileRelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SessionStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AssignedSegmentIdsJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                    LastAuthenticatedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    LastSuccessfulRunAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    LastFailedRunAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SuspendedUntilUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrowserAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentSourceProfileKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CanonicalSourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedLocation = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    TechnicalStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CommitmentStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdeaCommitmentStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FirstSeenAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    LastActivityText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastActivityAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LatestEvaluationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FounderQualityScore = table.Column<decimal>(type: "TEXT", nullable: true),
                    OurFitScore = table.Column<decimal>(type: "TEXT", nullable: true),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: true),
                    ActivityScore = table.Column<decimal>(type: "TEXT", nullable: true),
                    RiskPenalty = table.Column<decimal>(type: "TEXT", nullable: true),
                    InvitationPriority = table.Column<decimal>(type: "TEXT", nullable: true),
                    AnalysisWorkerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AnalysisClaimedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    AnalysisClaimExpiresAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    AnalysisAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAnalysisErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualInvitationWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    EndAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryQueueSize = table.Column<int>(type: "INTEGER", nullable: false),
                    ReserveQueueSize = table.Column<int>(type: "INTEGER", nullable: false),
                    SentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualInvitationWindows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FilterSortHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RowCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    DeleteAfterUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportExports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoverySegments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    AssignedAccountId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastRunAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    ViewedCount = table.Column<long>(type: "INTEGER", nullable: false),
                    NewCount = table.Column<long>(type: "INTEGER", nullable: false),
                    DuplicateCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ConsecutiveLowYieldRuns = table.Column<int>(type: "INTEGER", nullable: false),
                    PausedUntilUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoverySegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoverySegments_BrowserAccounts_AssignedAccountId",
                        column: x => x.AssignedAccountId,
                        principalTable: "BrowserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CandidateActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OccurredAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DataJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    RelatedInvitationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RelatedEvaluationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RelatedRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateActions_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CandidateIdentityAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AliasType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedValueHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    SourceAccountId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsStrong = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateIdentityAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateIdentityAliases_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProfileSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAccountId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceSegmentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceProfileKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CanonicalSourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    ParserVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceAdapterVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CapturedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    RawArtifactRelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    DeleteAfterUtc = table.Column<string>(type: "TEXT", nullable: true),
                    NormalizedProfileJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: true),
                    ExtractionCompleteness = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExtractionConfidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileSnapshots_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SegmentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DomainRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastSourceProfileKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    StateJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    CapturedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryCheckpoints_BrowserAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BrowserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiscoveryCheckpoints_DiscoverySegments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "DiscoverySegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Evaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScorecardVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EvaluatorProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EvaluatorModel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EvaluatorDeploymentVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PromptVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InputHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    FounderQualityScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    OurFitScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    ActivityScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    BaseScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    RiskPenalty = table.Column<decimal>(type: "TEXT", nullable: false),
                    FinalScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    InvitationPriority = table.Column<decimal>(type: "TEXT", nullable: false),
                    Recommendation = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StructuredEvaluationJson = table.Column<string>(type: "TEXT", maxLength: 1048576, nullable: false),
                    RawProviderArtifactReference = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryAfterUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Evaluations_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Evaluations_ProfileSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "ProfileSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RulesetVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Score = table.Column<decimal>(type: "TEXT", nullable: true),
                    ReasonCodesJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningDecisions_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningDecisions_ProfileSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "ProfileSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Score = table.Column<decimal>(type: "TEXT", nullable: false),
                    Maximum = table.Column<decimal>(type: "TEXT", nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationCategories_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationRisks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Penalty = table.Column<decimal>(type: "TEXT", nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationRisks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationRisks_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvitationDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShortDraft = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    DetailedDraft = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    FactsUsedJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ValidationErrorsJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    SimilarityFingerprint = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Superseded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvitationDrafts_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvitationDrafts_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserAccounts_Enabled_Status_Id",
                table: "BrowserAccounts",
                columns: new[] { "Enabled", "SessionStatus", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateActions_Candidate_Occurred_Id",
                table: "CandidateActions",
                columns: new[] { "CandidateId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateActions_Correlation_Id",
                table: "CandidateActions",
                columns: new[] { "CorrelationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateActions_RelatedRun_Id",
                table: "CandidateActions",
                columns: new[] { "RelatedRunId", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_CandidateIdentityAliases_Candidate_Type_Hash",
                table: "CandidateIdentityAliases",
                columns: new[] { "CandidateId", "AliasType", "NormalizedValueHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CandidateIdentityAliases_StrongActiveHash",
                table: "CandidateIdentityAliases",
                columns: new[] { "NormalizedValueHash", "IsStrong", "IsActive" },
                unique: true,
                filter: "IsStrong = 1 AND IsActive = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_AnalysisQueue_Priority_Id",
                table: "Candidates",
                columns: new[] { "Status", "AnalysisClaimExpiresAtUtc", "InvitationPriority", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_CurrentSnapshotId",
                table: "Candidates",
                column: "CurrentSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_LastSeen_Id",
                table: "Candidates",
                columns: new[] { "LastSeenAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_LatestEvaluationId",
                table: "Candidates",
                column: "LatestEvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_Status_Priority_Id",
                table: "Candidates",
                columns: new[] { "Status", "InvitationPriority", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryCheckpoints_Account_Segment_Captured",
                table: "DiscoveryCheckpoints",
                columns: new[] { "AccountId", "SegmentId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryCheckpoints_DomainRun_Id",
                table: "DiscoveryCheckpoints",
                columns: new[] { "DomainRunId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryCheckpoints_SegmentId",
                table: "DiscoveryCheckpoints",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverySegments_Active_Priority_Id",
                table: "DiscoverySegments",
                columns: new[] { "Enabled", "PausedUntilUtc", "Priority", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverySegments_AssignedAccountId",
                table: "DiscoverySegments",
                column: "AssignedAccountId");

            migrationBuilder.CreateIndex(
                name: "UX_EvaluationCategories_Evaluation_Key",
                table: "EvaluationCategories",
                columns: new[] { "EvaluationId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_EvaluationRisks_Evaluation_Key",
                table: "EvaluationRisks",
                columns: new[] { "EvaluationId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_Candidate_Created",
                table: "Evaluations",
                columns: new[] { "CandidateId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_SnapshotId",
                table: "Evaluations",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_Status_RetryAfter_Created",
                table: "Evaluations",
                columns: new[] { "Status", "RetryAfterUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Evaluations_Candidate_InputHash",
                table: "Evaluations",
                columns: new[] { "CandidateId", "InputHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvitationDrafts_Candidate_Active_Created",
                table: "InvitationDrafts",
                columns: new[] { "CandidateId", "Superseded", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationDrafts_EvaluationId",
                table: "InvitationDrafts",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationDrafts_Status_Created",
                table: "InvitationDrafts",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualInvitationWindows_Start_End",
                table: "ManualInvitationWindows",
                columns: new[] { "StartAtUtc", "EndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileSnapshots_Candidate_Captured",
                table: "ProfileSnapshots",
                columns: new[] { "CandidateId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileSnapshots_DeleteAfter_Id",
                table: "ProfileSnapshots",
                columns: new[] { "DeleteAfterUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_ProfileSnapshots_Candidate_SourceKey_ContentHash",
                table: "ProfileSnapshots",
                columns: new[] { "CandidateId", "SourceProfileKey", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportExports_Created_Id",
                table: "ReportExports",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportExports_DeleteAfter_Id",
                table: "ReportExports",
                columns: new[] { "DeleteAfterUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningDecisions_Candidate_Created",
                table: "ScreeningDecisions",
                columns: new[] { "CandidateId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ScreeningDecisions_Snapshot_Ruleset",
                table: "ScreeningDecisions",
                columns: new[] { "SnapshotId", "RulesetVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateActions");

            migrationBuilder.DropTable(
                name: "CandidateIdentityAliases");

            migrationBuilder.DropTable(
                name: "DiscoveryCheckpoints");

            migrationBuilder.DropTable(
                name: "EvaluationCategories");

            migrationBuilder.DropTable(
                name: "EvaluationRisks");

            migrationBuilder.DropTable(
                name: "InvitationDrafts");

            migrationBuilder.DropTable(
                name: "ManualInvitationWindows");

            migrationBuilder.DropTable(
                name: "ReportExports");

            migrationBuilder.DropTable(
                name: "ScreeningDecisions");

            migrationBuilder.DropTable(
                name: "DiscoverySegments");

            migrationBuilder.DropTable(
                name: "Evaluations");

            migrationBuilder.DropTable(
                name: "BrowserAccounts");

            migrationBuilder.DropTable(
                name: "ProfileSnapshots");

            migrationBuilder.DropTable(
                name: "Candidates");
        }
    }
}
