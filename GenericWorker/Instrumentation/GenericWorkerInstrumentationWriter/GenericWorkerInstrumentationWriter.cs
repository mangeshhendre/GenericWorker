using Microsoft.Practices.Unity;
using Safeguard.Library.Universal.Json.Interfaces;
using Safeguard.Library.Universal.Json.Support;
using Common.Dto;
using MQSeries.Interfaces;
using MQSeries.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.Instrumentation.GenericWorkerInstrumentationWriter
{
    public class GenericWorkerInstrumentationWriter : IGenericWorkerInstrumentationWriter
    {
        const string InstrumentationServiceMethod = "Instrumentation.Write";

        #region Private Members
        private readonly IMQQueueController _mqQueueController;
        private readonly IJSonRPCHelper _jsonRpcHelper;
        private readonly IQueueSettingsResolver _queueSettingsResolver;
        private readonly QueueSettings _intrumentationQueueSettings;
        private QueueSettings _genericWorkerQueueSettings;
        #endregion

        #region Constructors
        [InjectionConstructor]
        public GenericWorkerInstrumentationWriter(IMQQueueController mqQueueController,
                                                  IJSonRPCHelper jsonRpcHelper,
                                                  IQueueSettingsResolver queueSettingsResolver)
        {
            _mqQueueController = mqQueueController;
            _jsonRpcHelper = jsonRpcHelper;
            _queueSettingsResolver = queueSettingsResolver;

            _intrumentationQueueSettings = _queueSettingsResolver.ResolveByMethodName(InstrumentationServiceMethod);
        }

        public GenericWorkerInstrumentationWriter(IUnityContainer container)
        {
            _mqQueueController = container.Resolve<IMQQueueController>();
            _jsonRpcHelper = container.Resolve<IJSonRPCHelper>();
            _queueSettingsResolver = container.Resolve<IQueueSettingsResolver>();

            _intrumentationQueueSettings = _queueSettingsResolver.ResolveByMethodName(InstrumentationServiceMethod);
        }
        #endregion

        #region Properties
        public QueueSettings QueueSettings
        {
            get
            {
                return _genericWorkerQueueSettings;
            }
            set
            {
                _genericWorkerQueueSettings = value;
            }
        }
        #endregion

        #region Event Handlers
        public void StateChange(object sender, StateChangeEventArgs e)
        {
        }

        public void InstrumentationInterval(object sender, IntervalEventArgs e)
        {
            var jsonRequest = GetJsonRequest(e);
            var instrumentationMessage = this._jsonRpcHelper.SerializeJSonRPCRequest(jsonRequest);

            Task.Factory.StartNew(() =>
            {   
                bool putSuccess = this._mqQueueController.Put(_intrumentationQueueSettings,
                                                              instrumentationMessage);
            });

        }
        #endregion

        #region Private Methods
        private JsonRequest GetJsonRequest(IntervalEventArgs e)
        {
            var details = new Dictionary<string, object>();
            //double workingIntervalActiveSeconds = 0;

            //Worker State details
            foreach (var s in e.WorkerStateDetails)
            {
                //In addition to storing the WorkingIntervalActive as an interval string, we are going to store the total seconds for easier math later. (Grab it here as we are already iterating the WorkerStateDetails)
                //if (string.Compare(s.Name, "Working", true) == 0) workingIntervalActiveSeconds = s.StopwatchInterval.Elapsed.TotalSeconds;

                var detail = new Dictionary<string, object>();
                detail.Add("TotalActive", s.StopwatchActive.Elapsed.TotalSeconds);
                detail.Add("IntervalActive", s.StopwatchInterval.Elapsed.TotalSeconds);
                details.Add(s.Name, detail);
            }

            //Statistics details
            var statsDetail = new Dictionary<string, object>();
            statsDetail.Add("IntervalProcessedMessageCount", e.IntervalProcessedMessageCount);
            //statsDetail.Add("WorkingIntervalActiveSeconds", workingIntervalActiveSeconds); // Yes, this is a duplicate value of the WorkerStateDetails.  However, It's far less expensive to SUM this value then try to convert oracle intervals to total seconds.
            details.Add("Statistics", statsDetail);

            //Method Specific Stats
            var methodStatsDetail = new Dictionary<string, object>();
            foreach (var s in e.IntervalProcessedMessageCountByMethod)
            {
                methodStatsDetail.Add(s.Key.ToUpper(), s.Value);
            }
            if(methodStatsDetail.Count > 0) details.Add("IntervalMethodProcessedCount:", methodStatsDetail);    

            
            var instrumentationEntry = new InstrumentationEntry
            {
                MachineName = Environment.MachineName,
                AppName = AppDomain.CurrentDomain.FriendlyName,
                ProcessId = Process.GetCurrentProcess().Id,
                Data = new InstrumentationData()
                {
                    TotalTime = e.TotalTime,
                    IntervalTime = e.IntervalTime,
                    Details = details
                },
                Date = DateTime.Now
            };

            if (this._genericWorkerQueueSettings != null && !string.IsNullOrEmpty(this._genericWorkerQueueSettings.RequestQueue))
            {
                instrumentationEntry.Data.Queue = this._genericWorkerQueueSettings.RequestQueue;
            }


            return new JsonRequest
            {
                JsonRpc = "2.0",
                Id = Guid.NewGuid(),
                Method = InstrumentationServiceMethod,
                Params = new Dictionary<string, object> { { "InstrumentationEntry", instrumentationEntry } }
            };
        }
        #endregion
    }
}
