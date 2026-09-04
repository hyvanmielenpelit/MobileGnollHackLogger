using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkSnapshotSourceChatSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceChatSessionId",
                table: "BenchmarkGameSnapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkGameSnapshots_SourceChatSessionId",
                table: "BenchmarkGameSnapshots",
                column: "SourceChatSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkGameSnapshots_ChatSession_SourceChatSessionId",
                table: "BenchmarkGameSnapshots",
                column: "SourceChatSessionId",
                principalTable: "ChatSession",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkGameSnapshots_ChatSession_SourceChatSessionId",
                table: "BenchmarkGameSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkGameSnapshots_SourceChatSessionId",
                table: "BenchmarkGameSnapshots");

            migrationBuilder.DropColumn(
                name: "SourceChatSessionId",
                table: "BenchmarkGameSnapshots");
        }
    }
}
