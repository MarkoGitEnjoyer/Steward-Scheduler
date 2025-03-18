using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class GeneticAlgorithmConfig
    {
        public int PopulationSize { get; set; } = 10;
        public int MaxGenerations { get; set; } = 50;
        public float MutationRate { get; set; } = 0.2f;
        public float CrossoverRate { get; set; } = 0.8f;
        public float ElitismRate { get; set; } = 0.05f; // Top percentage to keep unchanged
    }
}
