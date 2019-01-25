using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.WorkHandlerDelegate
{
    class JsonParameterValidationHelper
    {
        private JsonSchemaGenerator _jsonSchemaGenerator;

        public JsonParameterValidationHelper()
        {
            _jsonSchemaGenerator = new JsonSchemaGenerator();
        }

        public JsonSchema Generate(Type type, bool allowNullable, object defaultValue)
        {
            JsonSchema schema = _jsonSchemaGenerator.Generate(type, allowNullable ? IsNullable(type) : false);
            if (defaultValue != null)
                schema.Default = JToken.FromObject(defaultValue);
            return schema;
        }

        public JsonSchema Generate(Type type, bool allowNullable)
        {
            JsonSchema schema = _jsonSchemaGenerator.Generate(type, allowNullable ? IsNullable(type) : false);
            return schema;
        }

        #region Private Methods
        bool IsNullable(Type type)
        {
            if (!type.IsValueType) return true; // ref-type
            if (Nullable.GetUnderlyingType(type) != null) return true; // Nullable<T>
            return false; // value-type
        }
        #endregion
    }
}
