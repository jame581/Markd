using Markd.Core.Domain;
using Markd.ViewModels;
using Xunit;

namespace Markd.Tests;

public class OccasionEditTests
{
    private static readonly DateTime LoadedAnchor = new(2026, 1, 10, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CurrentAnchor = new(2026, 2, 20, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EditedAnchor = new(2026, 3, 30, 22, 0, 0, DateTimeKind.Utc);

    private static readonly OccasionFields Loaded = new(
        "Loaded", "🎉", "#E53935", LoadedAnchor, OccasionDirection.Since, "loaded notes", false, 1);

    /// <summary>The record as it is now, after another device changed every field.</summary>
    private static Occasion Current() => new()
    {
        Id = 7,
        Title = "Current",
        Emoji = "🌱",
        ColorHex = "#039BE5",
        AnchorDate = CurrentAnchor,
        Direction = OccasionDirection.Until,
        Notes = "current notes",
        IsPinned = true,
        CategoryId = 2
    };

    [Fact]
    public void Nothing_edited_keeps_every_current_value()
    {
        var result = OccasionEdit.ApplyChanges(Current(), Loaded, Loaded);

        Assert.Equal("Current", result.Title);
        Assert.Equal("🌱", result.Emoji);
        Assert.Equal("#039BE5", result.ColorHex);
        Assert.Equal(CurrentAnchor, result.AnchorDate);
        Assert.Equal(OccasionDirection.Until, result.Direction);
        Assert.Equal("current notes", result.Notes);
        Assert.True(result.IsPinned);
        Assert.Equal(2, result.CategoryId);
    }

    [Fact]
    public void Returns_the_current_record()
    {
        var current = Current();

        Assert.Same(current, OccasionEdit.ApplyChanges(current, Loaded, Loaded with { Title = "Edited" }));
    }

    public static TheoryData<string> FieldNames =>
        ["Title", "Emoji", "ColorHex", "AnchorDate", "Direction", "Notes", "IsPinned", "CategoryId"];

    [Theory]
    [MemberData(nameof(FieldNames))]
    public void Only_the_edited_field_takes_the_edited_value(string field)
    {
        var edited = field switch
        {
            "Title" => Loaded with { Title = "Edited" },
            "Emoji" => Loaded with { Emoji = "🎂" },
            "ColorHex" => Loaded with { ColorHex = "#000000" },
            "AnchorDate" => Loaded with { AnchorDate = EditedAnchor },
            "Direction" => Loaded with { Direction = OccasionDirection.Until },
            "Notes" => Loaded with { Notes = "edited notes" },
            "IsPinned" => Loaded with { IsPinned = true },
            "CategoryId" => Loaded with { CategoryId = null },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var result = OccasionEdit.ApplyChanges(Current(), Loaded, edited);

        Assert.Equal(field == "Title" ? "Edited" : "Current", result.Title);
        Assert.Equal(field == "Emoji" ? "🎂" : "🌱", result.Emoji);
        Assert.Equal(field == "ColorHex" ? "#000000" : "#039BE5", result.ColorHex);
        Assert.Equal(field == "AnchorDate" ? EditedAnchor : CurrentAnchor, result.AnchorDate);
        // Loaded and current both differ from the edit here, so the edited Since only shows when it was chosen.
        Assert.Equal(OccasionDirection.Until, result.Direction);
        Assert.Equal(field == "Notes" ? "edited notes" : "current notes", result.Notes);
        Assert.True(result.IsPinned);
        Assert.Equal(field == "CategoryId" ? null : 2, result.CategoryId);
    }

    [Fact]
    public void An_edited_direction_replaces_the_current_one()
    {
        var current = Current();
        current.Direction = OccasionDirection.Since;
        var loaded = Loaded with { Direction = OccasionDirection.Until };

        var result = OccasionEdit.ApplyChanges(current, loaded, loaded with { Direction = OccasionDirection.Since });

        Assert.Equal(OccasionDirection.Since, result.Direction);
    }

    [Fact]
    public void An_unpin_replaces_the_current_pin()
    {
        var loaded = Loaded with { IsPinned = true };

        var result = OccasionEdit.ApplyChanges(Current(), loaded, loaded with { IsPinned = false });

        Assert.False(result.IsPinned);
    }

    [Fact]
    public void Clearing_a_field_writes_null()
    {
        var result = OccasionEdit.ApplyChanges(Current(), Loaded, Loaded with { Notes = null, Emoji = null, ColorHex = null });

        Assert.Null(result.Notes);
        Assert.Null(result.Emoji);
        Assert.Null(result.ColorHex);
        Assert.Equal("Current", result.Title);
    }

    private static readonly CategoryFields LoadedCategory = new("Family", "💜", "#8E24AA");

    private static Category CurrentCategory() => new() { Id = 3, Name = "Kin", Emoji = "🏠", ColorHex = "#795548" };

    [Fact]
    public void Category_with_nothing_edited_keeps_every_current_value()
    {
        var current = CurrentCategory();

        var result = CategoryEdit.ApplyChanges(current, LoadedCategory, LoadedCategory);

        Assert.Same(current, result);
        Assert.Equal("Kin", result.Name);
        Assert.Equal("🏠", result.Emoji);
        Assert.Equal("#795548", result.ColorHex);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Emoji")]
    [InlineData("ColorHex")]
    public void Category_takes_only_the_edited_field(string field)
    {
        var edited = field switch
        {
            "Name" => LoadedCategory with { Name = "Relatives" },
            "Emoji" => LoadedCategory with { Emoji = null },
            "ColorHex" => LoadedCategory with { ColorHex = "#000000" },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var result = CategoryEdit.ApplyChanges(CurrentCategory(), LoadedCategory, edited);

        Assert.Equal(field == "Name" ? "Relatives" : "Kin", result.Name);
        Assert.Equal(field == "Emoji" ? null : "🏠", result.Emoji);
        Assert.Equal(field == "ColorHex" ? "#000000" : "#795548", result.ColorHex);
    }
}
