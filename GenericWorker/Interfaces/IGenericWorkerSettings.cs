using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Interfaces
{
    public interface IGenericWorkerSettings
    {
        string HandlerDirectory { get; set; }
        int InstrumentationTimer { get; set; }

        int? AutoShutdownMinutesMin { get; set; }
        int? AutoShutdownMinutesMax { get; set; }
        int? AutoShutdownProcessedMessageCountMin { get; set; }
        int? AutoShutdownProcessedMessageCountMax { get; set; }
        long? AutoShutdownMemoryThresholdBytes { get; set; }

        int LogLevel { get; set; }

        string CommandProxyTarget { get; set; }
        long CommandProxy_PollingIntervalSeconds { get; set; }
    }
}
