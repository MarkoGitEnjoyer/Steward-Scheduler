using Scheduler.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Services
{
    public interface ISchedulingService
    {
        Task<WeeklySchedule> GenerateWeeklyScheduleAsync(DateTime weekStart);
        Task<bool> SaveScheduleAsync(WeeklySchedule schedule);
        Task<WeeklySchedule> GetScheduleForWeekAsync(DateTime weekStart);
        Task<List<FlightDto>> GetStewardScheduleAsync(int stewardId, DateTime weekStart);
    }
}
