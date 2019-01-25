using Safeguard.Library.Universal.WorkHandling.Interfaces;
using GenericWorker.Interfaces;
using GenericWorker.WorkHandlerDelegate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace GenericWorker.SupportClasses
{
    public class WorkHandlerMapping : IWorkHandlerMapping
    {
        public string MethodName
        {
            get;
            set;
        }

        public MethodInfo MethodInfo
        {
            get;
            set;
        }

        public IWorkHandlerBase HandlerOriginal
        {
            get;
            set;
        }

        public object Parameters
        {
            get;
            set;
        }

        public MethodInfo ValidationMethodInfo
        {
            get;
            set;
        }
    }
}
