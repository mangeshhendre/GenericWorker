//using Microsoft.Practices.Unity;
//using Moq;
//using Newtonsoft.Json;
//using Safeguard.Library.Universal.Json;
//using Safeguard.Library.Universal.Json.Interfaces;
//using Safeguard.Library.Universal.Json.Support;
//using Safeguard.Library.Universal.Logging.Interfaces;
//using Safeguard.Library.Universal.StatsD;
//using Safeguard.Library.Universal.StatsD.NStatsD;
//using Safeguard.Library.Universal.WorkHandling;
//using Safeguard.Library.Universal.WorkHandling.Interfaces;
//using Common.Interfaces;
//using GenericWorker;
//using GenericWorker.Instrumentation;
//using GenericWorker.Instrumentation.GenericWorkerInstrumentationWriter;
//using GenericWorker.Interfaces;
//using GenericWorker.SupportClasses;
//using MQSeries.Interfaces;
//using MQSeries.Messages;
//using MQSeries.Settings;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics.CodeAnalysis;
//using System.IO;
//using System.Reflection;
//using System.Security;
//using Xunit;

//namespace GenericWorkerUnitTests
//{
//    public interface IDummyWorkHandler : IWorkHandlerBase
//    {
//        string DownloadTest(string jsonRpcString);
//    }

//    [ExcludeFromCodeCoverage]
//    public class GenericWorkerTests
//    {
//        #region Private variables
//        private const string TEST_REQUEST_QUEUE_NAME = "Image.DownloadTest.REQ";
//        private const string TEST_RESPONSE_QUEUE_NAME = "Image.DownloadTest.REP";
//        private const string TEST_METHOD_NAME = "Image.DownloadTest";
//        private const string TEST_SERVICE_NAME = "Image";
//        private const string TEST_MESSAGE = "Success";
//        private const string TEST_CHANNEL = "DEFAULT";
//        #endregion

//        [Fact]
//        public void GenericWorker_LoadAndRunSinglePollLoop()
//        {
//            var jsonRpcHelper = new JSonRPCHelper();
//            var jsonRequest = CreateJsonRequest();

//            //I officially give up.  The following line causes "Operation could destablize the runtime" error ONLY on the build server.
//            //After hours of trying to figure out how this could be happening in the build environment only... I call uncle.  We'll just set a damn string here.  
//            //string jsonRequestString = JsonConvert.SerializeObject(jsonRequest); //jsonRpcHelper.SerializeJSonRPCRequest(jsonRequest);
//            string jsonRequestString = "{\"jsonrpc\":null,\"method\":\"Image.DownloadTest\",\"params\":{\"imageData\":\"TEST\"},\"id\":\"422c5dd9-c7e6-4e78-bb6e-1dbb942453e3\"}";

//            string expectedResultString = "This is the result string";
//            bool messageAvailable = true;
//            var genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

//            var workHandlerMethodDataMock = new Mock<IWorkHandlerMethodData>();
//            var workHandlerBaseMock = new Mock<IDummyWorkHandler>();
//            var assemblyResolverMock = new Mock<IAssemblyResolver>();
//            var assemblyLocatorMock = new Mock<IAssemblyLocator>();
//            var genericWorkerConfigReaderMock = new Mock<IGenericWorkerConfigReader>();
//            var jsonRPCHelperMock = new Mock<IJSonRPCHelper>();
//            var queueSettingsResolverMock = new Mock<IQueueSettingsResolver>();
//            var queueMessageMock = new Mock<IQueueMessage>();
//            var mqQueueControllerMock = new Mock<IMQQueueController>();
//            var communicationManagerMock = new Mock<ICommunicationManager>();
//            var genericWorkerInstrumentationMock = new Mock<IGenericWorkerInstrumentation>();
//            var genericWorkerInstrumentationConfigReaderMock = new Mock<IGenericWorkerInstrumentationConfigReader>();
//            var genericWorkerInstrumentationWriterMock = new Mock<IGenericWorkerInstrumentationWriter>();
//            var loggerMock = new Mock<ILogger>();
//            var workHandlerMapping = new WorkHandlerMapping();
//            var nStatsDConfigurationSectionReaderMock = new Mock<INStatsDConfigurationSectionReader>();
//            var statsDClientMock = new Mock<IStatsDClient>();

