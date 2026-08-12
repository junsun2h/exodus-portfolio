using System;
using System.Linq;
using UnityEngine;

namespace PX
{
    public class CodeGenerator_CoreData_ParseJsonNode : CodeGeneratorBase
    {
        public CodeGenerator_CoreData_ParseJsonNode(string InFileName, string InOutputPathCS, bool isDebug)
        {
            OutputLanguage = CodeGeneratorBase.OutputLanguageType.CSharp;
            generatorCS = new GenerateCodeWriter_CoreData_ParseJsonNode(InFileName, InOutputPathCS, isDebug);
        }

        public override string GenerateTry()
        {
            // CommonCoreData 상속 받은 모든 클래스별로 프로퍼티의 Type 수집.
            GenerateCodeContext context = CreteCodeContext(typeof(CommonCoreData));
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
                        member.ParseJsonNode = $"{member.Name} = InNode.AsBool;";
                        member.MapValue = $".AsBool";
                        return;
                    case TypeCode.Byte:
                    case TypeCode.Decimal:
                        member.ParseJsonNode = $"{member.Name} = InNode.AsFloat;";
                        member.MapValue = $".AsFloat";
                        return;
                    case TypeCode.Double:
                        member.ParseJsonNode = $"{member.Name} = InNode.AsDouble;";
                        member.MapValue = $".AsDouble";
                        return;
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                        member.ParseJsonNode = $"{member.Name} = InNode.AsInt;";
                        member.MapValue = $".AsInt";
                        return;
                    case TypeCode.Int64:
                        member.ParseJsonNode = $"{member.Name} = InNode.AsLong;";
                        member.MapValue = $".AsLong";
                        return;
                    case TypeCode.SByte:
                    case TypeCode.Single:
                        member.ParseJsonNode = $"{member.Name} = InNode.AsFloat;";
                        member.MapValue = $".AsFloat";
                        return;
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                        member.ParseJsonNode = $"{member.Name} = InNode.AsInt;";
                        member.MapValue = $".AsInt";
                        return;
                    case TypeCode.UInt64:
                        member.ParseJsonNode = $"{member.Name} = InNode.AsULong;";
                        member.MapValue = $".AsULong";
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

            string GetAsValue(Type InType)
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
            Type dictionaryType;
            if (IsClosedDictionaryType(type))
                dictionaryType = type;
            else
                dictionaryType = type.GetInterfaces().FirstOrDefault(IsClosedDictionaryType);
            if (dictionaryType != null)
            {
                member.IsMapOrArray = true;
                string keyType = GetTypeRef(dictionaryType.GetGenericArguments().ElementAt(0), context);
                bool isKeyTypeEnum = dictionaryType.GetGenericArguments().ElementAt(0).IsEnum;
                bool isKeyTypeInt = keyType == "int";

                string valueType = GetTypeRef(dictionaryType.GetGenericArguments().ElementAt(1), context);
                string asValue = GetAsValue(dictionaryType.GetGenericArguments().ElementAt(1));
                bool isValueEnum = dictionaryType.GetGenericArguments().ElementAt(1).IsEnum;
                bool isValueObjectType = Type.GetTypeCode(dictionaryType.GetGenericArguments().ElementAt(1)) == TypeCode.Object;

                string keyTypeParseSentence = "";
                if (isKeyTypeEnum)
                {
                    keyTypeParseSentence = $"{INTEND7}{keyType} keyType = ({keyType})int.Parse(keyData);\n";
                }
                else if (isKeyTypeInt)
                {
                    keyTypeParseSentence = $"{INTEND7}{keyType} keyType = int.Parse(keyData);\n";
                }
                else
                {
                    keyTypeParseSentence = $"{INTEND7}{keyType} keyType = keyData;\n";
                }

                string addedSentence = "";
                string updatedSentence = "";
                string deletedSentence = "";

                string addedObjectSentence = "";
                string updatedObjectSentence = "";
                string deletedObjectSentence = "";
                if (isValueObjectType)
                {
                    // Crypto
                    if (valueType == "CryptoValueInt" || valueType == "CryptoValueFloat" || valueType == "CryptoValueDouble" || valueType == "CryptoValueBool")
                    {
                        string parseSentence = $"var newObject = {valueType}.Create(parseJson[keyData]);\n";

                        addedObjectSentence += parseSentence;

                        updatedObjectSentence += parseSentence;
                    }
                    // ModValue
                    else if (valueType == "ModValue")
                    {
                        string parseSentence1 = $"var newObject = GameObjectPoolManager.Instance.GetPoolModValue();\n";
                        string parseSentence2 = $"newObject.FromJson(parseJson[keyData]);\n";

                        addedObjectSentence += $"{parseSentence1}{INTEND11}{parseSentence2}";

                        updatedObjectSentence += $"{parseSentence1}{INTEND10}{parseSentence2}";
                    }
                    else
                    {
                        addedObjectSentence += $"var newObject = new {valueType}();\n";
                        addedObjectSentence += $"{INTEND11}newObject.FromJson(parseJson[keyData], InChangeType);\n";

                        updatedObjectSentence += $"{member.Name}[keyType].FromJson(parseJson[keyData], InChangeType);";
                    }

                    deletedObjectSentence += $"if (parseJson[keyData].IsNull)\n";
                    deletedObjectSentence += $"{INTEND10}{{\n";
                    deletedObjectSentence += $"{INTEND11}{member.Name}.Remove(keyType);\n";
                    deletedObjectSentence += $"{INTEND10}}}\n";
                    deletedObjectSentence += $"{INTEND10}else\n";
                    deletedObjectSentence += $"{INTEND10}{{\n";
                    deletedObjectSentence += $"{INTEND11}{member.Name}[keyType].ChangedJsonNode(parseJson[keyData], InChangeType);\n";
                    deletedObjectSentence += $"{INTEND10}}}";
                }

                //Added
                {
                    // value 타입이 object, enum, 일반 타입일 때를 구분한다.
                    if (isValueObjectType)
                    {
                        addedSentence += $"{INTEND10}if ({member.Name}.ContainsKey(keyType))\n";
                        addedSentence += $"{INTEND10}{{\n";
                        addedSentence += $"{INTEND11}{member.Name}[keyType].ChangedJsonNode(parseJson[keyData], InChangeType);\n";
                        addedSentence += $"{INTEND10}}}\n";
                        addedSentence += $"{INTEND10}else\n";
                        addedSentence += $"{INTEND10}{{\n";
                        addedSentence += $"{INTEND11}{addedObjectSentence}";
                        addedSentence += $"{INTEND11}{member.Name}.Add(keyType, newObject);\n";
                        addedSentence += $"{INTEND10}}}";
                    }
                    else if (isValueEnum)
                    {
                        addedSentence += $"{INTEND10}{member.Name}.Add(keyType, ({valueType})int.Parse(parseJson[keyData]));";
                    }
                    else
                    {
                        addedSentence += $"{INTEND10}{member.Name}.Add(keyType, parseJson[keyData]{asValue});";
                    }
                }

                //Updated
                {
                    // value 타입이 object, enum, 일반 타입일 때를 구분한다.
                    if (isValueObjectType)
                    {
                        // Crypto
                        if (valueType == "CryptoValueInt" || valueType == "CryptoValueFloat" || valueType == "CryptoValueDouble" || valueType == "CryptoValueBool")
                        {
                            updatedSentence += $"{INTEND10}{updatedObjectSentence}";
                            updatedSentence += $"{INTEND10}{member.Name}[keyType] = newObject;";
                        }
                        else
                        {
                            updatedSentence += $"{INTEND10}{updatedObjectSentence}";
                        }
                    }
                    else if (isValueEnum)
                    {
                        updatedSentence += $"{INTEND10}{member.Name}[keyType] = ({valueType})int.Parse(parseJson[keyData]);";
                    }
                    else
                    {
                        updatedSentence += $"{INTEND10}{member.Name}[keyType] = parseJson[keyData]{asValue};";
                    }
                }

                //Deleted
                {
                    // value 타입이 object, enum, 일반 타입일 때를 구분한다.
                    if (isValueObjectType)
                    {
                        deletedSentence += $"{INTEND10}{deletedObjectSentence}";
                    }
                    else if (isValueEnum)
                    {
                        deletedSentence += $"{INTEND10}{member.Name}.Remove(keyType);";
                    }
                    else
                    {
                        deletedSentence += $"{INTEND10}{member.Name}.Remove(keyType);";
                    }
                }

                member.ParseJsonNode = $"JSONNode parseJson = InNode;\n" +
                    $"{INTEND6}JSONNode.KeyEnumerator keyEnumerator = parseJson.Keys.GetEnumerator();\n" +
                    $"\n" +
                    $"{INTEND6}while (keyEnumerator.MoveNext())\n" +
                    $"{INTEND6}{{\n" +
                    $"{INTEND7}string keyData = keyEnumerator.Current;\n" +
                    $"{keyTypeParseSentence}" +

                    //Change Switch
                    $"{INTEND7}switch (InChangeType)\n" +
                    $"{INTEND7}{{\n" +

                    //Added
                    $"{INTEND8}case ECoreDataChangeType.Added:\n" +
                    $"{INTEND9}{{\n" +
                    $"{addedSentence}\n" +
                    $"{INTEND9}}}\n" +
                    $"{INTEND9}break;\n" +

                    //Updated
                    $"{INTEND8}case ECoreDataChangeType.Updated:\n" +
                    $"{INTEND9}{{\n" +
                    $"{updatedSentence}\n" +
                    $"{INTEND9}}}\n" +
                    $"{INTEND9}break;\n" +

                    //Deleted
                    $"{INTEND8}case ECoreDataChangeType.Deleted:\n" +
                    $"{INTEND9}{{\n" +
                    $"{deletedSentence}\n" +
                    $"{INTEND9}}}\n" +
                    $"{INTEND9}break;\n" +

                    $"{INTEND7}}}\n" +
                    $"{INTEND6}}}";

                return;
            }

            // Array
            Type enumerable;
            if (IsClosedEnumerableType(type))
                enumerable = type;
            else
                enumerable = type.GetInterfaces().FirstOrDefault(IsClosedEnumerableType);
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
                    // Crypto
                    if (valueType == "CryptoValueInt" || valueType == "CryptoValueFloat" || valueType == "CryptoValueDouble" || valueType == "CryptoValueBool")
                    {
                        conditionalSentence =
                            $"{INTEND7}var newObject = {valueType}.Create(enumerator.Current.Value);\n" +
                            $"{INTEND7}{member.Name}.Add(newObject);\n";
                    }
                    else if (valueType == "ModValue")
                    {
                        // ModValue
                        conditionalSentence =
                            $"{INTEND7}var newObject = GameObjectPoolManager.Instance.GetPoolModValue();\n" +
                            "{INTEND7}newObject.FromJson(enumerator.Current.Value, InChangeType);\n" +
                            $"{INTEND7}{member.Name}.Add(newObject);\n";
                    }
                    else
                    {
                        Debug.LogError($"Unsupported value type: {valueType}");
                        // Other types
                        conditionalSentence = $"{INTEND7}var newObject = new {valueType}();\n" +
                                            $"{INTEND7}newObject.FromJson(enumerator.Current.Value, InChangeType);\n" +
                                            $"{INTEND7}{member.Name}.Add(newObject);\n";
                    }
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

            // CryptoValue(=struct)
            if (type.IsValueType && type.Name == "CryptoValueInt")
            {
                member.ParseJsonNode = $"{member.Name} = CryptoValueInt.Create(InNode);";
                return;
            }
            else if (type.IsValueType && type.Name == "CryptoValueDouble")
            {
                member.ParseJsonNode = $"{member.Name} = CryptoValueDouble.Create(InNode);";
                return;
            }
            else if (type.IsValueType && type.Name == "CryptoValueFloat")
            {
                member.ParseJsonNode = $"{member.Name} = CryptoValueFloat.Create(InNode);";
                return;
            }
            else if (type.IsValueType && type.Name == "CryptoValueBool")
            {
                member.ParseJsonNode = $"{member.Name} = CryptoValueBool.Create(InNode);";
                return;
            }

            // PXBigInt(=struct)
            if (type.IsValueType && type.Name == "PXBigInt")
            {
                member.ParseJsonNode = $"{member.Name} = PXBigInt.Create(InNode);";
                return;
            }

            // ModValue — Updated 시 기존 객체를 유지하여 diff에서 생략된 ValueType 보존
            else if (type.IsClass && type.Name == "ModValue")
            {
                member.ParseJsonNode = $"switch (InChangeType)\n" +
                    $"{INTEND6}{{\n" +
                    $"{INTEND7}case ECoreDataChangeType.Added:\n" +
                    $"{INTEND8}{{\n" +
                    $"{INTEND9}{member.Name} = GameObjectPoolManager.Instance.GetPoolModValue();\n" +
                    $"{INTEND9}{member.Name}.FromJson(InNode);\n" +
                    $"{INTEND8}}}\n" +
                    $"{INTEND8}break;\n" +
                    $"{INTEND7}case ECoreDataChangeType.Updated:\n" +
                    $"{INTEND8}{{\n" +
                    $"{INTEND9}{member.Name}.FromJson(InNode, InChangeType);\n" +
                    $"{INTEND8}}}\n" +
                    $"{INTEND8}break;\n" +
                    $"{INTEND7}case ECoreDataChangeType.Deleted:\n" +
                    $"{INTEND8}{{\n" +
                    $"{INTEND9}{member.Name} = null;\n" +
                    $"{INTEND8}}}\n" +
                    $"{INTEND8}break;\n" +
                    $"{INTEND6}}}";
                return;
            }

            // object type
            member.ParseJsonNode = $"switch (InChangeType)\n" +
                $"{INTEND6}{{\n" +
                $"{INTEND7}case ECoreDataChangeType.Added:\n" +
                $"{INTEND8}{{\n" +
                $"{INTEND9}{member.Name} = new {member.Type}();\n" +
                $"{INTEND9}{member.Name}.FromJson(InNode, InChangeType);\n" +
                $"{INTEND8}}}\n" +
                $"{INTEND8}break;\n" +
                $"{INTEND7}case ECoreDataChangeType.Updated:\n" +
                $"{INTEND8}{{\n" +
                $"{INTEND9}{member.Name}.FromJson(InNode, InChangeType);\n" +
                $"{INTEND8}}}\n" +
                $"{INTEND8}break;\n" +
                $"{INTEND7}case ECoreDataChangeType.Deleted:\n" +
                $"{INTEND8}{{\n" +
                $"{INTEND9}{member.Name} = null;\n" +
                $"{INTEND8}}}\n" +
                $"{INTEND8}break;\n" +
                $"{INTEND6}}}";
        }
    }


