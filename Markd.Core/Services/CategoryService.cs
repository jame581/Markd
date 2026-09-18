using Markd.Core.Data;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Services
{
    public class CategoryService(MarkdDbContext db) : ICategoryService
    {
        public async Task<List<Category>> GetAllAsync()
        {
            return await db.Categories
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<Category?> GetByIdAsync(int id)
        {
            return await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<Category> CreateAsync(Category category)
        {
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            return category;
        }

        public async Task<Category> UpdateAsync(Category category)
        {
            var existing = await db.Categories.FindAsync(category.Id)
                ?? throw new InvalidOperationException(string.Format(LocalizationManager.Instance.Culture, Strings.Category_NotFoundById, category.Id));

            existing.Name = category.Name;
            existing.Emoji = category.Emoji;
            existing.ColorHex = category.ColorHex;

            await db.SaveChangesAsync();
            return existing;
        }

        public async Task DeleteAsync(int id)
        {
            var category = await db.Categories.FindAsync(id);
            if (category == null)
                return;

            db.Categories.Remove(category);
            await db.SaveChangesAsync();
        }
    }
}
