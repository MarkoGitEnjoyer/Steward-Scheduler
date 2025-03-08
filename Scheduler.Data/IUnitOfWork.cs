using Scheduler.Data.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data
{
    public interface IUnitOfWork : IDisposable
    {
        IStewardRepository Stewards { get; }
        IFlightRepository Flights { get; }
        IAssignmentRepository Assignments { get; }
        IFeedbackRepository Feedbacks { get; }
        IAircraftRepository AircraftTypes { get; }
        ILanguageRepository Languages { get; }
        Task<int> CompleteAsync();
    }
}
