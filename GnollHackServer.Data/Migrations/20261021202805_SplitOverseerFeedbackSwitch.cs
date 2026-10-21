using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitOverseerFeedbackSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Target",
                table: "OverseerFeedbackSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_OverseerFeedbackSettings_Target_Id",
                table: "OverseerFeedbackSettings",
                columns: new[] { "Target", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OverseerFeedbackSettings_Target_Id",
                table: "OverseerFeedbackSettings");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "OverseerFeedbackSettings");
        }
    }
}
