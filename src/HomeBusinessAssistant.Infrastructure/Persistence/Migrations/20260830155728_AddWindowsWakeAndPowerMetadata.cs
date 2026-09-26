using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBusinessAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWindowsWakeAndPowerMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KeepDisplayOn",
                table: "ScheduleOccurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "KeepSystemAwake",
                table: "ScheduleOccurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresWake",
                table: "ScheduleOccurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_RequiresWake_Status_DueAtUtc",
                table: "ScheduleOccurrences",
                columns: new[] { "RequiresWake", "Status", "DueAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduleOccurrences_RequiresWake_Status_DueAtUtc",
                table: "ScheduleOccurrences");

            migrationBuilder.DropColumn(
                name: "KeepDisplayOn",
                table: "ScheduleOccurrences");

            migrationBuilder.DropColumn(
                name: "KeepSystemAwake",
                table: "ScheduleOccurrences");

            migrationBuilder.DropColumn(
                name: "RequiresWake",
                table: "ScheduleOccurrences");
        }
    }
}
