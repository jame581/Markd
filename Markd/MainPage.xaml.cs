using Markd.Core.Domain;
using Markd.ViewModels;

namespace Markd
{
    public partial class MainPage : ContentPage
    {
        private readonly OccasionListViewModel _viewModel;

        public MainPage()
        {
            _viewModel = ServiceHelper.GetRequiredService<OccasionListViewModel>();
            BindingContext = _viewModel;
            Title = "Markd";

            var addButton = new Button { Text = "Add Occasion" };
            addButton.SetBinding(Button.CommandProperty, nameof(OccasionListViewModel.AddCommand));

            var collectionView = new CollectionView
            {
                SelectionMode = SelectionMode.Single,
                EmptyView = new VerticalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Label { Text = "No occasions yet.", FontSize = 18, FontAttributes = FontAttributes.Bold },
                        new Label { Text = "Tap 'Add Occasion' to create your first tracker." }
                    }
                }
            };

            collectionView.SetBinding(ItemsView.ItemsSourceProperty, nameof(OccasionListViewModel.Occasions));
            collectionView.SelectionChanged += OnOccasionSelected;

            collectionView.ItemTemplate = new DataTemplate(() =>
            {
                var emoji = new Label { FontSize = 20 };
                emoji.SetBinding(Label.TextProperty, nameof(Occasion.Emoji));

                var title = new Label { FontSize = 18, FontAttributes = FontAttributes.Bold };
                title.SetBinding(Label.TextProperty, nameof(Occasion.Title));

                var pinned = new Label { Text = "PINNED", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0A7C2F") };
                pinned.SetBinding(IsVisibleProperty, nameof(Occasion.IsPinned));

                var row = new HorizontalStackLayout { Spacing = 8, Children = { emoji, title, pinned } };

                var direction = new Label();
                direction.SetBinding(Label.TextProperty, nameof(Occasion.Direction));

                var anchor = new Label();
                anchor.SetBinding(Label.TextProperty, new Binding(nameof(Occasion.AnchorDate), stringFormat: "Anchor: {0:D}"));

                return new Frame
                {
                    Padding = 12,
                    Margin = new Thickness(0, 4),
                    BorderColor = Color.FromArgb("#DDDDDD"),
                    Content = new VerticalStackLayout { Spacing = 4, Children = { row, direction, anchor } }
                };
            });

            var layoutGrid = new Grid
            {
                Padding = 16,
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star)
                },
                RowSpacing = 12
            };

            layoutGrid.Add(addButton);
            Grid.SetRow(addButton, 0);

            layoutGrid.Add(collectionView);
            Grid.SetRow(collectionView, 1);

            Content = layoutGrid;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.LoadAsync();
        }

        private async void OnOccasionSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not Occasion occasion)
                return;

            await _viewModel.OpenDetailCommand.ExecuteAsync(occasion);

            if (sender is CollectionView collectionView)
                collectionView.SelectedItem = null;
        }
    }
}
