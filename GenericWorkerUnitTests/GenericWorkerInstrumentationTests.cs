using Microsoft.Practices.Unity;
using Moq;
using GenericWorker.Instrumentation;
using GenericWorker.Instrumentation.GenericWorkerInstrumentationWriter;
using GenericWorker.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GenericWorkerUnitTests
{
    [ExcludeFromCodeCoverage]
    public class GenericWorkerInstrumentationTests
    {
        [Fact]
        public void GenericWorkerInstrumentationTests_ConfigurationReaderGetsTimerIntervalOnCreateTest()
        {
            //Arrange
            var genericWorkerSettingsMock = new Mock<IGenericWorkerSettings>();

            IUnityContainer container = new UnityContainer();
            container.RegisterInstance(typeof(IGenericWorkerSettings), genericWorkerSettingsMock.Object);
        
            //Act
            var genericWorkerInstrumentation = new GenericWorkerInstrumentation(container);

            //Assert
            genericWorkerSettingsMock.VerifyGet(r => r.InstrumentationTimer, Times.Once());
        }
    }
}
