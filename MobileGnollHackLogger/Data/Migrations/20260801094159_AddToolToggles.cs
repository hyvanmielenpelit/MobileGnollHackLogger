using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddToolToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableClientTools",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableGameActions",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableToolUse",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableWebSearch",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableClientTools",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "EnableGameActions",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "EnableToolUse",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "EnableWebSearch",
                table: "UserAiSettings");
        }
    }
}
