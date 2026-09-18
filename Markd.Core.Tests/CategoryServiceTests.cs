using Markd.Core.Domain;
using Markd.Core.Services;
using Xunit;

namespace Markd.Core.Tests;

public class CategoryServiceTests
{
    [Fact]
    public async Task UpdateAsync_AcceptsDetachedInstanceWhileOriginalIsTracked()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;
        var service = new CategoryService(context);

        var created = await service.CreateAsync(new Category { Name = "Health", Emoji = "🌿", ColorHex = "#0B8043" });

        var updated = await service.UpdateAsync(new Category { Id = created.Id, Name = "Wellbeing", Emoji = "🧘", ColorHex = "#039BE5" });

        Assert.Equal("Wellbeing", updated.Name);
        var reloaded = await service.GetByIdAsync(created.Id);
        Assert.Equal("🧘", reloaded!.Emoji);
        Assert.Equal("#039BE5", reloaded.ColorHex);
    }
}
