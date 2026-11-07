using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Markd.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOccasionAnchorDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AnchorDay",
                table: "Occasions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnchorDay",
                table: "Occasions");
        }
    }
}
