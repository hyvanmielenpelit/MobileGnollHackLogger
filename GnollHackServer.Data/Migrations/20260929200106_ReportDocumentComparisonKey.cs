using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportDocumentComparisonKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComparisonKey",
                table: "BenchmarkReportDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPeer",
                table: "BenchmarkReportDocumentRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkReportDocuments_ComparisonKey_Origin_CreatedAtUtc",
                table: "BenchmarkReportDocuments",
                columns: new[] { "ComparisonKey", "Origin", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BenchmarkReportDocuments_ComparisonKey_Origin_CreatedAtUtc",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropColumn(
                name: "ComparisonKey",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropColumn(
                name: "IsPeer",
                table: "BenchmarkReportDocumentRuns");
        }
    }
}
