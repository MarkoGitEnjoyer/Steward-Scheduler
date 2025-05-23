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
    public class StewardRepository : Repository<Steward>, IStewardRepository
    {
        public StewardRepository(SchedulerDbContext context) : base(context)
        {
        }

        public async Task<float> GetMonthlyHoursAsync(int stewardId, int year, int month)
        {
            var hours = await _context.MonthlyHours
                .Where(mh => mh.StewardId == stewardId && mh.Year == year && mh.Month == month)
                .FirstOrDefaultAsync();

            return hours?.HoursWorked ?? 0;
        }

        public async Task UpdateMonthlyHoursAsync(int stewardId, int year, int month, float additionalHours)
        {
            var hours = await _context.MonthlyHours
                .Where(mh => mh.StewardId == stewardId && mh.Year == year && mh.Month == month)
                .FirstOrDefaultAsync();

            if (hours != null)
            {
                hours.HoursWorked += additionalHours;
            }
            else
            {
                await _context.MonthlyHours.AddAsync(new MonthlyHours
                {
                    StewardId = stewardId,
                    Year = year,
                    Month = month,
                    HoursWorked = additionalHours
                });
            }
        }

        public async Task UpdateLastFlightTimeAsync(int stewardId, DateTime endTime)
        {
            var steward = await _context.Stewards.FindAsync(stewardId);
            if (steward != null)
            {
                steward.LastFlightEndTime = endTime;
            }
        }
        public async Task<IEnumerable<int>> GetStewardLanguageIdsAsync(int stewardId)
        {
            return await _context.StewardLanguages
                .Where(sl => sl.StewardId == stewardId)
                .Select(sl => sl.LanguageId)
                .ToListAsync();
        }

        public async Task<IEnumerable<int>> GetStewardLicenseIdsAsync(int stewardId)
        {
            return await _context.StewardLicenses
                .Where(sl => sl.StewardId == stewardId)
                .Select(sl => sl.LicenseId)
                .ToListAsync();
        }


        public async Task<IEnumerable<string>> GetStewardLanguageNamesAsync(int stewardId)
        {
            return await _context.StewardLanguages
                .Where(sl => sl.StewardId == stewardId)
                .Include(sl => sl.Language)
                .Select(sl => sl.Language.LanguageName)
                .ToListAsync();
        }

        public async Task<IEnumerable<string>> GetStewardLicenseNamesAsync(int stewardId)
        {
            return await _context.StewardLicenses
                .Where(sl => sl.StewardId == stewardId)
                .Include(sl => sl.License)
                .Select(sl => sl.License.AircraftTypeId)
                .ToListAsync();
        }
    }

}