    public class GenerateCodeWriter_CoreData_ParseJsonNode : GenerateCodeWriter
    {
        public static bool HasExcludeJsonParsePropertyAttribute(MemberDefinition memberDef)
        {
            var excludeAttr = memberDef.GetCustomAttribute<ExcludeCodeGenerateJsonParseProperty>();
            return excludeAttr != null;
        }

        public GenerateCodeWriter_CoreData_ParseJsonNode(string InFileName, string InOutputPath, bool isDebug)
        {
            Initialize(InFileName, ".cs", InOutputPath, isDebug);
        }

        public override bool WriteToStringBuilder(GenerateCodeContext context)
        {
            GenerateAutoGeneratedComment("CodeGenerator_CoreData_ParseJsonNode.cs");

            // import
            sb.AppendLine($"using SimpleJSON;");
            sb.AppendLine($"using System.Collections.Generic;");
            sb.AppendLine($"using UnityEngine;");
            sb.AppendLine($"using Firebase.Firestore;");
            sb.AppendLine();

            // namespace {
            sb.AppendLine($"namespace PX");
            sb.AppendLine($"{{");

            // class
            foreach (TypeDefinition t in context.Types)
            {
                if (HasExcludeAttribute(t.ClrType)) // [ExcludeCodeGenerateClass]
                    continue;

                if (t.ClrType.IsInterface || t.ClrType.IsAbstract)
                    continue;

                sb.AppendLine($"{INTEND1}{t.Declaration}"); // class start
                sb.AppendLine($"{INTEND1}{{");

                // ParseJsonNode()
                {
                    sb.AppendLine($"{INTEND2}public override bool ParseJsonNode(string InDataKey, JSONNode InNode, ECoreDataChangeType InChangeType)");
                    sb.AppendLine($"{INTEND2}{{");

                    sb.AppendLine($"{INTEND3}if (InNode == null || InDataKey.Length == 0)");
                    sb.AppendLine($"{INTEND3}{{");
                    sb.AppendLine($"{INTEND4}Debug.LogError($\"ParseJsonNode TypeError, {t.Name}, InNode == null || InDataKey.Length == 0\");");
                    sb.AppendLine($"{INTEND4}return false;");
                    sb.AppendLine($"{INTEND3}}}");

                    sb.AppendLine();

                    // switch
                    {
                        sb.AppendLine($"{INTEND3}switch (InDataKey)");
                        sb.AppendLine($"{INTEND3}{{");

                        // base class members
                        SwitchCaseBaseTypeRecursively(t, context);

                        foreach (MemberDefinition m in t.Members)
                        {
                            if (HasExcludePropertyAttribute(m)) // [ExcludeCodeGenerateProperty]
                                continue;

                            if (HasExcludeJsonParsePropertyAttribute(m)) // [ExcludeCodeGenerateJsonParseProperty]
                                continue;

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
                    }

                    //Changed Delegate Invoke
                    {
                        sb.AppendLine($"{INTEND3}coreDataChangedDelegate?.Invoke(InDataKey, InChangeType);");
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
                sb.AppendLine($"{INTEND5}}}");
                sb.AppendLine($"{INTEND5}break;");
            }
        }
    }
}