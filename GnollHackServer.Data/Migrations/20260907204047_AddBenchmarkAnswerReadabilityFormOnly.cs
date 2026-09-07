using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkAnswerReadabilityFormOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReadabilityFormOnly",
                table: "BenchmarkRunAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadabilityFormOnly",
                table: "BenchmarkRunAnswers");
        }
    }
}