//            UnityContainer container = new UnityContainer();
//            container.RegisterInstance<IAssemblyResolver>(assemblyResolverMock.Object);
//            container.RegisterInstance<IAssemblyLocator>(assemblyLocatorMock.Object);
//            container.RegisterInstance<IGenericWorkerConfigReader>(genericWorkerConfigReaderMock.Object);
//            container.RegisterInstance<IJSonRPCHelper>(jsonRPCHelperMock.Object);
//            container.RegisterInstance<IQueueSettingsResolver>(queueSettingsResolverMock.Object);
//            container.RegisterInstance<IMQQueueController>(mqQueueControllerMock.Object);
//            container.RegisterInstance<ICommunicationManager>(communicationManagerMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentation>(genericWorkerInstrumentationMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentationConfigReader>(genericWorkerInstrumentationConfigReaderMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentationWriter>(genericWorkerInstrumentationWriterMock.Object);
//            container.RegisterInstance<ILogger>(loggerMock.Object);
//            container.RegisterInstance<IWorkHandlerMapping>(workHandlerMapping);
//            container.RegisterInstance<INStatsDConfigurationSectionReader>(nStatsDConfigurationSectionReaderMock.Object);
//            container.RegisterInstance<IStatsDClient>(statsDClientMock.Object);

//            QueueSettings settings = new QueueSettings();
//            settings.RequestQueue = "TEST";
//            settings.ResponseQueue = "TEST";

//            Lazy<IWorkHandlerBase, IWorkHandlerMethodData> lazy = new Lazy<IWorkHandlerBase, IWorkHandlerMethodData>(() => { return workHandlerBaseMock.Object; }, workHandlerMethodDataMock.Object);
//            Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>> dctMethods = new Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>>();
//            dctMethods.Add(TEST_METHOD_NAME, lazy);

//            MemoryStream jsonRequestMessageStream = new MemoryStream();
//            StreamWriter sr = new StreamWriter(jsonRequestMessageStream);
//            sr.Write(jsonRequestString);
//            sr.Flush();
//            jsonRequestMessageStream.Position = 0;

//            //Setup
//            string error;
//            workHandlerMethodDataMock.Setup(data => data.SupportedMethods).Returns(TEST_METHOD_NAME);
//            assemblyResolverMock.Setup(rsr => rsr.BuildMethodDictionary(It.IsAny<String>(), It.IsAny<bool>())).Returns(dctMethods);
//            assemblyLocatorMock.SetupGet(al => al.WorkHandlerServiceDirectories).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { TEST_SERVICE_NAME, "C:\\DummyDir" } });
//            GenericWorkerProcess process = new GenericWorkerProcess(container); // We need to create this here so we can callback in setup methods for ContinueExecution. (Short circuit POLL method)
//            jsonRPCHelperMock.Setup(helper => helper.DeSerializeJSonRPCRequest(It.IsAny<String>())).Returns(jsonRequest);
//            queueSettingsResolverMock.Setup(rsr => rsr.ResolveByQueueName(It.IsAny<String>())).Returns(settings);
//            queueMessageMock.Setup(msg => msg.Message).Returns(jsonRequestMessageStream);
//            queueMessageMock.Setup(msg => msg.MessageId).Returns(Guid.NewGuid());
//            communicationManagerMock.Setup(mgr => mgr.HeartbeatRequired).Returns(true);
//            workHandlerBaseMock.Setup(w => w.Validate(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), out error)).Returns(true);
//            workHandlerBaseMock.Setup(w => w.DownloadTest(It.IsAny<string>())).Returns(expectedResultString);
//            genericWorkerInstrumentationMock.SetupGet(gwi => gwi.IntervalProcessedMessagesCountByMethod).Returns(genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod);
//            mqQueueControllerMock.Setup(clr => clr.Get(It.IsAny<QueueSettings>())).Returns(() =>
//            {
//                if (messageAvailable)
//                {
//                    messageAvailable = false;
//                    return queueMessageMock.Object;
//                }
//                else
//                {
//                    return null;
//                }
//            }).Callback(() =>
//            {
//                process.ContinueExecution = false; //This prevents the GenericWorkerProcess from POLLing forever for messages.
//            });

//            //Act
//            process.Run(settings.RequestQueue);

//            //Verify
//            queueSettingsResolverMock.Verify(qsr => qsr.ResolveByQueueName(It.Is<String>(s => string.Compare(s, "TEST") == 0)), Times.Once());
//            genericWorkerInstrumentationWriterMock.VerifySet(gwiw => gwiw.QueueSettings, Times.Once());

