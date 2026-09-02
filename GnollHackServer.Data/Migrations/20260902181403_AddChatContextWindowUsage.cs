using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatContextWindowUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage");

            migrationBuilder.AddColumn<int>(
                name: "ContextInputLimitTokens",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContextOutputTokens",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContextPromptTokens",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContextWindowTokens",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage",
                columns: new[] { "ChatSessionId", "TimestampUtc" })
                .Annotation("SqlServer:Include", new[] { "Role", "IsHidden", "ModelUsed", "ProviderUsed", "TimeToFirstTokenMs", "TotalDurationMs", "ContextPromptTokens", "ContextOutputTokens", "ContextWindowTokens", "ContextInputLimitTokens" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "ContextInputLimitTokens",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "ContextOutputTokens",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "ContextPromptTokens",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "ContextWindowTokens",
                table: "ChatMessage");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage",
                columns: new[] { "ChatSessionId", "TimestampUtc" })
                .Annotation("SqlServer:Include", new[] { "Role", "IsHidden", "ModelUsed", "ProviderUsed", "TimeToFirstTokenMs", "TotalDurationMs" });
        }
    }
}
