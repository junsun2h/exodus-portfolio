using BansheeGz.BGDatabase;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PX
{
    public class GenerateEnumElementData
    {
        public string Name { get; set; }
        public string StrKeyName { get; set; } // StringKey
        public string StrKeyDesc { get; set; }
        public int Index { get; set; }
        public string Comment { get; set; }

        public GenerateEnumElementData()
        {
            Name = string.Empty;
            StrKeyName = string.Empty;
            StrKeyDesc = string.Empty;
            Comment = string.Empty;
        }
    }

    public class GenerateEnumData
    {
        public string SheetName { get; set; }
        public string EnumComment { get; set; }

        public List<GenerateEnumElementData> EnumElements { get; set; }

        public GenerateEnumData()
        {
            SheetName = string.Empty;
            EnumElements = new List<GenerateEnumElementData>();
        }
    }

    public class CodeGenerator_CommonEnum : CodeGeneratorBase
    {
        private readonly string enumSheetName = "enums";
        private readonly string SHEET = "sheet";
        private readonly string FIELD_NAME = "name";
        private readonly string FIELD_STRING_NAME = "str_name";
        private readonly string FIELD_STRING_DESC = "str_desc";
        private readonly string FIELD_ENUM_INDEX = "enum_index";
        private readonly string FIELD_COMMENT = "comment";

        // <Enum, GenerateEnumData>
        private readonly Dictionary<string, GenerateEnumData> generateEnumDatas = new Dictionary<string, GenerateEnumData>();

        public CodeGenerator_CommonEnum(string InFileName, string InOutputPathCS, string InOutputPathTS, bool isDebug)
        {
            generateEnumDatas.Clear();

            OutputLanguage = CodeGeneratorBase.OutputLanguageType.All;

            generatorCS = new GenerateCodeWriter_Enum_CS(InFileName, InOutputPathCS, isDebug);
            generatorTS = new GenerateCodeWriter_Enum_TS(InFileName, InOutputPathTS, isDebug);
        }

        private void CreateEnumsList(string sheetName)
        {
            BGMetaEntity metaEntity = BGRepo.I[sheetName];
            Debug.Assert(metaEntity != null, sheetName);
            Debug.Assert(metaEntity.CountEntities > 0, sheetName);

            foreach (BGEntity e in metaEntity.EntitiesToList())
            {
                GenerateEnumData data = new GenerateEnumData
                {
                    SheetName = e.Get<string>(SHEET)
                };
                if (metaEntity.HasField(FIELD_COMMENT))
                    data.EnumComment = e.Get<string>(FIELD_COMMENT);

                // enum element
                List<GenerateEnumElementData> newElements = CreateEnumElementList(data.SheetName);
                List<GenerateEnumElementData> ElementsOrderByIndex = newElements.OrderBy(x => x.Index).ToList();
                data.EnumElements.AddRange(ElementsOrderByIndex);

                Debug.Assert(!string.IsNullOrEmpty(data.SheetName));
                Debug.Assert(!string.IsNullOrEmpty(e.Get<string>(FIELD_NAME)));
                generateEnumDatas.Add(e.Get<string>(FIELD_NAME), data);
            }
        }

        private List<GenerateEnumElementData> CreateEnumElementList(string sheetName)
        {
            BGMetaEntity metaEntity = BGRepo.I[sheetName];
            Debug.Assert(metaEntity != null, sheetName);
            Debug.Assert(metaEntity.CountEntities > 0, sheetName);

            List<GenerateEnumElementData> retElements = new List<GenerateEnumElementData>();

            foreach (BGEntity e in metaEntity.EntitiesToList())
            {
                GenerateEnumElementData data = new GenerateEnumElementData
                {
                    Name = e.Get<string>(FIELD_NAME),
                    Index = e.Get<int>(FIELD_ENUM_INDEX)
                };

                if (metaEntity.HasField(FIELD_STRING_NAME))
                    data.StrKeyName = e.Get<string>(FIELD_STRING_NAME);
                if (metaEntity.HasField(FIELD_STRING_DESC))
                    data.StrKeyDesc = e.Get<string>(FIELD_STRING_DESC);
                if (metaEntity.HasField(FIELD_COMMENT))
                    data.Comment = e.Get<string>(FIELD_COMMENT);

                Debug.Assert(!string.IsNullOrEmpty(data.Name));
                Debug.Assert(data.Index >= 0);

                retElements.Add(data);
            }

            return retElements;
        }

        public override string GenerateTry()
        {
            CreateEnumsList(enumSheetName);

            bool IsGenerateCS = generatorCS.WriteToStringBuilder(generateEnumDatas);
            bool IsGenerateTS = generatorTS.WriteToStringBuilder(generateEnumDatas);
            Debug.Assert(IsGenerateCS == true && IsGenerateTS == true);

            generatorCS.WriteTempFile();
            generatorTS.WriteTempFile();

            bool IsNewCS = generatorCS.ReplaceWhenDiff();
            bool IsNewTS = generatorTS.ReplaceWhenDiff();

            return DisplayResult(IsNewCS || IsNewTS);
        }
    }

    public class GenerateCodeWriter_Enum_CS : GenerateCodeWriter
    {
        public GenerateCodeWriter_Enum_CS(string InFileName, string InOutputPath, bool isDebug)
        {
            Initialize(InFileName, ".cs", InOutputPath, isDebug);
        }

        public override bool WriteToStringBuilder(Dictionary<string, GenerateEnumData> generateEnumDatas)
        {
            GenerateAutoGeneratedComment("EnumCodeGenerateEditor.cs");
            sb.AppendLine("using System.ComponentModel;\n");

            sb.AppendLine("namespace PX");
            sb.AppendLine("{");

            // enum
            foreach (var e in generateEnumDatas)
            {
                if (!string.IsNullOrEmpty(e.Value.EnumComment))
                    sb.AppendLine($"\t//{e.Value.EnumComment}");
                sb.AppendLine($"\tpublic enum {e.Key}");
                sb.AppendLine("\t{");

                // enum element
                sb.AppendLine($"\t\tNone = 0,");
                foreach (GenerateEnumElementData el in e.Value.EnumElements)
                {
                    if (!string.IsNullOrEmpty(el.StrKeyName))
                        sb.AppendLine($"\t\t[Name(\"{el.StrKeyName}\")]");
                    if (!string.IsNullOrEmpty(el.StrKeyDesc))
                        sb.AppendLine($"\t\t[Description(\"{el.StrKeyDesc}\")]");
                    sb.Append($"\t\t{el.Name} = {el.Index},");
                    if (!string.IsNullOrEmpty(el.Comment))
                        sb.Append($"        //{el.Comment}");
                    sb.Append("\n");
                }

                sb.AppendLine("\t}\n");
            }

            sb.AppendLine("}"); // end namespace

            // 일관성 없는 줄 끝 문제. Windows (CR LF)
            sb.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

            return true;
        }
    }

    public class GenerateCodeWriter_Enum_TS : GenerateCodeWriter
    {
        public GenerateCodeWriter_Enum_TS(string InFileName, string InOutputPath, bool isDebug)
        {
            Initialize(InFileName, ".ts", InOutputPath, isDebug);
        }

        public override bool WriteToStringBuilder(Dictionary<string, GenerateEnumData> generateEnumDatas)
        {
            GenerateAutoGeneratedComment("CodeGenerator_CommonEnum.cs");

            // enum
            foreach (var e in generateEnumDatas)
            {
                if (!string.IsNullOrEmpty(e.Value.EnumComment))
                    sb.AppendLine($"//{e.Value.EnumComment}");
                sb.AppendLine($"export const {e.Key} = {{");

                // enum element
                sb.AppendLine($"\tNone: 0,");
                foreach (GenerateEnumElementData el in e.Value.EnumElements)
                {
                    //if (!string.IsNullOrEmpty(el.Description_ko))
                    //    sb.AppendLine($"\t\t[Description(\"{el.Description_ko}\")]");
                    sb.Append($"\t{el.Name}: {el.Index},");
                    if (!string.IsNullOrEmpty(el.Comment))
                        sb.Append($" //{el.Comment}");
                    sb.Append("\n");
                }

                sb.AppendLine("} as const;");
                sb.AppendLine($"export type {e.Key} = typeof {e.Key}[keyof typeof {e.Key}];\n");
            }

            sb.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

            return true;
        }
    }
}