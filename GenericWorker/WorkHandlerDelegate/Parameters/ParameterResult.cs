using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.WorkHandlerDelegate.Parameters
{
    public class ParameterResult
    {
        private readonly bool _isValid;
        private readonly object[] _parameters;
        private readonly List<string> _messages;

        public ParameterResult(object[] parameters, bool isValid, List<string> validationMessages)
        {
            _isValid = isValid;
            _parameters = parameters;
            _messages = validationMessages;
        }
        public bool IsValid
        {
            get
            {
                return _isValid;
            }
        }

        public object[] Parameters
        {
            get
            {
                return _parameters;
            }
        }

        public List<string> ValidationMessages
        {
            get
            {
                return _messages;
            }
        }
    }
}
