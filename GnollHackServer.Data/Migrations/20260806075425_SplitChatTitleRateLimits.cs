using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnollHackServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitChatTitleRateLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaxDailyRequests",
                table: "SystemAiApiConfigurations",
                newName: "MaxDailyChatRequests");

            migrationBuilder.RenameColumn(
                name: "MaxMonthlyRequests",
                table: "SystemAiApiConfigurations",
                newName: "MaxMonthlyChatRequests");

            migrationBuilder.RenameColumn(
                name: "MaxTotalRequests",
                table: "SystemAiApiConfigurations",
                newName: "MaxTotalChatRequests");

            migrationBuilder.RenameColumn(
                name: "DailyRequestsCount",
                table: "SystemAiApiConfigurations",
                newName: "DailyChatRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MonthlyRequestsCount",
                table: "SystemAiApiConfigurations",
                newName: "MonthlyChatRequestsCount");

            migrationBuilder.RenameColumn(
                name: "TotalRequestsCount",
                table: "SystemAiApiConfigurations",
                newName: "TotalChatRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MaxDailyRequests",
                table: "UserSystemAiApiConfigurations",
                newName: "MaxDailyChatRequests");

            migrationBuilder.RenameColumn(
                name: "MaxMonthlyRequests",
                table: "UserSystemAiApiConfigurations",
                newName: "MaxMonthlyChatRequests");

            migrationBuilder.RenameColumn(
                name: "MaxTotalRequests",
                table: "UserSystemAiApiConfigurations",
                newName: "MaxTotalChatRequests");

            migrationBuilder.RenameColumn(
                name: "DailyRequestsCount",
                table: "UserSystemAiApiConfigurations",
                newName: "DailyChatRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MonthlyRequestsCount",
                table: "UserSystemAiApiConfigurations",
                newName: "MonthlyChatRequestsCount");

            migrationBuilder.RenameColumn(
                name: "TotalRequestsCount",
                table: "UserSystemAiApiConfigurations",
                newName: "TotalChatRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MaxDailyRequests",
                table: "GroupSystemAiApiConfigurations",
                newName: "MaxDailyChatRequests");

            migrationBuilder.RenameColumn(
                name: "MaxMonthlyRequests",
                table: "GroupSystemAiApiConfigurations",
                newName: "MaxMonthlyChatRequests");

            migrationBuilder.RenameColumn(
                name: "MaxTotalRequests",
                table: "GroupSystemAiApiConfigurations",
                newName: "MaxTotalChatRequests");

            migrationBuilder.RenameColumn(
                name: "DailyRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                newName: "DailyChatRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MonthlyRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                newName: "MonthlyChatRequestsCount");

            migrationBuilder.RenameColumn(
                name: "TotalRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                newName: "TotalChatRequestsCount");

            migrationBuilder.AddColumn<int>(
                name: "DailyTitleRequestsCount",
                table: "SystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MonthlyTitleRequestsCount",
                table: "SystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalTitleRequestsCount",
                table: "SystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxDailyTitleRequests",
                table: "SystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxMonthlyTitleRequests",
                table: "SystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxTotalTitleRequests",
                table: "SystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DailyChatTokensCount",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyChatTokensCount",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalChatTokensCount",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MaxDailyChatTokens",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxMonthlyChatTokens",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxTotalChatTokens",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DailyTitleTokensCount",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyTitleTokensCount",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalTitleTokensCount",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MaxDailyTitleTokens",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxMonthlyTitleTokens",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxTotalTitleTokens",
                table: "SystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DailyTitleRequestsCount",
                table: "UserSystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MonthlyTitleRequestsCount",
                table: "UserSystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalTitleRequestsCount",
                table: "UserSystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxDailyTitleRequests",
                table: "UserSystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxMonthlyTitleRequests",
                table: "UserSystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxTotalTitleRequests",
                table: "UserSystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DailyChatTokensCount",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyChatTokensCount",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalChatTokensCount",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MaxDailyChatTokens",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxMonthlyChatTokens",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxTotalChatTokens",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DailyTitleTokensCount",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyTitleTokensCount",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalTitleTokensCount",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MaxDailyTitleTokens",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxMonthlyTitleTokens",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxTotalTitleTokens",
                table: "UserSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DailyTitleRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MonthlyTitleRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalTitleRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxDailyTitleRequests",
                table: "GroupSystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxMonthlyTitleRequests",
                table: "GroupSystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxTotalTitleRequests",
                table: "GroupSystemAiApiConfigurations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DailyChatTokensCount",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyChatTokensCount",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalChatTokensCount",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MaxDailyChatTokens",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxMonthlyChatTokens",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxTotalChatTokens",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DailyTitleTokensCount",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyTitleTokensCount",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalTitleTokensCount",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MaxDailyTitleTokens",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxMonthlyTitleTokens",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxTotalTitleTokens",
                table: "GroupSystemAiApiConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoleContext",
                table: "SystemAiUsageLogs",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaxDailyChatRequests",
                table: "SystemAiApiConfigurations",
                newName: "MaxDailyRequests");

            migrationBuilder.RenameColumn(
                name: "MaxMonthlyChatRequests",
                table: "SystemAiApiConfigurations",
                newName: "MaxMonthlyRequests");

            migrationBuilder.RenameColumn(
                name: "MaxTotalChatRequests",
                table: "SystemAiApiConfigurations",
                newName: "MaxTotalRequests");

            migrationBuilder.RenameColumn(
                name: "DailyChatRequestsCount",
                table: "SystemAiApiConfigurations",
                newName: "DailyRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MonthlyChatRequestsCount",
                table: "SystemAiApiConfigurations",
                newName: "MonthlyRequestsCount");

            migrationBuilder.RenameColumn(
                name: "TotalChatRequestsCount",
                table: "SystemAiApiConfigurations",
                newName: "TotalRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MaxDailyChatRequests",
                table: "UserSystemAiApiConfigurations",
                newName: "MaxDailyRequests");

            migrationBuilder.RenameColumn(
                name: "MaxMonthlyChatRequests",
                table: "UserSystemAiApiConfigurations",
                newName: "MaxMonthlyRequests");

            migrationBuilder.RenameColumn(
                name: "MaxTotalChatRequests",
                table: "UserSystemAiApiConfigurations",
                newName: "MaxTotalRequests");

            migrationBuilder.RenameColumn(
                name: "DailyChatRequestsCount",
                table: "UserSystemAiApiConfigurations",
                newName: "DailyRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MonthlyChatRequestsCount",
                table: "UserSystemAiApiConfigurations",
                newName: "MonthlyRequestsCount");

            migrationBuilder.RenameColumn(
                name: "TotalChatRequestsCount",
                table: "UserSystemAiApiConfigurations",
                newName: "TotalRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MaxDailyChatRequests",
                table: "GroupSystemAiApiConfigurations",
                newName: "MaxDailyRequests");

            migrationBuilder.RenameColumn(
                name: "MaxMonthlyChatRequests",
                table: "GroupSystemAiApiConfigurations",
                newName: "MaxMonthlyRequests");

            migrationBuilder.RenameColumn(
                name: "MaxTotalChatRequests",
                table: "GroupSystemAiApiConfigurations",
                newName: "MaxTotalRequests");

            migrationBuilder.RenameColumn(
                name: "DailyChatRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                newName: "DailyRequestsCount");

            migrationBuilder.RenameColumn(
                name: "MonthlyChatRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                newName: "MonthlyRequestsCount");

            migrationBuilder.RenameColumn(
                name: "TotalChatRequestsCount",
                table: "GroupSystemAiApiConfigurations",
                newName: "TotalRequestsCount");

            migrationBuilder.DropColumn(
                name: "DailyTitleRequestsCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyTitleRequestsCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalTitleRequestsCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyTitleRequests",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyTitleRequests",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalTitleRequests",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DailyChatTokensCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyChatTokensCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalChatTokensCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyChatTokens",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyChatTokens",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalChatTokens",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DailyTitleTokensCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyTitleTokensCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalTitleTokensCount",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyTitleTokens",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyTitleTokens",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalTitleTokens",
                table: "SystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DailyTitleRequestsCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyTitleRequestsCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalTitleRequestsCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyTitleRequests",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyTitleRequests",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalTitleRequests",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DailyChatTokensCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyChatTokensCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalChatTokensCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyChatTokens",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyChatTokens",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalChatTokens",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DailyTitleTokensCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyTitleTokensCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalTitleTokensCount",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyTitleTokens",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyTitleTokens",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalTitleTokens",
                table: "UserSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DailyTitleRequestsCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyTitleRequestsCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalTitleRequestsCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyTitleRequests",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyTitleRequests",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalTitleRequests",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DailyChatTokensCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyChatTokensCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalChatTokensCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyChatTokens",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyChatTokens",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalChatTokens",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "DailyTitleTokensCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MonthlyTitleTokensCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "TotalTitleTokensCount",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxDailyTitleTokens",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyTitleTokens",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxTotalTitleTokens",
                table: "GroupSystemAiApiConfigurations");

            migrationBuilder.DropColumn(
                name: "RoleContext",
                table: "SystemAiUsageLogs");
        }
    }
}
