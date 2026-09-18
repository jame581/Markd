using System.Globalization;
using System.Runtime.CompilerServices;

namespace Markd.Core.Tests;

/// <summary>Tests assert English text; pin the default so a Czech developer machine gets the same results.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
