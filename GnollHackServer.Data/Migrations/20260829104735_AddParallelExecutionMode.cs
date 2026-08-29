using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParallelExecutionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowParallelBadge",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ParallelExecutionMode",
                table: "UserAiApiKeys",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "ParallelExecutionMode",
                table: "SystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowParallelBadge",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ParallelExecutionMode",
                table: "UserAiApiKeys");

            migrationBuilder.DropColumn(
                name: "ParallelExecutionMode",
                table: "SystemAiApiConfigurations");
        }
    }
}
