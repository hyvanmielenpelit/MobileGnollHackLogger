using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkQuestionIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BenchmarkQuestionId",
                table: "BenchmarkRunAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemRevisionUsed",
                table: "BenchmarkRunAnswers",
                type: "int",
                nullable: true);

            // EF generates defaultValue: 0 for a new non-nullable int, while the CLR initializer
            // is 1 — and that initializer only applies to newly constructed entities, never to
            // rows already in the table. Left alone, every existing question would sit at
            // revision 0 while every new one starts at 1, for no reason a reader could recover.
            migrationBuilder.AddColumn<int>(
                name: "ItemRevision",
                table: "BenchmarkQuestions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("UPDATE [BenchmarkQuestions] SET [ItemRevision] = 1 WHERE [ItemRevision] = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_BenchmarkRunAnswers_BenchmarkQuestionId_ItemRevisionUsed",
                table: "BenchmarkRunAnswers",
                columns: new[] { "BenchmarkQuestionId", "ItemRevisionUsed" });

            migrationBuilder.AddForeignKey(
                name: "FK_BenchmarkRunAnswers_BenchmarkQuestions_BenchmarkQuestionId",
                table: "BenchmarkRunAnswers",
                column: "BenchmarkQuestionId",
                principalTable: "BenchmarkQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Backfill the link for historical answers, and only where it is unambiguous: the
            // answer's run must belong to the suite, and the suite must hold exactly one question
            // at that order index with that exact text. Everything else is left null and is
            // excluded from item analysis rather than guessed at — a wrong link would corrupt
            // every statistic built on it, and unlike a missing one, silently.
            //
            // ItemRevisionUsed is deliberately NOT backfilled. A historical answer was produced
            // against whatever the question said at the time, which is unknowable from here;
            // null means "unknown revision" and drops out of the per-revision grouping.
            migrationBuilder.Sql(@"
                UPDATE a
                SET a.[BenchmarkQuestionId] = m.[QuestionId]
                FROM [BenchmarkRunAnswers] a
                INNER JOIN [BenchmarkRuns] r ON r.[Id] = a.[BenchmarkRunId]
                CROSS APPLY (
                    SELECT MIN(q.[Id]) AS [QuestionId], COUNT(*) AS [MatchCount]
                    FROM [BenchmarkQuestions] q
                    WHERE q.[BenchmarkSuiteId] = r.[BenchmarkSuiteId]
                      AND q.[OrderIndex] = a.[OrderIndex]
                      AND q.[QuestionText] = a.[QuestionText]
                ) m
                WHERE a.[BenchmarkQuestionId] IS NULL
                  AND m.[MatchCount] = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BenchmarkRunAnswers_BenchmarkQuestions_BenchmarkQuestionId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropIndex(
                name: "IX_BenchmarkRunAnswers_BenchmarkQuestionId_ItemRevisionUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "BenchmarkQuestionId",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ItemRevisionUsed",
                table: "BenchmarkRunAnswers");

            migrationBuilder.DropColumn(
                name: "ItemRevision",
                table: "BenchmarkQuestions");
        }
    }
}
