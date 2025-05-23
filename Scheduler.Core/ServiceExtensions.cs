using Microsoft.Extensions.DependencyInjection;
using Scheduler.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            services.AddScoped<ISchedulingService, SchedulingService>();

            return services;
        }
    }
}
