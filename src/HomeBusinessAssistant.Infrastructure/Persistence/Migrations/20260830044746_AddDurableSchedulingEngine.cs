using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBusinessAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableSchedulingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TerminalMessage",
                table: "ScheduleOccurrences",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminalReasonCode",
                table: "ScheduleOccurrences",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowDisabledAgent",
                table: "AgentSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ArgumentsJson",
                table: "AgentSchedules",
                type: "TEXT",
                maxLength: 65536,
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "CommandName",
                table: "AgentSchedules",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "run");

            migrationBuilder.AddColumn<bool>(
                name: "IsPaused",
                table: "AgentSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MisfireGracePeriodSeconds",
                table: "AgentSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<Guid>(
                name: "PinnedConfigurationRevisionId",
                table: "AgentSchedules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultConcurrencyPolicy",
                table: "AgentDefinitions",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Forbid");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresInteractiveUserSession",
                table: "AgentDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SupportedCommandsJson",
                table: "AgentDefinitions",
                type: "TEXT",
                maxLength: 8192,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "SupportsManualRun",
                table: "AgentDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsScheduling",
                table: "AgentDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_ScheduleId_Status_DueAtUtc",
                table: "ScheduleOccurrences",
                columns: new[] { "ScheduleId", "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSchedules_PinnedConfigurationRevisionId",
                table: "AgentSchedules",
                column: "PinnedConfigurationRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentSchedules_ConfigurationRevisions_PinnedConfigurationRevisionId",
                table: "AgentSchedules",
                column: "PinnedConfigurationRevisionId",
                principalTable: "ConfigurationRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("UPDATE ScheduleOccurrences SET Status = 'Ready' WHERE Status = 'Pending';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentSchedules_ConfigurationRevisions_PinnedConfigurationRevisionId",
                table: "AgentSchedules");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleOccurrences_ScheduleId_Status_DueAtUtc",
                table: "ScheduleOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_AgentSchedules_PinnedConfigurationRevisionId",
                table: "AgentSchedules");

            migrationBuilder.DropColumn(
                name: "TerminalMessage",
                table: "ScheduleOccurrences");

            migrationBuilder.DropColumn(
                name: "TerminalReasonCode",
                table: "ScheduleOccurrences");

            migrationBuilder.DropColumn(
                name: "AllowDisabledAgent",
                table: "AgentSchedules");

            migrationBuilder.DropColumn(
                name: "ArgumentsJson",
                table: "AgentSchedules");

            migrationBuilder.DropColumn(
                name: "CommandName",
                table: "AgentSchedules");

            migrationBuilder.DropColumn(
                name: "IsPaused",
                table: "AgentSchedules");

            migrationBuilder.DropColumn(
                name: "MisfireGracePeriodSeconds",
                table: "AgentSchedules");

            migrationBuilder.DropColumn(
                name: "PinnedConfigurationRevisionId",
                table: "AgentSchedules");

            migrationBuilder.DropColumn(
                name: "DefaultConcurrencyPolicy",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "RequiresInteractiveUserSession",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "SupportedCommandsJson",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "SupportsManualRun",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "SupportsScheduling",
                table: "AgentDefinitions");
        }
    }
}
