using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSystemModelConfidentialTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSystemModelConfidentialTrusts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    UserTrustsForConfidential = table.Column<bool>(type: "bit", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSystemModelConfidentialTrusts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSystemModelConfidentialTrusts_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSystemModelConfidentialTrusts_SystemAiApiConfigurations_SystemAiApiConfigurationId",
                        column: x => x.SystemAiApiConfigurationId,
                        principalTable: "SystemAiApiConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSystemModelConfidentialTrusts_AspNetUserId_SystemAiApiConfigurationId",
                table: "UserSystemModelConfidentialTrusts",
                columns: new[] { "AspNetUserId", "SystemAiApiConfigurationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSystemModelConfidentialTrusts_SystemAiApiConfigurationId",
                table: "UserSystemModelConfidentialTrusts",
                column: "SystemAiApiConfigurationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSystemModelConfidentialTrusts");
        }
    }
}
