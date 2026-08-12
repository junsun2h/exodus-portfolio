using System;
using System.Linq;
using UnityEngine;

namespace PX
{
    public class CodeGenerator_GameDB_Client_ParseJsonNode : CodeGeneratorBase
    {
        public CodeGenerator_GameDB_Client_ParseJsonNode(string InFileName, string InOutputPathCS, bool isDebug)
        {
            OutputLanguage = CodeGeneratorBase.OutputLanguageType.CSharp;
            generatorCS = new GenerateCodeWriter_GameDB_Client_ParseJsonNode(InFileName, InOutputPathCS, isDebug);
        }

        public override string GenerateTry()
        {
            // CommonCoreData 상속 받은 모든 클래스별로 프로퍼티의 Type 수집.
            GenerateCodeContext context = CreteCodeContext(typeof(GameDBData_Client));
            Debug.Assert(context.Types.Count > 0);
            Debug.Assert(context.Enums.Count > 0);

            // 문자열로 출력
            generatorCS.WriteToStringBuilder(context);

            generatorCS.WriteTempFile();
            bool IsNew = generatorCS.ReplaceWhenDiff();

            return DisplayResult(IsNew);
        }

        public override void FillMemberInfo(MemberDefinition member, Type type, GenerateCodeContext context)
        {
            if (type.IsEnum)
            {
                member.ParseJsonNode = $"{member.Name} = ({type.Name})InNode.AsInt;";
                return;
            }

            // https://jyclb.tistory.com/9
            TypeCode typeCode = Type.GetTypeCode(type);
            if (typeCode != TypeCode.Object)
            {
                switch (typeCode)
                {
                    case TypeCode.Boolean:
                        member.ParseJsonNode = $"{member.Name} = CryptoValueBool.Create(InNode.AsBool);";
                        member.MapValue = $".AsBool";
                        return;
                    case TypeCode.Decimal:
                    case TypeCode.Double:
                    case TypeCode.Single:
                        member.ParseJsonNode = $"{member.Name} = CryptoValueDouble.Create(InNode.AsDouble);";
                        member.MapValue = $".AsDouble";
                        return;
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        member.ParseJsonNode = $"{member.Name} = CryptoValueInt.Create(InNode.AsInt);";
                        member.MapValue = $".AsInt";
                        return;
                    case TypeCode.Char:
                    case TypeCode.String:
                        member.ParseJsonNode = $"{member.Name} = InNode;";
                        member.MapValue = $"";
                        return;
                    default:
                        Debug.Assert(true, "Unidentified Type!");
                        return;
                }
            }

            static string GetAsValue(Type InType)
            {
                TypeCode code = Type.GetTypeCode(InType);
                switch (code)
                {
                    case TypeCode.Boolean:
                        return $".AsBool";
                    case TypeCode.Byte:
                    case TypeCode.Decimal:
                    case TypeCode.Double:
                        return $".AsDouble";
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                        return $".AsInt";
                    case TypeCode.Int64:
                        return $".AsLong";
                    case TypeCode.SByte:
                    case TypeCode.Single:
                        return $".AsFloat";
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                        return $".AsUInt";
                    case TypeCode.UInt64:
                        return $".AsULong";
                    case TypeCode.Char:
                    case TypeCode.String:
                        return $"";
                    default:
                        return "UNKNOWN";
                }
            }

            // Map
            Type dictionaryType = IsClosedDictionaryType(type) ? type : type.GetInterfaces().FirstOrDefault(IsClosedDictionaryType);
            if (dictionaryType != null)
            {
                member.IsMapOrArray = true;
                string keyType = GetTypeRef(dictionaryType.GetGenericArguments().ElementAt(0), context);
                string valueType = GetTypeRef(dictionaryType.GetGenericArguments().ElementAt(1), context);
                string mapKeyToAsValue = GetAsValue(dictionaryType.GetGenericArguments().ElementAt(0));
                string mapValueToAsValue = GetAsValue(dictionaryType.GetGenericArguments().ElementAt(1));
                bool isValueObjectType = Type.GetTypeCode(dictionaryType.GetGenericArguments().ElementAt(1)) == TypeCode.Object;

                DictionaryEnumKey dictionaryEnumKey = member.GetCustomAttribute<DictionaryEnumKey>();
                if (dictionaryEnumKey != null && keyType == "string")
                {
                    // ex) GameDBEquipment.cs
                    // [DictionaryEnumKey(typeof(EEquipmentMythic))]
                    // Map 의 key 값을 커스텀 어트리뷰트의 DictionaryEnumKey 타입으로 바꿔준다.
                    CreateEnumContext(dictionaryEnumKey.keyType, context);
                    keyType = GetTypeRef(dictionaryEnumKey.keyType, context);
                    member.Type = $"Dictionary<{keyType}, {valueType}>";
                }

                string conditionalSentence;
                // value 타입이 object 일 때와 일반 타입일 때를 구분해서 작성한다.
                if (isValueObjectType)
                {
                    // ModValue
                    conditionalSentence = valueType == "ModValue"
                        ? $"{INTEND7}var newObject = GameObjectPoolManager.Instance.GetPoolModValue();\n" +
                            $"{INTEND7}newObject.FromJson(parseJson[keyData]);\n" +
                            $"{INTEND7}{member.Name}.Add(keyType, newObject);\n"
                        : $"{INTEND7}var newObject = new {valueType}();\n" +
                            $"{INTEND7}newObject.FromJson(parseJson[keyData]);\n" +
                            $"{INTEND7}{member.Name}.Add(keyType, newObject);\n";
                }
                else
                {
                    conditionalSentence = $"{INTEND7}{member.Name}.Add(keyType, parseJson[keyData]{mapValueToAsValue});\n";
                }

                if (keyType == "string")
                {
                    member.ParseJsonNode = $"JSONNode parseJson = InNode;\n" +
                        $"{INTEND6}JSONNode.KeyEnumerator keyEnumerator = parseJson.Keys.GetEnumerator();\n" +
                        $"\n" +
                        $"{INTEND6}{member.Name} = new Dictionary<{keyType}, {valueType}>();\n" +
                        $"{INTEND6}while (keyEnumerator.MoveNext())\n" +
                        $"{INTEND6}{{\n" +
                        $"{INTEND7}string keyData = keyEnumerator.Current;\n" +
                        $"{INTEND7}{keyType} keyType = ({keyType})(keyData);\n" +
                        $"{conditionalSentence}" +
                        $"{INTEND6}}}";
                }
                else // dictionaryEnumKey 을 사용하는 타입
                {
                    member.ParseJsonNode = $"JSONNode parseJson = InNode;\n" +
                        $"{INTEND6}JSONNode.KeyEnumerator keyEnumerator = parseJson.Keys.GetEnumerator();\n" +
                        $"\n" +
                        $"{INTEND6}{member.Name} = new Dictionary<{keyType}, {valueType}>();\n" +
                        $"{INTEND6}while (keyEnumerator.MoveNext())\n" +
                        $"{INTEND6}{{\n" +
                        $"{INTEND7}string keyData = keyEnumerator.Current;\n" +
                        $"{INTEND7}{keyType} keyType = ({keyType})int.Parse(keyData);\n" +
                        $"{conditionalSentence}" +
                        $"{INTEND6}}}";
                }

                return;
            }

            // Array
            Type enumerable = IsClosedEnumerableType(type) ? type : type.GetInterfaces().FirstOrDefault(IsClosedEnumerableType);
            if (enumerable != null)
            {
                member.IsMapOrArray = true;
                string valueType = GetTypeRef(enumerable.GetGenericArguments().ElementAt(0), context);
                string asValue = GetAsValue(enumerable.GetGenericArguments().ElementAt(0));
                bool isValueObjectType = Type.GetTypeCode(enumerable.GetGenericArguments().ElementAt(0)) == TypeCode.Object;

                string conditionalSentence;
                // value 타입이 object 일 때와 일반 타입일 때를 구분해서 작성한다.
                if (isValueObjectType)
                {
                    // ModValue
                    conditionalSentence = valueType == "ModValue"
                        ? $"{INTEND7}var newObject = GameObjectPoolManager.Instance.GetPoolModValue();\n" +
                        $"{INTEND7}newObject.FromJson(enumerator.Current.Value);\n" +
                        $"{INTEND7}{member.Name}.Add(newObject);\n"
                        : $"{INTEND7}var newObject = new {valueType}();\n" +
                        $"{INTEND7}newObject.FromJson(enumerator.Current.Value);\n" +
                        $"{INTEND7}{member.Name}.Add(newObject);\n";
                }
                else
                {
                    conditionalSentence = $"{INTEND7}{member.Name}.Add(({valueType})enumerator.Current.Value{asValue});\n";
                }

                member.ParseJsonNode = $"JSONArray parseArr = InNode.AsArray;\n" +
                $"{INTEND6}JSONNode.Enumerator enumerator = parseArr.GetEnumerator();\n" +
                $"\n" +
                $"{INTEND6}{member.Name} = new List<{valueType}>();\n" +
                $"{INTEND6}while (enumerator.MoveNext())\n" +
                $"{INTEND6}{{\n" +
                $"{conditionalSentence}" +
                $"{INTEND6}}}";
                return;
            }

            // ModValue
            if (type.IsClass && type.Name == "ModValue")
            {
                member.ParseJsonNode = $"var newObject = GameObjectPoolManager.Instance.GetPoolModValue();\n" +
                $"{INTEND6}newObject.FromJson(InNode);\n" +
                $"{INTEND6}{member.Name} = newObject;";
                return;
            }

            // CryptoValue 추가
            if (type.IsValueType && type.Name.StartsWith("CryptoValue"))
            {
                switch (type.Name)
                {
                    case "CryptoValueBool":
                        member.ParseJsonNode = $"{member.Name} = CryptoValueBool.Create(InNode.AsBool);";
                        break;
                    case "CryptoValueInt":
                        member.ParseJsonNode = $"{member.Name} = CryptoValueInt.Create(InNode.AsInt);";
                        break;
                    case "CryptoValueDouble":
                    case "CryptoValueFloat":
                        member.ParseJsonNode = $"{member.Name} = CryptoValueDouble.Create(InNode.AsDouble);";
                        break;
                    default:
                        member.ParseJsonNode = "ERROR;";
                        break;
                }

                return;
            }

            // class, struct
            member.ParseJsonNode = $"var newObject = new {type.Name}();\n" +
                    $"{INTEND6}newObject.FromJson(InNode);\n" +
                    $"{INTEND6}{member.Name} = newObject;";
        }
    }

