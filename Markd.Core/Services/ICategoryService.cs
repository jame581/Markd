using Markd.Core.Domain;

namespace Markd.Core.Services
{
    public interface ICategoryService
    {
        Task<List<Category>> GetAllAsync();
        Task<Category?> GetByIdAsync(int id);
        Task<Category> CreateAsync(Category category);
        Task<Category> UpdateAsync(Category category);
        Task DeleteAsync(int id);

        /// <summary>Undo of a delete: bring the category back as itself (same Id and SyncId), or recreate it if it is gone.</summary>
        Task<Category> RestoreAsync(Category snapshot);
    }
}
