using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkClaimVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClaimVerifiedAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierDisplayNameUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierModelIdUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierProviderUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierReasoningModeUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerifierThinkingLevelUsed",
                table: "BenchmarkRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimsIndeterminateCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClaimsRefutedCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClaimsSupportedCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RefutedClaimAnswerCount",
                table: "BenchmarkRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationDurationMs",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationInputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalClaimVerificationOutputTokens",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerificationByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClaimVerificationDurationMs",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerificationError",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationInputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimVerificationJson",
                table: "BenchmarkRunAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationOutputTokens",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimVerificationToolCallCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimsIndeterminateCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimsRefutedCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimsSupportedCount",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRuns_ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns",
                column: "ClaimVerifierModelConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns",
                column: "ClaimVerifierModelConfigurationId",
                principalTable: "SystemAiApiConfigurations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRuns_SystemAiApiConfigurations_ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRuns_ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifiedAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierDisplayNameUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierModelConfigurationId",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierModelIdUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierProviderUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierReasoningModeUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerifierThinkingLevelUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimsIndeterminateCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimsRefutedCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimsSupportedCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "RefutedClaimAnswerCount",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationDurationMs",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationInputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "TotalClaimVerificationOutputTokens",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationByModelDisplayNameUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationDurationMs",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationError",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationInputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationJson",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationOutputTokens",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimVerificationToolCallCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimsIndeterminateCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimsRefutedCount",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ClaimsSupportedCount",
                table: "BenchmarkRunAnswers");
        }
    }
}
