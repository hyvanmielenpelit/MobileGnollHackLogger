using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParallelToolCallSettingsAndMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxParallelToolCalls",
                table: "UserAiSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionMs",
                table: "ChatMessageToolCall",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueueWaitMs",
                table: "ChatMessageToolCall",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxParallelToolCalls",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ExecutionMs",
                table: "ChatMessageToolCall");

            migrationBuilder.DropColumn(
                name: "QueueWaitMs",
                table: "ChatMessageToolCall");
        }
    }
}
