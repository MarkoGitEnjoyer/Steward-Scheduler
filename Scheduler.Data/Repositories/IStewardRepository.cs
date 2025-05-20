using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IStewardRepository : IRepository<Models.Steward>
    {

        Task<float> GetMonthlyHoursAsync(int stewardId, int year, int month);
        Task UpdateMonthlyHoursAsync(int stewardId, int year, int month, float additionalHours);
        Task UpdateLastFlightTimeAsync(int stewardId, DateTime endTime);
        Task<IEnumerable<int>> GetStewardLanguageIdsAsync(int stewardId);
        Task<IEnumerable<int>> GetStewardLicenseIdsAsync(int stewardId);
        Task<IEnumerable<string>> GetStewardLanguageNamesAsync(int stewardId);
        Task<IEnumerable<string>> GetStewardLicenseNamesAsync(int stewardId);
    }
}
