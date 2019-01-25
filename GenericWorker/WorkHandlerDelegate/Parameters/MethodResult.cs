using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.WorkHandlerDelegate.Parameters
{
    public class MethodResult
    {
        private readonly bool _isValid;
        private readonly string _message;

        public MethodResult(bool isValid, string validationMessage)
        {
            _isValid = isValid;
            _message = validationMessage;
        }
        public bool IsValid
        {
            get
            {
                return _isValid;
            }
        }

        public string ValidationMessage
        {
            get
            {
                return _message;
            }
        }
    }
}
