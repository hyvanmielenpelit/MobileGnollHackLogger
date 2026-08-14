using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiModelSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowMultipleModels",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ModelUsed",
                table: "ChatMessage",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderUsed",
                table: "ChatMessage",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThinkingLevelUsed",
                table: "ChatMessage",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserAiApiKeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ApiKeyNonce = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ApiKeyTag = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAiApiKeys_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAiModels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ThinkingLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAiModels_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAiApiKeys_AspNetUserId_Provider",
                table: "UserAiApiKeys",
                columns: new[] { "AspNetUserId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAiModels_AspNetUserId",
                table: "UserAiModels",
                column: "AspNetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAiApiKeys");

            migrationBuilder.DropTable(
                name: "UserAiModels");

            migrationBuilder.DropColumn(
                name: "AllowMultipleModels",
                table: "UserAiSettings");

            migrationBuilder.DropColumn(
                name: "ModelUsed",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "ProviderUsed",
                table: "ChatMessage");

            migrationBuilder.DropColumn(
                name: "ThinkingLevelUsed",
                table: "ChatMessage");
        }
    }
}
