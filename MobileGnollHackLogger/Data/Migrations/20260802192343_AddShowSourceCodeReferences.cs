using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShowSourceCodeReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowSourceCodeReferences",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowSourceCodeReferences",
                table: "UserAiSettings");
        }
    }
}
