using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDlpIbanAndPasswordMasking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskIbans",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskPasswords",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DlpMaskIbans",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DlpMaskPasswords",
                table: "UserAiSettings");
        }
    }
}
