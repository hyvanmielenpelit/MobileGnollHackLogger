using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGameLogIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameLog_ByteStart_ByteEnd_ByteLength",
                table: "GameLog");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GameLog_ByteStart_ByteEnd_ByteLength",
                table: "GameLog",
                columns: new[] { "ByteStart", "ByteEnd", "ByteLength" });
        }
    }
}
