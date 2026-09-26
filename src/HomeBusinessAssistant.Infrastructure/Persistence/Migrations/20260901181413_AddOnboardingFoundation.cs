using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBusinessAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    DeferredAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    CancelledAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AcknowledgedWarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedStepCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveSlot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    InitializationKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CheckKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AgentScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ObservedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    DetailsSchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                    RemediationKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnboardingChecks_AgentDefinitions_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnboardingChecks_OnboardingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "OnboardingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingChecks_AgentId",
                table: "OnboardingChecks",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingChecks_Session_Status_ObservedAtUtc",
                table: "OnboardingChecks",
                columns: new[] { "SessionId", "Status", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_OnboardingChecks_LatestIdentity",
                table: "OnboardingChecks",
                columns: new[] { "SessionId", "Scope", "CheckKey", "AgentScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingSessions_Status_CreatedAtUtc",
                table: "OnboardingSessions",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_OnboardingSessions_ActiveSlot",
                table: "OnboardingSessions",
                column: "ActiveSlot",
                unique: true,
                filter: "ActiveSlot IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_OnboardingSessions_InitializationKey",
                table: "OnboardingSessions",
                column: "InitializationKey",
                unique: true,
                filter: "InitializationKey IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnboardingChecks");

            migrationBuilder.DropTable(
                name: "OnboardingSessions");
        }
    }
}
