using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;

namespace PortableJson.Xamarin
{
    public static class JsonSerializationHelper
    {
        private static readonly CultureInfo serializationCulture;
        private static readonly Type[] supportedValueTypes = new[]
        {
            typeof(int), typeof(long), typeof(short), typeof(double), 
            typeof(float), typeof(decimal), typeof(bool), typeof(Guid),
            typeof(TimeSpan), typeof(DateTime)
        };

        static JsonSerializationHelper()
        {
            serializationCulture = new CultureInfo("en-US");
        }

        /// <summary>
        /// Goes through all targetType's base types to see if one of them match baseType. In other words, to see if targetType inherits from baseType.
        /// </summary>
        /// <param name="targetType"></param>
        /// <param name="baseType"></param>
        /// <returns></returns>
        private static bool IsInheritedBy(Type targetType, Type baseType)
        {
            return baseType.IsAssignableFrom(targetType);
        }

        private static bool IsArrayType(Type type)
        {
            return type.IsArray || IsInheritedBy(type, typeof(IEnumerable));
        }

        public static string Serialize<T>(T element)
        {
            var result = string.Empty;

            if (element != null && element.GetType().ToString().Contains("KeyValuePair`2"))
            {
                var kv = JsonUtil.KVCastFrom(element);
                result += "{" + Serialize(kv.Key) + ", " + Serialize(kv.Value) + "}";
            }

            else if (element is string)
            {
                result += "\"";

                result += element
                    .ToString()
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");

                result += "\"";
            }
            else if (element is Enum)
            {
                result += Convert.ChangeType(element, (element as Enum).GetTypeCode()).ToString();
            }
            else if (element is int || element is long || element is short)
            {
                result += element.ToString();
            }
            else if (element is float || element is decimal || element is double)
            {
                result += element
                    .ToString()
                    .Replace(",", ".");
            }
            else if (element is bool)
            {
                result = (bool)(object)element ? "true" : "false";
            }
            else if (element is Guid)
            {
                result = "\"" + element + "\"";
            }
            else if (element is TimeSpan)
            {
                result = "\"" + element.ToString() + "\"";
            }
            else if (element is DateTime)
            {
                var dateTime = (DateTime)(object)element;
                DateTime utcDateTime = dateTime.ToUniversalTime();
                long ticks = JsonUtil.ConvertDateTimeToJavaScriptTicks(utcDateTime);
                result = ticks.ToString(CultureInfo.InvariantCulture);
            }
            else if (element is Version)
            {
                result = "\"" + element.ToString() + "\"";
            }
            else if (element == null)
            {
                result += "null";
            }
            else
            {
                var type = element.GetType();

                var isArray = element is IEnumerable;
                if (isArray)
                {
                    result += "[";
                }
                else
                {
                    result += "{";
                }

                if (isArray)
                {
                    var subElements = element as IEnumerable;
                    var count = 0;
                    foreach (var subElement in subElements)
                    {
                        result += Serialize(subElement) + ",";
                        count++;
                    }

                    if (count > 0)
                    {
                        //remove the extra comma.
                        result = result.Substring(0, result.Length - 1);
                    }
                }
                else
                {
                    var properties = type
                        .GetRuntimeProperties()
                        .Where(p => p.CanRead && p.CanWrite);
                    foreach (var property in properties)
                    {
                        result += "\"" + property.Name + "\":";

                        var value = property.GetValue(element);
                        result += Serialize(value) + ",";
                    }

                    if (properties.Any())
                    {
                        result = result.Substring(0, result.Length - 1);
                    }

                }

                if (isArray)
                {
                    result += "]";
                }
                else
                {
                    result += "}";
                }
            }

            return SanitizeJson(result);
        }

        public static T Deserialize<T>(string input)
        {
            return (T)(Deserialize(input, typeof(T)) ?? default(T));
        }

        public static object Deserialize(string input, Type type)
        {
            if (input == null || input == "null")
            {
                return null;
            }

