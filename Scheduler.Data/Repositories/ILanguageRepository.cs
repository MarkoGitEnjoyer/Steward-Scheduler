using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface ILanguageRepository : IRepository<Models.Language>
    {
        Task<Models.Language> GetLanguageByNameAsync(string languageName);
    }
}
