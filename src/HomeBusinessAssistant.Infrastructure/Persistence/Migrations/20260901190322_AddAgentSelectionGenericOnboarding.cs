using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBusinessAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSelectionGenericOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingAgentSelections",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SelectionStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProgressStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AdapterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CurrentStepKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReviewedManifestVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ReviewedConfigurationSchemaVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StartingConfigurationRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartingConfigurationHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    SavedConfigurationRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SavedConfigurationHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    LastReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingAgentSelections", x => new { x.SessionId, x.AgentId });
                    table.ForeignKey(
                        name: "FK_OnboardingAgentSelections_AgentDefinitions_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnboardingAgentSelections_ConfigurationRevisions_SavedConfigurationRevisionId",
                        column: x => x.SavedConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnboardingAgentSelections_ConfigurationRevisions_StartingConfigurationRevisionId",
                        column: x => x.StartingConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnboardingAgentSelections_OnboardingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "OnboardingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingAgentSelections_Agent_UpdatedAtUtc",
                table: "OnboardingAgentSelections",
                columns: new[] { "AgentId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingAgentSelections_SavedConfigurationRevisionId",
                table: "OnboardingAgentSelections",
                column: "SavedConfigurationRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingAgentSelections_Session_Status_Progress",
                table: "OnboardingAgentSelections",
                columns: new[] { "SessionId", "SelectionStatus", "ProgressStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingAgentSelections_StartingConfigurationRevisionId",
                table: "OnboardingAgentSelections",
                column: "StartingConfigurationRevisionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnboardingAgentSelections");
        }
    }
}
