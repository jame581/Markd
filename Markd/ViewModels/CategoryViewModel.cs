using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Services;
using Microsoft.Maui.Devices;

namespace Markd.ViewModels;

public class CategoryViewModel : ViewModelBase
{
    private static readonly string[] DefaultEmoji = ["🏷️", "💜", "🌿", "🗺️", "🏠", "🎓", "💼", "🎉"];

    private readonly ICategoryService _categoryService;
    private readonly IOccasionService _occasionService;
    private readonly IAppShellService _shellService;
    private readonly IFeedbackService _feedbackService;
    private bool _isEditorOpen;
    private int _editingId;
    private string _editorName = string.Empty;
    private string? _editorEmoji;
    private string? _editorColorHex;
    private string? _editorError;
    private bool _hasLoaded;

    public CategoryViewModel(
        ICategoryService categoryService,
        IOccasionService occasionService,
        IAppShellService shellService,
        IFeedbackService feedbackService)
    {
        _categoryService = categoryService;
        _occasionService = occasionService;
        _shellService = shellService;
        _feedbackService = feedbackService;
        NewCommand = new RelayCommand(BeginNew);
        EditCommand = new RelayCommand<CategoryRow?>(row => { if (row is not null) BeginEdit(row); });
        DeleteCommand = new AsyncRelayCommand<CategoryRow?>(DeleteAsync);
        SaveEditorCommand = new AsyncRelayCommand(SaveEditorAsync);
        CancelEditorCommand = new RelayCommand(() => IsEditorOpen = false);
        SelectEditorColorCommand = new RelayCommand<string?>(hex => { EditorColorHex = hex; UpdateColorSelection(); });
        SelectEditorEmojiCommand = new RelayCommand<string?>(emoji => EditorEmoji = emoji);

        foreach (var hex in OccasionFormViewModel.ColorSwatches)
            EditorColorChoices.Add(new ColorChoice(hex));

        WeakReferenceMessenger.Default.Register<CategoryViewModel, OccasionsChangedMessage>(this, (vm, _) => vm.ReloadIfLoaded());
        WeakReferenceMessenger.Default.Register<CategoryViewModel, CategoriesChangedMessage>(this, (vm, message) =>
        {
            if (!ReferenceEquals(message.Source, vm))
                vm.ReloadIfLoaded();
        });
    }

    public ObservableCollection<CategoryRow> Rows { get; } = new();
    public ObservableCollection<ColorChoice> EditorColorChoices { get; } = new();
    public static IReadOnlyList<string> EmojiChoices => OccasionFormViewModel.EmojiChoices;

    public bool IsEmpty => _hasLoaded && Rows.Count == 0;

