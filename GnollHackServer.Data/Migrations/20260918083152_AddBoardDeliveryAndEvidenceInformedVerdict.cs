using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardDeliveryAndEvidenceInformedVerdict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CandidateDeliveryVerifiedAtUtc",
                table: "BenchmarkRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssessorBoardChars",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EvidenceInformedCriticalError",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceInformedJson",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvidenceInformedQualityScore",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondOpinionBoardChars",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerifierBoardChars",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CandidateDeliveryVerifiedAtUtc",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "AssessorBoardChars",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "EvidenceInformedCriticalError",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "EvidenceInformedJson",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "EvidenceInformedQualityScore",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "SecondOpinionBoardChars",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "VerifierBoardChars",
                table: "BenchmarkRunAnswers");
        }
    }
}
