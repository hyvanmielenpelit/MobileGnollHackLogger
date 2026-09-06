using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatCostAttributionAndTieredPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalUserEstimatedCost",
                table: "ChatSession",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SystemAiConfigurationIdUsed",
                table: "ChatMessage",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalLongContextCacheCreationTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalLongContextCacheReadTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalLongContextInputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalLongContextOutputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "LongContextCacheCreationTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LongContextCacheReadTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LongContextInputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LongContextOutputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            // D1 option A: nothing recorded until now distinguishes an operator-funded reply from a user-funded
            // one, so every existing session is treated as entirely user-funded and displays exactly as it did
            // before this migration. The new rule applies from here forward. Replies saved before this point
            // therefore still show operator cost to a regular user — a known, accepted limitation, recorded here,
            // in ChatService, and in chat.service.ts.
            migrationBuilder.Sql(
                "UPDATE [ChatSession] SET [TotalUserEstimatedCost] = [TotalEstimatedCost] " +
                "WHERE [TotalEstimatedCost] IS NOT NULL;");

            // The eight long-context columns need no backfill: null (per answer) and zero (per run) are
            // already the correct reading of "recorded before tiered pricing existed".
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalUserEstimatedCost",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "SystemAiConfigurationIdUsed",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "TotalLongContextCacheCreationTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalLongContextCacheReadTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalLongContextInputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalLongContextOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "LongContextCacheCreationTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "LongContextCacheReadTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "LongContextInputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "LongContextOutputTokens",
                table: "BenchmarkRunAnswers");
        }
    }
}
