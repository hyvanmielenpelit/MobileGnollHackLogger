using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotGuardQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SnapshotGuardQuarantines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SecurityEventId = table.Column<long>(type: "bigint", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TextKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Codes = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PlayerAuthored = table.Column<bool>(type: "bit", nullable: false),
                    Withheld = table.Column<bool>(type: "bit", nullable: false),
                    SessionMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SessionRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PlainLength = table.Column<int>(type: "int", nullable: false),
                    StoredLength = table.Column<int>(type: "int", nullable: false),
                    Truncated = table.Column<bool>(type: "bit", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Ciphertext = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotGuardQuarantines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SnapshotGuardQuarantines_SecurityEvents_SecurityEventId",
                        column: x => x.SecurityEventId,
                        principalTable: "SecurityEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotGuardQuarantines_AspNetUserId_OccurredUtc",
                table: "SnapshotGuardQuarantines",
                columns: new[] { "AspNetUserId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotGuardQuarantines_OccurredUtc",
                table: "SnapshotGuardQuarantines",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotGuardQuarantines_SecurityEventId",
                table: "SnapshotGuardQuarantines",
                column: "SecurityEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SnapshotGuardQuarantines");
        }
    }
}
