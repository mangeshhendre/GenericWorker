using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Instrumentation
{
    public class StateChangeEventArgs : EventArgs
    {
        public string OldState { get; set; }
        public string NewState { get; set; }
    }
}
