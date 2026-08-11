using CommunityToolkit.Mvvm.Input;
using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IAppSettingsService _settingsService;

        private AppSettings _settings = new();
        private string _selectedTheme = "System";
        private string _selectedLanguage = "en";
        private bool _notificationsEnabled = true;

        public SettingsViewModel(IAppSettingsService settingsService)
        {
            _settingsService = settingsService;
            SaveCommand = new AsyncRelayCommand(SaveAsync);
        }

        public IAsyncRelayCommand SaveCommand { get; }

        public List<string> Themes { get; } = ["System", "Light", "Dark"];
        public List<string> Languages { get; } = ["en", "cs"];

        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value))
                    ApplyTheme(value);
            }
        }

        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set => SetProperty(ref _selectedLanguage, value);
        }

        public bool NotificationsEnabled
        {
            get => _notificationsEnabled;
            set => SetProperty(ref _notificationsEnabled, value);
        }

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                _settings = await _settingsService.GetAsync();
                SelectedTheme = _settings.Theme;
                SelectedLanguage = _settings.Language;
                NotificationsEnabled = _settings.NotificationsEnabled;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveAsync()
        {
            _settings.Theme = SelectedTheme;
            _settings.Language = SelectedLanguage;
            _settings.NotificationsEnabled = NotificationsEnabled;
            await _settingsService.SaveAsync(_settings);
        }

        private static void ApplyTheme(string theme)
        {
            if (Application.Current is null) return;

            Application.Current.UserAppTheme = theme switch
            {
                "Light" => AppTheme.Light,
                "Dark"  => AppTheme.Dark,
                _       => AppTheme.Unspecified
            };
        }
    }
}
