using CommunityToolkit.Mvvm.Input;
using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.ViewModels;

public class OccasionFormViewModel : ViewModelBase
{
    private readonly IOccasionService _occasionService;
    private string _title = string.Empty;
    private string? _emoji;
    private string? _colorHex;
    private DateTime _anchorDate = DateTime.Today;
    private OccasionDirection _direction = OccasionDirection.Since;
    private string? _notes;
    private bool _isPinned;

    public OccasionFormViewModel(IOccasionService occasionService)
    {
        _occasionService = occasionService;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public int OccasionId { get; private set; }
    public IAsyncRelayCommand SaveCommand { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
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
        set => SetProperty(ref _anchorDate, value);
    }

    public OccasionDirection Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value);
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

    public string Header => OccasionId == 0 ? "New Occasion" : "Edit Occasion";

    public async Task InitializeAsync(int id)
    {
        OccasionId = id;
        ErrorMessage = null;

        if (id == 0)
        {
            Title = string.Empty;
            Emoji = null;
            ColorHex = null;
            AnchorDate = DateTime.Today;
            Direction = OccasionDirection.Since;
            Notes = null;
            IsPinned = false;
            OnPropertyChanged(nameof(Header));
            return;
        }

        var occasion = await _occasionService.GetByIdAsync(id);
        if (occasion == null)
        {
            ErrorMessage = "Occasion not found.";
            return;
        }

        Title = occasion.Title;
        Emoji = occasion.Emoji;
        ColorHex = occasion.ColorHex;
        AnchorDate = occasion.AnchorDate.ToLocalTime().Date;
        Direction = occasion.Direction;
        Notes = occasion.Notes;
        IsPinned = occasion.IsPinned;
        OnPropertyChanged(nameof(Header));
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
                AnchorDate = AnchorDate.ToUniversalTime(),
                Direction = Direction,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                IsPinned = IsPinned
            };

            if (OccasionId == 0)
                await _occasionService.CreateAsync(occasion);
            else
                await _occasionService.UpdateAsync(occasion);

            await Shell.Current.GoToAsync("..");
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
}