//            genericWorkerInstrumentationMock.Verify(gwi => gwi.Start(It.Is<string>(s => string.Compare(s, "Idle", true) == 0)), Times.Once());
//            genericWorkerInstrumentationMock.VerifySet(gwi => gwi.CurrentState = It.Is<string>(s => string.Compare(s, "Working", true) == 0), Times.Once());
//            genericWorkerInstrumentationMock.VerifySet(gwi => gwi.CurrentState = It.Is<string>(s => string.Compare(s, "Idle", true) == 0), Times.Once());

//            mqQueueControllerMock.Verify(q => q.Get(It.Is<QueueSettings>(qs => qs == settings)), Times.Once());
//            workHandlerBaseMock.Verify(wh => wh.Validate(It.Is<string>(s => string.Compare(s, TEST_METHOD_NAME, true) == 0), It.Is<Dictionary<string, object>>(d => string.Compare((string)d["imageData"], "TEST", true) == 0), out error), Times.Once());
//            workHandlerBaseMock.Verify(wh => wh.DownloadTest(It.Is<string>(s => string.Compare(s, jsonRequestString, true) == 0)), Times.Once());

//            assemblyLocatorMock.VerifyGet(al => al.WorkHandlerServiceDirectories, Times.Exactly(2));

//            Assert.Equal(1, genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod[TEST_METHOD_NAME]);
//        }

//        [Fact]
//        public void GenericWorker_LoadAndRunMultiplePollLoop()
//        {
//            var jsonRpcHelper = new JSonRPCHelper();
//            var jsonRequest = CreateJsonRequest();

//            //I officially give up.  The following line causes "Operation could destablize the runtime" error ONLY on the build server.
//            //After hours of trying to figure out how this could be happening in the build environment only... I call uncle.  We'll just set a damn string here.  
//            //string jsonRequestString = JsonConvert.SerializeObject(jsonRequest); //jsonRpcHelper.SerializeJSonRPCRequest(jsonRequest);
//            string jsonRequestString = "{\"jsonrpc\":null,\"method\":\"Image.DownloadTest\",\"params\":{\"imageData\":\"TEST\"},\"id\":\"422c5dd9-c7e6-4e78-bb6e-1dbb942453e3\"}";

//            string expectedResultString = "This is the result string";
//            int loopCount = 0;
//            bool messageAvailable = true;
//            var genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

//            var workHandlerMethodDataMock = new Mock<IWorkHandlerMethodData>();
//            var workHandlerBaseMock = new Mock<IDummyWorkHandler>();
//            var assemblyResolverMock = new Mock<IAssemblyResolver>();
//            var assemblyLocatorMock = new Mock<IAssemblyLocator>();
//            var genericWorkerConfigReaderMock = new Mock<IGenericWorkerConfigReader>();
//            var jsonRPCHelperMock = new Mock<IJSonRPCHelper>();
//            var queueSettingsResolverMock = new Mock<IQueueSettingsResolver>();
//            var queueMessageMock = new Mock<IQueueMessage>();
//            var mqQueueControllerMock = new Mock<IMQQueueController>();
//            var communicationManagerMock = new Mock<ICommunicationManager>();
//            var genericWorkerInstrumentationMock = new Mock<IGenericWorkerInstrumentation>();
//            var genericWorkerInstrumentationConfigReaderMock = new Mock<IGenericWorkerInstrumentationConfigReader>();
//            var genericWorkerInstrumentationWriterMock = new Mock<IGenericWorkerInstrumentationWriter>();
//            var loggerMock = new Mock<ILogger>();
//            var workHandlerMapping = new WorkHandlerMapping();
//            var nStatsDConfigurationSectionReaderMock = new Mock<INStatsDConfigurationSectionReader>();
//            var statsDClientMock = new Mock<IStatsDClient>();

//            UnityContainer container = new UnityContainer();
//            container.RegisterInstance<IAssemblyResolver>(assemblyResolverMock.Object);
//            container.RegisterInstance<IAssemblyLocator>(assemblyLocatorMock.Object);
//            container.RegisterInstance<IGenericWorkerConfigReader>(genericWorkerConfigReaderMock.Object);
//            container.RegisterInstance<IJSonRPCHelper>(jsonRPCHelperMock.Object);
//            container.RegisterInstance<IQueueSettingsResolver>(queueSettingsResolverMock.Object);
//            container.RegisterInstance<IMQQueueController>(mqQueueControllerMock.Object);
//            container.RegisterInstance<ICommunicationManager>(communicationManagerMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentation>(genericWorkerInstrumentationMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentationConfigReader>(genericWorkerInstrumentationConfigReaderMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentationWriter>(genericWorkerInstrumentationWriterMock.Object);
//            container.RegisterInstance<ILogger>(loggerMock.Object);
//            container.RegisterInstance<IWorkHandlerMapping>(workHandlerMapping);
//            container.RegisterInstance<INStatsDConfigurationSectionReader>(nStatsDConfigurationSectionReaderMock.Object);
//            container.RegisterInstance<IStatsDClient>(statsDClientMock.Object);

