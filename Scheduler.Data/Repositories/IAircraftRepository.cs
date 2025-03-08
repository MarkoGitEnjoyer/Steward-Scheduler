using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IAircraftRepository : IRepository<Models.AircraftType>
    {
        Task<Models.AircraftType> GetAircraftTypeByNameAsync(string aircraftType);
        Task<int> GetRequiredBusinessCrewAsync(string aircraftType);
        Task<int> GetRequiredEconomyCrewAsync(string aircraftType);
    }
}
