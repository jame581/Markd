using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Markd.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class DefaultLanguageSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "Language",
                value: "system");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "Language",
                value: "en");
        }
    }
}
