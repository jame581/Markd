using Markd.ViewModels;

namespace Markd.Pages;

/// <summary>
/// Both versions of one conflicting record side by side (phone, modal over the conflict list). A column without
/// values is the device that deleted the record; its header says so.
/// </summary>
public partial class ConflictComparePage : ContentPage
{
    private bool _closing;

    public ConflictComparePage(ConflictCompareViewModel compare)
    {
        InitializeComponent();
        AppBar.Title = compare.Title;
        AppBar.LeadingCommand = new Command(() => _ = CloseAsync());
        BuildTable(compare);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync();
        return true;
    }

    private void BuildTable(ConflictCompareViewModel compare)
    {
        var row = 0;
        AddRow(Header(compare.LocalHeader), Header(compare.RemoteHeader), ref row);

        var count = Math.Max(compare.Local.Count, compare.Remote.Count);
        for (var i = 0; i < count; i++)
        {
            var label = (i < compare.Local.Count ? compare.Local[i] : compare.Remote[i]).Label;
            var caption = new Label { Text = label, Style = Resource<Style>("M3RowSub"), FontSize = 12.5, Margin = new Thickness(0, 14, 0, 2) };
            Table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Table.Add(caption, 0, row);
            Grid.SetColumnSpan(caption, 2);
            row++;

            AddRow(Value(i < compare.Local.Count ? compare.Local[i].Value : null),
                Value(i < compare.Remote.Count ? compare.Remote[i].Value : null), ref row);
        }
    }

    private void AddRow(View left, View right, ref int row)
    {
        Table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Table.Add(left, 0, row);
        Table.Add(right, 1, row);
        row++;
    }

    private static Label Header(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("M3SectionHeader"),
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static Label Value(string? text) => new() { Text = text ?? string.Empty, FontSize = 15.5, LineBreakMode = LineBreakMode.WordWrap };

    private static T Resource<T>(string key) => (T)Application.Current!.Resources[key];

    private async Task CloseAsync()
    {
        if (_closing)
            return;

        _closing = true;
        try
        {
            await Navigation.PopModalAsync(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Closing the compare page failed: {ex}");
        }
    }
}
