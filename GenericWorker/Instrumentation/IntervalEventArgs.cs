using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Instrumentation
{
    public class IntervalEventArgs : EventArgs
    {
        public TimeSpan TotalTime { get; set; }
        public TimeSpan IntervalTime { get; set; }
        public List<WorkerStateDetail> WorkerStateDetails { get; set; }
        public int IntervalProcessedMessageCount { get; set; }
        public Dictionary<string, int> IntervalProcessedMessageCountByMethod { get; set; }
    }
}
