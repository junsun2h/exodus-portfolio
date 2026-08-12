using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PX
{
    public class CodeGenerator_GameDBData : CodeGeneratorBase
    {
        public CodeGenerator_GameDBData(string InFileName, string InOutputPathTS, bool isDebug, bool isLog)
        {
            OutputLanguage = CodeGeneratorBase.OutputLanguageType.TypeScript;
            generatorTS = new GenerateCodeWriter_GameDBData_TS(InFileName, InOutputPathTS, isDebug, isLog);
        }

        public override string GenerateTry()
        {
            // CommonCoreData 상속 받은 모든 클래스별로 프로퍼티의 Type 수집.
            GenerateCodeContext context = CreteCodeContext(typeof(GameDBData_Server));
            Debug.Assert(context.Types.Count > 0);
            Debug.Assert(context.Enums.Count > 0);

            // 문자열로 출력
            generatorTS.WriteToStringBuilder(context);

            generatorTS.WriteTempFile();
            bool IsNewTS = generatorTS.ReplaceWhenDiff();

            return DisplayResult(IsNewTS);
        }

        public override void FillMemberInfo(MemberDefinition member, Type type, GenerateCodeContext context)
        {
            if (type.IsEnum)
            {
                member.DefaultValue = $"{type.Name}.{Enum.GetName(type, 0)}";
                return;
            }

            TypeCode typeCode = Type.GetTypeCode(type);
            if (typeCode != TypeCode.Object)
            {
                bool useDateForDateTime = false;
                switch (typeCode)
                {
                    case TypeCode.Boolean:
                        member.DefaultValue = "false";
                        return;
                    case TypeCode.Byte:
                    case TypeCode.Decimal:
                    case TypeCode.Double:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.SByte:
                    case TypeCode.Single:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        member.DefaultValue = "0";
                        return;
                    case TypeCode.Char:
                    case TypeCode.String:
                        member.DefaultValue = "\"\"";
                        return;
                    case TypeCode.DateTime:
                        member.DefaultValue = useDateForDateTime ? "Date" : "\"\"";
                        return;
                    default:
                        Debug.Assert(true, "Unidentified Type!");
                        return;
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
                string valueType = GetTypeRef(dictionaryType.GetGenericArguments().ElementAt(1), context);

                bool isEnumKey = false;
                DictionaryEnumKey dictionaryEnumKey = member.GetCustomAttribute<DictionaryEnumKey>();
                if (dictionaryEnumKey != null && keyType == "string")
                {
                    // ex) GameDBEquipment.cs
                    // [DictionaryEnumKey(typeof(EEquipmentMythic))]
                    // Map 의 key 값을 커스텀 어트리뷰트의 DictionaryEnumKey 타입으로 바꿔준다.
                    CreateEnumContext(dictionaryEnumKey.keyType, context);
                    keyType = GetTypeRef(dictionaryEnumKey.keyType, context);
                    member.Type = $"Map<{keyType}, {valueType}>";

                    isEnumKey = true;
                }

                member.DefaultValue = $"new Map<{keyType}, {valueType}>()";

                bool isValueObjectType = Type.GetTypeCode(dictionaryType.GetGenericArguments().ElementAt(1)) == TypeCode.Object;

                string name_camelCase = char.ToLowerInvariant(member.Name[0]) + member.Name.Substring(1);
                string value_PascalCase = char.ToUpperInvariant(valueType[0]) + valueType.Substring(1);
                value_PascalCase = isValueObjectType ? $"new {value_PascalCase}" : $"{value_PascalCase}";

                string entryparam_keyAsValue = isEnumKey ?
                    $"Number(entry[0]) as {keyType}"
                    : $"entry[0] as {keyType}";
                string entryparam_valueType = isValueObjectType ?
                    $"plainToInstance({valueType}, entry[1])"
                    : $"entry[1] as {valueType}";

                if (isValueObjectType)
                    member.ObjectClrType = valueType;

                if (valueType.Contains("Array<")) // Map<Array<>>
                {
                    // 케이스가 발생하면 만들자.
                    Debug.Assert(false, "not yet supported as a value for Dictionary");
                    /*
                    Type arrayValueType = dictionaryType.GetGenericArguments().ElementAt(1).GetGenericArguments().ElementAt(0);
                    string arrayValueTypeName = GetTypeRef(dictionaryType.GetGenericArguments().ElementAt(1).GetGenericArguments().ElementAt(0), context);

                    string pushToArrayLine;
                    if (arrayValueType.IsEnum)
                        pushToArrayLine = $"{INTEND5}tempArrayValue.push(Number(value[1]) as {arrayValueTypeName});{NEWLINE}";
                    else
                        pushToArrayLine = $"{INTEND5}tempArrayValue.push(new {arrayValueTypeName}(value[1]));\n";

                    member.DefaultValueWithJson = $"new Map<{keyType}, {valueType}>();{NEWLINE}" +
                    $"{INTEND3}let {name_camelCase}Map = new Map(Object.entries(jsonData[\"{member.Name}\"] || {{}}));{NEWLINE}" +
                    $"{INTEND3}{name_camelCase}Map.forEach((arrayValue, key) => {{{NEWLINE}" +
                    $"{INTEND4}let tempArrayValue = new {valueType}();{NEWLINE}" +
                    $"{INTEND4}let tempArray = Object.entries(arrayValue as [string, unknown]);{NEWLINE}" +
                    $"{INTEND4}tempArray.forEach((value, index) => {{{NEWLINE}" +
                    pushToArrayLine +
                    $"{INTEND4}}});{NEWLINE}" +
                    $"{INTEND4}this.{member.Name}.set(Number(key) as {keyType}, tempArrayValue);{NEWLINE}" +
                    $"{INTEND3}}})";
                    */
                }
                else if (valueType.Contains("Dictionary<")) // Map<key, Map<>>
                {
                    Debug.Assert(false, "not yet supported as a value for Dictionary");
                }
                else // Map<key, value>
                {
                    member.TypeDecoForJavaObject = $"@Transform({NEWLINE}" +
                        $"{INTEND2}(value) => {{{NEWLINE}" +
                        $"{INTEND3}let map = new Map<{keyType}, {valueType}>();{NEWLINE}" +
                        $"{INTEND3}if (value.value != null) {{{NEWLINE}" +
                        $"{INTEND4}for (let entry of Object.entries(value.value)) {{{NEWLINE}" +
                        $"{INTEND5}map.set({entryparam_keyAsValue}, {entryparam_valueType});{NEWLINE}" +
                        $"{INTEND4}}}{NEWLINE}" +
                        $"{INTEND3}}}{NEWLINE}" +
                        $"{INTEND3}return map;{NEWLINE}" +
                        $"{INTEND2}}},{NEWLINE}" +
                        $"{INTEND2}{{ toClassOnly: true }}{NEWLINE}" +
                        $"{INTEND1})";
                    member.DefaultValue = $"new Map<{keyType}, {valueType}>()";
                }

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
                bool isValueObjectType = Type.GetTypeCode(enumerable.GetGenericArguments().ElementAt(0)) == TypeCode.Object;

                if (isValueObjectType)
                {
                    member.ObjectClrType = valueType;
                    member.TypeDecoForJavaObject = $"@Type(() => {valueType})";
                }
                member.DefaultValue = $"new Array<{valueType}>()";
                return;
            }

            // class, struct
            member.ObjectClrType = member.Type;
            member.TypeDecoForJavaObject = $"@Type(() => {member.Type})";
            member.DefaultValue = $"new {member.Type}()";
        }
    }

    public class GenerateCodeWriter_GameDBData_TS : GenerateCodeWriter
    {
        public GenerateCodeWriter_GameDBData_TS(string InFileName, string InOutputPath, bool isDebug, bool isLog)
        {
            Initialize(InFileName, ".ts", InOutputPath, isDebug, isLog);
        }

        public override bool WriteToStringBuilder(GenerateCodeContext context)
        {
            GenerateAutoGeneratedComment("CodeGenerator_GameDBData.cs");

            // import
            foreach (EnumDefinition e in context.Enums)
            {
                sb.AppendLine($"import {{ {e.Name} }} from \"./CommonEnum\";");
            }
            // sb.AppendLine($"import * as utilBasic from \"../Utility/UtilityBasic\";");
            sb.AppendLine($"import {{ ModValue }} from \"../Shared/Types/ModValueTypes\";");
            sb.AppendLine($"import {{ FormulaData }} from \"../Shared/Types/FormulaTypes\";");
            sb.AppendLine($"import {{ Transform, Type, plainToInstance }} from \"class-transformer\";");

            // 종속성 순서대로 정렬해준다.
            List<TypeDefinition> orderedTypes = OrderByDependencies(context);

            // class
            foreach (TypeDefinition t in orderedTypes)
            {
                if (HasExcludeAttribute(t.ClrType)) // [ExcludeCodeGenerate]
                    continue;

                sb.AppendLine();
                if (t.ClrType.IsInterface)
                    sb.AppendLine($"{t.Declaration} {{}}");
                else
                    sb.AppendLine($"{t.Declaration} {{");

                // members
                foreach (MemberDefinition m in t.Members)
                {
                    // deco
                    if (string.IsNullOrEmpty(m.TypeDecoForJavaObject) == false)
                        sb.AppendLine($"{INTEND1}{m.TypeDecoForJavaObject}");
                    sb.AppendLine($"{INTEND1}{m.Name}: {m.Type};");
                }
                if (t.Members.Count > 0)
                    sb.AppendLine();

                // constructor()
                {
                    if (t.ClrType.IsInterface)
                    {

                    }
                    else if (t.ClrType.IsAbstract)
                    {
                        if (t.Members.Count > 0)
                            sb.AppendLine($"{INTEND1}constructor() {{");
                        else
                            sb.AppendLine($"{INTEND1}constructor() {{}}");

                        foreach (MemberDefinition m in t.Members)
                        {
                            sb.AppendLine($"{INTEND2}this.{m.Name} = {m.DefaultValue};");
                        }

                        if (t.Members.Count > 0)
                            sb.AppendLine($"{INTEND1}}}");
                    }
                    else
                    {
                        sb.AppendLine($"{INTEND1}constructor() {{");
                        if (t.ClrType.BaseType.IsAbstract)
                            sb.AppendLine($"{INTEND2}super();\n");
                        else if (t.ClrType.BaseType == typeof(object))
                        { }
                        else
                            sb.AppendLine($"{INTEND2}super();\n");

                        if (IsOutputWithLog)
                        {
                            sb.AppendLine($"{INTEND2}console.log(\'@@@@ jsonData = \' + JSON.stringify(jsonData));");
                            sb.AppendLine();
                        }

                        foreach (MemberDefinition m in t.Members)
                        {
                            sb.AppendLine($"{INTEND2}this.{m.Name} = {m.DefaultValue};");
                        }

                        sb.AppendLine($"{INTEND1}}}");
                    }
                }

                if (!t.ClrType.IsInterface)
                    sb.AppendLine($"}}"); // end class
            }

            sb.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

            return true;
        }
    }
}
