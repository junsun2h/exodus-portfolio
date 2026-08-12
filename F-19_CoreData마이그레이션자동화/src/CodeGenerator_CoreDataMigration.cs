using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// CoreData 마이그레이션 코드 자동 생성기
    /// </summary>
    public class CodeGenerator_CoreDataMigration : CodeGeneratorBase
    {
        private readonly string schemaHistoryPath;
        private readonly string outputPath;
        private readonly bool isDebug;
        private readonly int majorVersionOverride; // -1이면 자동, 0 이상이면 해당 값으로 MAJOR 버전 설정

        private CoreDataSchemaExtractor extractor;
        private CoreDataSchemaComparer comparer;

        // 생성 결과
        private List<SchemaCompareResult> compareResults = new List<SchemaCompareResult>();
        private List<string> generatedFiles = new List<string>();
        private List<string> todoItems = new List<string>();

        public CodeGenerator_CoreDataMigration(string schemaHistoryPath, string outputPath, bool isDebug, int majorVersionOverride = -1)
        {
            this.schemaHistoryPath = schemaHistoryPath;
            this.outputPath = outputPath;
            this.isDebug = isDebug;
            this.majorVersionOverride = majorVersionOverride;

            extractor = new CoreDataSchemaExtractor(schemaHistoryPath);
            comparer = new CoreDataSchemaComparer();
        }

        public override string GenerateTry()
        {
            compareResults.Clear();
            generatedFiles.Clear();
            todoItems.Clear();

            // 1. 현재 스키마 추출
            Dictionary<string, CoreDataSchema> currentSchemas = extractor.ExtractAllSchemas();
            Debug.Log($"추출된 CoreData 스키마 수: {currentSchemas.Count}");

            // 2. 각 CoreData별로 비교 및 마이그레이션 코드 생성
            var coreDataVersions = new Dictionary<string, string>();

            foreach (var kvp in currentSchemas)
            {
                string coreDataName = kvp.Key;
                CoreDataSchema currentSchema = kvp.Value;

                // 이전 히스토리 로드
                SchemaHistory history = extractor.LoadSchemaHistory(coreDataName);

                if (history == null)
                {
                    // 첫 실행: 초기 히스토리 생성
                    history = extractor.CreateInitialHistory(currentSchema);
                    extractor.SaveSchemaHistory(history);
                    Debug.Log($"[{coreDataName}] 초기 스키마 히스토리 생성됨");

                    coreDataVersions[coreDataName] = history.CurrentVersion;
                    continue;
                }

                // 이전 버전 스키마와 비교
                CoreDataSchema previousSchema = history.Versions.Last().Schema;
                SchemaCompareResult result = comparer.Compare(previousSchema, currentSchema, majorVersionOverride);

                if (result.HasChanges)
                {
                    // 변경사항 있음
                    Debug.Log($"[{coreDataName}] 변경 감지됨:\n{comparer.GenerateChangeSummary(result)}");
                    compareResults.Add(result);

                    // 히스토리 업데이트
                    var newVersion = new SchemaVersion
                    {
                        Version = result.NewVersion,
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        Schema = currentSchema,
                        Changes = result.Changes
                    };
                    history.Versions.Add(newVersion);
                    history.CurrentVersion = result.NewVersion;
                    extractor.SaveSchemaHistory(history);

                    // TODO 항목 추출
                    foreach (var change in result.Changes)
                    {
                        if (change.Type == SchemaChangeType.FieldTypeChanged)
                        {
                            todoItems.Add($"[{coreDataName}] {change.FieldName}: {change.OldType} -> {change.NewType} 타입 변환 로직 필요");
                        }
                        else if (change.Type == SchemaChangeType.FieldRenamed)
                        {
                            todoItems.Add($"[{coreDataName}] {change.OldFieldName} -> {change.FieldName} 이름 변경 확인 필요");
                        }
                    }
                }

                coreDataVersions[coreDataName] = history.CurrentVersion;

                // 마이그레이션 파일 생성
                GenerateMigrationFile(coreDataName, history);
            }

            // 3. CoreDataVersions.generated.ts 생성
            GenerateVersionsFile(coreDataVersions);

            return DisplayResults();
        }

        /// <summary>
        /// 개별 CoreData 마이그레이션 파일 생성
        /// </summary>
        private void GenerateMigrationFile(string coreDataName, SchemaHistory history)
        {
            string content = CoreDataMigrationTemplates.GenerateFullMigrationFile(coreDataName, history);
            string fileName = $"{coreDataName}_migrations.generated.ts";
            string filePath = Path.Combine(outputPath, fileName);

            WriteFile(filePath, content);
            generatedFiles.Add(fileName);
        }

        /// <summary>
        /// CoreDataVersions.generated.ts 파일 생성
        /// </summary>
        private void GenerateVersionsFile(Dictionary<string, string> versions)
        {
            string content = CoreDataMigrationTemplates.GenerateVersionsFile(versions);
            string fileName = "CoreDataVersions.generated.ts";
            string filePath = Path.Combine(outputPath, fileName);

            WriteFile(filePath, content);
            generatedFiles.Add(fileName);
        }

        /// <summary>
        /// 파일 쓰기 (디버그 모드 지원)
        /// </summary>
        private void WriteFile(string filePath, string content)
        {
            // 출력 디렉토리 생성
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // 디버그 모드일 때는 _debug 접미사 추가
            if (isDebug)
            {
                string ext = Path.GetExtension(filePath);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                filePath = Path.Combine(directory, $"{nameWithoutExt}_debug{ext}");
            }

            File.WriteAllText(filePath, content, Encoding.UTF8);
            Debug.Log($"파일 생성됨: {filePath}");
        }

        /// <summary>
        /// 결과 표시
        /// </summary>
        private string DisplayResults()
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== CoreData 마이그레이션 코드 생성 완료 ===");
            sb.AppendLine();

            // 변경사항 요약
            if (compareResults.Count > 0)
            {
                sb.AppendLine("[ 변경된 CoreData ]");
                foreach (var result in compareResults)
                {
                    sb.AppendLine(comparer.GenerateChangeSummary(result));
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("변경된 CoreData 없음");
                sb.AppendLine();
            }

            // 생성된 파일
            sb.AppendLine($"[ 생성된 파일 ({generatedFiles.Count}개) ]");
            foreach (string file in generatedFiles)
            {
                sb.AppendLine($"  - {file}");
            }
            sb.AppendLine();

            // TODO 항목
            if (todoItems.Count > 0)
            {
                sb.AppendLine("[ TODO - 수작업 필요 ]");
                foreach (string todo in todoItems)
                {
                    sb.AppendLine($"  ! {todo}");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"출력 경로: {outputPath}");

            return sb.ToString();
        }

        /// <summary>
        /// 스키마 히스토리 초기화 (모든 CoreData의 현재 상태를 v0.0.1로 저장)
        /// </summary>
        public string InitializeAllSchemaHistory()
        {
            Dictionary<string, CoreDataSchema> schemas = extractor.ExtractAllSchemas();
            int count = 0;

            foreach (var kvp in schemas)
            {
                string coreDataName = kvp.Key;

                // 기존 히스토리가 없는 경우에만 생성
                SchemaHistory existing = extractor.LoadSchemaHistory(coreDataName);
                if (existing == null)
                {
                    SchemaHistory history = extractor.CreateInitialHistory(kvp.Value);
                    extractor.SaveSchemaHistory(history);
                    count++;
                    Debug.Log($"초기화됨: {coreDataName}");
                }
            }

            return $"초기 스키마 히스토리 생성 완료: {count}개 CoreData";
        }

        /// <summary>
        /// 스키마 히스토리 강제 재초기화 (기존 히스토리를 덮어씀)
        /// 중첩 필드 구조가 포함된 새 스키마로 재생성
        /// </summary>
        public string ForceReinitializeAllSchemaHistory()
        {
            Dictionary<string, CoreDataSchema> schemas = extractor.ExtractAllSchemas();
            int count = 0;

            foreach (var kvp in schemas)
            {
                string coreDataName = kvp.Key;

                // 기존 히스토리 여부 상관없이 새로 생성
                SchemaHistory history = extractor.CreateInitialHistory(kvp.Value);
                extractor.SaveSchemaHistory(history);
                count++;
                Debug.Log($"강제 재초기화됨: {coreDataName}");
            }

            return $"스키마 히스토리 강제 재초기화 완료: {count}개 CoreData\n(중첩 필드 구조 포함)";
        }
    }
}
