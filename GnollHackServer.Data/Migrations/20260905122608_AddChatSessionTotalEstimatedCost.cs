using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionTotalEstimatedCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalEstimatedCost",
                table: "ChatSession",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalEstimatedCost",
                table: "ChatSession");
        }
    }
}
