using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Markd.Core.Data;
using Markd.Core.Localization;
using Markd.Core.Sync;

namespace Markd.Core.Services
{
    public class ImportService : IImportService
    {
        private readonly MarkdDbContext _db;
        private readonly ISnapshotService _snapshots;
        private readonly ISyncStateStore _syncState;

        public ImportService(MarkdDbContext db) : this(db, new SnapshotService(db)) { }

        public ImportService(MarkdDbContext db, ISnapshotService snapshots) : this(db, snapshots, new SyncStateStore(db)) { }

        public ImportService(MarkdDbContext db, ISnapshotService snapshots, ISyncStateStore syncState)
        {
            _db = db;
            _snapshots = snapshots;
            _syncState = syncState;
        }

        /// <summary>A 1.0 backup: it carries no sync identity, so a restore on a synced device can only match on content.</summary>
        public static bool IsLegacy(ExportModel model) => model.SchemaVersion == "1";

        public async Task<ExportModel> ParseImportPackageAsync(byte[] package, string? passphrase = null)
        {
            if (package == null || package.Length == 0)
                throw new InvalidOperationException(Strings.Import_EmptyFile);

            if (!MarkdPackage.IsEncrypted(package))
                return ExportModelJson.Deserialize(Encoding.UTF8.GetString(package));

            var plain = await Task.Run(() => MarkdPackage.Decrypt(package, passphrase));
            try
            {
                return ExportModelJson.Deserialize(Encoding.UTF8.GetString(plain));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        public async Task<ImportOutcome> ApplyImportAsync(ExportModel model)
        {
            var target = ExportModelUpgrade.ToCurrent(model, _snapshots.Zone);   // v1 instants become the days the apply stores
            ExportModelJson.Validate(target);   // a hand-built v2 model with empty or duplicate SyncIds must not reach the database

            var conn = (SqliteConnection)_db.Database.GetDbConnection();
            var dataSource = GetSqliteDataSource(conn.ConnectionString);
            string? backupPath = null;
            if (!string.IsNullOrEmpty(dataSource) && File.Exists(dataSource))
            {
                backupPath = dataSource + $".backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
                File.Copy(dataSource, backupPath, overwrite: true);
            }

            ImportOutcome outcome;
            if (await _syncState.HasSyncStateAsync())
            {
                // A synced device only adds: nothing is tombstoned, live records stay as they are, and the next sync
                // uploads the additions as local edits against the kept base.
                var local = await _snapshots.ReadForSyncAsync();
                var additions = ImportMatcher.SkipMatches(target, local, out var matched);
                outcome = Count(additions, local, matched);
                await _snapshots.ApplyAsync(additions, new SnapshotApplyOptions(TakeNotifiedFromTarget: true, ApplySettings: true, Additive: true));
            }
            else
            {
                // Records missing from the file become tombstones, so the replace also reaches other synced devices.
                // On failure the transaction rolls back and the backup stays for manual recovery.
                await _snapshots.ApplyAsync(target, new SnapshotApplyOptions(TakeNotifiedFromTarget: true, ApplySettings: true));
                outcome = new ImportOutcome(false, target.Occasions.Count(o => o.DeletedAt is null), 0, 0);
            }

            if (backupPath is not null)
            {
                try { File.Delete(backupPath); }
                catch (Exception) { /* the startup sweep removes it later */ }
            }

            return outcome;
        }

        private static ImportOutcome Count(ExportModel additions, ExportModel local, int matched)
        {
            var localDeleted = local.Occasions.ToDictionary(o => o.SyncId, o => o.DeletedAt is not null);
            int added = 0, revived = 0, skipped = matched;
            foreach (var o in additions.Occasions.Where(o => o.DeletedAt is null))
            {
                if (!localDeleted.TryGetValue(o.SyncId, out var deleted)) added++;
                else if (deleted) revived++;
                else skipped++;
            }

            return new ImportOutcome(true, added, revived, skipped);
        }

        private static string? GetSqliteDataSource(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return null;
            // crude parse for 'Data Source=' key
            var parts = connectionString.Split(';');
            foreach (var p in parts)
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2 && kv[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                    return kv[1].Trim();
            }
            return null;
        }
    }
}
