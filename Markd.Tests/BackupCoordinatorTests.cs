using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Services;
using Markd.Services.Sync;
using Microsoft.Maui.Devices;
using Xunit;

namespace Markd.Tests;

public class BackupCoordinatorTests
{
    private static readonly byte[] PlainJson = "{}"u8.ToArray();

    /// <summary>A structurally valid encrypted package (real header) so MarkdPackage.ValidateHeader lets it through.</summary>
    private static byte[] EncryptedFixture(string password = "right") =>
        MarkdPackage.Encrypt(PlainJson, password, MarkdPackage.MinIterations);

    /// <summary>Calls on the occasion service and the sync link, in order.</summary>
    private readonly List<string> _log = [];
    private readonly FakeExport _export = new();
    private readonly FakeImport _import = new();
    private readonly FakePrompts _prompts = new();
    private readonly FakePicker _picker = new();
    private readonly FakeFiles _files = new();
    private readonly FakeFeedback _feedback = new();
    private readonly FakeShell _shell = new();
    private readonly FakeOccasions _occasions;
    private readonly FakeSyncLink _syncLink;

    public BackupCoordinatorTests()
    {
        _occasions = new FakeOccasions(_log);
        _syncLink = new FakeSyncLink(_log);
    }

    private BackupCoordinator Create() =>
        new(_export, _import, _prompts, _picker, _files, _feedback, _shell, _occasions, _syncLink);

    // ---- Erase ----

    [Fact]
    public async Task Erase_WithoutSyncState_DeletesAllWithTheUsualCopy()
    {
        Assert.True(await Create().EraseAllAsync());

        Assert.Equal(["delete-all"], _log);
        Assert.Equal(Strings.Settings_EraseConfirmMessage, _shell.LastMessage);
        Assert.True(_shell.LastDestructive);
    }

    [Fact]
    public async Task Erase_Declined_DoesNothing()
    {
        _shell.Answer = false;

        Assert.False(await Create().EraseAllAsync());

        Assert.Empty(_log);
    }

    [Fact]
    public async Task Erase_Synced_SyncsFirst_ThenForgetsTheLinkBeforePurging()
    {
        _syncLink.HasState = true;
        _syncLink.IsLinked = true;

        Assert.True(await Create().EraseAllAsync());

        Assert.Equal(["sync", "forget", "purge"], _log);
        Assert.Equal(Strings.Settings_EraseConfirmMessageSynced, _shell.LastMessage);
        Assert.True(_shell.LastDestructive);
    }

    [Fact]
    public async Task Erase_SyncedButTheSyncFailed_WarnsAboutUnsyncedChanges()
    {
        _syncLink.HasState = true;
        _syncLink.IsLinked = true;
        _syncLink.SyncSucceeds = false;

        Assert.True(await Create().EraseAllAsync());

        Assert.Contains(Strings.Settings_EraseConfirmMessageSynced, _shell.LastMessage);
        Assert.Contains(Strings.Settings_EraseUnsyncedWarning, _shell.LastMessage);
        Assert.Equal(["sync", "forget", "purge"], _log);
    }

    [Fact]
    public async Task Erase_DisconnectedWithAKeptBase_ForgetsAndPurgesWithoutSyncing()
    {
        _syncLink.HasState = true;

        Assert.True(await Create().EraseAllAsync());

        Assert.Equal(["forget", "purge"], _log);
        Assert.Equal(Strings.Settings_EraseConfirmMessageSynced, _shell.LastMessage);
    }

    [Fact]
    public async Task Erase_Synced_Declined_KeepsTheLinkAndTheData()
    {
        _syncLink.HasState = true;
        _syncLink.IsLinked = true;
        _shell.Answer = false;

        Assert.False(await Create().EraseAllAsync());

        Assert.Equal(["sync"], _log);
    }

    [Fact]
    public async Task Erase_ForgettingTheLinkFails_NeverPurges()
    {
        _syncLink.HasState = true;
        _syncLink.ForgetError = new InvalidOperationException("still linked");

        Assert.False(await Create().EraseAllAsync());

        Assert.Equal(["forget"], _log);
        Assert.Equal(Strings.Settings_EraseFailed, Assert.Single(_feedback.Titles));
    }

    // ---- Import with sync state ----

