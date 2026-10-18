using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTermsAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserTermsAcceptances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DocumentKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AcceptedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ClientVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTermsAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTermsAcceptances_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTermsAcceptances_AspNetUserId_DocumentKey_Version",
                table: "UserTermsAcceptances",
                columns: new[] { "AspNetUserId", "DocumentKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTermsAcceptances_DocumentKey_Version",
                table: "UserTermsAcceptances",
                columns: new[] { "DocumentKey", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserTermsAcceptances");
        }
    }
}
