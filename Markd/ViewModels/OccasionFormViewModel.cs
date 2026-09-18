using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Services;
using Microsoft.Maui.Devices;

namespace Markd.ViewModels;

public class OccasionFormViewModel : ViewModelBase
{
    private readonly Category _noneCategory = new() { Id = 0, Name = OccasionGroup.UncategorisedName };

    private readonly IOccasionService _occasionService;
    private readonly ICategoryService _categoryService;
    private readonly IAppShellService _shellService;
    private readonly IFeedbackService _feedbackService;
    private string _title = string.Empty;
    private string? _emoji;
    private string? _colorHex;
    private DateTime _anchorDate = DateTime.Today;
    private OccasionDirection _direction = OccasionDirection.Since;
    private string? _notes;
    private bool _isPinned;
    private Category? _selectedCategory;

    public OccasionFormViewModel(
        IOccasionService occasionService,
        ICategoryService categoryService,
        IAppShellService shellService,
        IFeedbackService feedbackService)
    {
        _occasionService = occasionService;
        _categoryService = categoryService;
        _shellService = shellService;
        _feedbackService = feedbackService;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new AsyncRelayCommand(() => _shellService.GoToAsync(".."));
        SelectColorCommand = new RelayCommand<string?>(SelectColor);
        SelectEmojiCommand = new RelayCommand<string?>(value => Emoji = value);
        SetDirectionCommand = new RelayCommand<string?>(value =>
            Direction = value == nameof(OccasionDirection.Until) ? OccasionDirection.Until : OccasionDirection.Since);
        TogglePinCommand = new RelayCommand(() => IsPinned = !IsPinned);

        foreach (var hex in ColorSwatches)
            ColorChoices.Add(new ColorChoice(hex));
    }

