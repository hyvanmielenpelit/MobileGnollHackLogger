using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomProviderEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiVersion",
                table: "UserAiApiKeys",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUrl",
                table: "UserAiApiKeys",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomHeadersJson",
                table: "UserAiApiKeys",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiVersion",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUrl",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomHeadersJson",
                table: "SystemAiApiConfigurations",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiVersion",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "BaseUrl",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "CustomHeadersJson",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "ApiVersion",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "BaseUrl",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "CustomHeadersJson",
                table: "SystemAiApiConfigurations");
        }
    }
}
