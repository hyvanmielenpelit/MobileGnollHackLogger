using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelRoleAndTitleSystemModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ModelRole",
                table: "UserSystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "TitleGenerationSystemModelId",
                table: "UserAiSettings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "SystemAiApiConfigurations",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "ModelRole",
                table: "SystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ModelRole",
                table: "GroupSystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelRole",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TitleGenerationSystemModelId",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "ModelRole",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "ModelRole",
                table: "GroupSystemAiApiConfigurations");
        }
    }
}
