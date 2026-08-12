using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReasoningModeAndSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReasoningMode",
                table: "UserAiModels",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningSummary",
                table: "UserAiModels",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningMode",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningSummary",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReasoningMode",
                table: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "ReasoningSummary",
                table: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "ReasoningMode",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "ReasoningSummary",
                table: "SystemAiApiConfigurations");
        }
    }
}
