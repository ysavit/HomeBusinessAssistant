using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBusinessAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCentralPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ManifestVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InstalledVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExecutableRelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    WorkingDirectoryRelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentLeases",
                columns: table => new
                {
                    LeaseName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AcquiredAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    FencingToken = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentLeases", x => x.LeaseName);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AgentSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MisfirePolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConcurrencyPolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryPolicyJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    WakePolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PausedUntilUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentSchedules_AgentDefinitions_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConfigurationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RevisionNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CanonicalConfigurationJson = table.Column<string>(type: "TEXT", maxLength: 1048576, nullable: false),
                    ConfigurationHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    ChangedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ChangeSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfigurationRevisions_AgentDefinitions_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentConfigurations_AgentDefinitions_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentConfigurations_ConfigurationRevisions_CurrentRevisionId",
                        column: x => x.CurrentRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CommandName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DueAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    TriggerType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentOccurrenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClaimedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClaimedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimExpiresAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CancellationRequestedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleOccurrences_AgentDefinitions_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleOccurrences_AgentSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "AgentSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleOccurrences_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleOccurrences_ScheduleOccurrences_ParentOccurrenceId",
                        column: x => x.ParentOccurrenceId,
                        principalTable: "ScheduleOccurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurrenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfigurationHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    TriggerType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RunnerVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AgentVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ManifestVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExecutableHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    MachineName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    HeartbeatAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    SummaryText = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    SummaryJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: true),
                    ErrorType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRuns_AgentDefinitions_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentRuns_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentRuns_ScheduleOccurrences_OccurrenceId",
                        column: x => x.OccurrenceId,
                        principalTable: "ScheduleOccurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentRunEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRunEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRunEvents_AgentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentRunMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NumericValue = table.Column<double>(type: "REAL", nullable: true),
                    TextValue = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    TagsJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                    TimestampUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRunMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRunMetrics_AgentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ActorType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DataJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_AgentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ArtifactType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 192, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    DeleteAfterUtc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunArtifacts_AgentDefinitions_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunArtifacts_AgentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentConfigurations_CurrentRevisionId",
                table: "AgentConfigurations",
                column: "CurrentRevisionId");

            migrationBuilder.CreateIndex(
                name: "UX_AgentConfigurations_AgentId",
                table: "AgentConfigurations",
                column: "AgentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentDefinitions_Enabled_Id",
                table: "AgentDefinitions",
                columns: new[] { "Enabled", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentLeases_ExpiresAtUtc",
                table: "AgentLeases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunEvents_RunId_TimestampUtc",
                table: "AgentRunEvents",
                columns: new[] { "RunId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AgentRunEvents_RunId_Sequence",
                table: "AgentRunEvents",
                columns: new[] { "RunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunMetrics_RunId_Name_TimestampUtc",
                table: "AgentRunMetrics",
                columns: new[] { "RunId", "Name", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_AgentId_StartedAtUtc",
                table: "AgentRuns",
                columns: new[] { "AgentId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_ConfigurationRevisionId",
                table: "AgentRuns",
                column: "ConfigurationRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_Status_HeartbeatAtUtc",
                table: "AgentRuns",
                columns: new[] { "Status", "HeartbeatAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AgentRuns_OccurrenceId",
                table: "AgentRuns",
                column: "OccurrenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentSchedules_Active_AgentId",
                table: "AgentSchedules",
                columns: new[] { "IsEnabled", "PausedUntilUtc", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "UX_AgentSchedules_AgentId_Name",
                table: "AgentSchedules",
                columns: new[] { "AgentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CorrelationId",
                table: "AuditEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_RunId",
                table: "AuditEvents",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Target_TimestampUtc",
                table: "AuditEvents",
                columns: new[] { "TargetType", "TargetId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TimestampUtc",
                table: "AuditEvents",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "UX_ConfigurationRevisions_AgentId_Hash",
                table: "ConfigurationRevisions",
                columns: new[] { "AgentId", "ConfigurationHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ConfigurationRevisions_AgentId_RevisionNumber",
                table: "ConfigurationRevisions",
                columns: new[] { "AgentId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunArtifacts_AgentId",
                table: "RunArtifacts",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_RunArtifacts_DeleteAfterUtc",
                table: "RunArtifacts",
                column: "DeleteAfterUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RunArtifacts_RunId_CreatedAtUtc",
                table: "RunArtifacts",
                columns: new[] { "RunId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_AgentId_Status",
                table: "ScheduleOccurrences",
                columns: new[] { "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_ConfigurationRevisionId",
                table: "ScheduleOccurrences",
                column: "ConfigurationRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_ParentOccurrenceId",
                table: "ScheduleOccurrences",
                column: "ParentOccurrenceId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_Status_DueAtUtc",
                table: "ScheduleOccurrences",
                columns: new[] { "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ScheduleOccurrences_RunId",
                table: "ScheduleOccurrences",
                column: "RunId",
                unique: true,
                filter: "RunId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ScheduleOccurrences_ScheduleId_DueAtUtc",
                table: "ScheduleOccurrences",
                columns: new[] { "ScheduleId", "DueAtUtc" },
                unique: true,
                filter: "ScheduleId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentConfigurations");

            migrationBuilder.DropTable(
                name: "AgentLeases");

            migrationBuilder.DropTable(
                name: "AgentRunEvents");

            migrationBuilder.DropTable(
                name: "AgentRunMetrics");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "RunArtifacts");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "AgentRuns");

            migrationBuilder.DropTable(
                name: "ScheduleOccurrences");

            migrationBuilder.DropTable(
                name: "AgentSchedules");

            migrationBuilder.DropTable(
                name: "ConfigurationRevisions");

            migrationBuilder.DropTable(
                name: "AgentDefinitions");
        }
    }
}
