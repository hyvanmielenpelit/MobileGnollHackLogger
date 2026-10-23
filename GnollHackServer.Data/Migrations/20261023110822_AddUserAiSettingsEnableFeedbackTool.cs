using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAiSettingsEnableFeedbackTool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableFeedbackTool",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                // Existing rows must come out enabled: the setting defaults to on, and false here
                // would withhold the tool from every user who never opened the page.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableFeedbackTool",
                table: "UserAiSettings");
        }
    }
}
