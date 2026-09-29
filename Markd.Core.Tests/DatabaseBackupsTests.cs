using Markd.Core.Services;
using Xunit;

namespace Markd.Core.Tests;

public class DatabaseBackupsTests
{
    [Fact]
    public void DeleteStale_removes_only_old_backup_files_next_to_the_database()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var db = Path.Combine(dir, "markd.db");
            File.WriteAllText(db, "db");
            var oldBackup = db + ".backup-20200101000000000-aaaa";
            var newBackup = db + ".backup-20990101000000000-bbbb";
            var unrelated = Path.Combine(dir, "other.backup-1");
            File.WriteAllText(oldBackup, "x"); File.WriteAllText(newBackup, "x"); File.WriteAllText(unrelated, "x");
            File.SetLastWriteTimeUtc(oldBackup, DateTime.UtcNow.AddDays(-3));

            var removed = DatabaseBackups.DeleteStale(db, TimeSpan.FromHours(24));

            Assert.Equal(1, removed);
            Assert.False(File.Exists(oldBackup));
            Assert.True(File.Exists(newBackup));
            Assert.True(File.Exists(unrelated));
            Assert.True(File.Exists(db));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DeleteStale_never_throws()
    {
        Assert.Equal(0, DatabaseBackups.DeleteStale(null, TimeSpan.FromHours(1)));
        Assert.Equal(0, DatabaseBackups.DeleteStale(Path.Combine(Path.GetTempPath(), "does-not-exist", "markd.db"), TimeSpan.FromHours(1)));
    }
}
