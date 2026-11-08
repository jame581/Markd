using Markd.Localization;
using Markd.Services.Sync;
using Markd.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace Markd.Controls;

/// <summary>
/// The sync activity mark for both layouts: a spinning sync glyph while a sync runs, a warning glyph when sync needs
/// the user, hidden otherwise. Follows <see cref="SyncCoordinator.StatusChanged"/> while it is loaded and raises
/// <see cref="Tapped"/>; the host decides what a tap opens. Screen readers hear "Sync status" with the status text as
/// the hint.
/// </summary>
public sealed class SyncActivityIcon : Border
{
    public static readonly BindableProperty SyncGlyphProperty =
        BindableProperty.Create(nameof(SyncGlyph), typeof(string), typeof(SyncActivityIcon), "Sync", propertyChanged: (b, _, _) => ((SyncActivityIcon)b).Update());

    public static readonly BindableProperty WarningGlyphProperty =
        BindableProperty.Create(nameof(WarningGlyph), typeof(string), typeof(SyncActivityIcon), "Warning", propertyChanged: (b, _, _) => ((SyncActivityIcon)b).Update());

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(SyncActivityIcon), 22d, propertyChanged: (b, _, n) => ((SyncActivityIcon)b)._icon.Size = (double)n);

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(SyncActivityIcon), 48d, propertyChanged: (b, _, n) => ((SyncActivityIcon)b).ApplySize((double)n));

    private const string SpinAnimation = "SyncSpin";

    private readonly SvgIcon _icon = new() { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Size = 22 };
    private SyncCoordinator? _sync;
    private bool _spinning;

    public SyncActivityIcon()
    {
        StrokeThickness = 0;
        BackgroundColor = Colors.Transparent;
        Content = _icon;
        IsVisible = false;
        ApplySize(48);
        SetBinding(SemanticProperties.DescriptionProperty, Tr.Bind("Sync_StatusDescription"));

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Tapped?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler? Tapped;

    public string SyncGlyph { get => (string)GetValue(SyncGlyphProperty); set => SetValue(SyncGlyphProperty, value); }
    public string WarningGlyph { get => (string)GetValue(WarningGlyphProperty); set => SetValue(WarningGlyphProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>The status kind shown, or Off before the coordinator is known.</summary>
    public SyncStatusKind Kind => _sync?.Status.Kind ?? SyncStatusKind.Off;

    /// <summary>The kinds where sync waits for the user: the warning glyph.</summary>
    public static bool NeedsUser(SyncStatusKind kind) => kind is SyncStatusKind.Conflicts or SyncStatusKind.NeedsPassphrase
        or SyncStatusKind.SignInAgain or SyncStatusKind.UpdateApp or SyncStatusKind.Unreadable;

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_sync is null)
        {
            try
            {
                _sync = ServiceHelper.GetRequiredService<SyncCoordinator>();
            }
            catch (InvalidOperationException)
            {
                return;   // no app services (designer): stay hidden
            }
        }

        _sync.StatusChanged -= OnStatusChanged;
        _sync.StatusChanged += OnStatusChanged;
        Update();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_sync is not null)
            _sync.StatusChanged -= OnStatusChanged;
        StopSpin();
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(Update);
        else
            Update();
    }

    private void Update()
    {
        var kind = Kind;
        if (_sync is not null)
            SemanticProperties.SetHint(this, SyncStatusText.Describe(_sync.Status, DateTime.UtcNow));
        if (kind == SyncStatusKind.Syncing)
        {
            _icon.Glyph = SyncGlyph;
            ThemeColors.Bind(_icon, SvgIcon.ColorProperty, "TextSecondary");
            IsVisible = true;
            StartSpin();
        }
        else if (NeedsUser(kind))
        {
            StopSpin();
            _icon.Glyph = WarningGlyph;
            ThemeColors.Bind(_icon, SvgIcon.ColorProperty, "Danger");
            IsVisible = true;
        }
        else
        {
            StopSpin();
            IsVisible = false;
        }
    }

    private void StartSpin()
    {
        if (_spinning)
            return;

        _spinning = true;
        _icon.Rotation = 0;
        _icon.Animate(SpinAnimation, new Animation(v => _icon.Rotation = v, 0, 360), length: 1100, easing: Easing.Linear,
            repeat: () => _spinning);
    }

    private void StopSpin()
    {
        if (!_spinning)
            return;

        _spinning = false;
        _icon.AbortAnimation(SpinAnimation);
        _icon.Rotation = 0;
    }

    private void ApplySize(double size)
    {
        WidthRequest = HeightRequest = size;
        StrokeShape = new RoundRectangle { CornerRadius = size / 2 };
    }
}
