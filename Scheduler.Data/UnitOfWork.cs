using Scheduler.Data.Models;
using Scheduler.Data.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Data
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly SchedulerDbContext _context;

        public IStewardRepository Stewards { get; private set; }
        public IFlightRepository Flights { get; private set; }
        public IAssignmentRepository Assignments { get; private set; }
        public IFeedbackRepository Feedbacks { get; private set; }
        public IAircraftRepository AircraftTypes { get; private set; }
        public ILanguageRepository Languages { get; private set; }

        public UnitOfWork(SchedulerDbContext context,
                          IStewardRepository stewardRepository,
                          IFlightRepository flightRepository,
                          IAssignmentRepository assignmentRepository,
                          IFeedbackRepository feedbackRepository,
                          IAircraftRepository aircraftRepository,
                          ILanguageRepository languageRepository)
        {
            _context = context;
            Stewards = stewardRepository;
            Flights = flightRepository;
            Assignments = assignmentRepository;
            Feedbacks = feedbackRepository;
            AircraftTypes = aircraftRepository;
            Languages = languageRepository;
        }

        public async Task<int> CompleteAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
