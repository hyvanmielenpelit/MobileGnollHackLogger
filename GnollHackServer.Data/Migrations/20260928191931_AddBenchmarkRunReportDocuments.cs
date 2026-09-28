using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkRunReportDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReportDocumentsMessage",
                table: "BenchmarkRuns",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportDocumentsStatus",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ReportWriterModelConfigurationId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "BenchmarkReportDocuments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkReportDocuments_SubjectKey_Origin",
                table: "BenchmarkReportDocuments",
                columns: new[] { "SubjectKey", "Origin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BenchmarkReportDocuments_SubjectKey_Origin",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropColumn(
                name: "ReportDocumentsMessage",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ReportDocumentsStatus",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ReportWriterModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "BenchmarkReportDocuments");
        }
    }
}
