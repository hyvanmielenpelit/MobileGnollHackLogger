using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SnapshotRefusalCode",
                table: "ChatSession",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SnapshotRefusedUtc",
                table: "ChatSession",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SnapshotGuardSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SetByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotGuardSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SnapshotGuardSettings");

            migrationBuilder.DropColumn(
                name: "SnapshotRefusalCode",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "SnapshotRefusedUtc",
                table: "ChatSession");
        }
    }
}
