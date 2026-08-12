using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace PX
{
    public class CodeGenerator_CoreData : CodeGeneratorBase
    {
        public CodeGenerator_CoreData(string InFileName, string InOutputPathCS, string InOutputPathTS, bool isDebug)
        {
            OutputLanguage = CodeGeneratorBase.OutputLanguageType.TypeScript;

            generatorCS = null;
            generatorTS = new GenerateCodeWriter_CoreData_TS(InFileName, InOutputPathTS, isDebug);
        }

        public override string GenerateTry()
        {
            // CommonCoreData 상속 받은 모든 클래스별로 프로퍼티의 Type 수집.
            GenerateCodeContext context = CreteCodeContext(typeof(CommonCoreData));
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
                bool isValueObjectType = Type.GetTypeCode(dictionaryType.GetGenericArguments().ElementAt(1)) == TypeCode.Object;

                if (isValueObjectType)
                    member.ObjectClrType = valueType;

                string entryparam_keyAsValue;
                if (dictionaryType.GetGenericArguments().ElementAt(0).IsEnum)
                    entryparam_keyAsValue = $"Number(entry[0]) as {keyType}";
                else if (dictionaryType.GetGenericArguments().ElementAt(0).IsPrimitive)
                {
                    entryparam_keyAsValue = $"Number(entry[0])";
                }
                else
                {
                    entryparam_keyAsValue = $"entry[0] as {keyType}";
                }
                string entryparam_valueType = isValueObjectType ?
                    $"plainToInstance({valueType}, entry[1])"
                    : $"entry[1] as {valueType}";

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
                if (isValueObjectType)
                    member.ObjectClrType = valueType;
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
                //Debug.Assert(false, "ERROR: List type isn't supported! Use Map type instead.");

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

            // object type
            member.ObjectClrType = member.Type;
            member.TypeDecoForJavaObject = $"@Type(() => {member.Type})";
            member.DefaultValue = $"new {member.Type}()";
        }
    }

    public class GenerateCodeWriter_CoreData_TS : GenerateCodeWriter
    {
        public GenerateCodeWriter_CoreData_TS(string InFileName, string InOutputPath, bool isDebug)
        {
            Initialize(InFileName, ".ts", InOutputPath, isDebug);
        }

        public override bool WriteToStringBuilder(GenerateCodeContext context)
        {
            GenerateAutoGeneratedComment("CodeGenerator_CoreData.cs");

            // import - 동적 경로 결정
            foreach (EnumDefinition e in context.Enums)
            {
                string importPath = GetEnumImportPath(e.ClrType);
                sb.AppendLine($"import {{ {e.Name} }} from \"{importPath}\";");
            }
            sb.AppendLine($"import {{ ModValue, ModValueInt, ModValueDouble, ModValueBool }} from \"../Shared/Types/ModValueTypes\";");
            sb.AppendLine($"import {{ PXBigInt }} from \"../Shared/Types/BigIntTypes\";");
            sb.AppendLine($"import {{ Transform, Type, plainToInstance }} from \"class-transformer\";");

            // 종속성 순서대로 정렬해준다.
            List<TypeDefinition> orderedTypes = OrderByDependencies(context);

            // "CryptoValue" to "ModValue"
            foreach (TypeDefinition t in orderedTypes)
            {
                t.ReplaceString("CryptoValue", "ModValue");
            }

            // class
            foreach (TypeDefinition t in orderedTypes)
            {
                if (HasExcludeAttribute(t.ClrType)) // [ExcludeCodeGenerateClass]
                    continue;

                sb.AppendLine();
                if (t.ClrType.IsInterface)
                    sb.AppendLine($"{t.Declaration} {{}}");
                else
                    sb.AppendLine($"{t.Declaration} {{");

                // members
                foreach (MemberDefinition m in t.Members)
                {
                    if (HasExcludePropertyAttribute(m)) // [ExcludeCodeGenerateProperty]
                        continue;

                    // "CryptoValue" to "ModValue"
                    m.ReplaceString("CryptoValue", "ModValue");

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
                            if (HasExcludePropertyAttribute(m)) continue;

                            sb.AppendLine($"{INTEND2}this.{m.Name} = {m.DefaultValue};");
                        }

                        if (t.Members.Count > 0)
                            sb.AppendLine($"{INTEND1}}}");
                    }
                    else
                    {

                        sb.AppendLine($"{INTEND1}constructor() {{");
                        sb.AppendLine($"{INTEND2}super();");
                        sb.AppendLine();

                        foreach (MemberDefinition m in t.Members)
                        {
                            if (HasExcludePropertyAttribute(m)) continue;

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

        // Enum의 소스 위치를 결정하는 메서드
        private string GetEnumImportPath(Type enumType)
        {
            // EnumSourceAttribute가 있는지 확인
            var enumSourceAttr = enumType.GetCustomAttribute<EnumSourceAttribute>();

            if (enumSourceAttr != null)
            {
                // Shared 경로는 Generated 폴더 기준 상대 경로로 변환
                // "Shared/Types/AccountTypes" → "../Shared/Types/AccountTypes"
                return $"../{enumSourceAttr.SourceFile}";
            }

            // 기본 경로 (CommonEnum은 같은 Generated 폴더)
            return "./CommonEnum";
        }
    }
}