    public class GenerateCodeWriter_GameDB_Client_ParseJsonNode : GenerateCodeWriter
    {
        public GenerateCodeWriter_GameDB_Client_ParseJsonNode(string InFileName, string InOutputPath, bool isDebug)
        {
            Initialize(InFileName, ".cs", InOutputPath, isDebug);
        }

        public override bool WriteToStringBuilder(GenerateCodeContext context)
        {
            GenerateAutoGeneratedComment("CodeGenerator_GameDB_Client_ParseJsonNode.cs");

            // import
            sb.AppendLine($"using SimpleJSON;");
            sb.AppendLine($"using System.Collections.Generic;");
            sb.AppendLine($"using UnityEngine;");
            sb.AppendLine();

            // namespace {
            sb.AppendLine($"namespace PX");
            sb.AppendLine($"{{");

            // class
            foreach (TypeDefinition t in context.Types)
            {
                if (HasExcludeAttribute(t.ClrType)) // [ExcludeCodeGenerate]
                    continue;

                if (t.ClrType.IsInterface || t.ClrType.IsAbstract)
                    continue;

                sb.AppendLine($"{INTEND1}{t.Declaration}"); // class start
                sb.AppendLine($"{INTEND1}{{");

                // ParseJsonNode()
                {
                    sb.AppendLine($"{INTEND2}public override bool ParseJsonNode(string InDataKey, JSONNode InNode)");
                    sb.AppendLine($"{INTEND2}{{");

                    sb.AppendLine($"{INTEND3}if (InNode == null)");
                    sb.AppendLine($"{INTEND4}return true;");

                    sb.AppendLine();

                    // switch
                    {
                        sb.AppendLine($"{INTEND3}switch (InDataKey)");
                        sb.AppendLine($"{INTEND3}{{");

                        // base class members
                        SwitchCaseBaseTypeRecursively(t, context);

                        foreach (MemberDefinition m in t.Members)
                        {
                            sb.AppendLine($"{INTEND4}case \"{m.Name}\":");
                            sb.AppendLine($"{INTEND5}{{");
                            sb.AppendLine($"{INTEND6}{m.ParseJsonNode}");
                            sb.AppendLine($"{INTEND5}}}");
                            sb.AppendLine($"{INTEND5}break;");
                        }

                        //if (t.ClrType.BaseType == typeof(CommonCoreData))
                        {
                            sb.AppendLine($"{INTEND4}default:");
                            sb.AppendLine($"{INTEND5}{{");
                            sb.AppendLine($"{INTEND6}Debug.LogError($\"ParseJsonNode TypeError, {t.Name}, InDataKey = {{InDataKey}}\");");
                            sb.AppendLine($"{INTEND6}return false;");
                            sb.AppendLine($"{INTEND5}}}");
                        }

                        sb.AppendLine($"{INTEND3}}}");
                        sb.AppendLine();
                        sb.AppendLine($"{INTEND3}return true;");
                    }

                    sb.AppendLine($"{INTEND2}}}");
                }

                sb.AppendLine($"{INTEND1}}}"); // class end
                sb.AppendLine();
            }

            // namespace }
            sb.AppendLine($"}}");

            sb.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

            return true;
        }

        private void SwitchCaseBaseTypeRecursively(TypeDefinition typeDef, GenerateCodeContext context)
        {
            Type baseType = typeDef.ClrType.BaseType;
            TypeDefinition baseTypeDef = context.Types.FirstOrDefault(t => t.ClrType == baseType);
            if (baseTypeDef == null) return;

            SwitchCaseBaseTypeRecursively(baseTypeDef, context);

            foreach (MemberDefinition m in baseTypeDef.Members)
            {
                sb.AppendLine($"{INTEND4}case \"{m.Name}\":");
                sb.AppendLine($"{INTEND5}{{");
                sb.AppendLine($"{INTEND6}{m.ParseJsonNode}");
                sb.AppendLine($"{INTEND6}return true;");
                sb.AppendLine($"{INTEND5}}}");
            }
        }
    }
}
