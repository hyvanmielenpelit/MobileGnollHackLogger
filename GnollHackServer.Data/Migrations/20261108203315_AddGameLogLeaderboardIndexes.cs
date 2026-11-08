using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGameLogLeaderboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GameLog_CreatedDate",
                table: "GameLog",
                column: "CreatedDate")
                .Annotation("SqlServer:DataCompression", DataCompressionType.Page)
                .Annotation("SqlServer:Include", new[] { "AspNetUserId", "Mode", "Scoring", "Turns", "Role", "Difficulty", "DeathText" })
                .Annotation("SqlServer:SortInTempDb", true);

            migrationBuilder.CreateIndex(
                name: "IX_GameLog_EndTimeUTC",
                table: "GameLog",
                column: "EndTimeUTC")
                .Annotation("SqlServer:DataCompression", DataCompressionType.Page)
                .Annotation("SqlServer:Include", new[] { "AspNetUserId", "Mode", "DeathText" })
                .Annotation("SqlServer:SortInTempDb", true);

            migrationBuilder.CreateIndex(
                name: "IX_GameLog_Points",
                table: "GameLog",
                column: "Points")
                .Annotation("SqlServer:DataCompression", DataCompressionType.Page)
                .Annotation("SqlServer:Include", new[] { "AspNetUserId", "Scoring", "Mode", "DeathText" })
                .Annotation("SqlServer:SortInTempDb", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameLog_CreatedDate",
                table: "GameLog");

            migrationBuilder.DropIndex(
                name: "IX_GameLog_EndTimeUTC",
                table: "GameLog");

            migrationBuilder.DropIndex(
                name: "IX_GameLog_Points",
                table: "GameLog");
        }
    }
}
