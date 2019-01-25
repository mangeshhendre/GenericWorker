//using Microsoft.Practices.Unity;
//using GenericWorker;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace GenericWorkerUnitTests
//{
//    public class TestGenericWorkerProcess : GenericWorkerProcess
//    {
//        public TestGenericWorkerProcess(UnityContainer container)
//            : base(container)
//        {
//        }

//        public new bool ValidateRequestQueue(string queueName)
//        {
//            return base.ValidateRequestQueue(queueName);
//        }

//        public void TestRun(string queueName)
//        {
//            base.ValidateRequestQueue(queueName);
//            base.Poll();
//        }

//        public void Poll()
//        {
//            base.ValidateRequestQueue(string.Empty);
//            base.Poll();
//        }
//    }
//}
