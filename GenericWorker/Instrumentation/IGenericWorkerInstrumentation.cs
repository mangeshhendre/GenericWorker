using GenericWorker.Instrumentation.GenericWorkerInstrumentationWriter;
using System;
using System.Collections.Generic;
namespace GenericWorker.Instrumentation
{
    public interface IGenericWorkerInstrumentation
    {
        void Start(string initialState);
        void Attach(IGenericWorkerInstrumentationWriter writer);
        void Detach(IGenericWorkerInstrumentationWriter writer);
        string CurrentState { get; set; }
        int IntervalProcessedMessagesCount { get; set; }
        Dictionary<string, int> IntervalProcessedMessagesCountByMethod { get; set; }
    }
}
