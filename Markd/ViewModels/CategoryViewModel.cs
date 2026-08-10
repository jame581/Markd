using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.ViewModels;

public class CategoryViewModel : ViewModelBase
{
    private readonly ICategoryService _categoryService;
    private string _newCategoryName = string.Empty;

    public CategoryViewModel(ICategoryService categoryService)
    {
        _categoryService = categoryService;
        AddCommand = new AsyncRelayCommand(AddAsync);
        DeleteCommand = new AsyncRelayCommand<Category?>(DeleteAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ObservableCollection<Category> Categories { get; } = new();

    public string NewCategoryName
    {
        get => _newCategoryName;
        set => SetProperty(ref _newCategoryName, value);
    }

    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand<Category?> DeleteCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            Categories.Clear();

            var categories = await _categoryService.GetAllAsync();
            foreach (var category in categories)
                Categories.Add(category);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            ErrorMessage = "Category name is required.";
            return;
        }

        await _categoryService.CreateAsync(new Category
        {
            Name = NewCategoryName.Trim()
        });

        NewCategoryName = string.Empty;
        await LoadAsync();
    }

    private async Task DeleteAsync(Category? category)
    {
        if (category == null)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync("Delete Category", $"Delete '{category.Name}'?", "Delete", "Cancel");
        if (!confirmed)
            return;

        await _categoryService.DeleteAsync(category.Id);
        await LoadAsync();
    }
}
