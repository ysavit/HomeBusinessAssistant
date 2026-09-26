using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBusinessAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFounderScoutOnboardingAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowDisabledAgent",
                table: "ScheduleOccurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowDisabledAgent",
                table: "ScheduleOccurrences");
        }
    }
}
