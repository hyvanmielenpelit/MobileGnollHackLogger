using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubAgentToolCallColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentName",
                table: "ChatMessageToolCall",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Depth",
                table: "ChatMessageToolCall",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ParentToolCallId",
                table: "ChatMessageToolCall",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentName",
                table: "ChatMessageToolCall");

            migrationBuilder.DropColumn(
                name: "Depth",
                table: "ChatMessageToolCall");

            migrationBuilder.DropColumn(
                name: "ParentToolCallId",
                table: "ChatMessageToolCall");
        }
    }
}
