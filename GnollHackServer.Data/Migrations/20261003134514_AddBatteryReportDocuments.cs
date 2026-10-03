using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBatteryReportDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReportDocumentsMessage",
                table: "BenchmarkBatteryRuns",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportDocumentsStatus",
                table: "BenchmarkBatteryRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ReportWriterModelConfigurationId",
                table: "BenchmarkBatteryRuns",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportDocumentsMessage",
                table: "BenchmarkBatteryRuns");

            migrationBuilder.DropColumn(
                name: "ReportDocumentsStatus",
                table: "BenchmarkBatteryRuns");

            migrationBuilder.DropColumn(
                name: "ReportWriterModelConfigurationId",
                table: "BenchmarkBatteryRuns");
        }
    }
}