            //remove all whitespaces from the JSON string.
            input = SanitizeJson(input);

            var nullablesRealType = Nullable.GetUnderlyingType(type);

            if (nullablesRealType != null && supportedValueTypes.Contains(nullablesRealType))
            {
                return DeserializeNullableSimple(input, nullablesRealType);
            }

            else if (nullablesRealType != null)
            {
                throw new NotSupportedException("unsupported nullable type found " + nullablesRealType);
            }

            else if (supportedValueTypes.Contains(type) || type == typeof(string) || type.IsEnum)
            {
                //simple deserialization.
                return DeserializeSimple(input, type);
            }
            else if (typeof(Version) == type)
            {
                return new Version(input.Replace("\"", ""));
            }
            else if (typeof(IDictionary).IsAssignableFrom(type))
            {
                return DeserializeDictionary(input, type);
            }
            else if (IsArrayType(type))
            {
                //array deserialization.
                return DeserializeArray(input, type);
            }
            else
            {
                //object deserialization.
                return DeserializeObject(input, type);
            }
        }

        /// <summary>
        /// Removes unnecessary whitespaces from a JSON input.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static string SanitizeJson(string input)
        {
            var inString = false;
            var inEscapeSequence = false;

            var result = string.Empty;

            foreach (var character in input)
            {
                if (inString)
                {
                    if (inEscapeSequence)
                    {
                        inEscapeSequence = false;
                    }
                    else if (character == '\\')
                    {
                        inEscapeSequence = true;
                    }
                }
                else
                {
                    if (character == ' ') continue;

                    if (character == '\"')
                    {
                        inString = true;
                    }
                }

                result += character;
            }

            return result;
        }

        /// <summary>
        /// Deserializes a JSON string representing an object into an object.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private static object DeserializeObject(string data, Type type)
        {
            if (data.Length < 2 || !(data.StartsWith("{") && data.EndsWith("}")))
            {
                throw new InvalidOperationException("JSON objects must begin with a '{' and end with a '}'.");
            }

            //get all properties that are relevant.
            var properties = type
                .GetRuntimeProperties()
                .Where(p => p.CanWrite);
            var instance = Activator.CreateInstance(type);

            //get the inner data.
            data = data.Substring(0, data.Length - 1).Substring(1);

            //maintain the state.
            var inString = false;
            var inEscapeSequence = false;
            var nestingLevel = 0;

            var temporaryData = string.Empty;
            for (var i = 0; i < data.Length; i++)
            {
                //are we done with the whole sequence, or are we at the next element?
                var isLastCharacter = i == data.Length - 1;

                var character = data[i];
                if (inString && !isLastCharacter)
                {
                    if (inEscapeSequence)
                    {
                        inEscapeSequence = false;
                    }
                    else
                    {
                        if (character == '\"')
                        {
                            temporaryData += character;
                            inString = false;
                        }
                        else if (character == '\\')
                        {
                            inEscapeSequence = true;
                        }
                        else
                        {
                            temporaryData += character;
                        }
                    }
                }
                else
                {
                    if (character == '\"' && !isLastCharacter)
                    {
                        inString = true;
                        temporaryData += character;
                    }
                    else
                    {

                        //keep track of nesting levels.
                        if (character == '[' || character == '{') nestingLevel++;
                        if (character == ']' || character == '}') nestingLevel--;

                        if (nestingLevel == 0 && (character == ',' || isLastCharacter))
                        {
                            //do we need to finalize the temporary data?
                            if (isLastCharacter)
                            {
                                temporaryData += character;
                            }

                            if (!string.IsNullOrEmpty(temporaryData))
                            {
                                var chunks = temporaryData.Split(new[] { ':' }, 2);

                                var propertyName = chunks[0];
                                if (propertyName.StartsWith("\"")) propertyName = propertyName.Substring(1);
                                if (propertyName.EndsWith("\"")) propertyName = propertyName.Substring(0, propertyName.Length - 1);

                                var propertyValue = chunks[1];

                                //now we can find the property on the object.
                                var property = properties.SingleOrDefault(p => p.Name == propertyName);
                                if (property != null)
                                {

                                    //deserialize the inner data and set it to the property.
                                    var element = Deserialize(propertyValue, property.PropertyType);
                                    property.SetValue(instance, element);

                                }

                                //reset the temporary data.
                                temporaryData = string.Empty;
                            }
                        }
                        else
                        {
                            temporaryData += character;
                        }
                    }
                }
            }

            return instance;
        }

