namespace Markd.Services;

public enum ExportDestination { Save, Share }

/// <summary>What the export dialog returned: a password (null for plain JSON) and where the file goes.</summary>
public sealed record ExportChoice(string? Password, ExportDestination Destination);

/// <summary>Export options and import password prompts: a modal page on phones, an overlay dialog on the desktop.</summary>
public interface IPasswordPromptService
{
    /// <summary>Null when cancelled.</summary>
    Task<ExportChoice?> PromptExportAsync();

    /// <summary>Null when cancelled. <paramref name="previousAttemptFailed"/> shows the wrong-password message.</summary>
    Task<string?> PromptImportPasswordAsync(string fileName, bool previousAttemptFailed);
}
