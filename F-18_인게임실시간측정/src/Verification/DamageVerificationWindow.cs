using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Unity.EditorCoroutines.Editor;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 원클릭 피해 검증 윈도우
    /// 시뮬레이션 vs 인게임 계산을 직접 비교
    /// </summary>
    public class DamageVerificationWindow : OdinEditorWindow
    {
        [MenuItem("PX Editor/피해 검증 윈도우 (원클릭)", false, 200)]
        public static void ShowWindow()
        {
            var window = GetWindow<DamageVerificationWindow>("피해 검증");
            window.minSize = new Vector2(600, 700);
            window.Show();
        }

        #region 설명

        [TitleGroup("ℹ️ 시스템 설명", order: -1)]
        [OnInspectorGUI]
        private void DrawSystemDescription()
        {
            EditorGUILayout.HelpBox(
                "📌 피해 검증 시스템 동작 원리\n\n" +
                "• 시뮬레이션 값: SimulatorCalculator의 Breakdown 함수들을 직접 호출하여 계산\n" +
                "• 인게임 값: BattleStatus의 Result 함수들을 직접 호출하여 계산",
                MessageType.Info);
        }

        #endregion

        #region 설정

        [TitleGroup("⚙️ 검증 설정", order: 0)]
        [LabelText("배틀 시뮬레이터에서 가져오기")]
        [Button(ButtonSizes.Medium), GUIColor(0.5f, 0.8f, 1f)]
        public void LoadFromBattleSimulator()
        {
            var simulator = Resources.FindObjectsOfTypeAll<BattleSimulatorWindow>().FirstOrDefault();
            if (simulator != null)
            {
                mainSpell = simulator.mainSpell;
                spellTier = simulator.spellTier;
                spellReinforce = simulator.spellReinforceLevel; // Spell Stats의 스킬 강화도에서 가져오기
                aura = simulator.aura;
                auraTier = simulator.auraTier;
                auraReinforce = simulator.auraReinforceLevel; // Aura Stats의 스킬 강화도에서 가져오기
                defender = simulator.defender;

            }
            else
            {
                EditorUtility.DisplayDialog("오류", "배틀 시뮬레이터 윈도우를 찾을 수 없습니다.\nPX Editor > 배틀 시뮬레이터 를 먼저 열어주세요.", "확인");
            }
        }

        [TitleGroup("⚙️ 검증 설정")]
        [BoxGroup("⚙️ 검증 설정/스킬")]
        [LabelText("주문 스킬")]
        public ESkill mainSpell = ESkill.None;

        [BoxGroup("⚙️ 검증 설정/스킬")]
        [LabelText("스킬 티어")]
        public ETier spellTier = ETier.common;

        [BoxGroup("⚙️ 검증 설정/스킬")]
        [LabelText("스펠 강화도")]
        public int spellReinforce = 0;

        [BoxGroup("⚙️ 검증 설정/스킬")]
        [LabelText("오라")]
        public ESkill aura = ESkill.None;

        [BoxGroup("⚙️ 검증 설정/스킬")]
        [LabelText("오라 티어")]
        public ETier auraTier = ETier.common;

        [BoxGroup("⚙️ 검증 설정/스킬")]
        [LabelText("오라 강화도")]
        public int auraReinforce = 0;

        [BoxGroup("⚙️ 검증 설정/방어자")]
        [LabelText("방어자 설정")]
        [HideLabel]
        public SimulatorDefender defender;

        #endregion

        #region 검증 실행

        [TitleGroup("🔍 검증 실행", order: 1)]
        [InfoBox("$GetQuickSummary", InfoMessageType.None, VisibleIf = "HasResult")]
        [Button(ButtonSizes.Gigantic, Name = "🔍 원클릭 전체 검증"), GUIColor(0.3f, 1f, 0.3f)]
        public void RunVerification()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("오류", "플레이 모드에서만 검증을 실행할 수 있습니다.", "확인");
                return;
            }

            // 배틀 시뮬레이터 찾기
            var simulator = Resources.FindObjectsOfTypeAll<BattleSimulatorWindow>().FirstOrDefault();
            if (simulator == null)
            {
                EditorUtility.DisplayDialog("오류", "배틀 시뮬레이터 윈도우를 찾을 수 없습니다.\nPX Editor > 배틀 시뮬레이터를 먼저 열어주세요.", "확인");
                return;
            }

            if (simulator.mainSpell == ESkill.None && simulator.aura == ESkill.None)
            {
                EditorUtility.DisplayDialog("오류", "검증할 스킬이 없습니다.\n배틀 시뮬레이터에서 주문 스킬 또는 오라를 선택해주세요.", "확인");
                return;
            }

            // 코루틴으로 시뮬레이션 실행 후 검증
            EditorCoroutineUtility.StartCoroutineOwnerless(RunVerificationRoutine(simulator));
        }

        /// <summary>
        /// 시뮬레이션 실행 후 검증하는 코루틴
        /// </summary>
        private IEnumerator RunVerificationRoutine(BattleSimulatorWindow simulator)
        {
            Debug.Log("[피해 검증] 🔄 시뮬레이션 실행 중...");

            // 1. 배틀 시뮬레이터의 시뮬레이션 실행 (장착 + DPS 계산)
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(simulator.RunSimulation());

            Debug.Log("[피해 검증] ✅ 시뮬레이션 완료. 검증 시작...");

            // 2. 시뮬레이터에서 설정값 동기화
            mainSpell = simulator.mainSpell;
            spellTier = simulator.spellTier;
            spellReinforce = simulator.spellReinforceLevel;
            aura = simulator.aura;
            auraTier = simulator.auraTier;
            auraReinforce = simulator.auraReinforceLevel;
            defender = simulator.defender;

            if (defender == null)
            {
                defender = SimulatorDefender.CreateDefault1000FloorBoss();
            }

            // 3. 검증 실행
            lastResult = DamageVerificationSystem.RunFullVerification(
                mainSpell, spellTier, spellReinforce,
                aura, auraTier, auraReinforce,
                defender
            );

            // 4. 리포트 생성
            lastReport = DamageVerificationSystem.GenerateReport(lastResult);

            // 5. 자동 저장
            string savedFilePath = SaveReportSilent();

            Debug.Log("[피해 검증] ✅ 검증 완료!");

            // 6. 완료 메시지 표시
            if (!string.IsNullOrEmpty(savedFilePath))
            {
                string message = $"피해 검증이 완료되었습니다.\n\n리포트 파일:\n{savedFilePath}";
                EditorUtility.DisplayDialog("검증 완료", message, "확인");
            }

            // UI 갱신
            Repaint();
        }

        /// <summary>
        /// 리포트 자동 저장 (Dialog 없이)
        /// </summary>
        /// <returns>저장된 파일의 전체 경로</returns>
        private string SaveReportSilent()
        {
            if (string.IsNullOrEmpty(lastReport)) return null;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = $"SimVsInGameDps_{timestamp}.txt";
            string dirPath = "Assets/Editor/BattleSimulator/Reports/SimVsInGameDps";

            string fullDirPath = System.IO.Path.Combine(Application.dataPath.Replace("/Assets", ""), dirPath);
            if (!System.IO.Directory.Exists(fullDirPath))
            {
                System.IO.Directory.CreateDirectory(fullDirPath);
            }

            string fullPath = System.IO.Path.Combine(fullDirPath, fileName);
            System.IO.File.WriteAllText(fullPath, lastReport, System.Text.Encoding.UTF8);
            AssetDatabase.Refresh();

            return fullPath;
        }

        private bool HasResult => lastResult != null;

        private string GetQuickSummary()
        {
            if (lastResult == null) return "";
            return lastResult.GetSummary();
        }

        #endregion

        #region 결과 표시

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🔮 Spell")]
        [ShowIf("@lastResult?.Spell?.IsEnabled == true")]
        [LabelText("")]
        [DisplayAsString(false), HideLabel]
        public string spellResultDisplay => FormatSpellResult();

        [TabGroup("📊 검증 결과/Tabs", "🌀 Aura")]
        [ShowIf("@lastResult?.Aura?.IsEnabled == true")]
        [LabelText("")]
        [DisplayAsString(false), HideLabel]
        public string auraResultDisplay => FormatAuraResult();

        [TabGroup("📊 검증 결과/Tabs", "💥 Ailment")]
        [ShowIf("@lastResult?.Ailment?.IsEnabled == true")]
        [LabelText("")]
        [DisplayAsString(false), HideLabel]
        public string ailmentResultDisplay => FormatAilmentResult();

        [TabGroup("📊 검증 결과/Tabs", "☠️ DoT")]
        [ShowIf("@lastResult?.Dot?.IsEnabled == true")]
        [LabelText("")]
        [DisplayAsString(false), HideLabel]
        public string dotResultDisplay => FormatDotResult();

        [TabGroup("📊 검증 결과/Tabs", "📈 총합")]
        [ShowIf("HasResult")]
        [LabelText("")]
        [DisplayAsString(false), HideLabel]
        public string totalResultDisplay => FormatTotalResult();

        // ===== 👹 몬스터→플레이어 탭 =====
        [TabGroup("📊 검증 결과/Tabs", "👹 몬스터→플레이어")]
        [ShowIf("@lastResult?.MonsterToPlayer?.IsEnabled == true")]
        [LabelText("")]
        [DisplayAsString(false), HideLabel]
        public string monsterToPlayerResultDisplay => FormatMonsterToPlayerResult();

        // ===== 🎯 실시간 검증 탭 =====
        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🎯 실시간 검증")]
        [InfoBox(
            "📖 사용 방법\n" +
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "① 배틀 시뮬레이터에서 스킬 설정 후 시뮬레이션 실행\n" +
            "② [시뮬 예측 DPS 가져오기] 버튼 클릭\n" +
            "③ 플레이 모드에서 전투 시작\n" +
            "④ [▶ 수집 시작] 버튼 클릭\n" +
            "⑤ 전투 진행 → 목표 도달 시 자동 완료\n" +
            "⑥ 측정 DPS vs 시뮬 DPS 비교 결과 확인",
            InfoMessageType.Info)]
        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/설정")]
        [LabelText("샘플 수준")]
        [InfoBox(
            "📊 샘플 수준 설명\n" +
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "• Minimum: 100회 타격 / 60초 - 빠른 확인용\n" +
            "• Normal:  300회 타격 / 180초 (3분) - 일반 검증 (권장)\n" +
            "• Precise: 500회 타격 / 360초 (6분) - 정밀 분석용\n" +
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            "💡 타격 횟수 또는 시간 중 먼저 도달하면 완료됩니다.",
            InfoMessageType.None)]
        [EnumToggleButtons]
        public ESampleLevel sampleLevel = ESampleLevel.Normal;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/설정")]
        [LabelText("시뮬 예측 DPS 가져오기")]
        [Button(ButtonSizes.Medium), GUIColor(0.5f, 0.8f, 1f)]
        public void LoadSimulatedDPS()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("오류", "플레이 모드에서만 실행할 수 있습니다.", "확인");
                return;
            }

            var simulator = Resources.FindObjectsOfTypeAll<BattleSimulatorWindow>().FirstOrDefault();
            if (simulator != null)
            {
                // 방어 적용 후 최종 DPS 값 파싱 (attack_final* 필드 사용)
                expectedSpellDPS = ParseDisplayValue(simulator.attack_finalSpellDamageDisplay);
                expectedAuraDPS = ParseDisplayValue(simulator.attack_finalAuraDamageDisplay);
                expectedAilmentDPS = ParseDisplayValue(simulator.attack_finalAilmentDamageDisplay);
                expectedDotDPS = ParseDisplayValue(simulator.attack_finalDotDamageDisplay);
                expectedCritRate = ParsePercentValue(simulator.criticalChanceDisplay);
                expectedCritBlowRate = ParsePercentValue(simulator.auraCriticalChanceDisplay);

                // 치명타 배율 로드 (범위 검증용)
                expectedCritMultiplier = ParsePercentValue(simulator.criticalMultiplierDisplay);
                expectedCritBlowMultiplier = ParsePercentValue(simulator.criticalBlowMultiplierDisplay);

                // 시전 속도 로드 (1타 피해 계산용) - 최종 시전 속도 사용
                expectedCastSpeed = ParseDisplayValue(simulator.finalCastSpeedDisplay);

                // 비크리 피해 계산 (범위 검증용)
                // 평균 1타 피해 = DPS / 시전 속도
                // 비크리 피해 = 평균 1타 피해 / 평균 크리 배율
                if (expectedCastSpeed > 0)
                {
                    double avgSpellHit = expectedSpellDPS / expectedCastSpeed;
                    double critRateRatio = expectedCritRate / 100.0;
                    double critMultRatio = expectedCritMultiplier / 100.0;
                    double critBlowRateRatio = expectedCritBlowRate / 100.0;
                    double critBlowMultRatio = expectedCritBlowMultiplier / 100.0;

                    // 평균 크리 배율 계산 (크리 확률에 따라)
                    double avgCritMult;
                    if (critRateRatio >= 1.0) // 100% 이상
                    {
                        // (1-크리일격확률) × 크리배율 + 크리일격확률 × 크리배율 × 크리일격배율
                        avgCritMult = (1 - critBlowRateRatio) * critMultRatio + critBlowRateRatio * critMultRatio * critBlowMultRatio;
                    }
                    else
                    {
                        // (1-크리확률) × 1 + 크리확률 × 크리배율
                        avgCritMult = (1 - critRateRatio) * 1.0 + critRateRatio * critMultRatio;
                    }

                    expectedSpellNonCritDamage = avgCritMult > 0 ? avgSpellHit / avgCritMult : avgSpellHit;

                    // Aura도 동일하게 계산 (Aura DPS = 1회 피해, 틱 레이트 1.0 기준)
                    expectedAuraNonCritDamage = avgCritMult > 0 ? expectedAuraDPS / avgCritMult : expectedAuraDPS;

                    // DoT도 동일하게 계산 (DoT 1틱 피해 기반)
                    // DoT 1틱 피해 = DPS (틱 레이트 1.0 기준)
                    expectedDotNonCritDamage = avgCritMult > 0 ? expectedDotDPS / avgCritMult : expectedDotDPS;
                    expectedDotCritMultiplier = critMultRatio;

                    // Ailment도 동일하게 계산 (Ailment DPS = 1회 피해, 초당 1틱 기준)
                    expectedAilmentNonCritDamage = avgCritMult > 0 ? expectedAilmentDPS / avgCritMult : expectedAilmentDPS;
                }

                if (expectedSpellDPS > 0 || expectedAuraDPS > 0 || expectedAilmentDPS > 0 || expectedDotDPS > 0)
                {
                    Debug.Log($"[실시간 검증] 방어 적용 후 DPS 로드: Spell={expectedSpellDPS:N0}, Aura={expectedAuraDPS:N0}, Ailment={expectedAilmentDPS:N0}, DoT={expectedDotDPS:N0}");
                    Debug.Log($"[실시간 검증] 치명타 정보: 확률={expectedCritRate:F1}%, 배율={expectedCritMultiplier:F1}%, 일격배율={expectedCritBlowMultiplier:F1}%");
                    Debug.Log($"[실시간 검증] 시전 속도: {expectedCastSpeed:F3}회/초, 비크리 피해: Spell={expectedSpellNonCritDamage:N0}, Aura={expectedAuraNonCritDamage:N0}");
                }
                else
                {
                    EditorUtility.DisplayDialog("오류", "배틀 시뮬레이터의 DPS 결과를 찾을 수 없습니다.\n먼저 시뮬레이션을 실행해주세요.", "확인");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("오류", "배틀 시뮬레이터 창을 찾을 수 없습니다.", "확인");
            }
        }

        /// <summary>
        /// 포맷된 숫자 문자열 파싱 (예: "1,234,567" 또는 "1.2M")
        /// </summary>
        private double ParseDisplayValue(string display)
        {
            if (string.IsNullOrEmpty(display) || display == "0")
                return 0;

            // 속성 표시 텍스트 제거 (예: "(화염)", "(물리)" 등)
            int parenIndex = display.IndexOf('(');
            if (parenIndex > 0)
                display = display.Substring(0, parenIndex).Trim();

            // 공백으로 구분된 단위 제거 (예: "2.168 회/초" → "2.168")
            int spaceIndex = display.IndexOf(' ');
            if (spaceIndex > 0)
                display = display.Substring(0, spaceIndex);

            // 쉼표 제거
            display = display.Replace(",", "");

            // K/M/B 접미사 처리
            double multiplier = 1;
            if (display.EndsWith("K", System.StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1000;
                display = display.Substring(0, display.Length - 1);
            }
            else if (display.EndsWith("M", System.StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1000000;
                display = display.Substring(0, display.Length - 1);
            }
            else if (display.EndsWith("B", System.StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1000000000;
                display = display.Substring(0, display.Length - 1);
            }

            if (double.TryParse(display, out double value))
                return value * multiplier;

            return 0;
        }

        /// <summary>
        /// 퍼센트 문자열 파싱 (예: "35.5%")
        /// </summary>
        private double ParsePercentValue(string display)
        {
            if (string.IsNullOrEmpty(display))
                return 0;

            display = display.Replace("%", "").Trim();

            if (double.TryParse(display, out double value))
                return value;

            return 0;
        }

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("Spell DPS 예측")]
        [SuffixLabel("$GetSpellDPSFormatted", Overlay = false)]
        public double expectedSpellDPS;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("Ailment DPS 예측")]
        [SuffixLabel("$GetAilmentDPSFormatted", Overlay = false)]
        public double expectedAilmentDPS;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("Ailment 비크리 1회 피해")]
        [SuffixLabel("$GetAilmentNonCritFormatted", Overlay = false)]
        [InfoBox("Ailment 범위 검증: 비크리 ~ 크리×크리일격 (최초 적용 시 1회 계산, duration 분할)")]
        public double expectedAilmentNonCritDamage;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("Aura DPS 예측")]
        [SuffixLabel("$GetAuraDPSFormatted", Overlay = false)]
        public double expectedAuraDPS;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("DoT DPS 예측")]
        [SuffixLabel("$GetDotDPSFormatted", Overlay = false)]
        public double expectedDotDPS;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("DoT 1틱 피해")]
        [SuffixLabel("$GetDot1TickFormatted", Overlay = false)]
        [InfoBox("DoT 범위 검증에 사용됩니다. 측정 1틱 피해가 ±20% 범위 내면 PASS")]
        public double expectedDot1TickDamage;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("크리티컬 확률 예측 (%)")]
        public double expectedCritRate;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("크리티컬 블로우 확률 예측 (%)")]
        public double expectedCritBlowRate;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("크리티컬 배율 (%)")]
        [InfoBox("치명타 범위 검증에 사용됩니다. 피해가 비크리티컬~크리티컬 범위 내면 PASS")]
        public double expectedCritMultiplier;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("크리티컬 일격 배율 (%)")]
        public double expectedCritBlowMultiplier;

        [BoxGroup("📊 검증 결과/Tabs/🎯 실시간 검증/예측값")]
        [LabelText("시전 속도 (회/초)")]
        [InfoBox("1타 피해 계산에 사용됩니다. 1타 피해 = DPS / 시전 속도")]
        public double expectedCastSpeed;

        // 범위 검증용 계산값 (내부 사용)
        private double expectedSpellNonCritDamage;
        private double expectedAuraNonCritDamage;
        private double expectedDotNonCritDamage;
        private double expectedDotCritMultiplier;

        // 포맷된 DPS 문자열 (SuffixLabel용)
        private string GetSpellDPSFormatted() => FormatLargeNumber(expectedSpellDPS);
        private string GetAuraDPSFormatted() => FormatLargeNumber(expectedAuraDPS);
        private string GetAilmentDPSFormatted() => FormatLargeNumber(expectedAilmentDPS);
        private string GetAilmentNonCritFormatted() => FormatLargeNumber(expectedAilmentNonCritDamage);
        private string GetDotDPSFormatted() => FormatLargeNumber(expectedDotDPS);
        private string GetDot1TickFormatted() => FormatLargeNumber(expectedDot1TickDamage);

        /// <summary>
        /// 큰 숫자를 읽기 쉬운 형태로 포맷 (K, M, B, T 단위)
        /// </summary>
        private string FormatLargeNumber(double value)
        {
            if (value <= 0) return "";

            if (value >= 1_000_000_000_000) // T (Trillion)
                return $"({value / 1_000_000_000_000:N2}T)";
            if (value >= 1_000_000_000) // B (Billion)
                return $"({value / 1_000_000_000:N2}B)";
            if (value >= 1_000_000) // M (Million)
                return $"({value / 1_000_000:N2}M)";
            if (value >= 1_000) // K (Thousand)
                return $"({value / 1_000:N2}K)";

            return $"({value:N0})";
        }

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🎯 실시간 검증")]
        [HorizontalGroup("📊 검증 결과/Tabs/🎯 실시간 검증/Buttons")]
        [Button(ButtonSizes.Large, Name = "▶ 수집 시작"), GUIColor(0.3f, 1f, 0.3f)]
        [EnableIf("@!LiveDamageCollector.Instance.IsCollecting")]
        public void StartLiveCollection()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("오류", "플레이 모드에서만 실행할 수 있습니다.", "확인");
                return;
            }

            // 시뮬레이터를 다시 실행하고 DPS 값을 가져온 후 수집 시작
            EditorCoroutineUtility.StartCoroutineOwnerless(StartLiveCollectionRoutine());
        }

        /// <summary>
        /// 시뮬레이터 실행 후 수집 시작하는 코루틴
        /// </summary>
        private IEnumerator StartLiveCollectionRoutine()
        {
            var simulator = Resources.FindObjectsOfTypeAll<BattleSimulatorWindow>().FirstOrDefault();
            if (simulator == null)
            {
                EditorUtility.DisplayDialog("오류", "배틀 시뮬레이터 창을 찾을 수 없습니다.", "확인");
                yield break;
            }

            // 1. 시뮬레이터 실행 (최신 DPS 값 계산)
            Debug.Log("[실시간 검증] 시뮬레이터 실행 중...");
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(simulator.RunSimulation());

            // 2. 시뮬 예측 DPS 가져오기
            LoadSimulatedDPS();

            // 3. 시뮬 예측값이 로드되지 않았는지 확인
            if (expectedSpellDPS <= 0 && expectedAuraDPS <= 0 && expectedAilmentDPS <= 0 && expectedDotDPS <= 0)
            {
                EditorUtility.DisplayDialog(
                    "시뮬 예측값 없음",
                    "시뮬레이터에서 DPS 값을 가져올 수 없습니다.\n\n" +
                    "배틀 시뮬레이터에서 스킬을 설정해주세요.",
                    "확인");
                yield break;
            }

            // 4. 수집 시작
            EditorLogCollector.StartLiveDamageCollection(sampleLevel);

            // 이벤트 구독
            LiveDamageCollector.Instance.OnCollectionCompleted -= OnLiveCollectionCompleted;
            LiveDamageCollector.Instance.OnCollectionCompleted += OnLiveCollectionCompleted;

            // UI 업데이트 등록
            EditorApplication.update -= UpdateLiveCollectionUI;
            EditorApplication.update += UpdateLiveCollectionUI;

            Debug.Log($"[실시간 검증] 수집 시작 (목표: {LiveDamageCollector.Instance.TargetHits}회 / {LiveDamageCollector.Instance.TargetDuration}초)");
        }

        [HorizontalGroup("📊 검증 결과/Tabs/🎯 실시간 검증/Buttons")]
        [Button(ButtonSizes.Large, Name = "⏹ 수집 중단"), GUIColor(1f, 0.5f, 0.3f)]
        [EnableIf("@LiveDamageCollector.Instance.IsCollecting")]
        public void StopLiveCollection()
        {
            EditorLogCollector.StopLiveDamageCollection();
            EditorApplication.update -= UpdateLiveCollectionUI;
            BuildLiveVerificationResult();
            Debug.Log("[실시간 검증] 수집 중단");
        }

        [HorizontalGroup("📊 검증 결과/Tabs/🎯 실시간 검증/Buttons")]
        [Button(ButtonSizes.Large, Name = "🔄 초기화"), GUIColor(0.8f, 0.8f, 0.8f)]
        public void ClearLiveCollection()
        {
            EditorLogCollector.ClearLiveDamageCollection();
            lastLiveResult = null;
            Debug.Log("[실시간 검증] 초기화");
        }

        private void OnLiveCollectionCompleted()
        {
            EditorApplication.update -= UpdateLiveCollectionUI;
            BuildLiveVerificationResult();
            Repaint();
            Debug.Log("[실시간 검증] 수집 완료!");

            // 완료 메시지 표시
            string resultIcon = lastLiveResult?.IsOverallValid == true ? "✅" : "❌";
            string resultText = lastLiveResult?.IsOverallValid == true ? "검증 통과" : "검증 실패";
            EditorUtility.DisplayDialog(
                "수집 완료",
                $"실시간 피해 수집이 완료되었습니다.\n\n" +
                $"수집 시간: {lastLiveResult?.Duration:F1}초\n" +
                $"총 타격: {LiveDamageCollector.Instance.TotalHits}회\n\n" +
                $"결과: {resultIcon} {resultText}",
                "확인");
        }

        private void UpdateLiveCollectionUI()
        {
            Repaint();
        }

        private void BuildLiveVerificationResult()
        {
            // 크리 배율을 비율로 변환 (% → 비율)
            double critMultRatio = expectedCritMultiplier / 100.0;
            double critBlowMultRatio = expectedCritBlowMultiplier / 100.0;

            lastLiveResult = LiveVerificationResultBuilder.Build(
                LiveDamageCollector.Instance,
                expectedSpellDPS,
                expectedAuraDPS,
                expectedAilmentDPS,
                expectedDotDPS,
                expectedCritRate,
                expectedCritBlowRate,
                expectedCritMultiplier,
                expectedCastSpeed,
                // 범위 검증용 추가 파라미터
                expectedSpellNonCritDamage,
                critMultRatio,
                critBlowMultRatio,
                expectedAuraNonCritDamage,
                critMultRatio,
                critBlowMultRatio,
                // Ailment 범위 검증: 비크리 ~ 크리×크리일격
                expectedAilmentNonCritDamage,
                critMultRatio,
                critBlowMultRatio,
                // DoT 1틱 피해 범위 검증용
                expectedDot1TickDamage,
                // DoT 범위 검증: 비크리 ~ 크리 (Spell과 동일)
                expectedDotNonCritDamage,
                expectedDotCritMultiplier);
        }

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🎯 실시간 검증")]
        [ProgressBar(0, 1, ColorGetter = "GetProgressColor")]
        [LabelText("진행률")]
        [ShowIf("@LiveDamageCollector.Instance.IsCollecting")]
        public float liveProgress => LiveDamageCollector.Instance.Progress;

        private Color GetProgressColor() => LiveDamageCollector.Instance.IsCompleted
            ? new Color(0.3f, 1f, 0.3f)
            : new Color(0.3f, 0.7f, 1f);

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🎯 실시간 검증")]
        [TextArea(8, 12)]
        [LabelText("수집 현황")]
        [ReadOnly]
        public string liveCollectionStatus => LiveDamageCollector.Instance.GetStatusString();

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🎯 실시간 검증")]
        [ShowIf("HasLiveResult")]
        [TextArea(15, 25)]
        [LabelText("검증 결과")]
        [ReadOnly]
        public string liveResultDisplay => lastLiveResult?.GetSummary() ?? "";

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🎯 실시간 검증")]
        [ShowIf("HasLiveResult")]
        [Button("📋 결과 복사"), GUIColor(0.8f, 0.8f, 1f)]
        public void CopyLiveResult()
        {
            if (lastLiveResult != null)
            {
                GUIUtility.systemCopyBuffer = lastLiveResult.GetSummary();
            }
        }

        [HideInInspector]
        public LiveVerificationResult lastLiveResult;

        private bool HasLiveResult => lastLiveResult != null;

        // ===== 📋 상세 리포트 탭 (MOD 백분율 앞에 배치) =====
        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "📋 상세 리포트")]
        [HorizontalGroup("📊 검증 결과/Tabs/📋 상세 리포트/Buttons")]
        [Button("📋 클립보드에 복사"), GUIColor(0.8f, 0.8f, 1f)]
        [ShowIf("HasResult")]
        public void CopyReport()
        {
            if (!string.IsNullOrEmpty(lastReport))
            {
                GUIUtility.systemCopyBuffer = lastReport;
            }
        }

        [HorizontalGroup("📊 검증 결과/Tabs/📋 상세 리포트/Buttons")]
        [Button("💾 파일로 저장"), GUIColor(0.8f, 1f, 0.8f)]
        [ShowIf("HasResult")]
        public void SaveReport()
        {
            if (string.IsNullOrEmpty(lastReport)) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = $"SimVsInGameDps_{timestamp}.txt";
            string dirPath = "Assets/Editor/BattleSimulator/Reports/SimVsInGameDps";

            // 디렉토리 생성
            string fullDirPath = System.IO.Path.Combine(Application.dataPath.Replace("/Assets", ""), dirPath);
            if (!System.IO.Directory.Exists(fullDirPath))
            {
                System.IO.Directory.CreateDirectory(fullDirPath);
            }

            // 파일 저장
            string fullPath = System.IO.Path.Combine(fullDirPath, fileName);
            System.IO.File.WriteAllText(fullPath, lastReport, System.Text.Encoding.UTF8);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("저장 완료", $"검증 리포트가 저장되었습니다.\n\n{fullPath}", "확인");
        }

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "📋 상세 리포트")]
        [ShowIf("HasResult")]
        [TextArea(15, 30)]
        [LabelText("")]
        public string lastReport = "";

        // ===== 🔢 MOD 백분율 탭 =====
        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🔢 MOD 백분율")]
        [Button(ButtonSizes.Large, Name = "🔢 MOD 백분율 분석 실행"), GUIColor(1f, 0.8f, 0.3f)]
        public void RunModPercentageAnalysis()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("오류", "플레이 모드에서만 분석을 실행할 수 있습니다.", "확인");
                return;
            }

            // 피해 속성 결정 (Spell 기준, 없으면 cold 기본값)
            ESkillTag damageType = ESkillTag.skilltag_cold;
            if (mainSpell != ESkill.None)
            {
                if (GameDBUtility.TryGetSkillDBData(mainSpell, out _, out var skillDB))
                {
                    var tags = skillDB.SkillTags?.Keys.ToList();
                    if (tags != null)
                    {
                        if (tags.Contains(ESkillTag.skilltag_fire)) damageType = ESkillTag.skilltag_fire;
                        else if (tags.Contains(ESkillTag.skilltag_cold)) damageType = ESkillTag.skilltag_cold;
                        else if (tags.Contains(ESkillTag.skilltag_lightning)) damageType = ESkillTag.skilltag_lightning;
                        else if (tags.Contains(ESkillTag.skilltag_poison)) damageType = ESkillTag.skilltag_poison;
                        else if (tags.Contains(ESkillTag.skilltag_physical)) damageType = ESkillTag.skilltag_physical;
                    }
                }
            }

            // 방어자 설정
            if (defender == null)
            {
                defender = SimulatorDefender.CreateDefault1000FloorBoss();
            }

            // MOD 백분율 분석 실행
            lastModPercentageReport = ModPercentageReportSystem.AnalyzeDamageCalculation(damageType, defender);
            lastModPercentageReportText = lastModPercentageReport.GenerateReport();

            // 자동 저장
            SaveModPercentageReportSilent();
        }

        /// <summary>
        /// MOD 백분율 리포트 자동 저장
        /// </summary>
        private void SaveModPercentageReportSilent()
        {
            if (string.IsNullOrEmpty(lastModPercentageReportText)) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = $"ModPercentageReport_{timestamp}.txt";
            string dirPath = "Assets/Editor/BattleSimulator/Reports/ModPercentage";

            string fullDirPath = System.IO.Path.Combine(Application.dataPath.Replace("/Assets", ""), dirPath);
            if (!System.IO.Directory.Exists(fullDirPath))
            {
                System.IO.Directory.CreateDirectory(fullDirPath);
            }

            string fullPath = System.IO.Path.Combine(fullDirPath, fileName);
            System.IO.File.WriteAllText(fullPath, lastModPercentageReportText, System.Text.Encoding.UTF8);
            AssetDatabase.Refresh();
        }

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🔢 MOD 백분율")]
        [ShowIf("HasModPercentageReport")]
        [TextArea(20, 40)]
        [LabelText("상세 리포트")]
        public string lastModPercentageReportText = "";

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🔢 MOD 백분율")]
        [Button("📋 리포트 복사"), GUIColor(0.8f, 0.8f, 1f)]
        [ShowIf("HasModPercentageReport")]
        public void CopyModPercentageReport()
        {
            if (!string.IsNullOrEmpty(lastModPercentageReportText))
            {
                GUIUtility.systemCopyBuffer = lastModPercentageReportText;
            }
        }

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🔢 MOD 백분율")]
        [Button("💾 파일로 저장"), GUIColor(0.8f, 1f, 0.8f)]
        [ShowIf("HasModPercentageReport")]
        public void SaveModPercentageReport()
        {
            if (string.IsNullOrEmpty(lastModPercentageReportText)) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = $"ModPercentageReport_{timestamp}.txt";
            string dirPath = "Assets/Editor/BattleSimulator/Reports/ModPercentage";

            string fullDirPath = System.IO.Path.Combine(Application.dataPath.Replace("/Assets", ""), dirPath);
            if (!System.IO.Directory.Exists(fullDirPath))
            {
                System.IO.Directory.CreateDirectory(fullDirPath);
            }

            string fullPath = System.IO.Path.Combine(fullDirPath, fileName);
            System.IO.File.WriteAllText(fullPath, lastModPercentageReportText, System.Text.Encoding.UTF8);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("저장 완료", $"MOD 백분율 리포트가 저장되었습니다.\n\n{fullPath}", "확인");
        }

        [TitleGroup("📊 검증 결과", order: 2)]
        [TabGroup("📊 검증 결과/Tabs", "🔢 MOD 백분율")]
        [ShowIf("HasModPercentageReport")]
        [InfoBox("$GetModPercentageSummary", InfoMessageType.None)]
        [LabelText("")]
        [DisplayAsString(false), HideLabel]
        public string modPercentageResultDisplay => FormatModPercentageResult();

        private bool HasModPercentageReport => lastModPercentageReport != null;

        private string GetModPercentageSummary()
        {
            if (lastModPercentageReport == null) return "";
            return $"피해 속성: {lastModPercentageReport.DamageType} | 총 MOD: {lastModPercentageReport.TotalModCount}개 | 경고: {lastModPercentageReport.WarningCount}개";
        }

        private string FormatModPercentageResult()
        {
            if (lastModPercentageReport == null)
                return "분석 결과 없음";

            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("🔢 MOD 백분율 처리 요약");
            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine($"피해 속성: {lastModPercentageReport.DamageType}");
            sb.AppendLine($"분석 시각: {lastModPercentageReport.Timestamp:HH:mm:ss}");
            sb.AppendLine($"총 MOD 수: {lastModPercentageReport.TotalModCount}개");
            sb.AppendLine($"경고 수: {lastModPercentageReport.WarningCount}개");
            sb.AppendLine();

            // 단계별 요약
            foreach (var stage in lastModPercentageReport.Stages)
            {
                string warningIcon = stage.HasWarnings ? "⚠️" : "✅";
                sb.AppendLine($"{warningIcon} [{stage.StageOrder}] {stage.StageName}: {stage.ModInfos.Count}개 MOD (결과: {stage.StageResult:F4})");
            }

            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine($"📋 종합: {(lastModPercentageReport.WarningCount == 0 ? "✅ 문제 없음" : $"❌ {lastModPercentageReport.WarningCount}개 경고 발견")}");
            sb.AppendLine();
            sb.AppendLine("💡 상세 내용은 아래 리포트 또는 저장된 파일 참조");

            return sb.ToString();
        }

        #endregion

        #region 내부 데이터

        [HideInInspector]
        public FullVerificationResult lastResult;

        [HideInInspector]
        public ModPercentageReport lastModPercentageReport;

        #endregion

        #region 결과 포맷팅

        private string FormatSpellResult()
        {
            if (lastResult?.Spell == null || !lastResult.Spell.IsEnabled)
                return "Spell 미사용";

            var spell = lastResult.Spell;
            var sb = new StringBuilder();

            sb.AppendLine($"스킬: {spell.SkillType} ({spell.SkillTier})");
            sb.AppendLine($"피해 속성: {spell.DamageType}");
            sb.AppendLine($"결과: {(spell.AllPassed ? "✅ 통과" : "❌ 실패")} ({spell.PassedCount}/{spell.TotalCount})");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("단계별 계산 비교:");
            sb.AppendLine("───────────────────────────────────────────────");

            AppendComparison(sb, "1. Flat Damage", spell.FlatDamage);
            AppendComparison(sb, "2. Skill Effectiveness", spell.SkillEffectiveness);
            AppendComparison(sb, "3. Base Damage", spell.BaseDamage);
            AppendComparison(sb, "4. 크리티컬 전 피해", spell.PreCritDamage);
            sb.AppendLine("───────────────────────────────────────────────");
            AppendComparison(sb, "5. 크리티컬 확률", spell.CriticalChance);
            AppendComparison(sb, "6. 크리티컬 배율", spell.CriticalMultiplier);
            AppendComparison(sb, "7. 치명타 일격 확률", spell.CritBlowChance);
            AppendComparison(sb, "8. 치명타 일격 배율", spell.CritBlowMultiplier);
            AppendComparison(sb, "9. 평균 크리티컬 배율", spell.AverageCritMultiplier);
            sb.AppendLine("───────────────────────────────────────────────");
            AppendComparison(sb, "10. 시전 속도", spell.CastSpeed);
            AppendComparison(sb, "11. Spell DPS", spell.FinalDPS);

            return sb.ToString();
        }

        private string FormatAuraResult()
        {
            if (lastResult?.Aura == null || !lastResult.Aura.IsEnabled)
                return "Aura 미사용";

            var aura = lastResult.Aura;
            var sb = new StringBuilder();

            sb.AppendLine($"오라: {aura.AuraType} ({aura.AuraTier})");
            sb.AppendLine($"피해 속성: {aura.DamageType}");
            sb.AppendLine($"결과: {(aura.AllPassed ? "✅ 통과" : "❌ 실패")} ({aura.PassedCount}/{aura.TotalCount})");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("단계별 계산 비교:");
            sb.AppendLine("───────────────────────────────────────────────");

            AppendComparison(sb, "1. Flat Damage", aura.BaseDamage);
            AppendComparison(sb, "2. 피해 비율", aura.DamagePercent);
            AppendComparison(sb, "3. Inc 배율", aura.IncMultiplier);
            AppendComparison(sb, "4. More 배율", aura.MoreMultiplier);
            sb.AppendLine("───────────────────────────────────────────────");
            AppendComparison(sb, "5. 크리티컬 확률", aura.CriticalChance);
            AppendComparison(sb, "6. 크리티컬 배율", aura.CriticalMultiplier);
            AppendComparison(sb, "7. 평균 피해", aura.AverageDamage);
            sb.AppendLine("───────────────────────────────────────────────");
            AppendComparison(sb, "8. Aura DPS", aura.FinalDPS);

            return sb.ToString();
        }

        private string FormatAilmentResult()
        {
            if (lastResult?.Ailment == null || !lastResult.Ailment.IsEnabled)
                return "Ailment 미사용";

            var ailment = lastResult.Ailment;
            var sb = new StringBuilder();

            sb.AppendLine($"활성화된 상태이상: {ailment.EnabledCount}종");
            sb.AppendLine($"결과: {(ailment.AllPassed ? "✅ 통과" : "❌ 실패")} ({ailment.PassedCount}/{ailment.EnabledCount})");
            sb.AppendLine();

            foreach (var kvp in ailment.AilmentResults)
            {
                var a = kvp.Value;
                if (!a.IsEnabled) continue;

                sb.AppendLine("═══════════════════════════════════════════════");
                sb.AppendLine($"💥 {a.AilmentName} ({a.DamageType})");
                sb.AppendLine("───────────────────────────────────────────────");

                AppendComparison(sb, "발동 확률", a.ProcChance);
                AppendComparison(sb, "Flat Damage", a.FlatDamage);
                AppendComparison(sb, "Inc 배율", a.IncMultiplier);
                AppendComparison(sb, "More 배율", a.MoreMultiplier);
                AppendComparison(sb, "DPS", a.DPS);
                sb.AppendLine();
            }

            sb.AppendLine("═══════════════════════════════════════════════");
            AppendComparison(sb, "Ailment 총 DPS", ailment.TotalDPS);

            return sb.ToString();
        }

        private string FormatDotResult()
        {
            if (lastResult?.Dot == null || !lastResult.Dot.IsEnabled)
                return "DoT 미사용";

            var dot = lastResult.Dot;
            var sb = new StringBuilder();

            sb.AppendLine($"DoT: {dot.DotName}");
            sb.AppendLine($"피해 속성: {dot.DamageType}");
            sb.AppendLine($"결과: {(dot.AllPassed ? "✅ 통과" : "❌ 실패")} ({dot.PassedCount}/{dot.TotalCount})");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("단계별 계산 비교:");
            sb.AppendLine("───────────────────────────────────────────────");

            AppendComparison(sb, "1. Flat Damage", dot.BaseDamage);
            AppendComparison(sb, "2. 피해 비율", dot.DamagePercent);
            AppendComparison(sb, "3. 지속시간", dot.Duration);
            AppendComparison(sb, "4. 틱 간격", dot.TickInterval);
            AppendComparison(sb, "5. Inc 배율", dot.IncMultiplier);
            AppendComparison(sb, "6. More 배율", dot.MoreMultiplier);
            sb.AppendLine("───────────────────────────────────────────────");
            AppendComparison(sb, "7. 크리티컬 확률", dot.CriticalChance);
            AppendComparison(sb, "8. 크리티컬 배율", dot.CriticalMultiplier);
            AppendComparison(sb, "9. 틱당 피해", dot.TickDamage);
            sb.AppendLine("───────────────────────────────────────────────");
            AppendComparison(sb, "10. DoT DPS", dot.FinalDPS);

            return sb.ToString();
        }

        private string FormatTotalResult()
        {
            if (lastResult == null)
                return "검증 결과 없음";

            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("📊 DPS 구성");
            sb.AppendLine("───────────────────────────────────────────────");

            if (lastResult.Spell.IsEnabled)
                sb.AppendLine($"🔮 Spell:   {lastResult.Spell.FinalDPS?.SimValue:N0} ({lastResult.SpellDPSRatio:F1}%)");
            if (lastResult.Ailment.IsEnabled)
                sb.AppendLine($"💥 Ailment: {lastResult.Ailment.TotalDPS?.SimValue:N0} ({lastResult.AilmentDPSRatio:F1}%)");
            if (lastResult.Aura.IsEnabled)
                sb.AppendLine($"🌀 Aura:    {lastResult.Aura.FinalDPS?.SimValue:N0} ({lastResult.AuraDPSRatio:F1}%)");
            if (lastResult.Dot.IsEnabled)
                sb.AppendLine($"☠️ DoT:     {lastResult.Dot.FinalDPS?.SimValue:N0} ({lastResult.DotDPSRatio:F1}%)");

            sb.AppendLine("───────────────────────────────────────────────");

            if (lastResult.GrandTotalDPS != null)
            {
                sb.AppendLine();
                sb.AppendLine($"📊 총 DPS 비교:");
                sb.AppendLine($"  시뮬레이션: {lastResult.GrandTotalDPS.SimValue:N0}");
                sb.AppendLine($"  인게임:     {lastResult.GrandTotalDPS.IngameValue:N0}");
                sb.AppendLine($"  오차:       {lastResult.GrandTotalDPS.ErrorPercent:F2}%");
                sb.AppendLine($"  상태:       {lastResult.GrandTotalDPS.GetStatusIcon()}");
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine($"🏁 최종 결과: {(lastResult.AllPassed ? "✅ 모든 검증 통과" : "❌ 일부 검증 실패")}");
            sb.AppendLine($"⏱️ 소요 시간: {lastResult.VerificationDuration:F0}ms");

            return sb.ToString();
        }

        private string FormatMonsterToPlayerResult()
        {
            if (lastResult?.MonsterToPlayer == null || !lastResult.MonsterToPlayer.IsEnabled)
                return "몬스터 → 플레이어 검증 미실행";

            var mtp = lastResult.MonsterToPlayer;
            var sb = new StringBuilder();

            // 헤더
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("👹 몬스터 → 플레이어 피해 분석");
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine();

            // 몬스터 정보
            sb.AppendLine("【몬스터 정보】");
            sb.AppendLine($"  이름: {mtp.MonsterName}");
            sb.AppendLine($"  타입: {mtp.MonsterTypeDisplay}");
            sb.AppendLine($"  공격 속성: {mtp.AttackElement}");
            sb.AppendLine();

            // 몬스터 공격력
            sb.AppendLine("【몬스터 공격력】");
            sb.AppendLine($"  공격력: {mtp.MonsterRawDamage?.SimValue:N0}");
            sb.AppendLine($"  공격 속도: {mtp.MonsterAttackSpeed?.SimValue:F2}회/초");
            sb.AppendLine($"  순수 DPS: {mtp.MonsterRawDPS?.SimValue:N0}");
            sb.AppendLine();

            // 플레이어 방어 스탯 (시뮬 vs 인게임 비교)
            sb.AppendLine("【플레이어 방어 스탯】 (시뮬 vs 인게임)");
            sb.AppendLine("───────────────────────────────────────────────");
            AppendComparisonLine(sb, "❤️ 생명력", mtp.PlayerLife);
            sb.AppendLine($"  물리 저항: {mtp.PlayerPhysicalResistance?.SimValue:F1}%");
            sb.AppendLine($"  화염 저항: {mtp.PlayerFireResistance?.SimValue:F1}%");
            sb.AppendLine($"  냉기 저항: {mtp.PlayerColdResistance?.SimValue:F1}%");
            sb.AppendLine($"  번개 저항: {mtp.PlayerLightningResistance?.SimValue:F1}%");
            sb.AppendLine($"  독 저항: {mtp.PlayerPoisonResistance?.SimValue:F1}%");
            sb.AppendLine($"  피해 감소: {mtp.PlayerDamageReduction?.SimValue:F1}%");
            sb.AppendLine();

            // 최종 피해 계산
            sb.AppendLine("【최종 피해 계산】");
            sb.AppendLine($"  적용 저항 ({mtp.AttackElement}): {mtp.AppliedResistance?.SimValue:F1}%");
            sb.AppendLine($"  → 저항 적용 후 DPS: {mtp.AfterResistanceDPS?.SimValue:N0}");
            sb.AppendLine($"  → 최종 받는 DPS: {mtp.FinalDamageTaken?.SimValue:N0}");
            sb.AppendLine();

            // 핵심 결과 (시뮬 vs 인게임 비교)
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("★ 핵심 결과 (시뮬 vs 인게임)");
            sb.AppendLine("───────────────────────────────────────────────");
            sb.AppendLine($"  💥 1회 피격 피해: {mtp.DamagePerHit?.SimValue:N0}");
            AppendComparisonLine(sb, "💀 사망까지 피격 횟수", mtp.HitsToKill, "회");
            AppendComparisonLine(sb, "⏱️ 예상 생존 시간", mtp.SurvivalTime, "초");
            sb.AppendLine();

            // 생존 평가 (인게임 값 기준)
            double hitsToKill = mtp.HitsToKill?.IngameValue ?? 0;
            double survivalTime = mtp.SurvivalTime?.IngameValue ?? 0;

            string survivalStatus;
            string survivalIcon;

            if (hitsToKill <= 1)
            {
                survivalStatus = "원킬 (1회 피격에 사망)";
                survivalIcon = "❌";
            }
            else if (hitsToKill <= 3)
            {
                survivalStatus = "위험 (2~3회 피격에 사망)";
                survivalIcon = "⚠️";
            }
            else if (survivalTime < 10)
            {
                survivalStatus = "생존 시간 부족 (10초 미만)";
                survivalIcon = "⚠️";
            }
            else if (survivalTime < 30)
            {
                survivalStatus = "생존 가능 (10~30초)";
                survivalIcon = "✅";
            }
            else
            {
                survivalStatus = "안전 (30초 이상)";
                survivalIcon = "✅";
            }

            sb.AppendLine($"📊 생존 평가: {survivalIcon} {survivalStatus}");
            sb.AppendLine();

            // 검증 결과
            sb.AppendLine("───────────────────────────────────────────────");
            bool lifeMatch = mtp.PlayerLife?.IsMatch ?? true;
            bool survivalMatch = mtp.SurvivalTime?.IsMatch ?? true;
            bool hitsMatch = mtp.HitsToKill?.IsMatch ?? true;
            bool allMatch = lifeMatch && survivalMatch && hitsMatch;

            sb.AppendLine($"🔍 검증 결과: {(allMatch ? "✅ 시뮬 vs 인게임 일치" : "❌ 시뮬 vs 인게임 불일치")}");
            if (!allMatch)
            {
                if (!lifeMatch) sb.AppendLine($"   ⚠️ 생명력 오차: {mtp.PlayerLife?.ErrorPercent:F2}%");
                if (!survivalMatch) sb.AppendLine($"   ⚠️ 생존 시간 오차: {mtp.SurvivalTime?.ErrorPercent:F2}%");
                if (!hitsMatch) sb.AppendLine($"   ⚠️ 피격 횟수 오차: {mtp.HitsToKill?.ErrorPercent:F2}%");
            }
            sb.AppendLine("═══════════════════════════════════════════════");

            return sb.ToString();
        }

        /// <summary>
        /// 시뮬 vs 인게임 비교 라인 추가
        /// </summary>
        private void AppendComparisonLine(StringBuilder sb, string label, ValueComparison comp, string suffix = "")
        {
            if (comp == null)
            {
                sb.AppendLine($"  {label}: (데이터 없음)");
                return;
            }

            string status = comp.IsMatch ? "✅" : "❌";
            string simVal = FormatLargeNumber(comp.SimValue).TrimStart('(').TrimEnd(')');
            string ingameVal = FormatLargeNumber(comp.IngameValue).TrimStart('(').TrimEnd(')');

            if (string.IsNullOrEmpty(simVal)) simVal = comp.SimValue.ToString("N0");
            if (string.IsNullOrEmpty(ingameVal)) ingameVal = comp.IngameValue.ToString("N0");

            sb.AppendLine($"  {status} {label}:");
            sb.AppendLine($"       시뮬: {simVal}{suffix} | 인게임: {ingameVal}{suffix} | 오차: {comp.ErrorPercent:F2}%");
        }

        private void AppendComparison(StringBuilder sb, string label, ValueComparison comp)
        {
            if (comp == null)
            {
                sb.AppendLine($"  {label}: (데이터 없음)");
                return;
            }

            string status = comp.GetStatusIcon();
            string simVal = comp.FormatValue(comp.SimValue);
            string ingameVal = comp.FormatValue(comp.IngameValue);
            string error = $"{comp.ErrorPercent:F2}%";

            sb.AppendLine($"{status} {label}");
            sb.AppendLine($"   시뮬: {simVal} | 인게임: {ingameVal} | 오차: {error}");
        }

        #endregion

        #region 생명주기

        protected override void OnEnable()
        {
            base.OnEnable();

            // 기본 defender 설정
            if (defender == null)
            {
                defender = SimulatorDefender.CreateDefault1000FloorBoss();
            }
        }

        #endregion
    }
}