        /// <summary>
        /// Deserializes a JSON string representing an array into an array.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private static object DeserializeArray(string data, Type type)
        {
            return DeserializeArray(data, type, "JSON arrays", "[", "]");
        }

        private static object DeserializeArray(string data, Type type, string what, string start_char, string end_char)
        {
            if (data.Length < 2 || !(data.StartsWith(start_char) && data.EndsWith(end_char)))
            {
                throw new InvalidOperationException(what + " must begin with a '[' and end with a ']'.");
            }

            Type innerType;

            //get the type of elements in this array.
            if (type.GenericTypeArguments.Length == 0)
            {
                // Something like Foo : List<>
                innerType = type.BaseType.GenericTypeArguments[0];
            }
            else
            {
                // Something like List<>
                innerType = type.GenericTypeArguments[0];
            }
            //var listType = typeof(List<>).MakeGenericType(innerType);
            var listType = type;
            var list = Activator.CreateInstance(listType);
            var addMethodName = nameof(List<object>.Add);
            var listAddMethod = listType.GetRuntimeMethod(addMethodName, new[] { innerType });

            //get the inner data.
            data = data.Substring(0, data.Length - 1).Substring(1);

            //maintain the state.
            var inString = false;
            var inEscapeSequence = false;
            var nestingLevel = 0;

            var temporaryData = string.Empty;
            for (var i = 0; i < data.Length; i++)
            {
                //are we done with the whole sequence, or are we at the next element?
                var isLastCharacter = i == data.Length - 1;

                var character = data[i];
                if (inString && !isLastCharacter)
                {
                    if (inEscapeSequence)
                    {
                        inEscapeSequence = false;
                    }
                    else
                    {
                        if (character == '\"')
                        {
                            temporaryData += character;
                            inString = false;
                        }
                        else if (character == '\\')
                        {
                            inEscapeSequence = true;
                        }
                        else
                        {
                            temporaryData += character;
                        }
                    }
                }
                else
                {
                    if (character == '\"' && !isLastCharacter)
                    {
                        inString = true;
                        temporaryData += character;
                    } else { 

                        //keep track of nesting levels.
                        if (character == '[' || character == '{') nestingLevel++;
                        if (character == ']' || character == '}') nestingLevel--;

                        if (nestingLevel == 0 && (character == ',' || isLastCharacter))
                        {
                            //do we need to finalize the temporary data?
                            if (isLastCharacter)
                            {
                                temporaryData += character;
                            }

                            if (!string.IsNullOrEmpty(temporaryData))
                            {
                                var element = Deserialize(temporaryData, innerType);
                                listAddMethod.Invoke(list, new[] { element });

                                //reset the temporary data.
                                temporaryData = string.Empty;
                            }
                        }
                        else
                        {
                            temporaryData += character;
                        }
                    }
                }
            }

            return list;
        }

        private static object DeserializeDictionary(string data, Type type)
        {
            if (data.Length < 2 || !(data.StartsWith("[") && data.EndsWith("]")))
            {
                throw new InvalidOperationException("JSON arrays must begin with a '[' and end with a ']'.");
            }

            Type keyType;
            Type valueType;

