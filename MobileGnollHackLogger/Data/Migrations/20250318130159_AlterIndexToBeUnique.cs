using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlterIndexToBeUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SaveFileTrackings_TimeStamp_AspNetUserId",
                table: "SaveFileTrackings");

            migrationBuilder.CreateIndex(
                name: "IX_SaveFileTrackings_TimeStamp_AspNetUserId",
                table: "SaveFileTrackings",
                columns: new[] { "TimeStamp", "AspNetUserId" },
                unique: true,
                filter: "[AspNetUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SaveFileTrackings_TimeStamp_AspNetUserId",
                table: "SaveFileTrackings");

            migrationBuilder.CreateIndex(
                name: "IX_SaveFileTrackings_TimeStamp_AspNetUserId",
                table: "SaveFileTrackings",
                columns: new[] { "TimeStamp", "AspNetUserId" });
        }
    }
}
