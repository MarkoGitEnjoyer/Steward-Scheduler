using Microsoft.EntityFrameworkCore;
using Scheduler.Data.Models;
using Scheduler.Data.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Implementations
{
    public class LanguageRepository : Repository<Language>, ILanguageRepository
    {
        public LanguageRepository(SchedulerDbContext context) : base(context)
        {
        }

        public async Task<Language> GetLanguageByNameAsync(string languageName)
        {
            return await _context.Languages
                .FirstOrDefaultAsync(l => l.LanguageName.Equals(languageName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
