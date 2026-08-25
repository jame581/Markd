using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Markd.Core.Services;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Markd.ViewModels
{
    public class ExportImportViewModel : BindableObject
    {
        private readonly IExportService _exportService;
        private readonly IImportService _importService;

        public ExportImportViewModel(IExportService exportService, IImportService importService)
        {
            _exportService = exportService;
            _importService = importService;

            ExportPlainCommand = new AsyncRelayCommand(ExportPlainAsync);
            ExportEncryptedCommand = new AsyncRelayCommand(ExportEncryptedAsync);
            ImportPlainCommand = new AsyncRelayCommand(ImportPlainAsync);
            ImportEncryptedCommand = new AsyncRelayCommand(ImportEncryptedAsync);
        }

        public IAsyncRelayCommand ExportPlainCommand { get; }
        public IAsyncRelayCommand ExportEncryptedCommand { get; }
        public IAsyncRelayCommand ImportPlainCommand { get; }
        public IAsyncRelayCommand ImportEncryptedCommand { get; }

        private string _status = "Idle";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private string? _passphrase;
        public string? Passphrase { get => _passphrase; set { _passphrase = value; OnPropertyChanged(); } }

        private string GetPlainPath() => Path.Combine(FileSystem.AppDataDirectory, "export-markd.json");
        private string GetBinPath() => Path.Combine(FileSystem.AppDataDirectory, "export-markd.bin");

        private async Task ExportPlainAsync()
        {
            try
            {
                Status = "Exporting (JSON)...";
                var data = await _exportService.CreateExportPackageAsync(null);
                var path = GetPlainPath();
                await File.WriteAllBytesAsync(path, data);
                Status = $"Exported JSON to: {path}";
            }
            catch (Exception ex)
            {
                Status = "Export failed: " + ex.Message;
            }
        }

        private async Task ExportEncryptedAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(Passphrase)) { Status = "Passphrase required"; return; }
                Status = "Exporting (encrypted)...";
                var data = await _exportService.CreateExportPackageAsync(Passphrase);
                var path = GetBinPath();
                await File.WriteAllBytesAsync(path, data);
                Status = $"Exported encrypted package to: {path}";
            }
            catch (Exception ex)
            {
                Status = "Export failed: " + ex.Message;
            }
        }

        private async Task ImportPlainAsync()
        {
            try
            {
                var path = GetPlainPath();
                if (!File.Exists(path)) { Status = "Plain export file not found: " + path; return; }
                Status = "Importing (JSON)...";
                var bytes = await File.ReadAllBytesAsync(path);
                var model = await _importService.ParseImportPackageAsync(bytes, null);
                await _importService.ApplyImportAsync(model);
                Status = "Import (JSON) completed";
            }
            catch (Exception ex)
            {
                Status = "Import failed: " + ex.Message;
            }
        }

        private async Task ImportEncryptedAsync()
        {
            try
            {
                var path = GetBinPath();
                if (!File.Exists(path)) { Status = "Encrypted export file not found: " + path; return; }
                if (string.IsNullOrEmpty(Passphrase)) { Status = "Passphrase required"; return; }
                Status = "Importing (encrypted)...";
                var bytes = await File.ReadAllBytesAsync(path);
                var model = await _importService.ParseImportPackageAsync(bytes, Passphrase);
                await _importService.ApplyImportAsync(model);
                Status = "Import (encrypted) completed";
            }
            catch (Exception ex)
            {
                Status = "Import failed: " + ex.Message;
            }
        }
    }
}
