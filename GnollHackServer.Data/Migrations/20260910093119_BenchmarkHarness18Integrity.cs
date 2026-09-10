using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class BenchmarkHarness18Integrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnswerFramingOpenerAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OutOfRubricAccuracyAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RerunCandidateSystemPromptSha256",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RerunCompletedAtUtc",
                table: "BenchmarkRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RerunStartedAtUtc",
                table: "BenchmarkRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RerunToolGuidesSha256",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerFramingOpenerAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "OutOfRubricAccuracyAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RerunCandidateSystemPromptSha256",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RerunCompletedAtUtc",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RerunStartedAtUtc",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RerunToolGuidesSha256",
                table: "BenchmarkRuns");
        }
    }
}
