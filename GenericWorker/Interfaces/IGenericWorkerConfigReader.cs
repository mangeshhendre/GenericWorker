using System;
namespace GenericWorker.SupportClasses
{
    interface IGenericWorkerConfigReader
    {
        bool ShadowCopyWorkHandlers { get; set; }
    }
}
