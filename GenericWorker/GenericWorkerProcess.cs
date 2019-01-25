using Grpc.Core;
using Microsoft.Practices.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GenericWorker.Interfaces;
using GenericWorker.Logging;
using GenericWorker.Utility;
using GenericWorker.WorkHandlerDelegate;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using MyLibrary.Common.Json.Interfaces;
using MyLibrary.Common.Interfaces;
using MyLibrary.Common.StatsD;
using MyLibrary.Common.WorkHandling.Interfaces;
using MyLibrary.Common.WorkHandling;
using MyLibrary.Common.Json.Support;
using MyLibrary.Common.Logging;
using MyLibrary.Common.Tracing;
using MyLibrary.Common.Json.Support.Tracing;
using MyLibrary.Common.WorkHandling.Exceptions;

namespace GenericWorker
{
    public class GenericWorkerProcess : IDisposable
    {
        #region Private variables
        private const string SERVICE_BUS_METHODS = "ServiceBusMethods";
        private const string SBASYNC_PROCESS_RESPONSE_METHOD = "SBAsync.ProcessResponse";
        private const string TRACE_ACTOR_ROLE = "Worker";

        private readonly IUnityContainer _container;
        private readonly IJSonRPCHelper _jsonRpcHelper;
        private readonly ICommunicationManager _communicationManager;
        private readonly Logger _logger;
        private readonly Tracer _tracer;
        private readonly IAssemblyResolver _assemblyResolver;
        private readonly IAssemblyLocator _assemblyLocator;
        private readonly IStatsDClient _statsDClient;
        private readonly IGenericWorkerSettings _genericWorkerSettings;

        //type arrays used for locating service methods (Currently support public methods that have parameter types of string or JsonRequest)
        private readonly Type[] _arrArgumentTypes = new Type[1] { typeof(string) };
        private readonly Type[] _arrJsonRequestArgumentTypes = new Type[1] { typeof(JsonRequest) };
        private readonly JsonSerializer _jsonSerializer;
        private readonly Guid _idGuid;


        //settings, handler types/methods, mappings
        private Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>> _handlerTypesMethodsOriginal;
        private Dictionary<string, Lazy<WorkHandler, IWorkHandlerMethodData>> _handlerTypesMethods;
        private Dictionary<string, IWorkHandlerMapping> _handlerMappings;
        private List<HandlerMethodInfo> _handlerMethodInfoCache;

        private bool _continueExecution = true;
        private bool _isAsyncResponseWorker = false;

        //auto shutdown
        private Stopwatch _totalStopwatch;
        private int _processedMessageCount;
        private int? _autoShutdownMinutes;
        private int? _autoShutdownProcessedMessageCount;
        private long? _autoShutdownMemoryThresholdBytes;

        //statistics
        private Stopwatch _processMessageStopwatch = null;

        //run 
        string _queueName = null;
        #endregion

        #region Constructors
        public GenericWorkerProcess(IUnityContainer container)
        {
            try
            {
                Console.WriteLine("Init GenericWorkerProcess");

                //get a new identifier for this instance
                _idGuid = Guid.NewGuid();

                //setup depdencies
                _container = container;
                _assemblyResolver = _container.Resolve<IAssemblyResolver>();
                _jsonRpcHelper = _container.Resolve<IJSonRPCHelper>();
                _communicationManager = _container.Resolve<ICommunicationManager>();
                _logger = (Logger)_container.Resolve<ILogger>();
                _tracer = (Tracer)_container.Resolve<ITracer>();
                _assemblyLocator = _container.Resolve<IAssemblyLocator>();
                _statsDClient = _container.Resolve<IStatsDClient>();

                //setup initial assignments 
                _handlerTypesMethodsOriginal = new Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>>(StringComparer.OrdinalIgnoreCase);
                _handlerTypesMethods = new Dictionary<string, Lazy<WorkHandler, IWorkHandlerMethodData>>(StringComparer.OrdinalIgnoreCase);
                _handlerMethodInfoCache = new List<HandlerMethodInfo>();

                _handlerMappings = new Dictionary<string, IWorkHandlerMapping>();
                _jsonSerializer = new JsonSerializer();

                //setup any configuration required at start
                _genericWorkerSettings = _container.Resolve<IGenericWorkerSettings>();
                SetupAutoShutdown();

            }
            catch (Exception ex)
            {
                //if an exception has happened while setting up there's a great chance we cannot log it normally.  
                //make this exception as visible as possible by printing to the screen.
                Console.WriteLine(string.Format("Exception:{0}\r\nStackTrace:{1}", ex.Message, ex.StackTrace));

                if (ex.InnerException != null)
                    Console.WriteLine(string.Format("Exception:{0}\r\nStackTrace:{1}", ex.InnerException.Message, ex.InnerException.StackTrace));
            }
        }
        #endregion

        #region Properties
        /// <summary>
        /// This method should be used ONLY for unit testing.  We want to fully test the GenericWorkerProcess, but have to short circuit the POLLING with this property.  
        /// Otherwise it would run forever.
        /// </summary>
        public bool ContinueExecution
        {
            get
            {
                return _continueExecution;
            }
            set
            {
                _continueExecution = value;
            }
        }
        #endregion

        #region Event Handlers
        #endregion

        #region Public and Protected Methods
        public void Run(string queueName)
        {
            _tracer.Write(string.Format("Worker starting - Queue: {0} - InstanceId: {1}", queueName, _idGuid));

            //set queue name (for logging purposes)
            _queueName = queueName;

            //log context
            SetLogContext(primary: "GenericWorkerProcess", secondary: "Run");
            _tracer.OtherData["InstanceId"] = _idGuid.ToString();
            _logger.OtherData["InstanceId"] = _idGuid.ToString();

            ////don't bother starting if the specified queue is invalid
            //if (!ValidateRequestQueue(queueName)) return;

            //preload
            if (!InitHandlerTypesMethods(queueName, true)) return;
            InitWorkHandlerMapping(); //original recipe
            InitWorkHandlerMethodInfoCache(); //extra crispy
            InitJsonSerializer();

            //initialize instrumentation
            _totalStopwatch.Start();

            //announce ready
            _communicationManager.ReportReady(_idGuid);
            _tracer.Write(string.Format("Worker started - Queue: {0} - InstanceId: {1}", queueName, _idGuid));

            //begin working
            while (_continueExecution)
            {
                Poll();
                ProcessCommands();
                CheckAutoShutdown();
            }
        }

