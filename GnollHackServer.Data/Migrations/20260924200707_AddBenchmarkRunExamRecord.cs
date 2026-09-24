using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkRunExamRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BoardSnapshotId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultSuiteVersionUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExpectedPointsRecorded",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedPointsUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BenchmarkRunBoardSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sha256 = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
                    SanitizedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DigestText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CharCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRunBoardSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_BoardSnapshotId",
                table: "BenchmarkRuns",
                column: "BoardSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunBoardSnapshots_Sha256",
                table: "BenchmarkRunBoardSnapshots",
                column: "Sha256",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_BenchmarkRunBoardSnapshots_BoardSnapshotId",
                table: "BenchmarkRuns",
                column: "BoardSnapshotId",
                principalTable: "BenchmarkRunBoardSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(BackfillSql);
        }

        // One batch, run inside the migration's transaction: the THROW rolls the whole migration back.
        // The board key matches BenchmarkRunBoardSnapshotStore: lower-case hex SHA-256 of the UTF-16LE
        // bytes of SanitizedText + NCHAR(0) + ISNULL(DigestText, N'').
        //
        // Rubric: recorded only where the question still exists at the revision the answer was made
        // against. Every write to a question's text, band or rubric bumps ItemRevision, so an equal
        // revision means an equal rubric. Board: any live board whose hash a run recorded still holds
        // that run's text.
        private const string BackfillSql = @"
SET NOCOUNT ON;

UPDATE a SET a.ExpectedPointsUsed = q.ExpectedPoints, a.ExpectedPointsRecorded = 1
FROM BenchmarkRunAnswers a
JOIN BenchmarkQuestions q ON q.Id = a.BenchmarkQuestionId
WHERE a.ItemRevisionUsed IS NOT NULL AND a.ItemRevisionUsed = q.ItemRevision;

INSERT INTO BenchmarkRunBoardSnapshots (Sha256, SanitizedText, DigestText, CharCount, CreatedAtUtc)
SELECT x.KeyHex, x.SanitizedText, x.DigestText, x.CharCount, SYSUTCDATETIME()
FROM (
    SELECT k.KeyHex, g.SanitizedText, g.DigestText, g.CharCount,
           ROW_NUMBER() OVER (PARTITION BY k.KeyHex ORDER BY g.Id) AS rn
    FROM BenchmarkGameSnapshots g
    CROSS APPLY (SELECT LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256',
        CAST(CAST(g.SanitizedText AS nvarchar(max)) + NCHAR(0) + ISNULL(g.DigestText, N'') AS varbinary(max))), 2)) AS KeyHex) k
    WHERE EXISTS (SELECT 1 FROM BenchmarkRuns r WHERE r.GameSnapshotSha256Used = g.Sha256)
) x
WHERE x.rn = 1
  AND NOT EXISTS (SELECT 1 FROM BenchmarkRunBoardSnapshots s WHERE s.Sha256 = x.KeyHex);

UPDATE r SET r.BoardSnapshotId = (
    SELECT TOP 1 s.Id FROM BenchmarkGameSnapshots g
    CROSS APPLY (SELECT LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256',
        CAST(CAST(g.SanitizedText AS nvarchar(max)) + NCHAR(0) + ISNULL(g.DigestText, N'') AS varbinary(max))), 2)) AS KeyHex) k
    JOIN BenchmarkRunBoardSnapshots s ON s.Sha256 = k.KeyHex
    WHERE g.Sha256 = r.GameSnapshotSha256Used ORDER BY g.Id)
FROM BenchmarkRuns r
WHERE r.GameSnapshotSha256Used IS NOT NULL AND r.BoardSnapshotId IS NULL;

IF EXISTS (SELECT 1 FROM BenchmarkRuns r
           WHERE r.GameSnapshotSha256Used IS NOT NULL AND r.BoardSnapshotId IS NULL
             AND EXISTS (SELECT 1 FROM BenchmarkGameSnapshots g WHERE g.Sha256 = r.GameSnapshotSha256Used))
    THROW 50001, 'AddBenchmarkRunExamRecord: a run with a live board was left without a board record.', 1;
";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_BenchmarkRunBoardSnapshots_BoardSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropTable(
                name: "BenchmarkRunBoardSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_BoardSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "BoardSnapshotId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "DefaultSuiteVersionUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ExpectedPointsRecorded",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ExpectedPointsUsed",
                table: "BenchmarkRunAnswers");
        }
    }
}
