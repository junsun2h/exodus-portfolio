using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// MOD 변경 이력 추적 시스템
    /// - 할당 변경사항 감지 (추가/제거/이동)
    /// - 코드 작업 TODO 명세서 생성
    /// - 변경 이력 저장 및 비교
    /// </summary>
    public static class ModChangeTracker
    {
        private static string snapshotPath = "Assets/Editor/BattleSimulator/Data/Reports/mod_snapshot.json";

        /// <summary>
        /// 변경 유형
        /// </summary>
        public enum ChangeType
        {
            Added,      // 새로 추가된 할당
            Removed,    // 제거된 할당
            Moved,      // 다른 단계로 이동
            None        // 변경 없음
        }

        /// <summary>
        /// MOD 변경 항목
        /// </summary>
        [Serializable]
        public class ModChange
        {
            public string modName;
            public int modId;
            public ChangeType changeType;
            public string oldStageId;
            public string oldStageName;
            public string newStageId;
            public string newStageName;
            public bool wasImplemented;
            public string oldCodeLocation;
            public DateTime changeTime;

            public string GetDescription()
            {
                switch (changeType)
                {
                    case ChangeType.Added:
                        return $"[추가] {modName}\n  → {newStageName}";

                    case ChangeType.Removed:
                        return $"[제거] {modName}\n  ← {oldStageName}";

                    case ChangeType.Moved:
                        return $"[이동] {modName}\n  {oldStageName} → {newStageName}";

                    default:
                        return $"{modName} (변경 없음)";
                }
            }

            public string GetCodeTodo()
            {
                switch (changeType)
                {
                    case ChangeType.Added:
                        return $"□ [{newStageName}] {modName} 구현 추가\n" +
                               $"  - 코드 작성 필요\n" +
                               $"  - 작성 후 '🔎 코드 스캔' 실행\n";

                    case ChangeType.Removed:
                        if (wasImplemented)
                        {
                            return $"□ [{oldStageName}] {modName} 구현 제거\n" +
                                   $"  - 코드 제거: {oldCodeLocation}\n" +
                                   $"  - implemented → false\n" +
                                   $"  - code_location → null\n";
                        }
                        else
                        {
                            return $"□ [{oldStageName}] {modName} 할당 해제\n" +
                                   $"  - 구현 안 되어 있었음 (코드 작업 불필요)\n";
                        }

                    case ChangeType.Moved:
                        if (wasImplemented)
                        {
                            return $"□ [이동] {modName}\n" +
                                   $"  - 기존 코드 제거: {oldCodeLocation} ({oldStageName})\n" +
                                   $"  - 새 코드 추가: {newStageName}\n" +
                                   $"  - 작성 후 '🔎 코드 스캔' 실행\n";
                        }
                        else
                        {
                            return $"□ [이동] {modName}\n" +
                                   $"  - {oldStageName} → {newStageName}\n" +
                                   $"  - 새 위치에 코드 작성 필요\n";
                        }

                    default:
                        return "";
                }
            }
        }

        /// <summary>
        /// 변경 이력 스냅샷
        /// </summary>
        [Serializable]
        public class ModSnapshot
        {
            public string timestamp;
            public List<ModAssignmentRecord> assignments;

            public ModSnapshot()
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                assignments = new List<ModAssignmentRecord>();
            }
        }

        [Serializable]
        public class ModAssignmentRecord
        {
            public string modName;
            public int modId;
            public string stageId;
            public string stageName;
            public bool implemented;
            public string codeLocation;
        }

        /// <summary>
        /// 현재 상태 스냅샷 생성
        /// </summary>
        public static ModSnapshot CreateSnapshot()
        {
            var snapshot = new ModSnapshot();
            var allMods = ModStageMetadata.GetAllMods();

            foreach (var mod in allMods)
            {
                if (mod.assigned_stages != null && mod.assigned_stages.Count > 0)
                {
                    foreach (var stage in mod.assigned_stages)
                    {
                        // implementations 배열에서 해당 stage 정보 찾기
                        bool implemented = false;
                        string codeLocation = "";

                        if (mod.implementation_status?.implementations != null)
                        {
                            var impl = mod.implementation_status.implementations
                                .FirstOrDefault(i => i.formula_type == stage.formula_type && i.stage_id == stage.stage_id);

                            if (impl != null)
                            {
                                implemented = impl.implemented;
                                codeLocation = impl.code_location ?? "";
                            }
                        }

                        snapshot.assignments.Add(new ModAssignmentRecord
                        {
                            modName = mod.mod_name,
                            modId = mod.mod_id,
                            stageId = stage.stage_id,
                            stageName = stage.stage_name,
                            implemented = implemented,
                            codeLocation = codeLocation
                        });
                    }
                }
            }

            return snapshot;
        }

        /// <summary>
        /// 스냅샷 저장
        /// </summary>
        public static void SaveSnapshot()
        {
            var snapshot = CreateSnapshot();
            string json = JsonUtility.ToJson(snapshot, true);
            File.WriteAllText(snapshotPath, json);
        }

        /// <summary>
        /// 스냅샷 로드
        /// </summary>
        public static ModSnapshot LoadSnapshot()
        {
            if (!File.Exists(snapshotPath))
            {
                return null;
            }

            string json = File.ReadAllText(snapshotPath);
            var snapshot = JsonUtility.FromJson<ModSnapshot>(json);
            return snapshot;
        }

        /// <summary>
        /// 변경사항 감지
        /// </summary>
        public static List<ModChange> DetectChanges()
        {
            var oldSnapshot = LoadSnapshot();
            if (oldSnapshot == null)
            {
                return new List<ModChange>();
            }

            var newSnapshot = CreateSnapshot();
            var changes = new List<ModChange>();

            // 이전 상태 딕셔너리 생성 (key: modName_stageId)
            var oldDict = oldSnapshot.assignments.ToDictionary(
                a => $"{a.modName}_{a.stageId}",
                a => a
            );

            // 현재 상태 딕셔너리 생성
            var newDict = newSnapshot.assignments.ToDictionary(
                a => $"{a.modName}_{a.stageId}",
                a => a
            );

            // 모든 MOD 이름 수집
            var allModNames = oldSnapshot.assignments
                .Select(a => a.modName)
                .Union(newSnapshot.assignments.Select(a => a.modName))
                .Distinct()
                .ToList();

            foreach (var modName in allModNames)
            {
                var oldAssignments = oldSnapshot.assignments.Where(a => a.modName == modName).ToList();
                var newAssignments = newSnapshot.assignments.Where(a => a.modName == modName).ToList();

                // 케이스 1: 새로 추가된 할당
                foreach (var newAssign in newAssignments)
                {
                    string key = $"{newAssign.modName}_{newAssign.stageId}";
                    if (!oldDict.ContainsKey(key))
                    {
                        changes.Add(new ModChange
                        {
                            modName = newAssign.modName,
                            modId = newAssign.modId,
                            changeType = ChangeType.Added,
                            newStageId = newAssign.stageId,
                            newStageName = newAssign.stageName,
                            wasImplemented = false,
                            changeTime = DateTime.Now
                        });
                    }
                }

                // 케이스 2: 제거된 할당
                foreach (var oldAssign in oldAssignments)
                {
                    string key = $"{oldAssign.modName}_{oldAssign.stageId}";
                    if (!newDict.ContainsKey(key))
                    {
                        changes.Add(new ModChange
                        {
                            modName = oldAssign.modName,
                            modId = oldAssign.modId,
                            changeType = ChangeType.Removed,
                            oldStageId = oldAssign.stageId,
                            oldStageName = oldAssign.stageName,
                            wasImplemented = oldAssign.implemented,
                            oldCodeLocation = oldAssign.codeLocation,
                            changeTime = DateTime.Now
                        });
                    }
                }

                // 케이스 3: 단계 이동 (이전에 있었고, 지금도 있지만 다른 단계)
                if (oldAssignments.Count == 1 && newAssignments.Count == 1)
                {
                    var oldAssign = oldAssignments[0];
                    var newAssign = newAssignments[0];

                    if (oldAssign.stageId != newAssign.stageId)
                    {
                        changes.Add(new ModChange
                        {
                            modName = oldAssign.modName,
                            modId = oldAssign.modId,
                            changeType = ChangeType.Moved,
                            oldStageId = oldAssign.stageId,
                            oldStageName = oldAssign.stageName,
                            newStageId = newAssign.stageId,
                            newStageName = newAssign.stageName,
                            wasImplemented = oldAssign.implemented,
                            oldCodeLocation = oldAssign.codeLocation,
                            changeTime = DateTime.Now
                        });
                    }
                }
            }

            return changes;
        }

        /// <summary>
        /// TODO 명세서 생성
        /// </summary>
        public static string GenerateTodoSpec(List<ModChange> changes)
        {
            if (changes.Count == 0)
            {
                return "✅ 변경사항이 없습니다.\n\n" +
                       "MOD 할당 변경 후 이 기능을 사용하세요.";
            }

            var spec = "====================================\n";
            spec += "   MOD 변경사항 코드 작업 TODO\n";
            spec += "====================================\n\n";

            var oldSnapshot = LoadSnapshot();
            spec += $"📅 기준 시점: {oldSnapshot?.timestamp ?? "알 수 없음"}\n";
            spec += $"📅 현재 시점: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
            spec += $"📊 총 변경: {changes.Count}개\n\n";

            // 변경 유형별 그룹화
            var added = changes.Where(c => c.changeType == ChangeType.Added).ToList();
            var removed = changes.Where(c => c.changeType == ChangeType.Removed).ToList();
            var moved = changes.Where(c => c.changeType == ChangeType.Moved).ToList();

            spec += $"➕ 추가: {added.Count}개\n";
            spec += $"➖ 제거: {removed.Count}개\n";
            spec += $"🔄 이동: {moved.Count}개\n\n";

            spec += "====================================\n";
            spec += "  1️⃣ 추가된 MOD - 코드 구현 필요\n";
            spec += "====================================\n\n";

            if (added.Count > 0)
            {
                var groupedAdded = added.GroupBy(c => c.newStageName);
                foreach (var group in groupedAdded.OrderBy(g => g.Key))
                {
                    spec += $"## {group.Key}\n\n";
                    foreach (var change in group)
                    {
                        spec += change.GetCodeTodo() + "\n";
                    }
                }
            }
            else
            {
                spec += "변경사항 없음\n\n";
            }

            spec += "====================================\n";
            spec += "  2️⃣ 제거된 MOD - 코드 정리 필요\n";
            spec += "====================================\n\n";

            if (removed.Count > 0)
            {
                // 구현된 것과 안 된 것 분리
                var implementedRemoved = removed.Where(c => c.wasImplemented).ToList();
                var notImplementedRemoved = removed.Where(c => !c.wasImplemented).ToList();

                if (implementedRemoved.Count > 0)
                {
                    spec += "⚠️ 구현된 MOD 제거 (코드 작업 필요)\n\n";
                    foreach (var change in implementedRemoved)
                    {
                        spec += change.GetCodeTodo() + "\n";
                    }
                }

                if (notImplementedRemoved.Count > 0)
                {
                    spec += "ℹ️ 미구현 MOD 제거 (코드 작업 불필요)\n\n";
                    foreach (var change in notImplementedRemoved)
                    {
                        spec += $"□ {change.modName} - {change.oldStageName}\n";
                    }
                    spec += "\n";
                }
            }
            else
            {
                spec += "변경사항 없음\n\n";
            }

            spec += "====================================\n";
            spec += "  3️⃣ 이동된 MOD - 코드 재배치 필요\n";
            spec += "====================================\n\n";

            if (moved.Count > 0)
            {
                foreach (var change in moved)
                {
                    spec += change.GetCodeTodo() + "\n";
                }
            }
            else
            {
                spec += "변경사항 없음\n\n";
            }

            spec += "====================================\n";
            spec += "  📋 작업 순서 권장\n";
            spec += "====================================\n\n";
            spec += "1. 제거된 MOD 코드 정리 (기존 코드 삭제)\n";
            spec += "2. 이동된 MOD 코드 재배치\n";
            spec += "3. 추가된 MOD 코드 구현\n";
            spec += "4. 🔎 코드 스캔으로 구현 상태 검증\n";
            spec += "5. 📸 스냅샷 저장으로 변경사항 확정\n\n";

            spec += "====================================\n";
            spec += "  ⚠️ 제거 작업 시 주의사항\n";
            spec += "====================================\n\n";
            spec += "제거된 MOD의 코드를 삭제한 후:\n";
            spec += "1. JSON 메타데이터를 직접 수정하거나\n";
            spec += "2. MOD 할당기에서 해당 MOD를 찾아서\n";
            spec += "   - implemented 체크 해제\n";
            spec += "   - code_location 비우기\n";
            spec += "3. 또는 전체 '🔎 코드 스캔' 실행 (자동 업데이트)\n\n";

            return spec;
        }

        /// <summary>
        /// 변경사항 요약
        /// </summary>
        public static string GetChangeSummary(List<ModChange> changes)
        {
            if (changes.Count == 0)
                return "변경사항 없음";

            var added = changes.Count(c => c.changeType == ChangeType.Added);
            var removed = changes.Count(c => c.changeType == ChangeType.Removed);
            var moved = changes.Count(c => c.changeType == ChangeType.Moved);

            return $"총 {changes.Count}개 변경 (➕{added} ➖{removed} 🔄{moved})";
        }
    }
}
