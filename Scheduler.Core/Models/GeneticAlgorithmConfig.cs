using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scheduler.Core.Models
{
    public class GeneticAlgorithmConfig
    {
        public int PopulationSize { get; set; } = 50;

        public int MaxGenerations { get; set; } = 500;

        public float MutationRate { get; set; } = 0.3f;

        public float CrossoverRate { get; set; } = 0.7f;

        public float ElitismRate { get; set; } = 0.1f; 
    }
}