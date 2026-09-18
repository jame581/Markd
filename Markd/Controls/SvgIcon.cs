using System.Globalization;

namespace Markd.Controls;

/// <summary>
/// Draws one of the design's line icons (<see cref="Icons"/>) scaled to its size and tinted with <see cref="Color"/>.
/// </summary>
public sealed class SvgIcon : GraphicsView
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(SvgIcon), null, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(SvgIcon), Colors.Black, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(SvgIcon), 20d, propertyChanged: OnSizeChanged);

    public SvgIcon()
    {
        Drawable = new IconDrawable(this);
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        WidthRequest = HeightRequest = Size;
    }

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((SvgIcon)bindable).Invalidate();

    private static void OnSizeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var icon = (SvgIcon)bindable;
        icon.WidthRequest = icon.HeightRequest = (double)newValue;
        icon.Invalidate();
    }

    private sealed class IconDrawable(SvgIcon owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (owner.Glyph is null || !Icons.TryGet(owner.Glyph, out var glyph))
                return;

            var scale = Math.Min(dirtyRect.Width / glyph.ViewWidth, dirtyRect.Height / glyph.ViewHeight);
            canvas.SaveState();
            canvas.Translate(
                dirtyRect.X + (dirtyRect.Width - glyph.ViewWidth * scale) / 2,
                dirtyRect.Y + (dirtyRect.Height - glyph.ViewHeight * scale) / 2);
            canvas.Scale(scale, scale);
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            foreach (var part in glyph.Parts)
            {
                var color = (part.Color ?? owner.Color).WithAlpha((part.Color ?? owner.Color).Alpha * part.Opacity);
                if (part.IsFill)
                {
                    canvas.FillColor = color;
                    switch (part.Kind)
                    {
                        case PartKind.Circle: canvas.FillCircle(part.A, part.B, part.C); break;
                        case PartKind.Rect: canvas.FillRoundedRectangle(part.A, part.B, part.C, part.D, part.E); break;
                        default: canvas.FillPath(part.Path!); break;
                    }
                }
                else
                {
                    canvas.StrokeColor = color;
                    canvas.StrokeSize = part.StrokeWidth;
                    switch (part.Kind)
                    {
                        case PartKind.Circle: canvas.DrawCircle(part.A, part.B, part.C); break;
                        case PartKind.Rect: canvas.DrawRoundedRectangle(part.A, part.B, part.C, part.D, part.E); break;
                        default: canvas.DrawPath(part.Path!); break;
                    }
                }
            }

            canvas.RestoreState();
        }
    }
}

public enum PartKind { Path, Circle, Rect }

public sealed record IconPart(PartKind Kind, bool IsFill, float StrokeWidth, PathF? Path, float A, float B, float C, float D, float E, Color? Color, float Opacity)
{
    public static IconPart Stroke(string data, float width) => new(PartKind.Path, false, width, SvgPath.Parse(data), 0, 0, 0, 0, 0, null, 1);
    public static IconPart Fill(string data, Color? color = null, float opacity = 1) => new(PartKind.Path, true, 0, SvgPath.Parse(data), 0, 0, 0, 0, 0, color, opacity);
    public static IconPart StrokeCircle(float cx, float cy, float r, float width) => new(PartKind.Circle, false, width, null, cx, cy, r, 0, 0, null, 1);
    public static IconPart FillCircle(float cx, float cy, float r, Color? color = null, float opacity = 1) => new(PartKind.Circle, true, 0, null, cx, cy, r, 0, 0, color, opacity);
    public static IconPart StrokeRect(float x, float y, float w, float h, float rx, float width) => new(PartKind.Rect, false, width, null, x, y, w, h, rx, null, 1);
    public static IconPart FillRect(float x, float y, float w, float h, float rx) => new(PartKind.Rect, true, 0, null, x, y, w, h, rx, null, 1);
    public IconPart WithColor(Color color, float opacity) => this with { Color = color, Opacity = opacity };
}

public sealed record IconGlyph(float ViewWidth, float ViewHeight, IReadOnlyList<IconPart> Parts);

/// <summary>
/// Minimal SVG path-data parser (M, L, H, V, C, Q, Z in absolute and relative form).
/// </summary>
public static class SvgPath
{
    public static PathF Parse(string data)
    {
        var path = new PathF();
        var tokens = Tokenize(data);
        var index = 0;
        var command = 'M';
        PointF current = default, start = default;

        while (index < tokens.Count)
        {
            if (tokens[index] is { Length: 1 } token && char.IsLetter(token[0]))
            {
                command = token[0];
                index++;
                if (command is 'Z' or 'z')
                {
                    path.Close();
                    current = start;
                    continue;
                }
            }

            float Next() => float.Parse(tokens[index++], CultureInfo.InvariantCulture);
            var relative = char.IsLower(command);
            PointF Point(float x, float y) => relative ? new PointF(current.X + x, current.Y + y) : new PointF(x, y);

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    current = start = Point(Next(), Next());
                    path.MoveTo(current);
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    current = Point(Next(), Next());
                    path.LineTo(current);
                    break;
                case 'H':
                    var x = Next();
                    current = new PointF(relative ? current.X + x : x, current.Y);
                    path.LineTo(current);
                    break;
                case 'V':
                    var y = Next();
                    current = new PointF(current.X, relative ? current.Y + y : y);
                    path.LineTo(current);
                    break;
                case 'C':
                    var c1 = Point(Next(), Next());
                    var c2 = Point(Next(), Next());
                    var end = Point(Next(), Next());
                    path.CurveTo(c1, c2, end);
                    current = end;
                    break;
                case 'Q':
                    var q = Point(Next(), Next());
                    var qEnd = Point(Next(), Next());
                    path.QuadTo(q, qEnd);
                    current = qEnd;
                    break;
                default:
                    throw new FormatException($"Unsupported path command '{command}'.");
            }
        }

        return path;
    }

    private static List<string> Tokenize(string data)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < data.Length)
        {
            var c = data[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (char.IsLetter(c)) { tokens.Add(c.ToString()); i++; continue; }

            var startIndex = i;
            if (c is '-' or '+') i++;
            var seenDot = false;
            while (i < data.Length && (char.IsDigit(data[i]) || (data[i] == '.' && !seenDot)))
            {
                if (data[i] == '.') seenDot = true;
                i++;
            }
            tokens.Add(data[startIndex..i]);
        }
        return tokens;
    }
}
