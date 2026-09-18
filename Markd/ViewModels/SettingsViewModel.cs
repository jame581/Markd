using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Services;
using Microsoft.Maui.Devices;

namespace Markd.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private const string TimeFormat = @"hh\:mm";

    private readonly IAppSettingsService _settingsService;
    private readonly IOccasionService _occasionService;
    private readonly IAppShellService _shellService;
    private readonly IFeedbackService _feedbackService;
    private readonly IExportService _exportService;
    private readonly IImportService _importService;
    private readonly IFileExportService _fileExportService;
    private readonly NotificationService _notificationService;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private AppSettings _settings = new();
    private bool _isLoading;
    private string _selectedTheme = "System";
    private LanguageOption? _selectedLanguage;
    private bool _notificationsEnabled = true;
    private TimeSpan _notificationTimeOfDay = new(9, 0, 0);

    public SettingsViewModel(
        IAppSettingsService settingsService,
        IOccasionService occasionService,
        IAppShellService shellService,
        IFeedbackService feedbackService,
        IExportService exportService,
        IImportService importService,
        IFileExportService fileExportService,
        NotificationService notificationService)
    {
        _settingsService = settingsService;
        _occasionService = occasionService;
        _shellService = shellService;
        _feedbackService = feedbackService;
        _exportService = exportService;
        _importService = importService;
        _fileExportService = fileExportService;
        _notificationService = notificationService;

        ThemeOptions =
        [
            new ThemeOption("System", "Settings_ThemeSystem", "Settings_ThemeSystemShort"),
            new ThemeOption("Light", "Settings_ThemeLight", "Settings_ThemeLight"),
            new ThemeOption("Dark", "Settings_ThemeDark", "Settings_ThemeDark")
        ];

        SelectThemeCommand = new RelayCommand<ThemeOption?>(option => { if (option is not null) SelectedTheme = option.Value; });
        ToggleNotificationsCommand = new RelayCommand(() => NotificationsEnabled = !NotificationsEnabled);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        OpenAboutCommand = new AsyncRelayCommand(() => _shellService.GoToAsync(nameof(AboutPage)));
        EraseAllCommand = new AsyncRelayCommand(EraseAllAsync);
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<LanguageOption> Languages { get; private set; } = BuildLanguages();

    private static IReadOnlyList<LanguageOption> BuildLanguages() =>
    [
        new(LanguageSetting.System, Strings.Settings_LanguageSystem),
        new(LanguageSetting.English, "English"),
        new(LanguageSetting.Czech, "Čeština")
    ];

    /// <summary>Half-hour slots for the desktop time dropdown, plus the saved time if it falls between them.</summary>
    public ObservableCollection<string> TimeOptions { get; } =
        new(Enumerable.Range(0, 48).Select(i => TimeSpan.FromMinutes(i * 30).ToString(TimeFormat, CultureInfo.InvariantCulture)));

    public IRelayCommand<ThemeOption?> SelectThemeCommand { get; }
    public IRelayCommand ToggleNotificationsCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IAsyncRelayCommand OpenAboutCommand { get; }
    public IAsyncRelayCommand EraseAllCommand { get; }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                foreach (var option in ThemeOptions)
                    option.IsSelected = option.Value == value;

                ThemeService.Apply(value);
                ScheduleSave();
            }
        }
    }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguage, value))
                return;

            if (LanguageService.Apply(value.Value))
            {
                // Sent on the next UI tick: this setter can run inside the WinUI Picker's selection callback, and
                // OnLanguageChanged swaps the Picker's ItemsSource — reentering that callback crashes WinUI.
                if (Application.Current?.Dispatcher is { } dispatcher)
                    dispatcher.Dispatch(() => WeakReferenceMessenger.Default.Send(new LanguageChangedMessage()));
                else
                    WeakReferenceMessenger.Default.Send(new LanguageChangedMessage());
            }

            // The language itself switches synchronously above; only notifying about it is deferred.
            // requestPermission: false — a language change should never pop the OS notification-permission prompt.
            ScheduleSave(reschedule: true, requestPermission: false);
        }
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (SetProperty(ref _notificationsEnabled, value))
                ScheduleSave(reschedule: true);
        }
    }

    public TimeSpan NotificationTimeOfDay
    {
        get => _notificationTimeOfDay;
        set
        {
            if (SetProperty(ref _notificationTimeOfDay, value))
            {
                EnsureTimeOption(NotificationTimeText);
                OnPropertyChanged(nameof(NotificationTimeText));
                OnPropertyChanged(nameof(SelectedTimeOption));
                ScheduleSave(reschedule: true);
            }
        }
    }

    public string NotificationTimeText => NotificationTimeOfDay.ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>The desktop dropdown's selection, as "09:00".</summary>
    public string? SelectedTimeOption
    {
        get => NotificationTimeText;
        set
        {
            if (TimeSpan.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, out var time))
                NotificationTimeOfDay = time;
        }
    }

    public string VersionText => string.Format(LocalizationManager.Instance.Culture, Strings.Settings_Version, AppInfo.Current.VersionString, AppInfo.Current.BuildString);

    public async Task LoadAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        IsBusy = true;
        try
        {
            _settings = await _settingsService.GetAsync();
            SelectedTheme = string.IsNullOrWhiteSpace(_settings.Theme) ? "System" : _settings.Theme;
            foreach (var option in ThemeOptions)
                option.IsSelected = option.Value == SelectedTheme;
            SelectedLanguage = Languages.FirstOrDefault(l => l.Value == _settings.Language) ?? Languages[0];
            NotificationsEnabled = _settings.NotificationsEnabled;
            NotificationTimeOfDay = _settings.NotificationTimeOfDay;
        }
        finally
        {
            IsBusy = false;
            _isLoading = false;
        }
    }

    private void EnsureTimeOption(string text)
    {
        if (TimeOptions.Contains(text))
            return;

        var index = TimeOptions.TakeWhile(option => string.CompareOrdinal(option, text) < 0).Count();
        TimeOptions.Insert(index, text);
    }

    private void ScheduleSave(bool reschedule = false, bool requestPermission = true)
    {
        if (_isLoading)
            return;

        _ = SaveAsync(reschedule, requestPermission);
    }

    private async Task SaveAsync(bool reschedule, bool requestPermission = true)
    {
        await _saveGate.WaitAsync();
        try
        {
            _settings.Theme = SelectedTheme;
            _settings.Language = SelectedLanguage?.Value ?? LanguageSetting.System;
            _settings.NotificationsEnabled = NotificationsEnabled;
            _settings.NotificationTimeOfDay = NotificationTimeOfDay;
            await _settingsService.SaveAsync(_settings);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _saveGate.Release();
        }

        if (reschedule)
            await _notificationService.RescheduleAsync(requestPermission: requestPermission && NotificationsEnabled);
    }

    private async Task ExportAsync()
    {
        try
        {
            var data = await _exportService.CreateExportJsonAsync();
            var location = await _fileExportService.SaveAsync($"markd-export-{DateTime.Now:yyyyMMdd-HHmmss}.json", data);
            if (location is not null)
                await _feedbackService.ShowAsync(Strings.Export_Saved, location);
        }
        catch (Exception ex)
        {
            await _feedbackService.ShowAsync(Strings.Export_Failed, ex.Message);
        }
    }

    private async Task ImportAsync()
    {
        var confirmed = await _shellService.DisplayAlertAsync(
            Strings.Import_ConfirmTitle,
            Strings.Import_ConfirmMessage,
            Strings.Import_ChooseFile,
            Strings.Common_Cancel);
        if (!confirmed)
            return;

        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Strings.Import_PickerTitle });
            if (file is null)
                return;

            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            var model = await _importService.ParseImportPackageAsync(memory.ToArray());
            await _importService.ApplyImportAsync(model);

            WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage(this));
            WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(null, this));
            await LoadAsync();
            await _notificationService.RescheduleAsync();
            await _feedbackService.ShowAsync(Strings.Import_Complete, string.Format(LocalizationManager.Instance.Culture, Strings.Import_Loaded, file.FileName));
        }
        catch (Exception ex)
        {
            await _feedbackService.ShowAsync(Strings.Import_Failed, ex.Message);
        }
    }

    private async Task EraseAllAsync()
    {
        var confirmed = await _shellService.DisplayAlertAsync(
            Strings.Settings_EraseConfirmTitle,
            Strings.Settings_EraseConfirmMessage,
            Strings.Settings_EraseAllShort,
            Strings.Common_Cancel,
            destructive: true);
        if (!confirmed)
            return;

        await _occasionService.DeleteAllAsync();
        WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(null, this));
        await _notificationService.RescheduleAsync();

        if (_shellService.Platform != DevicePlatform.Android)
            await _feedbackService.ShowAsync(Strings.Settings_ErasedTitle, Strings.Settings_ErasedDetail);
    }

    protected override void OnLanguageChanged()
    {
        var selected = SelectedLanguage?.Value;
        // This can run inside LoadAsync (SelectedLanguage is set there); restore, never force false,
        // or the rest of LoadAsync would start saving half-loaded values.
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            Languages = BuildLanguages();
            OnPropertyChanged(nameof(Languages));
            _selectedLanguage = Languages.First(l => l.Value == (selected ?? LanguageSetting.System));
            OnPropertyChanged(nameof(SelectedLanguage));
            foreach (var option in ThemeOptions)
                option.RefreshLabel();
        }
        finally
        {
            _isLoading = wasLoading;
        }

        base.OnLanguageChanged();
    }
}

public sealed class ThemeOption(string value, string labelKey, string shortLabelKey) : ObservableObject
{
    private bool _isSelected;

    public string Value { get; } = value;
    public string Label => LocalizationManager.Instance[labelKey];

    /// <summary>Short form for tight layouts, e.g. the desktop segmented control.</summary>
    public string ShortLabel => LocalizationManager.Instance[shortLabelKey];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void RefreshLabel()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(ShortLabel));
    }
}

public sealed record LanguageOption(string Value, string Label);