//            QueueSettings settings = new QueueSettings();
//            settings.RequestQueue = "TEST";
//            settings.ResponseQueue = "TEST";

//            Lazy<IWorkHandlerBase, IWorkHandlerMethodData> lazy = new Lazy<IWorkHandlerBase, IWorkHandlerMethodData>(() => { return workHandlerBaseMock.Object; }, workHandlerMethodDataMock.Object);
//            Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>> dctMethods = new Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>>();
//            dctMethods.Add(TEST_METHOD_NAME, lazy);

//            MemoryStream jsonRequestMessageStream = new MemoryStream();
//            StreamWriter sr = new StreamWriter(jsonRequestMessageStream);
//            sr.Write(jsonRequestString);
//            sr.Flush();
//            jsonRequestMessageStream.Position = 0;

//            //Setup
//            string error;
//            workHandlerMethodDataMock.Setup(data => data.SupportedMethods).Returns(TEST_METHOD_NAME);
//            assemblyResolverMock.Setup(rsr => rsr.BuildMethodDictionary(It.IsAny<String>(), It.IsAny<bool>())).Returns(dctMethods);
//            assemblyLocatorMock.SetupGet(al => al.WorkHandlerServiceDirectories).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { TEST_SERVICE_NAME, "C:\\DummyDir" } });
//            GenericWorkerProcess process = new GenericWorkerProcess(container); // We need to create this here so we can callback in setup methods for ContinueExecution. (Short circuit POLL method)
//            jsonRPCHelperMock.Setup(helper => helper.DeSerializeJSonRPCRequest(It.IsAny<String>())).Returns(jsonRequest);
//            queueSettingsResolverMock.Setup(rsr => rsr.ResolveByQueueName(It.IsAny<String>())).Returns(settings);
//            queueMessageMock.Setup(msg => msg.Message).Returns(jsonRequestMessageStream);
//            queueMessageMock.Setup(msg => msg.MessageId).Returns(Guid.NewGuid());
//            communicationManagerMock.Setup(mgr => mgr.HeartbeatRequired).Returns(true);
//            workHandlerBaseMock.Setup(w => w.Validate(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), out error)).Returns(true);
//            workHandlerBaseMock.Setup(w => w.DownloadTest(It.IsAny<string>())).Returns(expectedResultString);
//            genericWorkerInstrumentationMock.SetupGet(gwi => gwi.IntervalProcessedMessagesCountByMethod).Returns(genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod);
//            mqQueueControllerMock.Setup(clr => clr.Get(It.IsAny<QueueSettings>())).Returns(() =>
//            {
//                if (messageAvailable)
//                {
//                    if (loopCount > 0)
//                        messageAvailable = false;

//                    loopCount++;

//                    return queueMessageMock.Object;
//                }
//                else
//                {
//                    return null;
//                }
//            }).Callback(() =>
//            {
//                jsonRequestMessageStream = new MemoryStream();
//                sr = new StreamWriter(jsonRequestMessageStream);
//                sr.Write(jsonRequestString);
//                sr.Flush();
//                jsonRequestMessageStream.Position = 0;
//                queueMessageMock.Setup(msg => msg.Message).Returns(jsonRequestMessageStream);

//                if (loopCount > 1)
//                    process.ContinueExecution = false; //This prevents the GenericWorkerProcess from POLLing forever for messages.
//            });

//            //Act
//            process.Run(settings.RequestQueue);

//            //Verify
//            queueSettingsResolverMock.Verify(qsr => qsr.ResolveByQueueName(It.Is<String>(s => string.Compare(s, "TEST") == 0)), Times.Once());
//            genericWorkerInstrumentationWriterMock.VerifySet(gwiw => gwiw.QueueSettings, Times.Once());

