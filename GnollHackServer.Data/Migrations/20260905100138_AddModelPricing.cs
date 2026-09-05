using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowChatCost",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CachedInputPricePerMillion",
                table: "UserAiModels",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InputPricePerMillion",
                table: "UserAiModels",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OutputPricePerMillion",
                table: "UserAiModels",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingMode",
                table: "UserAiModels",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CachedInputPricePerMillion",
                table: "SystemAiApiConfigurations",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InputPricePerMillion",
                table: "SystemAiApiConfigurations",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OutputPricePerMillion",
                table: "SystemAiApiConfigurations",
                type: "decimal(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingMode",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheCreationTokens",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadTokens",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCurrency",
                table: "ChatMessage",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                table: "ChatMessage",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingSource",
                table: "ChatMessage",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingSnapshotJson",
                table: "BenchmarkRuns",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowChatCost",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "CachedInputPricePerMillion",
                table: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "InputPricePerMillion",
                table: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "OutputPricePerMillion",
                table: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "PricingMode",
                table: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "CachedInputPricePerMillion",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "InputPricePerMillion",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "OutputPricePerMillion",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "PricingMode",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "CacheCreationTokens",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "CacheReadTokens",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "CostCurrency",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "PricingSource",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "PricingSnapshotJson",
                table: "BenchmarkRuns");
        }
    }
}
