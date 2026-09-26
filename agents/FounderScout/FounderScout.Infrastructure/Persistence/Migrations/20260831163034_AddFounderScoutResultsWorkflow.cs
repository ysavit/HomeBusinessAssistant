using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FounderScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFounderScoutResultsWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RawArtifactDeletedAtUtc",
                table: "ProfileSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawArtifactDeletionReasonCode",
                table: "ProfileSnapshots",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FounderScoutLeases",
                columns: table => new
                {
                    Resource = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FencingToken = table.Column<long>(type: "INTEGER", nullable: false),
                    AcquiredAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FounderScoutLeases", x => x.Resource);
                });

            migrationBuilder.CreateTable(
                name: "InvitationDraftRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceDraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShortDraft = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    DetailedDraft = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    EditedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EditedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidationErrorsJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    MaximumObservedSimilarity = table.Column<decimal>(type: "TEXT", nullable: false),
                    SimilarityFingerprint = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationDraftRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvitationDraftRevisions_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvitationDraftRevisions_InvitationDrafts_SourceDraftId",
                        column: x => x.SourceDraftId,
                        principalTable: "InvitationDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvitationQueueEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WindowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueueKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ManuallyIncluded = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AddedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    RemovedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    RemovalReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationQueueEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvitationQueueEntries_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvitationQueueEntries_ManualInvitationWindows_WindowId",
                        column: x => x.WindowId,
                        principalTable: "ManualInvitationWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FounderScoutLeases_Expires",
                table: "FounderScoutLeases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationDraftRevisions_Candidate_Active_Edited",
                table: "InvitationDraftRevisions",
                columns: new[] { "CandidateId", "IsActive", "EditedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationDraftRevisions_Source_Edited",
                table: "InvitationDraftRevisions",
                columns: new[] { "SourceDraftId", "EditedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationQueueEntries_CandidateId",
                table: "InvitationQueueEntries",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationQueueEntries_Window_Kind_Position",
                table: "InvitationQueueEntries",
                columns: new[] { "WindowId", "QueueKind", "Position", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_InvitationQueueEntries_Window_Candidate_Active",
                table: "InvitationQueueEntries",
                columns: new[] { "WindowId", "CandidateId" },
                unique: true,
                filter: "\"RemovedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FounderScoutLeases");

            migrationBuilder.DropTable(
                name: "InvitationDraftRevisions");

            migrationBuilder.DropTable(
                name: "InvitationQueueEntries");

            migrationBuilder.DropColumn(
                name: "RawArtifactDeletedAtUtc",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "RawArtifactDeletionReasonCode",
                table: "ProfileSnapshots");
        }
    }
}
