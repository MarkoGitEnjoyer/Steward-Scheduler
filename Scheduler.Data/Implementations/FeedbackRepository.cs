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
    public class FeedbackRepository : Repository<Feedback>, IFeedbackRepository
    {
        public FeedbackRepository(SchedulerDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Feedback>> GetFeedbackByStewardAsync(int stewardId)
        {
            return await _context.Feedbacks
                .Where(f => f.StewardId == stewardId)
                .ToListAsync();
        }

        public async Task<int> GetPositiveFeedbackCountAsync(int stewardId)
        {
            return await _context.Feedbacks
                .CountAsync(f => f.StewardId == stewardId && f.FeedbackType == FeedbackType.Praise);
        }

        public async Task<int> GetNegativeFeedbackCountAsync(int stewardId)
        {
            return await _context.Feedbacks
                .CountAsync(f => f.StewardId == stewardId && f.FeedbackType == FeedbackType.Complaint);
        }
    }

}