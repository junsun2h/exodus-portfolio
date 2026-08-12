using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Unity.EditorCoroutines.Editor;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 배틀 시뮬레이터 - 빌드 최적화 기능
    /// 최적의 장비와 룬 조합을 자동으로 찾아주는 기능
    /// </summary>
    public partial class BattleSimulatorWindow
    {
        #region UI - 최적화 버튼

        [TabGroup("Tabs", "👤 플레이어", Order = 1)]
        [BoxGroup("Tabs/👤 플레이어/Optimization")]
        [PropertyOrder(-1)] // 최상단 배치
        [InfoBox("현재 빌드 설정에서 DPS를 최대화할 수 있는 최적의 장비와 룬 조합을 찾습니다.\n" +
                 "• Equipment: 모든 Mythic 등급 장비 테스트 (weapon 44종 등)\n" +
                 "• Runes: 모든 룬의 DPS 영향도 분석 후 상위 N개 선택\n" +
                 "• 나머지 설정(스킬, 레벨, 속성 등)은 현재 값 유지\n" +
                 "• 각 항목별 상세 DPS 리포트 제공", InfoMessageType.Info)]
        [BoxGroup("Tabs/👤 플레이어/Optimization")]
        [EnumToggleButtons]
        [LabelText("빌드 타입")]
        public BuildType buildType = BuildType.HitBuild;

        [BoxGroup("Tabs/👤 플레이어/Optimization")]
        [Button(ButtonSizes.Large, Name = "🔄 모든 프리셋 최적화")]
        [GUIColor(0.5f, 1f, 0.5f)]
        private void OptimizeAllPresets()
        {
            if (!ValidateOptimizationConditions())
                return;

            // 확인 다이얼로그
            bool confirmed = EditorUtility.DisplayDialog(
                "모든 프리셋 최적화",
                "10개 프리셋을 순서대로 최적화합니다.\n\n" +
                "각 프리셋에 대해:\n" +
                "1. 프리셋 로드\n" +
                "2. 최적 장비 찾기\n" +
                "3. 최적 룬 찾기\n" +
                "4. 프리셋 덮어씌워 저장\n\n" +
                "⚠️ 이 작업은 30분~1시간 이상 걸릴 수 있습니다.\n" +
                "계속하시겠습니까?",
                "시작",
                "취소"
            );

            if (confirmed)
            {
                StartOptimizeAllPresets();
            }
        }

        [BoxGroup("Tabs/👤 플레이어/Optimization")]
        [Button(ButtonSizes.Large, Name = "⚔️ 최적 장비 찾기")]
        [GUIColor(0.5f, 0.8f, 1f)]
        private void OptimizeEquipmentOnly()
        {
            if (!ValidateOptimizationConditions())
                return;

            EditorCoroutineUtility.StartCoroutine(OptimizeBuildRoutine(true, false), this);
        }

        [BoxGroup("Tabs/👤 플레이어/Optimization")]
        [Button(ButtonSizes.Large, Name = "🔮 최적 룬 찾기")]
        [GUIColor(1f, 0.7f, 0.3f)]
        private void OptimizeRunesOnly()
        {
            if (!ValidateOptimizationConditions())
                return;

            EditorCoroutineUtility.StartCoroutine(OptimizeBuildRoutine(false, true), this);
        }

        /// <summary>
        /// 모든 프리셋 최적화 시작 (외부에서 호출)
        /// </summary>
        public void StartOptimizeAllPresets()
        {
            if (!ValidateOptimizationConditions())
                return;

            EditorCoroutineUtility.StartCoroutine(OptimizeAllPresetsRoutine(), this);
        }

        /// <summary>
        /// 최적화 실행 조건 검증
        /// </summary>
        private bool ValidateOptimizationConditions()
        {
            if (!Application.isPlaying || GameDBClientManager.Instance == null)
            {
                EditorUtility.DisplayDialog("오류", "Unity가 Play 모드에서 실행 중이어야 합니다.", "확인");
                return false;
            }

            if (mainSpell == ESkill.None)
            {
                EditorUtility.DisplayDialog("오류", "먼저 주문(Spell)을 선택해주세요.", "확인");
                return false;
            }

            return true;
        }

        #endregion

        #region 최적화 로직

        /// <summary>
        /// 최적화 실행 코루틴
        /// </summary>
        /// <param name="optimizeEquipment">장비 최적화 여부</param>
        /// <param name="optimizeRunes">룬 최적화 여부</param>
        private IEnumerator OptimizeBuildRoutine(bool optimizeEquipment, bool optimizeRunes)
        {
            // 1. 현재 빌드 스냅샷 생성
            var snapshot = PlayerBuildSnapshot.FromWindow(this);

            // 2. 최적화 실행
            var optimizer = new BuildOptimizer(this);
            OptimizationResult result = null;

            bool optimizationCompleted = false;
            optimizer.OptimizeAsync(snapshot, optimizeEquipment, optimizeRunes, (r) =>
            {
                result = r;
                optimizationCompleted = true;
            });

            // 최적화 완료 대기
            while (!optimizationCompleted)
            {
                yield return null;
            }

            // 3. 결과 표시
            if (result != null && result.success)
            {
                ShowOptimizationResults(result, optimizeEquipment, optimizeRunes);
            }
            else
            {
                Debug.LogError($"❌ [최적화 실패] {result?.errorMessage ?? "알 수 없는 오류"}");
                EditorUtility.DisplayDialog("최적화 실패",
                    result?.errorMessage ?? "알 수 없는 오류가 발생했습니다.",
                    "확인");
            }
        }

        /// <summary>
        /// 최적화 결과 표시
        /// </summary>
        private void ShowOptimizationResults(OptimizationResult result, bool optimizedEquipment, bool optimizedRunes)
        {
            // 상세 리포트 파일로 저장
            string reportFilePath = SaveDetailedReport(result, optimizedEquipment, optimizedRunes);

            // 요약 다이얼로그
            string message = $"✅ 최적화 완료!\n\n";
            message += $"📊 DPS 비교:\n";
            message += $"  • 이전 DPS: {result.initialDPS:F0}\n";
            message += $"  • 이후 DPS: {result.bestDPS:F0}\n";
            string improvementSign = result.dpsImprovement >= 0 ? "+" : "";
            message += $"  • 증가량: {result.bestDPS - result.initialDPS:F0} ({improvementSign}{result.dpsImprovement:F1}%)\n\n";

            if (optimizedEquipment && result.bestEquipments.Count > 0)
            {
                message += "🔧 최적 장비 변경:\n";
                foreach (var kvp in result.bestEquipments)
                {
                    message += $"  • {kvp.Key}: {kvp.Value}\n";
                }
                message += "\n";
            }

            if (optimizedRunes && result.bestRunes.Count > 0)
            {
                message += $"🔮 최적 룬 ({result.bestRunes.Count}개):\n";
                for (int i = 0; i < result.bestRunes.Count; i++)
                {
                    message += $"  {i + 1}. {result.bestRunes[i]}\n";
                }
                message += "\n";
            }

            message += $"⏱️ 소요 시간: {result.elapsedTime:F1}초\n";
            message += $"🔬 테스트한 조합: {result.testedCombinations}개\n\n";
            message += $"📋 상세 리포트 저장: {reportFilePath}";
        }

        /// <summary>
        /// 상세 리포트를 파일로 저장
        /// </summary>
        private string SaveDetailedReport(OptimizationResult result, bool optimizedEquipment, bool optimizedRunes)
        {
            // 리포트 디렉토리 생성
            string reportDir = "Assets/Editor/BattleSimulator/Reports/BuildOptimization";
            if (!Directory.Exists(reportDir))
            {
                Directory.CreateDirectory(reportDir);
            }

            // 파일명 생성 (타임스탬프 포함)
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string type = optimizedEquipment && optimizedRunes ? "All" :
                         optimizedEquipment ? "Equipment" : "Runes";
            string fileName = $"OptimizationReport_{type}_{timestamp}.txt";
            string filePath = Path.Combine(reportDir, fileName);

            // 리포트 내용 생성
            using (StreamWriter writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("====================================");
                writer.WriteLine("📊 최적화 상세 리포트");
                writer.WriteLine("====================================");
                writer.WriteLine($"생성 일시: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                // 프리셋 이름 추가
                if (!string.IsNullOrEmpty(currentPresetFileName))
                {
                    writer.WriteLine($"프리셋: {currentPresetFileName}");
                }

                // 빌드 타입 추가
                string buildTypeStr = buildType == BuildType.HitBuild ? "Hit 빌드 (명중 피해 우선)" :
                                     buildType == BuildType.AilmentBuild ? "Ailment 빌드 (상태이상 피해 우선)" :
                                     "기본 빌드 (총 DPS 우선)";
                writer.WriteLine($"빌드 타입: {buildTypeStr}");
                writer.WriteLine();

                writer.WriteLine($"이전 DPS: {result.initialDPS:F0}");
                writer.WriteLine($"최종 DPS: {result.bestDPS:F0}");
                string fileImprovementSign = result.dpsImprovement >= 0 ? "+" : "";
                writer.WriteLine($"증가량: {result.bestDPS - result.initialDPS:F0} ({fileImprovementSign}{result.dpsImprovement:F1}%)");
                writer.WriteLine($"소요 시간: {result.elapsedTime:F1}초");
                writer.WriteLine($"테스트 조합: {result.testedCombinations}개");
                writer.WriteLine("====================================\n");

                // 최적화 방식 설명
                if (optimizedEquipment)
                {
                    writer.WriteLine("📌 최적화 방식 안내");
                    writer.WriteLine("------------------------------------");
                    writer.WriteLine("■ 장비 최적화 순서:");
                    writer.WriteLine("  1. weapon → 2. helmet → 3. bodyArmor → 4. gloves");
                    writer.WriteLine("  5. boots → 6. amulet → 7. belt → 8. ring1");
                    writer.WriteLine();

                    writer.WriteLine("■ 빌드 타입별 최적화 기준 (DPS 분리 방식):");
                    switch (buildType)
                    {
                        case BuildType.HitBuild:
                            writer.WriteLine("  [Hit 빌드] Spell + Aura + Dot DPS (Ailment 제외)");
                            writer.WriteLine("  → Ailment DPS는 점수 계산에서 완전히 배제됩니다");
                            writer.WriteLine("  → 명중 피해와 공통 DPS(Aura/Dot)만으로 평가합니다");
                            break;
                        case BuildType.AilmentBuild:
                            writer.WriteLine("  [Ailment 빌드] Ailment + Aura + Dot DPS (Spell 제외)");
                            writer.WriteLine("  → Spell DPS는 점수 계산에서 완전히 배제됩니다");
                            writer.WriteLine("  → 상태이상 피해와 공통 DPS(Aura/Dot)만으로 평가합니다");
                            break;
                        default:
                            writer.WriteLine("  [기본 빌드] 총 DPS 증가율로만 평가");
                            break;
                    }
                    writer.WriteLine();

                    writer.WriteLine("■ DPS 구성 요소:");
                    writer.WriteLine("  - Spell: 스킬 명중 피해 (Hit 빌드 전용)");
                    writer.WriteLine("  - Ailment: 상태이상 피해 (Ailment 빌드 전용)");
                    writer.WriteLine("  - Aura: 오라 지속 피해 (공통)");
                    writer.WriteLine("  - Dot: 도트 버프 피해 (공통)");
                    writer.WriteLine();
                    writer.WriteLine("■ DPS 증가량 측정 방식:");
                    writer.WriteLine("  - 각 슬롯은 순차적으로 최적화되므로, 뒤쪽 슬롯일수록");
                    writer.WriteLine("    이전 슬롯들의 최적화 효과가 누적 반영됩니다");
                    writer.WriteLine("------------------------------------\n");
                }

                // 장비 상세 리포트
                if (optimizedEquipment && result.equipmentDetails.Count > 0)
                {
                    // 요약 섹션 추가
                    writer.WriteLine("📋 최적 장비 요약 (슬롯별 최고 효율):");
                    writer.WriteLine("------------------------------------");
                    foreach (var kvp in result.equipmentDetails)
                    {
                        string slotName = kvp.Key;
                        var details = kvp.Value;

                        if (details.Count > 0)
                        {
                            // result.bestEquipments에서 실제 선택된 최적 장비를 찾기
                            if (!result.bestEquipments.TryGetValue(slotName, out var bestEquipment))
                                continue;

                            // details에서 bestEquipment에 해당하는 항목 찾기
                            var bestEntry = details.FirstOrDefault(d => d.name == bestEquipment.ToString());

                            // 못 찾으면 스킵 (이론상 발생하지 않아야 함)
                            if (string.IsNullOrEmpty(bestEntry.name))
                                continue;

                            // 빌드별 DPS 증가율 사용 (선택 기준과 일치)
                            double buildChangePercent = 0;
                            if (result.buildDpsChangePercent.TryGetValue(slotName, out var slotChanges))
                            {
                                slotChanges.TryGetValue(bestEntry.name, out buildChangePercent);
                            }
                            string percentSign = buildChangePercent >= 0 ? "+" : "";

                            // 원래 장비 확인
                            bool isOriginal = result.originalEquipments.TryGetValue(slotName, out var originalEquip)
                                           && originalEquip.ToString() == bestEntry.name;
                            string marker = isOriginal ? " [현재 장착]" : "";

                            writer.WriteLine($"  {slotName}: {bestEntry.name}{marker} ({percentSign}{buildChangePercent:F1}%)");
                        }
                    }
                    writer.WriteLine("------------------------------------\n");

                    writer.WriteLine("⚔️ 장비 테스트 결과 (슬롯별 상세):");
                    writer.WriteLine("------------------------------------");

                    foreach (var kvp in result.equipmentDetails)
                    {
                        string slotName = kvp.Key;
                        var details = kvp.Value;

                        // 빌드별 DPS 증가율 기준 내림차순 정렬
                        var slotBuildChanges = result.buildDpsChangePercent.ContainsKey(slotName)
                            ? result.buildDpsChangePercent[slotName]
                            : new Dictionary<string, double>();
                        details.Sort((a, b) =>
                        {
                            double aChange = slotBuildChanges.TryGetValue(a.name, out var ac) ? ac : 0;
                            double bChange = slotBuildChanges.TryGetValue(b.name, out var bc) ? bc : 0;
                            return bChange.CompareTo(aChange);
                        });

                        writer.WriteLine($"\n[{slotName}] - {details.Count}개 테스트");
                        foreach (var (name, dps, change, changePercent, absoluteChange, absoluteChangePercent, uniqueMods, combineMods) in details)
                        {
                            // 원래 장착 장비 확인
                            bool isOriginal = result.originalEquipments.TryGetValue(slotName, out var originalEquip)
                                           && originalEquip.ToString() == name;
                            string marker = isOriginal ? " [현재 장착]" : "";

                            // 빌드별 DPS 증가율 사용
                            double buildChangePercent = slotBuildChanges.TryGetValue(name, out var bcp) ? bcp : 0;
                            string buildPercentSign = buildChangePercent >= 0 ? "+" : "";

                            writer.WriteLine($"  • {name}{marker}");
                            writer.WriteLine($"    DPS: {dps:F0}");
                            writer.WriteLine($"    증가량: {buildPercentSign}{buildChangePercent:F1}% (빌드 DPS 기준) ← 원래 빌드 대비");

                            // UniqueModValueList 출력
                            if (uniqueMods != null && uniqueMods.Count > 0)
                            {
                                writer.WriteLine($"    [UniqueModValueList]");
                                foreach (var mod in uniqueMods)
                                {
                                    var (value, contribution, contributionPercent) = mod.Value;
                                    string modPercentSign = contributionPercent >= 0 ? "+" : "";
                                    writer.WriteLine($"      - {mod.Key}: {value:F2} | 기여: {modPercentSign}{contribution:F0} ({modPercentSign}{contributionPercent:F1}%)");
                                }
                            }

                            // CombineModValueList 출력
                            if (combineMods != null && combineMods.Count > 0)
                            {
                                writer.WriteLine($"    [CombineModValueList]");
                                foreach (var mod in combineMods)
                                {
                                    var (value, contribution, contributionPercent) = mod.Value;
                                    string modPercentSign = contributionPercent >= 0 ? "+" : "";
                                    writer.WriteLine($"      - {mod.Key}: {value:F2} | 기여: {modPercentSign}{contribution:F0} ({modPercentSign}{contributionPercent:F1}%)");
                                }
                            }
                        }
                    }
                    writer.WriteLine("\n====================================\n");
                }

                // 룬 상세 리포트
                if (optimizedRunes && result.runeDetails.Count > 0)
                {
                    writer.WriteLine("🔮 룬 테스트 결과:");
                    writer.WriteLine("------------------------------------");

                    // DPS 변화량 기준 내림차순 정렬
                    var sortedRunes = result.runeDetails.OrderByDescending(r => r.change).ToList();

                    writer.WriteLine($"\n총 {sortedRunes.Count}개 룬 테스트");
                    foreach (var (name, dps, change, changePercent, runeMods) in sortedRunes)
                    {
                        // 증가량과 백분율 표시 형식 통일 (음수면 - 기호만, 양수면 + 기호)
                        string changeSign = change >= 0 ? "+" : "";
                        string percentSign = changePercent >= 0 ? "+" : "";
                        writer.WriteLine($"  • {name}");
                        writer.WriteLine($"    DPS: {dps:F0} | 증가량: {changeSign}{change:F0} ({percentSign}{changePercent:F1}%)");

                        // 룬 MOD 출력
                        if (runeMods != null && runeMods.Count > 0)
                        {
                            writer.WriteLine($"    [RuneModValues]");
                            foreach (var mod in runeMods)
                            {
                                var (value, contribution, contributionPercent) = mod.Value;
                                string modPercentSign = contributionPercent >= 0 ? "+" : "";
                                writer.WriteLine($"      - {mod.Key}: {value:F2} | 기여: {modPercentSign}{contribution:F0} ({modPercentSign}{contributionPercent:F1}%)");
                            }
                        }
                    }
                    writer.WriteLine("\n====================================\n");
                }

                writer.WriteLine($"✅ 리포트 저장 완료");
            }

            return filePath;
        }

        /// <summary>
        /// 최적화 결과를 플레이어 탭에 적용
        /// </summary>
        private void ApplyOptimizationResults(OptimizationResult result, bool applyEquipment, bool applyRunes)
        {
            // 장비 적용
            if (applyEquipment)
            {
                foreach (var kvp in result.bestEquipments)
                {
                    switch (kvp.Key)
                    {
                        case "weapon":
                            weaponMythic = kvp.Value;
                            break;
                        case "helmet":
                            helmetMythic = kvp.Value;
                            break;
                        case "bodyArmor":
                            bodyArmorMythic = kvp.Value;
                            break;
                        case "gloves":
                            glovesMythic = kvp.Value;
                            break;
                        case "boots":
                            bootsMythic = kvp.Value;
                            break;
                        case "amulet":
                            amuletMythic = kvp.Value;
                            break;
                        case "belt":
                            beltMythic = kvp.Value;
                            break;
                        case "ring1":
                            ring1Mythic = kvp.Value;
                            break;
                    }
                }
            }

            // 룬 적용
            if (applyRunes && result.bestRunes.Count > 0)
            {
                // 먼저 기존 소켓 초기화
                RefreshRuneSocketSlots();

                // 룬 적용
                for (int i = 0; i < result.bestRunes.Count && i < runeSocketSlots.Count; i++)
                {
                    runeSocketSlots[i].rune = result.bestRunes[i];
                    runeSocketSlots[i].isEquipped = true;
                }
            }
        }

        #endregion

        #region 모든 프리셋 일괄 최적화

        /// <summary>
        /// 모든 프리셋(10개)을 순서대로 최적화하고 저장하는 코루틴
        /// </summary>
        private IEnumerator OptimizeAllPresetsRoutine()
        {
            var startTime = DateTime.Now;
            int successCount = 0;
            int skipCount = 0;
            int failCount = 0;

            // buildPresetsContainer 초기화
            InitializeBuildPresets();

            if (buildPresetsContainer == null || buildPresetsContainer.slots == null)
            {
                Debug.LogError("[AllPresetsOptimize] ❌ 프리셋 컨테이너를 찾을 수 없습니다.");
                EditorUtility.ClearProgressBar();
                yield break;
            }

            // 기본 10개 프리셋만 처리
            var presetTypes = new[]
            {
                EBuildPresetType.PhysicalHit,
                EBuildPresetType.PhysicalAilment,
                EBuildPresetType.FireHit,
                EBuildPresetType.FireAilment,
                EBuildPresetType.ColdHit,
                EBuildPresetType.ColdAilment,
                EBuildPresetType.LightningHit,
                EBuildPresetType.LightningAilment,
                EBuildPresetType.PoisonHit,
                EBuildPresetType.PoisonAilment
            };

            for (int i = 0; i < presetTypes.Length; i++)
            {
                var presetType = presetTypes[i];
                string presetName = BuildPresetSlot.GetBuildTypeName(presetType);

                EditorUtility.DisplayProgressBar(
                    "모든 프리셋 최적화",
                    $"[{i + 1}/{presetTypes.Length}] {presetName} 처리 중...",
                    (float)i / presetTypes.Length
                );

                // 1. 프리셋 파일 경로 확인
                int slotIndex = (int)presetType;
                if (slotIndex >= buildPresetsContainer.slots.Count)
                {
                    skipCount++;
                    continue;
                }

                var slot = buildPresetsContainer.slots[slotIndex];
                if (!slot.HasPreset())
                {
                    skipCount++;
                    continue;
                }

                string presetFilePath = Path.Combine(PRESETS_FOLDER, slot.presetFilePath);
                if (!File.Exists(presetFilePath))
                {
                    skipCount++;
                    continue;
                }

                // 2. 프리셋 로드
                LoadPresetFromPath(presetFilePath, silent: true);
                yield return new WaitForSeconds(0.5f);

                // 3. 장비 최적화 실행
                yield return OptimizeBuildRoutineInternal(true, false);

                // 4. 룬 최적화 실행
                yield return OptimizeBuildRoutineInternal(false, true);

                // 5. 프리셋 덮어씌워 저장
                bool saved = SavePresetToPath(presetFilePath, silent: false);
                if (saved)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                }
            }

            EditorUtility.ClearProgressBar();

            var elapsed = DateTime.Now - startTime;

            EditorUtility.DisplayDialog(
                "모든 프리셋 최적화 완료",
                $"성공: {successCount}개\n스킵: {skipCount}개\n실패: {failCount}개\n\n총 소요 시간: {elapsed.TotalMinutes:F1}분",
                "확인"
            );
        }

        /// <summary>
        /// 최적화 실행 (내부용 - 리포트 생성 없이, 콜백 기반)
        /// </summary>
        private IEnumerator OptimizeBuildRoutineInternal(bool optimizeEquipment, bool optimizeRunes)
        {
            // 1. 현재 빌드 스냅샷 생성
            var snapshot = PlayerBuildSnapshot.FromWindow(this);

            // 2. 최적화 실행 (콜백 기반)
            var optimizer = new BuildOptimizer(this);
            OptimizationResult finalResult = null;
            bool isComplete = false;

            optimizer.OptimizeAsync(snapshot, optimizeEquipment, optimizeRunes, (result) =>
            {
                finalResult = result;
                isComplete = true;

                // UI 갱신
                if (result.success)
                {
                    // 최적 장비 적용
                    if (optimizeEquipment && result.bestEquipments.Count > 0)
                    {
                        if (result.bestEquipments.TryGetValue("weapon", out var bestWeapon))
                            weaponMythic = bestWeapon;
                        if (result.bestEquipments.TryGetValue("helmet", out var bestHelmet))
                            helmetMythic = bestHelmet;
                        if (result.bestEquipments.TryGetValue("bodyArmor", out var bestBodyArmor))
                            bodyArmorMythic = bestBodyArmor;
                        if (result.bestEquipments.TryGetValue("gloves", out var bestGloves))
                            glovesMythic = bestGloves;
                        if (result.bestEquipments.TryGetValue("boots", out var bestBoots))
                            bootsMythic = bestBoots;
                        if (result.bestEquipments.TryGetValue("amulet", out var bestAmulet))
                            amuletMythic = bestAmulet;
                        if (result.bestEquipments.TryGetValue("belt", out var bestBelt))
                            beltMythic = bestBelt;
                        if (result.bestEquipments.TryGetValue("ring1", out var bestRing1))
                            ring1Mythic = bestRing1;
                    }

                    // 최적 룬 적용
                    if (optimizeRunes && result.bestRunes.Count > 0)
                    {
                        // 먼저 기존 소켓 초기화
                        RefreshRuneSocketSlots();

                        for (int i = 0; i < result.bestRunes.Count && i < runeSocketSlots.Count; i++)
                        {
                            runeSocketSlots[i].rune = result.bestRunes[i];
                            runeSocketSlots[i].isEquipped = true;
                        }
                    }
                }
            });

            // 3. 완료 대기
            while (!isComplete)
            {
                yield return null;
            }
        }

        #endregion
    }

    #region 데이터 클래스

    /// <summary>
    /// 플레이어 빌드 스냅샷
    /// 현재 플레이어 탭의 설정을 저장
    /// </summary>
    public class PlayerBuildSnapshot
    {
        // 고정 설정 (최적화 대상 아님)
        public ESkill mainSpell;
        public ETier spellTier;
        public EGrade spellGrade;
        public ESkill aura;
        public ETier auraTier;
        public EPet pet;
        public ETier petTier;
        public EConstellation constellation;

        // 현재 장착 장비 (Normal)
        public EEquipmentNormal weapon;
        public EEquipmentNormal helmet;
        public EEquipmentNormal bodyArmor;
        public EEquipmentNormal gloves;
        public EEquipmentNormal boots;
        public EEquipmentNormal amulet;
        public EEquipmentNormal belt;
        public EEquipmentNormal ring1;

        // 현재 장착 장비 (Mythic) - 최적화 대상
        public EEquipmentMythic weaponMythic;
        public EEquipmentMythic helmetMythic;
        public EEquipmentMythic bodyArmorMythic;
        public EEquipmentMythic glovesMythic;
        public EEquipmentMythic bootsMythic;
        public EEquipmentMythic amuletMythic;
        public EEquipmentMythic beltMythic;
        public EEquipmentMythic ring1Mythic;

        // 현재 장착 룬 - 최적화 대상
        public List<ESkill> currentRunes = new List<ESkill>();

        /// <summary>
        /// BattleSimulatorWindow에서 스냅샷 생성
        /// </summary>
        public static PlayerBuildSnapshot FromWindow(BattleSimulatorWindow window)
        {
            var snapshot = new PlayerBuildSnapshot
            {
                // 고정 설정
                mainSpell = window.mainSpell,
                spellTier = window.spellTier,
                spellGrade = window.spellGrade,
                aura = window.aura,
                auraTier = window.auraTier,
                pet = window.pet,
                petTier = window.petTier,
                constellation = window.constellationSlot1,

                // Normal 장비 (고정)
                weapon = window.weapon,
                helmet = window.helmet,
                bodyArmor = window.bodyArmor,
                gloves = window.gloves,
                boots = window.boots,
                amulet = window.amulet,
                belt = window.belt,
                ring1 = window.ring1,

                // Mythic 장비 (최적화 대상)
                weaponMythic = window.weaponMythic,
                helmetMythic = window.helmetMythic,
                bodyArmorMythic = window.bodyArmorMythic,
                glovesMythic = window.glovesMythic,
                bootsMythic = window.bootsMythic,
                amuletMythic = window.amuletMythic,
                beltMythic = window.beltMythic,
                ring1Mythic = window.ring1Mythic,
            };

            // 현재 룬 추출
            if (window.runeSocketSlots != null)
            {
                snapshot.currentRunes = window.runeSocketSlots
                    .Where(slot => slot.rune != ESkill.None)
                    .Select(slot => slot.rune)
                    .ToList();
            }

            return snapshot;
        }
    }

    /// <summary>
    /// 최적화 결과
    /// </summary>
    public class OptimizationResult
    {
        public bool success;
        public string errorMessage;

        public double bestDPS;
        public double initialDPS;
        public double dpsImprovement; // 향상률 (%)

        public Dictionary<string, EEquipmentMythic> bestEquipments = new Dictionary<string, EEquipmentMythic>();
        public List<ESkill> bestRunes = new List<ESkill>();

        // 원래 장착했던 장비 (절대 비교용)
        public Dictionary<string, EEquipmentMythic> originalEquipments = new Dictionary<string, EEquipmentMythic>();

        // 빌드별 DPS 증가율 저장 (요약 표시용)
        // key: slotName, value: Dictionary<equipmentName, buildDpsChangePercent>
        public Dictionary<string, Dictionary<string, double>> buildDpsChangePercent = new Dictionary<string, Dictionary<string, double>>();

        // 초기 빌드별 DPS (전체 최적화 시작 시점)
        public double initialHitBuildDps;
        public double initialAilmentBuildDps;

        public int testedCombinations; // 테스트한 조합 수
        public float elapsedTime; // 소요 시간 (초)

        // 상세 리포트용 데이터
        // MOD별 기여도: (값, 기여량, 기여율)
        // change/changePercent: 현재 baseline 대비 (상대 비교)
        // absoluteChange/absoluteChangePercent: 원래 장착 장비 대비 (절대 비교)
        public Dictionary<string, List<(string name, double dps, double change, double changePercent,
            double absoluteChange, double absoluteChangePercent,
            Dictionary<EMod, (double value, double contribution, double contributionPercent)> uniqueMods,
            Dictionary<ECombineMod, (double value, double contribution, double contributionPercent)> combineMods)>> equipmentDetails
            = new Dictionary<string, List<(string, double, double, double, double, double,
                Dictionary<EMod, (double, double, double)>,
                Dictionary<ECombineMod, (double, double, double)>)>>();
        public List<(string name, double dps, double change, double changePercent,
            Dictionary<EMod, (double value, double contribution, double contributionPercent)> runeMods)> runeDetails
            = new List<(string, double, double, double, Dictionary<EMod, (double, double, double)>)>();
    }

    #endregion

    #region 최적화 엔진

    /// <summary>
    /// 빌드 최적화 엔진
    /// </summary>
    public class BuildOptimizer
    {
        private BattleSimulatorWindow window;
        private EquipmentDataProvider equipmentProvider;
        private RuneDataProvider runeProvider;
        private bool hasEquipmentError = false;

        // 최적화 모드 플래그 (장비 최적화 시에만 룬 제거)
        private bool isOptimizingEquipment = false;

        // 베이스라인 측정 플래그 (룬 없이 시뮬레이션)
        private bool isBaselineMeasurement = false;

        // 베이스라인 DPS (슬롯별 최적화 시작 시점의 DPS)
        private double baselineSkillDps = 0;
        private double baselineAilmentDps = 0;
        private double baselineAuraDps = 0;
        private double baselineDotDps = 0;
        private double baselineTotalDps = 0;

        // 빌드별 DPS (Hit: spell+aura+dot, Ailment: ailment+aura+dot)
        private double baselineHitBuildDps = 0;
        private double baselineAilmentBuildDps = 0;

        public BuildOptimizer(BattleSimulatorWindow window)
        {
            this.window = window;
            this.equipmentProvider = new EquipmentDataProvider();
            this.runeProvider = new RuneDataProvider(window.mainSpell);

            // 에러 로그 리스너 등록
            Application.logMessageReceived += OnLogMessageReceived;
        }

        /// <summary>
        /// 로그 에러 감지
        /// </summary>
        private void OnLogMessageReceived(string logString, string stackTrace, LogType type)
        {
            if (type == LogType.Error)
            {
                // 장비 장착 타임아웃 에러 감지
                if (logString.Contains("장비 장착 타임아웃") ||
                    logString.Contains("Mythic 장비 장착 타임아웃") ||
                    logString.Contains("장착 타임아웃"))
                {
                    hasEquipmentError = true;
                    Debug.LogError("[BuildOptimizer] ⚠️ 장비 장착 에러 감지! 최적화를 중단합니다.");
                }
            }
        }

        /// <summary>
        /// 리스너 해제
        /// </summary>
        private void Cleanup()
        {
            Application.logMessageReceived -= OnLogMessageReceived;

            // 플래그 리셋
            isOptimizingEquipment = false;
            isBaselineMeasurement = false;
        }

        /// <summary>
        /// 비동기 최적화 실행
        /// </summary>
        public void OptimizeAsync(PlayerBuildSnapshot snapshot, bool optimizeEquipment, bool optimizeRunes, System.Action<OptimizationResult> onComplete)
        {
            EditorCoroutineUtility.StartCoroutine(OptimizeRoutine(snapshot, optimizeEquipment, optimizeRunes, onComplete), window);
        }

        /// <summary>
        /// 최적화 코루틴
        /// </summary>
        private IEnumerator OptimizeRoutine(PlayerBuildSnapshot snapshot, bool optimizeEquipment, bool optimizeRunes, System.Action<OptimizationResult> onComplete)
        {
            var result = new OptimizationResult
            {
                success = true,
                initialDPS = 0,
                bestDPS = 0,
                testedCombinations = 0
            };

            float startTime = Time.realtimeSinceStartup;

            string target = optimizeEquipment && optimizeRunes ? "장비/룬" :
                           optimizeEquipment ? "장비" : "룬";

            EditorUtility.DisplayProgressBar($"{target} 최적화", "현재 빌드 DPS 계산 중...", 0.1f);

            // 0-1. UI 슬롯 기반 룬 해제 (수동 "소켓 초기화" 버튼과 동일한 방식)
            // API 캐시가 아닌 UI 슬롯에 있는 룬들을 직접 해제해야 동기화 문제 방지
            if (window.runeSocketSlots != null)
            {
                int unequipCount = 0;
                foreach (var slot in window.runeSocketSlots)
                {
                    if (slot.isEquipped && slot.rune != ESkill.None)
                    {
                        bool complete = false;
                        GameAPISkillManager.Instance.Request_Rune_Equip(
                            false, // 해제
                            window.mainSpell,
                            window.spellTier,
                            slot.socketIndex,
                            slot.rune,
                            window.spellTier,
                            () => { complete = true; }
                        );

                        float timeout = 0;
                        while (!complete && timeout < 5f)
                        {
                            timeout += Time.deltaTime;
                            yield return null;
                        }
                        yield return new WaitForSeconds(0.05f);
                        unequipCount++;
                    }
                }
            }

            // 0-1-1. 스냅샷의 currentRunes 클리어 (UI 초기화 후 상태 반영)
            // 이렇게 해야 최종 룬 비교 시 "현재와 동일" 판정을 피하고 항상 최종 시뮬레이션 수행
            snapshot.currentRunes.Clear();

            // 0-2. UI 슬롯 초기화 (주문 티어 기준)
            window.RefreshRuneSocketSlots();

            SimulatorAPIController.LogCurrentRuneState(window.mainSpell, window.spellTier);

            // [DEBUG] 베이스라인 1/3 - 최적화 시작 전 DPS (장비 해제 전)
            if (optimizeEquipment)
            {
                yield return SimulateAndCalculateDPS();
            }

            // 0-3. 장비 최적화 시 Mythic 장비 초기화 (베이스라인 측정 전에 수행)
            // 이전 최적화 결과가 남아있으면 베이스라인 자체가 달라지므로 초기화 필수
            if (optimizeEquipment)
            {
                // 서버에서 모든 Mythic 장비 해제 (IsEquipped 캐시에 의존하지 않고 prefix로 직접 해제)
                // UnequipAllMythicGearRoutine()은 캐시 동기화 문제로 인해 신뢰할 수 없음
                var slotPrefixes = new[] { "weapon_", "armour_helmet_", "armour_bodyarmour_",
                    "armour_glove_", "armour_boots_", "accessory_amulet_", "accessory_belt_", "accessory_ring_" };
                foreach (var prefix in slotPrefixes)
                {
                    yield return SimulatorAPIController.UnequipMythicGearByPrefixRoutine(prefix);
                }

                // UI 상태도 초기화
                window.weaponMythic = EEquipmentMythic.None;
                window.helmetMythic = EEquipmentMythic.None;
                window.bodyArmorMythic = EEquipmentMythic.None;
                window.glovesMythic = EEquipmentMythic.None;
                window.bootsMythic = EEquipmentMythic.None;
                window.amuletMythic = EEquipmentMythic.None;
                window.beltMythic = EEquipmentMythic.None;
                window.ring1Mythic = EEquipmentMythic.None;
            }

            // 1. 현재 빌드의 베이스라인 DPS 계산 (룬 없는 상태, 장비 초기화 후)
            isBaselineMeasurement = true;
            yield return SimulateAndCalculateDPS();
            isBaselineMeasurement = false;
            double baselineDPS = GetLastCalculatedDPS();
            result.initialDPS = baselineDPS;
            result.bestDPS = baselineDPS;


            // 초기 빌드별 DPS 저장
            var initialStats = window.GetLastStats();
            double initialSkillDps = CalculateSkillDPS();
            double initialAilmentDps = CalculateAilmentDPS(initialStats);
            double initialAuraDps = CalculateAuraDPS();
            double initialDotDps = CalculateDotDPS();
            result.initialHitBuildDps = initialSkillDps + initialAuraDps + initialDotDps;
            result.initialAilmentBuildDps = initialAilmentDps + initialAuraDps + initialDotDps;

            if (baselineDPS <= 0)
            {
                result.success = false;
                result.errorMessage = "베이스라인 DPS 계산 실패. Play 모드와 빌드 설정을 확인해주세요.";
                EditorUtility.ClearProgressBar();
                result.elapsedTime = Time.realtimeSinceStartup - startTime;
                Cleanup();
                onComplete?.Invoke(result);
                yield break;
            }

            // [DEBUG] 테스트용: 장비/룬 해제 후 베이스라인 측정까지만 실행하고 중단
            const bool DEBUG_STOP_AFTER_BASELINE = false;
            if (DEBUG_STOP_AFTER_BASELINE)
            {
                result.success = true;
                result.elapsedTime = Time.realtimeSinceStartup - startTime;
                EditorUtility.ClearProgressBar();
                Cleanup();
                onComplete?.Invoke(result);
                yield break;
            }

            // 2. 장비 최적화 (슬롯별 Greedy)
            if (optimizeEquipment)
            {
                EditorUtility.DisplayProgressBar($"{target} 최적화", "Mythic 장비 최적화 중...", 0.3f);
                yield return OptimizeEquipments(snapshot, result);

                // 에러 발생 시 중단
                if (!result.success)
                {
                    EditorUtility.ClearProgressBar();
                    result.elapsedTime = Time.realtimeSinceStartup - startTime;
                    Cleanup();
                    onComplete?.Invoke(result);
                    yield break;
                }

            }

            // 3. 룬 최적화 (DPS 변화량 기반)
            if (optimizeRunes)
            {
                EditorUtility.DisplayProgressBar($"{target} 최적화", "룬 조합 최적화 중...", 0.7f);
                yield return OptimizeRunes(snapshot, result);

                // 에러 발생 시 중단
                if (!result.success)
                {
                    EditorUtility.ClearProgressBar();
                    result.elapsedTime = Time.realtimeSinceStartup - startTime;
                    Cleanup();
                    onComplete?.Invoke(result);
                    yield break;
                }
            }

            EditorUtility.ClearProgressBar();

            result.elapsedTime = Time.realtimeSinceStartup - startTime;
            Cleanup();
            onComplete?.Invoke(result);
        }

        /// <summary>
        /// 장비 최적화 (슬롯별 순차)
        /// </summary>
        private IEnumerator OptimizeEquipments(PlayerBuildSnapshot snapshot, OptimizationResult result)
        {
            // 장비 최적화 모드 활성화 (API 호출 시 룬 제거)
            isOptimizingEquipment = true;

            // 참고: Mythic 장비 초기화는 OptimizeRoutine의 베이스라인 측정 전에 수행됨 (735줄)

            // Mythic 장비 슬롯 정의 (snapshot에서 현재 장착 장비 가져옴)
            // prefix는 GetMythicEquipmentListByPrefix와 동일하게 맞춰야 함
            // currentEquip: 최적화 시작 전 장착된 장비 (해제 대상)
            var slots = new[]
            {
                ("weapon_", "weapon", snapshot.weaponMythic),
                ("armour_helmet_", "helmet", snapshot.helmetMythic),
                ("armour_bodyarmour_", "bodyArmor", snapshot.bodyArmorMythic),
                ("armour_glove_", "gloves", snapshot.glovesMythic),
                ("armour_boots_", "boots", snapshot.bootsMythic),
                ("accessory_amulet_", "amulet", snapshot.amuletMythic),
                ("accessory_belt_", "belt", snapshot.beltMythic),
                ("accessory_ring_", "ring1", snapshot.ring1Mythic)
            };

            // 원래 장착했던 장비 저장 (절대 비교용) - snapshot에서 가져옴
            result.originalEquipments["weapon"] = snapshot.weaponMythic;
            result.originalEquipments["helmet"] = snapshot.helmetMythic;
            result.originalEquipments["bodyArmor"] = snapshot.bodyArmorMythic;
            result.originalEquipments["gloves"] = snapshot.glovesMythic;
            result.originalEquipments["boots"] = snapshot.bootsMythic;
            result.originalEquipments["amulet"] = snapshot.amuletMythic;
            result.originalEquipments["belt"] = snapshot.beltMythic;
            result.originalEquipments["ring1"] = snapshot.ring1Mythic;

            // [DEBUG] 테스트할 슬롯 개수 제한 (0이면 전체, 1이면 무기만, 2면 무기+투구...)
            const int DEBUG_MAX_SLOT_COUNT = 0;  // 0 = 전체 슬롯

            int slotIndex = 0;
            foreach (var (prefix, slotName, currentEquip) in slots)
            {
                slotIndex++;
                yield return OptimizeEquipmentSlot(prefix, slotName, currentEquip, result, slotIndex, slots.Length);

                // [DEBUG] 지정된 슬롯 수만큼만 테스트
                if (DEBUG_MAX_SLOT_COUNT > 0 && slotIndex >= DEBUG_MAX_SLOT_COUNT)
                {
                    break;
                }
            }

            // 장비 최적화 모드 비활성화
            isOptimizingEquipment = false;
        }

        /// <summary>
        /// 특정 슬롯의 최적 장비 찾기
        /// </summary>
        private IEnumerator OptimizeEquipmentSlot(string slotPrefix, string slotName, EEquipmentMythic currentEquip, OptimizationResult result, int slotIndex, int totalSlots)
        {
            // 참고: 장비 해제는 0-3 단계에서 이미 완료됨 (prefix 기반으로 모든 Mythic 장비 해제)
            // 여기서 다시 해제하면 중복이므로 제거

            // [DEBUG] 슬롯별 테스트 장비 개수 (0이면 전체)
            const int DEBUG_MAX_EQUIPMENT_COUNT = 0;  // 0 = 전체 장비

            var allCandidates = equipmentProvider.GetMythicEquipments(slotPrefix);

            if (allCandidates.Count == 0)
            {
                Debug.LogError($"[BuildOptimizer] ❌ {slotName} 슬롯에 Mythic 장비가 없습니다! (prefix: {slotPrefix})");
                result.bestEquipments[slotName] = currentEquip;
                yield break;
            }

            // 모든 후보 테스트 (로컬 환경은 Rate Limit 없음)
            var candidates = allCandidates.OrderBy(e => e.ToString()).ToList();

            // 디버깅용: 장비 개수 제한 적용
            if (DEBUG_MAX_EQUIPMENT_COUNT > 0 && candidates.Count > DEBUG_MAX_EQUIPMENT_COUNT)
            {
                candidates = candidates.Take(DEBUG_MAX_EQUIPMENT_COUNT).ToList();
            }

            // 초기화 - 먼저 현재 장비로 베이스라인 DPS 계산
            SetEquipmentSlot(slotName, currentEquip);
            yield return SimulateAndCalculateDPS();

            if (lastSimulationFailed)
            {
                result.success = false;
                result.errorMessage = $"베이스라인 계산 실패 (슬롯: {slotName})";
                Debug.LogError($"[BuildOptimizer] ❌ 베이스라인 계산 중단: {result.errorMessage}");
                yield break;
            }

            // 이 슬롯의 베이스라인 DPS를 현재 상태로 설정
            var baselineStats = window.GetLastStats();
            baselineSkillDps = CalculateSkillDPS();
            baselineAilmentDps = CalculateAilmentDPS(baselineStats);
            baselineAuraDps = CalculateAuraDPS();
            baselineDotDps = CalculateDotDPS();
            baselineTotalDps = baselineStats.realDPS;

            // 빌드별 DPS 계산
            baselineHitBuildDps = baselineSkillDps + baselineAuraDps + baselineDotDps;
            baselineAilmentBuildDps = baselineAilmentDps + baselineAuraDps + baselineDotDps;

            double baselineScoreForSlot = GetOptimizationScore();  // 최적화 점수 (증가율 기반)
            double baselineRealDPS = baselineStats.realDPS;        // 실제 DPS 값

            EEquipmentMythic bestEquip = currentEquip;
            double bestScore = baselineScoreForSlot;               // 최적화 점수 (장비 선택 기준)
            double bestRealDPS = baselineRealDPS;                  // 실제 DPS 값 (리포트 표시용)

            // 상세 리포트용 리스트 초기화
            result.equipmentDetails[slotName] = new List<(string, double, double, double, double, double,
                Dictionary<EMod, (double, double, double)>,
                Dictionary<ECombineMod, (double, double, double)>)>();
            result.buildDpsChangePercent[slotName] = new Dictionary<string, double>();

            int testedCount = 0;
            // 각 후보 장비 테스트 (현재 장비 포함)
            foreach (var candidate in candidates)
            {
                testedCount++;

                // 프로그레스 바 업데이트 (10개마다 또는 첫/마지막)
                bool shouldUpdateProgress = (testedCount % 10 == 0) || (testedCount == 1) || (testedCount == candidates.Count);
                if (shouldUpdateProgress)
                {
                    float slotProgress = (float)(slotIndex - 1) / totalSlots;
                    float candidateProgress = (float)testedCount / candidates.Count / totalSlots;
                    float totalProgress = slotProgress + candidateProgress;
                    string progressMessage = $"[{slotIndex}/{totalSlots}] {slotName} 슬롯: {testedCount}/{candidates.Count} 테스트 중...\n현재 장비: {candidate}";
                    EditorUtility.DisplayProgressBar("장비 최적화", progressMessage, totalProgress);
                }

                // 현재 장착된 장비인지 확인
                bool isCurrentEquip = (candidate == currentEquip);
                double candidateScore;
                double candidateRealDPS;

                // 이미 장착된 장비면 API 호출 스킵 (베이스라인 결과 재사용)
                if (isCurrentEquip)
                {
                    // 베이스라인 결과 재사용 (중복 API 호출 방지)
                    candidateScore = baselineScoreForSlot;
                    candidateRealDPS = baselineRealDPS;
                }
                else
                {
                    // 장비 임시 장착
                    SetEquipmentSlot(slotName, candidate);
                    yield return SimulateAndCalculateDPS();

                    // API 에러 체크
                    if (lastSimulationFailed)
                    {
                        result.success = false;
                        result.errorMessage = $"장비 장착 실패 (슬롯: {slotName}, 장비: {candidate})\n" +
                                            $"타임아웃 또는 API 에러가 발생했습니다.\n" +
                                            $"Firebase Emulator를 재시작하거나 Play 모드를 재시작해주세요.";
                        Debug.LogError($"[BuildOptimizer] ❌ 최적화 중단: {result.errorMessage}");
                        yield break;
                    }

                    candidateScore = GetOptimizationScore();
                    candidateRealDPS = GetLastCalculatedDPS();
                }

                result.testedCombinations++;

                // 최적화 점수 차이 (장비 선택 기준)
                double improvement = candidateScore - baselineScoreForSlot;
                double improvementPercent = baselineScoreForSlot != 0 ? (improvement / Math.Abs(baselineScoreForSlot)) * 100 : 0;

                // 실제 DPS 변화량 (MOD 기여도 계산용)
                double realImprovement = candidateRealDPS - baselineRealDPS;
                double realImprovementPercent = baselineRealDPS > 0 ? (realImprovement / baselineRealDPS) * 100 : 0;

                // 상세 리포트에 추가 (실제 DPS 표시용)
                var uniqueMods = equipmentProvider.GetEquipmentUniqueMods(candidate);
                var combineMods = equipmentProvider.GetEquipmentCombineMods(candidate);

                // 각 MOD의 기여도 계산 (실제 DPS 변화량 기반)
                var uniqueModsWithContribution = CalculateModContributions(uniqueMods, realImprovement, realImprovementPercent);
                var combineModsWithContribution = CalculateModContributions(combineMods, realImprovement, realImprovementPercent);

                // 절대 비교값 계산 (전체 최적화 시작 전 대비)
                double absoluteChange = candidateRealDPS - result.initialDPS;
                double absoluteChangePercent = result.initialDPS > 0 ? (absoluteChange / result.initialDPS) * 100 : 0;

                // 빌드별 DPS 증가율 계산 (요약 표시용)
                var candidateStats = window.GetLastStats();
                double candidateSkillDps = CalculateSkillDPS();
                double candidateAilmentDps = CalculateAilmentDPS(candidateStats);
                double candidateAuraDps = CalculateAuraDPS();
                double candidateDotDps = CalculateDotDPS();
                double candidateHitBuildDps = candidateSkillDps + candidateAuraDps + candidateDotDps;
                double candidateAilmentBuildDps = candidateAilmentDps + candidateAuraDps + candidateDotDps;

                // 선택 기준과 동일하게 현재 슬롯의 베이스라인 대비로 계산
                double buildDpsChange = window.buildType == BuildType.HitBuild
                    ? (baselineHitBuildDps > 0 ? (candidateHitBuildDps - baselineHitBuildDps) / baselineHitBuildDps * 100 : 0)
                    : (baselineAilmentBuildDps > 0 ? (candidateAilmentBuildDps - baselineAilmentBuildDps) / baselineAilmentBuildDps * 100 : 0);
                result.buildDpsChangePercent[slotName][candidate.ToString()] = buildDpsChange;

                result.equipmentDetails[slotName].Add((candidate.ToString(), candidateRealDPS, improvement, improvementPercent,
                    absoluteChange, absoluteChangePercent,
                    uniqueModsWithContribution, combineModsWithContribution));

                // 더 좋은 점수이거나, 동점이면서 아직 장비가 선택되지 않은 경우 업데이트
                if (candidateScore > bestScore ||
                    (candidateScore >= bestScore && bestEquip == EEquipmentMythic.None))
                {
                    bestScore = candidateScore;
                    bestEquip = candidate;
                    bestRealDPS = candidateRealDPS;  // 실제 DPS도 함께 저장
                }

                yield return null; // 프레임 양보
            }

            // 최적 장비가 없으면 현재 장비 유지
            if (bestEquip == EEquipmentMythic.None)
            {
                bestEquip = currentEquip;
                bestScore = baselineScoreForSlot;
                bestRealDPS = baselineRealDPS;
            }

            // 최적 장비 적용
            SetEquipmentSlot(slotName, bestEquip);
            result.bestEquipments[slotName] = bestEquip;

            // 실제 DPS 값을 result에 저장
            result.bestDPS = bestRealDPS;
            result.dpsImprovement = result.initialDPS > 0
                ? ((result.bestDPS - result.initialDPS) / result.initialDPS) * 100
                : 0;

            // 슬롯 최적화 결과 로그
            double slotImprovement = bestScore - baselineScoreForSlot;
            double slotImprovementPercent = baselineScoreForSlot != 0
                ? (slotImprovement / Math.Abs(baselineScoreForSlot)) * 100
                : 0;

        }

        /// <summary>
        /// 룬 최적화 (DPS 변화량 기반 우선순위 정렬)
        /// </summary>
        private IEnumerator OptimizeRunes(PlayerBuildSnapshot snapshot, OptimizationResult result)
        {
            // 룬 최적화 모드 (장비 최적화 모드 비활성화)
            isOptimizingEquipment = false;

            // 최대 소켓 수 가져오기
            window.RefreshRuneSocketSlots();
            int maxSocketCount = window.runeSocketSlots?.Count ?? 7;

            var availableRunes = runeProvider.GetAvailableRunes();

            if (availableRunes.Count == 0)
            {
                Debug.LogError("[BuildOptimizer] ❌ 사용 가능한 룬이 없습니다.");
                result.bestRunes = snapshot.currentRunes;
                yield break;
            }

            // 디버깅용: 테스트할 룬 범위 설정
            // - DEBUG_SKIP_RUNE_COUNT: 처음 N개 건너뛰기 (0이면 처음부터)
            // - DEBUG_MAX_RUNE_COUNT: 최대 테스트 개수 (0이면 전체)
            const int DEBUG_SKIP_RUNE_COUNT = 0;   // 0이면 처음부터 테스트
            const int DEBUG_MAX_RUNE_COUNT = 0;    // 0이면 전체 테스트

            // 모든 룬 테스트 (로컬 환경은 Rate Limit 없음)
            var testRunes = availableRunes.OrderBy(r => r.ToString()).ToList();

            // DEBUG: 특정 룬만 테스트 (빈 리스트면 전체 테스트)
            var debugTestRunes = new List<ESkill> { };
            if (debugTestRunes.Count > 0)
            {
                testRunes = debugTestRunes;
            }
            // 디버깅용: 룬 범위 제한 적용 (debugTestRunes가 비어있을 때만)
            else if (DEBUG_SKIP_RUNE_COUNT > 0 || DEBUG_MAX_RUNE_COUNT > 0)
            {
                int originalCount = testRunes.Count;
                if (DEBUG_SKIP_RUNE_COUNT > 0)
                {
                    testRunes = testRunes.Skip(DEBUG_SKIP_RUNE_COUNT).ToList();
                }
                if (DEBUG_MAX_RUNE_COUNT > 0 && testRunes.Count > DEBUG_MAX_RUNE_COUNT)
                {
                    testRunes = testRunes.Take(DEBUG_MAX_RUNE_COUNT).ToList();
                }
            }

            // ========================================
            // 0단계: 베이스라인 DPS 계산 (룬 없는 상태)
            // ========================================
            // 내부 추적 초기화 (캐시 동기화 문제 방지)
            SimulatorAPIController.ClearRuneTracking();

            // API를 통해 서버에서 모든 룬 해제 + 캐시 검증 (중요!)
            SimulatorAPIController.LogCurrentRuneState(window.mainSpell, window.spellTier);
            yield return SimulatorAPIController.UnequipAllRunesWithVerification(window.mainSpell, window.spellTier);
            SimulatorAPIController.LogCurrentRuneState(window.mainSpell, window.spellTier);

            // 소켓 초기화 (UI 클리어)
            window.RefreshRuneSocketSlots();

            // 룬 없는 상태로 시뮬레이션하여 베이스라인 DPS 측정
            isBaselineMeasurement = true;
            yield return SimulateAndCalculateDPS();
            isBaselineMeasurement = false;

            // 시뮬레이션 직후 stats 확인
            var baselineStats = window.GetLastStats();
            if (baselineStats == null)
            {
                result.success = false;
                result.errorMessage = "베이스라인 stats가 null";
                yield break;
            }

            var (baselineTotalDps, baselineAilmentDps, baselineAuraDotDps, baselineHitBuildDps, baselineAilmentBuildDps)
                = CalculateBuildDPS(baselineStats);

            // 빌드 타입에 따른 베이스라인 DPS
            double baselineBuildDps = window.buildType == BuildType.HitBuild ? baselineHitBuildDps : baselineAilmentBuildDps;

            // 룬 최적화에서는 "이전 DPS"를 베이스라인(룬 없음)으로 통일
            // 이렇게 해야 회차마다 일관된 비교 기준이 됨
            result.initialDPS = baselineBuildDps;

            // ========================================
            // 1단계: 모든 룬의 단독 테스트
            // ========================================
            // 튜플: (룬, 빌드별DPS변화량, 빌드별DPS, TotalDPS)
            var runeResults = new List<(ESkill rune, double buildDpsChange, double buildDps, double totalDps)>();

            int testedRuneCount = 0;
            foreach (var rune in testRunes)
            {
                testedRuneCount++;

                // 프로그레스 바 업데이트
                if (testedRuneCount % 5 == 0 || testedRuneCount == 1 || testedRuneCount == testRunes.Count)
                {
                    float progress = (float)testedRuneCount / testRunes.Count;
                    EditorUtility.DisplayProgressBar("룬 최적화", $"룬 테스트: {testedRuneCount}/{testRunes.Count} - {rune}", progress);
                }

                // 이전 테스트의 룬 해제 + 캐시 검증 (중요!)
                yield return SimulatorAPIController.UnequipAllRunesWithVerification(window.mainSpell, window.spellTier);

                // 해당 룬만 단독 장착하여 시뮬레이션
                SetRunes(new List<ESkill> { rune });

                yield return SimulateAndCalculateDPS();

                if (lastSimulationFailed)
                {
                    result.success = false;
                    result.errorMessage = $"룬 테스트 실패: {rune}";
                    Debug.LogError($"[BuildOptimizer] ❌ 룬 최적화 중단: {result.errorMessage}");
                    yield break;
                }

                SimulatorDamageStats runeStats = window.GetLastStats();

                result.testedCombinations++;

                // stats에서 직접 빌드별 DPS 계산
                var (runeTotalDps, runeAilmentDps, runeAuraDotDps, runeHitBuildDps, runeAilmentBuildDps)
                    = CalculateBuildDPS(runeStats);

                // 빌드 타입에 따른 현재 빌드 DPS
                double currentBuildDps = window.buildType == BuildType.HitBuild ? runeHitBuildDps : runeAilmentBuildDps;

                // 베이스라인(룬 없음) 대비 변화량
                double buildDpsChange = currentBuildDps - baselineBuildDps;
                double buildDpsChangePercent = baselineBuildDps > 0 ? (buildDpsChange / baselineBuildDps) * 100 : 0;

                // 결과 저장
                runeResults.Add((rune, buildDpsChange, currentBuildDps, runeTotalDps));

                // 리포트용 상세 정보
                var runeMods = runeProvider.GetRuneMods(rune, ETier.common);
                var runeModsWithContribution = CalculateModContributions(runeMods, buildDpsChange, buildDpsChangePercent);
                result.runeDetails.Add((rune.ToString(), currentBuildDps, buildDpsChange, buildDpsChangePercent, runeModsWithContribution));

                yield return null; // 프레임 양보
            }

            // ========================================
            // 2단계: 빌드별 DPS 변화량 기준 내림차순 정렬
            // ========================================
            runeResults.Sort((a, b) => b.buildDpsChange.CompareTo(a.buildDpsChange));

            // ========================================
            // 3단계: 상위 N개 룬 선택 (DPS 증가가 있는 것만)
            // ========================================
            var selectedRunes = new List<ESkill>();
            for (int i = 0; i < maxSocketCount && i < runeResults.Count; i++)
            {
                var (rune, buildDpsChange, buildDps, totalDps) = runeResults[i];

                // DPS 증가가 없으면 중단
                if (buildDpsChange <= 0)
                    break;

                selectedRunes.Add(rune);
            }

            // ========================================
            // 4단계: 최종 룬 조합 적용 및 결과 계산
            // ========================================
            if (selectedRunes.Count > 0)
            {
                // 현재 룬과 동일한지 확인
                bool isSameAsCurrentRunes = false;
                if (snapshot.currentRunes != null && selectedRunes.Count == snapshot.currentRunes.Count)
                {
                    var sortedSelected = selectedRunes.OrderBy(r => r).ToList();
                    var sortedCurrent = snapshot.currentRunes.OrderBy(r => r).ToList();
                    isSameAsCurrentRunes = sortedSelected.SequenceEqual(sortedCurrent);
                }

                double finalDPS;
                if (isSameAsCurrentRunes)
                {
                    finalDPS = result.initialDPS;
                }
                else
                {
                    // 이전 테스트 룬 해제 + 캐시 검증 후 최종 조합 장착
                    yield return SimulatorAPIController.UnequipAllRunesWithVerification(window.mainSpell, window.spellTier);
                    SetRunes(selectedRunes);
                    yield return SimulateAndCalculateDPS();

                    if (lastSimulationFailed)
                    {
                        result.success = false;
                        result.errorMessage = "최종 룬 조합 테스트 실패";
                        Debug.LogError($"[BuildOptimizer] ❌ 룬 최적화 중단: {result.errorMessage}");
                        yield break;
                    }

                    finalDPS = GetLastCalculatedDPS();
                }

                result.bestRunes = selectedRunes;
                result.bestDPS = finalDPS;
                result.dpsImprovement = result.initialDPS > 0
                    ? ((result.bestDPS - result.initialDPS) / result.initialDPS) * 100
                    : 0;
            }
            else
            {
                // 선택된 룬 없음 (모든 룬이 DPS 감소) → 베이스라인(룬 없음) 유지
                result.bestRunes = new List<ESkill>();
                result.bestDPS = baselineTotalDps;
                result.dpsImprovement = result.initialDPS > 0
                    ? ((result.bestDPS - result.initialDPS) / result.initialDPS) * 100
                    : 0;
            }
        }

        /// <summary>
        /// MOD별 기여도 계산 (MOD 값의 비율에 따라 전체 기여도 분배)
        /// </summary>
        private Dictionary<T, (double value, double contribution, double contributionPercent)> CalculateModContributions<T>(
            Dictionary<T, double> mods, double totalContribution, double totalContributionPercent)
        {
            var result = new Dictionary<T, (double, double, double)>();

            if (mods == null || mods.Count == 0)
                return result;

            // 모든 MOD 값의 합계 계산
            double totalModValue = mods.Values.Sum();

            if (totalModValue <= 0)
            {
                // MOD 값이 모두 0이거나 음수인 경우, 균등 분배
                double equalContribution = totalContribution / mods.Count;
                double equalPercent = totalContributionPercent / mods.Count;

                foreach (var mod in mods)
                {
                    result[mod.Key] = (mod.Value, equalContribution, equalPercent);
                }
            }
            else
            {
                // MOD 값에 비례하여 기여도 분배
                foreach (var mod in mods)
                {
                    double ratio = mod.Value / totalModValue;
                    double contribution = totalContribution * ratio;
                    double contributionPercent = totalContributionPercent * ratio;

                    result[mod.Key] = (mod.Value, contribution, contributionPercent);
                }
            }

            return result;
        }

        /// <summary>
        /// 시뮬레이션 실행 및 DPS 계산 (BuildOptimizer 전용 - Mythic 장비만)
        /// </summary>
        private IEnumerator SimulateAndCalculateDPS()
        {
            // MOD 데이터 클리어 (이전 시뮬레이션 잔여 값 방지)
            ClearAllModData();

            // Normal 장비를 임시로 None으로 설정 (Mythic 장비만 테스트)
            var tempWeapon = window.weapon;
            var tempHelmet = window.helmet;
            var tempBodyArmor = window.bodyArmor;
            var tempGloves = window.gloves;
            var tempBoots = window.boots;
            var tempAmulet = window.amulet;
            var tempBelt = window.belt;
            var tempRing1 = window.ring1;

            window.weapon = EEquipmentNormal.None;
            window.helmet = EEquipmentNormal.None;
            window.bodyArmor = EEquipmentNormal.None;
            window.gloves = EEquipmentNormal.None;
            window.boots = EEquipmentNormal.None;
            window.amulet = EEquipmentNormal.None;
            window.belt = EEquipmentNormal.None;
            window.ring1 = EEquipmentNormal.None;

            // 장비 최적화 또는 베이스라인 측정 시 룬 소켓을 임시로 None으로 설정
            // - 장비 최적화: 중복 장착 API 에러 방지
            // - 베이스라인 측정: 룬 없는 상태의 정확한 DPS 측정
            var tempRuneSlots = new List<(ESkill rune, bool isEquipped)>();
            bool shouldClearRunes = isOptimizingEquipment || isBaselineMeasurement;
            if (shouldClearRunes && window.runeSocketSlots != null)
            {
                foreach (var slot in window.runeSocketSlots)
                {
                    tempRuneSlots.Add((slot.rune, slot.isEquipped));
                    slot.rune = ESkill.None;
                    slot.isEquipped = false;
                }
            }

            // [DEBUG] 시뮬레이션 직전 전체 상태 로그 (베이스라인 측정 시에만)
            if (isBaselineMeasurement)
            {
                // 서버 캐시 Mythic 장비 상태
                var equippedList = new System.Collections.Generic.List<string>();
                var equipmentData = GameAPIUserManager.Instance?.userData?.equipmentData?.CoreData;
                if (equipmentData?.MythicEquipments != null)
                {
                    foreach (var kvp in equipmentData.MythicEquipments)
                    {
                        if (kvp.Value.IsEquipped)
                        {
                            equippedList.Add(kvp.Key.ToString());
                        }
                    }
                }

            }

            // 기존 시뮬레이션 실행
            // - 장비 최적화: Mythic 장비만, 룬 제외
            // - 룬 최적화: Mythic 장비 + 설정된 룬
            // - 베이스라인 측정: 룬 없음
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(window.RunSimulation());

            // API 에러 체크
            if (CheckForAPIError())
            {
                lastSimulationFailed = true;
                Debug.LogError("[BuildOptimizer] ❌ API 에러 감지! 시뮬레이션 실패");
            }
            else
            {
                lastSimulationFailed = false;
            }

            // Normal 장비 복원
            window.weapon = tempWeapon;
            window.helmet = tempHelmet;
            window.bodyArmor = tempBodyArmor;
            window.gloves = tempGloves;
            window.boots = tempBoots;
            window.amulet = tempAmulet;
            window.belt = tempBelt;
            window.ring1 = tempRing1;

            // 룬 소켓 복원 (장비 최적화 또는 베이스라인 측정 후)
            // 베이스라인 측정 시에는 복원하지 않음 (룬 없는 상태 유지)
            if (isOptimizingEquipment && !isBaselineMeasurement && tempRuneSlots.Count > 0 && window.runeSocketSlots != null)
            {
                for (int i = 0; i < tempRuneSlots.Count && i < window.runeSocketSlots.Count; i++)
                {
                    window.runeSocketSlots[i].rune = tempRuneSlots[i].rune;
                    window.runeSocketSlots[i].isEquipped = tempRuneSlots[i].isEquipped;
                }
            }
        }

        /// <summary>
        /// API 에러 체크 (Rate Limit 등)
        /// </summary>
        private bool CheckForAPIError()
        {
            // 장비 장착 에러 체크
            if (hasEquipmentError)
            {
                Debug.LogError("[BuildOptimizer] ⚠️ 장비 장착 에러 발생. 최적화 중단.");
                return true;
            }

            // DPS가 0이거나 비정상적으로 낮으면 에러로 간주
            double lastDPS = GetLastCalculatedDPS();
            if (lastDPS <= 0)
            {
                Debug.LogError("[BuildOptimizer] ⚠️ 비정상적인 DPS 값 (0 이하). API 에러 가능성 있음.");
                return true;
            }

            return false;
        }

        private bool lastSimulationFailed = false;

        /// <summary>
        /// 마지막 계산된 DPS 가져오기 (하위 호환용)
        /// </summary>
        private double GetLastCalculatedDPS()
        {
            return window.GetLastCalculatedDPS();
        }

        /// <summary>
        /// 빌드 타입에 따른 최적화 점수 계산 (증가율 기반)
        /// Hit 빌드: spell + aura + dot DPS (ailment 제외)
        /// Ailment 빌드: ailment + aura + dot DPS (spell 제외)
        /// </summary>
        private double GetOptimizationScore()
        {
            var stats = window.GetLastStats();
            if (stats == null) return 0;

            // 개별 DPS 계산
            double skillDps = CalculateSkillDPS();
            double ailmentDps = CalculateAilmentDPS(stats);
            double auraDps = CalculateAuraDPS();
            double dotDps = CalculateDotDPS();

            // 빌드별 DPS 계산 (각 빌드에 해당하지 않는 DPS는 제외)
            double hitBuildDps = skillDps + auraDps + dotDps;        // Ailment 제외
            double ailmentBuildDps = ailmentDps + auraDps + dotDps;  // Spell 제외

            // 베이스라인이 0이면 초기화하고 절대값 반환 (첫 테스트)
            if (baselineTotalDps <= 0)
            {
                baselineSkillDps = skillDps;
                baselineAilmentDps = ailmentDps;
                baselineAuraDps = auraDps;
                baselineDotDps = dotDps;
                baselineTotalDps = stats.realDPS;
                baselineHitBuildDps = hitBuildDps;
                baselineAilmentBuildDps = ailmentBuildDps;

                // 첫 테스트는 빌드 타입에 맞는 절대값 반환
                return window.buildType == BuildType.HitBuild ? hitBuildDps : ailmentBuildDps;
            }

            // 빌드별 DPS 증가율 계산 (베이스라인 대비)
            switch (window.buildType)
            {
                case BuildType.HitBuild:
                    // Hit 빌드: spell + aura + dot DPS 증가율만 사용
                    return baselineHitBuildDps > 0
                        ? (hitBuildDps - baselineHitBuildDps) / baselineHitBuildDps
                        : 0;

                case BuildType.AilmentBuild:
                    // Ailment 빌드: ailment + aura + dot DPS 증가율만 사용
                    return baselineAilmentBuildDps > 0
                        ? (ailmentBuildDps - baselineAilmentBuildDps) / baselineAilmentBuildDps
                        : 0;

                default:
                    // 기본: 총 DPS 증가율
                    double totalDps = stats.realDPS;
                    return baselineTotalDps > 0
                        ? (totalDps - baselineTotalDps) / baselineTotalDps
                        : 0;
            }
        }

        /// <summary>
        /// 스킬 명중 DPS 계산
        /// </summary>
        private double CalculateSkillDPS()
        {
            double skillDps = 0;
            if (!string.IsNullOrEmpty(window.dpsDisplay))
            {
                string cleanValue = window.dpsDisplay.Replace(",", "");
                double.TryParse(cleanValue, out skillDps);
            }
            return skillDps;
        }

        /// <summary>
        /// Ailment 총 DPS 계산 (모든 Ailment의 maxStackDps 합산)
        /// </summary>
        private double CalculateAilmentDPS(SimulatorDamageStats stats)
        {
            double ailmentDps = 0;
            if (stats != null && stats.ailmentDetails != null)
            {
                foreach (var detail in stats.ailmentDetails)
                {
                    ailmentDps += detail.maxStackDps;
                }
            }
            return ailmentDps;
        }

        /// <summary>
        /// Aura DPS 계산 (auraTotalDpsDisplay 파싱)
        /// </summary>
        private double CalculateAuraDPS()
        {
            double auraDps = 0;
            if (!string.IsNullOrEmpty(window.auraTotalDpsDisplay))
            {
                // 숫자 부분만 추출 (괄호 안 텍스트 제거)
                string cleanValue = window.auraTotalDpsDisplay.Split('(')[0].Trim().Replace(",", "");
                double.TryParse(cleanValue, out auraDps);
            }
            return auraDps;
        }

        /// <summary>
        /// Dot DPS 계산 (dotBuffTotalDpsDisplay 파싱)
        /// </summary>
        private double CalculateDotDPS()
        {
            double dotDps = 0;
            if (!string.IsNullOrEmpty(window.dotBuffTotalDpsDisplay))
            {
                string cleanValue = window.dotBuffTotalDpsDisplay.Replace(",", "");
                double.TryParse(cleanValue, out dotDps);
            }
            return dotDps;
        }

        /// <summary>
        /// 빌드별 DPS 계산 (stats 객체에서 직접 계산 - UI 파싱 없음)
        /// </summary>
        /// <returns>(totalDps, ailmentDps, auraDotDps, hitBuildDps, ailmentBuildDps)</returns>
        private (double totalDps, double ailmentDps, double auraDotDps, double hitBuildDps, double ailmentBuildDps) CalculateBuildDPS(SimulatorDamageStats stats)
        {
            if (stats == null)
                return (0, 0, 0, 0, 0);

            double totalDps = stats.realDPS;

            // Ailment DPS: stats.ailmentDetails에서 직접 합산
            double ailmentDps = 0;
            if (stats.ailmentDetails != null)
            {
                foreach (var detail in stats.ailmentDetails)
                {
                    ailmentDps += detail.maxStackDps;
                }
            }

            // Aura + Dot DPS: stats.dotBuffDetails에서 직접 합산
            double auraDotDps = 0;
            if (stats.dotBuffDetails != null)
            {
                foreach (var detail in stats.dotBuffDetails)
                {
                    auraDotDps += detail.dps;
                }
            }

            // 빌드별 DPS 계산
            // Hit Build = Total - Ailment (Spell + AuraDot)
            // Ailment Build = Total - Spell = Ailment + AuraDot
            double hitBuildDps = totalDps - ailmentDps;
            double ailmentBuildDps = ailmentDps + auraDotDps;

            // 음수 방지
            if (hitBuildDps < 0) hitBuildDps = 0;
            if (ailmentBuildDps < 0) ailmentBuildDps = 0;

            return (totalDps, ailmentDps, auraDotDps, hitBuildDps, ailmentBuildDps);
        }

        /// <summary>
        /// 장비 슬롯에 장비 설정
        /// </summary>
        private void SetEquipmentSlot(string slotName, EEquipmentMythic equipment)
        {
            switch (slotName)
            {
                case "weapon": window.weaponMythic = equipment; break;
                case "helmet": window.helmetMythic = equipment; break;
                case "bodyArmor": window.bodyArmorMythic = equipment; break;
                case "gloves": window.glovesMythic = equipment; break;
                case "boots": window.bootsMythic = equipment; break;
                case "amulet": window.amuletMythic = equipment; break;
                case "belt": window.beltMythic = equipment; break;
                case "ring1": window.ring1Mythic = equipment; break;
            }
        }

        /// <summary>
        /// 룬 조합 설정
        /// </summary>
        private void SetRunes(List<ESkill> runes)
        {
            // 슬롯이 없거나 개수가 맞지 않으면 갱신 (초기화 시에만)
            // 이미 슬롯이 있으면 기존 슬롯을 유지하고 룬만 업데이트
            if (window.runeSocketSlots == null || window.runeSocketSlots.Count == 0)
            {
                window.RefreshRuneSocketSlots();
            }

            // 룬 설정
            for (int i = 0; i < runes.Count && i < window.runeSocketSlots.Count; i++)
            {
                window.runeSocketSlots[i].rune = runes[i];
                window.runeSocketSlots[i].isEquipped = true;  // 룬 장착 활성화
            }

            // 나머지 슬롯 초기화
            for (int i = runes.Count; i < window.runeSocketSlots.Count; i++)
            {
                window.runeSocketSlots[i].rune = ESkill.None;
                window.runeSocketSlots[i].isEquipped = false;  // 룬 장착 비활성화
            }
        }

        /// <summary>
        /// 모든 MOD 데이터 클리어 (이전 시뮬레이션 잔여 값 방지)
        /// </summary>
        private void ClearAllModData()
        {
            if (!Application.isPlaying)
                return;

            var player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player?.CharacterStatus?.BattleStatus == null)
                return;

            var battleStatus = player.CharacterStatus.BattleStatus;
            var battleStatusType = typeof(FBattleStatus);

            // _allBattleModData 클리어
            var allModDataField = battleStatusType.GetField("_allBattleModData", BindingFlags.NonPublic | BindingFlags.Instance);
            if (allModDataField != null)
            {
                var modData = allModDataField.GetValue(battleStatus);
                modData?.GetType().GetMethod("ClearData")?.Invoke(modData, null);
            }

            // _allBattleModSkillData 클리어
            var allModSkillDataField = battleStatusType.GetField("_allBattleModSkillData", BindingFlags.NonPublic | BindingFlags.Instance);
            if (allModSkillDataField != null)
            {
                var modSkillData = allModSkillDataField.GetValue(battleStatus);
                modSkillData?.GetType().GetMethod("ClearData")?.Invoke(modSkillData, null);
            }

            // _allCombineModData 클리어
            var allCombineModDataField = battleStatusType.GetField("_allCombineModData", BindingFlags.NonPublic | BindingFlags.Instance);
            if (allCombineModDataField != null)
            {
                var combineModData = allCombineModDataField.GetValue(battleStatus);
                combineModData?.GetType().GetMethod("ClearData")?.Invoke(combineModData, null);
            }

            // _allCombineBattleModData 클리어 (잔여 CombineMod 효과 방지)
            var allCombineBattleModDataField = battleStatusType.GetField("_allCombineBattleModData", BindingFlags.NonPublic | BindingFlags.Instance);
            if (allCombineBattleModDataField != null)
            {
                var combineModBattleData = allCombineBattleModDataField.GetValue(battleStatus);
                combineModBattleData?.GetType().GetMethod("ClearData")?.Invoke(combineModBattleData, null);
            }

            // buffData 클리어 (잔여 버프 MOD 누적 방지)
            var buffDataProp = battleStatusType.GetProperty("buffData", BindingFlags.Public | BindingFlags.Instance);
            if (buffDataProp != null)
            {
                var buffData = buffDataProp.GetValue(battleStatus);
                buffData?.GetType().GetMethod("UpdateStatus")?.Invoke(buffData, null);
            }
        }
    }

    /// <summary>
    /// 장비 데이터 제공자
    /// Mythic 장비 목록과 MOD 정보 제공
    /// </summary>
    public class EquipmentDataProvider
    {
        /// <summary>
        /// 특정 슬롯의 Mythic 장비 목록 반환
        /// </summary>
        public List<EEquipmentMythic> GetMythicEquipments(string slotPrefix)
        {
            if (!Application.isPlaying || GameDBClientManager.Instance == null)
            {
                return new List<EEquipmentMythic>();
            }

            var equipmentMythicDB = GameDBClientManager.Instance.GameDB_Equipment?.EquipmentMythic;
            if (equipmentMythicDB == null || equipmentMythicDB.MapData == null)
            {
                return new List<EEquipmentMythic>();
            }

            var result = new List<EEquipmentMythic>();

            // EquipmentMythic.MapData 순회
            foreach (var equipTypeEntry in equipmentMythicDB.MapData)
            {
                if (equipTypeEntry.Value.MythicMods != null)
                {
                    foreach (var mythicKey in equipTypeEntry.Value.MythicMods.Keys)
                    {
                        string keyStr = mythicKey.ToString();
                        if (keyStr.StartsWith(slotPrefix))
                        {
                            result.Add(mythicKey);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Mythic 장비의 MOD 추출
        /// </summary>
        public Dictionary<EMod, double> GetEquipmentMods(EEquipmentMythic equipment)
        {
            var mods = new Dictionary<EMod, double>();

            if (equipment == EEquipmentMythic.None)
                return mods;

            if (!Application.isPlaying || GameDBClientManager.Instance == null)
                return mods;

            var equipmentMythicDB = GameDBClientManager.Instance.GameDB_Equipment?.EquipmentMythic;
            if (equipmentMythicDB == null || equipmentMythicDB.MapData == null)
                return mods;

            // 장비 데이터 찾기
            GameDB_Client_EquipmentMythicMod equipData = null;
            foreach (var equipTypeEntry in equipmentMythicDB.MapData)
            {
                if (equipTypeEntry.Value.MythicMods != null &&
                    equipTypeEntry.Value.MythicMods.TryGetValue(equipment, out equipData))
                {
                    break;
                }
            }

            if (equipData == null)
                return mods;

            // UniqueModValueList에서 MOD 추출
            if (equipData.UniqueModValueList != null)
            {
                foreach (var modEntry in equipData.UniqueModValueList)
                {
                    EMod modType = modEntry.Key;
                    var modValueList = modEntry.Value;

                    if (modValueList != null && modValueList.ModValues != null && modValueList.ModValues.Count > 0)
                    {
                        // 첫 번째 값 사용 (티어 1 기준)
                        double value = modValueList.ModValues[0].GetValue;
                        mods[modType] = value;
                    }
                }
            }

            return mods;
        }

        /// <summary>
        /// Mythic 장비의 UniqueModValueList 추출
        /// </summary>
        public Dictionary<EMod, double> GetEquipmentUniqueMods(EEquipmentMythic equipment)
        {
            var mods = new Dictionary<EMod, double>();

            if (equipment == EEquipmentMythic.None)
                return mods;

            var equipData = GetEquipmentData(equipment);
            if (equipData == null)
                return mods;

            // UniqueModValueList에서 MOD 추출
            if (equipData.UniqueModValueList != null)
            {
                foreach (var modEntry in equipData.UniqueModValueList)
                {
                    EMod modType = modEntry.Key;
                    var modValueList = modEntry.Value;

                    if (modValueList != null && modValueList.ModValues != null && modValueList.ModValues.Count > 0)
                    {
                        // 첫 번째 값 사용 (Grade 0 기준)
                        double value = modValueList.ModValues[0].GetValue;
                        mods[modType] = value;
                    }
                }
            }

            return mods;
        }

        /// <summary>
        /// Mythic 장비의 CombineModValueList 추출
        /// </summary>
        public Dictionary<ECombineMod, double> GetEquipmentCombineMods(EEquipmentMythic equipment)
        {
            var mods = new Dictionary<ECombineMod, double>();

            if (equipment == EEquipmentMythic.None)
                return mods;

            var equipData = GetEquipmentData(equipment);
            if (equipData == null)
                return mods;

            // CombineModValueList에서 MOD 추출
            if (equipData.CombineModValueList != null)
            {
                foreach (var modEntry in equipData.CombineModValueList)
                {
                    ECombineMod modType = modEntry.Key;
                    var modValueList = modEntry.Value;

                    if (modValueList != null && modValueList.ModValues != null && modValueList.ModValues.Count > 0)
                    {
                        // 첫 번째 값 사용 (Grade 0 기준)
                        double value = modValueList.ModValues[0].GetValue;
                        mods[modType] = value;
                    }
                }
            }

            return mods;
        }

        /// <summary>
        /// 장비 데이터 가져오기 (공통 메소드)
        /// </summary>
        private GameDB_Client_EquipmentMythicMod GetEquipmentData(EEquipmentMythic equipment)
        {
            if (!Application.isPlaying || GameDBClientManager.Instance == null)
                return null;

            var equipmentMythicDB = GameDBClientManager.Instance.GameDB_Equipment?.EquipmentMythic;
            if (equipmentMythicDB == null || equipmentMythicDB.MapData == null)
                return null;

            // 장비 데이터 찾기
            GameDB_Client_EquipmentMythicMod equipData = null;
            foreach (var equipTypeEntry in equipmentMythicDB.MapData)
            {
                if (equipTypeEntry.Value.MythicMods != null &&
                    equipTypeEntry.Value.MythicMods.TryGetValue(equipment, out equipData))
                {
                    break;
                }
            }

            return equipData;
        }
    }

    /// <summary>
    /// 룬 데이터 제공자
    /// 주문에 맞는 룬 목록과 MOD 정보 제공
    /// </summary>
    public class RuneDataProvider
    {
        private ESkill targetSpell;

        public RuneDataProvider(ESkill targetSpell)
        {
            this.targetSpell = targetSpell;
        }

        /// <summary>
        /// 주문에 장착 가능한 모든 룬 목록 반환
        /// </summary>
        public List<ESkill> GetAvailableRunes()
        {
            var result = new List<ESkill>();

            if (!Application.isPlaying || GameDBClientManager.Instance == null)
                return result;

            var skillRuneDB = GameDBClientManager.Instance.GameDB_Skill?.Rune;
            if (skillRuneDB == null || skillRuneDB.MapData == null)
                return result;

            // 주문 정보 가져오기
            var spellDB = GameDBClientManager.Instance.GameDB_Skill?.Spell;
            if (spellDB == null || !spellDB.MapData.TryGetValue(targetSpell, out var spellData))
                return result;

            // 모든 룬 순회하며 태그 매칭 확인 (BattleSimulatorWindow.GetMatchingRuneList 로직 참고)
            foreach (var runeEntry in skillRuneDB.MapData)
            {
                ESkill runeSkill = runeEntry.Key;
                var runeData = runeEntry.Value;

                // skillrune_으로 시작하는지 확인
                if (!runeSkill.ToString().StartsWith("skillrune_"))
                    continue;

                // 스킬 태그 매칭 확인 (OR 연산)
                bool isMatch = false;
                if (spellData.SkillTags != null && runeData.SkillTags != null)
                {
                    foreach (var spellTag in spellData.SkillTags)
                    {
                        if (spellTag.Value && runeData.SkillTags.TryGetValue(spellTag.Key, out bool runeHasTag) && runeHasTag)
                        {
                            isMatch = true;
                            break;
                        }
                    }
                }

                if (isMatch)
                {
                    result.Add(runeSkill);
                }
            }

            return result;
        }

        /// <summary>
        /// 룬의 MOD 추출
        /// </summary>
        public Dictionary<EMod, double> GetRuneMods(ESkill rune, ETier tier = ETier.common)
        {
            var mods = new Dictionary<EMod, double>();

            if (rune == ESkill.None)
                return mods;

            if (!Application.isPlaying || GameDBClientManager.Instance == null)
                return mods;

            var skillRuneDB = GameDBClientManager.Instance.GameDB_Skill?.Rune;
            if (skillRuneDB == null || !skillRuneDB.MapData.TryGetValue(rune, out var runeData))
                return mods;

            // SkillTierModValues에서 MOD 추출
            if (runeData.SkillTierModValues != null)
            {
                foreach (var tierEntry in runeData.SkillTierModValues)
                {
                    var modTierValues = tierEntry.Value;
                    EMod modType = modTierValues.Mod;
                    var valuesByTier = modTierValues.ValuesByTier;

                    // 지정된 티어의 값 가져오기 (없으면 common 사용)
                    if (valuesByTier != null)
                    {
                        if (valuesByTier.TryGetValue(tier, out var value))
                        {
                            mods[modType] = value.GetValue;
                        }
                        else if (valuesByTier.TryGetValue(ETier.common, out var defaultValue))
                        {
                            mods[modType] = defaultValue.GetValue;
                        }
                    }
                }
            }

            // SkillMasterModValues에서 MOD 추출 (마스터 레벨 MOD)
            if (runeData.SkillMasterModValues != null)
            {
                foreach (var modValue in runeData.SkillMasterModValues)
                {
                    if (modValue != null && modValue.Mod != EMod.None)
                    {
                        // 마스터 MOD는 tier와 무관하게 고정값
                        mods[modValue.Mod] = modValue.Value.GetValue;
                    }
                }
            }

            return mods;
        }
    }

    #endregion

    /// <summary>
    /// 빌드 타입 (최적화 목표)
    /// </summary>
    public enum BuildType
    {
        [LabelText("Hit 빌드")]
        HitBuild,           // 명중 피해 우선, 총 DPS 보조

        [LabelText("Ailment 빌드")]
        AilmentBuild        // Ailment 피해 우선, 총 DPS 보조
    }
}
