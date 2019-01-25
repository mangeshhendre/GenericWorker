using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Instrumentation
{
    public class WorkerStateDetail
    {
        public string Name { get; set; }
        public Stopwatch StopwatchActive { get; set; }
        public Stopwatch StopwatchInterval { get; set; }
        public bool IsActive
        {
            get
            {
                return StopwatchActive.IsRunning;
            }
        }
    }
}
