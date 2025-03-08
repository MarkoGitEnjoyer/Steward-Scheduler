using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IFlightRepository : IRepository<Models.Flight>
    {
        Task<IEnumerable<Models.Flight>> GetUpcomingFlightsAsync(DateTime fromDate);
        Task<IEnumerable<Models.Flight>> GetFlightsByAircraftTypeAsync(string aircraftType);
        Task<IEnumerable<Models.Flight>> GetFlightsByPriorityAsync(int minimumPriority);
        Task<IEnumerable<Models.Flight>> GetUnassignedFlightsAsync();
    }
}
