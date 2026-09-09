using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkCorpusFingerprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstMemberSourceCodeHeadSha",
                table: "BenchmarkRunSeries",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstMemberWikiHeadSha",
                table: "BenchmarkRunSeries",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceCodeHeadSha",
                table: "BenchmarkRuns",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikiHeadSha",
                table: "BenchmarkRuns",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstMemberSourceCodeHeadSha",
                table: "BenchmarkRunSeries");

            migrationBuilder.DropColumn(
                name: "FirstMemberWikiHeadSha",
                table: "BenchmarkRunSeries");

            migrationBuilder.DropColumn(
                name: "SourceCodeHeadSha",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "WikiHeadSha",
                table: "BenchmarkRuns");
        }
    }
}
