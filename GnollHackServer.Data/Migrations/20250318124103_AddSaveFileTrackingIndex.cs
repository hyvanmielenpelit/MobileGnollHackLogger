using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSaveFileTrackingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SaveFileTrackings_TimeStamp_AspNetUserId",
                table: "SaveFileTrackings",
                columns: new[] { "TimeStamp", "AspNetUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SaveFileTrackings_TimeStamp_AspNetUserId",
                table: "SaveFileTrackings");
        }
    }
}
