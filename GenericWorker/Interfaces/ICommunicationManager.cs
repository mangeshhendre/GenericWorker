using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Interfaces
{
    public interface ICommunicationManager
    {
        void Initialize(int interval, int sendPort, int receivePort);

        bool HeartbeatRequired
        {
            get;
            set;
        }

        int Interval
        {
            get;
            set;
        }

        int SendPort
        {
            get;
            set;
        }

        int ReceivePort
        {
            get;
            set;
        }

        void Heartbeat(Guid idGuid);

        void ReportReady(Guid idGuid);

        void SendNotification(string data);
    }
}
