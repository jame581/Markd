using CommunityToolkit.Mvvm.ComponentModel;

namespace Markd.ViewModels;

/// <summary>One of the twelve identity swatches in a colour picker.</summary>
public sealed class ColorChoice : ObservableObject
{
    private bool _isSelected;

    public ColorChoice(string hex)
    {
        Hex = hex;
        Color = Color.FromArgb(hex);
    }

    public string Hex { get; }
    public Color Color { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
