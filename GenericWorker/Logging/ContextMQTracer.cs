using Newtonsoft.Json;
using Safeguard.Library.Universal.Contracts.Tracing;
using Safeguard.Library.Universal.Dto;
using Safeguard.Library.Universal.Json.Support;
using Safeguard.Library.Universal.Logging;
using Safeguard.Library.Universal.Logging.Interfaces;
using MQSeries.Interfaces;
using MQSeries.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Logging
{
    class ContextMQTracer : ITracer
    {
        #region Private Members
        private const string LoggingServiceMethod = "Logging.Write";

        private readonly IMQQueueController _mqQueueController;
        private readonly QueueSettings _loggingQueueSettings;
        private readonly ILogger _fallbackLogger;

        private LogEntrySeverity _logLevel = LogEntrySeverity.Error;
        private string _applicationName = null;
        private string _applicationParameters = null;
        private string _contextPrimary = null;
        private string _contextSecondary = null;
        private Dictionary<string, string> _otherData = new Dictionary<string, string>();
        #endregion

        #region Constructors
        public ContextMQTracer(IMQQueueController mqQueueController, IQueueSettingsResolver queueSettingsResolver)
        {
            _mqQueueController = mqQueueController;
            _loggingQueueSettings = queueSettingsResolver.ResolveByMethodName(LoggingServiceMethod);
            _fallbackLogger = new FallBackLogger();
        }
        #endregion

        #region Public Properties
        public LogEntrySeverity LogLevel { get { return _logLevel; } set { _logLevel = value; } }
        public string ApplicationName { get { return _applicationName; } set { _applicationName = value; } }
        public string ApplicationParameters { get { return _applicationParameters; } set { _applicationParameters = value; } }
        public string ContextPrimary { get { return _contextPrimary; } set { _contextPrimary = value; } }
        public string ContextSecondary { get { return _contextSecondary; } set { _contextSecondary = value; } }
        public Dictionary<string, string> OtherData { get { return _otherData; } set { _otherData = value; } }
        #endregion

        #region Public Methods
        public void Write(string message, Dictionary<string, string> otherData = null)
        {
            this.Put(LogEntrySeverity.Trace, message, otherData: otherData);
        }
        #endregion

        #region Private Methods
        private void Put(LogEntrySeverity level, string message, Exception ex = null, Dictionary<string, string> otherData = null)
        {
            //only log if the current desired level calls for it
            if (_logLevel < level)
                return;

            //add "global" OtherData to otherData
            AddGlobalOtherDataToOtherData(this.OtherData, ref otherData);

            //build the request
            var jsonRequest = new JsonRequest
            {
                JsonRpc = "2.0",
                Id = Guid.NewGuid(),
                Method = LoggingServiceMethod,
                Params = new
                {
                    Host = Environment.MachineName,
                    Application = _applicationName,
                    ApplicationParameters = _applicationParameters,
                    PID = Process.GetCurrentProcess().Id,
                    Level = level,
                    Language = ".NET",
                    Message = message,
                    ContextPrimary = _contextPrimary,
                    ContextSecondary = _contextSecondary,
                    Other = otherData
                }
            };

            //serialize
            var loggingMessage = JsonConvert.SerializeObject(jsonRequest);

            //send it
            Task.Factory.StartNew(() =>
            {
                bool putSuccess = this._mqQueueController.Put(_loggingQueueSettings, loggingMessage);

                System.Diagnostics.Trace.Write(message);
            }); //task
        }

        private void AddGlobalOtherDataToOtherData(Dictionary<string, string> globalOtherData, ref Dictionary<string, string> otherData)
        {
            var newOtherData = otherData == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(otherData, StringComparer.OrdinalIgnoreCase);

            //add extra to other 
            foreach (var de in globalOtherData)
            {
                if (!newOtherData.ContainsKey(de.Key))
                    newOtherData[de.Key] = de.Value;
            }

            otherData = newOtherData;
        }
        #endregion
    }
}
