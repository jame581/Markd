namespace Markd.Controls;

/// <summary>Occasion and category colours are stored as hex strings; an empty or invalid value falls back to the accent.</summary>
public static class HexColor
{
    private static readonly Color Fallback = Color.FromArgb("#6F63C9");

    public static Color Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Fallback;

        try { return Color.FromArgb(hex); }
        catch (Exception) { return Fallback; }
    }
}
