using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class GameLogTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Discriminator",
                table: "AspNetUsers",
                type: "nvarchar(21)",
                maxLength: 21,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "GameLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AspNetUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EditLevel = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<long>(type: "bigint", nullable: false),
                    DeathDungeonNumber = table.Column<int>(type: "int", nullable: false),
                    DeathLevel = table.Column<int>(type: "int", nullable: false),
                    MaxLevel = table.Column<int>(type: "int", nullable: false),
                    HitPoints = table.Column<int>(type: "int", nullable: false),
                    MaxHitPoints = table.Column<int>(type: "int", nullable: false),
                    Deaths = table.Column<int>(type: "int", nullable: false),
                    DeathDateText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BirthDateText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessUserID = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Race = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Alignment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CharacterName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeathText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WhileText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConductsBinary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Turns = table.Column<int>(type: "int", nullable: false),
                    AchievementsBinary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AchievementsText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConductsText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RealTime = table.Column<long>(type: "bigint", nullable: false),
                    StartTime = table.Column<long>(type: "bigint", nullable: false),
                    EndTime = table.Column<long>(type: "bigint", nullable: false),
                    StartingGender = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartingAlignment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FlagsBinary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Scoring = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DungeonCollapses = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameLog_AspNetUsers_AspNetUserId",
                        column: x => x.AspNetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameLog_AspNetUserId",
                table: "GameLog",
                column: "AspNetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameLog");

            migrationBuilder.DropColumn(
                name: "Discriminator",
                table: "AspNetUsers");
        }
    }
}
