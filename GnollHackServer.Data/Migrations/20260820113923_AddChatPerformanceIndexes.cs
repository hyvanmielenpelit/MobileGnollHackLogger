using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSession_AspNetUserId",
                table: "ChatSession");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessageToolCall_ChatMessageId",
                table: "ChatMessageToolCall");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_ChatSessionId",
                table: "ChatMessage");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_AspNetUserId_LastMessageUtc",
                table: "ChatSession",
                columns: new[] { "AspNetUserId", "LastMessageUtc" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "Title", "IsGnollHackSession" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageToolCall_ChatMessageId_SortOrder",
                table: "ChatMessageToolCall",
                columns: new[] { "ChatMessageId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage",
                columns: new[] { "ChatSessionId", "TimestampUtc" })
                .Annotation("SqlServer:Include", new[] { "Role", "IsHidden", "ModelUsed", "ProviderUsed", "TimeToFirstTokenMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSession_AspNetUserId_LastMessageUtc",
                table: "ChatSession");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessageToolCall_ChatMessageId_SortOrder",
                table: "ChatMessageToolCall");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_AspNetUserId",
                table: "ChatSession",
                column: "AspNetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageToolCall_ChatMessageId",
                table: "ChatMessageToolCall",
                column: "ChatMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_ChatSessionId",
                table: "ChatMessage",
                column: "ChatSessionId");
        }
    }
}
