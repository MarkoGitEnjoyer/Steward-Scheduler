using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data.Repositories
{
    public interface IFeedbackRepository : IRepository<Models.Feedback>
    {
        Task<int> GetPositiveFeedbackCountAsync(int stewardId);
        Task<int> GetNegativeFeedbackCountAsync(int stewardId);
    }
}
