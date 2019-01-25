using GenericWorker.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace GenericWorker.SupportClasses
{
    public class CommunicationManager : ICommunicationManager
    {
        private Timer _timer;

        public CommunicationManager()
        {

        }

        public void Initialize(int interval, int sendPort, int receivePort)
        {
            this.Interval = interval;
            this.SendPort = sendPort;
            this.ReceivePort = receivePort;
            this._timer = new Timer(interval);
            this._timer.Elapsed += OnTimerElapsed;
            this._timer.Enabled = true;
        }

        public bool HeartbeatRequired
        {
            get;
            set;
        }

        public int Interval
        {
            get;
            set;
        }

        public int SendPort
        {
            get;
            set;
        }

        public int ReceivePort
        {
            get;
            set;
        }

        public void Heartbeat(Guid idGuid)
        {
            Console.WriteLine("Sent heartbeat to manager.");
            this.HeartbeatRequired = false;
        }

        public void ReportReady(Guid idGuid)
        {
            Console.WriteLine(string.Concat(idGuid, " ready to work."));
        }

        public void SendNotification(string data)
        {
            Console.WriteLine(data);
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            this.HeartbeatRequired = true;
        }
    }
}
