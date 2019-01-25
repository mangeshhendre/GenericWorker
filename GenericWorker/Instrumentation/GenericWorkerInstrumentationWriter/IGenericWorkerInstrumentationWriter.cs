using MQSeries.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Instrumentation.GenericWorkerInstrumentationWriter
{
    public interface IGenericWorkerInstrumentationWriter
    {
        void StateChange(object sender, StateChangeEventArgs e);
        void InstrumentationInterval(object sender, IntervalEventArgs e);
        QueueSettings QueueSettings { get; set; }
    }
}