            //get the type of elements in this array.
            if (type.GenericTypeArguments.Length == 0)
            {
                // Something like Foo : Dictionary<,>
                keyType = type.BaseType.GenericTypeArguments[0];
                valueType = type.BaseType.GenericTypeArguments[1];
            }
            else
            {
                // Something like List<>
                keyType = type.GenericTypeArguments[0];
                valueType = type.GenericTypeArguments[1];
            }
            var dictType = type;
            var dict = Activator.CreateInstance(dictType);
            var addMethodName = nameof(Dictionary<object,object>.Add);
            var dictAddMethod = dictType.GetRuntimeMethod(addMethodName, new[] { keyType, valueType });

            //get the inner data.
            data = data.Substring(0, data.Length - 1).Substring(1);

            //maintain the state.
            var inString = false;
            var inEscapeSequence = false;
            var nestingLevel = 0;

            var temporaryData = string.Empty;
            for (var i = 0; i < data.Length; i++)
            {
                //are we done with the whole sequence, or are we at the next element?
                var isLastCharacter = i == data.Length - 1;

                var character = data[i];
                if (inString && !isLastCharacter)
                {
                    if (inEscapeSequence)
                    {
                        inEscapeSequence = false;
                    }
                    else
                    {
                        if (character == '\"')
                        {
                            temporaryData += character;
                            inString = false;
                        }
                        else if (character == '\\')
                        {
                            inEscapeSequence = true;
                        }
                        else
                        {
                            temporaryData += character;
                        }
                    }
                }
                else
                {
                    if (character == '\"' && !isLastCharacter)
                    {
                        inString = true;
                        temporaryData += character;
                    }
                    else
                    {

                        //keep track of nesting levels.
                        if (character == '[' || character == '{') nestingLevel++;
                        if (character == ']' || character == '}') nestingLevel--;

                        if (nestingLevel == 0 && (character == ',' || isLastCharacter))
                        {
                            //do we need to finalize the temporary data?
                            if (isLastCharacter)
                            {
                                temporaryData += character;
                            }

                            if (!string.IsNullOrEmpty(temporaryData))
                            {
                                // We get something like {"key", "value"} here
                                // Use array de-serializer to get key and value
                                //var kv = (DeserializeArray(temporaryData, typeof(List<object>), "key, value pairs", "{", "}");

                                var kv = DeserializeKeyValue(temporaryData, keyType, valueType);
                                dictAddMethod.Invoke(dict, new[] { kv.Key, kv.Value });

                                //reset the temporary data.
                                temporaryData = string.Empty;
                            }
                        }
                        else
                        {
                            temporaryData += character;
                        }
                    }
                }
            }

            return dict;
        }
        
        // Something like {0, "hello world"} where 0 is serialized key and "hello world" is serialized value
        private static KeyValuePair<object,object> DeserializeKeyValue(string data, Type keyType, Type valueType)
        {
            if (data.Length < 2 || !(data.StartsWith("{") && data.EndsWith("}")))
            {
                throw new InvalidOperationException("key value pairs must begin with a '{' and end with a '}'.");
            }

            object key = null, value = null;

            //get the inner data.
            data = data.Substring(0, data.Length - 1).Substring(1);

            //maintain the state.
            var inString = false;
            var inEscapeSequence = false;
            var nestingLevel = 0;
            int numElementsFound = 0;

            var temporaryData = string.Empty;
            for (var i = 0; i < data.Length; i++)
            {
                //are we done with the whole sequence, or are we at the next element?
                var isLastCharacter = i == data.Length - 1;

                var character = data[i];
                if (inString && !isLastCharacter)
                {
                    if (inEscapeSequence)
                    {
                        inEscapeSequence = false;
                    }
                    else
                    {
                        if (character == '\"')
                        {
                            temporaryData += character;
                            inString = false;
                        }
                        else if (character == '\\')
                        {
                            inEscapeSequence = true;
                        }
                        else
                        {
                            temporaryData += character;
                        }
                    }
                }
                else
                {
                    if (character == '\"' && !isLastCharacter)
                    {
                        inString = true;
                        temporaryData += character;
                    } else { 

                        //keep track of nesting levels.
                        if (character == '[' || character == '{') nestingLevel++;
                        if (character == ']' || character == '}') nestingLevel--;

                        if (nestingLevel == 0 && (character == ',' || isLastCharacter))
                        {
                            //do we need to finalize the temporary data?
                            if (isLastCharacter)
                            {
                                temporaryData += character;
                            }

                            if (!string.IsNullOrEmpty(temporaryData))
                            {
                                if (numElementsFound == 0)
                                {
                                    key = Deserialize(temporaryData, keyType);
                                }
                                else if (numElementsFound == 1)
                                {
                                    value = Deserialize(temporaryData, valueType);
                                }
                                else
                                {
                                    throw new Exception("only 2 elements should be in a dictionary key-value pair");
                                }
                                ++numElementsFound;

                                //reset the temporary data.
                                temporaryData = string.Empty;
                            }
                        }
                        else
                        {
                            temporaryData += character;
                        }
                    }
                }
            }

            return new KeyValuePair<object, object>(key, value);
        }