    [Fact]
    public async Task Import_WithSyncState_AddsWithTheSyncedCopy_NotDestructive()
    {
        _syncLink.HasState = true;
        _import.Schema = "2";
        _import.Outcome = new ImportOutcome(true, 3, 1, 2);
        _picker.File = new PickedFile("backup.json", PlainJson);

        Assert.True(await Create().ImportAsync());

        Assert.Equal(Strings.Import_ConfirmMessageSynced, _shell.LastMessage);
        Assert.False(_shell.LastDestructive);
        Assert.True(_import.Applied);
        Assert.Equal(Strings.Import_Complete, Assert.Single(_feedback.Titles));
        var detail = Assert.Single(_feedback.Details);
        var culture = LocalizationManager.Instance.Culture;
        Assert.Contains(Plural.Format("Import_Added", 3, culture), detail);
        Assert.Contains(Plural.Format("Import_Revived", 1, culture), detail);
        Assert.Contains(Plural.Format("Import_Skipped", 2, culture), detail);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("2", false)]
    public async Task Import_WithSyncState_WarnsAboutDuplicatesOnlyForLegacyBackups(string schema, bool warns)
    {
        _syncLink.HasState = true;
        _import.Schema = schema;
        _picker.File = new PickedFile("backup.json", PlainJson);

        Assert.True(await Create().ImportAsync());

        Assert.Equal(warns, _shell.Messages.Any(m => m.Contains(Strings.Import_LegacyMayDuplicate)));
    }

    [Fact]
    public async Task Import_WithSyncState_Declined_LeavesDataAlone()
    {
        _syncLink.HasState = true;
        _shell.Answer = false;
        _picker.File = new PickedFile("backup.json", PlainJson);

        Assert.False(await Create().ImportAsync());

        Assert.False(_import.Applied);
    }

    [Fact]
    public async Task Import_WithoutSyncState_KeepsTheReplaceCopy()
    {
        _picker.File = new PickedFile("backup.json", PlainJson);

        Assert.True(await Create().ImportAsync());

        Assert.Equal(Strings.Import_ConfirmMessage, _shell.LastMessage);
        Assert.DoesNotContain(_shell.Messages, m => m.Contains(Strings.Import_LegacyMayDuplicate));
    }

    [Fact]
    public async Task Export_Cancelled_DoesNothing()
    {
        _prompts.ExportChoice = null;

        await Create().ExportAsync();

        Assert.Null(_export.LastPassword);
        Assert.Empty(_files.Saved);
        Assert.Empty(_files.Shared);
    }

    [Fact]
    public async Task Export_Encrypted_SavesMarkdFile()
    {
        _prompts.ExportChoice = new ExportChoice("long enough", ExportDestination.Save);

        await Create().ExportAsync();

        Assert.Equal("long enough", _export.LastPassword);
        var name = Assert.Single(_files.Saved);
        Assert.StartsWith("markd-export-", name);
        Assert.EndsWith(".markd", name);
        Assert.Equal("Export saved", Assert.Single(_feedback.Titles));
    }

    [Fact]
    public async Task Export_Plain_SharesJsonFile()
    {
        _prompts.ExportChoice = new ExportChoice(null, ExportDestination.Share);

        await Create().ExportAsync();

        Assert.Null(_export.LastPassword);
        Assert.EndsWith(".json", Assert.Single(_files.Shared));
        Assert.Empty(_feedback.Titles);
    }

    [Fact]
    public async Task Export_SaveCancelled_ShowsNothing()
    {
        _prompts.ExportChoice = new ExportChoice(null, ExportDestination.Save);
        _files.SaveResult = null;

        await Create().ExportAsync();

        Assert.Empty(_feedback.Titles);
    }

    [Fact]
    public async Task Export_Failure_NonInvalidOperationException_ShowsGenericMessage()
    {
        _prompts.ExportChoice = new ExportChoice(null, ExportDestination.Save);
        _export.PackageError = new IOException("disk full");

        await Create().ExportAsync();

        Assert.Equal("Export failed", Assert.Single(_feedback.Titles));
        Assert.Equal("Something went wrong. Please try again.", Assert.Single(_feedback.Details));
    }

    [Fact]
    public async Task Import_PlainFile_SkipsPasswordPrompt()
    {
        _picker.File = new PickedFile("backup.json", PlainJson);

        Assert.True(await Create().ImportAsync());

        Assert.Empty(_prompts.PasswordRequests);
        Assert.True(_import.Applied);
        Assert.True(_shell.LastDestructive);
    }

