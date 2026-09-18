using static Markd.Controls.IconPart;

namespace Markd.Controls;

/// <summary>
/// The design's icon set, transcribed from the prototypes. Android glyphs use a 24 unit box,
/// Windows glyphs ("Win" prefix) a 16 unit box. Arc segments are rewritten as curves.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, IconGlyph> Glyphs = new(StringComparer.Ordinal)
    {
        // ----- Android / shared, 24 box -----
        ["Plus"] = G24(Stroke("M12 5v14M5 12h14", 2.2f)),
        ["Back"] = G24(Stroke("M19 12H5M11 5.6L4.6 12l6.4 6.4", 2f)),
        ["Forward"] = G24(Stroke("M5 12h14M13 5.6l6.4 6.4-6.4 6.4", 2f)),
        ["Dots"] = G24(FillCircle(12, 5, 2), FillCircle(12, 12, 2), FillCircle(12, 19, 2)),
        ["Close"] = G24(Stroke("M6 6l12 12M18 6L6 18", 2.1f)),
        ["Tick"] = G24(Stroke("M4.5 12.6l5 5L19.5 6.8", 2.4f)),
        ["Flag"] = G24(Stroke("M6.4 21V3.6h11l-2.1 4.3 2.1 4.3h-11", 1.9f)),
        ["Pin"] = G24(Fill("M9 3h6l-1 6 4 3v2H6v-2l4-3z"), Stroke("M12 14v7", 1.8f)),
        ["Pencil"] = G24(Stroke("M4 20h4L20 8l-4-4L4 16z", 1.9f)),
        ["Caret"] = new IconGlyph(14, 9, [Stroke("M1.2 1.6L7 7.2l5.8-5.6", 1.8f)]),
        ["Calendar"] = G24(StrokeRect(3.2f, 5, 17.6f, 16, 3, 1.8f), Stroke("M3.2 9.8h17.6M8.4 2.8v4M15.6 2.8v4", 1.8f)),
        ["MarkBig"] = new IconGlyph(42, 42,
        [
            StrokeRect(4, 7, 34, 31, 7, 2.4f),
            Stroke("M4 15.4h34M13.5 3.4v6.6M28.5 3.4v6.6", 2.4f),
            Stroke("M14.4 26l4 4 8.8-8.8", 2.6f)
        ]),
        ["TabHomeOn"] = G24(Fill("M3.4 10.6L12 3.4l8.6 7.2v9.2Q20.6 21 19.4 21H4.6Q3.4 21 3.4 19.8z")),
        ["TabHome"] = G24(Stroke("M3.4 10.6L12 3.4l8.6 7.2v9.2Q20.6 21 19.4 21H4.6Q3.4 21 3.4 19.8z", 1.8f)),
        ["TabCalendarOn"] = G24(
            FillRect(3.2f, 5, 17.6f, 16, 3),
            Stroke("M8.4 2.4v4M15.6 2.4v4", 1.9f),
            Stroke("M3.2 9.8h17.6", 1.5f).WithColor(Colors.White, 0.55f)),
        ["TabCalendar"] = G24(StrokeRect(3.2f, 5, 17.6f, 16, 3, 1.8f), Stroke("M3.2 9.8h17.6M8.4 2.4v4M15.6 2.4v4", 1.8f)),
        ["TabTagOn"] = G24(
            Fill("M12.4 3.2H20Q21 3.2 21 4.2v7.6l-9.6 9.6Q10.4 22.4 9.4 21.4l-6.6-6.6Q1.8 13.8 2.8 12.8z"),
            FillCircle(16.4f, 7.8f, 1.6f, Colors.White, 0.9f)),
        ["TabTag"] = G24(
            Stroke("M12.4 3.2H20Q21 3.2 21 4.2v7.6l-9.6 9.6Q10.4 22.4 9.4 21.4l-6.6-6.6Q1.8 13.8 2.8 12.8z", 1.8f),
            FillCircle(16.4f, 7.8f, 1.5f)),
        ["TabGearOn"] = G24(
            Fill("M12 2.2l1.4 2.6 2.9-.6.7 2.9 2.6 1.4-1.5 2.5 1.5 2.5-2.6 1.4-.7 2.9-2.9-.6L12 21.8l-1.4-2.6-2.9.6-.7-2.9-2.6-1.4 1.5-2.5-1.5-2.5 2.6-1.4.7-2.9 2.9.6z"),
            FillCircle(12, 12, 3.1f, Colors.White, 0.92f)),
        ["TabGear"] = G24(
            StrokeCircle(12, 12, 3.3f, 1.8f),
            Stroke("M12 2.2l1.4 2.6 2.9-.6.7 2.9 2.6 1.4-1.5 2.5 1.5 2.5-2.6 1.4-.7 2.9-2.9-.6L12 21.8l-1.4-2.6-2.9.6-.7-2.9-2.6-1.4 1.5-2.5-1.5-2.5 2.6-1.4.7-2.9 2.9.6z", 1.7f)),

        // ----- Windows, 16 box -----
        ["WinHome"] = G16(Stroke("M2.4 6.8L8 2.2l5.6 4.6V13Q13.6 13.8 12.8 13.8H3.2Q2.4 13.8 2.4 13z", 1.35f)),
        ["WinCalendar"] = G16(StrokeRect(2, 3.2f, 12, 11, 2, 1.35f), Stroke("M2 6.6h12M5.4 1.8v2.6M10.6 1.8v2.6", 1.35f)),
        ["WinTag"] = G16(
            Stroke("M8.4 2.2H13Q13.8 2.2 13.8 3v4.6L7.6 13.8Q6.95 14.45 6.3 13.8L2.2 9.7Q1.55 9.05 2.2 8.4z", 1.35f),
            FillCircle(10.6f, 5.4f, 1.05f)),
        ["WinGear"] = G16(
            StrokeCircle(8, 8, 2.4f, 1.35f),
            Stroke("M8 1.6l.9 1.7 1.9-.4.5 1.9 1.7.9-1 1.7 1 1.7-1.7.9-.5 1.9-1.9-.4L8 14.4l-.9-1.7-1.9.4-.5-1.9-1.7-.9 1-1.7-1-1.7 1.7-.9.5-1.9 1.9.4z", 1.2f)),
        ["WinInfo"] = G16(StrokeCircle(8, 8, 6.4f, 1.35f), Stroke("M8 7v4.2M8 4.6v.2", 1.5f)),
        ["WinSun"] = G16(
            StrokeCircle(8, 8, 3.2f, 1.35f),
            Stroke("M8 1.4v1.8M8 12.8v1.8M1.4 8h1.8M12.8 8h1.8M3.3 3.3l1.3 1.3M11.4 11.4l1.3 1.3M12.7 3.3l-1.3 1.3M4.6 11.4l-1.3 1.3", 1.3f)),
        ["WinMoon"] = new IconGlyph(24, 24,
        [
            Stroke("M20 14.5C18.9 15 17.7 15.2 16.5 15.2C11.9 15.2 8.8 12.1 8.8 7.5C8.8 6.3 9 5.1 9.5 4C6.2 5.2 4 8.3 4 11.9C4 16.4 7.6 20 12.1 20C15.7 20 18.8 17.8 20 14.5z", 2f)
        ]),
        ["WinCheck"] = G16(Stroke("M3.5 8.4l3 3 6-6.4", 1.8f)),
        ["WinFlag"] = G16(Stroke("M4 14V2.6h7.6l-1.4 2.8 1.4 2.8H4", 1.4f)),
        ["WinPlus"] = G16(Stroke("M8 2.5v11M2.5 8h11", 1.6f)),
        ["WinClose"] = G16(Stroke("M3.5 3.5l9 9M12.5 3.5l-9 9", 1.4f)),
        ["WinTrash"] = G16(Stroke("M3 4.5h10M6 4.5V3h4v1.5M4.4 4.5l.6 9h6l.6-9", 1.3f)),
        ["WinPencil"] = G16(Stroke("M11 2.2l2.8 2.8L5.6 13.2 2 14l.8-3.6z", 1.3f)),
        ["WinPin"] = G16(Fill("M6 1.5h4l-.6 4.2 3.1 3.1H9.1L8 14.5 6.9 8.8H3.5l3.1-3.1z")),
        ["WinPinOutline"] = G16(Stroke("M6 1.5h4l-.6 4.2 3.1 3.1H9.1L8 14.5 6.9 8.8H3.5l3.1-3.1z", 1.3f)),
        ["WinChevronLeft"] = G16(Stroke("M9.5 3L5 8l4.5 5", 1.5f)),
        ["WinChevronRight"] = G16(Stroke("M6.5 3L11 8l-4.5 5", 1.5f)),
        ["WinChevronDown"] = G16(Stroke("M3.5 6l4.5 4.5L12.5 6", 1.5f)),
        ["WinChevronUp"] = G16(Stroke("M3.5 10.5L8 6l4.5 4.5", 1.5f)),
        ["WinClock"] = G16(StrokeCircle(8, 8, 6.4f, 1.4f), Stroke("M8 4.4V8l2.6 1.6", 1.4f)),
        ["WinBell"] = G16(Stroke("M8 1.8C5.8 1.8 4 3.6 4 5.8v2.6L2.6 11h10.8L12 8.4V5.8C12 3.6 10.2 1.8 8 1.8zM6.4 11.6C6.4 12.5 7.1 13.2 8 13.2C8.9 13.2 9.6 12.5 9.6 11.6", 1.3f)),
        ["WinGlobe"] = G16(
            StrokeCircle(8, 8, 6.4f, 1.4f),
            Stroke("M1.6 8h12.8M8 1.6c1.8 2 1.8 10.8 0 12.8M8 1.6c-1.8 2-1.8 10.8 0 12.8", 1.2f)),
        ["WinWarning"] = G16(StrokeCircle(8, 8, 6.6f, 1.4f), Stroke("M8 4.6v4.2M8 11.2v.2", 1.6f)),
        ["WinEmpty"] = new IconGlyph(44, 44,
        [
            StrokeRect(5, 9, 34, 30, 5, 2),
            Stroke("M5 17h34M14 5v7M30 5v7", 2),
            FillCircle(22, 28, 4.5f)
        ]),
        ["WinPick"] = new IconGlyph(34, 34,
        [
            StrokeCircle(17, 17, 13, 2),
            Stroke("M17 9.5V17l5.5 3.4", 2)
        ]),
        ["WinMinimize"] = new IconGlyph(10, 10, [Stroke("M0 5h10", 1)]),
        ["WinMaximize"] = new IconGlyph(10, 10, [StrokeRect(0.5f, 0.5f, 9, 9, 0, 1)]),
        ["WinWindowClose"] = new IconGlyph(10, 10, [Stroke("M0 0l10 10M10 0L0 10", 1)]),
    };

    public static bool TryGet(string name, out IconGlyph glyph) => Glyphs.TryGetValue(name, out glyph!);

    private static IconGlyph G24(params IconPart[] parts) => new(24, 24, parts);

    private static IconGlyph G16(params IconPart[] parts) => new(16, 16, parts);
}
