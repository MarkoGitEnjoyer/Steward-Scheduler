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
    public class AircraftRepository : Repository<AircraftType>, IAircraftRepository
    {
        public AircraftRepository(SchedulerDbContext context) : base(context)
        {
        }

        public async Task<AircraftType> GetAircraftTypeByNameAsync(string aircraftType)
        {
            return await _context.AircraftTypes
                .FirstOrDefaultAsync(a => a.AircraftTypeId == aircraftType);
        }
    }
}
