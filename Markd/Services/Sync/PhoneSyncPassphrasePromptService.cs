using Markd.Pages;

namespace Markd.Services.Sync;

/// <summary>Phones: the Material passphrase dialog page, pushed modally. Always completes (cancelled when dismissed).</summary>
public sealed class PhoneSyncPassphrasePromptService : ISyncPassphrasePromptService
{
    public Task<PassphrasePromptResult> PromptAsync(PassphrasePromptKind kind, bool previousAttemptFailed) =>
        PhoneModalPrompt.ShowAsync(() => new SyncPassphrasePage(kind, previousAttemptFailed), page => page.Result,
            new PassphrasePromptResult(null, false));
}
