using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyFailureAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBudgetExhausted",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "LastBudgetNotificationSentUtc",
                table: "SystemAiApiConfigurations");

            migrationBuilder.CreateTable(
                name: "ApiKeyFailureAlertStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KeyFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FirstOccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastOccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEmailSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OccurrencesSinceLastEmail = table.Column<int>(type: "int", nullable: false),
                    LastFailureKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LastSystemAiApiConfigurationId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyFailureAlertStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyFailureAlertStates_KeyFingerprint",
                table: "ApiKeyFailureAlertStates",
                column: "KeyFingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeyFailureAlertStates");

            migrationBuilder.AddColumn<bool>(
                name: "IsBudgetExhausted",
                table: "SystemAiApiConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastBudgetNotificationSentUtc",
                table: "SystemAiApiConfigurations",
                type: "datetime2",
                nullable: true);
        }
    }
}
