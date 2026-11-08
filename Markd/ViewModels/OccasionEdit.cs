using Markd.Core.Domain;

namespace Markd.ViewModels;

/// <summary>The editable fields of an occasion, as the form showed them when it opened or holds them at save.</summary>
public sealed record OccasionFields(
    string Title,
    string? Emoji,
    string? ColorHex,
    DateTime AnchorDate,
    OccasionDirection Direction,
    string? Notes,
    bool IsPinned,
    int? CategoryId);

/// <summary>
/// A save writes only what the user changed. A form opened before a sync applied another device's edit would
/// otherwise save the old values back over it, and the next sync would upload them as a local edit.
/// </summary>
public static class OccasionEdit
{
    /// <summary>Copies onto <paramref name="current"/> each field where <paramref name="edited"/> differs from <paramref name="loaded"/>.</summary>
    public static Occasion ApplyChanges(Occasion current, OccasionFields loaded, OccasionFields edited)
    {
        if (edited.Title != loaded.Title)
            current.Title = edited.Title;
        if (edited.Emoji != loaded.Emoji)
            current.Emoji = edited.Emoji;
        if (edited.ColorHex != loaded.ColorHex)
            current.ColorHex = edited.ColorHex;
        if (edited.AnchorDate != loaded.AnchorDate)
            current.AnchorDate = edited.AnchorDate;
        if (edited.Direction != loaded.Direction)
            current.Direction = edited.Direction;
        if (edited.Notes != loaded.Notes)
            current.Notes = edited.Notes;
        if (edited.IsPinned != loaded.IsPinned)
            current.IsPinned = edited.IsPinned;
        if (edited.CategoryId != loaded.CategoryId)
            current.CategoryId = edited.CategoryId;
        return current;
    }
}

/// <summary>The editable fields of a category, as the editor showed them when it opened or holds them at save.</summary>
public sealed record CategoryFields(string Name, string? Emoji, string? ColorHex);

/// <summary>The category editor's equivalent of <see cref="OccasionEdit"/>.</summary>
public static class CategoryEdit
{
    /// <summary>Copies onto <paramref name="current"/> each field where <paramref name="edited"/> differs from <paramref name="loaded"/>.</summary>
    public static Category ApplyChanges(Category current, CategoryFields loaded, CategoryFields edited)
    {
        if (edited.Name != loaded.Name)
            current.Name = edited.Name;
        if (edited.Emoji != loaded.Emoji)
            current.Emoji = edited.Emoji;
        if (edited.ColorHex != loaded.ColorHex)
            current.ColorHex = edited.ColorHex;
        return current;
    }
}
