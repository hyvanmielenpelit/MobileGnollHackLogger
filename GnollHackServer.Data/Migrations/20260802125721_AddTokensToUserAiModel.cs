using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTokensToUserAiModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxInputTokens",
                table: "UserAiModels",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxOutputTokens",
                table: "UserAiModels",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxInputTokens",
                table: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "MaxOutputTokens",
                table: "UserAiModels");
        }
    }
}
