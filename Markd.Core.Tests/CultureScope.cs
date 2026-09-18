using System.Globalization;
using Markd.Core.Localization;
using Xunit;

namespace Markd.Core.Tests;

/// <summary>
/// Switches the app language (<see cref="LocalizationManager"/>, which is process-wide) for one test.
/// Only use it in classes marked <c>[Collection(nameof(GlobalCultureCollection))]</c>, which never run in parallel.
/// </summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous = LocalizationManager.Instance.Culture;

    public CultureScope(string name) => LocalizationManager.Instance.SetCulture(CultureInfo.GetCultureInfo(name));

    public void Dispose() => LocalizationManager.Instance.SetCulture(_previous);
}

/// <summary>Tests that change the global language. xUnit runs these after the parallel tests, one at a time.</summary>
[CollectionDefinition(nameof(GlobalCultureCollection), DisableParallelization = true)]
public sealed class GlobalCultureCollection;
