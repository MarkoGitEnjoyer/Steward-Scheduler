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

        public async Task<IEnumerable<Flight>> GetUpcomingFlightsAsync(DateTime fromDate)
        {
            return await _context.Flights
                .Where(f => f.DepartureTime >= fromDate)
                .OrderBy(f => f.DepartureTime)
                .ToListAsync();
        }

        public async Task<IEnumerable<Flight>> GetFlightsByAircraftTypeAsync(string aircraftType)
        {
            return await _context.Flights
                .Where(f => f.AircraftType == aircraftType)
                .ToListAsync();
        }

        public async Task<IEnumerable<Flight>> GetFlightsByPriorityAsync(int minimumPriority)
        {
            return await _context.Flights
                .Where(f => f.Priority >= minimumPriority)
                .OrderByDescending(f => f.Priority)
                .ToListAsync();
        }

        public async Task<IEnumerable<Flight>> GetUnassignedFlightsAsync()
        {
            // Get flights that don't have the required number of crew members assigned
            var flights = await _context.Flights
                .Include(f => f.Aircraft)
                .Include(f => f.Assignments)
                .ToListAsync();

            var unassignedFlights = new List<Flight>();

            foreach (var flight in flights)
            {
                var businessAssignments = await _context.Assignments
                    .Include(a => a.Steward)
                    .Where(a => a.FlightId == flight.FlightId && a.Steward.Role == Role.Business)
                    .CountAsync();

                var economyAssignments = await _context.Assignments
                    .Include(a => a.Steward)
                    .Where(a => a.FlightId == flight.FlightId && a.Steward.Role == Role.Economy)
                    .CountAsync();

                if (businessAssignments < flight.Aircraft.BusinessClassCrew ||
                    economyAssignments < flight.Aircraft.EconomyClassCrew)
                {
                    unassignedFlights.Add(flight);
                }
            }

            return unassignedFlights;
        }
    }
}
