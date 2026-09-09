using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfidentialSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ConfidentialDisablePromptCache",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConfidentialDisableTitleGeneration",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConfidentialDisableToolEgress",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConfidentialFirstUseNoticeAcknowledged",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ConfidentialImmediatePurge",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfidentialModelGate",
                table: "UserAiSettings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfidentialPersistence",
                table: "UserAiSettings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfidentialRetentionDays",
                table: "UserAiSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfidentialPolicyJson",
                table: "ChatSession",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfidentialUpgradedUtc",
                table: "ChatSession",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EffectiveRetentionDays",
                table: "ChatSession",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImmediatePurgeOnDelete",
                table: "ChatSession",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsConfidential",
                table: "ChatSession",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_IsDeleted_IsConfidential_EffectiveRetentionDays_LastMessageUtc",
                table: "ChatSession",
                columns: new[] { "IsDeleted", "IsConfidential", "EffectiveRetentionDays", "LastMessageUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSession_IsDeleted_IsConfidential_EffectiveRetentionDays_LastMessageUtc",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "ConfidentialDisablePromptCache",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ConfidentialDisableTitleGeneration",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ConfidentialDisableToolEgress",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ConfidentialFirstUseNoticeAcknowledged",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ConfidentialImmediatePurge",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ConfidentialModelGate",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ConfidentialPersistence",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ConfidentialRetentionDays",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ConfidentialPolicyJson",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "ConfidentialUpgradedUtc",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "EffectiveRetentionDays",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "ImmediatePurgeOnDelete",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "IsConfidential",
                table: "ChatSession");
        }
    }
}
