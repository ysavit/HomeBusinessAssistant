using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FounderScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFounderScoutProcessingPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvaluatorInputHash",
                table: "ScreeningDecisions",
                type: "TEXT",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EvaluatorInputJson",
                table: "ScreeningDecisions",
                type: "TEXT",
                maxLength: 262144,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsManualOverride",
                table: "ScreeningDecisions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MissingEvidenceJson",
                table: "ScreeningDecisions",
                type: "TEXT",
                maxLength: 32768,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EvaluatorInputHash",
                table: "ProfileSnapshots",
                type: "TEXT",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "ProfileSnapshots",
                type: "TEXT",
                maxLength: 262144,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedProfileHash",
                table: "ProfileSnapshots",
                type: "TEXT",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttemptCount",
                table: "ProfileSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingClaimExpiresAtUtc",
                table: "ProfileSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingClaimedAtUtc",
                table: "ProfileSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingWorkerId",
                table: "ProfileSnapshots",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawContentHash",
                table: "ProfileSnapshots",
                type: "TEXT",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RedactionJson",
                table: "ProfileSnapshots",
                type: "TEXT",
                maxLength: 32768,
                nullable: true);

            migrationBuilder.Sql("UPDATE ProfileSnapshots SET RawContentHash = ContentHash, NormalizedProfileJson = NULL, Status = 'Captured' WHERE NormalizedProfileHash IS NULL;");
            migrationBuilder.Sql("UPDATE Candidates SET Status = 'Captured', CurrentSnapshotId = NULL WHERE Status = 'PendingAnalysis' AND LatestEvaluationId IS NULL;");
            migrationBuilder.Sql("UPDATE ScreeningDecisions SET MissingEvidenceJson = '[]', EvaluatorInputJson = '{}', EvaluatorInputHash = '0000000000000000000000000000000000000000000000000000000000000000';");

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoCandidateId",
                table: "Candidates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CandidateIdentityConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConflictingCandidateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AliasType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedValueHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    Resolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<string>(type: "TEXT", nullable: true),
                    ResolutionAction = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateIdentityConflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateIdentityConflicts_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CandidateIdentityConflicts_Candidates_ConflictingCandidateId",
                        column: x => x.ConflictingCandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileSnapshots_ProcessingQueue",
                table: "ProfileSnapshots",
                columns: new[] { "Status", "ProcessingClaimExpiresAtUtc", "CapturedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_MergedIntoCandidateId",
                table: "Candidates",
                column: "MergedIntoCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateIdentityConflicts_Candidates_Hash",
                table: "CandidateIdentityConflicts",
                columns: new[] { "CandidateId", "ConflictingCandidateId", "NormalizedValueHash", "Resolved" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateIdentityConflicts_ConflictingCandidateId",
                table: "CandidateIdentityConflicts",
                column: "ConflictingCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateIdentityConflicts_Open_Created",
                table: "CandidateIdentityConflicts",
                columns: new[] { "Resolved", "CreatedAtUtc", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_Candidates_MergedIntoCandidateId",
                table: "Candidates",
                column: "MergedIntoCandidateId",
                principalTable: "Candidates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_Candidates_MergedIntoCandidateId",
                table: "Candidates");

            migrationBuilder.DropTable(
                name: "CandidateIdentityConflicts");

            migrationBuilder.DropIndex(
                name: "IX_ProfileSnapshots_ProcessingQueue",
                table: "ProfileSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_MergedIntoCandidateId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "EvaluatorInputHash",
                table: "ScreeningDecisions");

            migrationBuilder.DropColumn(
                name: "EvaluatorInputJson",
                table: "ScreeningDecisions");

            migrationBuilder.DropColumn(
                name: "IsManualOverride",
                table: "ScreeningDecisions");

            migrationBuilder.DropColumn(
                name: "MissingEvidenceJson",
                table: "ScreeningDecisions");

            migrationBuilder.DropColumn(
                name: "EvaluatorInputHash",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "NormalizedProfileHash",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "ProcessingAttemptCount",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "ProcessingClaimExpiresAtUtc",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "ProcessingClaimedAtUtc",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "ProcessingWorkerId",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "RawContentHash",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "RedactionJson",
                table: "ProfileSnapshots");

            migrationBuilder.DropColumn(
                name: "MergedIntoCandidateId",
                table: "Candidates");
        }
    }
}
