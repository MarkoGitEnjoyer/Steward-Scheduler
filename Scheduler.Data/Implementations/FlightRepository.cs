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
    public class FlightRepository : Repository<Flight>, IFlightRepository
    {
        public FlightRepository(SchedulerDbContext context) : base(context)
        {
        }

        public async Task<List<Flight>> GetFlightsForAWeek(DateTime weekStart)
        {
            return await _context.Flights
                .Where(fl => fl.DepartureTime >= weekStart && fl.DepartureTime <= weekStart.AddDays(7))
                .ToListAsync();
        }
    }
}
