using Markd.Core.Data;
using Markd.Core.Domain;
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
            db.Categories.Update(category);
            await db.SaveChangesAsync();
            return category;
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
