using Markd.ViewModels;

namespace Markd.Desktop.Dialogs;

/// <summary>
/// Both versions of one conflicting record side by side, shown over the conflict list. A column without values is
/// the device that deleted the record; its header says so. Closing returns to the list.
/// </summary>
public partial class ConflictCompareDialog : ContentView
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConflictCompareDialog(ConflictCompareViewModel compare)
    {
        InitializeComponent();
        TitleLabel.Text = compare.Title;
        BuildTable(compare);
    }

    public Task Closed => _closed.Task;

    public void FitTo(double windowHeight) => Card.MaximumHeightRequest = Math.Max(320, windowHeight - 120);

    public void Close() => _closed.TrySetResult();

    private void OnCloseClicked(object? sender, EventArgs e) => Close();

    private void BuildTable(ConflictCompareViewModel compare)
    {
        var row = 0;
        AddRow(Header(compare.LocalHeader), Header(compare.RemoteHeader), ref row);

        var count = Math.Max(compare.Local.Count, compare.Remote.Count);
        for (var i = 0; i < count; i++)
        {
            var label = (i < compare.Local.Count ? compare.Local[i] : compare.Remote[i]).Label;
            var caption = new Label { Text = label, Style = Resource<Style>("DeskFieldLabel"), Margin = new Thickness(0, 12, 0, 2) };
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
        Style = Resource<Style>("DeskCardHeader"),
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static Label Value(string? text) => new() { Text = text ?? string.Empty, FontSize = 14, LineBreakMode = LineBreakMode.WordWrap };

    private static T Resource<T>(string key) => (T)Application.Current!.Resources[key];
}