//            genericWorkerInstrumentationMock.Verify(gwi => gwi.Start(It.Is<string>(s => string.Compare(s, "Idle", true) == 0)), Times.Once());
//            genericWorkerInstrumentationMock.VerifySet(gwi => gwi.CurrentState = It.Is<string>(s => string.Compare(s, "Working", true) == 0), Times.Exactly(2));
//            genericWorkerInstrumentationMock.VerifySet(gwi => gwi.CurrentState = It.Is<string>(s => string.Compare(s, "Idle", true) == 0), Times.Exactly(2));

//            mqQueueControllerMock.Verify(q => q.Get(It.Is<QueueSettings>(qs => qs == settings)), Times.Exactly(2));
//            workHandlerBaseMock.Verify(wh => wh.Validate(It.Is<string>(s => string.Compare(s, TEST_METHOD_NAME, true) == 0), It.Is<Dictionary<string, object>>(d => string.Compare((string)d["imageData"], "TEST", true) == 0), out error), Times.Exactly(2));
//            workHandlerBaseMock.Verify(wh => wh.DownloadTest(It.Is<string>(s => string.Compare(s, jsonRequestString, true) == 0)), Times.Exactly(2));
//            assemblyLocatorMock.VerifyGet(al => al.WorkHandlerServiceDirectories, Times.Exactly(3));

//            Assert.Equal(2, genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod[TEST_METHOD_NAME]);
//        }

//        [Fact]
//        public void GenericWorker_LoadAndRunAutoShutdownMinutes()
//        {
//            using (Microsoft.QualityTools.Testing.Fakes.ShimsContext.Create())
//            {
//                System.Diagnostics.Fakes.ShimStopwatch.Constructor =
//                (@this) =>
//                {
//                    var shim = new System.Diagnostics.Fakes.ShimStopwatch(@this);
//                    shim.ElapsedGet = () => new TimeSpan(0, 60, 0);
//                };

//                var jsonRpcHelper = new JSonRPCHelper();
//                var jsonRequest = CreateJsonRequest();
                
//                //I officially give up.  The following line causes "Operation could destablize the runtime" error ONLY on the build server.
//                //After hours of trying to figure out how this could be happening in the build environment only... I call uncle.  We'll just set a damn string here.  
//                //string jsonRequestString = JsonConvert.SerializeObject(jsonRequest); //jsonRpcHelper.SerializeJSonRPCRequest(jsonRequest);
//                string jsonRequestString = "{\"jsonrpc\":null,\"method\":\"Image.DownloadTest\",\"params\":{\"imageData\":\"TEST\"},\"id\":\"422c5dd9-c7e6-4e78-bb6e-1dbb942453e3\"}";

//                string expectedResultString = "This is the result string";
//                bool messageAvailable = true;
//                int loopCount = 0;
//                var genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

//                var workHandlerMethodDataMock = new Mock<IWorkHandlerMethodData>();
//                var workHandlerBaseMock = new Mock<IDummyWorkHandler>();
//                var assemblyResolverMock = new Mock<IAssemblyResolver>();
//                var assemblyLocatorMock = new Mock<IAssemblyLocator>();
//                var genericWorkerConfigReaderMock = new Mock<IGenericWorkerConfigReader>();
//                var jsonRPCHelperMock = new Mock<IJSonRPCHelper>();
//                var queueSettingsResolverMock = new Mock<IQueueSettingsResolver>();
//                var queueMessageMock = new Mock<IQueueMessage>();
//                var mqQueueControllerMock = new Mock<IMQQueueController>();
//                var communicationManagerMock = new Mock<ICommunicationManager>();
//                var genericWorkerInstrumentationMock = new Mock<IGenericWorkerInstrumentation>();
//                var genericWorkerInstrumentationConfigReaderMock = new Mock<IGenericWorkerInstrumentationConfigReader>();
//                var genericWorkerInstrumentationWriterMock = new Mock<IGenericWorkerInstrumentationWriter>();
//                var loggerMock = new Mock<ILogger>();
//                var workHandlerMapping = new WorkHandlerMapping();
//                var nStatsDConfigurationSectionReaderMock = new Mock<INStatsDConfigurationSectionReader>();
//                var statsDClientMock = new Mock<IStatsDClient>();

