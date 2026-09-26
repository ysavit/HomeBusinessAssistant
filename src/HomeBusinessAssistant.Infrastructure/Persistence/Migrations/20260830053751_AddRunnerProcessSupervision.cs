using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBusinessAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerProcessSupervision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduleOccurrences_ParentOccurrenceId",
                table: "ScheduleOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_AgentRuns_Status_HeartbeatAtUtc",
                table: "AgentRuns");

            migrationBuilder.RenameColumn(
                name: "HeartbeatAtUtc",
                table: "AgentRuns",
                newName: "LastHeartbeatAtUtc");

            migrationBuilder.AlterColumn<int>(
                name: "ProcessId",
                table: "AgentRuns",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "ProcessStartedAtUtc",
                table: "AgentRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_ScheduleOccurrences_ParentOccurrenceId_AttemptNumber",
                table: "ScheduleOccurrences",
                columns: new[] { "ParentOccurrenceId", "AttemptNumber" },
                unique: true,
                filter: "ParentOccurrenceId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_Status_LastHeartbeatAtUtc",
                table: "AgentRuns",
                columns: new[] { "Status", "LastHeartbeatAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ScheduleOccurrences_ParentOccurrenceId_AttemptNumber",
                table: "ScheduleOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_AgentRuns_Status_LastHeartbeatAtUtc",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "ProcessStartedAtUtc",
                table: "AgentRuns");

            migrationBuilder.RenameColumn(
                name: "LastHeartbeatAtUtc",
                table: "AgentRuns",
                newName: "HeartbeatAtUtc");

            migrationBuilder.AlterColumn<int>(
                name: "ProcessId",
                table: "AgentRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_ParentOccurrenceId",
                table: "ScheduleOccurrences",
                column: "ParentOccurrenceId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_Status_HeartbeatAtUtc",
                table: "AgentRuns",
                columns: new[] { "Status", "HeartbeatAtUtc" });
        }
    }
}
