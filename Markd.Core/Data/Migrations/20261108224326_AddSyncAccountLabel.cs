using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Markd.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncAccountLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountLabel",
                table: "SyncStates",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountLabel",
                table: "SyncStates");
        }
    }
}