//                UnityContainer container = new UnityContainer();
//                container.RegisterInstance<IAssemblyResolver>(assemblyResolverMock.Object);
//                container.RegisterInstance<IAssemblyLocator>(assemblyLocatorMock.Object);
//                container.RegisterInstance<IGenericWorkerConfigReader>(genericWorkerConfigReaderMock.Object);
//                container.RegisterInstance<IJSonRPCHelper>(jsonRPCHelperMock.Object);
//                container.RegisterInstance<IQueueSettingsResolver>(queueSettingsResolverMock.Object);
//                container.RegisterInstance<IMQQueueController>(mqQueueControllerMock.Object);
//                container.RegisterInstance<ICommunicationManager>(communicationManagerMock.Object);
//                container.RegisterInstance<IGenericWorkerInstrumentation>(genericWorkerInstrumentationMock.Object);
//                container.RegisterInstance<IGenericWorkerInstrumentationConfigReader>(genericWorkerInstrumentationConfigReaderMock.Object);
//                container.RegisterInstance<IGenericWorkerInstrumentationWriter>(genericWorkerInstrumentationWriterMock.Object);
//                container.RegisterInstance<ILogger>(loggerMock.Object);
//                container.RegisterInstance<IWorkHandlerMapping>(workHandlerMapping);
//                container.RegisterInstance<INStatsDConfigurationSectionReader>(nStatsDConfigurationSectionReaderMock.Object);
//                container.RegisterInstance<IStatsDClient>(statsDClientMock.Object);

//                QueueSettings settings = new QueueSettings();
//                settings.RequestQueue = "TEST";
//                settings.ResponseQueue = "TEST";

//                Lazy<IWorkHandlerBase, IWorkHandlerMethodData> lazy = new Lazy<IWorkHandlerBase, IWorkHandlerMethodData>(() => { return workHandlerBaseMock.Object; }, workHandlerMethodDataMock.Object);
//                Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>> dctMethods = new Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>>();
//                dctMethods.Add(TEST_METHOD_NAME, lazy);

//                MemoryStream jsonRequestMessageStream = new MemoryStream();
//                StreamWriter sr = new StreamWriter(jsonRequestMessageStream);
//                sr.Write(jsonRequestString);
//                sr.Flush();
//                jsonRequestMessageStream.Position = 0;

//                //Setup
//                string error;
//                workHandlerMethodDataMock.Setup(data => data.SupportedMethods).Returns(TEST_METHOD_NAME);
//                assemblyResolverMock.Setup(rsr => rsr.BuildMethodDictionary(It.IsAny<String>(), It.IsAny<bool>())).Returns(dctMethods);

//                assemblyLocatorMock.SetupGet(al => al.WorkHandlerServiceDirectories).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { TEST_SERVICE_NAME, "C:\\DummyDir" } });

//                genericWorkerConfigReaderMock.SetupGet(c => c.AutoShutdownMinutesMin).Returns(10);
//                genericWorkerConfigReaderMock.SetupGet(c => c.AutoShutdownMinutesMax).Returns(20);

//                GenericWorkerProcess process = new GenericWorkerProcess(container); // We need to create this here so we can callback in setup methods for ContinueExecution. (Short circuit POLL method)

//                jsonRPCHelperMock.Setup(helper => helper.DeSerializeJSonRPCRequest(It.IsAny<String>())).Returns(jsonRequest);
//                queueSettingsResolverMock.Setup(rsr => rsr.ResolveByQueueName(It.IsAny<String>())).Returns(settings);
//                queueMessageMock.Setup(msg => msg.Message).Returns(jsonRequestMessageStream);
//                queueMessageMock.Setup(msg => msg.MessageId).Returns(Guid.NewGuid());
//                communicationManagerMock.Setup(mgr => mgr.HeartbeatRequired).Returns(true);
//                workHandlerBaseMock.Setup(w => w.Validate(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), out error)).Returns(true);
//                workHandlerBaseMock.Setup(w => w.DownloadTest(It.IsAny<string>())).Returns(expectedResultString);
//                genericWorkerInstrumentationMock.SetupGet(gwi => gwi.IntervalProcessedMessagesCountByMethod).Returns(genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod);
//                mqQueueControllerMock.Setup(clr => clr.Get(It.IsAny<QueueSettings>())).Returns(() =>
//                {
//                    if (messageAvailable)
//                    {
//                        if (loopCount > 0)
//                            messageAvailable = false;

//                        loopCount++;

