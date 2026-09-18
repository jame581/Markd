using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Markd.ViewModels;

public class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string? _errorMessage;

    protected ViewModelBase()
    {
        WeakReferenceMessenger.Default.Register<ViewModelBase, LanguageChangedMessage>(this, (vm, _) => vm.OnLanguageChanged());
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Re-renders computed text after a language switch. Views with cached rows override this to reload.</summary>
    protected virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);
}
