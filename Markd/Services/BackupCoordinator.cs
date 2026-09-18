using Markd.Core.Localization;
using Markd.Core.Services;

namespace Markd.Services;

/// <summary>
/// Export and import without platform APIs: every dialog, picker and file destination is a seam,
/// so the whole flow runs in unit tests.
/// </summary>
public sealed class BackupCoordinator(
    IExportService exportService,
    IImportService importService,
    IPasswordPromptService prompts,
    IFilePickService picker,
    IFileExportService files,
    IFeedbackService feedback,
    IAppShellService shell)
{
    public async Task ExportAsync()
    {
        try
        {
            var choice = await prompts.PromptExportAsync();
            if (choice is null)
                return;

            var data = await exportService.CreateExportPackageAsync(choice.Password);
            var extension = choice.Password is null ? ".json" : MarkdPackage.EncryptedExtension;
            var fileName = $"markd-export-{DateTime.Now:yyyyMMdd-HHmmss}{extension}";

            if (choice.Destination == ExportDestination.Share)
            {
                await files.ShareAsync(fileName, data);
                return;
            }

            var location = await files.SaveAsync(fileName, data);
            if (location is not null)
                await feedback.ShowAsync(Strings.Export_Saved, location);
        }
        catch (Exception ex)
        {
            await feedback.ShowAsync(Strings.Export_Failed, ex.Message);
        }
    }

    /// <summary>Returns true when the data on this device was replaced.</summary>
    public async Task<bool> ImportAsync()
    {
        try
        {
            var file = await picker.PickImportFileAsync();
            if (file is null)
                return false;

            var model = await ParseAsync(file);
            if (model is null)
                return false;

            var confirmed = await shell.DisplayAlertAsync(
                Strings.Import_ConfirmTitle, Strings.Import_ConfirmMessage, Strings.Import_Replace, Strings.Common_Cancel, destructive: true);
            if (!confirmed)
                return false;

            await importService.ApplyImportAsync(model);
            await feedback.ShowAsync(Strings.Import_Complete, string.Format(LocalizationManager.Instance.Culture, Strings.Import_Loaded, file.FileName));
            return true;
        }
        catch (Exception ex)
        {
            await feedback.ShowAsync(Strings.Import_Failed, ex.Message);
            return false;
        }
    }

    /// <summary>Asks for the password until it is right or the user cancels; the file is read only once.</summary>
    private async Task<ExportModel?> ParseAsync(PickedFile file)
    {
        if (!MarkdPackage.IsEncrypted(file.Data))
            return await importService.ParseImportPackageAsync(file.Data);

        var failed = false;
        while (true)
        {
            var password = await prompts.PromptImportPasswordAsync(file.FileName, failed);
            if (password is null)
                return null;

            try
            {
                return await importService.ParseImportPackageAsync(file.Data, password);
            }
            catch (MarkdPackageException ex) when (ex.Error == PackageError.WrongPassword)
            {
                failed = true;
            }
        }
    }
}
