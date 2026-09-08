using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkIdentitySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BenchmarkSuiteIdUsed",
                table: "BenchmarkRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BenchmarkQuestionIdUsed",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            // Direct column copies: the snapshot must equal the live foreign key for every row that
            // still has one, or the same run would render two different comparability key values
            // either side of this migration. A row whose key is already null cannot be recovered and
            // stays null, which the tier resolver treats as absent identity.
            migrationBuilder.Sql(
                "UPDATE BenchmarkRuns SET BenchmarkSuiteIdUsed = BenchmarkSuiteId WHERE BenchmarkSuiteId IS NOT NULL;");

            migrationBuilder.Sql(
                "UPDATE BenchmarkRunAnswers SET BenchmarkQuestionIdUsed = BenchmarkQuestionId WHERE BenchmarkQuestionId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BenchmarkSuiteIdUsed",
                table: "BenchmarkRuns");

            migrationBuilder.DropColumn(
                name: "BenchmarkQuestionIdUsed",
                table: "BenchmarkRunAnswers");
        }
    }
}
