using Microsoft.Practices.Unity;
using GenericWorker.Interfaces;
using GenericWorker.SupportClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Configuration
{
    public class GenericWorkerConfiguration
    {
        [InjectionConstructor]
        public GenericWorkerConfiguration() { }
        public GenericWorkerConfiguration(bool getConfigFromEtcd) { } //provided for unit testing

        //Settings
        public string HandlerDirectory { get; set; }
        public int? AutoShutdownMinutesMin { get; set; }
        public int? AutoShutdownMinutesMax { get; set; }
        public int? AutoShutdownProcessedMessageCountMin { get; set; }
        public int? AutoShutdownProcessedMessageCountMax { get; set; }
        public long? AutoShutdownMemoryThresholdBytes { get; set; }
        public int InstrumentationTimer { get; set; }
        public int LogLevel { get; set; }


        //StatsD
        public string StatsD_Host { get; set; }
        public int StatsD_Port { get; set; }

        //Command Proxy GRPC
        public string CommandProxy_Host { get; set; }
        public int CommandProxy_Port { get; set; }
        public long CommandProxy_PollingIntervalSeconds { get; set; }


        public IGenericWorkerSettings Settings
        {
            get
            {
                return new GenericWorkerSettings
                {
                     HandlerDirectory = this.HandlerDirectory,
                     InstrumentationTimer = this.InstrumentationTimer,
                     AutoShutdownMinutesMin = this.AutoShutdownMinutesMin,
                     AutoShutdownMinutesMax = this.AutoShutdownMinutesMax,
                     AutoShutdownProcessedMessageCountMin = this.AutoShutdownProcessedMessageCountMin,
                     AutoShutdownProcessedMessageCountMax = this.AutoShutdownProcessedMessageCountMax,
                     AutoShutdownMemoryThresholdBytes = this.AutoShutdownMemoryThresholdBytes,
                     LogLevel = this.LogLevel,
					 CommandProxyTarget = string.Format("{0}:{1}", this.CommandProxy_Host, this.CommandProxy_Port),
                     CommandProxy_PollingIntervalSeconds = this.CommandProxy_PollingIntervalSeconds
                };
            }
        }
    }
}
