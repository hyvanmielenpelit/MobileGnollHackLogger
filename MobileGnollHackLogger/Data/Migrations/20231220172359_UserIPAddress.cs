using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserIPAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RequestCommand",
                table: "RequestLogs",
                newName: "UserIPAddress");

            migrationBuilder.AddColumn<string>(
                name: "RequestMethod",
                table: "RequestLogs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestMethod",
                table: "RequestLogs");

            migrationBuilder.RenameColumn(
                name: "UserIPAddress",
                table: "RequestLogs",
                newName: "RequestCommand");
        }
    }
}
