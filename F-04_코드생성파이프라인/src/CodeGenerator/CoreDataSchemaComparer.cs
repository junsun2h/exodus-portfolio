using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// 스키마 비교 결과
    /// </summary>
    public class SchemaCompareResult
    {
        public string CoreDataName;
        public string PreviousVersion;
        public string NewVersion;
        public List<SchemaChange> Changes = new List<SchemaChange>();
        public bool HasChanges => Changes.Count > 0;
        public bool HasBreakingChanges => Changes.Any(c =>
            c.Type == SchemaChangeType.FieldRemoved ||
            c.Type == SchemaChangeType.FieldTypeChanged ||
            c.Type == SchemaChangeType.FieldRenamed);
    }

    /// <summary>
    /// 두 스키마를 비교하여 변경사항을 감지하는 클래스
    /// </summary>
    public class CoreDataSchemaComparer
    {
        /// <summary>
        /// 이전 스키마와 현재 스키마 비교
        /// </summary>
        /// <param name="majorVersionOverride">-1이면 자동, 0 이상이면 해당 값으로 MAJOR 버전 설정</param>
        public SchemaCompareResult Compare(CoreDataSchema previous, CoreDataSchema current, int majorVersionOverride = -1)
        {
            var result = new SchemaCompareResult
            {
                CoreDataName = current.Name,
                PreviousVersion = previous?.Version ?? "0.0.0",
            };

            if (previous == null)
            {
                // 첫 버전인 경우
                result.NewVersion = "0.0.1";
                return result;
            }

            var previousFields = previous.Fields ?? new Dictionary<string, SchemaField>();
            var currentFields = current.Fields ?? new Dictionary<string, SchemaField>();

            // 1. 추가된 필드 감지
            foreach (var kvp in currentFields)
            {
                if (!previousFields.ContainsKey(kvp.Key))
                {
                    // 이름 변경 가능성 체크
                    var possibleRename = FindPossibleRename(kvp.Value, previousFields, currentFields);

                    if (possibleRename != null)
                    {
                        result.Changes.Add(new SchemaChange
                        {
                            Type = SchemaChangeType.FieldRenamed,
                            FieldName = kvp.Key,
                            OldFieldName = possibleRename,
                            OldType = previousFields[possibleRename].TypeScriptType,
                            NewType = kvp.Value.TypeScriptType,
                            DefaultValue = kvp.Value.DefaultValue
                        });
                    }
                    else
                    {
                        result.Changes.Add(new SchemaChange
                        {
                            Type = SchemaChangeType.FieldAdded,
                            FieldName = kvp.Key,
                            NewType = kvp.Value.TypeScriptType,
                            DefaultValue = kvp.Value.DefaultValue
                        });
                    }
                }
            }

            // 2. 삭제된 필드 감지 (Renamed로 처리되지 않은 것만)
            foreach (var kvp in previousFields)
            {
                if (!currentFields.ContainsKey(kvp.Key))
                {
                    // 이미 Renamed로 처리되었는지 확인
                    bool isRenamed = result.Changes.Any(c =>
                        c.Type == SchemaChangeType.FieldRenamed &&
                        c.OldFieldName == kvp.Key);

                    if (!isRenamed)
                    {
                        result.Changes.Add(new SchemaChange
                        {
                            Type = SchemaChangeType.FieldRemoved,
                            FieldName = kvp.Key,
                            OldType = kvp.Value.TypeScriptType
                        });
                    }
                }
            }

            // 3. 타입 변경 감지
            foreach (var kvp in currentFields)
            {
                if (previousFields.TryGetValue(kvp.Key, out var prevField))
                {
                    if (prevField.TypeScriptType != kvp.Value.TypeScriptType)
                    {
                        result.Changes.Add(new SchemaChange
                        {
                            Type = SchemaChangeType.FieldTypeChanged,
                            FieldName = kvp.Key,
                            OldType = prevField.TypeScriptType,
                            NewType = kvp.Value.TypeScriptType,
                            DefaultValue = kvp.Value.DefaultValue
                        });
                    }

                    // 4. 중첩 필드 변경 감지
                    if (prevField.IsNestedCoreData && kvp.Value.IsNestedCoreData)
                    {
                        CompareNestedFields(kvp.Key, prevField, kvp.Value, result);
                    }
                }
            }

            // 버전 결정
            result.NewVersion = DetermineNewVersion(result.PreviousVersion, result.Changes, majorVersionOverride);

            return result;
        }

        /// <summary>
        /// 중첩 필드 비교
        /// </summary>
        private void CompareNestedFields(string parentFieldName, SchemaField prevField, SchemaField currField, SchemaCompareResult result)
        {
            var prevNested = prevField.NestedFields ?? new Dictionary<string, SchemaField>();
            var currNested = currField.NestedFields ?? new Dictionary<string, SchemaField>();

            // 1. 추가된 중첩 필드
            foreach (var kvp in currNested)
            {
                if (!prevNested.ContainsKey(kvp.Key))
                {
                    result.Changes.Add(new SchemaChange
                    {
                        Type = SchemaChangeType.NestedFieldAdded,
                        FieldName = kvp.Key,
                        ParentFieldName = parentFieldName,
                        NestedFieldPath = $"{parentFieldName}.{kvp.Key}",
                        NewType = kvp.Value.TypeScriptType,
                        DefaultValue = kvp.Value.DefaultValue
                    });
                }
            }

            // 2. 삭제된 중첩 필드
            foreach (var kvp in prevNested)
            {
                if (!currNested.ContainsKey(kvp.Key))
                {
                    result.Changes.Add(new SchemaChange
                    {
                        Type = SchemaChangeType.NestedFieldRemoved,
                        FieldName = kvp.Key,
                        ParentFieldName = parentFieldName,
                        NestedFieldPath = $"{parentFieldName}.{kvp.Key}",
                        OldType = kvp.Value.TypeScriptType
                    });
                }
            }

            // 3. 타입 변경된 중첩 필드
            foreach (var kvp in currNested)
            {
                if (prevNested.TryGetValue(kvp.Key, out var prevNestedField))
                {
                    if (prevNestedField.TypeScriptType != kvp.Value.TypeScriptType)
                    {
                        result.Changes.Add(new SchemaChange
                        {
                            Type = SchemaChangeType.NestedFieldTypeChanged,
                            FieldName = kvp.Key,
                            ParentFieldName = parentFieldName,
                            NestedFieldPath = $"{parentFieldName}.{kvp.Key}",
                            OldType = prevNestedField.TypeScriptType,
                            NewType = kvp.Value.TypeScriptType,
                            DefaultValue = kvp.Value.DefaultValue
                        });
                    }

                    // 재귀적으로 더 깊은 중첩 필드도 비교
                    if (prevNestedField.IsNestedCoreData && kvp.Value.IsNestedCoreData)
                    {
                        CompareNestedFieldsRecursive($"{parentFieldName}.{kvp.Key}", prevNestedField, kvp.Value, result);
                    }
                }
            }
        }

        /// <summary>
        /// 재귀적 중첩 필드 비교
        /// </summary>
        private void CompareNestedFieldsRecursive(string path, SchemaField prevField, SchemaField currField, SchemaCompareResult result)
        {
            var prevNested = prevField.NestedFields ?? new Dictionary<string, SchemaField>();
            var currNested = currField.NestedFields ?? new Dictionary<string, SchemaField>();

            foreach (var kvp in currNested)
            {
                if (!prevNested.ContainsKey(kvp.Key))
                {
                    result.Changes.Add(new SchemaChange
                    {
                        Type = SchemaChangeType.NestedFieldAdded,
                        FieldName = kvp.Key,
                        ParentFieldName = path,
                        NestedFieldPath = $"{path}.{kvp.Key}",
                        NewType = kvp.Value.TypeScriptType,
                        DefaultValue = kvp.Value.DefaultValue
                    });
                }
            }

            foreach (var kvp in prevNested)
            {
                if (!currNested.ContainsKey(kvp.Key))
                {
                    result.Changes.Add(new SchemaChange
                    {
                        Type = SchemaChangeType.NestedFieldRemoved,
                        FieldName = kvp.Key,
                        ParentFieldName = path,
                        NestedFieldPath = $"{path}.{kvp.Key}",
                        OldType = kvp.Value.TypeScriptType
                    });
                }
            }

            foreach (var kvp in currNested)
            {
                if (prevNested.TryGetValue(kvp.Key, out var prevNestedField))
                {
                    if (prevNestedField.TypeScriptType != kvp.Value.TypeScriptType)
                    {
                        result.Changes.Add(new SchemaChange
                        {
                            Type = SchemaChangeType.NestedFieldTypeChanged,
                            FieldName = kvp.Key,
                            ParentFieldName = path,
                            NestedFieldPath = $"{path}.{kvp.Key}",
                            OldType = prevNestedField.TypeScriptType,
                            NewType = kvp.Value.TypeScriptType,
                            DefaultValue = kvp.Value.DefaultValue
                        });
                    }

                    if (prevNestedField.IsNestedCoreData && kvp.Value.IsNestedCoreData)
                    {
                        CompareNestedFieldsRecursive($"{path}.{kvp.Key}", prevNestedField, kvp.Value, result);
                    }
                }
            }
        }

        /// <summary>
        /// 이름 변경 가능성 감지 (휴리스틱)
        /// </summary>
        private string FindPossibleRename(SchemaField newField, Dictionary<string, SchemaField> previousFields, Dictionary<string, SchemaField> currentFields)
        {
            // 조건:
            // 1. 이전에 있었지만 현재 없는 필드 중에서
            // 2. 타입이 동일하고
            // 3. 이름 유사도가 높은 필드

            var deletedFields = previousFields
                .Where(p => !currentFields.ContainsKey(p.Key))
                .ToList();

            foreach (var deleted in deletedFields)
            {
                // 타입이 동일한지 확인
                if (deleted.Value.TypeScriptType != newField.TypeScriptType)
                    continue;

                // 이름 유사도 체크
                if (CalculateNameSimilarity(deleted.Key, newField.Name) > 0.6f)
                {
                    return deleted.Key;
                }
            }

            return null;
        }

        /// <summary>
        /// 이름 유사도 계산 (0~1)
        /// </summary>
        private float CalculateNameSimilarity(string oldName, string newName)
        {
            // 공통 접두사/접미사 체크
            string oldLower = oldName.ToLowerInvariant();
            string newLower = newName.ToLowerInvariant();

            // 정확히 같은 경우
            if (oldLower == newLower)
                return 1.0f;

            // 접두사가 같은 경우 (예: SetIndex_GachaDatas -> SetIndex_NewGachaDatas)
            int commonPrefixLength = GetCommonPrefixLength(oldLower, newLower);
            int commonSuffixLength = GetCommonSuffixLength(oldLower, newLower);

            int maxLength = Math.Max(oldLower.Length, newLower.Length);
            float similarity = (float)(commonPrefixLength + commonSuffixLength) / maxLength;

            // 부분 문자열 포함 체크
            if (oldLower.Contains(newLower) || newLower.Contains(oldLower))
            {
                float containSimilarity = (float)Math.Min(oldLower.Length, newLower.Length) / maxLength;
                similarity = Math.Max(similarity, containSimilarity);
            }

            return similarity;
        }

        private int GetCommonPrefixLength(string a, string b)
        {
            int length = 0;
            int minLen = Math.Min(a.Length, b.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (a[i] == b[i])
                    length++;
                else
                    break;
            }
            return length;
        }

        private int GetCommonSuffixLength(string a, string b)
        {
            int length = 0;
            int minLen = Math.Min(a.Length, b.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (a[a.Length - 1 - i] == b[b.Length - 1 - i])
                    length++;
                else
                    break;
            }
            return length;
        }

        /// <summary>
        /// 변경사항에 따른 새 버전 결정 (Semantic Versioning)
        /// </summary>
        /// <param name="majorVersionOverride">-1이면 자동, 0 이상이면 해당 값으로 MAJOR 버전 설정</param>
        private string DetermineNewVersion(string currentVersion, List<SchemaChange> changes, int majorVersionOverride = -1)
        {
            if (changes.Count == 0)
                return currentVersion;

            string[] parts = currentVersion.Split('.');
            int major = int.Parse(parts[0]);
            int minor = int.Parse(parts[1]);
            int patch = int.Parse(parts[2]);

            // MAJOR 버전 Override 적용
            if (majorVersionOverride >= 0)
            {
                // MAJOR가 변경되면 MINOR, PATCH는 0으로 리셋
                if (majorVersionOverride != major)
                {
                    return $"{majorVersionOverride}.0.0";
                }
                // 같은 MAJOR면 아래 로직으로 MINOR/PATCH 처리
            }

            // 변경 유형에 따른 버전 증가 (중첩 필드 변경 포함)
            bool hasBreakingChange = changes.Any(c =>
                c.Type == SchemaChangeType.FieldRemoved ||
                c.Type == SchemaChangeType.FieldTypeChanged ||
                c.Type == SchemaChangeType.FieldRenamed ||
                c.Type == SchemaChangeType.NestedFieldRemoved ||
                c.Type == SchemaChangeType.NestedFieldTypeChanged ||
                c.Type == SchemaChangeType.NestedFieldRenamed);

            bool hasAddition = changes.Any(c =>
                c.Type == SchemaChangeType.FieldAdded ||
                c.Type == SchemaChangeType.NestedFieldAdded);

            if (hasBreakingChange)
            {
                // Breaking change: minor 증가
                minor++;
                patch = 0;
            }
            else if (hasAddition)
            {
                // 필드 추가: patch 증가
                patch++;
            }

            return $"{major}.{minor}.{patch}";
        }

        /// <summary>
        /// 변경사항 요약 문자열 생성
        /// </summary>
        public string GenerateChangeSummary(SchemaCompareResult result)
        {
            if (!result.HasChanges)
                return $"{result.CoreDataName}: 변경 없음";

            var lines = new List<string>
            {
                $"{result.CoreDataName}: {result.PreviousVersion} -> {result.NewVersion}"
            };

            foreach (var change in result.Changes)
            {
                switch (change.Type)
                {
                    case SchemaChangeType.FieldAdded:
                        lines.Add($"  + 추가: {change.FieldName} ({change.NewType})");
                        break;
                    case SchemaChangeType.FieldRemoved:
                        lines.Add($"  - 삭제: {change.FieldName} ({change.OldType})");
                        break;
                    case SchemaChangeType.FieldRenamed:
                        lines.Add($"  ~ 이름변경: {change.OldFieldName} -> {change.FieldName}");
                        break;
                    case SchemaChangeType.FieldTypeChanged:
                        lines.Add($"  ! 타입변경: {change.FieldName} ({change.OldType} -> {change.NewType})");
                        break;
                    // 중첩 필드 변경
                    case SchemaChangeType.NestedFieldAdded:
                        lines.Add($"  + 추가 (중첩): {change.NestedFieldPath} ({change.NewType})");
                        break;
                    case SchemaChangeType.NestedFieldRemoved:
                        lines.Add($"  - 삭제 (중첩): {change.NestedFieldPath} ({change.OldType})");
                        break;
                    case SchemaChangeType.NestedFieldRenamed:
                        lines.Add($"  ~ 이름변경 (중첩): {change.NestedFieldPath}");
                        break;
                    case SchemaChangeType.NestedFieldTypeChanged:
                        lines.Add($"  ! 타입변경 (중첩): {change.NestedFieldPath} ({change.OldType} -> {change.NewType})");
                        break;
                }
            }

            return string.Join("\n", lines);
        }
    }
}
