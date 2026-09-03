using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkGameSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "GameSnapshotId",
                table: "BenchmarkSuites",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasGeneratedQuestions",
                table: "BenchmarkSuites",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GameSnapshotCaptureMethodUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GameSnapshotCharCountUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GameSnapshotNameUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GameSnapshotSha256Used",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SuiteQuestionsReviewed",
                table: "BenchmarkRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SuiteReviewedQuestionCountAtStart",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsGenerated",
                table: "BenchmarkQuestions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedAtRevision",
                table: "BenchmarkQuestions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "BenchmarkQuestions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByUserId",
                table: "BenchmarkQuestions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BenchmarkGameSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SanitizedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DigestText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CharCount = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CaptureMethod = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceGnollHackVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkGameSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkSuites_GameSnapshotId",
                table: "BenchmarkSuites",
                column: "GameSnapshotId",
                unique: true,
                filter: "[GameSnapshotId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkGameSnapshots_Name",
                table: "BenchmarkGameSnapshots",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkSuites_BenchmarkGameSnapshots_GameSnapshotId",
                table: "BenchmarkSuites",
                column: "GameSnapshotId",
                principalTable: "BenchmarkGameSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkSuites_BenchmarkGameSnapshots_GameSnapshotId",
                table: "BenchmarkSuites");

            migrationBuilder.DropTable(
                name: "BenchmarkGameSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkSuites_GameSnapshotId",
                table: "BenchmarkSuites");

            migrationBuilder.DropColumn(
                name: "GameSnapshotId",
                table: "BenchmarkSuites");

            migrationBuilder.DropColumn(
                name: "HasGeneratedQuestions",
                table: "BenchmarkSuites");

            migrationBuilder.DropColumn(
                name: "GameSnapshotCaptureMethodUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "GameSnapshotCharCountUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "GameSnapshotNameUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "GameSnapshotSha256Used",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SuiteQuestionsReviewed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "SuiteReviewedQuestionCountAtStart",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "IsGenerated",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "ReviewedAtRevision",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "BenchmarkQuestions");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "BenchmarkQuestions");
        }
    }
}
