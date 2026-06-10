using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Slice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SliceReports_Email_ReportDate",
                table: "SliceReports",
                columns: new[] { "GeneratedByEmail", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SliceReports_ReportDate_GeneratedAt",
                table: "SliceReports",
                columns: new[] { "ReportDate", "GeneratedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SliceReports_Email_ReportDate",
                table: "SliceReports");

            migrationBuilder.DropIndex(
                name: "IX_SliceReports_ReportDate_GeneratedAt",
                table: "SliceReports");
        }
    }
}
