using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultApiKeysAndKeyVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApiKeyVerification",
                table: "UserAiApiKeys",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApiKeyVerificationCheckedAtUtc",
                table: "UserAiApiKeys",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyVerificationMessage",
                table: "UserAiApiKeys",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseDefaultApiKey",
                table: "SystemAiApiConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SystemDefaultApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ApiKeyNonce = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ApiKeyTag = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    KeyHint = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApiKeyVerification = table.Column<int>(type: "int", nullable: true),
                    ApiKeyVerificationCheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApiKeyVerificationMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemDefaultApiKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemDefaultApiKeys_Provider",
                table: "SystemDefaultApiKeys",
                column: "Provider",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemDefaultApiKeys");

            migrationBuilder.DropColumn(
                name: "ApiKeyVerification",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "ApiKeyVerificationCheckedAtUtc",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "ApiKeyVerificationMessage",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "UseDefaultApiKey",
                table: "SystemAiApiConfigurations");
        }
    }
}
