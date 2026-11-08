using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Services.Sync;
using Microsoft.Maui.Devices;

namespace Markd.Services;

/// <summary>
/// Export, import and erase without platform APIs: every dialog, picker and file destination is a seam,
/// so the whole flow runs in unit tests. Import and erase follow the sync rules on a device with sync state.
/// </summary>
public sealed class BackupCoordinator(
    IExportService exportService,
    IImportService importService,
    IPasswordPromptService prompts,
    IFilePickService picker,
    IFileExportService files,
    IFeedbackService feedback,
    IAppShellService shell,
    IOccasionService occasions,
    ISyncLink syncLink)
{
    /// <summary>
    /// Erases every occasion on this device after a confirm. With sync state it clears this device only: a linked
    /// device syncs first (and warns when that failed), then the link is forgotten BEFORE the purge, so a failure in
    /// between never leaves an empty device that a sync would read as deletes. Returns true when data was erased.
    /// </summary>
    public async Task<bool> EraseAllAsync()
    {
        try
        {
            var synced = await syncLink.HasSyncStateAsync();
            var message = Strings.Settings_EraseConfirmMessage;
            if (synced)
            {
                message = Strings.Settings_EraseConfirmMessageSynced;
                if (syncLink.IsLinked && !await syncLink.TrySyncNowAsync())
                    message += "\n\n" + Strings.Settings_EraseUnsyncedWarning;
            }

            var confirmed = await shell.DisplayAlertAsync(
                Strings.Settings_EraseConfirmTitle, message, Strings.Settings_EraseAllShort, Strings.Common_Cancel, destructive: true);
            if (!confirmed)
                return false;

            if (synced)
            {
                await syncLink.ForgetLinkForEraseAsync();
                await occasions.PurgeAllAsync();
            }
            else
            {
                await occasions.DeleteAllAsync();
            }
        }
        catch (Exception)
        {
            // Nothing an erase throws (a failed forget included) is useful to show verbatim.
            await feedback.ShowAsync(Strings.Settings_EraseFailed, Strings.Backup_UnexpectedError);
            return false;
        }

        if (shell.Platform != DevicePlatform.Android)
            await feedback.ShowAsync(Strings.Settings_ErasedTitle, Strings.Settings_ErasedDetail);
        return true;
    }

    public async Task ExportAsync()
    {
        try
        {
            var choice = await prompts.PromptExportAsync();
            if (choice is null)
                return;

            var data = await exportService.CreateExportPackageAsync(choice.Password);
            var extension = MarkdPackage.IsEncrypted(data) ? MarkdPackage.EncryptedExtension : ".json";
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
            await feedback.ShowAsync(Strings.Export_Failed, DetailFor(ex));
        }
    }

    /// <summary>
    /// Returns true when the data on this device changed: replaced, or (with sync state) added to, with a warning
    /// first that a 1.0 backup may double events changed since.
    /// </summary>
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

            var additive = await syncLink.HasSyncStateAsync();
            var confirmed = additive
                ? await shell.DisplayAlertAsync(Strings.Import_ConfirmTitleSynced, AdditiveConfirmMessage(model),
                    Strings.Import_Add, Strings.Common_Cancel, destructive: false)
                : await shell.DisplayAlertAsync(Strings.Import_ConfirmTitle, Strings.Import_ConfirmMessage,
                    Strings.Import_Replace, Strings.Common_Cancel, destructive: true);
            if (!confirmed)
                return false;

            var outcome = await importService.ApplyImportAsync(model);
            var culture = LocalizationManager.Instance.Culture;
            var detail = outcome.Additive
                ? OutcomeText(outcome, culture)
                : string.Format(culture, Strings.Import_Loaded, file.FileName);
            await feedback.ShowAsync(Strings.Import_Complete, detail);
            return true;
        }
        catch (Exception ex)
        {
            await feedback.ShowAsync(Strings.Import_Failed, DetailFor(ex));
            return false;
        }
    }

    private static string AdditiveConfirmMessage(ExportModel model) =>
        ImportService.IsLegacy(model)
            ? Strings.Import_ConfirmMessageSynced + "\n\n" + Strings.Import_LegacyMayDuplicate
            : Strings.Import_ConfirmMessageSynced;

    /// <summary>Added, restored and skipped counts, each only when it is not zero.</summary>
    private static string OutcomeText(ImportOutcome outcome, System.Globalization.CultureInfo culture)
    {
        var parts = new List<string>();
        if (outcome.Added > 0)
            parts.Add(Plural.Format("Import_Added", outcome.Added, culture));
        if (outcome.Revived > 0)
            parts.Add(Plural.Format("Import_Revived", outcome.Revived, culture));
        if (outcome.Skipped > 0)
            parts.Add(Plural.Format("Import_Skipped", outcome.Skipped, culture));
        return parts.Count == 0 ? Strings.Import_NothingNew : string.Join(" ", parts);
    }

    /// <summary>
    /// Our own errors (bad password, malformed package, invalid import data, ...) are safe and useful to show
    /// verbatim. Anything else (IO failures, provider errors, ...) gets a generic message instead.
    /// </summary>
    private static string DetailFor(Exception ex) => ex is InvalidOperationException ? ex.Message : Strings.Backup_UnexpectedError;

    /// <summary>Asks for the password until it is right or the user cancels; the file is read only once.</summary>
    private async Task<ExportModel?> ParseAsync(PickedFile file)
    {
        if (!MarkdPackage.IsEncrypted(file.Data))
            return await importService.ParseImportPackageAsync(file.Data);

        // Structural problems (bad version, truncation, ...) surface here so the user is never asked for a
        // password just to be told the file is unusable.
        MarkdPackage.ValidateHeader(file.Data);

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
