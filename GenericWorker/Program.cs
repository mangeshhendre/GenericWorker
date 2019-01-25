using CommandLine;
using Microsoft.Practices.Unity;
using GenericWorker.Configuration;
using GenericWorker.Instrumentation;
using GenericWorker.Instrumentation.GenericWorkerInstrumentationWriter;
using GenericWorker.SupportClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MyLibrary.Common.Logging;
using MyLibrary.Common.Tracing;
using MyLibrary.Common.Interfaces;
using MyLibrary.Common.WorkHandling.Interfaces;
using MyLibrary.Common.WorkHandling;
using MyLibrary.Common.StatsD;
using MyLibrary.Common;
using MyLibrary.Common.Json.Interfaces;
using MyLibrary.Common.Json;

namespace GenericWorker
{
    public class Program
    {
        #region Private Members
        private static string APPLICATON_NAME = "GenericWorker";
        private static Logger _logger = null;
        private static Tracer _tracer = null;
        #endregion

        static void Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args)
                .WithNotParsed(errors => { })
                .WithParsed(options => {
                    Console.WriteLine("Starting: {0}", options.QueueName);

                    //some things need to be in the config file itself and loaded outside of the container
                    var configReader = new GenericWorkerConfigReader();
                    var shadowCopy = configReader.ShadowCopyWorkHandlers;
                    Console.WriteLine("Shadow Copy: {0}", shadowCopy);

                    //start
                    StartWorkerProcess(options, shadowCopy);
                });
        } 

        #region Private Members
        private static void StartWorkerProcess(Options commandLineOptions, bool shadowCopy)
        {
            if (shadowCopy)
            {
                //start in new appDomain
                var appDomainSetup = new AppDomainSetup { ShadowCopyFiles = "true" };
                var applicationName = string.Format("GenericWorker.exe {0}", commandLineOptions.QueueName);
                var appDomain = AppDomain.CreateDomain(applicationName, AppDomain.CurrentDomain.Evidence, appDomainSetup);
                appDomain.SetData("QueueName", commandLineOptions.QueueName);
                appDomain.SetData("AutoShutdownMemoryThresholdBytes", commandLineOptions.AutoShutdownMemoryThresholdBytes);
                appDomain.DoCallBack(new CrossAppDomainDelegate(DoWorkInShadowCopiedDomain));
            }
            else
            {
                //start in host appDomain
                GenericWorkerProcess process = new GenericWorkerProcess(CreateUnityContainer(commandLineOptions.QueueName, commandLineOptions.AutoShutdownMemoryThresholdBytes));
                
                //execute 
                GenericWorkerProcessRun(process, commandLineOptions.QueueName);
            }
        }

        private static IUnityContainer CreateUnityContainer(string queueName, long? autoShutdownMemoryThresholdBytes)
        {
            //configuration 
            var genericWorkerConfiguration = new GenericWorkerConfiguration();

            //setup container
            UnityContainer container = new UnityContainer();
            container.RegisterType<IAssemblyResolver, AssemblyResolver>(new ContainerControlledLifetimeManager());
            container.RegisterType<ICommunicationManager, CommunicationManager>();
            container.RegisterType<IJSonRPCHelper, JSonRPCHelper>();
            container.RegisterType<ILogger, Logger>(new ContainerControlledLifetimeManager());
            container.RegisterType<ITracer, Tracer>(new ContainerControlledLifetimeManager());
            container.RegisterType<IWorkHandlerToolkit, WorkHandlerToolkit>();
            container.RegisterType<IGenericWorkerInstrumentationWriter, GenericWorkerInstrumentationWriter>();
            container.RegisterType<IGenericWorkerInstrumentation, GenericWorkerInstrumentation>();
            container.RegisterType<IWorkHandlerMapping, WorkHandlerMapping>();
            container.RegisterType<IStatsDClient, StatsDClient>(new InjectionConstructor(new object[] { genericWorkerConfiguration.StatsD_Host, genericWorkerConfiguration.StatsD_Port }));

            var genericWorkerSettings = genericWorkerConfiguration.Settings;
            if (autoShutdownMemoryThresholdBytes.HasValue) genericWorkerSettings.AutoShutdownMemoryThresholdBytes = autoShutdownMemoryThresholdBytes;
            container.RegisterInstance<IGenericWorkerSettings>(genericWorkerSettings);

            //logging & tracing
            _logger = (Logger)container.Resolve<ILogger>();
            _tracer = (Tracer)container.Resolve<ITracer>();
            _logger.ApplicationName = APPLICATON_NAME;
            _tracer.ApplicationName = APPLICATON_NAME;
            _logger.ApplicationParameters = queueName;
            _tracer.ApplicationParameters = queueName;
            _logger.LogLevel = (LogEntrySeverity)genericWorkerConfiguration.Settings.LogLevel;
            _tracer.LogLevel = (LogEntrySeverity)genericWorkerConfiguration.Settings.LogLevel;
            _logger.OtherData["GenericWorkerVersion"] = Assembly.GetExecutingAssembly().FullName;
            _tracer.OtherData["GenericWorkerVersion"] = Assembly.GetExecutingAssembly().FullName;
            _logger.OtherData["QueueName"] = queueName;
            _tracer.OtherData["QueueName"] = queueName;

            //assemblyLocator
            var handlerDirectory = container.Resolve<IGenericWorkerSettings>().HandlerDirectory;
            container.RegisterType<IAssemblyLocator, AssemblyLocator>(new InjectionConstructor(handlerDirectory));

            return container;
        }

        private static void DoWorkInShadowCopiedDomain()
        {
            var queueName = (string)AppDomain.CurrentDomain.GetData("QueueName");
            var autoShutdownMemoryThresholdBytes = (long?)AppDomain.CurrentDomain.GetData("AutoShutdownMemoryThresholdBytes");
            GenericWorkerProcess process = new GenericWorkerProcess(CreateUnityContainer(queueName, autoShutdownMemoryThresholdBytes));

            //execute
            GenericWorkerProcessRun(process, queueName);
        }

        private static void GenericWorkerProcessRun(GenericWorkerProcess process, string queueName)
        {
            try
            {
                process.Run(queueName);
            }
            catch (ReflectionTypeLoadException ex)
            {
                HandleReflectionTypeLoadException(ex);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }
        #endregion

        #region Exception Handling
        private static void HandleReflectionTypeLoadException(ReflectionTypeLoadException ex)
        {
            //main exception message
            var message = string.Format("Exception: {0}", ex.Message);
            _logger.Error(message, ex, new Dictionary<string, string> { { "Fatal", "true" } });
            Console.WriteLine("\r\n" + message);

            //loader exceptons
            foreach (Exception loaderEx in ex.LoaderExceptions)
            {
                var loaderExMessage = string.Format("LoaderException: {0}", loaderEx.Message);
                _logger.Error(loaderExMessage, loaderEx, new Dictionary<string, string> { { "Fatal", "true" } });
                Console.WriteLine("\r\n" + loaderExMessage);
            }

            //write stack to screen (already been logged)
            Console.WriteLine(string.Format("\r\nStackTrace: {0}", ex.StackTrace));
        }

        private static void HandleException(Exception ex)
        {
            //main exception message
            var message = string.Format("Exception: {0}", ex.Message);

            //log it
            _logger.Error(message, ex, new Dictionary<string, string> { { "Fatal", "true" } });

            //print it
            Console.WriteLine("\r\n" + message);
            Console.WriteLine(string.Format("\r\nStackTrace: {0}", ex.StackTrace));
        }
        #endregion
    }
}