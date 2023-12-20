using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequestUserNameAndAntiForgeryToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestAntiForgeryToken",
                table: "Logs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestUserName",
                table: "Logs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestAntiForgeryToken",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "RequestUserName",
                table: "Logs");
        }
    }
}
