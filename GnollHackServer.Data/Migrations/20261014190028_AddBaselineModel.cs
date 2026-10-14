using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBaselineModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferenceEntryKey",
                table: "BenchmarkReportDocuments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BenchmarkBaselineModelSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModelSnapshotId = table.Column<long>(type: "bigint", nullable: true),
                    SourceConfigurationId = table.Column<long>(type: "bigint", nullable: true),
                    SetAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SetByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkBaselineModelSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkBaselineModelSettings_SystemAiConfigurationSnapshots_ModelSnapshotId",
                        column: x => x.ModelSnapshotId,
                        principalTable: "SystemAiConfigurationSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkBaselineModelSettings_ModelSnapshotId",
                table: "BenchmarkBaselineModelSettings",
                column: "ModelSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkBaselineModelSettings");

            migrationBuilder.DropColumn(
                name: "ReferenceEntryKey",
                table: "BenchmarkReportDocuments");
        }
    }
}
