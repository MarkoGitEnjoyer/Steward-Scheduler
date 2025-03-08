using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IFeedbackRepository : IRepository<Models.Feedback>
    {
        Task<IEnumerable<Models.Feedback>> GetFeedbackByStewardAsync(int stewardId);
        Task<int> GetPositiveFeedbackCountAsync(int stewardId);
        Task<int> GetNegativeFeedbackCountAsync(int stewardId);
    }
}