    public IRelayCommand NewCommand { get; }
    public IRelayCommand<CategoryRow?> EditCommand { get; }
    public IAsyncRelayCommand<CategoryRow?> DeleteCommand { get; }
    public IAsyncRelayCommand SaveEditorCommand { get; }
    public IRelayCommand CancelEditorCommand { get; }
    public IRelayCommand<string?> SelectEditorColorCommand { get; }
    public IRelayCommand<string?> SelectEditorEmojiCommand { get; }

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set => SetProperty(ref _isEditorOpen, value);
    }

    public bool IsEditingExisting => _editingId != 0;
    public string EditorTitle => _editingId == 0 ? Strings.Category_New : Strings.Category_Edit;
    public string EditorSaveLabel => _editingId == 0 ? Strings.Common_Add : Strings.Common_Save;

    public string EditorName
    {
        get => _editorName;
        set
        {
            if (SetProperty(ref _editorName, value) && !string.IsNullOrWhiteSpace(value))
                EditorError = null;
        }
    }

    public string? EditorEmoji
    {
        get => _editorEmoji;
        set => SetProperty(ref _editorEmoji, value);
    }

    public string? EditorColorHex
    {
        get => _editorColorHex;
        set => SetProperty(ref _editorColorHex, value);
    }

    public string? EditorError
    {
        get => _editorError;
        set => SetProperty(ref _editorError, value);
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var categories = await _categoryService.GetAllAsync();
            var counts = (await _occasionService.GetAllAsync())
                .Where(o => o.CategoryId.HasValue)
                .GroupBy(o => o.CategoryId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            Rows.Clear();
            foreach (var category in categories)
                Rows.Add(new CategoryRow(category, counts.GetValueOrDefault(category.Id)));

            _hasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
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

    public void BeginNew()
    {
        _editingId = 0;
        EditorName = string.Empty;
        EditorEmoji = DefaultEmoji[Rows.Count % DefaultEmoji.Length];
        EditorColorHex = OccasionFormViewModel.ColorSwatches[Rows.Count % OccasionFormViewModel.ColorSwatches.Count];
        OpenEditor();
    }

    public void BeginEdit(CategoryRow row)
    {
        _editingId = row.Category.Id;
        EditorName = row.Name;
        EditorEmoji = row.Emoji;
        EditorColorHex = row.ColorHex;
        OpenEditor();
    }

    private void OpenEditor()
    {
        EditorError = null;
        UpdateColorSelection();
        OnPropertyChanged(nameof(IsEditingExisting));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorSaveLabel));
        IsEditorOpen = true;
    }

    private async Task SaveEditorAsync()
    {
        if (string.IsNullOrWhiteSpace(EditorName))
        {
            EditorError = Strings.Category_NameRequired;
            return;
        }

        var name = EditorName.Trim();
        var isNew = _editingId == 0;
        var category = new Category
        {
            Id = _editingId,
            Name = name,
            Emoji = string.IsNullOrWhiteSpace(EditorEmoji) ? null : EditorEmoji,
            ColorHex = EditorColorHex
        };

        if (isNew)
            await _categoryService.CreateAsync(category);
        else
            await _categoryService.UpdateAsync(category);

        IsEditorOpen = false;
        await LoadAsync();
        WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage(this));

        if (_shellService.Platform != DevicePlatform.Android)
            await _feedbackService.ShowAsync(
                isNew ? Strings.Category_AddedTitle : Strings.Category_SavedTitle,
                isNew ? string.Format(Strings.Category_ReadyDetail, name) : null);
    }

    private async Task DeleteAsync(CategoryRow? row)
    {
        if (row is null)
            return;

        var category = row.Category;
        var android = _shellService.Platform == DevicePlatform.Android;

        if (!android)
        {
            var confirmed = await _shellService.DisplayAlertAsync(
                string.Format(Strings.Category_DeleteConfirmTitle, category.Name),
                Strings.Category_DeleteConfirmMessage,
                Strings.Common_Delete,
                Strings.Common_Cancel,
                destructive: true);
            if (!confirmed)
                return;
        }

        var affectedOccasionIds = (await _occasionService.GetAllAsync())
            .Where(occasion => occasion.CategoryId == category.Id)
            .Select(occasion => occasion.Id)
            .ToList();

        var snapshot = new Category { Name = category.Name, Emoji = category.Emoji, ColorHex = category.ColorHex };
        await _categoryService.DeleteAsync(category.Id);
        await LoadAsync();
        WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage(this));

        if (android)
        {
            if (await _feedbackService.ShowUndoAsync(string.Format(Strings.Category_DeletedUndo, category.Name)))
                await RestoreAsync(snapshot, affectedOccasionIds);
        }
        else
        {
            await _feedbackService.ShowAsync(Strings.Category_DeletedTitle, string.Format(Strings.Category_DeletedDetail, category.Name));
        }
    }

    private async Task RestoreAsync(Category snapshot, IReadOnlyList<int> affectedOccasionIds)
    {
        var restored = await _categoryService.CreateAsync(snapshot);

        foreach (var occasionId in affectedOccasionIds)
        {
            var occasion = await _occasionService.GetByIdAsync(occasionId);
            if (occasion == null)
                continue;

            occasion.CategoryId = restored.Id;
            await _occasionService.UpdateAsync(occasion);
        }

        await LoadAsync();
        WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage(this));
        WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(null, this));
    }

    private void UpdateColorSelection()
    {
        foreach (var choice in EditorColorChoices)
            choice.IsSelected = string.Equals(choice.Hex, EditorColorHex, StringComparison.OrdinalIgnoreCase);
    }

    private void ReloadIfLoaded()
    {
        if (_hasLoaded)
            Application.Current?.Dispatcher.Dispatch(async () => await LoadAsync());
    }

    protected override void OnLanguageChanged() => ReloadIfLoaded();
}

public sealed class CategoryRow(Category category, int occasionCount)
{
    public Category Category { get; } = category;
    public string Name => Category.Name;
    public string? Emoji => Category.Emoji;
    public string? ColorHex => Category.ColorHex;
    public Color DotColor => Controls.HexColor.Parse(Category.ColorHex);
    public int OccasionCount { get; } = occasionCount;
    public string CountLabel => OccasionCount == 0
        ? Strings.Category_RowEmpty
        : Plural.Format("Occasion_Count", OccasionCount);
}
