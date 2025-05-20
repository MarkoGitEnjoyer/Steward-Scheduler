using Scheduler.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IFlightRepository : IRepository<Models.Flight>
    {
        Task<List<Flight>> GetFlightsForAWeek(DateTime weekStart);
    }
}
