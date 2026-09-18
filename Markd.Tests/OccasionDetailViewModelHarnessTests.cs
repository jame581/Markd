using Xunit;

namespace Markd.Tests;

public class OccasionDetailViewModelHarnessTests
{
    [Fact]
    public async Task Runs_detail_viewmodel_harness()
    {
        await OccasionDetailViewModelTests.RunAsync();
    }
}
