using Markd.ViewModels;

namespace Markd
{
    public class SettingsPage : ContentPage
    {
        private readonly SettingsViewModel _viewModel;

        public SettingsPage()
        {
            _viewModel = ServiceHelper.GetRequiredService<SettingsViewModel>();
            BindingContext = _viewModel;
            Title = "Settings";

            // Theme picker
            var themeLabel = new Label { Text = "Theme", FontAttributes = FontAttributes.Bold };
            var themePicker = new Picker();
            themePicker.SetBinding(Picker.ItemsSourceProperty, nameof(SettingsViewModel.Themes));
            themePicker.SetBinding(Picker.SelectedItemProperty, nameof(SettingsViewModel.SelectedTheme));

            // Language picker
            var languageLabel = new Label { Text = "Language", FontAttributes = FontAttributes.Bold };
            var languagePicker = new Picker();
            languagePicker.SetBinding(Picker.ItemsSourceProperty, nameof(SettingsViewModel.Languages));
            languagePicker.SetBinding(Picker.SelectedItemProperty, nameof(SettingsViewModel.SelectedLanguage));

            // Notifications toggle
            var notificationsLabel = new Label { Text = "Notifications", FontAttributes = FontAttributes.Bold };
            var notificationsSwitch = new Switch();
            notificationsSwitch.SetBinding(Switch.IsToggledProperty, nameof(SettingsViewModel.NotificationsEnabled));

            var notificationsRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            notificationsRow.Add(notificationsLabel);
            Grid.SetColumn(notificationsLabel, 0);
            notificationsRow.Add(notificationsSwitch);
            Grid.SetColumn(notificationsSwitch, 1);

            // Save button
            var saveButton = new Button { Text = "Save" };
            saveButton.SetBinding(Button.CommandProperty, nameof(SettingsViewModel.SaveCommand));

            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 20,
                    Spacing = 16,
                    Children =
                    {
                        themeLabel,
                        themePicker,
                        languageLabel,
                        languagePicker,
                        notificationsRow,
                        saveButton
                    }
                }
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.LoadAsync();
        }
    }
}
