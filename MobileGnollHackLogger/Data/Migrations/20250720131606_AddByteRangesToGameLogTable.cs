using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddByteRangesToGameLogTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ByteEnd",
                table: "GameLog",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ByteLength",
                table: "GameLog",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ByteStart",
                table: "GameLog",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ByteEnd",
                table: "GameLog");

            migrationBuilder.DropColumn(
                name: "ByteLength",
                table: "GameLog");

            migrationBuilder.DropColumn(
                name: "ByteStart",
                table: "GameLog");
        }
    }
}
