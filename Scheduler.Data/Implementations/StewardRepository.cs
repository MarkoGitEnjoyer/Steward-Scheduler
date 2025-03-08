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

        public async Task<IEnumerable<Steward>> GetStewardsByRoleAsync(Role role)
        {
            string roleString = role.ToString();
            return await _context.Stewards
                .Where(s => s.RoleString.ToLower() == roleString.ToLower())
                .ToListAsync();
        }

        // Rest of the methods remain unchanged
        public async Task<IEnumerable<Steward>> GetAvailableStewardsAsync(DateTime startTime, float flightDuration)
        {
            var minRestTime = TimeSpan.FromHours(12);

            return await _context.Stewards
                .Where(s => s.LastFlightEndTime == null ||
                            startTime - s.LastFlightEndTime.Value > minRestTime)
                .ToListAsync();
        }

        public async Task<IEnumerable<Steward>> GetStewardsWithLicenseAsync(string aircraftType)
        {
            var licenseIds = await _context.AircraftLicenses
                .Where(l => l.AircraftTypeId == aircraftType)
                .Select(l => l.LicenseId)
                .ToListAsync();

            return await _context.Stewards
                .Where(s => s.StewardLicenses.Any(sl => licenseIds.Contains(sl.LicenseId)))
                .ToListAsync();
        }

        public async Task<IEnumerable<Steward>> GetStewardsWithLanguageAsync(int languageId)
        {
            return await _context.Stewards
                .Where(s => s.StewardLanguages.Any(sl => sl.LanguageId == languageId))
                .ToListAsync();
        }

        public async Task<IEnumerable<Steward>> GetSeniorStewardsAsync()
        {
            return await _context.Stewards
                .Where(s => s.IsSenior)
                .ToListAsync();
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
            await _context.SaveChangesAsync();
        }

        public async Task UpdateLastFlightTimeAsync(int stewardId, DateTime endTime)
        {
            var steward = await _context.Stewards.FindAsync(stewardId);
            if (steward != null)
            {
                steward.LastFlightEndTime = endTime;
            }
        }
    }

}
