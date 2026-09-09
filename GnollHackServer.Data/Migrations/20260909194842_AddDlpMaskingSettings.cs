using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDlpMaskingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskApiKeys",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskCreditCards",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskEmails",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskPhoneNumbers",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskPrivateKeys",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskSsns",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DlpMaskTokens",
                table: "UserAiSettings",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DlpMaskApiKeys",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DlpMaskCreditCards",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DlpMaskEmails",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DlpMaskPhoneNumbers",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DlpMaskPrivateKeys",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DlpMaskSsns",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "DlpMaskTokens",
                table: "UserAiSettings");
        }
    }
}
