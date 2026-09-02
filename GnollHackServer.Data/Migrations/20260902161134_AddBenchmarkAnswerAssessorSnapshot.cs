using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkAnswerAssessorSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssessedAtUtc",
                table: "BenchmarkRunAnswers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedByModelIdUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessedByModelProviderUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers",
                column: "AssessedByModelConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiApiConfigurations_AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers",
                column: "AssessedByModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_SystemAiApiConfigurations_AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedAtUtc",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedByModelConfigurationId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedByModelIdUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "AssessedByModelProviderUsed",
                table: "BenchmarkRunAnswers");
        }
    }
}
