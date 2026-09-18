namespace Markd.Desktop;

/// <summary>
/// One rail destination hosted by <see cref="DesktopShellPage"/>. The shell draws the header strip
/// from these values and calls <see cref="ShowAsync"/> / <see cref="Hide"/> as the rail selection changes.
/// </summary>
public interface IDesktopSection
{
    string Title { get; }
    string Subtitle { get; }

    /// <summary>Home shows "+ New occasion" at the right of the header.</summary>
    bool ShowsNewOccasion { get; }

    /// <summary>False on Home and Detail, where the header's bottom rule is transparent.</summary>
    bool HasHeaderRule { get; }

    /// <summary>A back affordance above the title (the collapsed Home detail), or null.</summary>
    string? BackLabel { get; }

    /// <summary>Raised when any header value changes while the section is visible.</summary>
    event EventHandler? HeaderChanged;

    Task ShowAsync();
    void Hide();

    /// <summary>Handles the header back affordance or a ".." route. Returns false when there is nothing to go back from.</summary>
    bool GoBack();
}