        private static object DeserializeNullableSimple(string data, Type underlyingType)
        {
            if (data == "null")
            {
                return null;
            }
            else
            {
                return DeserializeSimple(data, underlyingType);
            }
        }

        private static object DeserializeSimple(string data, Type type)
        {
            if (type == typeof(string))
            {
                if (data.Length < 2 || (!data.EndsWith("\"") && !data.StartsWith("\"")))
                {
                    throw new InvalidOperationException("String deserialization requires the JSON input to be encapsulated in quotation marks.");
                }
                else
                {
                    //get the inner string data.
                    data = data.Substring(0, data.Length - 1).Substring(1);

                    //keep track of state.
                    var inEscapeSequence = false;

                    var result = string.Empty;
                    foreach (var character in data)
                    {
                        if (inEscapeSequence)
                        {
                            inEscapeSequence = false;
                        }
                        else
                        {
                            if (character == '\\')
                            {
                                inEscapeSequence = true;
                                continue;
                            }
                        }

                        result += character;
                    }

                    return result;
                }
            }
            else if (type.IsEnum)
            {
                return Enum.ToObject(type, int.Parse(data));
            }
            else if (type == typeof(int))
            {
                return int.Parse(data, NumberStyles.Any, serializationCulture);
            }
            else if (type == typeof(long))
            {
                return long.Parse(data, NumberStyles.Any, serializationCulture);
            }
            else if (type == typeof(short))
            {
                return short.Parse(data, NumberStyles.Any, serializationCulture);
            }
            else if (type == typeof(float))
            {
                return float.Parse(data, NumberStyles.Any, serializationCulture);
            }
            else if (type == typeof(decimal))
            {
                return decimal.Parse(data, NumberStyles.Any, serializationCulture);
            }
            else if (type == typeof(double))
            {
                return double.Parse(data, NumberStyles.Any, serializationCulture);
            }
            else if (type == typeof(bool))
            {
                return string.Equals("true", data, StringComparison.OrdinalIgnoreCase) ? true : false;
            }
            else if (type == typeof(TimeSpan))
            {
                return TimeSpan.Parse(data.Replace("\"", ""));
            }
            else if (type == typeof(DateTime))
            {
                return JsonUtil.ConvertJavaScriptTicksToDateTime(long.Parse(data)).ToLocalTime();
            }
            else if(type == typeof(Guid))
            {
                if (data.Length < 2 || (!data.EndsWith("\"") && !data.StartsWith("\"")))
                {
                    throw new InvalidOperationException("GUID deserialization requires the JSON input to be encapsulated in quotation marks.");
                }
                else
                {
                    data = data.Substring(0, data.Length - 1).Substring(1);
                    return new Guid(data);
                }
            }
            else
            {
                throw new NotImplementedException(string.Format("Can't serialize a type of {0}.", data));
            }
        }
    }
}
