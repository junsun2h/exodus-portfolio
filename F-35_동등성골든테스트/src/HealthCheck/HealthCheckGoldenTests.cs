// Phase 5 Track C — 동등성 골든 테스트 (CS 측)
//
// 이 테스트는 동일한 입력 JSON을 소비하여 CS 계산 결과를
// FirebaseCLI/functions/healthcheck/raw/golden-cs-*.json 으로 저장한다.
// TS 측 결과와 compare-golden.ts가 diff하여 parity 리포트를 생성한다.
//
// 실행:
//   - Unity Editor > Test Runner > EditMode > HealthCheckGolden 카테고리
//   - 또는 unicli exec RunEditModeTests --filter HealthCheckGolden

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using PX;
using SimpleJSON;
using UnityEngine;
using UnityEngine.TestTools;

namespace PX.Tests.HealthCheck
{
    [TestFixture]
    [Category("HealthCheckGolden")]
    public class HealthCheckGoldenTests
    {
        // Docs/plans/테스트/healthcheck/golden (프로젝트 루트에서)
        private static string GoldenDir
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, "Docs", "plans", "테스트", "healthcheck", "golden");
            }
        }

        // FirebaseCLI/functions/healthcheck/raw
        private static string OutDir
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, "FirebaseCLI", "functions", "healthcheck", "raw");
            }
        }

        [OneTimeSetUp]
        public void EnsureOutDir()
        {
            if (!Directory.Exists(OutDir))
                Directory.CreateDirectory(OutDir);
        }

        // 골든 입력셋은 의도적으로 극단값(int overflow, double overflow 등)을 포함하므로
        // 운영 코드(ConvertToModValue 등)가 찍는 Debug.LogError를 테스트 실패로 간주하지 않는다.
        // 목적은 CS/TS 결과 비교이지, 에러 로그 부재 검증이 아님.
        // LogAssert.ignoreFailingMessages는 각 테스트 시작 전에 리셋되므로 테스트 메서드 본체 시작에서 직접 설정.
        private static void AllowLogErrors()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        // ---------- 공통 helper ----------

        private static JSONNode ReadGolden(string fileName)
        {
            string filePath = Path.Combine(GoldenDir, fileName);
            Assert.IsTrue(File.Exists(filePath), $"골든 입력 파일 없음: {filePath}");
            string text = File.ReadAllText(filePath);
            return JSON.Parse(text);
        }

        private static void WriteGoldenResult(string name, string jsonContent)
        {
            string outPath = Path.Combine(OutDir, $"golden-cs-{name}.json");
            File.WriteAllText(outPath, jsonContent, new UTF8Encoding(false));
            Debug.Log($"[healthcheck-golden] wrote {outPath}");
        }

        // 문자열 직렬화 — Infinity/NaN/큰 숫자 안전 처리
        private static string Serialize(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Serialize(float v)
        {
            if (float.IsNaN(v)) return "NaN";
            if (float.IsPositiveInfinity(v)) return "Infinity";
            if (float.IsNegativeInfinity(v)) return "-Infinity";
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string SerializeBig(BigInteger v)
        {
            return v.ToString(CultureInfo.InvariantCulture) + "n";
        }

        // JSON escape
        private static string JsonEscape(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ---------- Enum 파서 ----------

        private static EModValueType ParseModValueType(string s)
        {
            if (Enum.TryParse(s, true, out EModValueType parsed))
                return parsed;
            return EModValueType.None;
        }

        private static EFormulaType ParseFormulaType(string s)
        {
            if (Enum.TryParse(s, true, out EFormulaType parsed))
                return parsed;
            return EFormulaType.None;
        }

        private static string ModValueTypeName(EModValueType t) => t.ToString();

        // ---------- 테스트 1: BigInt ----------

        [Test]
        public void BigInt_Golden()
        {
            AllowLogErrors();
            JSONNode data = ReadGolden("bigint_cases.json");
            var sb = new StringBuilder();
            sb.Append("{\n  \"operation\": \"numberToBigInt\",\n  \"rows\": [\n");

            bool first = true;
            AppendRowsBigInt(sb, ref first, data["mismatchSeeds"]);
            AppendRowsBigInt(sb, ref first, data["cases"]);

            sb.Append("\n  ]\n}\n");
            WriteGoldenResult("bigint", sb.ToString());
        }

        private void AppendRowsBigInt(StringBuilder sb, ref bool first, JSONNode arr)
        {
            if (arr == null || !arr.IsArray) return;
            foreach (JSONNode c in arr.AsArray)
            {
                string id = c["id"].Value;
                string inputType = c["inputType"]?.Value;
                string result;
                string error = null;
                string inputStr;

                try
                {
                    PXBigInt big;
                    if (inputType == "string")
                    {
                        string strInput = c["input"].Value;
                        inputStr = JsonEscape(strInput);
                        big = PXBigInt.Create(strInput);
                    }
                    else
                    {
                        double input = c["input"].AsDouble;
                        inputStr = input.ToString("R", CultureInfo.InvariantCulture);
                        big = PXBigInt.Create(input);
                    }
                    result = SerializeBig(big.Value);
                }
                catch (Exception e)
                {
                    result = "ERROR";
                    error = e.Message;
                    inputStr = c["input"]?.Value ?? "null";
                }

                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {");
                sb.Append($"\"id\": {JsonEscape(id)}, ");
                sb.Append($"\"input\": {inputStr}, ");
                sb.Append($"\"result\": {JsonEscape(result)}");
                if (error != null) sb.Append($", \"error\": {JsonEscape(error)}");
                sb.Append("}");
            }
        }

        // ---------- 테스트 2: Formula ----------

        [Test]
        public void Formula_Golden()
        {
            AllowLogErrors();
            JSONNode data = ReadGolden("formula_cases.json");
            var sb = new StringBuilder();
            sb.Append("{\n  \"operation\": \"FormulaData.GetValue\",\n  \"rows\": [\n");

            bool first = true;
            AppendRowsFormula(sb, ref first, data["mismatchSeeds"]);
            AppendRowsFormula(sb, ref first, data["cases"]);

            sb.Append("\n  ]\n}\n");
            WriteGoldenResult("formula", sb.ToString());
        }

        private void AppendRowsFormula(StringBuilder sb, ref bool first, JSONNode arr)
        {
            if (arr == null || !arr.IsArray) return;
            foreach (JSONNode c in arr.AsArray)
            {
                string id = c["id"].Value;
                string result;
                string error = null;

                try
                {
                    var fd = new FormulaData();
                    fd.FormulaType = ParseFormulaType(c["formulaType"].Value);
                    fd.Start = c["start"].AsDouble;
                    fd.Multiplier = c["multiplier"].AsDouble;
                    fd.ExpBase = c["expBase"].AsDouble;
                    fd.ValueType = ParseModValueType(c["valueType"].Value);

                    ModValue mv = fd.GetValue(c["level"].AsInt);
                    if (mv == null)
                    {
                        result = "ERROR";
                        error = "ConvertToModValue returned null";
                    }
                    else
                    {
                        // GetValue는 property — FLOAT_PER의 경우 0.01 배율 변환 포함
                        result = $"{ModValueTypeName(mv.ValueType)}:{Serialize(mv.GetValue)}";
                    }
                }
                catch (Exception e)
                {
                    result = "ERROR";
                    error = e.Message;
                }

                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {");
                sb.Append($"\"id\": {JsonEscape(id)}, ");
                sb.Append($"\"result\": {JsonEscape(result)}");
                if (error != null) sb.Append($", \"error\": {JsonEscape(error)}");
                sb.Append("}");
            }
        }

        // ---------- 테스트 3: ModValue ----------

        [Test]
        public void ModValue_Golden()
        {
            AllowLogErrors();
            JSONNode data = ReadGolden("modvalue_cases.json");
            var sb = new StringBuilder();
            sb.Append("{\n  \"operation\": \"ModValue.GetValue\",\n  \"rows\": [\n");

            bool first = true;
            AppendRowsModValue(sb, ref first, data["mismatchSeeds"]);
            AppendRowsModValue(sb, ref first, data["cases"]);

            sb.Append("\n  ]\n}\n");
            WriteGoldenResult("modvalue", sb.ToString());
        }

        private void AppendRowsModValue(StringBuilder sb, ref bool first, JSONNode arr)
        {
            if (arr == null || !arr.IsArray) return;
            foreach (JSONNode c in arr.AsArray)
            {
                string id = c["id"].Value;
                string result;
                string error = null;

                try
                {
                    EModValueType type = ParseModValueType(c["valueType"].Value);
                    double val = c["value"].AsDouble;
                    string createPath = c["createPath"]?.Value;

                    ModValue mv;
                    if (createPath == "CreateByValueType")
                    {
                        mv = ModValue.CreateByValueType(type, val);
                    }
                    else if (createPath == "CreateInt")
                    {
                        mv = ModValue.CreateInt((int)val);
                    }
                    else if (createPath == "CreateFloatPer")
                    {
                        mv = ModValue.CreateFloatPer((float)val);
                    }
                    else
                    {
                        // 기본: ConvertToModValue 운영 경로
                        mv = ModValue.ConvertToModValue(type, val);
                    }

                    if (mv == null)
                    {
                        result = "ERROR";
                        error = $"ModValue creation returned null for type {type}";
                    }
                    else
                    {
                        result = Serialize(mv.GetValue);
                    }
                }
                catch (Exception e)
                {
                    result = "ERROR";
                    error = e.Message;
                }

                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {");
                sb.Append($"\"id\": {JsonEscape(id)}, ");
                sb.Append($"\"result\": {JsonEscape(result)}");
                if (error != null) sb.Append($", \"error\": {JsonEscape(error)}");
                sb.Append("}");
            }
        }

        // ---------- 테스트 4: HashPRNG (가챠) ----------

        [Test]
        public void GachaHash_Golden()
        {
            AllowLogErrors();
            JSONNode data = ReadGolden("gachahash_seeds.json");
            int iters = data["iterations"].AsInt;
            if (iters <= 0) iters = 20;

            var sb = new StringBuilder();
            sb.Append("{\n  \"operation\": \"HashPRNG.NextRandom\",\n");
            sb.Append($"  \"iterations\": {iters},\n");
            sb.Append("  \"rows\": [\n");

            bool first = true;
            foreach (JSONNode seed in data["seeds"].AsArray)
            {
                string id = seed["id"].Value;
                string hash = seed["hash"].Value;

                // hash가 짧으면 패딩 (ValidateHash 통과용, 최소 8자)
                if (hash.Length < 8) hash = hash.PadRight(8, '0');

                var prng = new HashPRNG(hash);
                prng.CreatePRNG();

                var seqSb = new StringBuilder();
                seqSb.Append("[");
                for (int i = 0; i < iters; i++)
                {
                    if (i > 0) seqSb.Append(", ");
                    float r = prng.NextRandom();
                    seqSb.Append(JsonEscape(Serialize(r)));
                }
                seqSb.Append("]");

                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {");
                sb.Append($"\"id\": {JsonEscape(id)}, ");
                sb.Append($"\"hash\": {JsonEscape(hash)}, ");
                sb.Append($"\"sequence\": {seqSb}");
                sb.Append("}");
            }

            sb.Append("\n  ]\n}\n");
            WriteGoldenResult("gachahash", sb.ToString());
        }

        // ---------- 테스트 6: SampleBinomial ----------

        [Test]
        public void SampleBinomial_Golden()
        {
            AllowLogErrors();
            JSONNode data = ReadGolden("binomial_cases.json");
            var sb = new StringBuilder();
            sb.Append("{\n  \"operation\": \"SampleBinomial\",\n  \"rows\": [\n");

            bool first = true;
            foreach (JSONNode c in data["cases"].AsArray)
            {
                string id = c["id"].Value;
                int n = c["n"].AsInt;
                double p = c["p"].AsDouble;
                float rand1 = c["rand1"].AsFloat;
                float rand2 = c["rand2"].AsFloat;
                string result;
                string error = null;

                try
                {
                    int r = GameUtility.SampleBinomial(n, p, rand1, rand2);
                    result = r.ToString(CultureInfo.InvariantCulture);
                }
                catch (Exception e)
                {
                    result = "ERROR";
                    error = e.Message;
                }

                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {");
                sb.Append($"\"id\": {JsonEscape(id)}, ");
                sb.Append($"\"result\": {JsonEscape(result)}");
                if (error != null) sb.Append($", \"error\": {JsonEscape(error)}");
                sb.Append("}");
            }

            sb.Append("\n  ]\n}\n");
            WriteGoldenResult("binomial", sb.ToString());
        }

        // ---------- 테스트 7: Shared Enum 동등성 ----------

        [Test]
        public void SharedEnum_Golden()
        {
            AllowLogErrors();
            JSONNode data = ReadGolden("shared_enums_cases.json");
            var sb = new StringBuilder();
            sb.Append("{\n  \"operation\": \"SharedEnumValues\",\n  \"rows\": [\n");

            bool first = true;
            foreach (JSONNode e in data["enums"].AsArray)
            {
                string id = e["id"].Value;
                string enumName = e["name"].Value;
                string result;
                string error = null;

                try
                {
                    result = SerializeEnum(enumName);
                }
                catch (Exception ex)
                {
                    result = "ERROR";
                    error = ex.Message;
                }

                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {");
                sb.Append($"\"id\": {JsonEscape(id)}, ");
                sb.Append($"\"name\": {JsonEscape(enumName)}, ");
                sb.Append($"\"result\": {JsonEscape(result)}");
                if (error != null) sb.Append($", \"error\": {JsonEscape(error)}");
                sb.Append("}");
            }

            sb.Append("\n  ]\n}\n");
            WriteGoldenResult("shared_enums", sb.ToString());
        }

        // enum 이름 → "name1=val1,name2=val2,..." 직렬화
        private static string SerializeEnum(string enumName)
        {
            Type enumType;
            switch (enumName)
            {
                case "EModValueType": enumType = typeof(EModValueType); break;
                case "EFormulaType": enumType = typeof(EFormulaType); break;
                case "EEquipmentLargeType": enumType = typeof(EEquipmentLargeType); break;
                case "EEquipmentType": enumType = typeof(EEquipmentType); break;
                case "EGachaProduct": enumType = typeof(EGachaProduct); break;
                case "EGrade": enumType = typeof(EGrade); break;
                case "ETier": enumType = typeof(ETier); break;
                default: throw new Exception($"Unknown enum: {enumName}");
            }

            var names = Enum.GetNames(enumType);
            var values = Enum.GetValues(enumType);
            var pairs = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                pairs.Add($"{names[i]}={(int)values.GetValue(i)}");
            }
            pairs.Sort(); // 알파벳 순 정렬로 순서 차이 제거
            return string.Join(",", pairs);
        }

        // ---------- 테스트 5: HashPRNG (범용 RandomHash) ----------

        [Test]
        public void RandomHash_Golden()
        {
            AllowLogErrors();
            JSONNode data = ReadGolden("randomhash_seeds.json");
            int iters = data["iterations"].AsInt;
            if (iters <= 0) iters = 30;

            var sb = new StringBuilder();
            sb.Append("{\n  \"operation\": \"HashPRNG.NextRandom\",\n");
            sb.Append($"  \"iterations\": {iters},\n");
            sb.Append("  \"rows\": [\n");

            bool first = true;
            foreach (JSONNode seed in data["seeds"].AsArray)
            {
                string id = seed["id"].Value;
                string hash = seed["hash"].Value;
                if (hash.Length < 8) hash = hash.PadRight(8, '0');

                var prng = new HashPRNG(hash);
                prng.CreatePRNG();

                var seqSb = new StringBuilder();
                seqSb.Append("[");
                for (int i = 0; i < iters; i++)
                {
                    if (i > 0) seqSb.Append(", ");
                    float r = prng.NextRandom();
                    seqSb.Append(JsonEscape(Serialize(r)));
                }
                seqSb.Append("]");

                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {");
                sb.Append($"\"id\": {JsonEscape(id)}, ");
                sb.Append($"\"hash\": {JsonEscape(hash)}, ");
                sb.Append($"\"sequence\": {seqSb}");
                sb.Append("}");
            }

            sb.Append("\n  ]\n}\n");
            WriteGoldenResult("randomhash", sb.ToString());
        }
    }
}
