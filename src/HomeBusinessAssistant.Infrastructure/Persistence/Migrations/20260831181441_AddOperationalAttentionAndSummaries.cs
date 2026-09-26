using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBusinessAssistant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalAttentionAndSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttentionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScheduleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FirstObservedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    LastObservedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AcknowledgedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResolvedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    ContextJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttentionItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailySummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalDate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SummaryText = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    SummaryJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    GeneratedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodStartUtc = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodEndUtc = table.Column<string>(type: "TEXT", nullable: false),
                    GenerationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailySummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttentionItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    NotBeforeUtc = table.Column<string>(type: "TEXT", nullable: false),
                    DeliveredAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    DeliveryAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ThrottleKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalNotifications_AttentionItems_AttentionItemId",
                        column: x => x.AttentionItemId,
                        principalTable: "AttentionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttentionItems_Status_Severity_LastObservedAtUtc",
                table: "AttentionItems",
                columns: new[] { "Status", "Severity", "LastObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AttentionItems_DedupeKey",
                table: "AttentionItems",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailySummaries_GeneratedAtUtc",
                table: "DailySummaries",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_DailySummaries_Date_TimeZone_Generation",
                table: "DailySummaries",
                columns: new[] { "LocalDate", "TimeZoneId", "GenerationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocalNotifications_AttentionItemId",
                table: "LocalNotifications",
                column: "AttentionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalNotifications_Status_NotBeforeUtc_CreatedAtUtc",
                table: "LocalNotifications",
                columns: new[] { "Status", "NotBeforeUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_LocalNotifications_DedupeKey",
                table: "LocalNotifications",
                column: "DedupeKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailySummaries");

            migrationBuilder.DropTable(
                name: "LocalNotifications");

            migrationBuilder.DropTable(
                name: "AttentionItems");
        }
    }
}