    public int OccasionId { get; private set; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand<string?> SelectColorCommand { get; }
    public IRelayCommand<string?> SelectEmojiCommand { get; }
    public IRelayCommand<string?> SetDirectionCommand { get; }
    public IRelayCommand TogglePinCommand { get; }
    public ObservableCollection<Category> Categories { get; } = new();

    /// <summary>The twelve identity swatches.</summary>
    public static IReadOnlyList<string> ColorSwatches { get; } =
    [
        "#E53935", "#F4511E", "#F6BF26", "#33B679",
        "#0B8043", "#039BE5", "#3F51B5", "#7986CB",
        "#8E24AA", "#795548", "#616161", "#000000"
    ];

    /// <summary>Quick picks for the icon; the first six are the desktop's inline row.</summary>
    public static IReadOnlyList<string> EmojiChoices { get; } =
    [
        "💕", "🌱", "✈️", "👶", "🏃", "🎉",
        "🎂", "💍", "🏠", "🎓", "💼", "🐶",
        "🐱", "🌿", "☕", "🚭", "🍷", "💪",
        "📚", "🎵", "⚽", "🧘", "🌍", "⭐"
    ];

    public static IReadOnlyList<string> QuickEmojiChoices { get; } = EmojiChoices.Take(6).ToList();

    public ObservableCollection<ColorChoice> ColorChoices { get; } = new();

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value) && !string.IsNullOrWhiteSpace(value))
                ErrorMessage = null;
        }
    }

    public string? Emoji
    {
        get => _emoji;
        set => SetProperty(ref _emoji, value);
    }

    public string? ColorHex
    {
        get => _colorHex;
        set => SetProperty(ref _colorHex, value);
    }

    public DateTime AnchorDate
    {
        get => _anchorDate;
        set
        {
            if (SetProperty(ref _anchorDate, value.Date))
                RaisePreview();
        }
    }

    public OccasionDirection Direction
    {
        get => _direction;
        set
        {
            if (SetProperty(ref _direction, value))
            {
                OnPropertyChanged(nameof(IsSinceSelected));
                OnPropertyChanged(nameof(IsUntilSelected));
                RaisePreview();
            }
        }
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public Category? SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    public bool IsNew => OccasionId == 0;
    public string Header => IsNew ? "New occasion" : "Edit occasion";
    public string Subtitle => IsNew ? "Pick a date, give it a name" : "Changes save when you press Save";
    public string SaveLabel => IsNew ? "Create occasion" : "Save changes";

    public bool IsSinceSelected
    {
        get => Direction == OccasionDirection.Since;
        set { if (value) Direction = OccasionDirection.Since; }
    }

    public bool IsUntilSelected
    {
        get => Direction == OccasionDirection.Until;
        set { if (value) Direction = OccasionDirection.Until; }
    }

    public string AnchorDateText => OccasionMath.FormatShortDate(AnchorDate);

    /// <summary>The count the occasion will read, shown live in the preview card.</summary>
    public int PreviewDays => Math.Abs(PreviewSignedDays);

    /// <summary>The plural-correct unit word next to <see cref="PreviewDays"/> (e.g. "day"/"days", "den"/"dny"/"dní").</summary>
    public string PreviewDaysUnit => Plural.Format("Occasion_UnitDays", PreviewDays);

    public string PreviewCaption
    {
        get
        {
            var date = AnchorDateText;
            var days = PreviewSignedDays;
            if (Direction == OccasionDirection.Since)
            {
                if (days < 0)
                    return $"days until the counter starts on {date}";
                return days == 0 ? $"the counter starts today, {date}" : $"days since {date} — the counter starts here";
            }

            return days >= 0 ? $"days until {date}" : $"days since {date}";
        }
    }

    private int PreviewSignedDays => Direction == OccasionDirection.Since
        ? (DateTime.Today - AnchorDate.Date).Days
        : (AnchorDate.Date - DateTime.Today).Days;

    public async Task InitializeAsync(int id)
    {
        OccasionId = id;
        ErrorMessage = null;

        await LoadCategoriesAsync();

        if (id == 0)
        {
            Title = string.Empty;
            Emoji = "🎉";
            ColorHex = null;
            AnchorDate = DateTime.Today;
            Direction = OccasionDirection.Since;
            Notes = null;
            IsPinned = false;
            SelectedCategory = _noneCategory;
        }
        else
        {
            var occasion = await _occasionService.GetByIdAsync(id);
            if (occasion == null)
            {
                ErrorMessage = "Occasion not found.";
                return;
            }

            Title = occasion.Title;
            Emoji = occasion.Emoji;
            ColorHex = occasion.ColorHex;
            AnchorDate = OccasionDates.ToLocalDate(occasion.AnchorDate);
            Direction = occasion.Direction;
            Notes = occasion.Notes;
            IsPinned = occasion.IsPinned;
            SelectedCategory = occasion.CategoryId.HasValue
                ? Categories.FirstOrDefault(c => c.Id == occasion.CategoryId.Value) ?? _noneCategory
                : _noneCategory;
        }

        UpdateColorSelection();
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SaveLabel));
        RaisePreview();
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Title is required.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var occasion = new Occasion
            {
                Id = OccasionId,
                Title = Title.Trim(),
                Emoji = string.IsNullOrWhiteSpace(Emoji) ? null : Emoji.Trim(),
                ColorHex = string.IsNullOrWhiteSpace(ColorHex) ? null : ColorHex.Trim(),
                AnchorDate = DateTime.SpecifyKind(AnchorDate.Date, DateTimeKind.Local).ToUniversalTime(),
                Direction = Direction,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                IsPinned = IsPinned,
                CategoryId = SelectedCategory is { Id: > 0 } category ? category.Id : null
            };

            var isNew = OccasionId == 0;
            var saved = isNew
                ? await _occasionService.CreateAsync(occasion)
                : await _occasionService.UpdateAsync(occasion);

            WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(saved.Id, this));
            await _shellService.GoToAsync("..");

            if (_shellService.Platform != DevicePlatform.Android)
            {
                var anchor = OccasionMath.FormatShortDate(AnchorDate);
                await _feedbackService.ShowAsync(
                    isNew ? "Occasion created" : "Changes saved",
                    $"{saved.Title} · counting {(Direction == OccasionDirection.Since ? "since" : "until")} {anchor}");
            }
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

    private void SelectColor(string? hex)
    {
        ColorHex = hex;
        UpdateColorSelection();
    }

    private void UpdateColorSelection()
    {
        foreach (var choice in ColorChoices)
            choice.IsSelected = string.Equals(choice.Hex, ColorHex, StringComparison.OrdinalIgnoreCase);
    }

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(AnchorDateText));
        OnPropertyChanged(nameof(PreviewDays));
        OnPropertyChanged(nameof(PreviewDaysUnit));
        OnPropertyChanged(nameof(PreviewCaption));
    }

    private async Task LoadCategoriesAsync()
    {
        Categories.Clear();
        Categories.Add(_noneCategory);

        foreach (var category in await _categoryService.GetAllAsync())
            Categories.Add(category);
    }

    protected override void OnLanguageChanged()
    {
        _noneCategory.Name = OccasionGroup.UncategorisedName;
        var index = Categories.IndexOf(_noneCategory);
        if (index >= 0)
            Categories[index] = _noneCategory;

        base.OnLanguageChanged();
    }
}
