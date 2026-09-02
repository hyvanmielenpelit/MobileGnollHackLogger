using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShowContextWindowUsageSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowContextWindowUsage",
                table: "UserAiSettings",
                type: "bit",
                nullable: false,
                // Existing rows must come out enabled: the indicator shipped unconditionally
                // before this setting existed, so false here would silently remove it for
                // every current user. Matches the ShowParallelBadge column above.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowContextWindowUsage",
                table: "UserAiSettings");
        }
    }
}
