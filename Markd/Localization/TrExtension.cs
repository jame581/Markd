using Markd.Core.Localization;

namespace Markd.Localization;

/// <summary><c>Text="{loc:Tr Settings_Language}"</c>: a one-way binding to the current language's text, updated live.</summary>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class TrExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = string.Empty;

    public string? StringFormat { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => Tr.Bind(Key, StringFormat);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

/// <summary>The same binding for controls built in C#.</summary>
public static class Tr
{
    public static BindingBase Bind(string key, string? stringFormat = null) =>
        new Binding($"[{key}]", BindingMode.OneWay, source: LocalizationManager.Instance, stringFormat: stringFormat);
}
