using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using PX;
using Game.Editor.BattleSimulator;

namespace BattleSimulator
{
    /// <summary>
    /// BattleSimulatorWindow - MOD 관리 탭 (Tab 3, 4, 5)
    /// </summary>
    public partial class BattleSimulatorWindow
    {
        #region Tab 3: 📐 MOD 할당기

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [Button(ButtonSizes.Large, Name = "🔄 메타데이터 새로고침")]
        private void RefreshModMetadata()
        {
            ModStageMetadata.Load();
            UpdateDamageFormulaView();
            UpdateUnimplementedModStats();
        }

        // 미구현 MOD 통계
        private int totalEModCount = 0;
        private int implementedModCount = 0;
        private int unimplementedModCount = 0;
        private int totalAssignedModCount = 0;

        private string modImplementationSummary =>
            $"1. EMod 전체 개수: {totalEModCount}개\n" +
            $"2. 전체 할당: {totalAssignedModCount}개\n" +
            $"3. 구현 완료: {implementedModCount}개\n" +
            $"4. 미구현: {unimplementedModCount}개";

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [PropertySpace(10)]
        [InfoBox("$modImplementationSummary", InfoMessageType.Info)]
        [Button(ButtonSizes.Medium, Name = "📊 구현 현황")]
        private void ShowImplementationStats()
        {
            // InfoBox 표시를 위한 더미 버튼
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [FoldoutGroup("Tabs/📐 MOD 할당기/Controls/미구현 MOD 목록", false)]
        [ShowIf("@unimplementedModCount > 0")]
        [ListDrawerSettings(
            HideAddButton = true,
            HideRemoveButton = true,
            DraggableItems = false,
            ShowIndexLabels = false,
            ShowPaging = true,
            NumberOfItemsPerPage = 10
        )]
        [LabelText("미구현 MOD")]
        public List<UnimplementedModInfo> unimplementedMods = new List<UnimplementedModInfo>();

        /// <summary>
        /// 미구현 MOD 정보 표시용 클래스
        /// </summary>
        [System.Serializable]
        public class UnimplementedModInfo
        {
            [HorizontalGroup("Info")]
            [LabelText("MOD (복사 가능)"), LabelWidth(250)]
            public string modName;

            [HorizontalGroup("Info")]
            [LabelText("Stage"), LabelWidth(300)]
            [ReadOnly]
            public string stageName;

            [PropertySpace(5)]
            [LabelText("권장 코드")]
            [ReadOnly, MultiLineProperty(2)]
            public string suggestedCode;

            [LabelText("구현 위치")]
            [ReadOnly]
            public string codeLocation;
        }

        /// <summary>
        /// 미구현 MOD 통계 업데이트
        /// </summary>
        private void UpdateUnimplementedModStats()
        {
            // EMod enum 전체 개수 (EMod.None 제외)
            totalEModCount = System.Enum.GetValues(typeof(EMod)).Length - 1;

            // 전체 MOD 데이터 로드
            var allMods = ModStageMetadata.GetAllMods();

            // 할당된 MOD 수 계산 (assigned_stages가 있는 MOD만)
            totalAssignedModCount = allMods.Count(m => m.assigned_stages != null && m.assigned_stages.Count > 0);

            // 미구현 MOD 목록 가져오기
            var tasks = ModImplementationHelper.GetUnimplementedTasks();
            unimplementedModCount = tasks.Count;

            // 구현된 MOD 수 계산
            implementedModCount = totalAssignedModCount - unimplementedModCount;

            // 미구현 MOD 목록 업데이트
            unimplementedMods.Clear();
            foreach (var task in tasks)
            {
                unimplementedMods.Add(new UnimplementedModInfo
                {
                    modName = task.modName,
                    stageName = task.stageName,
                    suggestedCode = task.suggestedCode,
                    codeLocation = ModImplementationHelper.SuggestImplementationLocation(task.modName, task.stageId)
                });
            }
        }

        // 워크플로우: 전체 실행 (자동 할당 + Stage별 MOD 추출 + 코드 구현 확인)
        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [Button(ButtonSizes.Large, Name = "🚀 전체 실행 (할당 → 추출 → 구현확인)")]
        [GUIColor(0.5f, 1f, 0.5f)]
        private void ExecuteFullWorkflow()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "전체 워크플로우 실행 확인",
                "다음 작업을 순차적으로 실행합니다:\n\n" +
                "1️⃣ 자동 할당: 379개 MOD를 네이밍 패턴 기반으로 재할당\n" +
                "2️⃣ Stage별 MOD 추출: 할당 결과를 마크다운 파일로 추출\n" +
                "3️⃣ 코드 구현 확인: 코드베이스 스캔하여 구현 상태 업데이트\n\n" +
                "⚠️ 기존 수동 수정 내용이 덮어씌워질 수 있습니다.\n\n" +
                "계속하시겠습니까?",
                "전체 실행",
                "취소"
            );

            if (!confirm) return;

            // 1단계: 자동 할당
            var assignResult = ModStageAutoMapper.AutoAssignAllMods(overwriteExisting: true);
            ModStageMetadata.Save();
            UpdateDamageFormulaView();

            // 2단계: Stage별 MOD 추출
            bool exportSuccess = ExportStageModListInternal();
            if (!exportSuccess)
            {
                Debug.LogError("[전체 실행] 2/3 - Stage별 MOD 추출 실패 (Python 미설치 또는 오류)");
            }

            // 3단계: 코드 구현 확인
            var scanResult = ModCodeScanner.ScanCodebase();
            int updatedCount = ModCodeScanner.ApplyToMetadata(scanResult);
            UpdateDamageFormulaView();

            // 최종 결과 표시
            string summary = $"✅ 전체 워크플로우 실행 완료\n\n" +
                           $"1️⃣ 자동 할당: {assignResult.successCount}개 성공 / {assignResult.totalMods}개 전체\n" +
                           $"2️⃣ Stage별 MOD 추출: {(exportSuccess ? "성공" : "실패")}\n" +
                           $"3️⃣ 코드 구현 확인: {updatedCount}개 MOD 업데이트\n\n" +
                           ModCodeScanner.GetSummary(scanResult);

            EditorUtility.DisplayDialog("전체 실행 완료", summary, "확인");

            // 미구현 MOD 통계 업데이트
            UpdateUnimplementedModStats();
        }

