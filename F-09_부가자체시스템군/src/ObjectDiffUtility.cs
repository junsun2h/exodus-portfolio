using System;
using System.Numerics;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class ObjectDiffUtility
{
    private const string ADDED = "Added";
    private const string UPDATED = "Updated";
    private const string REMOVED = "Removed";
    private const string COUNT = "Count";
    private const string VALUE = "Value";
    private const string VALUE_STRING = "ValueString";

    public static string CompareObjects<T>(T objA, T objB)
    {
        var diffResult = ObjectComparator.Compare(objA, objB);
        var restructuredResult = JsonRestructurer.Restructure(diffResult);
        CalculateCountDifferences(restructuredResult, JObject.FromObject(objA), JObject.FromObject(objB));
        var result = JsonHelper.AddClassNameAsTopLevelKey(restructuredResult, typeof(T).Name);
        Debug.Log($"Final comparison result: {JsonConvert.SerializeObject(result, Formatting.Indented)}");
        return JsonConvert.SerializeObject(result, Formatting.Indented);
    }

    private static void CalculateCountDifferences(JObject diff, JObject objA, JObject objB)
    {
        foreach (var category in diff.Properties())
        {
            if (category.Value is JObject categoryObj)
            {
                CalculateCountDifferencesRecursive(categoryObj, objA, objB, category.Name);
            }
        }
    }

    private static void CalculateCountDifferencesRecursive(JObject obj, JObject objA, JObject objB, string path)
    {
        foreach (var prop in obj.Properties().ToList())
        {
            string currentPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";

            if (prop.Value is JObject childObj)
            {
                ProcessChildObject(childObj, objA, objB, currentPath);
            }
            else if (prop.Value is JArray arrayValue)
            {
                ProcessArrayValue(arrayValue, objA, objB, currentPath);
            }
        }
    }

    private static void ProcessChildObject(JObject childObj, JObject objA, JObject objB, string currentPath)
    {
        if (childObj.ContainsKey(COUNT) && childObj[COUNT] is JObject countObj && countObj.ContainsKey(VALUE))
        {
            CalculateAndUpdateCountDifference(countObj, objA, objB, currentPath);
        }
        else
        {
            CalculateCountDifferencesRecursive(childObj, objA, objB, currentPath);
        }
    }

    private static void ProcessArrayValue(JArray arrayValue, JObject objA, JObject objB, string currentPath)
    {
        for (int i = 0; i < arrayValue.Count; i++)
        {
            if (arrayValue[i] is JObject arrayObj)
            {
                CalculateCountDifferencesRecursive(arrayObj, objA, objB, $"{currentPath}[{i}]");
            }
        }
    }

    private static void CalculateAndUpdateCountDifference(JObject countObj, JObject objA, JObject objB, string currentPath)
    {
        var valueA = GetNestedValue(objA, currentPath + $".{COUNT}.{VALUE}");
        var valueB = GetNestedValue(objB, currentPath + $".{COUNT}.{VALUE}");

        if (valueA != null && valueB != null)
        {
            if (BigInteger.TryParse(valueA.ToString(), out BigInteger countA) &&
                BigInteger.TryParse(valueB.ToString(), out BigInteger countB))
            {
                BigInteger diff = countB - countA;
                countObj[VALUE] = diff.ToString();

                if (countObj.ContainsKey(VALUE_STRING))
                {
                    countObj[VALUE_STRING] = diff.ToString();
                }

                Debug.Log($"Count difference calculated for {currentPath}: B({countB}) - A({countA}) = {diff}");
            }
            else
            {
                Debug.LogWarning($"Failed to parse count values for {currentPath}");
            }
        }
    }

    private static JToken GetNestedValue(JObject obj, string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        string[] parts = path.Split('.');

        // 최상위 키 처리
        string[] topLevelKeys = { "Added", "Updated", "Removed" };
        if (parts.Length > 1 && topLevelKeys.Contains(parts[0]))
        {
            // 최상위 키가 있으면 제거
            parts = parts.Skip(1).ToArray();
        }

        JToken current = obj;

        foreach (string part in parts)
        {
            if (current == null)
                return null;

            if (current is JObject jObj)
            {
                current = jObj[part];
            }
            else if (current is JArray jArr && int.TryParse(part, out int index))
            {
                if (index >= 0 && index < jArr.Count)
                    current = jArr[index];
                else
                    return null;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static class ObjectComparator
    {
        public static JObject Compare<T>(T objA, T objB)
        {
            var diff = new JObject
            {
                ["Added"] = new JObject(),
                ["Updated"] = new JObject(),
                ["Removed"] = new JObject()
            };
            CompareProperties(objA, objB, typeof(T).Name, diff, true);
            JsonHelper.RemoveEmptyObjects(diff);
            return diff;
        }

        private static void CompareProperties(object obj1, object obj2, string path, JObject diff, bool isTopLevel)
        {
            if (obj1 == null && obj2 == null) return;

            Type type = obj1?.GetType() ?? obj2?.GetType();
            if (type == null) return;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var value1 = obj1 != null ? prop.GetValue(obj1) : null;
                    var value2 = obj2 != null ? prop.GetValue(obj2) : null;

                    string currentPath = TypeHelper.GetCurrentPath(path, prop.Name, isTopLevel);

                    if (value1 == null && value2 != null)
                    {
                        JsonHelper.AddToDiff(diff["Added"] as JObject, currentPath, JToken.FromObject(value2));
                        Debug.Log($"Added: {currentPath} = {value2}");
                    }
                    else if (value1 != null && value2 == null)
                    {
                        JsonHelper.AddToDiff(diff["Removed"] as JObject, currentPath, JToken.FromObject(value1));
                        Debug.Log($"Removed: {currentPath} = {value1}");
                    }
                    else if (!TypeHelper.SafeEquals(value1, value2))
                    {
                        if (TypeHelper.IsSimpleType(prop.PropertyType))
                        {
                            JsonHelper.AddToDiff(diff["Updated"] as JObject, currentPath, JToken.FromObject(value2));
                            Debug.Log($"Updated: {currentPath} = {value2}");
                        }
                        else if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
                        {
                            var collectionDiff = CompareCollections(value1 as IEnumerable, value2 as IEnumerable);
                            foreach (var kvp in collectionDiff)
                            {
                                if (kvp.Value.HasValues)
                                {
                                    JsonHelper.AddToDiff(diff[kvp.Key] as JObject, currentPath, kvp.Value);
                                    Debug.Log($"{kvp.Key}: {currentPath} = {kvp.Value}");
                                }
                            }
                        }
                        else
                        {
                            var nestedDiff = new JObject
                            {
                                ["Added"] = new JObject(),
                                ["Updated"] = new JObject(),
                                ["Removed"] = new JObject()
                            };
                            CompareProperties(value1, value2, currentPath, nestedDiff, false);
                            JsonHelper.MergeDiff(diff, nestedDiff);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error comparing property {prop.Name}: {ex.Message}");
                }
            }
        }

        private static JObject CompareCollections(IEnumerable collection1, IEnumerable collection2)
        {
            var list1 = collection1 != null ? new List<object>(TypeHelper.EnumerableToList(collection1)) : new List<object>();
            var list2 = collection2 != null ? new List<object>(TypeHelper.EnumerableToList(collection2)) : new List<object>();

            var result = new JObject
            {
                ["Added"] = new JObject(),
                ["Updated"] = new JObject(),
                ["Removed"] = new JObject()
            };

            for (int i = 0; i < Math.Max(list1.Count, list2.Count); i++)
            {
                if (i >= list1.Count)
                {
                    ((JObject)result["Added"])[i.ToString()] = JToken.FromObject(list2[i]);
                }
                else if (i >= list2.Count)
                {
                    ((JObject)result["Removed"])[i.ToString()] = JToken.FromObject(list1[i]);
                }
                else if (!TypeHelper.SafeEquals(list1[i], list2[i]))
                {
                    ((JObject)result["Updated"])[i.ToString()] = JToken.FromObject(list2[i]);
                }
            }

            return result;
        }
    }

    private static class JsonRestructurer
    {
        public static JObject Restructure(JObject diff)
        {
            foreach (var category in diff.Properties())
            {
                if (category.Value is JObject categoryObj)
                {
                    RestructureObjectRecursively(categoryObj);
                }
            }
            return diff;
        }

        private static void RestructureObjectRecursively(JObject obj)
        {
            var propsToModify = new List<(string, JToken)>();

            foreach (var prop in obj.Properties().ToList())
            {
                if (prop.Value is JObject childObj)
                {
                    RestructureObjectRecursively(childObj);

                    if (IsCoreDataObject(childObj))
                    {
                        propsToModify.Add((prop.Name, RestructureCoreDataObject(childObj)));
                    }
                }
                else if (prop.Value is JArray arrayValue)
                {
                    for (int i = 0; i < arrayValue.Count; i++)
                    {
                        if (arrayValue[i] is JObject arrayObj)
                        {
                            RestructureObjectRecursively(arrayObj);
                        }
                    }
                }
            }

            foreach (var (propName, newValue) in propsToModify)
            {
                obj[propName] = newValue;
            }
        }

        private static bool IsCoreDataObject(JObject obj)
        {
            return obj.Properties().Any(p => p.Value is JObject && ((JObject)p.Value).ContainsKey("Key") && ((JObject)p.Value).ContainsKey("Value"));
        }

        private static JObject RestructureCoreDataObject(JObject coreDataObj)
        {
            var result = new JObject();
            foreach (var kvp in coreDataObj)
            {
                if (kvp.Value is JObject itemObj && itemObj.ContainsKey("Key") && itemObj.ContainsKey("Value"))
                {
                    var key = itemObj["Key"].ToString();
                    result[key] = itemObj["Value"];
                }
            }
            return result;
        }
    }

    private static class JsonHelper
    {
        public static void AddToDiff(JObject diff, string path, JToken value)
        {
            if (diff == null) return;

            var pathParts = path.Split('.');
            var current = diff;

            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                if (!current.ContainsKey(pathParts[i]))
                {
                    current[pathParts[i]] = new JObject();
                }
                current = current[pathParts[i]] as JObject;
                if (current == null)
                {
                    Debug.LogWarning($"Unexpected null object at path: {string.Join(".", pathParts.Take(i + 1))}");
                    return;
                }
            }

            current[pathParts[pathParts.Length - 1]] = value;
        }

        public static void RemoveEmptyObjects(JToken token)
        {
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                var propsToRemove = obj.Properties()
                    .Where(p => (p.Value.Type == JTokenType.Object && !((JObject)p.Value).HasValues) ||
                                (p.Value.Type == JTokenType.Array && !((JArray)p.Value).HasValues))
                    .Select(p => p.Name)
                    .ToList();

                foreach (var prop in propsToRemove)
                {
                    obj.Remove(prop);
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                var array = (JArray)token;
                for (int i = array.Count - 1; i >= 0; i--)
                {
                    RemoveEmptyObjects(array[i]);
                    if ((array[i].Type == JTokenType.Object && !((JObject)array[i]).HasValues) ||
                        (array[i].Type == JTokenType.Array && !((JArray)array[i]).HasValues))
                    {
                        array.RemoveAt(i);
                    }
                }
            }
        }

        public static void MergeDiff(JObject mainDiff, JObject nestedDiff)
        {
            foreach (var kvp in nestedDiff)
            {
                if (kvp.Value.HasValues)
                {
                    if (!mainDiff.ContainsKey(kvp.Key))
                    {
                        mainDiff[kvp.Key] = new JObject();
                    }
                    MergeObjects(mainDiff[kvp.Key] as JObject, kvp.Value as JObject);
                }
            }
        }

        private static void MergeObjects(JObject target, JObject source)
        {
            if (target == null || source == null) return;

            foreach (var kvp in source)
            {
                if (!target.ContainsKey(kvp.Key))
                {
                    target[kvp.Key] = kvp.Value;
                }
                else if (kvp.Value.Type == JTokenType.Object)
                {
                    MergeObjects(target[kvp.Key] as JObject, kvp.Value as JObject);
                }
            }
        }

        public static JObject AddClassNameAsTopLevelKey(JObject diff, string className)
        {
            var result = new JObject();
            foreach (var kvp in diff)
            {
                if (kvp.Value.HasValues)
                {
                    result[kvp.Key] = new JObject
                    {
                        [className] = kvp.Value
                    };
                }
            }
            return result;
        }
    }

    private static class TypeHelper
    {
        public static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime);
        }

        public static bool SafeEquals(object obj1, object obj2)
        {
            if (obj1 == null && obj2 == null)
                return true;
            if (obj1 == null || obj2 == null)
                return false;
            if (obj1.GetType() != obj2.GetType())
                return false;

            if (IsSimpleType(obj1.GetType()))
                return Equals(obj1, obj2);

            try
            {
                return JsonConvert.SerializeObject(obj1) == JsonConvert.SerializeObject(obj2);
            }
            catch
            {
                return false;
            }
        }

        public static string GetCurrentPath(string path, string propName, bool isTopLevel)
        {
            return isTopLevel ? propName : $"{path}.{propName}";
        }

        public static IEnumerable<object> EnumerableToList(IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                yield return item;
            }
        }
    }
}