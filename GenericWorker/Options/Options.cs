using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker
{
    public class Options
    {
        [Value(0, HelpText = "Queue Name", Required = true)]
        public string QueueName { get; set; }

        [Option("AutoShutdownMemoryThresholdBytes", HelpText = "Memory autoshutdown threshold in bytes")]
        public long? AutoShutdownMemoryThresholdBytes { get; set; }
    }
}
