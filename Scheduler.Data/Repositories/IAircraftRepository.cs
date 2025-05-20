using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IAircraftRepository : IRepository<Models.AircraftType>
    {
        // get the id of aircraft by it's name in string
        Task<Models.AircraftType> GetAircraftTypeByNameAsync(string aircraftType);
    }
}