    [Fact]
    public async Task Import_WrongThenRightPassword_Succeeds()
    {
        _picker.File = new PickedFile("backup.markd", EncryptedFixture());
        _prompts.Passwords.Enqueue("wrong");
        _prompts.Passwords.Enqueue("right");
        _import.CorrectPassword = "right";

        Assert.True(await Create().ImportAsync());

        Assert.Equal(new[] { false, true }, _prompts.PasswordRequests);
        Assert.Equal(1, _picker.Calls);
        Assert.True(_import.Applied);
    }

    [Fact]
    public async Task Import_CancelAtPassword_LeavesDataAlone()
    {
        _picker.File = new PickedFile("backup.markd", EncryptedFixture());

        Assert.False(await Create().ImportAsync());

        Assert.False(_import.Applied);
    }

    [Fact]
    public async Task Import_UnsupportedVersion_ReportsFailureWithoutPasswordPrompt()
    {
        var package = EncryptedFixture();
        package[5] = 99; // right after the "MARKD" magic
        _picker.File = new PickedFile("backup.markd", package);

        Assert.False(await Create().ImportAsync());

        Assert.Empty(_prompts.PasswordRequests);
        Assert.Equal("Import failed", Assert.Single(_feedback.Titles));
        Assert.False(_import.Applied);
    }

    [Fact]
    public async Task Import_ApplyFailure_InvalidOperationException_ShowsItsMessage()
    {
        _picker.File = new PickedFile("backup.json", PlainJson);
        _import.ApplyError = new InvalidOperationException("Could not write to the database.");

        Assert.False(await Create().ImportAsync());

        Assert.Equal("Import failed", Assert.Single(_feedback.Titles));
        Assert.Equal("Could not write to the database.", Assert.Single(_feedback.Details));
        Assert.False(_import.Applied);
    }

    [Fact]
    public async Task Import_DeclineConfirmation_LeavesDataAlone()
    {
        _picker.File = new PickedFile("backup.json", PlainJson);
        _shell.Answer = false;

        Assert.False(await Create().ImportAsync());

        Assert.False(_import.Applied);
    }

    [Fact]
    public async Task Import_InvalidFile_ReportsFailure()
    {
        _picker.File = new PickedFile("notes.txt", "hello"u8.ToArray());
        _import.ParseError = new InvalidOperationException("The selected file is not a valid Markd JSON export.");

        Assert.False(await Create().ImportAsync());

        Assert.Equal("Import failed", Assert.Single(_feedback.Titles));
        Assert.False(_import.Applied);
    }

    [Fact]
    public async Task Import_NoFilePicked_DoesNothing()
    {
        _picker.File = null;

        Assert.False(await Create().ImportAsync());

        Assert.Empty(_feedback.Titles);
    }

    private sealed class FakeExport : IExportService
    {
        public string? LastPassword { get; private set; }
        public Exception? PackageError { get; set; }

        public Task<byte[]> CreateExportJsonAsync() => Task.FromResult(PlainJson);

        public Task<byte[]> CreateExportPackageAsync(string? passphrase = null)
        {
            LastPassword = passphrase;
            if (PackageError is not null)
                throw PackageError;

            // Mirrors the real ExportService: encrypts when a passphrase is given, matching MarkdPackage's
            // format so BackupCoordinator's extension choice (MarkdPackage.IsEncrypted) sees real data.
            var package = string.IsNullOrEmpty(passphrase) ? PlainJson : MarkdPackage.Encrypt(PlainJson, passphrase, MarkdPackage.MinIterations);
            return Task.FromResult(package);
        }
    }

    private sealed class FakeImport : IImportService
    {
        public string? CorrectPassword { get; set; }
        public Exception? ParseError { get; set; }
        public Exception? ApplyError { get; set; }
        public bool Applied { get; private set; }
        public string Schema { get; set; } = "1";
        public ImportOutcome? Outcome { get; set; }

        public Task<ExportModel> ParseImportPackageAsync(byte[] package, string? passphrase = null)
        {
            if (ParseError is not null)
                throw ParseError;
            if (MarkdPackage.IsEncrypted(package) && passphrase != CorrectPassword)
                throw new MarkdPackageException(PackageError.WrongPassword, "Wrong password");
            return Task.FromResult(new ExportModel { SchemaVersion = Schema });
        }

        public Task<ImportOutcome> ApplyImportAsync(ExportModel model)
        {
            if (ApplyError is not null)
                throw ApplyError;
            Applied = true;
            return Task.FromResult(Outcome ?? new ImportOutcome(false, model.Occasions.Count, 0, 0));
        }
    }

