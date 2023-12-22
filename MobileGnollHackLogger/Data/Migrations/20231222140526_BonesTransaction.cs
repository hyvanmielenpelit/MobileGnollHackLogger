using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class BonesTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BonesTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "getutcdate()"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    BonesId = table.Column<long>(type: "bigint", nullable: false),
                    DifficultyLevel = table.Column<int>(type: "int", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PlatformVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Port = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PortVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PortBuild = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    VersionNumber = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    VersionCompatibilityNumber = table.Column<decimal>(type: "decimal(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BonesTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BonesTransactions_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BonesTransactions_AspNetUserId",
                table: "BonesTransactions",
                column: "AspNetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BonesTransactions");
        }
    }
}
