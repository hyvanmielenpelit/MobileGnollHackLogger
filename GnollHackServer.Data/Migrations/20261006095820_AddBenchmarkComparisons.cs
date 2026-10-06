using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ComparisonId",
                table: "BenchmarkReportDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoveredEntryKeysJson",
                table: "BenchmarkReportDocuments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoveredSetKey",
                table: "BenchmarkReportDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "BenchmarkReportDocuments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "BenchmarkComparisons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComparisonKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntryKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubjectKind = table.Column<int>(type: "int", nullable: false),
                    EntryCount = table.Column<int>(type: "int", nullable: false),
                    DefaultName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RenamedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkComparisons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkReportDocuments_ComparisonId_Scope_CoveredSetKey_Audience",
                table: "BenchmarkReportDocuments",
                columns: new[] { "ComparisonId", "Scope", "CoveredSetKey", "Audience" });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkComparisons_ComparisonKey",
                table: "BenchmarkComparisons",
                column: "ComparisonKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkReportDocuments_BenchmarkComparisons_ComparisonId",
                table: "BenchmarkReportDocuments",
                column: "ComparisonId",
                principalTable: "BenchmarkComparisons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkReportDocuments_BenchmarkComparisons_ComparisonId",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropTable(
                name: "BenchmarkComparisons");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkReportDocuments_ComparisonId_Scope_CoveredSetKey_Audience",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropColumn(
                name: "ComparisonId",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropColumn(
                name: "CoveredEntryKeysJson",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropColumn(
                name: "CoveredSetKey",
                table: "BenchmarkReportDocuments");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "BenchmarkReportDocuments");
        }
    }
}
