using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Safeguard.Library.Universal.WorkHandling.Attributes;
using Safeguard.Library.Universal.WorkHandling.Parameters;
using GenericWorker.WorkHandlerDelegate.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.WorkHandlerDelegate
{
    public class HandlerMethodInfo
    {
        private readonly JsonSchemaGenerator _jsonSchemaGenerator = new JsonSchemaGenerator();
        private readonly MethodInfo _methodInfo;
        private readonly ParameterInfo[] _parameterInfo;
        private readonly Dictionary<string, JsonSchema> _parameterJsonSchema;
        private readonly JsonSchema _returnJsonSchema;
        private readonly Dictionary<string, ServiceBusAttribute> _parameterAttributes;
        private readonly Dictionary<string, LateBoundMethod> _parameterValidators;
        private readonly string _methodValidatorName;
        private readonly LateBoundMethod _methodValidator;
        private readonly LateBoundMethod _delegate;
        private readonly JsonParameterValidationHelper _jsonParameterValidationHelper = new JsonParameterValidationHelper();

        public HandlerMethodInfo(MethodInfo methodInfo, MethodInfo[] validationMethodsForParameters, MethodInfo[] validationMethodsForMethods)
        {
            //method
            _methodInfo = methodInfo;

            //return type schema
            _returnJsonSchema = _jsonSchemaGenerator.Generate(_methodInfo.ReturnType);

            //method validation - allows validation where parameters have dependencies on other parameters (e.g.  "ParamOne" must not be null if "ParamTwo" is not null)
            var methodAttribute = _methodInfo.GetCustomAttribute(typeof(ServiceBusAttribute));
            if (methodAttribute != null)
            {
                var serviceBusMethodAttribute = (ServiceBusAttribute)methodAttribute;

                if (!string.IsNullOrEmpty(serviceBusMethodAttribute.ValidationDelegateName))
                {
                    var validationMethod = validationMethodsForMethods.FirstOrDefault(mi => mi.Name.Equals(serviceBusMethodAttribute.ValidationDelegateName, StringComparison.OrdinalIgnoreCase));
                    if (validationMethod != null)
                    {
                        _methodValidatorName = validationMethod.Name;
                        _methodValidator = DelegateFactory.Create(validationMethod);
                    }
                }
            }

            //parameters
            _parameterInfo = _methodInfo.GetParameters();

            //parameter attributes
            _parameterAttributes = new Dictionary<string, ServiceBusAttribute>();
            _parameterValidators = new Dictionary<string, LateBoundMethod>();
            _parameterJsonSchema = new Dictionary<string, JsonSchema>();
            foreach (var pi in _parameterInfo)
            {
                var attribute = pi.GetCustomAttribute(typeof(ServiceBusAttribute));
                if (attribute != null)
                {
                    var serviceBusAttribute = (ServiceBusAttribute)attribute;

                    _parameterAttributes.Add(pi.Name, serviceBusAttribute);
                    if (!string.IsNullOrEmpty(serviceBusAttribute.ValidationDelegateName))
                    {
                        var validationMethod = validationMethodsForParameters.FirstOrDefault(mi => mi.Name.Equals(serviceBusAttribute.ValidationDelegateName, StringComparison.OrdinalIgnoreCase));
                        if (validationMethod != null)
                        {
                            _parameterValidators.Add(pi.Name, DelegateFactory.Create(validationMethod));
                        }
                    }
                }

                //generate schema
                bool allowNullable = _parameterAttributes.ContainsKey(pi.Name) && !_parameterAttributes[pi.Name].IsNullAble ? false : true; //allow nullable (type itself may still not be nullbale) if not explicitly set to false
                _parameterJsonSchema.Add(pi.Name, pi.HasDefaultValue ? _jsonParameterValidationHelper.Generate(pi.ParameterType, allowNullable, pi.DefaultValue == null ? "null" : pi.DefaultValue) : _jsonParameterValidationHelper.Generate(pi.ParameterType, allowNullable));
            }

            //delegate
            _delegate = DelegateFactory.Create(_methodInfo.DeclaringType, _methodInfo.Name, _parameterInfo.Select(p => p.ParameterType).ToArray());
        }

        public MethodInfo MethodInfo
        {
            get
            {
                return _methodInfo;
            }
        }
        public ParameterInfo[] ParameterInfo
        {
            get
            {
                return _parameterInfo;
            }
        }
        public Dictionary<string, ServiceBusAttribute> ParameterAttributes
        {
            get
            {
                return _parameterAttributes;
            }
        }

        public Dictionary<string, JsonSchema> ParametersJsonSchema
        {
            get
            {
                return _parameterJsonSchema;
            }
        }

        public JsonSchema ReturnJsonSchema
        {
            get
            {
                return _returnJsonSchema;
            }
        }

        public LateBoundMethod Delegate
        {
            get
            {
                return _delegate;
            }
        }
        public ParameterResult ValidateParameters(object instance, Dictionary<string, object> inboundParameters)
        {
            var returnParameters = new List<object>();
            var isValid = true;
            var validationMessages = new List<string>();


            _parameterInfo.ToList().ForEach(pi =>
            {
                //if parameter has not been sent, but has a default value... use the default
                if (!inboundParameters.ContainsKey(pi.Name) && pi.HasDefaultValue)
                {
                    returnParameters.Add(pi.DefaultValue);
                    return;
                }

                //parameter value
                var parameterValue = inboundParameters[pi.Name];

                //attribute
                if (_parameterAttributes.ContainsKey(pi.Name))
                {
                    var attribute = _parameterAttributes[pi.Name];

                    //check nullable
                    if (parameterValue == null && !attribute.IsNullAble)
                    {
                        isValid = false;
                        validationMessages.Add(string.Format("Invalid Parameter - Method: {0} - Name: {1} - Type: {2} - Value: NULL", _methodInfo.Name, pi.Name, pi.ParameterType.Name));
                        return;
                    }

                    //****
                    //***
                    //**
                    object changedValue;
                    IList<string> jsonSchemaErrorMessages;
                    if (!TryChangeType(parameterValue, pi.ParameterType, _parameterJsonSchema[pi.Name], out changedValue, out jsonSchemaErrorMessages))
                    {
                        var schemaMessage = jsonSchemaErrorMessages == null || jsonSchemaErrorMessages.Count == 0 ?
                            string.Empty :
                            string.Format(" - JsonSchemaErrors: {0}", string.Join(". ", jsonSchemaErrorMessages));

                        isValid = false;
                        validationMessages.Add(string.Format("Invalid Parameter - Method: {0} - Name: {1} - Type: {2} - Value: {3}{4}", _methodInfo.Name, pi.Name, pi.ParameterType.Name, parameterValue, schemaMessage));
                        return;
                    }
                    else
                    {
                        parameterValue = changedValue;
                    }

                    //execute validation delegate
                    if (!string.IsNullOrEmpty(attribute.ValidationDelegateName))
                    {
                        if (!_parameterValidators.ContainsKey(pi.Name))
                            throw new InvalidOperationException("ValidationDelegateName not found: " + attribute.ValidationDelegateName);
                        
                        var validationResult = (ParameterValidationResult)_parameterValidators[pi.Name](instance, new object[] { parameterValue });
                        if (!(validationResult.IsValid))
                        {
                            isValid = false;
                            validationMessages.Add(string.Format("Invalid Parameter - Method: {0} - Name: {1} - Type: {2} - Value: {3} - ValidationDelegate: {4} - ValidationMessage: {5}", _methodInfo.Name, pi.Name, pi.ParameterType.Name, parameterValue, attribute.ValidationDelegateName, validationResult.Message == null ? string.Empty : validationResult.Message));
                            return;
                        }
                    }
                }
                else
                {
                    object changedValue;
                    IList<string> jsonSchemaErrorMessages;
                    if (!TryChangeType(parameterValue, pi.ParameterType, _parameterJsonSchema[pi.Name], out changedValue, out jsonSchemaErrorMessages))
                    {

                        var schemaMessage = jsonSchemaErrorMessages == null || jsonSchemaErrorMessages.Count == 0 ? 
                            string.Empty : 
                            string.Format(" - JsonSchemaErrors: {0}", string.Join(". ", jsonSchemaErrorMessages));

                        isValid = false;
                        validationMessages.Add(string.Format("Invalid Parameter - Method: {0} - Name: {1} - Type: {2} - Value: {3}{4}", _methodInfo.Name, pi.Name, pi.ParameterType.Name, parameterValue, schemaMessage));
                        return;
                    }
                    else
                    {
                        parameterValue = changedValue;
                    }
                }

                returnParameters.Add(parameterValue);
            });

            return new ParameterResult(returnParameters.ToArray(), isValid, validationMessages);
        }
        public MethodResult ValidateMethod(object instance, object[] parameters)
        {
            //no method validator specified...  default to valid
            if(_methodValidator == null)
                return new MethodResult(isValid:true, validationMessage:null);

            int parameterIndex = 0;
            var parameterDictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _parameterInfo.ToList().ForEach(pi =>
            {
                parameterDictionary[pi.Name] = parameters[parameterIndex];
                parameterIndex++;
            });

            var validationResult = (ParameterValidationResult)_methodValidator(instance, new object[] { parameterDictionary });
            if (!(validationResult.IsValid))
            {
                var message = string.Format("Invalid Parameter(s) For Method Validation - Method: {0} - ValidationDelegate: {1} - ValidationMessage: {2}", _methodInfo.Name, _methodValidatorName, validationResult.Message);
                return new MethodResult(isValid: false, validationMessage: message);
            }
            else
            {
                return new MethodResult(isValid: true, validationMessage: null);
            }
        }
        #region Private Methods
        private bool TryChangeType(object value, Type type, JsonSchema jsonSchema, out object result, out IList<string> jsonSchemaErrorMessages)
        {
            //set to null, if applicable it will be set with values later
            jsonSchemaErrorMessages = null;

            try
            {
                if (type.IsPrimitive || type.Equals(typeof(string)) || type.Equals(typeof(DateTime)))
                {
                    result = Convert.ChangeType(value, type);
                }
                else
                {
                    if (type.Namespace == "System")
                    {
                        //Guid must be wrapped in quotes for JsonConvert to Deserialize it 
                        if (value == null || type == typeof(Guid))
                            result = JsonConvert.DeserializeObject(string.Format("\"{0}\"", value), type);
                        else
                            result = JsonConvert.DeserializeObject(string.Format("{0}", value), type);
                    }
                    else
                    {
                        //check the type mask for Null
                        bool isNullable = (jsonSchema.Type | Newtonsoft.Json.Schema.JsonSchemaType.Null) == jsonSchema.Type;
                        if (isNullable && value == null)
                        {
                            result = null;
                            return true;
                        }
                        else
                        {
                            var jContainer = value as JContainer;
                            if (jContainer == null || !jContainer.IsValid(jsonSchema, out jsonSchemaErrorMessages))
                            {
                                result = null;
                                return false;
                            }
                            result = JsonConvert.DeserializeObject(value.ToString(), type);
                        }
                    }
                }

                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }
        #endregion
    }
}
