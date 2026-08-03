using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiPerformanceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiKeyNonce",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ApiKeyTag",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DefaultModel",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DefaultProvider",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "EncryptedApiKey",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ThinkingLevel",
                table: "UserAiSettings");

            migrationBuilder.RenameColumn(
                name: "MaxOutputTokens",
                table: "UserAiSettings",
                newName: "MaxToolIterations");

            migrationBuilder.RenameColumn(
                name: "MaxInputTokens",
                table: "UserAiSettings",
                newName: "MaxResultLength");

            migrationBuilder.AddColumn<int>(
                name: "MaxCallsPerSession",
                table: "UserAiSettings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxCallsPerSession",
                table: "UserAiSettings");

            migrationBuilder.RenameColumn(
                name: "MaxToolIterations",
                table: "UserAiSettings",
                newName: "MaxOutputTokens");

            migrationBuilder.RenameColumn(
                name: "MaxResultLength",
                table: "UserAiSettings",
                newName: "MaxInputTokens");

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyNonce",
                table: "UserAiSettings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyTag",
                table: "UserAiSettings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultModel",
                table: "UserAiSettings",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultProvider",
                table: "UserAiSettings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedApiKey",
                table: "UserAiSettings",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThinkingLevel",
                table: "UserAiSettings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }
    }
}
