using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecalibrateBenchmarkSpeedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only: the speed constants live in columns that already exist, so changing the
            // C# property defaults leaves every stored profile on the old scale. The 15000 ms /
            // k = 20 pair saturated the Speed Index — a fast agentic candidate scored 100 on most
            // of a suite — and the guard limits this to profiles still holding exactly that pair,
            // so a profile an administrator has tuned is left as it stands.
            migrationBuilder.Sql(@"
                UPDATE BenchmarkScoringProfiles
                   SET SpeedTargetMs = 2000,
                       SpeedDecayK = 12.0,
                       ModifiedAtUtc = GETUTCDATE()
                 WHERE SpeedTargetMs = 15000
                   AND SpeedDecayK = 20.0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE BenchmarkScoringProfiles
                   SET SpeedTargetMs = 15000,
                       SpeedDecayK = 20.0,
                       ModifiedAtUtc = GETUTCDATE()
                 WHERE SpeedTargetMs = 2000
                   AND SpeedDecayK = 12.0;
            ");
        }
    }
}