        protected void Poll()
        {
            SetLogContext(primary: "GenericWorkerProcess", secondary: "Poll");
            var message = ""; //pull the message from somewhere

            if (message != null)
            {
                JsonRequest request = _jsonRpcHelper.DeSerializeJSonRPCRequest(message); 
                //if (_isAsyncResponseWorker)
                //{
                //    //handle async response messages
                //    HandleSBAsyncResponseMessage(request);
                //}
                //else
                {
                    //handle normal request messages
                    HandleRequestMessage(request);
                }
            }//end if
        }
        #endregion

        #region Private Methods
        private int? GetLogLevel(string logLevel)
        {
            if (string.Equals(logLevel, LogEntrySeverity.Error.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return (int)LogEntrySeverity.Error;
            }
            else if (string.Equals(logLevel, LogEntrySeverity.Warn.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return (int)LogEntrySeverity.Warn;
            }
            else if (string.Equals(logLevel, LogEntrySeverity.Debug.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return (int)LogEntrySeverity.Debug;
            }
            else if (string.Equals(logLevel, LogEntrySeverity.Info.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return (int)LogEntrySeverity.Info;
            }
            else if (string.Equals(logLevel, LogEntrySeverity.Trace.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return (int)LogEntrySeverity.Trace;
            }
            else
            {
                return null;
            }
        }
        private bool InitHandlerTypesMethods(string queueName, bool eagerLoad = false)
        {
            var serviceName = queueName.Split('.')[0];
            _tracer.Write(string.Format("Initializing Handler Types - service: {0}", serviceName));

            //a special queue exists called "SBAsyncResponse" which is unique in that it will contain RESPONSES not REQUESTS.  
            //this special case requires that we load the SBAsync service code instead of processing based on Queue Name.  
            //it's a one-off that should literally never happen again
            if (_isAsyncResponseWorker) { serviceName = "SBAsync"; }

            if (!_assemblyLocator.WorkHandlerServiceDirectories.ContainsKey(serviceName))
            {
                var message = string.Format("Handler not found for service: {0}", serviceName);
                _logger.Error(message, otherData: new Dictionary<string, string> { { "Fatal", "true" } });
                Console.WriteLine(message);
                return false;
            }

            var handlerDirectory = _assemblyLocator.WorkHandlerServiceDirectories[serviceName];
            _tracer.Write(string.Format("Initializing Handler Types - directory: {0}", handlerDirectory));
            var workHandlerInfo = _assemblyResolver.GetAssemblyWorkHandlerInfo(handlerDirectory, true);
            _handlerTypesMethodsOriginal = workHandlerInfo.OriginalWorkHandlers;
            _handlerTypesMethods = workHandlerInfo.WorkHandlers;

            if (eagerLoad)
            {
                //by getting "value" on the lazy loaded object we create an instance
                //need to iterate each service and instantiate the FIRST method for each.  
                //no need to create for each method; all methods in a service use the same instance. (e.g. Logging.Write, Logging.Read use the same instance)
                _handlerTypesMethodsOriginal.Keys.GroupBy(k => k.Split('.')[0]).ToList().ForEach(s => {
                    Console.WriteLine("Loading: {0}", s.Key);
                    _tracer.Write(string.Format("Initializing Handler Types - loading: {0}", s.Key));
                    string key = _handlerTypesMethodsOriginal.Keys.First(k => k.StartsWith(string.Concat(s.Key, ".")));
                    IWorkHandlerBase value = _handlerTypesMethodsOriginal[key].Value;
                });

                //new way
                _handlerTypesMethods.Keys.GroupBy(k => k.Split('.')[0]).ToList().ForEach(s =>
                {
                    Console.WriteLine("Loading: {0}", s.Key);
                    _tracer.Write(string.Format("Initializing Handler Types - loading: {0}", s.Key));
                    string key = _handlerTypesMethods.Keys.First(k => k.StartsWith(string.Concat(s.Key, ".")));
                    _handlerTypesMethods[key].Value.GetType(); //instantiates the class               
                });
            }

            //if we have types assume this is the only workhandler and set logging
            if (_handlerTypesMethods.Count() > 0)
            {
                var assemblyName = _handlerTypesMethods.First().Value.Value.GetAssemblyMetadata().AssemblyName;
                _logger.OtherData["WorkHandlerVersion"] = assemblyName;
                _tracer.OtherData["WorkHandlerVersion"] = assemblyName;
            }

            return true;
        }

        //Parameters and Validation
        private void ConvertRequestParameters(ref JsonRequest request)
        {
            if (request == null || request.Params == null)
                return;

            if (request.Params.GetType() == typeof(JArray))
            {
                //Update to List
                var parametersList = new List<object>();
                foreach (var item in (JArray)request.Params)
                {
                    if (item is JObject)
                        parametersList.Add(item.Value<JObject>());
                    else
                        parametersList.Add(item.Value<JValue>().Value);
                }
                request.Params = parametersList;
            }
            else if (request.Params.GetType() == typeof(JObject))
            {
                //Update to Dictionary
                var parametersDictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in (JObject)request.Params)
                {
                    if (item.Value is JObject)
                        parametersDictionary.Add(item.Key, item.Value.Value<JObject>());
                    else if (item.Value is JArray)
                        parametersDictionary.Add(item.Key, item.Value.Value<JArray>());
                    else
                        parametersDictionary.Add(item.Key, item.Value.Value<JValue>().Value);
                }
                request.Params = parametersDictionary;
            }
            else
            {
                //Params were not a JObject (e.g. key/value pairs) or JArray (ordinal)
                throw new Exception("Params must be either name/value pairs or an array");
            }
        }
        private bool ValidateService(JsonRequest request, Guid? correlationId, string serviceName, TraceActor traceActor)
        {
            var returnValue = _assemblyLocator.WorkHandlerServiceDirectories.ContainsKey(serviceName);

            //Use the AssemblyLocator's WorkHandlerServiceDirectories to check that we have a WorkHandler directory for the provided ServiceName
            if (!returnValue)
            {
                //no handler exists that can process the message
                var errorMessage = string.Format("No handler found. Cannot process request method: {0}", request.Method);

                //notify with error
                _communicationManager.SendNotification(errorMessage);

                //get error
                var response = _jsonRpcHelper.GetJsonRPCErrorResponse(request, errorMessage, null, -32601);

                //set trace if applicable
                SetTraceActor(request.TraceId, traceActor, ref response);

                if (!string.IsNullOrEmpty(request.AsyncMessageId)) //Async Response
                {
                    //Set AsyncMessageId
                    response.AsyncMessageId = request.AsyncMessageId;

                    //Serialize response
                    var responseString = _jsonRpcHelper.SerializeJSonRPCResponse(response);

                    //Place response in AsyncResponseQueue
                    _queueController.PutAsyncResponseQueue(_settings, responseString);
                }
                else if (!string.IsNullOrEmpty(_settings.ResponseQueue))
                {
                    //Serialize response
                    var responseString = _jsonRpcHelper.SerializeJSonRPCResponse(response);

                    if (correlationId != null)
                        _queueController.PutResponse(_settings, responseString, (Guid)correlationId);
                }
            }

            return returnValue;
        }
        private bool ValidateMethods(JsonRequest request, Guid? correlationId, string serviceName, TraceActor traceActor)
        {
            var returnValue = false;

            //resolve handler location and supported methods
            if (!_handlerTypesMethodsOriginal.ContainsKey(request.Method) &&
                !_handlerTypesMethods.ContainsKey(request.Method))
            {
                InitHandlerTypesMethods(serviceName);
            }

            //validate the handler supports the requested method
            if (_handlerTypesMethodsOriginal.ContainsKey(request.Method)) //original
            {
                returnValue = true;
            }
            //else if (_handlerTypesMethods.ContainsKey(request.Method) &&
            //         _handlerMethodInfoCache.Any(i =>
            //            string.Format("{0}.{1}", i.MethodInfo.DeclaringType.Name, i.MethodInfo.Name).Equals(request.Method, StringComparison.OrdinalIgnoreCase) &&
            //            i.ParameterInfo.All(pi => pi.HasDefaultValue || passedInParams.Keys.Contains(pi.Name))))
            else if (_handlerTypesMethods.ContainsKey(request.Method))
            {
                returnValue = true;
            }
            else
            {
                //no supported method exists that can process the message
                var errorMessage = string.Format("Method {0} not found for Service: {1}", request.Method, serviceName);

                //notify with error
                _communicationManager.SendNotification(errorMessage);

                //get error
                var response = _jsonRpcHelper.GetJsonRPCErrorResponse(request, errorMessage, null, -32601);

                //set trace if applicable
                SetTraceActor(request.TraceId, traceActor, ref response);

                //response with error
                if (!string.IsNullOrEmpty(request.AsyncMessageId)) //Async Response
                {
                    //Set AsyncMessageId
                    response.AsyncMessageId = request.AsyncMessageId;

                    //Serialize response
                    var responseString = _jsonRpcHelper.SerializeJSonRPCResponse(response);

                    //Place response in AsyncResponseQueue
                    _queueController.PutAsyncResponseQueue(_settings, responseString);
                }
                else if (!string.IsNullOrEmpty(_settings.ResponseQueue))
                {
                    //Serialize response
                    var responseString = _jsonRpcHelper.SerializeJSonRPCResponse(response);

                    if (correlationId != null)
                        _queueController.PutResponse(_settings, responseString, (Guid)correlationId);
                }
            }

            return returnValue;
        }
        private IWorkHandlerMapping GetWorkHandlerMapping(JsonRequest request)
        {
            IWorkHandlerMapping returnValue = null;
            if (_handlerMappings.ContainsKey(request.Method))
            {
                returnValue = _handlerMappings[request.Method];
                returnValue.Parameters = request.Params;
            }
            else
            {
                returnValue = _container.Resolve<IWorkHandlerMapping>();
                returnValue.MethodName = request.Method;
                returnValue.HandlerOriginal = _handlerTypesMethodsOriginal[request.Method].Value;
                returnValue.MethodInfo = GetMethodInfo(returnValue.HandlerOriginal.GetType(), request.Method);
                returnValue.ValidationMethodInfo = GetValidateMethodInfo(returnValue.HandlerOriginal.GetType());
                returnValue.Parameters = request.Params;
                _handlerMappings.Add(request.Method, returnValue);
            }

            return returnValue;
        }
        private void InitWorkHandlerMapping()
        {
            //original recipe
            foreach (var method in _handlerTypesMethodsOriginal.Keys)
            {
                Console.WriteLine("Mapping {0}", method);
                _tracer.Write(string.Format("Initializing Handler Mapping - {0}", method));

                var mapping = _container.Resolve<IWorkHandlerMapping>();
                mapping.MethodName = method;
                mapping.HandlerOriginal = _handlerTypesMethodsOriginal[method].Value;

                var handlerType = mapping.HandlerOriginal.GetType();
                mapping.MethodInfo = GetMethodInfo(handlerType, method);
                mapping.ValidationMethodInfo = GetValidateMethodInfo(handlerType);

                var pt = mapping.MethodInfo.GetParameters()[0].ParameterType;

                _handlerMappings.Add(method, mapping);
            }

            //if we have types assume this is the only workhandler and set logging
            if (_handlerTypesMethodsOriginal.Count() > 0)
            {
                var assemblyName = _handlerTypesMethodsOriginal.First().Value.Value.GetAssemblyMetadata().AssemblyName;
                _logger.OtherData["WorkHandlerVersion"] = assemblyName;
                _tracer.OtherData["WorkHandlerVersion"] = assemblyName;
            }
        }

        private void InitWorkHandlerMethodInfoCache()
        {
            //*************************************
            //new extra crispy recipe

            _handlerTypesMethods.Keys.GroupBy(k => k.Split('.')[0]).ToList().ForEach(s =>
            {
                var key = _handlerTypesMethods.Keys.First(k => k.StartsWith(string.Concat(s.Key, ".")));
                var type = _handlerTypesMethods[key].Value.GetType();

                //get all public and private instance methods
                var methodInfoPublic = type.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                var methodInfoPrivateValidation = type.GetMethods(BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance).Where(mi => mi.ReturnType == typeof(ParameterValidationResult)).ToArray();
                var methodInfoPrivateValidationForMethods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance).Where(mi => mi.ReturnType == typeof(ParameterValidationResult)).Where(mi =>
                {
                    //only where one parameter that is Dictioanry<string, object>
                    var parametersInfo = mi.GetParameters();
                    if (parametersInfo != null && parametersInfo.Length == 1)
                    {
                        return parametersInfo[0].ParameterType == typeof(Dictionary<string, object>);
                    }

                    return false;
                }).ToArray();

                methodInfoPublic.ToList().ForEach(mi =>
                {
                    Console.WriteLine("Caching Delegate {0}", mi.Name);
                    _tracer.Write(string.Format("Initializing Handler Delegate - {0}.{1}", mi.DeclaringType.Name, mi.Name));
                    _handlerMethodInfoCache.Add(new HandlerMethodInfo(mi, methodInfoPrivateValidation, methodInfoPrivateValidationForMethods));
                });
            });
        }

        private void InitJsonSerializer()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (StreamWriter sw = new StreamWriter(ms))
                {
                    sw.Write("{\"jsonrpc\":\"2.0\",\"method\":\"\",\"params\":{\"empty\":0},\"id\":\"\"}");
                    sw.Flush();
                    ms.Position = 0;

                    using (StreamReader sr = new StreamReader(ms))
                    {
                        using (JsonReader r = new JsonTextReader(sr))
                        {
                            _jsonSerializer.Deserialize<JsonRequest>(r);
                        }
                    }
                }
            }

            JsonConvert.SerializeObject(new JsonResponse { JsonRpc = "2.0", Id = string.Empty, Result = null });
        }

        private MethodInfo GetMethodInfo(Type handlerType, string methodName)
        {
            string[] methodPieces = methodName.Split('.');
            MethodInfo info = handlerType.GetMethod(methodPieces.Last(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase, null, _arrArgumentTypes, null);
            MethodInfo jsonRequestInfo = handlerType.GetMethod(methodPieces.Last(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase, null, _arrJsonRequestArgumentTypes, null);

            if (jsonRequestInfo != null)
                info = jsonRequestInfo;

            if (info == null)
            {
                throw new Exception(string.Concat("Handler not valid for method: ", methodName));
            }
            return info;
        }
        private MethodInfo GetValidateMethodInfo(Type handlerType)
        {
            MethodInfo info = handlerType.GetMethod("Validate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase, null, new Type[3] { typeof(string), typeof(Dictionary<string, object>), typeof(string).MakeByRefType() }, null);
            if (info == null)
            {
                throw new Exception("Handler not valid for method: Validate");
            }
            return info;
        }

        //Message Handling
        public string HandleRequestMessage(JsonRequest request)
        {
            string result = null;
            try
            {
                //handle any extended request stuff
                if (!string.IsNullOrEmpty(request.LogLevel))
                {
                    var logLevel = GetLogLevel(request.LogLevel);
                    if (logLevel.HasValue)
                        SetLogLevel(logLevel.Value);
                }

                //get trace if applicable
                var traceActor = GetTraceActor(request.TraceId);

                //get method and service information
                var serviceMethodSplit = request.Method.Split('.');
                var serviceName = serviceMethodSplit[0];
                var methodName = serviceMethodSplit[1];

                //check for special flags
                var isReportMetadata = request.IsReportMetadata != null && (bool)request.IsReportMetadata;

                //only convert parameters/validate if we're going to execute a method (if getting metadata skip it)
                if (!isReportMetadata)
                {
                    //convert request parameters so they may be validated later
                    ConvertRequestParameters(ref request);

                    //validate
                    if (!ValidateService(request, serviceName, traceActor)) return result;
                    if (!ValidateMethods(request, serviceName, traceActor)) return result;
                }

                try
                {
                    SetLogContext(serviceName, methodName);

                    if (_handlerTypesMethods.ContainsKey(request.Method))
                    {
                        //***********************************************
                        //new WorkHandler style WorkHandlers
                        //***********************************

                        //set "global" values for logger and tracer
                        var assemblyName = _handlerTypesMethods[request.Method].Value.GetAssemblyMetadata().AssemblyName;
                        _logger.OtherData["WorkHandlerVersion"] = assemblyName;
                        _tracer.OtherData["WorkHandlerVersion"] = assemblyName;

                        string response = isReportMetadata ? BuildMetaDataResponse(_handlerTypesMethods[request.Method].Value, request.Id) :
                                                             ProcessMessage(request);

                        result = ProcessResponse(request, response, traceActor);
                    }
                    else
                    {
                        //***********************************************
                        //original WorkHandlerBase style WorkHandlers
                        //***********************************
                        IWorkHandlerMapping mapping = GetWorkHandlerMapping(request);

                        //set "global" values for logger and tracer
                        var assemblyName = mapping.HandlerOriginal.GetAssemblyMetadata().AssemblyName;
                        _logger.OtherData["WorkHandlerVersion"] = assemblyName;
                        _tracer.OtherData["WorkHandlerVersion"] = assemblyName;

                        string response = isReportMetadata ? BuildMetaDataResponse(mapping.HandlerOriginal, request.Id) :
                                                             ProcessMessage(request, mapping);

                        result = ProcessResponse(request, response, traceActor);
                    }
                }
                catch (Exception ex)
                {
                    //respond to error in handling of the request or reply
                    HandleProcessingException(request, ex, traceActor);
                }

            }//end try
            catch (Exception ex)
            {
                SetLogContext(primary: "GenericWorkerProcess", secondary: "HandleRequestMessage");
                _logger.Error(ex.Message, ex);
                Console.WriteLine(ex.Message);
            }
            finally
            {
                //clear all "global" logger and tracer data
                SetLogContext(null, null);
                _logger.OtherData.Remove("WorkHandlerVersion");
                _tracer.OtherData.Remove("WorkHandlerVersion");

                if (_tracer.OtherData.ContainsKey("TraceId"))
                    _tracer.OtherData.Remove("TraceId");
                if (_logger.OtherData.ContainsKey("TraceId"))
                    _logger.OtherData.Remove("TraceId");

                //if the request changed the log level, change it back
                if (request != null && (!string.IsNullOrEmpty(request.LogLevel) || !string.IsNullOrWhiteSpace(request.TraceId)))
                    SetLogLevel(_genericWorkerSettings.LogLevel);
            }
            return result;
        }
        private void HandleSBAsyncResponseMessage(JsonResponse response)
        {
            try
            {
                //_genericWorkerInstrumentation.CurrentState = "Working";

                //Create a new Request that wraps the Response message pulled from the Queue.
                var request = new JsonRequest()
                {
                    Id = Guid.NewGuid(),
                    JsonRpc = "2.0",
                    Method = "NotImplemented",
                    Params = new Dictionary<string, object>()
                    {
                        {"response", JObject.FromObject(response)} //Conver to JObject so validation works properly when calling SBASYNC_PROCESS_RESPONSE_METHOD
                    }
                };

                var serviceName = request.Method.Split('.')[0];

                //validation
                if (!ValidateService(request, serviceName, null)) return;
                if (!ValidateMethods(request, serviceName, null)) return;

                //get mapping for current request
                IWorkHandlerMapping mapping = GetWorkHandlerMapping(request);

                try
                {
                    //process the new request (which is really an async response)
                    var requestMessage = _jsonRpcHelper.SerializeJSonRPCRequest(request);
                    var processResponse = ProcessMessageString(requestMessage, mapping);
                }
                catch (Exception ex)
                {
                    //handle async response processing errors lightly
                    var loggerExceptionDict = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, object> kvp in _jsonRpcHelper.GetExceptionDictionary(ex))
                    {
                        loggerExceptionDict.Add(kvp.Key, kvp.Value.ToString());
                    }

                    SetLogContext(primary: "GenericWorkerProcess", secondary: "HandleSBAsyncResponseMessage");
                    _logger.Error(ex.Message, ex, loggerExceptionDict);

                    var errorString = new StringBuilder();
                    foreach (var item in loggerExceptionDict)
                    {
                        errorString.AppendLine(string.Format("{0} --- {1}", item.Key, item.Value));
                    }

                    _communicationManager.SendNotification(errorString.ToString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                SetLogContext(primary: "GenericWorkerProcess", secondary: "HandleSBAsyncResponseMessage");
                _logger.Error(ex.Message, ex);
            }
            finally
            {
                //_genericWorkerInstrumentation.CurrentState = "Idle";
            }
        }

        //Message Processing
        public string ProcessMessage(JsonRequest request)
        {
            string returnValue = null;

            //start timing
            var stopwatch = _processMessageStopwatch ?? new Stopwatch();
            stopwatch.Restart();

            //make sure we can read the parameters
            var passedInParams = (Dictionary<string, object>)request.Params;

            //get the cached delegate info
            var handlerMethodInfo = _handlerMethodInfoCache.Where(i =>
                    string.Format("{0}.{1}", i.MethodInfo.DeclaringType.Name, i.MethodInfo.Name).Equals(request.Method, StringComparison.OrdinalIgnoreCase) &&
                    i.ParameterInfo.All(pi => pi.HasDefaultValue || passedInParams.Keys.Contains(pi.Name)))
                .OrderByDescending(x => x.ParameterInfo.Count())
                .FirstOrDefault();

            if (handlerMethodInfo == null)
            {
                //no method found for passed params
                var errorMessage = string.Format("No method found for Method: {0} - Parameter(s): {1}", request.Method, string.Join(", ", passedInParams.Keys));
                var _retRes = _jsonRpcHelper.GetJsonRPCErrorResponse(request, errorMessage, null);
                returnValue = _jsonRpcHelper.SerializeJSonRPCResponse(_retRes);
            }
            else
            {
                //get the service instance
                var serviceInstance = _handlerTypesMethods[request.Method].Value;

                //validate parameters
                var paramValidationResult = handlerMethodInfo.ValidateParameters(serviceInstance, passedInParams);

                //validate parameters - method
                var methodParamValidationResult = paramValidationResult.IsValid ? handlerMethodInfo.ValidateMethod(serviceInstance, paramValidationResult.Parameters) : null;

                if (!paramValidationResult.IsValid)
                {
                    //bad parameters
                    var parameterValidationMessages = string.Join(". ", paramValidationResult.ValidationMessages);
                    var _retRes = _jsonRpcHelper.GetJsonRPCErrorResponse(request, parameterValidationMessages, null);
                    returnValue = _jsonRpcHelper.SerializeJSonRPCResponse(_retRes);
                }
                else if (!methodParamValidationResult.IsValid)
                {
                    //bad parameters (for method)
                    var _retRes = _jsonRpcHelper.GetJsonRPCErrorResponse(request, methodParamValidationResult.ValidationMessage, null);
                    returnValue = _jsonRpcHelper.SerializeJSonRPCResponse(_retRes);
                }
                else
                {
                    try
                    {
                        //Statistics
                        UpdateStatistics(request.Method);

                        //process the message
                        var result = handlerMethodInfo.Delegate(serviceInstance, paramValidationResult.Parameters);

                        returnValue = _jsonRpcHelper.SerializeJSonRPCResponse(request.Id, result);
                    }
                    catch (RPCException rex)
                    {
                        returnValue = HandleRPCExceptionAndGetResponse(request.Id.ToString(), rex);
                    }
                }//end else
            }//end else

            //end timing
            Task.Factory.StartNew(() =>
            {
                //NStatsD
                var nstatsmethodkey = string.Format("ServiceBus.GenericWorker.{0}.ElapsedMilliseconds", request.Method.ToUpper());
                _statsDClient.Timing(nstatsmethodkey, stopwatch.ElapsedMilliseconds);
            });

            return returnValue;
        }
        private string ProcessMessage(JsonRequest request, IWorkHandlerMapping mapping)
        {
            string returnValue = null;
            var stopwatch = _processMessageStopwatch ?? new Stopwatch();
            stopwatch.Restart();

            if (mapping.MethodInfo.GetParameters()[0].ParameterType == typeof(JsonRequest))
            {
                var jsonResponse = ProcessMessageObject(request, mapping);
                returnValue = _jsonRpcHelper.SerializeJSonRPCResponse(jsonResponse);
            }

            stopwatch.Stop();

            Task.Factory.StartNew(() =>
            {
                //NStatsD
                var nstatsmethodkey = string.Format("ServiceBus.GenericWorker.{0}.ElapsedMilliseconds", mapping.MethodName.ToUpper());
                _statsDClient.Timing(nstatsmethodkey, stopwatch.ElapsedMilliseconds);
            });

            return returnValue;
        }
        private string ProcessMessageString(string message, IWorkHandlerMapping mapping)
        {
            //Validate
            var parameters = mapping.Parameters as Dictionary<string, object>;
            var arguments = new object[] { mapping.MethodName, parameters, string.Empty };  //arguments[2] is an out string that will be populated with error messages 
            if (!(bool)mapping.ValidationMethodInfo.Invoke(mapping.HandlerOriginal, arguments))
            {
                return _jsonRpcHelper.GetJsonRPCErrorResponse(message, "Invalid parameters", arguments[2], -32602);
            }

            //Statistics
            UpdateStatistics(mapping.MethodName);

            //Execute
            object response = mapping.MethodInfo.Invoke(mapping.HandlerOriginal, new object[] { message });
            return response as string;
        }
        private JsonResponse ProcessMessageObject(JsonRequest message, IWorkHandlerMapping mapping)
        {
            //Validate
            var parameters = message.Params as Dictionary<string, object>;
            var arguments = new object[] { mapping.MethodName, parameters, string.Empty };  //arguments[2] is an out string that will be populated with error messages 
            if (!(bool)mapping.ValidationMethodInfo.Invoke(mapping.HandlerOriginal, arguments))
            {
                return _jsonRpcHelper.GetJsonRPCErrorResponse(message, "Invalid parameters", arguments[2], -32602);
            }

            //Statistics
            UpdateStatistics(mapping.MethodName);

            //Execute
            object response = mapping.MethodInfo.Invoke(mapping.HandlerOriginal, new object[] { message });
            return response as JsonResponse;
        }
        private string ProcessResponse(JsonRequest request, string response, TraceActor traceActor)
        {
            SetLogContext(primary: "GenericWorkerProcess", secondary: "ProcessResponse");

            if (!string.IsNullOrEmpty(request.AsyncMessageId)) //Async Response
            {
                //Deserialize response and set AsyncMessageId
                var jsonResponse = _jsonRpcHelper.DeSerializeJSonRPCResponse(response);
                jsonResponse.AsyncMessageId = request.AsyncMessageId;

                //set trace if applicable
                SetTraceActor(request.TraceId, traceActor, ref jsonResponse);

                //Serialize response
                response = _jsonRpcHelper.SerializeJSonRPCResponse(jsonResponse);

            }
            else if (string.IsNullOrEmpty(response))
            {
                //set trace if applicable
                SetTraceActor(request.TraceId, traceActor, ref response);
                throw new Exception("A non-null response is expected for this queue.");
            }
            return response;
        }
        private void HandleProcessingException(JsonRequest request, Exception ex, TraceActor traceActor)
        {
            //Any unhandled exception inside handlers will be of type TargetInvocationException(not very helpful).  
            //Let's cut to the chase and get the inner exception if available
            var exception = ex is TargetInvocationException ? ex.InnerException ?? ex : ex;

            //If some unknown exception happens, send a RPCError into the response queue (provided a response queue exists)
            if (!string.IsNullOrEmpty(request.AsyncMessageId)) //Async Response
            {
                //var response = _jsonRpcHelper.GetJsonRPCErrorResponse(message.Message, exception.Message, _jsonRpcHelper.GetExceptionDictionary(exception));
                var jsonResponse = _jsonRpcHelper.GetJsonRPCErrorResponse(request, exception.Message, _jsonRpcHelper.GetExceptionDictionary(exception));

                //Deserialize response and set AsyncMessageId
                //var jsonResponse = _jsonRpcHelper.DeSerializeJSonRPCResponse(response);
                jsonResponse.AsyncMessageId = request.AsyncMessageId;

                //set trace if applicable
                SetTraceActor(request.TraceId, traceActor, ref jsonResponse);

                //Serialize response
                var responseWithAsyncMessageId = _jsonRpcHelper.SerializeJSonRPCResponse(jsonResponse);

            }

            var loggerExceptionDict = new Dictionary<string, string>();
            foreach (KeyValuePair<string, object> kvp in _jsonRpcHelper.GetExceptionDictionary(exception))
            {
                loggerExceptionDict.Add(kvp.Key, kvp.Value.ToString());
            }

            _logger.Error(exception.Message, exception, loggerExceptionDict);

            var errorString = new StringBuilder();
            foreach (var item in loggerExceptionDict)
            {
                errorString.AppendLine(string.Format("{0} --- {1}", item.Key, item.Value));
            }

            _communicationManager.SendNotification(errorString.ToString());

            //NStatsD
            var methodName = request.Method.Split('.')[1];
            var nstatsmethodkey = string.Format("ServiceBus.GenericWorker.{0}.FailureCount", methodName.ToUpper());
            _statsDClient.Increment(nstatsmethodkey);
        }

        //RPCException & RPCError
        private string GetJsonRPCErrorResponse(string requestId, string errorMessage, object errorData, int errorCode = -32100, Dictionary<string, object> otherProperties = null)
        {
            var response = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "JsonRpc", "2.0"},
                { "Id", requestId},
                { "Error", new JsonRpcException(errorCode, errorMessage, errorData)}
            };

            if (otherProperties != null)
            {
                foreach (var de in otherProperties)
                {
                    if (!response.ContainsKey(de.Key))
                        response.Add(de.Key, de.Value);
                }
            }

            return JsonConvert.SerializeObject(response);
        }
        private string HandleRPCExceptionAndGetResponse(string requestId, RPCException rex)
        {
            string returnValue;
            //WorkHandler author has thrown a "known" RPCException

            if (rex is FatalException)
            {
                //stop executing and shut down when finished processing this response
                _continueExecution = false;

                //get the response
                returnValue = GetJsonRPCErrorResponse(requestId, rex.Message, rex.RPCData, rex.Code, new Dictionary<string, object> { { "Fatal", true } });

                //log it ERROR
                var message = string.Format("FatalException: {0}", rex.Message);
                _logger.Error(message, rex, new Dictionary<string, string> { { "Fatal", "true" } });
            }
            else if (rex is RetryableException)
            {
                returnValue = GetJsonRPCErrorResponse(requestId, rex.Message, rex.RPCData, rex.Code, new Dictionary<string, object> { { "Retry", true } });

                //log it WARN
                var message = string.Format("RetryableException: {0}", rex.Message);
                _logger.Warn(message, rex, new Dictionary<string, string> { { "Retry", "true" } });
            }
            else if (rex is NonRetryableException)
            {
                returnValue = GetJsonRPCErrorResponse(requestId, rex.Message, rex.RPCData, rex.Code, new Dictionary<string, object> { { "Retry", false } });

                //log it WARN
                var message = string.Format("NonRetryableException: {0}", rex.Message);
                _logger.Warn(message, rex, new Dictionary<string, string> { { "Retry", "false" } });
            }
            else
            {
                returnValue = GetJsonRPCErrorResponse(requestId, rex.Message, rex.RPCData, rex.Code);

                //log it WARN
                var message = string.Format("RPCException: {0}", rex.Message);
                _logger.Warn(message, rex);
            }

            return returnValue;
        }


        //Special Message Handling
        private string BuildMetaDataResponse(IWorkHandlerBase handler, object requestId)
        {
            var metadata = handler.GetAssemblyMetadata();
            return _jsonRpcHelper.SerializeJSonRPCResponse(requestId, metadata);
        }
        private string BuildMetaDataResponse(WorkHandler handler, object requestId)
        {
            var metadata = handler.GetAssemblyMetadata();

            return _jsonRpcHelper.SerializeJSonRPCResponse(requestId, new Dictionary<string, object>{
                { "AssemblyName" , metadata.AssemblyName },
                { "AssemblyVersion" , metadata.AssemblyVersion }
            });
        }
        private string BuildServiceBusMethodsResponse(string serviceName, object requestId)
        {
            //special request to identify all known methods (and signatures) for a specified service

            //iterate all methods for the specified serviceName
            var serviceBusMethods = new List<object>();
            _handlerMethodInfoCache.Where(i => i.MethodInfo.DeclaringType.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase)).ToList().ForEach(mhi =>
            {
                //get parameters
                var parameters = mhi.ParametersJsonSchema.Count > 0 ? new List<object>() : null;
                mhi.ParametersJsonSchema.ToList().ForEach(p =>
                {
                    parameters.Add(new
                    {
                        Name = p.Key,
                        Type = p.Value.ToString()
                    });
                });

                //add what we know to the list
                serviceBusMethods.Add(new
                {
                    Service = serviceName,
                    Method = mhi.MethodInfo.Name,
                    Signature = new
                    {
                        Parameters = parameters,
                        Return = mhi.ReturnJsonSchema.ToString()
                    }
                });
            });

            //***************
            _handlerMappings.Where(hm => hm.Value.MethodInfo.DeclaringType.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase)).ToList().ForEach(mhi =>
            {
                //add what we know to the list
                serviceBusMethods.Add(new
                {
                    Service = serviceName,
                    Method = mhi.Value.MethodInfo.Name,
                    Signature = new
                    {
                        Parameters = "N/A",
                        Return = "N/A"
                    }
                });
            });


            //get handler metadata (use the first method for this service)
            IWorkerMetadata handlerAssemblyMetadata = null;
            var handlerTypeMethodValue = _handlerTypesMethods.FirstOrDefault(htm => htm.Key.StartsWith(string.Format("{0}.", serviceName), StringComparison.OrdinalIgnoreCase)).Value;
            var handlerTypeMethodValueOrig = _handlerMappings.FirstOrDefault(htm => htm.Key.StartsWith(string.Format("{0}.", serviceName), StringComparison.OrdinalIgnoreCase)).Value;

            if (handlerTypeMethodValue != null)
                handlerAssemblyMetadata = handlerTypeMethodValue.Value.GetAssemblyMetadata();
            else
                handlerAssemblyMetadata = handlerTypeMethodValueOrig.HandlerOriginal.GetAssemblyMetadata();



            var fullResponse = new
            {
                WorkHandler = new
                {
                    AssemblyName = handlerAssemblyMetadata.AssemblyName,
                    AssemblyVersion = handlerAssemblyMetadata.AssemblyVersion
                },
                Methods = serviceBusMethods
            };

            //setup the response with the list of methods
            var jsonResponse = new JsonResponse()
            {
                Id = requestId,
                Result = fullResponse
            };

            //serialize
            return _jsonRpcHelper.SerializeJSonRPCResponse(jsonResponse);
        }

        //Statistics and Shutdown
        private void CheckAutoShutdown()
        {
            if (_autoShutdownMinutes != null && _totalStopwatch.Elapsed.TotalMinutes >= _autoShutdownMinutes)
            {
                SetLogContext(primary: "GenericWorkerProcess", secondary: "CheckAutoShutdown");
                _logger.Info(string.Format("GenericWorker {0} - Auto Shutdown at {1} Minutes", _settings.RequestQueue, _autoShutdownMinutes));
                System.Threading.Thread.Sleep(2000); //Logging happens async, sleep to make sure it completes before returning and shutting the app down
                _continueExecution = false;
            }

            if (_autoShutdownProcessedMessageCount != null && _processedMessageCount >= _autoShutdownProcessedMessageCount)
            {
                SetLogContext(primary: "GenericWorkerProcess", secondary: "CheckAutoShutdown");
                _logger.Info(string.Format("GenericWorker {0} - Auto Shutdown at {1} Messages Processed", _settings.RequestQueue, _processedMessageCount));
                System.Threading.Thread.Sleep(2000); //Logging happens async, sleep to make sure it completes before returning and shutting the app down
                _continueExecution = false;
            }

            if (_autoShutdownMemoryThresholdBytes.HasValue)
            {
                var workingSet64Bytes = Process.GetCurrentProcess().WorkingSet64;
                //Console.WriteLine(workingSet64Bytes);
                if (workingSet64Bytes >= _autoShutdownMemoryThresholdBytes)
                {
                    _logger.Error(string.Format("GenericWorker {0} - Auto Shutdown at memory threshold: {1} bytes", _settings.RequestQueue, workingSet64Bytes));
                    System.Threading.Thread.Sleep(2000); //Logging happens async, sleep to make sure it completes before returning and shutting the app down
                    _continueExecution = false;
                }
            }
        }
        private void SetupAutoShutdown()
        {
            _totalStopwatch = new Stopwatch();

            if (_genericWorkerSettings.AutoShutdownMinutesMin != null && _genericWorkerSettings.AutoShutdownMinutesMax != null && _genericWorkerSettings.AutoShutdownMinutesMin < _genericWorkerSettings.AutoShutdownMinutesMax)
            {
                _autoShutdownMinutes = new Random().Next((int)_genericWorkerSettings.AutoShutdownMinutesMin, (int)_genericWorkerSettings.AutoShutdownMinutesMax);
            }

            if (_genericWorkerSettings.AutoShutdownProcessedMessageCountMin != null && _genericWorkerSettings.AutoShutdownProcessedMessageCountMax != null && _genericWorkerSettings.AutoShutdownProcessedMessageCountMin < _genericWorkerSettings.AutoShutdownProcessedMessageCountMax)
            {
                _autoShutdownProcessedMessageCount = new Random().Next((int)_genericWorkerSettings.AutoShutdownProcessedMessageCountMin, (int)_genericWorkerSettings.AutoShutdownProcessedMessageCountMax);
            }

            if (_genericWorkerSettings.AutoShutdownMemoryThresholdBytes.HasValue)
            {
                _autoShutdownMemoryThresholdBytes = _genericWorkerSettings.AutoShutdownMemoryThresholdBytes;
            }
        }
        private void UpdateStatistics(string methodName)
        {
            //Internal counter used for auto shutdown (total count since begining of worker life)
            _processedMessageCount++;

            //Interval counter used for instrumentation (interval count since the last instrumentation interval)
            //_genericWorkerInstrumentation.IntervalProcessedMessagesCount++;

            //Service Method Specific Stats (Interval counts)
            //if (!_genericWorkerInstrumentation.IntervalProcessedMessagesCountByMethod.ContainsKey(methodName))
            //{
            //    _genericWorkerInstrumentation.IntervalProcessedMessagesCountByMethod.Add(methodName, 1);
            //}
            //else
            //{
            //    _genericWorkerInstrumentation.IntervalProcessedMessagesCountByMethod[methodName]++;
            //}

            Task.Factory.StartNew(() =>
            {
                //NStatsD
                var nstatsmethodkey = string.Format("ServiceBus.GenericWorker.{0}.RequestCount", methodName.ToUpper());
                _statsDClient.Increment(nstatsmethodkey);
            });
        }

        //Log Setup
        private void SetLogLevel(int logLevel)
        {
            _logger.LogLevel = (LogEntrySeverity)logLevel;
            _tracer.LogLevel = (LogEntrySeverity)logLevel;
        }
        private void SetLogContext(string primary, string secondary)
        {
            _logger.ContextPrimary = primary;
            _logger.ContextSecondary = secondary;
            _tracer.ContextPrimary = primary;
            _tracer.ContextSecondary = secondary;
        }

        //tracing actor
        private TraceActor GetTraceActor(string traceId)
        {
            TraceActor traceActor = null;
            var isTrace = !string.IsNullOrWhiteSpace(traceId);
            if (isTrace)
            {
                traceActor = new TraceActor
                {
                    Role = TRACE_ACTOR_ROLE,
                    Id = _idGuid.ToString(),
                    Host = Environment.MachineName,
                    TimeEnter = DateTime.UtcNow
                };


                SetLogLevel((int)LogEntrySeverity.Trace);

                _tracer.OtherData["TraceId"] = traceId;
                _logger.OtherData["TraceId"] = traceId;

                _tracer.Write(string.Format("{0} Trace Enter - TraceId: {1} - InstanceId: {2}", TRACE_ACTOR_ROLE, traceId, _idGuid));
            }

            return traceActor;
        }
        private void SetTraceActor(string traceId, TraceActor traceActor, ref JsonResponse response)
        {
            if (string.IsNullOrWhiteSpace(traceId) || traceActor == null)
                return;

            if (response.TraceData == null)
                response.TraceData = new TraceData();

            traceActor.TimeExit = DateTime.UtcNow;

            response.TraceId = traceId;
            response.TraceData.Actors.Add(traceActor);

            _tracer.Write(string.Format("{0} Trace Exit - TraceId: {1} - InstanceId: {2}", TRACE_ACTOR_ROLE, traceId, _idGuid));
        }
        private void SetTraceActor(string traceId, TraceActor traceActor, ref string response)
        {
            if (string.IsNullOrWhiteSpace(traceId))
                return;

            var jsonResponse = _jsonRpcHelper.DeSerializeJSonRPCResponse(response);

            SetTraceActor(traceId, traceActor, ref jsonResponse);

            response = _jsonRpcHelper.SerializeJSonRPCResponse(jsonResponse);
        }

        //commands
        private void ProcessCommands()
        {
            lock (_commandLock)
            {
                if (_currentCommandItems != null)
                {
                    //*****************8
                    //TERM
                    _currentCommandItems.Where(x => x.Command.Equals("TERM", StringComparison.OrdinalIgnoreCase) && x.Arguments.Count >= 1).ToList().ForEach(x => {
                        try
                        {
                            var minutes = Convert.ToInt32(x.Arguments[0]);

                            //if minutes is zero don't bother with random autoShutdown, die now
                            if (minutes == 0)
                            {
                                _continueExecution = false;
                                Console.WriteLine("Terminating (Graceful) ...");
                            }
                            else
                            {
                                var randomMinutes = new Random().Next(0, minutes + 1);
                                _autoShutdownMinutes = (int)_totalStopwatch.Elapsed.TotalMinutes + randomMinutes;
                                Console.WriteLine("Terminating (Graceful in {0}) ...", randomMinutes);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(string.Format("Exception Processing Command - Command: {0} - Arguments: {1} - Exception: {2}", x.Command, string.Join(" ", x.Arguments.ToArray()), ex.Message), ex);
                        }
                    });

                    //we should have processed all commands, clear
                    _currentCommandItems.Clear();
                }
            }
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            //_genericWorkerInstrumentation.Detach(_genericWorkerInstrumentationWriter);
        }
        #endregion
    }
}
