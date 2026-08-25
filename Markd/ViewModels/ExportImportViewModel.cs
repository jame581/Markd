using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Markd.Core.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace Markd.ViewModels
{
    public class ExportImportViewModel : ViewModelBase
    {
        private readonly IExportService _exportService;
        private readonly IImportService _importService;

        public ExportImportViewModel(IExportService exportService, IImportService importService)
        {
            _exportService = exportService;
            _importService = importService;

            ExportJsonCommand = new AsyncRelayCommand(ExportJsonAsync, () => !IsBusy);
            ImportJsonCommand = new AsyncRelayCommand(ImportJsonAsync, () => !IsBusy);
        }

        public IAsyncRelayCommand ExportJsonCommand { get; }
        public IAsyncRelayCommand ImportJsonCommand { get; }

        private string _status = "Ready to export or import JSON.";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string GetExportPath()
        {
            var fileName = $"markd-export-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            return Path.Combine(FileSystem.CacheDirectory, fileName);
        }

        private async Task ExportJsonAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            RefreshCommands();

            try
            {
                Status = "Preparing JSON export...";
                var data = await _exportService.CreateExportJsonAsync();
                var path = GetExportPath();
                await File.WriteAllBytesAsync(path, data);

                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Share Markd export",
                    File = new ShareFile(path)
                });

                Status = $"JSON export prepared: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                Status = "Export failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RefreshCommands();
            }
        }

        public async Task ImportJsonAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            RefreshCommands();

            try
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Select a Markd JSON export"
                });

                if (file == null)
                {
                    Status = "Import canceled.";
                    return;
                }

                Status = "Importing JSON...";

                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);

                var model = await _importService.ParseImportPackageAsync(memory.ToArray());
                await _importService.ApplyImportAsync(model);

                Status = $"Imported JSON from: {file.FileName}";
            }
            catch (Exception ex)
            {
                Status = "Import failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                RefreshCommands();
            }
        }

        private void RefreshCommands()
        {
            ExportJsonCommand.NotifyCanExecuteChanged();
            ImportJsonCommand.NotifyCanExecuteChanged();
        }
    }
}