    private sealed class FakeSyncLink(List<string> log) : ISyncLink
    {
        public bool HasState { get; set; }
        public bool IsLinked { get; set; }
        public bool SyncSucceeds { get; set; } = true;
        public Exception? ForgetError { get; set; }

        public Task<bool> HasSyncStateAsync() => Task.FromResult(HasState);

        public Task<bool> TrySyncNowAsync()
        {
            log.Add("sync");
            return Task.FromResult(SyncSucceeds);
        }

        public Task ForgetLinkForEraseAsync()
        {
            log.Add("forget");
            return ForgetError is { } error ? Task.FromException(error) : Task.CompletedTask;
        }
    }

    /// <summary>Only the erase calls; nothing else is used by the coordinator.</summary>
    private sealed class FakeOccasions(List<string> log) : IOccasionService
    {
        public Task DeleteAllAsync()
        {
            log.Add("delete-all");
            return Task.CompletedTask;
        }

        public Task PurgeAllAsync()
        {
            log.Add("purge");
            return Task.CompletedTask;
        }

        public Task<List<Occasion>> GetAllAsync() => throw new NotSupportedException();
        public Task<Occasion?> GetByIdAsync(int id) => throw new NotSupportedException();
        public Task<Occasion> CreateAsync(Occasion occasion) => throw new NotSupportedException();
        public Task<Occasion> UpdateAsync(Occasion occasion) => throw new NotSupportedException();
        public Task DeleteAsync(int id) => throw new NotSupportedException();
        public Task<Occasion> RestoreAsync(Occasion snapshot) => throw new NotSupportedException();
        public Task SetPinnedAsync(int id) => throw new NotSupportedException();
        public Task<Milestone> AddMilestoneAsync(int occasionId, int thresholdDays, string label) => throw new NotSupportedException();
        public Task RemoveMilestoneAsync(int milestoneId) => throw new NotSupportedException();
        public Task<IReadOnlyList<CalendarMark>> GetCalendarMarksAsync(int year, int month) => throw new NotSupportedException();
        public int GetDays(Occasion occasion) => throw new NotSupportedException();
        public Task<List<(Occasion, Milestone)>> GetPendingMilestonesAsync() => throw new NotSupportedException();
        public Task MarkMilestoneNotifiedAsync(int milestoneId) => throw new NotSupportedException();
    }

    private sealed class FakePrompts : IPasswordPromptService
    {
        public ExportChoice? ExportChoice { get; set; }
        public Queue<string> Passwords { get; } = new();
        public List<bool> PasswordRequests { get; } = [];

        public Task<ExportChoice?> PromptExportAsync() => Task.FromResult(ExportChoice);

        public Task<string?> PromptImportPasswordAsync(string fileName, bool previousAttemptFailed)
        {
            PasswordRequests.Add(previousAttemptFailed);
            return Task.FromResult(Passwords.TryDequeue(out var p) ? p : null);
        }
    }

    private sealed class FakePicker : IFilePickService
    {
        public PickedFile? File { get; set; }
        public int Calls { get; private set; }

        public Task<PickedFile?> PickImportFileAsync()
        {
            Calls++;
            return Task.FromResult(File);
        }
    }

    private sealed class FakeFiles : IFileExportService
    {
        public string? SaveResult { get; set; } = "Documents";
        public List<string> Saved { get; } = [];
        public List<string> Shared { get; } = [];

        public Task<string?> SaveAsync(string fileName, byte[] data)
        {
            Saved.Add(fileName);
            return Task.FromResult(SaveResult);
        }

        public Task ShareAsync(string fileName, byte[] data)
        {
            Shared.Add(fileName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFeedback : IFeedbackService
    {
        public List<string> Titles { get; } = [];
        public List<string?> Details { get; } = [];

        public Task ShowAsync(string title, string? detail = null)
        {
            Titles.Add(title);
            Details.Add(detail);
            return Task.CompletedTask;
        }

        public Task<bool> ShowUndoAsync(string message) => Task.FromResult(false);
    }

    private sealed class FakeShell : IAppShellService
    {
        public bool Answer { get; set; } = true;
        public bool LastDestructive { get; private set; }
        public List<string> Messages { get; } = [];
        public string? LastMessage => Messages.LastOrDefault();
        public DevicePlatform Platform => DevicePlatform.Android;

        public Task GoToAsync(string route) => Task.CompletedTask;

        public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel, bool destructive = false)
        {
            LastDestructive = destructive;
            Messages.Add(message);
            return Task.FromResult(Answer);
        }

        public Task ShowMessageAsync(string title, string message, string close) => Task.CompletedTask;
    }
}
