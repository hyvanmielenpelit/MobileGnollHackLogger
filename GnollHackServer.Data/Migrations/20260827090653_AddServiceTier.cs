using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServiceTier",
                table: "UserAiModels",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceTier",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceTierUsed",
                table: "ChatMessage",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServiceTier",
                table: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "ServiceTier",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "ServiceTierUsed",
                table: "ChatMessage");
        }
    }
}
