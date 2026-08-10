using Markd.Core.Domain;
using Markd.ViewModels;

namespace Markd;

public class CategoryPage : ContentPage
{
    private readonly CategoryViewModel _viewModel;

    public CategoryPage()
    {
        _viewModel = ServiceHelper.GetRequiredService<CategoryViewModel>();
        BindingContext = _viewModel;
        Title = "Categories";

        var nameEntry = new Entry { Placeholder = "New category name" };
        nameEntry.SetBinding(Entry.TextProperty, nameof(CategoryViewModel.NewCategoryName));

        var addButton = new Button { Text = "Add Category" };
        addButton.SetBinding(Button.CommandProperty, nameof(CategoryViewModel.AddCommand));

        var errorLabel = new Label { TextColor = Colors.Red };
        errorLabel.SetBinding(Label.TextProperty, nameof(CategoryViewModel.ErrorMessage));

        var collection = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            EmptyView = new Label { Text = "No categories yet." }
        };
        collection.SetBinding(ItemsView.ItemsSourceProperty, nameof(CategoryViewModel.Categories));

        collection.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { FontSize = 16, VerticalOptions = LayoutOptions.Center };
            name.SetBinding(Label.TextProperty, nameof(Category.Name));

            var deleteButton = new Button
            {
                Text = "Delete",
                BackgroundColor = Color.FromArgb("#D9534F"),
                TextColor = Colors.White,
                Padding = new Thickness(10, 4)
            };
            deleteButton.SetBinding(Button.CommandProperty, new Binding(nameof(CategoryViewModel.DeleteCommand), source: _viewModel));
            deleteButton.SetBinding(Button.CommandParameterProperty, new Binding("."));

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Padding = new Thickness(0, 6)
            };

            row.Add(name);
            Grid.SetColumn(name, 0);

            row.Add(deleteButton);
            Grid.SetColumn(deleteButton, 1);

            return row;
        });

        Content = new VerticalStackLayout
        {
            Padding = 16,
            Spacing = 12,
            Children =
            {
                nameEntry,
                addButton,
                errorLabel,
                collection
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
