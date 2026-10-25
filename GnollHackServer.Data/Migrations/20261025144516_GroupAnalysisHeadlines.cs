using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class GroupAnalysisHeadlines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "HeadlineCostPerRun",
                table: "BenchmarkGroupAnalyses",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HeadlineHalfWidth",
                table: "BenchmarkGroupAnalyses",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HeadlineIndex",
                table: "BenchmarkGroupAnalyses",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HeadlineSpeedIndex",
                table: "BenchmarkGroupAnalyses",
                type: "float",
                nullable: true);

            // ResultJson is written with default JsonSerializer options, so its property names are PascalCase.
            migrationBuilder.Sql(@"
UPDATE BenchmarkGroupAnalyses SET
  HeadlineIndex      = TRY_CAST(JSON_VALUE(ResultJson, '$.Index.PointEstimate') AS float),
  HeadlineHalfWidth  = TRY_CAST(JSON_VALUE(ResultJson, '$.Index.CombinedHalfWidth') AS float),
  HeadlineSpeedIndex = TRY_CAST(JSON_VALUE(ResultJson, '$.Speed.MeanSpeedIndex') AS float),
  HeadlineCostPerRun = TRY_CAST(JSON_VALUE(ResultJson, '$.Cost.MeanCostPerRun') AS float)
WHERE ResultJson IS NOT NULL AND ISJSON(ResultJson) = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeadlineCostPerRun",
                table: "BenchmarkGroupAnalyses");

            migrationBuilder.DropColumn(
                name: "HeadlineHalfWidth",
                table: "BenchmarkGroupAnalyses");

            migrationBuilder.DropColumn(
                name: "HeadlineIndex",
                table: "BenchmarkGroupAnalyses");

            migrationBuilder.DropColumn(
                name: "HeadlineSpeedIndex",
                table: "BenchmarkGroupAnalyses");
        }
    }
}
