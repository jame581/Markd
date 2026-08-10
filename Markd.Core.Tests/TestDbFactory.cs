using Markd.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Tests;

internal static class TestDbFactory
{
    public static (MarkdDbContext Context, SqliteConnection Connection) CreateSqliteInMemoryContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<MarkdDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new MarkdDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }
}
