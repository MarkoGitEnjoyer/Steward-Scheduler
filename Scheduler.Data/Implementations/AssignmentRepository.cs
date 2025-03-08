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
    public class AssignmentRepository : Repository<Assignment>, IAssignmentRepository
    {
        public AssignmentRepository(SchedulerDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Assignment>> GetAssignmentsByStewardAsync(int stewardId)
        {
            return await _context.Assignments
                .Include(a => a.Flight)
                .Where(a => a.StewardId == stewardId)
                .ToListAsync();
        }

        public async Task<IEnumerable<Assignment>> GetAssignmentsByFlightAsync(int flightId)
        {
            return await _context.Assignments
                .Include(a => a.Steward)
                .Where(a => a.FlightId == flightId)
                .ToListAsync();
        }

        public async Task<IEnumerable<Steward>> GetAssignedStewardsAsync(int flightId)
        {
            return await _context.Assignments
                .Where(a => a.FlightId == flightId)
                .Select(a => a.Steward)
                .ToListAsync();
        }

        public async Task<bool> IsAssignedAsync(int stewardId, int flightId)
        {
            return await _context.Assignments
                .AnyAsync(a => a.StewardId == stewardId && a.FlightId == flightId);
        }
    }
}
