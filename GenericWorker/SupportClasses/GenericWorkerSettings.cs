using GenericWorker.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.SupportClasses
{
    class GenericWorkerSettings : IGenericWorkerSettings
    {
        public string HandlerDirectory { get; set; }
        public int InstrumentationTimer { get; set; }

        public int? AutoShutdownMinutesMin { get; set; }
        public int? AutoShutdownMinutesMax { get; set; }
        public int? AutoShutdownProcessedMessageCountMin { get; set; }
        public int? AutoShutdownProcessedMessageCountMax { get; set; }
        public long? AutoShutdownMemoryThresholdBytes { get; set; }

        public int LogLevel { get; set; }

        public string CommandProxyTarget { get; set; }
        public long CommandProxy_PollingIntervalSeconds { get; set; }
    }
}
