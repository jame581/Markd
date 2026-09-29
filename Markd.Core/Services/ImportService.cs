using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Markd.Core.Data;
using Markd.Core.Localization;

namespace Markd.Core.Services
{
    public class ImportService : IImportService
    {
        private readonly MarkdDbContext _db;
        private readonly ISnapshotService _snapshots;

        public ImportService(MarkdDbContext db) : this(db, new SnapshotService(db)) { }

        public ImportService(MarkdDbContext db, ISnapshotService snapshots)
        {
            _db = db;
            _snapshots = snapshots;
        }

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

        public async Task ApplyImportAsync(ExportModel model)
        {
            var target = ExportModelUpgrade.ToCurrent(model);
            ExportModelJson.Validate(target);   // a hand-built v2 model with empty or duplicate SyncIds must not reach the database

            var conn = (SqliteConnection)_db.Database.GetDbConnection();
            var dataSource = GetSqliteDataSource(conn.ConnectionString);
            string? backupPath = null;
            if (!string.IsNullOrEmpty(dataSource) && File.Exists(dataSource))
            {
                backupPath = dataSource + $".backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
                File.Copy(dataSource, backupPath, overwrite: true);
            }

            // Records missing from the file become tombstones, so the replace also reaches other synced devices.
            // On failure the transaction rolls back and the backup stays for manual recovery.
            await _snapshots.ApplyAsync(target, new SnapshotApplyOptions(TakeNotifiedFromTarget: true, ApplySettings: true));

            if (backupPath is not null)
            {
                try { File.Delete(backupPath); }
                catch (Exception) { /* the startup sweep removes it later */ }
            }
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
