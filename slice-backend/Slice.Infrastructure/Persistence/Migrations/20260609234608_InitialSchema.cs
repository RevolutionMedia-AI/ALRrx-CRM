using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Slice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReportId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TotalFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessedFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedByEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SliceReports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    JobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GeneratedByEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    MergedCsvPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MergedXlsxPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SliceReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyAgentRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<string>(type: "TEXT", nullable: false),
                    Pod = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SupervisorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AgentEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    HC = table.Column<int>(type: "INTEGER", nullable: false),
                    TC = table.Column<int>(type: "INTEGER", nullable: false),
                    NumberOfHolds = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgHoldTime = table.Column<double>(type: "REAL", nullable: false),
                    ASA = table.Column<double>(type: "REAL", nullable: false),
                    AHT = table.Column<double>(type: "REAL", nullable: false),
                    ACW = table.Column<double>(type: "REAL", nullable: false),
                    PctContactsOnHold = table.Column<double>(type: "REAL", nullable: false),
                    PctSLUnder15Sec = table.Column<double>(type: "REAL", nullable: false),
                    PctTransfers = table.Column<double>(type: "REAL", nullable: false),
                    Shift = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyAgentRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyAgentRows_SliceReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SliceReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyGlobalRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<string>(type: "TEXT", nullable: false),
                    Pod = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Queued = table.Column<int>(type: "INTEGER", nullable: false),
                    Handled = table.Column<int>(type: "INTEGER", nullable: false),
                    MissedCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    TransferredCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    PctQueued = table.Column<double>(type: "REAL", nullable: false),
                    PctHandled = table.Column<double>(type: "REAL", nullable: false),
                    PctMissed = table.Column<double>(type: "REAL", nullable: false),
                    PctTransferred = table.Column<double>(type: "REAL", nullable: false),
                    ConvPct = table.Column<double>(type: "REAL", nullable: false),
                    OrderCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RefundedOrders = table.Column<int>(type: "INTEGER", nullable: false),
                    PctOrdersWithErrors = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyGlobalRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyGlobalRows_SliceReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SliceReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopCallMetricsRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<string>(type: "TEXT", nullable: false),
                    WeekStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShopId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ShopName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PodId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TotalCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    OverflowCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    HandledCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    MissedCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    TransferredCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    PctOverflow = table.Column<double>(type: "REAL", nullable: false),
                    PctQueued = table.Column<double>(type: "REAL", nullable: false),
                    PctHandled = table.Column<double>(type: "REAL", nullable: false),
                    PctMissedOfQueued = table.Column<double>(type: "REAL", nullable: false),
                    PctTransferred = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopCallMetricsRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopCallMetricsRows_SliceReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SliceReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopDailyRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<string>(type: "TEXT", nullable: false),
                    ShopName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ShopId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TotalOrders = table.Column<int>(type: "INTEGER", nullable: false),
                    RefundedOrders = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorRate = table.Column<double>(type: "REAL", nullable: false),
                    ConversionRate = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopDailyRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopDailyRows_SliceReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SliceReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyAgentRows_ReportId_Pod",
                table: "DailyAgentRows",
                columns: new[] { "ReportId", "Pod" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyGlobalRows_ReportId_Pod",
                table: "DailyGlobalRows",
                columns: new[] { "ReportId", "Pod" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_CreatedByEmail",
                table: "ProcessingJobs",
                column: "CreatedByEmail");

            migrationBuilder.CreateIndex(
                name: "IX_ShopCallMetricsRows_ReportId_ShopId_PodId_WeekStart",
                table: "ShopCallMetricsRows",
                columns: new[] { "ReportId", "ShopId", "PodId", "WeekStart" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopCallMetricsRows_WeekStart",
                table: "ShopCallMetricsRows",
                column: "WeekStart");

            migrationBuilder.CreateIndex(
                name: "IX_ShopDailyRows_ReportId_ShopId",
                table: "ShopDailyRows",
                columns: new[] { "ReportId", "ShopId" });

            migrationBuilder.CreateIndex(
                name: "IX_SliceReports_GeneratedAt",
                table: "SliceReports",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SliceReports_GeneratedByEmail",
                table: "SliceReports",
                column: "GeneratedByEmail");

            migrationBuilder.CreateIndex(
                name: "IX_SliceReports_ReportDate",
                table: "SliceReports",
                column: "ReportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyAgentRows");

            migrationBuilder.DropTable(
                name: "DailyGlobalRows");

            migrationBuilder.DropTable(
                name: "ProcessingJobs");

            migrationBuilder.DropTable(
                name: "ShopCallMetricsRows");

            migrationBuilder.DropTable(
                name: "ShopDailyRows");

            migrationBuilder.DropTable(
                name: "SliceReports");
        }
    }
}
