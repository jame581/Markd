using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Markd.Core.Data;

namespace Markd.Core.Services
{
    public class ExportService : IExportService
    {
        private readonly MarkdDbContext _db;
        private readonly TimeZoneInfo _zone;

        /// <param name="zone">The time zone whose calendar days the export writes. Defaults to local.</param>
        public ExportService(MarkdDbContext db, TimeZoneInfo? zone = null)
        {
            _db = db;
            _zone = zone ?? TimeZoneInfo.Local;
        }

        public async Task<byte[]> CreateExportJsonAsync()
        {
            var model = await BuildExportModelAsync();
            return Encoding.UTF8.GetBytes(ExportModelJson.Serialize(model, indented: true));
        }

        public async Task<byte[]> CreateExportPackageAsync(string? passphrase = null)
        {
            var plain = await CreateExportJsonAsync();
            if (string.IsNullOrEmpty(passphrase))
                return plain;

            try
            {
                // Key derivation takes about a second on a slow phone; keep it off the UI thread.
                return await Task.Run(() => MarkdPackage.Encrypt(plain, passphrase));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        private async Task<ExportModel> BuildExportModelAsync()
        {
            var model = await SnapshotReader.ReadAsync(_db, includeTombstones: false, includeNotified: true, _zone);
            model.AppVersion = ""; // populate from app if available
            model.DeviceId = Environment.MachineName;

            var settings = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
            if (settings != null)
            {
                model.Settings = new AppSettingsDto
                {
                    Theme = settings.Theme,
                    Language = settings.Language,
                    NotificationsEnabled = settings.NotificationsEnabled
                };
            }

            return model;
        }
    }
}
