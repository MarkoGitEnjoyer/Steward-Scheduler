using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IAssignmentRepository : IRepository<Models.Assignment>
    {
        Task<IEnumerable<Models.Assignment>> GetAssignmentsByStewardAsync(int stewardId);
        Task<IEnumerable<Models.Assignment>> GetAssignmentsByFlightAsync(int flightId);
        Task<IEnumerable<Models.Steward>> GetAssignedStewardsAsync(int flightId);
        Task<bool> IsAssignedAsync(int stewardId, int flightId);
    }
}
