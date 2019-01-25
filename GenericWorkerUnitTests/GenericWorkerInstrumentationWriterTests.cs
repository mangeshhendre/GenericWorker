using Microsoft.Practices.Unity;
using Moq;
using Safeguard.Library.Universal.Json.Interfaces;
using Safeguard.Library.Universal.Json.Support;
using GenericWorker.Instrumentation;
using GenericWorker.Instrumentation.GenericWorkerInstrumentationWriter;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenericWorkerUnitTests
{
    [ExcludeFromCodeCoverage]
    public class GenericWorkerInstrumentationWriterTests
    {
        [Fact]
        public void GenericWorkerInstrumentationWriter_ResolveInstrumentationDotWrite()
        {
            var jsonHelperMock = new Mock<IJSonRPCHelper>();

            IUnityContainer container = new UnityContainer();
            container.RegisterInstance(typeof(IJSonRPCHelper), jsonHelperMock.Object);
        
            //Act
            var genericWorkerInstrumentationWriter = new GenericWorkerInstrumentationWriter(container);

        }

        [Fact]
        public void GenericWorkerInstrumentationWriter_InstrumentationIntervalTest()
        {
            var intervalEventArgs = new IntervalEventArgs()
            {
                IntervalTime = new TimeSpan(0, 0, 60),
                TotalTime = new TimeSpan(0, 60, 0),
                WorkerStateDetails = new List<WorkerStateDetail>() { new WorkerStateDetail() { Name = "Test", StopwatchActive = new Stopwatch(), StopwatchInterval = new Stopwatch() } },
                IntervalProcessedMessageCountByMethod = new Dictionary<string,int>()
            };

            var jsonHelperMock = new Mock<IJSonRPCHelper>();

            IUnityContainer container = new UnityContainer();
            container.RegisterInstance(typeof(IJSonRPCHelper), jsonHelperMock.Object);

            //Act
            var genericWorkerInstrumentationWriter = new GenericWorkerInstrumentationWriter(container);
            genericWorkerInstrumentationWriter.InstrumentationInterval(this, intervalEventArgs);

            System.Threading.Thread.Sleep(500);

            jsonHelperMock.Verify(j => j.SerializeJSonRPCRequest(It.IsAny<JsonRequest>()), Times.Once());
        }
    }
}