//                        return queueMessageMock.Object;
//                    }
//                    else
//                    {
//                        return null;
//                    }
//                }).Callback(() =>
//                {
//                    jsonRequestMessageStream = new MemoryStream();
//                    sr = new StreamWriter(jsonRequestMessageStream);
//                    sr.Write(jsonRequestString);
//                    sr.Flush();
//                    jsonRequestMessageStream.Position = 0;
//                    queueMessageMock.Setup(msg => msg.Message).Returns(jsonRequestMessageStream);

//                    if (loopCount > 10)
//                        process.ContinueExecution = false; //This prevents the GenericWorkerProcess from POLLing forever for messages.
//                });

//                //Act
//                process.Run(settings.RequestQueue);

//                //Verify
//                Assert.Equal(1, loopCount); //This should never loop more then once if AutoShutdown worked.  Failsafe at 10 so our unittest doesn't hang.
//            }
//        }

//        [Fact]
//        public void GenericWorker_LoadAndRunAutoShutdownMessagesProcessed()
//        {
//            var jsonRpcHelper = new JSonRPCHelper();
//            var jsonRequest = CreateJsonRequest();

//            //I officially give up.  The following line causes "Operation could destablize the runtime" error ONLY on the build server.
//            //After hours of trying to figure out how this could be happening in the build environment only... I call uncle.  We'll just set a damn string here.  
//            //string jsonRequestString = JsonConvert.SerializeObject(jsonRequest); //jsonRpcHelper.SerializeJSonRPCRequest(jsonRequest);
//            string jsonRequestString = "{\"jsonrpc\":null,\"method\":\"Image.DownloadTest\",\"params\":{\"imageData\":\"TEST\"},\"id\":\"422c5dd9-c7e6-4e78-bb6e-1dbb942453e3\"}";

//            string expectedResultString = "This is the result string";
//            bool messageAvailable = true;
//            int loopCount = 0;
//            var genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

//            var workHandlerMethodDataMock = new Mock<IWorkHandlerMethodData>();
//            var workHandlerBaseMock = new Mock<IDummyWorkHandler>();
//            var assemblyResolverMock = new Mock<IAssemblyResolver>();
//            var assemblyLocatorMock = new Mock<IAssemblyLocator>();
//            var genericWorkerConfigReaderMock = new Mock<IGenericWorkerConfigReader>();
//            var jsonRPCHelperMock = new Mock<IJSonRPCHelper>();
//            var queueSettingsResolverMock = new Mock<IQueueSettingsResolver>();
//            var queueMessageMock = new Mock<IQueueMessage>();
//            var mqQueueControllerMock = new Mock<IMQQueueController>();
//            var communicationManagerMock = new Mock<ICommunicationManager>();
//            var genericWorkerInstrumentationMock = new Mock<IGenericWorkerInstrumentation>();
//            var genericWorkerInstrumentationConfigReaderMock = new Mock<IGenericWorkerInstrumentationConfigReader>();
//            var genericWorkerInstrumentationWriterMock = new Mock<IGenericWorkerInstrumentationWriter>();
//            var loggerMock = new Mock<ILogger>();
//            var workHandlerMapping = new WorkHandlerMapping();
//            var nStatsDConfigurationSectionReaderMock = new Mock<INStatsDConfigurationSectionReader>();
//            var statsDClientMock = new Mock<IStatsDClient>();

//            UnityContainer container = new UnityContainer();
//            container.RegisterInstance<IAssemblyResolver>(assemblyResolverMock.Object);
//            container.RegisterInstance<IAssemblyLocator>(assemblyLocatorMock.Object);
//            container.RegisterInstance<IGenericWorkerConfigReader>(genericWorkerConfigReaderMock.Object);
//            container.RegisterInstance<IJSonRPCHelper>(jsonRPCHelperMock.Object);
//            container.RegisterInstance<IQueueSettingsResolver>(queueSettingsResolverMock.Object);
//            container.RegisterInstance<IMQQueueController>(mqQueueControllerMock.Object);
//            container.RegisterInstance<ICommunicationManager>(communicationManagerMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentation>(genericWorkerInstrumentationMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentationConfigReader>(genericWorkerInstrumentationConfigReaderMock.Object);
//            container.RegisterInstance<IGenericWorkerInstrumentationWriter>(genericWorkerInstrumentationWriterMock.Object);
//            container.RegisterInstance<ILogger>(loggerMock.Object);
//            container.RegisterInstance<IWorkHandlerMapping>(workHandlerMapping);
//            container.RegisterInstance<INStatsDConfigurationSectionReader>(nStatsDConfigurationSectionReaderMock.Object);
//            container.RegisterInstance<IStatsDClient>(statsDClientMock.Object);