        // 개별 실행: 1. 자동 할당
        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [FoldoutGroup("Tabs/📐 MOD 할당기/Controls/개별 실행", false)]
        [HorizontalGroup("Tabs/📐 MOD 할당기/Controls/개별 실행/Row1")]
        [Button(ButtonSizes.Medium, Name = "🤖 자동 할당")]
        [GUIColor(0.4f, 0.8f, 1f)]
        private void AutoAssignMods()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "전체 재할당 확인",
                "379개 MOD를 네이밍 패턴 기반으로 전체 재할당합니다.\n\n" +
                "⚠️ 기존에 할당된 모든 MOD를 새로 할당합니다!\n" +
                "수동으로 수정한 내용이 있다면 덮어씌워집니다.\n\n" +
                "계속하시겠습니까?",
                "전체 재할당",
                "취소"
            );

            if (!confirm) return; // 취소

            var result = ModStageAutoMapper.AutoAssignAllMods(overwriteExisting: true);

            // 저장
            ModStageMetadata.Save();

            // 뷰 업데이트
            UpdateDamageFormulaView();

            // 결과 표시
            EditorUtility.DisplayDialog(
                "자동 할당 완료",
                result.GetSummary(),
                "확인"
            );
        }

        // 내부 헬퍼: Stage별 MOD 추출 (반환값으로 성공 여부)
        private bool ExportStageModListInternal()
        {
            string pythonPath = "python";
            string scriptPath = Path.Combine(Application.dataPath, "Editor", "BattleSimulator", "Scripts", "ExportStageModList.py");
            string outputPath = Path.Combine(Application.dataPath, "Editor", "BattleSimulator", "Data", "ModAssigner", "modAssignByAllStage.md");

            if (!File.Exists(scriptPath))
            {
                Debug.LogError($"스크립트 파일을 찾을 수 없습니다: {scriptPath}");
                return false;
            }

            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(Application.dataPath)
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"Python 스크립트 실행 오류:\n{error}");
                        return false;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Python 실행 오류: {ex.Message}");
                return false;
            }
        }

        // 개별 실행: 2. Stage별 MOD 추출
        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [FoldoutGroup("Tabs/📐 MOD 할당기/Controls/개별 실행", false)]
        [HorizontalGroup("Tabs/📐 MOD 할당기/Controls/개별 실행/Row1")]
        [Button(ButtonSizes.Medium, Name = "📄 Stage별 MOD 추출")]
        [GUIColor(0.7f, 0.9f, 1f)]
        private void ExportStageModList()
        {
            bool success = ExportStageModListInternal();

            if (success)
            {
                string outputPath = Path.Combine(Application.dataPath, "Editor", "BattleSimulator", "Data", "ModAssigner", "modAssignByAllStage.md");
                EditorUtility.DisplayDialog("추출 완료",
                    $"Stage별 MOD 목록이 추출되었습니다.\n\n파일 위치:\n{outputPath}",
                    "확인");
            }
            else
            {
                EditorUtility.DisplayDialog("오류",
                    "Stage별 MOD 추출에 실패했습니다.\n\n" +
                    "Python이 설치되어 있고 PATH에 등록되어 있는지 확인하세요.\n" +
                    "자세한 내용은 Console 로그를 확인하세요.",
                    "확인");
            }
        }

        // 개별 실행: 3. 코드 구현 확인
        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [FoldoutGroup("Tabs/📐 MOD 할당기/Controls/개별 실행", false)]
        [HorizontalGroup("Tabs/📐 MOD 할당기/Controls/개별 실행/Row1")]
        [Button(ButtonSizes.Medium, Name = "🔎 코드 구현 확인")]
        [GUIColor(0.5f, 1f, 0.7f)]
        private void ScanCodeForModUsage()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "코드 구현 확인",
                "코드베이스를 스캔해서 MOD 구현 상태를 자동으로 업데이트하시겠습니까?\n\n" +
                "- BattleStatus.cs, SkillData.cs 등을 스캔합니다\n" +
                "- EMod 사용 위치를 자동으로 찾아서 코드 위치를 기록합니다\n" +
                "- 구현됨 체크박스를 자동으로 설정합니다",
                "스캔 시작",
                "취소"
            );

            if (!confirmed) return;

            var scanResult = ModCodeScanner.ScanCodebase();
            int updatedCount = ModCodeScanner.ApplyToMetadata(scanResult);

            // 뷰 업데이트
            UpdateDamageFormulaView();
            UpdateUnimplementedModStats();

            // 결과 표시
            string summary = ModCodeScanner.GetSummary(scanResult);
            summary += $"\n\n메타데이터 업데이트: {updatedCount}개 MOD";

            EditorUtility.DisplayDialog(
                "코드 구현 확인 완료",
                summary,
                "확인"
            );
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [FoldoutGroup("Tabs/📐 MOD 할당기/Controls/개별 실행", false)]
        [HorizontalGroup("Tabs/📐 MOD 할당기/Controls/개별 실행/Row2")]
        [Button(ButtonSizes.Medium, Name = "📋 미구현 MOD 확인")]
        [GUIColor(1f, 0.9f, 0.5f)]
        private void ShowImplementationGuide()
        {
            var tasks = ModImplementationHelper.GetUnimplementedTasks();
            string guide = ModImplementationHelper.GenerateImplementationGuide(tasks);

            EditorUtility.DisplayDialog(
                "미구현 MOD 확인",
                guide,
                "확인"
            );
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [FoldoutGroup("Tabs/📐 MOD 할당기/Controls/개별 실행", false)]
        [HorizontalGroup("Tabs/📐 MOD 할당기/Controls/개별 실행/Row2")]
        [Button(ButtonSizes.Medium, Name = "💾 편집 저장")]
        [GUIColor(1f, 0.7f, 0.7f)]
        private void SaveModMetadata()
        {
            ModStageMetadata.Save();
            EditorUtility.DisplayDialog("저장 완료", "MOD 메타데이터가 저장되었습니다.", "확인");
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [BoxGroup("Tabs/📐 MOD 할당기/Controls")]
        [PropertySpace(10)]
        [Button(ButtonSizes.Large, Name = "⚖️ 이론적 밸런스 분석 리포트 (현재 빌드)")]
        [GUIColor(0.8f, 0.6f, 1f)]
        private void ExportTheoreticalBalanceReportButton()
        {
            ExportTheoreticalBalanceReport();
        }

        // ====================================
        // ⚔️ Player → Monster 루트 그룹
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster", expanded: true)]
        [HideLabel]
        public string playerToMonsterRoot = "";

        // ====================================
        // Spell Damage MOD 목록
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]", expanded: false)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집", expanded: false)]
        [InfoBox("스킬 기본 피해 + flat MOD 합산\n공식: base_damage = skill_base_damage + Σ(flat_mods)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage1Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod stage1NewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToStage1()
        {
            if (stage1NewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }

            ModStageMetadata.AssignModToStage(
                stage1NewMod,
                "player_to_monster.spell_damage",
                "1_base_flat_collection",
                "[Spell] 1: 기본 플랫 수집"
            );
            UpdateDamageFormulaView();
            stage1NewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집/RemoveMod")]
        [ValueDropdown("GetStage1ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod stage1RemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 1단계: 기본 플랫 수집/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromStage1()
        {
            if (stage1RemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }

            ModStageMetadata.UnassignModFromStage(
                stage1RemoveMod,
                "player_to_monster.spell_damage",
                "1_base_flat_collection"
            );
            UpdateDamageFormulaView();
            stage1RemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetStage1ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("spell_damage", "1_base_flat_collection");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None)
                       .Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 2단계: 스킬 효과", expanded: false)]
        [InfoBox("스킬별 효과 배율 적용 (스킬 내부 로직)\n공식: effective_damage = base_damage × skill_effectiveness\n※ MOD 직접 할당 없음")]
        [ReadOnly]
        [LabelText("할당된 MOD")]
        public string stage2Info = "스킬 시스템 내부 처리 (MOD 할당 없음)";

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용", expanded: false)]
        [InfoBox("모든 Increased MOD 합산 후 한 번 곱셈\n공식: inc_damage = effective_damage × (1 + Σ(inc_mods) × 0.01)\n코드: ModCalculator.cs:AddInc(), Calculate() line 96")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage3Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod stage3NewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToStage3()
        {
            if (stage3NewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }

            ModStageMetadata.AssignModToStage(
                stage3NewMod,
                "player_to_monster.spell_damage",
                "3_increased_application",
                "[Spell] 3: Increased 적용"
            );
            UpdateDamageFormulaView();
            stage3NewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용", expanded: false)]
        [InfoBox("More MOD 개별 곱셈\n공식: more_damage = inc_damage × ∏(1 + more_i × 0.01)\n코드: ModCalculator.cs:AddMore(), Calculate() line 99-114")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage4Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod stage4NewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToStage4()
        {
            if (stage4NewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }

            ModStageMetadata.AssignModToStage(
                stage4NewMod,
                "player_to_monster.spell_damage",
                "4_more_application",
                "[Spell] 4: More 적용"
            );
            UpdateDamageFormulaView();
            stage4NewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타", expanded: false)]
        [InfoBox("치명타 확률 계산 및 배율 적용\n공식: crit_damage = more_damage × (is_crit ? crit_multiplier : 1.0)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage5Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS", expanded: false)]
        [InfoBox("시전 속도 증가 MOD 및 DPS 계산\n공식: dps = average_hit_damage × cast_speed")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage6Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해", expanded: false)]
        [InfoBox("Double/Triple 피해 확률 기반 평균 피해 배율\n공식: expected_damage = damage × (1 + double_chance × 1.0 + triple_chance × 2.0)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage7Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 8단계: 투사체 메커닉", expanded: false)]
        [InfoBox("투사체 추가 발사, 연쇄, 관통, 속도 등 메커닉\n주의: 일부 MOD는 클라이언트 로직 구현 필요")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage8Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 9단계: 스킬 메커닉", expanded: false)]
        [InfoBox("스킬 범위 (AoE Radius) 및 쿨타임 (Cooltime) 관련 MOD\nAoE 4개 + Cooltime 2개")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage9Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 10단계: 적 처치 시 발동 효과", expanded: false)]
        [InfoBox("적 처치 시 폭발 (5가지 속성) 및 회복 (HP/MP) 효과\n폭발: 최대 생명력 10% 피해, 1m 범위")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> stage10Mods = new List<ModDisplayInfo>();

        // ====================================
        // Curse MOD 목록 + 관리
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Curse]", expanded: false)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Curse]/[Curse] 1단계: 저주 스킬 시전", expanded: false)]
        [InfoBox("펫이 시전하는 저주 스킬의 시전 능력 (시전 속도, 범위, 쿨다운, 슬롯)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> curseStage1Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Curse]/[Curse] 2단계: 저주 효과 적용", expanded: false)]
        [InfoBox("적에게 적용되는 저주의 지속시간 및 효과 배율")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> curseStage2Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Curse]/[Curse] 3단계: Ailment 시너지", expanded: false)]
        [InfoBox("저주받은 적에게 Ailment 확률 추가 (기존 Ailment 단계와 연동)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> curseStage3Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Curse]/[Curse] 4단계: 저항 약화", expanded: false)]
        [InfoBox("저주받은 적의 속성 저항 감소 (기존 Defense 단계와 연동)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> curseStage4Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Curse]/[Curse] 5단계: 전투 능력 약화", expanded: false)]
        [InfoBox("저주받은 적의 전투 능력 변화 (기존 Spell/Defense 단계와 연동)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> curseStage5Mods = new List<ModDisplayInfo>();

        // ====================================
        // Ailment Damage MOD 목록 + 관리
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]", expanded: false)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 0단계: Ailment 제한", expanded: false)]
        [InfoBox("상태이상 유발 제한 (Cannot Cause) MOD\nmod_cannot_cause_ailment (전체), mod_cannot_cause_* (개별 6개)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> ailmentStage0Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 1단계: 기본 Ailment 피해", expanded: false)]
        [InfoBox("이 단계는 [Spell] 1: 기본 플랫 수집의 계산 결과를 입력으로 사용합니다.\n\n" +
                 "계산식: ailment_base_damage = skill_hit_base_damage × ailment_damage_percent\n\n" +
                 "💡 MOD는 [Spell] 1단계(1_base_flat_collection)에서 이미 적용되므로, 이 단계에서는 별도 MOD 할당이 필요하지 않습니다.\n\n" +
                 "[Spell] 1단계에 할당된 MOD들:\n" +
                 "• mod_all_damage\n" +
                 "• mod_all_skill_damage\n" +
                 "• mod_elemental_damage\n" +
                 "• mod_physical_damage\n" +
                 "• mod_fire_damage\n" +
                 "• mod_lightning_damage\n" +
                 "• mod_cold_damage\n" +
                 "• mod_poison_damage",
                 InfoMessageType.Info)]
        [LabelText(" ")]
        [HideLabel]
        public string ailmentStage1MgtDescription = "";

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률", expanded: false)]
        [InfoBox("상태이상 유발 확률\n공식: proc_chance = base_chance × (1 + Σ(proc_chance_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> ailmentStage2Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod ailmentStage2MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToAilmentStageMgt2()
        {
            if (ailmentStage2MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(ailmentStage2MgtNewMod, "player_to_monster.ailment_damage", "2_ailment_proc_chance", "[Ailment] 2단계: Ailment 확률");
            UpdateDamageFormulaView();
            ailmentStage2MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률/RemoveMod")]
        [ValueDropdown("GetAilmentStageMgt2ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod ailmentStage2MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 2단계: Ailment 확률/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromAilmentStageMgt2()
        {
            if (ailmentStage2MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(ailmentStage2MgtRemoveMod, "player_to_monster.ailment_damage", "2_ailment_proc_chance");
            UpdateDamageFormulaView();
            ailmentStage2MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetAilmentStageMgt2ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("ailment_damage", "2_ailment_proc_chance");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간", expanded: false)]
        [InfoBox("상태이상 지속시간\n공식: duration = base_duration × (1 + Σ(duration_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> ailmentStage3Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod ailmentStage3MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToAilmentStageMgt3()
        {
            if (ailmentStage3MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(ailmentStage3MgtNewMod, "player_to_monster.ailment_damage", "3_ailment_duration", "[Ailment] 3단계: Ailment 지속시간");
            UpdateDamageFormulaView();
            ailmentStage3MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간/RemoveMod")]
        [ValueDropdown("GetAilmentStageMgt3ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod ailmentStage3MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 3단계: Ailment 지속시간/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromAilmentStageMgt3()
        {
            if (ailmentStage3MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(ailmentStage3MgtRemoveMod, "player_to_monster.ailment_damage", "3_ailment_duration");
            UpdateDamageFormulaView();
            ailmentStage3MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetAilmentStageMgt3ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("ailment_damage", "3_ailment_duration");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More", expanded: false)]
        [InfoBox("상태이상 피해 증가/증폭\n공식: final_ailment = base × (1 + ΣInc) × ∏(1 + More)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> ailmentStage4Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod ailmentStage4MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToAilmentStageMgt4()
        {
            if (ailmentStage4MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(ailmentStage4MgtNewMod, "player_to_monster.ailment_damage", "4_ailment_inc_more", "[Ailment] 4단계: Ailment Inc·More");
            UpdateDamageFormulaView();
            ailmentStage4MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More/RemoveMod")]
        [ValueDropdown("GetAilmentStageMgt4ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod ailmentStage4MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Ailment Damage]/[Ailment] 4단계: Ailment Inc·More/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromAilmentStageMgt4()
        {
            if (ailmentStage4MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(ailmentStage4MgtRemoveMod, "player_to_monster.ailment_damage", "4_ailment_inc_more");
            UpdateDamageFormulaView();
            ailmentStage4MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetAilmentStageMgt4ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("ailment_damage", "4_ailment_inc_more");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        // ====================================
        // Aura Damage MOD 목록 + 관리
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]", expanded: false)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해", expanded: false)]
        [InfoBox("지속 피해 기본 피해\n공식: dot_base = base_damage × dot_percent")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> dotBuffStage1Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod dotBuffStage1MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDotBuffStageMgt1()
        {
            if (dotBuffStage1MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(dotBuffStage1MgtNewMod, "player_to_monster.aura_damage", "1_base_aura_damage", "[Aura] 1단계: 기본 Aura 피해");
            UpdateDamageFormulaView();
            dotBuffStage1MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해/RemoveMod")]
        [ValueDropdown("GetDotBuffStageMgt1ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod dotBuffStage1MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 1단계: 기본 Aura 피해/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDotBuffStageMgt1()
        {
            if (dotBuffStage1MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(dotBuffStage1MgtRemoveMod, "player_to_monster.aura_damage", "1_base_aura_damage");
            UpdateDamageFormulaView();
            dotBuffStage1MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDotBuffStageMgt1ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("aura_damage", "1_base_aura_damage");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱", expanded: false)]
        [InfoBox("지속 피해 지속시간 및 틱 간격\n공식: total_damage = damage_per_tick × (duration / tick_interval)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> dotBuffStage2Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod dotBuffStage2MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDotBuffStageMgt2()
        {
            if (dotBuffStage2MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(dotBuffStage2MgtNewMod, "player_to_monster.aura_damage", "2_aura_duration_tick", "[Aura] 2단계: Aura 지속시간·틱");
            UpdateDamageFormulaView();
            dotBuffStage2MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱/RemoveMod")]
        [ValueDropdown("GetDotBuffStageMgt2ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod dotBuffStage2MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 2단계: Aura 지속시간·틱/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDotBuffStageMgt2()
        {
            if (dotBuffStage2MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(dotBuffStage2MgtRemoveMod, "player_to_monster.aura_damage", "2_aura_duration_tick");
            UpdateDamageFormulaView();
            dotBuffStage2MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDotBuffStageMgt2ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("aura_damage", "2_aura_duration_tick");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc", expanded: false)]
        [InfoBox("지속 피해 증가 배율\n공식: dot_inc = 1 + ΣInc")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> dotBuffStage3Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod dotBuffStage3MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDotBuffStageMgt3()
        {
            if (dotBuffStage3MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(dotBuffStage3MgtNewMod, "player_to_monster.aura_damage", "3_aura_inc", "[Aura] 3단계: Aura Inc");
            UpdateDamageFormulaView();
            dotBuffStage3MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc/RemoveMod")]
        [ValueDropdown("GetDotBuffStageMgt3ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod dotBuffStage3MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 3단계: Aura Inc/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDotBuffStageMgt3()
        {
            if (dotBuffStage3MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(dotBuffStage3MgtRemoveMod, "player_to_monster.aura_damage", "3_aura_inc");
            UpdateDamageFormulaView();
            dotBuffStage3MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDotBuffStageMgt3ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("aura_damage", "3_aura_inc");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More", expanded: false)]
        [InfoBox("지속 피해 증폭 배율\n공식: dot_more = ∏(1 + More)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> dotBuffStage4Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod dotBuffStage4MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDotBuffStageMgt4()
        {
            if (dotBuffStage4MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(dotBuffStage4MgtNewMod, "player_to_monster.aura_damage", "4_aura_more", "[Aura] 4단계: Aura More");
            UpdateDamageFormulaView();
            dotBuffStage4MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More/RemoveMod")]
        [ValueDropdown("GetDotBuffStageMgt4ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod dotBuffStage4MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Aura Damage]/[Aura] 4단계: Aura More/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDotBuffStageMgt4()
        {
            if (dotBuffStage4MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(dotBuffStage4MgtRemoveMod, "player_to_monster.aura_damage", "4_aura_more");
            UpdateDamageFormulaView();
            dotBuffStage4MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDotBuffStageMgt4ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("aura_damage", "4_aura_more");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        // ====================================
        // Defense MOD 목록 + 관리
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]", expanded: false)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)", expanded: false)]
        [InfoBox("명중 판정 - 회피 및 막기 확률\n공식: hit_success = 1 - (evasion_chance + block_chance)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> defenseStage7Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod defenseStage7MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDefenseStageMgt7()
        {
            if (defenseStage7MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(defenseStage7MgtNewMod, "player_to_monster.defense", "1_hit_determination", "[Defense]/[Defense] 1단계: 명중 판정 (회피·막기)");
            UpdateDamageFormulaView();
            defenseStage7MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)/RemoveMod")]
        [ValueDropdown("GetDefenseStageMgt7ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod defenseStage7MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 1단계: 명중 판정 (회피·막기)/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDefenseStageMgt7()
        {
            if (defenseStage7MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(defenseStage7MgtRemoveMod, "player_to_monster.defense", "1_hit_determination");
            UpdateDamageFormulaView();
            defenseStage7MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDefenseStageMgt7ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("player_to_monster.defense", "1_hit_determination");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크", expanded: false)]
        [InfoBox("속성별 면역 체크\n공식: damage = damage_type_immune ? 0 : damage")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> defenseStage8Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod defenseStage8MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDefenseStageMgt8()
        {
            if (defenseStage8MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(defenseStage8MgtNewMod, "player_to_monster.defense", "2_immunity_check", "[Defense]/[Defense] 2단계: 면역 체크");
            UpdateDamageFormulaView();
            defenseStage8MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크/RemoveMod")]
        [ValueDropdown("GetDefenseStageMgt8ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod defenseStage8MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 2단계: 면역 체크/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDefenseStageMgt8()
        {
            if (defenseStage8MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(defenseStage8MgtRemoveMod, "player_to_monster.defense", "2_immunity_check");
            UpdateDamageFormulaView();
            defenseStage8MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDefenseStageMgt8ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("player_to_monster.defense", "2_immunity_check");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)", expanded: false)]
        [InfoBox("물리 피해 방어력 경감\n공식: damage = damage × (1 - armor_reduction_rate)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> defenseStage9Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod defenseStage9MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDefenseStageMgt9()
        {
            if (defenseStage9MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(defenseStage9MgtNewMod, "player_to_monster.defense", "3_armor_application", "[Defense]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)");
            UpdateDamageFormulaView();
            defenseStage9MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)/RemoveMod")]
        [ValueDropdown("GetDefenseStageMgt9ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod defenseStage9MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 3단계: 방어력 적용 (물리 피해 경감)/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDefenseStageMgt9()
        {
            if (defenseStage9MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(defenseStage9MgtRemoveMod, "player_to_monster.defense", "3_armor_application");
            UpdateDamageFormulaView();
            defenseStage9MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDefenseStageMgt9ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("player_to_monster.defense", "3_armor_application");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)", expanded: false)]
        [InfoBox("원소 피해 저항 경감\n공식: damage = damage × (1 - resistance_rate)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> defenseStage10Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod defenseStage10MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDefenseStageMgt10()
        {
            if (defenseStage10MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(defenseStage10MgtNewMod, "player_to_monster.defense", "4_resistance_application", "[Defense]/[Defense] 4단계: 저항 적용 (원소 피해 경감)");
            UpdateDamageFormulaView();
            defenseStage10MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)/RemoveMod")]
        [ValueDropdown("GetDefenseStageMgt10ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod defenseStage10MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 4단계: 저항 적용 (원소 피해 경감)/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDefenseStageMgt10()
        {
            if (defenseStage10MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(defenseStage10MgtRemoveMod, "player_to_monster.defense", "4_resistance_application");
            UpdateDamageFormulaView();
            defenseStage10MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDefenseStageMgt10ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("player_to_monster.defense", "4_resistance_application");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More", expanded: false)]
        [InfoBox("최종 받는 피해 증감\n공식: final_damage = damage × (1 + ΣInc) × ∏(1 + More)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> defenseStage11Mods = new List<ModDisplayInfo>();

        // ====================================
        // Movement MOD 목록
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Movement]", expanded: false)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Movement]/[Movement] 1단계: 이동속도", expanded: false)]
        [InfoBox("이동속도 증가 및 조건부 이동속도 증가\n공식: movement_speed = base_speed × (1 + Σ(movespeed_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> movementStage1Mods = new List<ModDisplayInfo>();

        // ====================================
        // Resource MOD 목록
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Resource]", expanded: false)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Resource]/[Resource] 1단계: 생명력", expanded: false)]
        [InfoBox("최대 생명력 증가 및 회복\n공식: max_life = base_life × (1 + Σ(life_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> resourceStage1Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Resource]/[Resource] 2단계: 마나", expanded: false)]
        [InfoBox("최대 마나 증가 및 회복\n공식: max_mana = base_mana × (1 + Σ(mana_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> resourceStage2Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Resource]/[Resource] 3단계: 마나 소모", expanded: false)]
        [InfoBox("스킬 마나 소모 면제 (속성별)\n공식: mana_cost = skill_cost × (has_nocost_mod ? 0 : 1)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> resourceStage3Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Resource]/[Resource] 4단계: 생명력 흡수", expanded: false)]
        [InfoBox("생명력 흡수 관련 MOD (POE 스타일 인스턴스 시스템)\n- 흡수 불가 조건 1개 (cannotbe_lifeleeched)\n- 흡수율 6개 (all_damage + 속성별 5개: physical, cold, fire, lightning, poison)\n- 인스턴스 제한 2개 (max_instance_count: 최대 4개, max_per_instance: 인스턴스당 최대 5%)\n공식: leech = damage × leech_rate, 인스턴스당 초당 회복 = max_life × max_per_instance")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> resourceStage4Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Resource]/[Resource] 5단계: 마나 흡수", expanded: false)]
        [InfoBox("마나 흡수 관련 MOD (POE 스타일 인스턴스 시스템)\n- 흡수 불가 조건 1개 (cannotbe_manaleeched)\n- 흡수율 6개 (all_damage + 속성별 5개: physical, cold, fire, lightning, poison)\n- 인스턴스 제한 2개 (max_instance_count: 최대 4개, max_per_instance: 인스턴스당 최대 5%)\n공식: leech = damage × leech_rate, 인스턴스당 초당 회복 = max_mana × max_per_instance")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> resourceStage5Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Resource]/[Resource] 6단계: 보상 (서버 전용)", expanded: false)]
        [InfoBox("경험치, 골드, 아이템 드랍 확률 증가 (서버에서 처리)\n공식: reward = base_reward × (1 + Σ(reward_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> resourceStage6Mods = new List<ModDisplayInfo>();

        // ====================================
        // Buff MOD 목록
        // ====================================

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Buff]", expanded: false)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Buff]/[Buff] 1단계: Killing Spree (연속 처치)", expanded: false)]
        [InfoBox("Killing Spree 버프 관련 MOD (획득 제한, 획득 확률, 지속시간, 최대/최소 스택)\n공식: chance = base_chance + Σ(chance_mods), max_stack = base_max + Σ(max_mods), duration = base_duration × (1 + Σ(duration_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> buffStage1Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Buff]/[Buff] 2단계: Backstab (배후 공격)", expanded: false)]
        [InfoBox("Backstab 버프 관련 MOD (획득 제한, 획득 확률, 지속시간, 최대/최소 스택)\n공식: chance = base_chance + Σ(chance_mods), max_stack = base_max + Σ(max_mods), duration = base_duration × (1 + Σ(duration_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> buffStage2Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Buff]/[Buff] 3단계: Ignore Pain (고통 무시)", expanded: false)]
        [InfoBox("Ignore Pain 버프 관련 MOD (획득 제한, 획득 확률, 지속시간, 최대/최소 스택)\n공식: chance = base_chance + Σ(chance_mods), max_stack = base_max + Σ(max_mods), duration = base_duration × (1 + Σ(duration_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> buffStage3Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Buff]/[Buff] 4단계: Frenzy (광란)", expanded: false)]
        [InfoBox("Frenzy 버프 관련 MOD (획득 확률, 최대/최소 스택)\n공식: chance = base_chance + Σ(chance_mods), max_stack = base_max + Σ(max_mods)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> buffStage4Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Buff]/[Buff] 5단계: Invisibility (투명화)", expanded: false)]
        [InfoBox("Invisibility 버프 관련 MOD (획득 확률, 지속시간)\n공식: chance = base_chance + Σ(chance_mods), duration = base_duration × (1 + Σ(duration_inc) × 0.01)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> buffStage5Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [GUIColor(1, 1, 1)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Buff]/[Buff] 6단계: Surge of Power (힘의 파동)", expanded: false)]
        [InfoBox("Surge of Power 버프 관련 MOD (획득 확률, 지속시간, 효과)\n공식: chance = base_chance + Σ(chance_mods), duration = base_duration × (1 + Σ(duration_inc) × 0.01), effect = base_effect + Σ(effect_mods)")]
        [ListDrawerSettings(IsReadOnly = true, ShowIndexLabels = false, DraggableItems = false)]
        [LabelText("할당된 MOD")]
        public List<ModDisplayInfo> buffStage6Mods = new List<ModDisplayInfo>();

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod defenseStage11MgtNewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToDefenseStageMgt11()
        {
            if (defenseStage11MgtNewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(defenseStage11MgtNewMod, "player_to_monster.defense", "5_damage_taken_modifiers", "[Defense]/[Defense] 5단계: 받는 피해 Inc·More");
            UpdateDamageFormulaView();
            defenseStage11MgtNewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More/RemoveMod")]
        [ValueDropdown("GetDefenseStageMgt11ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod defenseStage11MgtRemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Defense - 적 방어]/[Defense] 5단계: 받는 피해 Inc·More/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromDefenseStageMgt11()
        {
            if (defenseStage11MgtRemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(defenseStage11MgtRemoveMod, "player_to_monster.defense", "5_damage_taken_modifiers");
            UpdateDamageFormulaView();
            defenseStage11MgtRemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetDefenseStageMgt11ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("player_to_monster.defense", "5_damage_taken_modifiers");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }
        private void UpdateDamageFormulaView()
        {
            // Spell Damage Stage (1~10)
            stage1Mods = GetModDisplayInfos("player_to_monster.spell_damage", "1_base_flat_collection");
            stage3Mods = GetModDisplayInfos("player_to_monster.spell_damage", "3_increased_application");
            stage4Mods = GetModDisplayInfos("player_to_monster.spell_damage", "4_more_application");
            stage5Mods = GetModDisplayInfos("player_to_monster.spell_damage", "5_critical_strike");
            stage6Mods = GetModDisplayInfos("player_to_monster.spell_damage", "6_cast_speed");
            stage7Mods = GetModDisplayInfos("player_to_monster.spell_damage", "7_multiple_damage");
            stage8Mods = GetModDisplayInfos("player_to_monster.spell_damage", "8_projectile_mechanics");
            stage9Mods = GetModDisplayInfos("player_to_monster.spell_damage", "9_skill_mechanics");
            stage10Mods = GetModDisplayInfos("player_to_monster.spell_damage", "10_on_kill_effects");

            // Curse Stage (1~5)
            curseStage1Mods = GetModDisplayInfos("player_to_monster.curse", "1_curse_skill_casting");
            curseStage2Mods = GetModDisplayInfos("player_to_monster.curse", "2_curse_effect_application");
            curseStage3Mods = GetModDisplayInfos("player_to_monster.curse", "3_ailment_synergy");
            curseStage4Mods = GetModDisplayInfos("player_to_monster.curse", "4_resistance_weakness");
            curseStage5Mods = GetModDisplayInfos("player_to_monster.curse", "5_combat_debuffs");

            // Ailment Damage Stage (0~4)
            ailmentStage0Mods = GetModDisplayInfos("player_to_monster.ailment_damage", "0_ailment_restrictions");
            // ailmentStage1Mods - Skill Hit 1단계 결과를 사용하므로 MOD 목록 없음
            ailmentStage2Mods = GetModDisplayInfos("player_to_monster.ailment_damage", "2_ailment_proc_chance");
            ailmentStage3Mods = GetModDisplayInfos("player_to_monster.ailment_damage", "3_ailment_duration");
            ailmentStage4Mods = GetModDisplayInfos("player_to_monster.ailment_damage", "4_ailment_inc_more");

            // Aura Damage Stage (1~4)
            dotBuffStage1Mods = GetModDisplayInfos("player_to_monster.aura_damage", "1_base_aura_damage");
            dotBuffStage2Mods = GetModDisplayInfos("player_to_monster.aura_damage", "2_aura_duration_tick");
            dotBuffStage3Mods = GetModDisplayInfos("player_to_monster.aura_damage", "3_aura_inc");
            dotBuffStage4Mods = GetModDisplayInfos("player_to_monster.aura_damage", "4_aura_more");

            // Defense Stage (1~5)
            defenseStage7Mods = GetModDisplayInfos("player_to_monster.defense", "1_hit_determination");
            defenseStage8Mods = GetModDisplayInfos("player_to_monster.defense", "2_immunity_check");
            defenseStage9Mods = GetModDisplayInfos("player_to_monster.defense", "3_armor_application");
            defenseStage10Mods = GetModDisplayInfos("player_to_monster.defense", "4_resistance_application");
            defenseStage11Mods = GetModDisplayInfos("player_to_monster.defense", "5_damage_taken_modifiers");

            // Movement Stage (1)
            movementStage1Mods = GetModDisplayInfos("player_to_monster.movement", "1_movement_speed");

            // Resource Stage (1~6)
            resourceStage1Mods = GetModDisplayInfos("player_to_monster.resource", "1_life");
            resourceStage2Mods = GetModDisplayInfos("player_to_monster.resource", "2_mana");
            resourceStage3Mods = GetModDisplayInfos("player_to_monster.resource", "3_mana_cost");
            resourceStage4Mods = GetModDisplayInfos("player_to_monster.resource", "4_life_leech");
            resourceStage5Mods = GetModDisplayInfos("player_to_monster.resource", "5_mana_leech");
            resourceStage6Mods = GetModDisplayInfos("player_to_monster.resource", "6_rewards");

            // Buff Stage (1~6)
            buffStage1Mods = GetModDisplayInfos("player_to_monster.buff", "1_killingspree");
            buffStage2Mods = GetModDisplayInfos("player_to_monster.buff", "2_backstab");
            buffStage3Mods = GetModDisplayInfos("player_to_monster.buff", "3_ignorepain");
            buffStage4Mods = GetModDisplayInfos("player_to_monster.buff", "4_frenzy");
            buffStage5Mods = GetModDisplayInfos("player_to_monster.buff", "5_invisibility");
            buffStage6Mods = GetModDisplayInfos("player_to_monster.buff", "6_surgeofpower");

            // 인라인 MOD 목록도 함께 새로고침
            RefreshInlineModLists();

            // Odin Inspector 강제 새로고침
            Repaint();
        }

        /// <summary>
        /// 특정 단계의 MOD 목록을 표시용 정보로 변환
        /// </summary>
        private List<ModDisplayInfo> GetModDisplayInfos(string formulaType, string stageId)
        {
            var mods = ModStageMetadata.GetModsForStage(formulaType, stageId);
            if (mods == null) return new List<ModDisplayInfo>();

            return mods.Select(m =>
            {
                // implementations 배열에서 해당 formula/stage 정보 찾기
                bool implemented = false;
                string codeLocation = "";

                if (m.implementation_status?.implementations != null)
                {
                    var impl = m.implementation_status.implementations
                        .FirstOrDefault(i => i.formula_type == formulaType && i.stage_id == stageId);

                    if (impl != null)
                    {
                        implemented = impl.implemented;
                        codeLocation = impl.code_location ?? "";
                    }
                }

                return new ModDisplayInfo
                {
                    modName = m.mod_name ?? "",
                    modId = m.mod_id,
                    implemented = implemented,
                    codeLocation = codeLocation
                };
            }).ToList();
        }

        /// <summary>
        /// 모든 EMod enum 드롭다운
        /// </summary>
        private IEnumerable<EMod> GetAllModsDropdown()
        {
            return System.Enum.GetValues(typeof(EMod)).Cast<EMod>().Where(m => m != EMod.None);
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// MOD 표시용 정보
        /// </summary>
        [System.Serializable]
        public class ModDisplayInfo
        {
            [ShowInInspector]
            [LabelText("MOD 이름")]
            [OnInspectorGUI("DrawModNameWithCopyButton")]
            public string modName;

            [ShowInInspector]
            [LabelText("ID")]
            [DisplayAsString]
            [GUIColor("GetModNameColor")]
            public int modId;

            // MOD 이름과 복사 버튼 그리기
            private void DrawModNameWithCopyButton()
            {
#if UNITY_EDITOR
                UnityEditor.EditorGUILayout.BeginHorizontal();

                // MOD 이름 표시 (선택 가능한 텍스트 필드)
                UnityEditor.EditorGUILayout.SelectableLabel(modName, UnityEngine.GUILayout.Height(18));

                // 복사 버튼
                if (UnityEngine.GUILayout.Button("📋", UnityEngine.GUILayout.Width(30)))
                {
                    UnityEditor.EditorGUIUtility.systemCopyBuffer = modName;
                }

                UnityEditor.EditorGUILayout.EndHorizontal();
#endif
            }

            [ShowInInspector]
            [LabelText("구현됨")]
            [ReadOnly]
            [GUIColor("GetImplementedColor")]
            public bool implemented;

            [ShowInInspector]
            [LabelText("코드 위치")]
            [OnInspectorGUI("DrawCodeLocationButton")]
            public string codeLocation;

            // 코드 위치 하이퍼링크 버튼 그리기
            private void DrawCodeLocationButton()
            {
#if UNITY_EDITOR
                UnityEditor.EditorGUILayout.BeginHorizontal();
                UnityEditor.EditorGUILayout.LabelField(codeLocation);

                if (!string.IsNullOrEmpty(codeLocation) && codeLocation.Contains(".cs"))
                {
                    if (UnityEngine.GUILayout.Button("열기", UnityEngine.GUILayout.Width(50)))
                    {
                        OpenCodeLocation(codeLocation);
                    }
                }
                UnityEditor.EditorGUILayout.EndHorizontal();
#endif
            }

            // 코드 위치 열기 (Visual Studio 또는 선택된 IDE)
            private void OpenCodeLocation(string location)
            {
#if UNITY_EDITOR
                try
                {
                    // 형식: "ModCalculator.cs:AddInc(), Calculate() line 96"
                    // 또는: "ModCalculator.cs:96"
                    string[] parts = location.Split(':');
                    if (parts.Length >= 1)
                    {
                        string fileName = parts[0].Trim();
                        int lineNumber = 0;

                        // 라인 번호 추출 시도
                        if (parts.Length >= 2)
                        {
                            string lineInfo = parts[1].Trim();
                            // "line 96" 형식 처리
                            var lineMatch = System.Text.RegularExpressions.Regex.Match(lineInfo, @"line\s+(\d+)");
                            if (lineMatch.Success)
                            {
                                int.TryParse(lineMatch.Groups[1].Value, out lineNumber);
                            }
                            // "96" 형식 처리
                            else if (int.TryParse(lineInfo, out int parsedLine))
                            {
                                lineNumber = parsedLine;
                            }
                        }

                        // 프로젝트에서 파일 찾기
                        string[] guids = UnityEditor.AssetDatabase.FindAssets($"{System.IO.Path.GetFileNameWithoutExtension(fileName)} t:Script");
                        if (guids.Length > 0)
                        {
                            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                            var script = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>(assetPath);

                            if (script != null)
                            {
                                if (lineNumber > 0)
                                {
                                    // 특정 라인으로 이동
                                    UnityEditor.AssetDatabase.OpenAsset(script, lineNumber);
                                }
                                else
                                {
                                    // 파일만 열기
                                    UnityEditor.AssetDatabase.OpenAsset(script);
                                }
                            }
                        }
                        else
                        {
                            UnityEngine.Debug.LogError($"[MOD 관리] 파일을 찾을 수 없습니다: {fileName}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"[MOD 관리] 코드 위치 열기 실패: {ex.Message}");
                }
#endif
            }

            // 구현된 MOD는 초록색, 미구현은 빨간색으로 표시
            private UnityEngine.Color GetModNameColor()
            {
                return implemented
                    ? new UnityEngine.Color(0.5f, 1f, 0.5f) // 연한 초록색
                    : new UnityEngine.Color(1f, 0.5f, 0.5f); // 연한 빨간색
            }

            // 구현됨 체크박스도 초록색/빨간색으로 강조
            private UnityEngine.Color GetImplementedColor()
            {
                return implemented
                    ? new UnityEngine.Color(0.3f, 1f, 0.3f) // 밝은 초록색
                    : new UnityEngine.Color(1f, 0.3f, 0.3f); // 밝은 빨간색
            }
        }

        /// <summary>
        /// MOD 원본 데이터 표시용 정보
        /// </summary>
        [System.Serializable]
        public class ModRawDataInfo
        {
            [HorizontalGroup("Row")]
            [LabelText("MOD")]
            [LabelWidth(300)]
            [ReadOnly]
            public string modName;

            [HorizontalGroup("Row")]
            [LabelText("ID")]
            [LabelWidth(50)]
            [ReadOnly]
            public int modId;

            [HorizontalGroup("Row")]
            [LabelText("할당 단계")]
            [LabelWidth(60)]
            [ReadOnly]
            public int assignedStageCount;

            [ShowInInspector]
            [HideLabel]
            [ReadOnly]
            public string stagesSummary;
        }


        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용/RemoveMod")]
        [ValueDropdown("GetStage3ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod stage3RemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 3단계: Increased 적용/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromStage3()
        {
            if (stage3RemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(stage3RemoveMod, "spell_damage", "3_increased_application");
            UpdateDamageFormulaView();
            stage3RemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetStage3ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("spell_damage", "3_increased_application");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용/RemoveMod")]
        [ValueDropdown("GetStage4ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod stage4RemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 4단계: More 적용/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromStage4()
        {
            if (stage4RemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(stage4RemoveMod, "spell_damage", "4_more_application");
            UpdateDamageFormulaView();
            stage4RemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetStage4ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("spell_damage", "4_more_application");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod stage5NewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToStage5()
        {
            if (stage5NewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(stage5NewMod, "spell_damage", "5_critical_strike", "[Spell] 5: 치명타");
            UpdateDamageFormulaView();
            stage5NewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타/RemoveMod")]
        [ValueDropdown("GetStage5ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod stage5RemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 5단계: 치명타/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromStage5()
        {
            if (stage5RemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(stage5RemoveMod, "spell_damage", "5_critical_strike");
            UpdateDamageFormulaView();
            stage5RemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetStage5ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("spell_damage", "5_critical_strike");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod stage6NewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToStage6()
        {
            if (stage6NewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(stage6NewMod, "spell_damage", "6_cast_speed", "[Spell] 6: 시전 속도");
            UpdateDamageFormulaView();
            stage6NewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS/RemoveMod")]
        [ValueDropdown("GetStage6ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod stage6RemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 6단계: 시전 속도 및 DPS/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromStage6()
        {
            if (stage6RemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(stage6RemoveMod, "spell_damage", "6_cast_speed");
            UpdateDamageFormulaView();
            stage6RemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetStage6ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("spell_damage", "6_cast_speed");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해/AddMod")]
        [ValueDropdown("GetAllModsDropdown")]
        [LabelText("추가할 MOD")]
        [HideLabel]
        public EMod stage7NewMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해/AddMod")]
        [Button(Name = "➕ MOD 추가")]
        private void AddModToStage7()
        {
            if (stage7NewMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.AssignModToStage(stage7NewMod, "player_to_monster.spell_damage", "7_multiple_damage", "[Spell] 7: 멀티플 피해");
            UpdateDamageFormulaView();
            stage7NewMod = EMod.None;
        }

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해/RemoveMod")]
        [ValueDropdown("GetStage7ModsForRemoval")]
        [LabelText("제거할 MOD")]
        [HideLabel]
        public EMod stage7RemoveMod = EMod.None;

        [TabGroup("Tabs", "📐 MOD 할당기", Order = 4)]
        [FoldoutGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해")]
        [HorizontalGroup("Tabs/📐 MOD 할당기/⚔️ Player → Monster/[Spell Damage]/[Spell] 7단계: 멀티플 피해/RemoveMod")]
        [Button(Name = "➖ MOD 제거")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void RemoveModFromStage7()
        {
            if (stage7RemoveMod == EMod.None)
            {
                EditorUtility.DisplayDialog("오류", "MOD를 선택해주세요.", "확인");
                return;
            }
            ModStageMetadata.UnassignModFromStage(stage7RemoveMod, "player_to_monster.spell_damage", "7_multiple_damage");
            UpdateDamageFormulaView();
            stage7RemoveMod = EMod.None;
        }

        private IEnumerable<EMod> GetStage7ModsForRemoval()
        {
            var mods = ModStageMetadata.GetModsForStage("player_to_monster.spell_damage", "7_multiple_damage");
            if (mods == null || mods.Count == 0) return new[] { EMod.None };
            return mods.Select(m => System.Enum.TryParse(m.mod_name, out EMod mod) ? mod : EMod.None).Where(m => m != EMod.None);
        }

        partial void RefreshInlineModLists();
        partial void UpdateFinalResourceValues();

        #endregion
    }
}
