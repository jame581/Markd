using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Markd.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Occasions_IsPinned",
                table: "Occasions");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Occasions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Occasions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Occasions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Milestones",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Milestones",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Milestones",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Categories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Categories",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Categories",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    AccountName = table.Column<string>(type: "TEXT", nullable: true),
                    RemoteRevision = table.Column<string>(type: "TEXT", nullable: true),
                    LastSyncUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastResult = table.Column<string>(type: "TEXT", nullable: true),
                    Epoch = table.Column<Guid>(type: "TEXT", nullable: true),
                    BaseSnapshotJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                });

            foreach (var table in new[] { "Categories", "Occasions", "Milestones" })
            {
                // Random version-4 style GUID per row, upper-case to match how EF stores Guid in SQLite.
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"SyncId\" = upper(" +
                    "hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || " +
                    "substr('89AB', (random() & 3) + 1, 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6)));");
            }

            migrationBuilder.Sql("UPDATE \"Occasions\" SET \"UpdatedAt\" = \"CreatedAt\";");
            migrationBuilder.Sql("UPDATE \"Categories\" SET \"UpdatedAt\" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql("UPDATE \"Milestones\" SET \"UpdatedAt\" = strftime('%Y-%m-%d %H:%M:%f', 'now');");

            migrationBuilder.CreateIndex(
                name: "IX_Occasions_IsPinned",
                table: "Occasions",
                column: "IsPinned",
                unique: true,
                filter: "\"IsPinned\" = 1 AND \"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Occasions_SyncId",
                table: "Occasions",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Milestones_SyncId",
                table: "Milestones",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SyncId",
                table: "Categories",
                column: "SyncId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropIndex(
                name: "IX_Occasions_IsPinned",
                table: "Occasions");

            migrationBuilder.DropIndex(
                name: "IX_Occasions_SyncId",
                table: "Occasions");

            migrationBuilder.DropIndex(
                name: "IX_Milestones_SyncId",
                table: "Milestones");

            migrationBuilder.DropIndex(
                name: "IX_Categories_SyncId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Occasions");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Occasions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Occasions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Milestones");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Milestones");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Milestones");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Occasions_IsPinned",
                table: "Occasions",
                column: "IsPinned",
                unique: true,
                filter: "\"IsPinned\" = 1");
        }
    }
}