//            QueueSettings settings = new QueueSettings();
//            settings.RequestQueue = "TEST";
//            settings.ResponseQueue = "TEST";

//            Lazy<IWorkHandlerBase, IWorkHandlerMethodData> lazy = new Lazy<IWorkHandlerBase, IWorkHandlerMethodData>(() => { return workHandlerBaseMock.Object; }, workHandlerMethodDataMock.Object);
//            Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>> dctMethods = new Dictionary<string, Lazy<IWorkHandlerBase, IWorkHandlerMethodData>>();
//            dctMethods.Add(TEST_METHOD_NAME, lazy);

//            MemoryStream jsonRequestMessageStream = new MemoryStream();
//            StreamWriter sr = new StreamWriter(jsonRequestMessageStream);
//            sr.Write(jsonRequestString);
//            sr.Flush();
//            jsonRequestMessageStream.Position = 0;

//            //Setup
//            string error;
//            workHandlerMethodDataMock.Setup(data => data.SupportedMethods).Returns(TEST_METHOD_NAME);
//            assemblyResolverMock.Setup(rsr => rsr.BuildMethodDictionary(It.IsAny<String>(), It.IsAny<bool>())).Returns(dctMethods);

//            genericWorkerConfigReaderMock.SetupGet(c => c.AutoShutdownProcessedMessageCountMin).Returns(5);
//            genericWorkerConfigReaderMock.SetupGet(c => c.AutoShutdownProcessedMessageCountMax).Returns(6);

//            assemblyLocatorMock.SetupGet(al => al.WorkHandlerServiceDirectories).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { TEST_SERVICE_NAME, "C:\\DummyDir" } });

//            GenericWorkerProcess process = new GenericWorkerProcess(container); // We need to create this here so we can callback in setup methods for ContinueExecution. (Short circuit POLL method)

//            jsonRPCHelperMock.Setup(helper => helper.DeSerializeJSonRPCRequest(It.IsAny<String>())).Returns(jsonRequest);
//            queueSettingsResolverMock.Setup(rsr => rsr.ResolveByQueueName(It.IsAny<String>())).Returns(settings);
//            queueMessageMock.Setup(msg => msg.Message).Returns(jsonRequestMessageStream);
//            queueMessageMock.Setup(msg => msg.MessageId).Returns(Guid.NewGuid());
//            communicationManagerMock.Setup(mgr => mgr.HeartbeatRequired).Returns(true);
//            workHandlerBaseMock.Setup(w => w.Validate(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), out error)).Returns(true);
//            workHandlerBaseMock.Setup(w => w.DownloadTest(It.IsAny<string>())).Returns(expectedResultString);
//            genericWorkerInstrumentationMock.SetupGet(gwi => gwi.IntervalProcessedMessagesCountByMethod).Returns(genericWorkerInstrumentationIntervalProcessedMessagesCountByMethod);
//            mqQueueControllerMock.Setup(clr => clr.Get(It.IsAny<QueueSettings>())).Returns(() =>
//            {
//                if (messageAvailable)
//                {
//                    if (loopCount > 10)
//                        messageAvailable = false;

//                    loopCount++;

//                    return queueMessageMock.Object;
//                }
//                else
//                {
//                    return null;
//                }
//            }).Callback(() =>
//            {
//                jsonRequestMessageStream = new MemoryStream();
//                sr = new StreamWriter(jsonRequestMessageStream);
//                sr.Write(jsonRequestString);
//                sr.Flush();
//                jsonRequestMessageStream.Position = 0;
//                queueMessageMock.Setup(msg => msg.Message).Returns(jsonRequestMessageStream);

//                if (loopCount > 10)
//                    process.ContinueExecution = false; //This prevents the GenericWorkerProcess from POLLing forever for messages.
//            });

//            //Act
//            process.Run(settings.RequestQueue);

//            //Verify
//            Assert.Equal(5, loopCount); //This should never loop more then five if AutoShutdown worked.  Failsafe at 10 so our unittest doesn't hang.
//        }

//        private JsonRequest CreateJsonRequest()
//        {
//            Dictionary<string, object> parameters = new Dictionary<string, object>();
//            parameters.Add("imageData", "TEST");
//            JsonRequest request = new JsonRequest
//            {
//                Method = "Image.DownloadTest",
//                Params = parameters,
//                Id = Guid.NewGuid().ToString()
//            };
//            return request;
//        }
//    }
//}
