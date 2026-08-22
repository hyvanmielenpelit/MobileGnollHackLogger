using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionSoftDeleteAndPinning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSession_AspNetUserId_LastMessageUtc",
                table: "ChatSession");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "ChatSession",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "ChatSession",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ChatSession",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "ChatSession",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_AspNetUserId_IsDeleted_LastMessageUtc",
                table: "ChatSession",
                columns: new[] { "AspNetUserId", "IsDeleted", "LastMessageUtc" },
                descending: new[] { false, false, true })
                .Annotation("SqlServer:Include", new[] { "Title", "IsGnollHackSession", "IsPinned" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_IsDeleted_DeletedUtc",
                table: "ChatSession",
                columns: new[] { "IsDeleted", "DeletedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSession_AspNetUserId_IsDeleted_LastMessageUtc",
                table: "ChatSession");

            migrationBuilder.DropIndex(
                name: "IX_ChatSession_IsDeleted_DeletedUtc",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ChatSession");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "ChatSession");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSession_AspNetUserId_LastMessageUtc",
                table: "ChatSession",
                columns: new[] { "AspNetUserId", "LastMessageUtc" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "Title", "IsGnollHackSession" });
        }
    }
}
