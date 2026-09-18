using CommunityToolkit.Mvvm.ComponentModel;
using Markd.Core.Localization;
using Markd.Services;

namespace Markd.ViewModels;

/// <summary>The export dialog's fields and validation, shared by the phone page and the desktop dialog.</summary>
public sealed class ExportOptionsForm : ObservableObject
{
    public const int MinimumPasswordLength = 8;

    private bool _encrypt = true;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private string? _errorMessage;

    public bool Encrypt
    {
        get => _encrypt;
        set
        {
            if (SetProperty(ref _encrypt, value))
                ErrorMessage = null;
        }
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value ?? string.Empty);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value ?? string.Empty);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public ExportChoice? TryCreate(ExportDestination destination)
    {
        if (!Encrypt)
        {
            ErrorMessage = null;
            return new ExportChoice(null, destination);
        }

        if (Password.Length < MinimumPasswordLength)
        {
            ErrorMessage = Strings.Export_PasswordTooShort;
            return null;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = Strings.Export_PasswordMismatch;
            return null;
        }

        ErrorMessage = null;
        return new ExportChoice(Password, destination);
    }
}
