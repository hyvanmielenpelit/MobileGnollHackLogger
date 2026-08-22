using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatTotalDurationMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage");

            migrationBuilder.AddColumn<int>(
                name: "TotalDurationMs",
                table: "ChatMessage",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage",
                columns: new[] { "ChatSessionId", "TimestampUtc" })
                .Annotation("SqlServer:Include", new[] { "Role", "IsHidden", "ModelUsed", "ProviderUsed", "TimeToFirstTokenMs", "TotalDurationMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "TotalDurationMs",
                table: "ChatMessage");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_ChatSessionId_TimestampUtc",
                table: "ChatMessage",
                columns: new[] { "ChatSessionId", "TimestampUtc" })
                .Annotation("SqlServer:Include", new[] { "Role", "IsHidden", "ModelUsed", "ProviderUsed", "TimeToFirstTokenMs" });
        }
    }
}
