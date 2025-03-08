using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IStewardRepository : IRepository<Models.Steward>
    {
        Task<IEnumerable<Models.Steward>> GetAvailableStewardsAsync(DateTime startTime, float flightDuration);
        Task<IEnumerable<Models.Steward>> GetStewardsWithLicenseAsync(string aircraftType);
        Task<IEnumerable<Models.Steward>> GetStewardsByRoleAsync(Models.Role role);
        Task<IEnumerable<Models.Steward>> GetStewardsWithLanguageAsync(int languageId);
        Task<IEnumerable<Models.Steward>> GetSeniorStewardsAsync();
        Task<float> GetMonthlyHoursAsync(int stewardId, int year, int month);
        Task UpdateMonthlyHoursAsync(int stewardId, int year, int month, float additionalHours);
        Task UpdateLastFlightTimeAsync(int stewardId, DateTime endTime);
    }
}
