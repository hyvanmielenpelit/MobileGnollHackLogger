using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBonesTransactionBonesRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BonesTransactions_Bones_BonesId",
                table: "BonesTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BonesTransactions_BonesId",
                table: "BonesTransactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BonesTransactions_BonesId",
                table: "BonesTransactions",
                column: "BonesId");

            migrationBuilder.AddForeignKey(
                name: "FK_BonesTransactions_Bones_BonesId",
                table: "BonesTransactions",
                column: "BonesId",
                principalTable: "Bones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
