using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkToolCallRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BenchmarkRunAnswerToolCalls",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BenchmarkRunAnswerId = table.Column<long>(type: "bigint", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IterationIndex = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ToolCallId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ArgsText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QueueWaitMs = table.Column<int>(type: "int", nullable: true),
                    ExecutionMs = table.Column<int>(type: "int", nullable: true),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    AgentName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ArgsTruncated = table.Column<bool>(type: "bit", nullable: false),
                    ResultTruncated = table.Column<bool>(type: "bit", nullable: false),
                    ResultLengthChars = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenchmarkRunAnswerToolCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenchmarkRunAnswerToolCalls_BenchmarkRunAnswers_BenchmarkRunAnswerId",
                        column: x => x.BenchmarkRunAnswerId,
                        principalTable: "BenchmarkRunAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswerToolCalls_BenchmarkRunAnswerId_SortOrder",
                table: "BenchmarkRunAnswerToolCalls",
                columns: new[] { "BenchmarkRunAnswerId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenchmarkRunAnswerToolCalls");
        }
    }
}
