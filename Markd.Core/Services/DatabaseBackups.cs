namespace Markd.Core.Services
{
    public static class DatabaseBackups
    {
        /// <summary>
        /// Import keeps a plaintext copy of the database while it runs. Copies left behind (an import that
        /// crashed, or 1.0 which never removed them) are deleted once they are old enough. Never throws.
        /// </summary>
        public static int DeleteStale(string? databasePath, TimeSpan olderThan)
        {
            var removed = 0;
            try
            {
                if (string.IsNullOrEmpty(databasePath))
                    return 0;

                var directory = Path.GetDirectoryName(databasePath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    return 0;

                var cutoff = DateTime.UtcNow - olderThan;
                foreach (var file in Directory.GetFiles(directory, Path.GetFileName(databasePath) + ".backup-*"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                        {
                            File.Delete(file);
                            removed++;
                        }
                    }
                    catch (Exception) { /* one locked file must not stop the rest */ }
                }
            }
            catch (Exception) { /* cleanup is best effort */ }

            return removed;
        }
    }
}
