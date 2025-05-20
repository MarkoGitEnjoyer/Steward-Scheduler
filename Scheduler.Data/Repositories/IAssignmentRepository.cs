using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IAssignmentRepository : IRepository<Models.Assignment>
    {
        // get assignments where stewardId is assigned
        Task<IEnumerable<Models.Assignment>> GetAssignmentsByStewardAsync(int stewardId);
        // get assignments of flightId
        Task<IEnumerable<Models.Assignment>> GetAssignmentsByFlightAsync(int flightId);
    }
}
