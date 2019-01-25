using Safeguard.Library.Universal.WorkHandling.Interfaces;
using GenericWorker.WorkHandlerDelegate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace GenericWorker.Interfaces
{
    public interface IWorkHandlerMapping
    {
        string MethodName
        {
            get;
            set;
        }

        //"original" style workhandler properties
        MethodInfo MethodInfo
        {
            get;
            set;
        }

        IWorkHandlerBase HandlerOriginal
        {
            get;
            set;
        }

        object Parameters
        {
            get;
            set;
        }

        MethodInfo ValidationMethodInfo
        {
            get;
            set;
        }
    }
}
