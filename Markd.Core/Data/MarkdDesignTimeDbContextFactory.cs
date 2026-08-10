using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Markd.Core.Data
{
    public class MarkdDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MarkdDbContext>
    {
        public MarkdDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<MarkdDbContext>();
            optionsBuilder.UseSqlite("Data Source=markd.design.db");
            return new MarkdDbContext(optionsBuilder.Options);
        }
    }
}
