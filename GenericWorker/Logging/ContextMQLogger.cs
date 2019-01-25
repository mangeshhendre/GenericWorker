using Newtonsoft.Json;
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
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Logging
{
    class ContextMQLogger : ILogger
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
        public ContextMQLogger(IMQQueueController mqQueueController, IQueueSettingsResolver queueSettingsResolver)
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
        public Dictionary<string,string> OtherData { get { return _otherData; } set { _otherData = value; } }
        #endregion

        #region Public Methods
        public void Error(string message, Exception ex = null, Dictionary<string, string> otherData = null)
        {
            this.Put(LogEntrySeverity.Error, message, ex, otherData);
        }

        public void Warn(string message, Exception ex = null, Dictionary<string, string> otherData = null)
        {
            this.Put(LogEntrySeverity.Warn, message, ex, otherData);
        }

        public void Debug(string message, Dictionary<string, string> otherData = null)
        {
            this.Put(LogEntrySeverity.Debug, message, otherData: otherData);
        }

        public void Info(string message, Dictionary<string, string> otherData = null)
        {
            this.Put(LogEntrySeverity.Info, message, otherData: otherData);
        }

        #endregion

        #region Private Methods
        private void Put(LogEntrySeverity level, string message, Exception ex = null, Dictionary<string, string> otherData = null)
        {
            //only log if the current desired level calls for it
            if (_logLevel < level)
                return;

            //add exception details to otherData (if they don't already exist)
            AddExceptionToOtherData(ex, ref otherData);

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
                    Message = message,
                    Language = ".NET",
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

                if (!putSuccess)
                {
                    switch (level)
                    {
                        case LogEntrySeverity.Debug:
                            this._fallbackLogger.Debug(message);
                            break;
                        case LogEntrySeverity.Info:
                            this._fallbackLogger.Info(message);
                            break;
                        case LogEntrySeverity.Error:
                            this._fallbackLogger.Error(message, ex);
                            break;
                        case LogEntrySeverity.Warn:
                            this._fallbackLogger.Warn(message, ex);
                            break;
                    }
                }
            }); //task
        }
        private void AddExceptionToOtherData(Exception ex, ref Dictionary<string, string> otherData)
        {
            if (ex == null)
                return;

            var newOtherData = otherData == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(otherData, StringComparer.OrdinalIgnoreCase);

            if (!newOtherData.ContainsKey("ExceptionMessage"))
                newOtherData["ExceptionMessage"] = ex.Message;

            if (!newOtherData.ContainsKey("ExceptionStackTrace"))
                newOtherData["ExceptionStackTrace"] = ex.StackTrace;

            if (!newOtherData.ContainsKey("InnerExceptionMessage") && ex.InnerException != null)
                newOtherData["InnerExceptionMessage"] = ex.InnerException.Message;

            otherData = newOtherData;
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
