namespace Markd.Services.Sync;

public enum PassphrasePromptKind { CreateNew, EnterExisting, EnterChanged, ChangeOwn }

/// <summary>Passphrase null with ForgotIt false means the prompt was cancelled.</summary>
public sealed record PassphrasePromptResult(string? Passphrase, bool ForgotIt);

/// <summary>Asks for the sync passphrase (implemented per layout).</summary>
public interface ISyncPassphrasePromptService
{
    Task<PassphrasePromptResult> PromptAsync(PassphrasePromptKind kind, bool previousAttemptFailed);
}
