using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// 스키마 필드 정보
    /// </summary>
    [Serializable]
    public class SchemaField
    {
        public string Name;
        public string CSharpType;
        public string TypeScriptType;
        public bool IsObject;
        public bool IsMap;
        public bool IsArray;
        public string KeyType;      // Map인 경우 키 타입
        public string ValueType;    // Map/Array인 경우 값 타입
        public string DefaultValue;

        // 중첩 CommonCoreData 관련
        public bool IsNestedCoreData;                               // 내부 CommonCoreData 클래스인지
        public string NestedTypeName;                               // 내부 클래스 타입명 (예: AccountLoginInfo)
        public Dictionary<string, SchemaField> NestedFields;        // 내부 클래스의 필드들 (변경 감지용)
    }

    /// <summary>
    /// CoreData 스키마 정보
    /// </summary>
    [Serializable]
    public class CoreDataSchema
    {
        public string Name;
        public string ClassName;
        public string Version;
        public Dictionary<string, SchemaField> Fields = new Dictionary<string, SchemaField>();
    }

    /// <summary>
    /// 스키마 변경 타입
    /// </summary>
    public enum SchemaChangeType
    {
        FieldAdded,
        FieldRemoved,
        FieldTypeChanged,
        FieldRenamed,
        // 중첩 필드 변경
        NestedFieldAdded,
        NestedFieldRemoved,
        NestedFieldTypeChanged,
        NestedFieldRenamed,
    }

    /// <summary>
    /// 스키마 변경 정보
    /// </summary>
    [Serializable]
    public class SchemaChange
    {
        public SchemaChangeType Type;
        public string FieldName;
        public string OldFieldName;     // Renamed인 경우
        public string OldType;
        public string NewType;
        public string DefaultValue;

        // 중첩 필드 관련
        public string ParentFieldName;  // 부모 필드명 (예: "LoginInfo")
        public string NestedFieldPath;  // 전체 경로 (예: "LoginInfo.Version")
    }

    /// <summary>
    /// 스키마 버전 기록
    /// </summary>
    [Serializable]
    public class SchemaVersion
    {
        public string Version;
        public string Timestamp;
        public CoreDataSchema Schema;
        public List<SchemaChange> Changes;
    }

    /// <summary>
    /// 스키마 히스토리 전체
    /// </summary>
    [Serializable]
    public class SchemaHistory
    {
        public string CoreDataName;
        public string CurrentVersion;
        public List<SchemaVersion> Versions = new List<SchemaVersion>();
    }

    /// <summary>
    /// C# CoreData에서 스키마 정보를 추출하는 클래스
    /// </summary>
    public class CoreDataSchemaExtractor
    {
        private static readonly BindingFlags BindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // GlobalValue.ts 파일 경로 (CoreDataKey 정의가 있는 파일)
        private static readonly string GlobalValueTsPath = Path.Combine(
            Application.dataPath, "..", "FirebaseCLI", "functions", "src", "Data", "GlobalValue.ts");

        // CoreDataKey 값 캐시 (한 번 파싱 후 재사용)
        private static HashSet<string> _cachedCoreDataKeys = null;

        // C# 클래스 이름에서 TypeScript CoreDataKey 값으로 매핑
        // GlobalValue.ts의 CoreDataKey와 일치해야 함
        // C# 클래스명이 모두 복수형으로 통일되어 매핑 불필요
        private static readonly Dictionary<string, string> CSharpToTypeScriptKeyMap = new Dictionary<string, string>
        {
            // 모든 C# CoreData 클래스명이 TypeScript CoreDataKey와 일치하도록 변경 완료
            // Achievement -> Achievements, Collection -> Collections,
            // Constellation -> Constellations, Content -> Contents
        };

        private readonly string schemaHistoryPath;

        public CoreDataSchemaExtractor(string schemaHistoryPath)
        {
            this.schemaHistoryPath = schemaHistoryPath;
        }

        /// <summary>
        /// 모든 CoreData 클래스에서 스키마 추출
        /// </summary>
        public Dictionary<string, CoreDataSchema> ExtractAllSchemas()
        {
            var schemas = new Dictionary<string, CoreDataSchema>();

            // CommonCoreData 상속받은 모든 클래스 찾기
            IEnumerable<Type> coreDataTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => typeof(CommonCoreData).IsAssignableFrom(p) && p.IsClass && !p.IsAbstract);

            foreach (Type type in coreDataTypes)
            {
                // CoreData_ 접두사가 있는 최상위 클래스만 처리
                if (!type.Name.StartsWith("CoreData_"))
                    continue;

                // 중첩 클래스 제외 (예: CoreData_PlayerGrowth 는 CoreData_Player의 서브타입)
                if (IsSubCoreData(type))
                    continue;

                string coreDataName = GetCoreDataName(type.Name);
                CoreDataSchema schema = ExtractSchema(type, coreDataName);
                schemas[coreDataName] = schema;
            }

            return schemas;
        }

        /// <summary>
        /// 서브 CoreData인지 확인 (예: CoreData_PlayerGrowth)
        /// GlobalValue.ts의 CoreDataKey에 정의된 값만 메인 CoreData로 인식
        /// </summary>
        private bool IsSubCoreData(Type type)
        {
            // CoreData_{Name} 형식의 최상위만 남기고 나머지는 서브로 취급
            string name = type.Name;
            if (!name.StartsWith("CoreData_"))
                return true;

            // GlobalValue.ts에서 CoreDataKey 값들을 자동으로 파싱
            HashSet<string> mainCoreDataNames = GetCoreDataKeysFromGlobalValue();

            string extractedName = name.Substring("CoreData_".Length);
            return !mainCoreDataNames.Contains(extractedName);
        }

        /// <summary>
        /// GlobalValue.ts 파일에서 CoreDataKey 값들을 파싱하여 반환
        /// 수동 입력 없이 TypeScript 파일을 단일 소스로 사용
        /// </summary>
        private HashSet<string> GetCoreDataKeysFromGlobalValue()
        {
            // 캐시가 있으면 반환
            if (_cachedCoreDataKeys != null)
                return _cachedCoreDataKeys;

            _cachedCoreDataKeys = new HashSet<string>();

            try
            {
                if (!File.Exists(GlobalValueTsPath))
                {
                    Debug.LogError($"[CoreDataSchemaExtractor] GlobalValue.ts 파일을 찾을 수 없습니다: {GlobalValueTsPath}");
                    return _cachedCoreDataKeys;
                }

                string content = File.ReadAllText(GlobalValueTsPath);

                // CoreDataKey 객체 블록 추출
                // export const CoreDataKey = { ... } as const; 패턴 매칭
                var coreDataKeyMatch = Regex.Match(content,
                    @"export\s+const\s+CoreDataKey\s*=\s*\{([^}]+)\}\s*as\s+const",
                    RegexOptions.Singleline);

                if (!coreDataKeyMatch.Success)
                {
                    Debug.LogError("[CoreDataSchemaExtractor] GlobalValue.ts에서 CoreDataKey 정의를 찾을 수 없습니다.");
                    return _cachedCoreDataKeys;
                }

                string coreDataKeyBlock = coreDataKeyMatch.Groups[1].Value;

                // 각 라인에서 값 추출: COREDATAKEY_XXX: "Value" 패턴
                var valueMatches = Regex.Matches(coreDataKeyBlock, @":\s*""([^""]+)""");

                foreach (Match match in valueMatches)
                {
                    string value = match.Groups[1].Value;
                    _cachedCoreDataKeys.Add(value);
                }

                Debug.Log($"[CoreDataSchemaExtractor] GlobalValue.ts에서 {_cachedCoreDataKeys.Count}개의 CoreDataKey 값을 로드했습니다: {string.Join(", ", _cachedCoreDataKeys)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CoreDataSchemaExtractor] GlobalValue.ts 파싱 실패: {ex.Message}");
            }

            return _cachedCoreDataKeys;
        }

        /// <summary>
        /// 클래스 이름에서 CoreData 이름 추출 (TypeScript CoreDataKey 값으로 매핑)
        /// </summary>
        private string GetCoreDataName(string className)
        {
            // CoreData_Player -> Player
            string extractedName = className;
            if (className.StartsWith("CoreData_"))
                extractedName = className.Substring("CoreData_".Length);

            // C# 클래스 이름을 TypeScript CoreDataKey 값으로 매핑
            if (CSharpToTypeScriptKeyMap.TryGetValue(extractedName, out string mappedName))
                return mappedName;

            return extractedName;
        }

        /// <summary>
        /// 특정 타입에서 스키마 추출
        /// </summary>
        public CoreDataSchema ExtractSchema(Type type, string coreDataName)
        {
            var schema = new CoreDataSchema
            {
                Name = coreDataName,
                ClassName = type.Name,
                Version = "0.0.1"  // 기본 버전
            };

            // 현재 히스토리에서 버전 읽기
            string historyPath = Path.Combine(schemaHistoryPath, $"{coreDataName}_schema_history.json");
            if (File.Exists(historyPath))
            {
                try
                {
                    string json = File.ReadAllText(historyPath);
                    var history = JsonConvert.DeserializeObject<SchemaHistory>(json);
                    schema.Version = history?.CurrentVersion ?? "0.0.1";
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"스키마 히스토리 읽기 실패: {historyPath}, {ex.Message}");
                }
            }

            // 필드 추출
            ExtractFields(type, schema);

            return schema;
        }

        /// <summary>
        /// 타입의 필드/프로퍼티 추출
        /// </summary>
        private void ExtractFields(Type type, CoreDataSchema schema)
        {
            var visitedTypes = new HashSet<Type>();

            // 필드
            foreach (FieldInfo field in type.GetFields(BindingFlags))
            {
                if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                    continue;

                SchemaField schemaField = CreateSchemaFieldWithNested(field.Name, field.FieldType, visitedTypes);
                if (schemaField != null)
                    schema.Fields[field.Name] = schemaField;
            }

            // 프로퍼티
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags))
            {
                // JsonIgnore 속성 체크
                if (Attribute.GetCustomAttribute(prop, typeof(Newtonsoft.Json.JsonIgnoreAttribute)) != null)
                    continue;
                if (Attribute.GetCustomAttribute(prop, typeof(ExcludeCodeGenerateProperty)) != null)
                    continue;

                SchemaField schemaField = CreateSchemaFieldWithNested(prop.Name, prop.PropertyType, visitedTypes);
                if (schemaField != null)
                    schema.Fields[prop.Name] = schemaField;
            }
        }

        /// <summary>
        /// 중첩 타입 포함하여 스키마 필드 생성
        /// </summary>
        private SchemaField CreateSchemaFieldWithNested(string name, Type memberType, HashSet<Type> visitedTypes)
        {
            // 내부 CommonCoreData 클래스인 경우
            if (IsNestedCommonCoreData(memberType))
            {
                // 순환 참조 방지
                if (visitedTypes.Contains(memberType))
                    return CreateSchemaField(name, memberType);

                visitedTypes.Add(memberType);

                var nestedFields = new Dictionary<string, SchemaField>();

                // 내부 클래스의 필드 추출
                foreach (FieldInfo field in memberType.GetFields(BindingFlags))
                {
                    if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                        continue;

                    SchemaField nestedField = CreateSchemaFieldWithNested(field.Name, field.FieldType, new HashSet<Type>(visitedTypes));
                    if (nestedField != null)
                        nestedFields[field.Name] = nestedField;
                }

                // 내부 클래스의 프로퍼티 추출
                foreach (PropertyInfo prop in memberType.GetProperties(BindingFlags))
                {
                    if (Attribute.GetCustomAttribute(prop, typeof(Newtonsoft.Json.JsonIgnoreAttribute)) != null)
                        continue;
                    if (Attribute.GetCustomAttribute(prop, typeof(ExcludeCodeGenerateProperty)) != null)
                        continue;

                    SchemaField nestedField = CreateSchemaFieldWithNested(prop.Name, prop.PropertyType, new HashSet<Type>(visitedTypes));
                    if (nestedField != null)
                        nestedFields[prop.Name] = nestedField;
                }

                // 중첩 CoreData 필드 생성
                var schemaField = new SchemaField
                {
                    Name = name,
                    CSharpType = memberType.Name,
                    TypeScriptType = memberType.Name,
                    IsObject = true,
                    IsNestedCoreData = true,
                    NestedTypeName = memberType.Name,
                    NestedFields = nestedFields,
                    DefaultValue = $"new {memberType.Name}()"
                };

                return schemaField;
            }
            else
            {
                // 일반 필드
                return CreateSchemaField(name, memberType);
            }
        }

        /// <summary>
        /// 내부 CommonCoreData 클래스인지 확인
        /// CoreData_ 접두사가 없고 CommonCoreData를 상속받는 클래스
        /// </summary>
        private bool IsNestedCommonCoreData(Type type)
        {
            if (type == null || type.IsAbstract)
                return false;

            // CommonCoreData를 상속받아야 함
            if (!typeof(CommonCoreData).IsAssignableFrom(type))
                return false;

            // CoreData_ 접두사가 있으면 최상위 CoreData
            if (type.Name.StartsWith("CoreData_"))
                return false;

            // 기본 타입 제외
            if (type == typeof(CommonCoreData))
                return false;

            return true;
        }

        /// <summary>
        /// 필드 정보 생성
        /// </summary>
        private SchemaField CreateSchemaField(string name, Type type)
        {
            var field = new SchemaField
            {
                Name = name,
                CSharpType = GetCSharpTypeName(type),
                TypeScriptType = GetTypeScriptTypeName(type),
                IsObject = Type.GetTypeCode(type) == TypeCode.Object,
            };

            // Nullable 처리
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                type = type.GetGenericArguments()[0];
            }

            // Dictionary/Map 처리
            if (IsDictionaryType(type))
            {
                field.IsMap = true;
                Type keyType = type.GetGenericArguments()[0];
                Type valueType = type.GetGenericArguments()[1];
                field.KeyType = GetTypeScriptTypeName(keyType);
                field.ValueType = GetTypeScriptTypeName(valueType);
                field.DefaultValue = $"new Map<{field.KeyType}, {field.ValueType}>()";
            }
            // List/Array 처리
            else if (IsEnumerableType(type))
            {
                field.IsArray = true;
                Type elementType = type.GetGenericArguments().FirstOrDefault() ?? type.GetElementType();
                field.ValueType = GetTypeScriptTypeName(elementType);
                field.DefaultValue = $"new Array<{field.ValueType}>()";
            }
            // 기본 타입
            else
            {
                field.DefaultValue = GetDefaultValue(type, field.TypeScriptType);
            }

            return field;
        }

        private bool IsDictionaryType(Type type)
        {
            if (!type.IsGenericType) return false;
            Type genericDef = type.GetGenericTypeDefinition();
            return genericDef == typeof(Dictionary<,>) ||
                   genericDef == typeof(IDictionary<,>) ||
                   genericDef == typeof(IReadOnlyDictionary<,>);
        }

        private bool IsEnumerableType(Type type)
        {
            if (!type.IsGenericType) return false;
            Type genericDef = type.GetGenericTypeDefinition();
            return genericDef == typeof(List<>) ||
                   genericDef == typeof(IEnumerable<>) ||
                   genericDef == typeof(IList<>) ||
                   genericDef == typeof(ICollection<>);
        }

        private string GetCSharpTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                string genericName = type.GetGenericTypeDefinition().Name;
                genericName = genericName.Substring(0, genericName.IndexOf('`'));
                string args = string.Join(", ", type.GetGenericArguments().Select(GetCSharpTypeName));
                return $"{genericName}<{args}>";
            }

            TypeCode typeCode = Type.GetTypeCode(type);
            switch (typeCode)
            {
                case TypeCode.Boolean: return "bool";
                case TypeCode.Int32: return "int";
                case TypeCode.Int64: return "long";
                case TypeCode.Single: return "float";
                case TypeCode.Double: return "double";
                case TypeCode.String: return "string";
                default: return type.Name;
            }
        }

        private string GetTypeScriptTypeName(Type type)
        {
            if (type == null) return "any";

            if (type.IsGenericType)
            {
                Type genericDef = type.GetGenericTypeDefinition();

                if (genericDef == typeof(Dictionary<,>) ||
                    genericDef == typeof(IDictionary<,>) ||
                    genericDef == typeof(IReadOnlyDictionary<,>))
                {
                    string keyType = GetTypeScriptTypeName(type.GetGenericArguments()[0]);
                    string valueType = GetTypeScriptTypeName(type.GetGenericArguments()[1]);
                    return $"Map<{keyType}, {valueType}>";
                }

                if (genericDef == typeof(List<>) ||
                    genericDef == typeof(IEnumerable<>) ||
                    genericDef == typeof(IList<>) ||
                    genericDef == typeof(ICollection<>))
                {
                    string elementType = GetTypeScriptTypeName(type.GetGenericArguments()[0]);
                    return $"Array<{elementType}>";
                }
            }

            if (type.IsEnum)
                return type.Name;

            TypeCode typeCode = Type.GetTypeCode(type);
            switch (typeCode)
            {
                case TypeCode.Boolean: return "boolean";
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    return "number";
                case TypeCode.Char:
                case TypeCode.String:
                    return "string";
                default:
                    // CryptoValue -> ModValue 변환
                    string typeName = type.Name;
                    if (typeName.StartsWith("CryptoValue"))
                        return typeName.Replace("CryptoValue", "ModValue");
                    return typeName;
            }
        }

        private string GetDefaultValue(Type type, string tsType)
        {
            if (type.IsEnum)
                return $"{type.Name}.{Enum.GetName(type, 0)}";

            TypeCode typeCode = Type.GetTypeCode(type);
            switch (typeCode)
            {
                case TypeCode.Boolean: return "false";
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    return "0";
                case TypeCode.Char:
                case TypeCode.String:
                    return "\"\"";
                default:
                    return $"new {tsType}()";
            }
        }

        /// <summary>
        /// 스키마 히스토리 로드
        /// </summary>
        public SchemaHistory LoadSchemaHistory(string coreDataName)
        {
            string historyPath = Path.Combine(schemaHistoryPath, $"{coreDataName}_schema_history.json");

            if (!File.Exists(historyPath))
                return null;

            try
            {
                string json = File.ReadAllText(historyPath);
                return JsonConvert.DeserializeObject<SchemaHistory>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"스키마 히스토리 로드 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 스키마 히스토리 저장
        /// </summary>
        public void SaveSchemaHistory(SchemaHistory history)
        {
            string historyPath = Path.Combine(schemaHistoryPath, $"{history.CoreDataName}_schema_history.json");

            // 디렉토리 생성
            string directory = Path.GetDirectoryName(historyPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonConvert.SerializeObject(history, Formatting.Indented);
            File.WriteAllText(historyPath, json);

            Debug.Log($"스키마 히스토리 저장됨: {historyPath}");
        }

        /// <summary>
        /// 초기 스키마 히스토리 생성 (첫 실행 시)
        /// </summary>
        public SchemaHistory CreateInitialHistory(CoreDataSchema schema)
        {
            var history = new SchemaHistory
            {
                CoreDataName = schema.Name,
                CurrentVersion = "0.0.1",
                Versions = new List<SchemaVersion>
                {
                    new SchemaVersion
                    {
                        Version = "0.0.1",
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        Schema = schema,
                        Changes = null
                    }
                }
            };

            return history;
        }
    }
}
