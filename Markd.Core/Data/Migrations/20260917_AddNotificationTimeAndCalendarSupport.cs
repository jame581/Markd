using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Markd.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTimeAndCalendarSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "NotificationTimeOfDay",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeSpan(9, 0, 0));

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Language", "NotificationTimeOfDay", "NotificationsEnabled", "Theme" },
                values: new object[] { "en", new TimeSpan(9, 0, 0), true, "System" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationTimeOfDay",
                table: "AppSettings");
        }
    }
}
