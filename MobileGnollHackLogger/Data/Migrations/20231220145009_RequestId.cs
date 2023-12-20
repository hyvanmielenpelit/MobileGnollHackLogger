using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobileGnollHackLogger.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Logs",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Logs");
        }
    }
}
