using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessageSnapshotFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGameSnapshot",
                table: "ChatMessage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMessageHistory",
                table: "ChatMessage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            /* Backfill from the text patterns that detected these messages before the columns
               existed: ChatService.GameSnapshotLikePatterns and
               ChatService.MessageHistoryPrefix. LIKE is evaluated under the database's
               collation, which is case-insensitive by default and therefore agrees with the
               OrdinalIgnoreCase checks it replaces; a case-sensitive collation would backfill
               fewer rows than the old detection found.

               Superseded snapshot markers match neither pattern and so backfill to false,
               which is the state ChatController.AttachSnapshot now maintains explicitly. */
            migrationBuilder.Sql(@"
UPDATE [ChatMessage]
SET [IsGameSnapshot] = 1
WHERE [Role] = 'system'
  AND ([Content] LIKE 'Game Snapshot%' OR [Content] LIKE 'Game Context Snapshot%');");

            migrationBuilder.Sql(@"
UPDATE [ChatMessage]
SET [IsMessageHistory] = 1
WHERE [Role] = 'system'
  AND [Content] LIKE 'Full Message History%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGameSnapshot",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "IsMessageHistory",
                table: "ChatMessage");
        }
    }
}
