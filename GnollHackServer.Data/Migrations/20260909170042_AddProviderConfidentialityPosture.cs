using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderConfidentialityPosture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfidentialTrustDecidedUtc",
                table: "UserAiApiKeys",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfidentialityNote",
                table: "UserAiApiKeys",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfidentialityPosture",
                table: "UserAiApiKeys",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostureDeclaredUtc",
                table: "UserAiApiKeys",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UserTrustsForConfidential",
                table: "UserAiApiKeys",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfidentialityNote",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfidentialityPosture",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataRegion",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostureAgreementRef",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostureVerifiedUtc",
                table: "SystemAiApiConfigurations",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfidentialTrustDecidedUtc",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "ConfidentialityNote",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "ConfidentialityPosture",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "PostureDeclaredUtc",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "UserTrustsForConfidential",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "ConfidentialityNote",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "ConfidentialityPosture",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DataRegion",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "PostureAgreementRef",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "PostureVerifiedUtc",
                table: "SystemAiApiConfigurations");
        }
    }
}
