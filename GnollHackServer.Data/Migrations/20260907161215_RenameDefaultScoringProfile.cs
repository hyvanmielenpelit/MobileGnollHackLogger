using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameDefaultScoringProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The badge beside the heading already states that the profile is the default, so the
            // suffix said it twice. Matched exactly rather than trimmed: a profile deliberately
            // named "Experimental (Default)" is its author's name to keep. [Name] carries a unique
            // index, so the guard is what keeps this from throwing where a clean-named row exists.
            // [ModifiedAtUtc] is left alone — the scoring definition did not change.
            migrationBuilder.Sql(@"
UPDATE [BenchmarkScoringProfiles]
SET [Name] = N'Standard Intelligence Index'
WHERE [Name] = N'Standard Intelligence Index (Default)'
  AND NOT EXISTS (
      SELECT 1 FROM [BenchmarkScoringProfiles]
      WHERE [Name] = N'Standard Intelligence Index');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. The seed has always written the clean name, so the suffix was a
            // data anomaly rather than a schema state to restore; re-appending it here would
            // introduce the anomaly on every database that never carried it.
        }
    }
}
