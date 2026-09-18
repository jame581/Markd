using Markd.Core.Data;
using Markd.Core.Services;
using Xunit;

namespace Markd.Core.Tests;

public class AppSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsDefaultSettings_WhenNoRowExists()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        using (connection)
        using (context)
        {
            var service = new AppSettingsService(context);
            var settings = await service.GetAsync();

            Assert.NotNull(settings);
            Assert.Equal("System", settings.Theme);
            Assert.Equal("system", settings.Language);
            Assert.True(settings.NotificationsEnabled);
            Assert.Equal(new TimeSpan(9, 0, 0), settings.NotificationTimeOfDay);
        }
    }

    [Fact]
    public async Task SaveAsync_PersistsChanges()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        using (connection)
        using (context)
        {
            var service = new AppSettingsService(context);
            var settings = await service.GetAsync();

            settings.Theme = "Dark";
            settings.Language = "cs";
            settings.NotificationsEnabled = false;
            settings.NotificationTimeOfDay = new TimeSpan(7, 45, 0);

            await service.SaveAsync(settings);

            var reloaded = await service.GetAsync();
            Assert.Equal("Dark", reloaded.Theme);
            Assert.Equal("cs", reloaded.Language);
            Assert.False(reloaded.NotificationsEnabled);
            Assert.Equal(new TimeSpan(7, 45, 0), reloaded.NotificationTimeOfDay);
        }
    }

    [Fact]
    public async Task SaveAsync_CalledTwice_DoesNotDuplicate()
    {
        var (context, connection) = TestDbFactory.CreateSqliteInMemoryContext();
        using (connection)
        using (context)
        {
            var service = new AppSettingsService(context);

            var settings = await service.GetAsync();
            settings.Theme = "Light";
            await service.SaveAsync(settings);

            settings.Theme = "Dark";
            await service.SaveAsync(settings);

            var all = context.AppSettings.ToList();
            Assert.Single(all);
            Assert.Equal("Dark", all[0].Theme);
        }
    }
}
