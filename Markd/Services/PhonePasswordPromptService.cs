using Markd.Pages;

namespace Markd.Services;

/// <summary>Phones: Material dialog pages, pushed modally.</summary>
public sealed class PhonePasswordPromptService : IPasswordPromptService
{
    public async Task<ExportChoice?> PromptExportAsync()
    {
        var page = new ExportOptionsPage();
        await Shell.Current.Navigation.PushModalAsync(page, false);
        return await page.Result;
    }

    public async Task<string?> PromptImportPasswordAsync(string fileName, bool previousAttemptFailed)
    {
        var page = new PasswordPage(fileName, previousAttemptFailed);
        await Shell.Current.Navigation.PushModalAsync(page, false);
        return await page.Result;
    }
}
