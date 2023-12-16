using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class Bones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bones",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DifficultyLevel = table.Column<int>(type: "int", nullable: false),
                    BonesFilePath = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PlatformVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Port = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PortVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PortBuild = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    VersionNumber = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    VersionCompatibilityNumber = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "getutcdate()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bones_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bones_AspNetUserId",
                table: "Bones",
                column: "AspNetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bones");
        }
    }
}
