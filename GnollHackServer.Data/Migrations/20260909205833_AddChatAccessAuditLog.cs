using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAccessAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatAccessAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ActorWasAdmin = table.Column<bool>(type: "bit", nullable: false),
                    SubjectUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChatSessionId = table.Column<long>(type: "bigint", nullable: true),
                    ChatMessageAttachmentId = table.Column<long>(type: "bigint", nullable: true),
                    SessionRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WasConfidential = table.Column<bool>(type: "bit", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAccessAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAccessAuditLogs_ActorUserId_OccurredUtc",
                table: "ChatAccessAuditLogs",
                columns: new[] { "ActorUserId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAccessAuditLogs_ChatSessionId_OccurredUtc",
                table: "ChatAccessAuditLogs",
                columns: new[] { "ChatSessionId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAccessAuditLogs_OccurredUtc",
                table: "ChatAccessAuditLogs",
                column: "OccurredUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatAccessAuditLogs");
        }
    }
}
