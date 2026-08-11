using Markd.Core.Domain;

namespace Markd.Core.Services
{
    public interface IAppSettingsService
    {
        /// <summary>
        /// Returns the single app settings row, creating it with defaults if it doesn't exist.
        /// </summary>
        Task<AppSettings> GetAsync();

        /// <summary>
        /// Persists changes to the settings row.
        /// </summary>
        Task SaveAsync(AppSettings settings);
    }
}
