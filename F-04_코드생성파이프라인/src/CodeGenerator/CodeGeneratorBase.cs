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
    public class GenerateCodeContext
    {
        public GenerateCodeContext()
        {
            Types = new List<TypeDefinition>();
            Enums = new List<EnumDefinition>();
        }

        public List<TypeDefinition> Types { get; }
        public List<EnumDefinition> Enums { get; }
    }

    public class TypeDefinition
    {
        public TypeDefinition(Type clrType, string name, string declaration, bool isExcludeAttribute)
        {
            ClrType = clrType;
            Name = name;
            Declaration = declaration;
            Members = new List<MemberDefinition>();
            IsExcludeAttribute = isExcludeAttribute;
        }

        public void ReplaceString(string from, string to)
        {
            Name = Name.Replace(from, to);
            Declaration = Declaration.Replace(from, to);

            foreach (var item in Members)
            {
                item.ReplaceString(from, to);
            }
        }

        public Type ClrType { get; }
        public string Name { get; set; }
        public string Declaration { get; set; }
        public List<MemberDefinition> Members { get; }
        public bool IsExcludeAttribute { get; }
    }

    public class EnumDefinition
    {
        public EnumDefinition(Type clrType, string name)
        {
            ClrType = clrType;
            Name = name;
        }

        public Type ClrType { get; }
        public string Name { get; }
    }

    public class MemberDefinition
    {
        public MemberDefinition(string name, string type, bool isNullable = false, IEnumerable<string> decorators = null, IEnumerable<Attribute> attrs = null)
        {
            Name = name;
            Type = type;
            IsNullable = isNullable;
            Decorators = (decorators?.ToList() ?? new List<string>()).AsReadOnly();
            Attrs = (attrs?.ToList() ?? new List<Attribute>()).AsReadOnly();
        }

        public void ReplaceString(string from, string to)
        {
            Name = Name.Replace(from, to);
            Type = Type.Replace(from, to);

            if (string.IsNullOrEmpty(DefaultValue) == false)
                DefaultValue = DefaultValue.Replace(from, to);
            if (string.IsNullOrEmpty(TypeDecoForJavaObject) == false)
                TypeDecoForJavaObject = TypeDecoForJavaObject.Replace(from, to);
        }

        public string Name { get; set; }
        public string Type { get; set; }
        public string ObjectClrType { get; set; }   // Object 형일때 타입 (map 은 밸류의 타입)
        public bool IsNullable { get; }
        public IReadOnlyCollection<string> Decorators { get; }
        public IReadOnlyCollection<Attribute> Attrs { get; }

        // CoreData.ts
        public string TypeDecoForJavaObject { get; set; }
        public string DefaultValue { get; set; }

        // _ParseJsonNode.cs
        public string ParseJsonNode { get; set; }
        public string MapValue { get; set; }

        public bool IsMapOrArray { get; set; }

        public T GetCustomAttribute<T>() where T : class
        {
            foreach (Attribute attr in Attrs)
            {
                if (attr is T)
                {
                    return attr as T;
                }
            }

            return null;
        }
    }

    public class GenerateCodeWriter
    {
        public string outputPath;
        public string outputFilePath;
        public string outputFileName;
        protected string outputTempFilePath;
        protected string ext { get; set; }

        protected string INTEND1 = "\t";
        protected string INTEND2 = "\t\t";
        protected string INTEND3 = "\t\t\t";
        protected string INTEND4 = "\t\t\t\t";
        protected string INTEND5 = "\t\t\t\t\t";
        protected string INTEND6 = "\t\t\t\t\t\t";
        protected string INTEND7 = "\t\t\t\t\t\t\t";
        protected string INTEND8 = "\t\t\t\t\t\t\t\t";

        protected StringBuilder sb = new StringBuilder();

        public bool IsOutputWithLog { get; set; }

        public int DiffInsertNum { get; set; }
        public int DiffDeleteNum { get; set; }

        public void Initialize(string fileName, string ext, string outputPath, bool isDebug, bool isLog = false)
        {
            IsOutputWithLog = isLog;
            outputFileName = fileName;
            this.ext = ext;
            this.outputPath = outputPath;
            outputFilePath = Path.Combine(outputPath, isDebug ? $"{outputFileName}_debug{ext}" : $"{outputFileName}{ext}");
            outputTempFilePath = Path.Combine(outputPath, $"temp{outputFileName}{ext}");
        }

        // 해당 클래스에 [ExcludeCodeGenerateClass]가 붙어 있는지 검사
        public bool HasExcludeAttribute(Type type)
        {
            foreach (Attribute attr in System.Attribute.GetCustomAttributes(type))
            {
                if (attr is ExcludeCodeGenerateClass)
                    return true;
            }

            return false;
        }

        // 해당 프로퍼티에 [ExcludeCodeGenerateProperty]가 붙어 있는지 검사
        public static bool HasExcludePropertyAttribute(MemberDefinition memberDef)
        {
            var excludeAttr = memberDef.GetCustomAttribute<ExcludeCodeGenerateProperty>();
            return excludeAttr != null;
        }

        public virtual bool WriteToStringBuilder(GenerateCodeContext context)
        {
            return false;
        }

        public virtual bool WriteToStringBuilder(Dictionary<string, GenerateEnumData> generateEnumDatas)
        {
            return false;
        }

        // 맴버 변수의 종속성에 따라 순서대로 정렬해준다.
        // ts 에서 전방선언이 없고, @Type 떄문에 추가된 기능
        public List<TypeDefinition> OrderByDependencies(GenerateCodeContext context)
        {
            var orderedTypes = new List<TypeDefinition>();
            context.Types.ForEach((t) =>
            {
                OrderByDependenciesRecursive(orderedTypes, context.Types, t);
            });

            Debug.Assert(orderedTypes.Count == context.Types.Count);
            return orderedTypes;
        }

        public void OrderByDependenciesRecursive(List<TypeDefinition> orderedTypes, List<TypeDefinition> sourceTypes, TypeDefinition t)
        {
            // BaseType
            if (t.ClrType.BaseType != null)
            {
                if (!orderedTypes.Exists(ot => ot.Name == t.ClrType.BaseType.Name))
                {
                    // Base 찾기의 재귀
                    var findType = sourceTypes.FindLast(tf => tf.Name == t.ClrType.BaseType.Name);
                    if (findType != null)
                        OrderByDependenciesRecursive(orderedTypes, sourceTypes, findType);
                }
            }

            // 맴버 변수
            t.Members.ForEach((m) =>
            {
                if (m.ObjectClrType != null)  // object 타입이 아니면 패스
                {
                    // object 타입인데 없으면 
                    if (!orderedTypes.Exists(ot => ot.Name == m.ObjectClrType))
                    {
                        // 이미 추가됐는지 체크하고 아니라면 추가한다.
                        var findType = sourceTypes.FindLast(tf => tf.Name == m.ObjectClrType);
                        if (findType != null)
                            OrderByDependenciesRecursive(orderedTypes, sourceTypes, findType);
                    }
                }
            });

            // 더 찾을 Base, 맴버가 없으면 추가
            if (!orderedTypes.Exists(ot => ot.Name == t.Name))
                orderedTypes.Add(t);
        }

        protected void GenerateAutoGeneratedComment(string sourceFile)
        {
            sb.AppendLine($"// <auto-generated>");
            sb.AppendLine($"// This file was generated; DO NOT EDIT!");
            sb.AppendLine($"// source: {sourceFile}");
            sb.AppendLine($"// </auto-generated>\n");
        }

        public void WriteTempFile()
        {
            File.WriteAllText($"{outputTempFilePath}", sb.ToString(), Encoding.UTF8);
        }

        // DiffPlex : https://github.com/mmanela/diffplex
        public bool ReplaceWhenDiff()
        {
            if (!File.Exists($"{outputFilePath}"))
            {
                File.Move($"{outputTempFilePath}", $"{outputFilePath}");

                return true;
            }

            if (IsDiff())
            {
                // delete old, temp -> new
                File.Delete($"{outputFilePath}");
                File.Move($"{outputTempFilePath}", $"{outputFilePath}");

                return true;
            }

            return false;
        }

        private bool IsDiff()
        {
            string oldFileText = File.ReadAllText($"{outputFilePath}");

            DiffPaneModel diff = InlineDiffBuilder.Diff(oldFileText, sb.ToString());
            StringBuilder diffList = new StringBuilder();
            foreach (DiffPiece line in diff.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Inserted:
                        diffList.AppendLine($"<color=green>+ {line.Text}</color>");
                        DiffInsertNum++;
                        break;
                    case ChangeType.Deleted:
                        diffList.AppendLine($"<color=red>- {line.Text}</color>");
                        DiffDeleteNum++;
                        break;
                    default:
                        break;
                }
            }

            Debug.Log($"({Path.GetFileName(outputFilePath)}) Total Lines : {diff.Lines.Count}, insert : {DiffInsertNum}, delete {DiffDeleteNum}");
            Debug.Log(diffList.ToString());

            return true;
        }

        public void DeleteTempFile()
        {
            if (File.Exists($"{outputTempFilePath}"))
                File.Delete($"{outputTempFilePath}");
        }
    }

    // ref : https://github.com/DogusTeknoloji/cs-to-ts
    public class CodeGeneratorBase
    {
        protected string INTEND1 = "\t";
        protected string INTEND2 = "\t\t";
        protected string INTEND3 = "\t\t\t";
        protected string INTEND4 = "\t\t\t\t";
        protected string INTEND5 = "\t\t\t\t\t";
        protected string INTEND6 = "\t\t\t\t\t\t";
        protected string INTEND7 = "\t\t\t\t\t\t\t";
        protected string INTEND8 = "\t\t\t\t\t\t\t\t";
        protected string INTEND9 = "\t\t\t\t\t\t\t\t\t";
        protected string INTEND10 = "\t\t\t\t\t\t\t\t\t\t";
        protected string INTEND11 = "\t\t\t\t\t\t\t\t\t\t\t";

        protected string NEWLINE = "\n";

        protected string NAMESPACE_PX = "PX";

        public enum OutputLanguageType
        {
            All,
            CSharp,
            TypeScript,
        }

        public OutputLanguageType OutputLanguage { get; set; }

        protected GenerateCodeWriter generatorCS { get; set; }
        protected GenerateCodeWriter generatorTS { get; set; }

        private static readonly BindingFlags BindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        public virtual string Generate()
        {
#if UNITY_EDITOR
            try
            {
                return GenerateTry();
            }
            catch (System.Exception ex)
            {
                Debug.Assert(false, $"Failed! CodeGenerate : {ex}");
            }
            finally
            {
                if (generatorCS != null)
                    generatorCS.DeleteTempFile();
                if (generatorTS != null)
                    generatorTS.DeleteTempFile();
            }
#endif
            return null;
        }

        public virtual string GenerateTry()
        {
            return string.Empty;
        }

        public string DisplayResult(bool IsNew)
        {
            string text = string.Empty;
            if (IsNew)
            {
                string retCS = string.Empty;
                if (generatorCS != null)
                    retCS = $"({Path.GetFileName(generatorCS.outputFilePath)}) insert : {generatorCS.DiffInsertNum}, delete : {generatorCS.DiffDeleteNum}\n";
                string retTS = string.Empty;
                if (generatorTS != null)
                    retTS = $"({Path.GetFileName(generatorTS.outputFilePath)}) insert : {generatorTS.DiffInsertNum}, delete : {generatorTS.DiffDeleteNum}\n";

                text = $"Success! Code Generation Successful.\n\n" + retCS + retTS;
            }
            else
            {
                text = "Failed! Nothing has changed";
            }

            return text;
        }

        public T HasCustomAttribute<T>(Type type) where T : class
        {
            foreach (Attribute attr in System.Attribute.GetCustomAttributes(type))
            {
                if (attr is T)
                {
                    return attr as T;
                }
            }

            return null;
        }

        public bool IsClosedDictionaryType(Type type)
        {
            return type.IsConstructedGenericType && (type.GetGenericTypeDefinition() == typeof(IDictionary<,>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
        }

        public bool IsClosedEnumerableType(Type type)
        {
            return type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);
        }

        private string StripGenericFromName(string name)
        {
            return name.Substring(0, name.IndexOf('`'));
        }

        private IEnumerable<MemberDefinition> GetMembers(Type type, GenerateCodeContext context)
        {
            Func<MemberInfo, string> memberRenamer = new Func<MemberInfo, string>(x => x.Name);
            Func<MemberInfo, IEnumerable<string>> useDecorators = new Func<MemberInfo, IEnumerable<string>>(_ => (new List<string>()));

            List<MemberDefinition> memberDefs = type.GetFields(BindingFlags)
                .Select(f =>
                {
                    Type fieldType = f.FieldType;

                    if (typeof(Delegate).IsAssignableFrom(fieldType))
                        return null;

                    bool nullable = false;
                    if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        // choose the generic parameter, rather than the nullable
                        fieldType = fieldType.GetGenericArguments()[0];
                        nullable = true;
                    }

                    MemberDefinition memberDef = new MemberDefinition(memberRenamer(f), GetTypeRef(fieldType, context), nullable, useDecorators(f).ToList());
                    FillMemberInfo(memberDef, fieldType, context);

                    return memberDef;
                })
                .Where(m => m != null)
                .ToList();

            memberDefs.AddRange(
                type.GetProperties(BindingFlags)
                .Select(p =>
                {
                    Type propertyType = p.PropertyType;

                    bool nullable = false;
                    if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        // choose the generic parameter, rather than the nullable
                        propertyType = propertyType.GetGenericArguments()[0];
                        nullable = true;
                    }
                    MemberDefinition memberDef = new MemberDefinition(
                        memberRenamer(p),
                        GetTypeRef(propertyType, context),
                        nullable,
                        useDecorators(p).ToList(),
                        System.Attribute.GetCustomAttributes(p));

                    FillMemberInfo(memberDef, propertyType, context);

                    return memberDef;
                })
                .Where(m => m != null)
                );

            return memberDefs;
        }

        public GenerateCodeContext CreteCodeContext(Type baseType)
        {
            System.Collections.Generic.IEnumerable<Type> types = AppDomain.CurrentDomain.GetAssemblies()
               .SelectMany(s => s.GetTypes())
               .Where(p => baseType.IsAssignableFrom(p) && p.IsClass);

            GenerateCodeContext context = new GenerateCodeContext();
            foreach (Type type in types)
            {
                if (type.IsEnum)
                {
                    CreateEnumContext(type, context);
                }
                else
                {
                    CreateTypeContext(type, context);
                }
            }

            return context;
        }

        public EnumDefinition CreateEnumContext(Type type, GenerateCodeContext context)
        {
            EnumDefinition existing = context.Enums.FirstOrDefault(t => t.ClrType == type);
            if (existing != null) return existing;

            EnumDefinition def = new EnumDefinition(type, type.Name);
            context.Enums.Add(def);

            return def;
        }

        private TypeDefinition CreateTypeContext(Type type, GenerateCodeContext context)
        {
            if (type.IsGenericParameter) return null;
            TypeCode typeCode = Type.GetTypeCode(type);
            if (typeCode != TypeCode.Object) return null;
            if (type.Namespace != NAMESPACE_PX) return null;

            if (type.IsConstructedGenericType)
            {
                type.GetGenericArguments().ToList().ForEach(t => CreateTypeContext(t, context));
                type = type.GetGenericTypeDefinition();
            }

            TypeDefinition existing = context.Types.FirstOrDefault(t => t.ClrType == type);
            if (existing != null) return existing;

            string declaration, typeName;
            (declaration, typeName) = GetDeclaration(type, context);

            bool isExcludeAttr = HasCustomAttribute<ExcludeCodeGenerateClass>(type) != null ? true : false;

            TypeDefinition typeDef = new TypeDefinition(type, typeName, declaration, isExcludeAttr);
            context.Types.Add(typeDef);

            // if (type.Name == "GameDB_ClientMailboxItem")
            // {
            //     Debug.Log($"typeDef : {typeDef.Name}");
            // }

            if (isExcludeAttr == false)
                typeDef.Members.AddRange(GetMembers(type, context));

            return typeDef;
        }

        private (string, string) GetDeclaration(Type type, GenerateCodeContext context)
        {
            switch (OutputLanguage)
            {
                case OutputLanguageType.CSharp:
                    return GetDeclaration_CS(type, context);
                case OutputLanguageType.TypeScript:
                    return GetDeclaration_TS(type, context);
                default:
                    return ("", "");
            }
        }

        private (string, string) GetDeclaration_CS(Type type, GenerateCodeContext context)
        {
            List<string> interfaceRefs = GetInterfaces(type, context);
            bool isInterface = type.IsInterface;
            string baseTypeRef = string.Empty;
            if (type.IsClass)
            {
                if (type.BaseType != typeof(object) && CreateTypeContext(type.BaseType, context) != null)
                {
                    baseTypeRef = GetTypeRef(type.BaseType, context);
                }
            }

            string declaration, typeName;
            if (!type.IsGenericType)
            {
                typeName = declaration = type.Name;
            }
            else
            {
                IEnumerable<string> genericPrms = type.GetGenericArguments().Select(g =>
                {
                    List<string> constraints = g.GetGenericParameterConstraints()
                        .Where(c => CreateTypeContext(c, context) != null)
                        .Select(c => GetTypeRef(c, context))
                        .ToList();

                    if (constraints.Any())
                    {
                        return $"{g.Name} : {string.Join(" & ", constraints)}";
                    }

                    return g.Name;
                });

                typeName = StripGenericFromName(type.Name);
                string genericPrmStr = string.Join(", ", genericPrms);
                declaration = $"{typeName}<{genericPrmStr}>";
            }
            if (isInterface)
            {
                declaration = $"interface {declaration}";

                if (!string.IsNullOrEmpty(baseTypeRef))
                {
                    interfaceRefs.Insert(0, baseTypeRef);
                }
            }
            else
            {
                string abs = type.IsAbstract ? " abstract" : string.Empty;
                declaration = $"{abs}class {declaration}";

                if (!string.IsNullOrEmpty(baseTypeRef))
                {
                    declaration = $"public partial {declaration} : {baseTypeRef}";
                }
            }

            if (interfaceRefs.Any())
            {
                string imp = isInterface ? ":" : ":";
                string interfaceRefStr = string.Join(", ", interfaceRefs);
                declaration = $"public partial {declaration} {imp} {interfaceRefStr}";
            }

            return (declaration, typeName);
        }

        private (string, string) GetDeclaration_TS(Type type, GenerateCodeContext context)
        {
            List<string> interfaceRefs = GetInterfaces(type, context);
            bool isInterface = type.IsInterface;
            string baseTypeRef = string.Empty;
            if (type.IsClass)
            {
                if (type.BaseType != typeof(object) && CreateTypeContext(type.BaseType, context) != null)
                {
                    baseTypeRef = GetTypeRef(type.BaseType, context);
                }
            }

            string declaration, typeName;
            if (!type.IsGenericType)
            {
                typeName = declaration = type.Name;
            }
            else
            {
                IEnumerable<string> genericPrms = type.GetGenericArguments().Select(g =>
                {
                    List<string> constraints = g.GetGenericParameterConstraints()
                        .Where(c => CreateTypeContext(c, context) != null)
                        .Select(c => GetTypeRef(c, context))
                        .ToList();

                    if (constraints.Any())
                    {
                        return $"{g.Name} extends {string.Join(" & ", constraints)}";
                    }

                    return g.Name;
                });

                typeName = StripGenericFromName(type.Name);
                string genericPrmStr = string.Join(", ", genericPrms);
                declaration = $"{typeName}<{genericPrmStr}>";
            }
            if (isInterface)
            {
                declaration = $"export interface {declaration}";

                if (!string.IsNullOrEmpty(baseTypeRef))
                {
                    interfaceRefs.Insert(0, baseTypeRef);
                }
            }
            else
            {
                string abs = type.IsAbstract ? " abstract" : string.Empty;
                declaration = $"export{abs} class {declaration}";

                if (!string.IsNullOrEmpty(baseTypeRef))
                {
                    declaration = $"{declaration} extends {baseTypeRef}";
                }
            }

            if (interfaceRefs.Any())
            {
                string imp = isInterface ? "extends" : "implements";
                string interfaceRefStr = string.Join(", ", interfaceRefs);
                declaration = $"{declaration} {imp} {interfaceRefStr}";
            }

            return (declaration, typeName);
        }

        private List<string> GetInterfaces(Type type, GenerateCodeContext context)
        {
            List<Type> interfaces = type.GetInterfaces().ToList();
            return interfaces
                .Except(type.BaseType?.GetInterfaces() ?? Enumerable.Empty<Type>())
                .Except(interfaces.SelectMany(i => i.GetInterfaces())) // get only implemented by this type
                .Where(i => CreateTypeContext(i, context) != null)
                .Select(i => GetTypeRef(i, context))
                .ToList();
        }

        public string GetTypeRef(Type type, GenerateCodeContext context)
        {
            if (type.IsGenericParameter)
                return type.Name;

            if (type.IsEnum)
            {
                EnumDefinition enumDef = CreateEnumContext(type, context);
                return enumDef != null ? enumDef.Name : "any";
            }

            TypeCode typeCode = Type.GetTypeCode(type);
            if (typeCode != TypeCode.Object)
                return GetPrimitiveMemberType(typeCode);

            Type dictionaryType;
            if (IsClosedDictionaryType(type))
                dictionaryType = type;
            else
                dictionaryType = type.GetInterfaces().FirstOrDefault(IsClosedDictionaryType);

            if (dictionaryType != null)
            {
                Type keyType = dictionaryType.GetGenericArguments().ElementAt(0);
                Type valueType = dictionaryType.GetGenericArguments().ElementAt(1);

                switch (OutputLanguage)
                {
                    case OutputLanguageType.CSharp:
                        return $"Dictionary<{GetTypeRef(keyType, context)}, {GetTypeRef(valueType, context)}>";
                    case OutputLanguageType.TypeScript:
                        return $"Map<{GetTypeRef(keyType, context)}, {GetTypeRef(valueType, context)}>";
                    default:
                        return "";
                }
            }

            Type enumerable;
            if (IsClosedEnumerableType(type))
                enumerable = type;
            else
                enumerable = type.GetInterfaces().FirstOrDefault(IsClosedEnumerableType);

            if (enumerable != null)
            {
                switch (OutputLanguage)
                {
                    case OutputLanguageType.CSharp:
                        return $"List<{GetTypeRef(enumerable.GetGenericArguments().First(), context)}>";
                    case OutputLanguageType.TypeScript:
                        return $"Array<{GetTypeRef(enumerable.GetGenericArguments().First(), context)}>";
                    default:
                        return "";
                }
            }

            TypeDefinition typeDef = CreateTypeContext(type, context);
            if (typeDef == null)
                return "any";

            string typeName = type.Name;
            if (type.IsGenericType)
            {
                System.Collections.Generic.IEnumerable<string> genericPrms = type.GetGenericArguments().Select(t => GetTypeRef(t, context));
                return $"{typeName}<{string.Join(", ", genericPrms)}>";
            }

            return typeName;
        }

        private string GetPrimitiveMemberType(TypeCode typeCode)
        {
            switch (OutputLanguage)
            {
                case OutputLanguageType.CSharp:
                    return GetPrimitiveMemberType_CS(typeCode);
                case OutputLanguageType.TypeScript:
                    return GetPrimitiveMemberType_TS(typeCode);
                default:
                    return "";
            }
        }

        private string GetPrimitiveMemberType_CS(TypeCode typeCode)
        {
            switch (typeCode)
            {
                case TypeCode.Boolean:
                    return "bool";
                case TypeCode.Byte:
                    return "byte";
                case TypeCode.Decimal:
                    return "decimal";
                case TypeCode.Double:
                    return "double";
                case TypeCode.Int16:
                case TypeCode.Int32:
                    return "int";
                case TypeCode.Int64:
                    return "long";
                case TypeCode.SByte:
                case TypeCode.Single:
                    return "float";
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                    return "uint";
                case TypeCode.UInt64:
                    return "ulong";
                case TypeCode.Char:
                case TypeCode.String:
                    return "string";
                default:
                    return "any";
            }
        }

        private string GetPrimitiveMemberType_TS(TypeCode typeCode)
        {
            bool useDateForDateTime = false;
            switch (typeCode)
            {
                case TypeCode.Boolean:
                    return "boolean";
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
                    return "number";
                case TypeCode.Char:
                case TypeCode.String:
                    return "string";
                case TypeCode.DateTime:
                    return useDateForDateTime ? "Date" : "string";
                default:
                    return "any";
            }
        }

        public virtual void FillMemberInfo(MemberDefinition member, Type type, GenerateCodeContext context)
        { }
    }
}
