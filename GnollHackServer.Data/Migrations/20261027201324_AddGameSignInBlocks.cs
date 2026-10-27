using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSignInBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameSignInBlocks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<short>(type: "smallint", nullable: false),
                    Kind = table.Column<short>(type: "smallint", nullable: false),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    BlockedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BlockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClearedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClearedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSignInBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSignInBlocks_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameSignInBlocks_AspNetUserId_Source_Kind",
                table: "GameSignInBlocks",
                columns: new[] { "AspNetUserId", "Source", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSignInBlocks");
        }
    }
}
