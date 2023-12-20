using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequestPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestPath",
                table: "Logs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestPath",
                table: "Logs");
        }
    }
}
