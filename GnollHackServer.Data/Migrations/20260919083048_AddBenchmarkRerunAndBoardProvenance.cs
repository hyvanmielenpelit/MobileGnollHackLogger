using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkRerunAndBoardProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameSnapshotFormatVersionUsed",
                table: "BenchmarkRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RerunCandidateDeliveryVerifiedAtUtc",
                table: "BenchmarkRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RerunAtUtc",
                table: "BenchmarkRunAnswers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RerunOfErrorMessage",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RerunOfStatus",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoardHeaderTimestamp",
                table: "BenchmarkGameSnapshots",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotFormatVersion",
                table: "BenchmarkGameSnapshots",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GameSnapshotFormatVersionUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RerunCandidateDeliveryVerifiedAtUtc",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RerunAtUtc",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "RerunOfErrorMessage",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "RerunOfStatus",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "BoardHeaderTimestamp",
                table: "BenchmarkGameSnapshots");

            migrationBuilder.DropColumn(
                name: "SnapshotFormatVersion",
                table: "BenchmarkGameSnapshots");
        }
    }
}
