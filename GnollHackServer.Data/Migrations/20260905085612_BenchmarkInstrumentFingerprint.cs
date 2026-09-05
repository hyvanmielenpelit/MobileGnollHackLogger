using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class BenchmarkInstrumentFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CandidateSystemPromptSha256",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CandidateSystemPromptText",
                table: "BenchmarkRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KnowledgeBaseHeadSha",
                table: "BenchmarkRuns",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolGuidesSha256",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CandidateSystemPromptSha256",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "CandidateSystemPromptText",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "KnowledgeBaseHeadSha",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ToolGuidesSha256",
                table: "BenchmarkRuns");
        }
    }
}
