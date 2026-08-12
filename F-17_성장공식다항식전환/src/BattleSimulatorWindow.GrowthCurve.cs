using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Sirenix.OdinInspector;
using Unity.EditorCoroutines.Editor;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 스테이지별 성장 곡선 데이터
    /// </summary>
    public class GrowthCurveData
    {
        public int StageLevel { get; set; }
        public int MonsterLevel { get; set; }
        public double MonsterHP { get; set; }
        public double MonsterDPS { get; set; }  // 보스 DPS (Raw)
        public double MonsterDPSAfterDefense { get; set; }  // 보스 DPS (Defense 적용 후)
        public double MonsterAverageResistance { get; set; }  // 5가지 저항 평균
        public double PlayerDPS { get; set; }
        public double PlayerHP { get; set; }    // 플레이어 HP
        public double PlayerResistance { get; set; }  // 적용된 플레이어 저항 (물리 기준)
        public double TimeToKill { get; set; }  // 처치 시간 (초)
        public double TimeToSurvive { get; set; }  // 생존 시간 (초) - Defense 적용 후
        public string KillStatus { get; set; }     // 처치 상태
        public string SurviveStatus { get; set; }  // 생존 상태
    }

    /// <summary>
    /// 공식별 파라미터 정보
    /// </summary>
    public class FormulaInfo
    {
        public string Name { get; set; }
        public string FormulaType { get; set; }
        public double Start { get; set; }
        public double Multiplier { get; set; }
        public double ExpBase { get; set; }
        public double ValueAt1 { get; set; }
        public double ValueAt100 { get; set; }
        public double ValueAt500 { get; set; }
        public double ValueAt1000 { get; set; }
    }

    /// <summary>
    /// BattleSimulatorWindow의 성장 곡선 분석 기능
    /// POLY_SECTION 전환을 위한 현재 상태 분석 도구
    /// </summary>
    public partial class BattleSimulatorWindow
    {
        #region 성장 곡선 분석 필드

        /// <summary>
        /// 성장 곡선 분석 설명 (FoldoutGroup 바로 아래, BoxGroup 밖에 배치)
        /// </summary>
        [TabGroup("Tabs", "📈 밸런싱 리포트", Order = 7)]
        [FoldoutGroup("Tabs/📈 밸런싱 리포트/📊 성장 곡선 분석", Expanded = true, Order = 100)]
        [InfoBox("플레이어 DPS vs 몬스터 HP 성장률을 분석하여 POLY_SECTION 전환 기준점을 찾습니다", InfoMessageType.None)]
        [PropertyOrder(-100)]
        [HideLabel]
        [DisplayAsString]
        [ShowInInspector]
        private string growthCurveDescription = "";

        /// <summary>
        /// 고정 샘플링 스테이지 목록 (1, 100, 200, 300, ..., 900, 1000)
        /// </summary>
        private static readonly int[] FixedSamplingStages = { 1, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };
        /// <summary>
        /// Resistance MOD list (5 types)
        /// </summary>
        private static readonly EMod[] ResistanceMods = new EMod[]
        {
            EMod.mod_physical_resistance,
            EMod.mod_fire_resistance,
            EMod.mod_cold_resistance,
            EMod.mod_lightning_resistance,
            EMod.mod_poison_resistance
        };

        /// <summary>
        /// 목표 처치 시간 (초)
        /// </summary>
        [BoxGroup("Tabs/📈 밸런싱 리포트/📊 성장 곡선 분석/Settings")]
        [LabelText("목표 처치 시간(초)")]
        [LabelWidth(100)]
        public double growthCurveTargetTime = 60.0;
        /// <summary>
        /// 현재 선택된 빌드의 DPS 사용 여부
        /// </summary>
        [BoxGroup("Tabs/📈 밸런싱 리포트/📊 성장 곡선 분석/Settings")]
        [InfoBox("미체크 시 모든 빌드 평균 DPS 사용", InfoMessageType.None)]
        [HorizontalGroup("Tabs/📈 밸런싱 리포트/📊 성장 곡선 분석/Settings/DPSOption")]
        [LabelText("현재 빌드 DPS 사용")]
        [LabelWidth(120)]
        [Tooltip("체크: 현재 선택된 빌드의 DPS 사용 / 미체크: 10개 빌드 평균 DPS 사용 (캐시가 없으면 자동으로 전체 빌드 비교 리포트 실행)")]
        public bool useCurrentBuildDPS = false;

        /// <summary>
        /// 캐시된 평균 DPS 표시
        /// </summary>
        [HorizontalGroup("Tabs/📈 밸런싱 리포트/📊 성장 곡선 분석/Settings/DPSOption")]
        [ShowInInspector]
        [LabelText("평균 DPS")]
        [LabelWidth(60)]
        [DisplayAsString]
        [PropertyOrder(1)]
        private string CachedAverageDPSDisplay => HasCachedBuildResults
            ? $"{FormatLargeNumber(GetCachedAverageDPS())} ({GetCachedBuildCount()}개 빌드)"
            : "미계산 (분석 시 자동 실행)";

        /// <summary>
        /// 성장 곡선 데이터 캐시
        /// </summary>
        private List<GrowthCurveData> cachedGrowthCurveData = new List<GrowthCurveData>();

        /// <summary>
        /// 공식 정보 캐시
        /// </summary>
        private List<FormulaInfo> cachedFormulaInfos = new List<FormulaInfo>();

        /// <summary>
        /// 평균 DPS (10개 빌드)
        /// </summary>
        private double averageBuildDPS = 0;

        /// <summary>
        /// 평균 HP (10개 빌드)
        /// </summary>
        private double averageBuildHP = 0;

        /// <summary>
        /// 평균 저항 (10개 빌드) - 5가지 속성(물리, 화염, 냉기, 번개, 독) 평균
        /// </summary>
        private double averageBuildResistance = 0;
        /// <summary>
        #endregion

        #region 성장 곡선 분석 UI 버튼

        [TabGroup("Tabs", "📈 밸런싱 리포트", Order = 7)]
        [BoxGroup("Tabs/📈 밸런싱 리포트/📊 성장 곡선 분석/Actions")]
        [InfoBox("💡 성장 곡선 분석은 현재 공식(EXP)으로 스테이지별 몬스터 HP와 플레이어 DPS를 비교합니다.\n" +
                 "이를 통해 '어느 스테이지부터 밸런스가 깨지는지' 파악하고 POLY_SECTION 전환 기준점을 찾을 수 있습니다.\n\n" +
                 "📌 출력 내용:\n" +
                 "  • 스테이지별 몬스터 HP / 플레이어 DPS / 처치 시간\n" +
                 "  • 처치 시간 > 목표 시간 시작 구간 (문제 구간)\n" +
                 "  • 현재 적용된 공식 파라미터 (StageLevel, MonsterNormalLevel 등)",
                 InfoMessageType.Info)]
        [Button(ButtonSizes.Large, Name = "📋 공식 파라미터 보기")]
        [GUIColor(0.9f, 0.7f, 0.3f)]
        private void ShowFormulaParameters()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("알림", "Play Mode에서만 실행 가능합니다.", "확인");
                return;
            }

            // 공식 정보 수집 및 리포트 출력
            CollectFormulaInfos();
            ExportFormulaParametersReport();
        }

        [BoxGroup("Tabs/📈 밸런싱 리포트/📊 성장 곡선 분석/Actions")]
        [Button(ButtonSizes.Large, Name = "📈 성장 곡선 분석")]
        [GUIColor(0.4f, 0.8f, 1f)]
        private void AnalyzeGrowthCurve()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("알림", "Play Mode에서만 실행 가능합니다.", "확인");
                return;
            }

            // 코루틴으로 실행
            EditorCoroutineUtility.StartCoroutine(AnalyzeGrowthCurveRoutine(), this);
        }

        #endregion

        #region 성장 곡선 분석 로직

        /// <summary>
        /// 성장 곡선 분석 코루틴
        /// </summary>
        private IEnumerator AnalyzeGrowthCurveRoutine()
        {
            cachedGrowthCurveData.Clear();

            // 플레이어 DPS, HP 가져오기
            double playerDPS = 0;
            double playerHP = 0;
            string dpsSource = "";

            // 플레이어 저항 - 5가지 속성(물리, 화염, 냉기, 번개, 독) 평균
            double playerResistance = 0;

            if (useCurrentBuildDPS)
            {
                // 현재 빌드의 DPS 사용
                playerDPS = ParseDisplayValue(grandTotalDpsDisplay);
                if (playerDPS <= 0)
                {
                    EditorUtility.DisplayDialog("알림", "먼저 시뮬레이션을 실행하여 DPS를 계산해주세요.", "확인");
                    yield break;
                }

                // 현재 빌드의 HP, 저항 가져오기
                UCharacterActor playerChar = GameCharacterManager.Instance?.GetPlayerCharacter();
                FBattleStatus playerBattleStatus = playerChar?.CharacterStatus?.BattleStatus;

                // 플레이어 HP 가져오기 (defender.maxLife는 몬스터 Life이므로 플레이어 Life 사용)
                playerHP = playerBattleStatus?.ResultMaxLife() ?? 0;

                if (playerBattleStatus != null)
                {
                    // TotalModValue는 이미 ratio 반환 (0.75 = 75%), maxResistance도 ratio
                    double maxResist = GameGlobalConfig.maxResistance;

                    // 5가지 저항 가져오기 (최대치 적용)
                    double physRes = System.Math.Min(playerBattleStatus.TotalModValue(EMod.mod_physical_resistance), maxResist);
                    double fireRes = System.Math.Min(playerBattleStatus.TotalModValue(EMod.mod_fire_resistance), maxResist);
                    double coldRes = System.Math.Min(playerBattleStatus.TotalModValue(EMod.mod_cold_resistance), maxResist);
                    double lightRes = System.Math.Min(playerBattleStatus.TotalModValue(EMod.mod_lightning_resistance), maxResist);
                    double poisonRes = System.Math.Min(playerBattleStatus.TotalModValue(EMod.mod_poison_resistance), maxResist);

                    // 5가지 저항 평균 계산
                    playerResistance = (physRes + fireRes + coldRes + lightRes + poisonRes) / 5.0;
                }

                dpsSource = "현재 빌드";
            }
            else
            {
                // 10개 빌드 평균 DPS, HP 사용 (캐시된 결과에서 가져옴)
                if (!HasCachedBuildResults)
                {
                    // 캐시가 없으면 자동으로 전체 빌드 비교 리포트 실행
                    Debug.Log("[GrowthCurve] 평균 DPS/HP 캐시가 없어 전체 빌드 비교 리포트를 먼저 실행합니다...");

                    // 전체 빌드 비교 리포트 실행 (silent 모드로 파일 탐색기 열기 생략)
                    yield return ExportAllBuildsComparisonReportRoutine(silent: true);

                    // 리포트 실행 후에도 캐시가 없으면 에러
                    if (!HasCachedBuildResults)
                    {
                        EditorUtility.DisplayDialog("오류",
                            "전체 빌드 비교 리포트 실행에 실패했습니다.\n\n" +
                            "빌드 프리셋이 올바르게 설정되어 있는지 확인해주세요.",
                            "확인");
                        yield break;
                    }

                    Debug.Log($"[GrowthCurve] 전체 빌드 비교 리포트 완료. 평균 DPS: {FormatLargeNumber(GetCachedAverageDPS())}, 평균 HP: {FormatLargeNumber(GetCachedAverageHP())}");
                }
                playerDPS = GetCachedAverageDPS();
                playerHP = GetCachedAverageHP();
                playerResistance = GetCachedAverageAllResistance();
                dpsSource = $"평균 ({GetCachedBuildCount()}개 빌드)";
            }

            averageBuildDPS = playerDPS;
            averageBuildHP = playerHP;
            averageBuildResistance = playerResistance;
            Debug.Log($"[GrowthCurve] 분석 시작 - 소스: {dpsSource}, DPS: {FormatLargeNumber(playerDPS)}, HP: {FormatLargeNumber(playerHP)}, 평균저항(5속성): {averageBuildResistance * 100:F1}%");

            // 진행률 표시
            int totalSamples = FixedSamplingStages.Length;
            int currentSample = 0;

            EditorUtility.DisplayProgressBar("성장 곡선 분석", "데이터 수집 중...", 0f);

            // 고정 스테이지별 데이터 수집 (1, 100, 200, 300, ..., 900, 1000)
            foreach (int stage in FixedSamplingStages)
            {
                currentSample++;
                float progress = (float)currentSample / totalSamples;
                EditorUtility.DisplayProgressBar("성장 곡선 분석", $"스테이지 {stage} 분석 중... ({currentSample}/{totalSamples})", progress);

                // 몬스터 스탯 계산
                var monsterStats = CalculateMonsterStatsForStage(stage);

                // 처치 시간 계산 (플레이어 DPS → 보스 HP)
                double timeToKill = monsterStats.HP > 0 && playerDPS > 0 ? monsterStats.HP / playerDPS : double.MaxValue;

                // 저항 적용 후 보스 DPS 계산 (물리 저항 기준)
                // 저항 배율 = 1 - 저항, 최소 25% 피해 적용 (최대 75% 저항)
                // averageBuildResistance는 이미 ratio (0.75 = 75%)
                double resistanceMultiplier = 1.0 - averageBuildResistance;
                resistanceMultiplier = System.Math.Max(0.25, resistanceMultiplier);
                double monsterDpsAfterDefense = monsterStats.Damage * resistanceMultiplier;

                // 생존 시간 계산 (Defense 적용 후 보스 DPS → 플레이어 HP)
                double timeToSurvive = playerHP > 0 && monsterDpsAfterDefense > 0 ? playerHP / monsterDpsAfterDefense : double.MaxValue;

                // 처치 상태 판정
                string killStatus = "✅ 정상";
                if (timeToKill > growthCurveTargetTime * 2)
                    killStatus = "❌ 불가능";
                else if (timeToKill > growthCurveTargetTime)
                    killStatus = "⚠️ 주의";

                // 생존 상태 판정
                string surviveStatus = "✅ 정상";
                if (timeToSurvive < growthCurveTargetTime / 2)
                    surviveStatus = "❌ 위험";
                else if (timeToSurvive < growthCurveTargetTime)
                    surviveStatus = "⚠️ 주의";

                cachedGrowthCurveData.Add(new GrowthCurveData
                {
                    StageLevel = stage,
                    MonsterLevel = monsterStats.Level,
                    MonsterHP = monsterStats.HP,
                    MonsterDPS = monsterStats.Damage,
                    MonsterDPSAfterDefense = monsterDpsAfterDefense,
                    MonsterAverageResistance = monsterStats.AverageResistance,
                    PlayerDPS = playerDPS,
                    PlayerHP = playerHP,
                    PlayerResistance = averageBuildResistance,
                    TimeToKill = timeToKill,
                    TimeToSurvive = timeToSurvive,
                    KillStatus = killStatus,
                    SurviveStatus = surviveStatus
                });

                yield return null;
            }

            EditorUtility.ClearProgressBar();

            // 공식 정보도 함께 수집
            CollectFormulaInfos();

            // 리포트 출력
            ExportGrowthCurveReport();

            EditorUtility.DisplayDialog("완료", "성장 곡선 분석 리포트가 생성되었습니다.", "확인");
        }

        /// <summary>
        /// <summary>
        /// Calculate monster stats for specific stage (4-stage calculation)
        /// Stage 1: MonsterDefaultMod (base values)
        /// Stage 2: Level data (Normal/Boss/FloorBoss Level)
        /// Stage 3: Type Mod data (Normal/Boss/FloorBoss Mod)
        /// Stage 4: MonsterType Multiplier (multiply)
        /// </summary>
        private (int Level, double HP, double Damage, double AverageResistance) CalculateMonsterStatsForStage(int stageLevel)
        {
            // Get stage data from GameDB
            if (GameDBClientManager.Instance == null || GameDBClientManager.Instance.GameDB_Stage == null)
            {
                return (stageLevel, 0, 0, 10);
            }

            // Main stage
            EStage selectedStage = EStage.stage_main;
            if (!GameDBClientManager.Instance.GameDB_Stage.Stage.MapData.TryGetValue(selectedStage, out GameDB_Client_Stage stageData))
            {
                return (stageLevel, 0, 0, 10);
            }

            // Stage level -> Monster level conversion
            int monsterLevel = stageLevel;
            if (stageData.StageLevel != null)
            {
                double multiplier = stageData.StageLevel.GetValue(1).GetValue;
                monsterLevel = (int)(stageLevel * multiplier);
            }

            // Stat dictionary for 4-stage calculation
            Dictionary<EMod, double> modValues = new Dictionary<EMod, double>();

            // ==========================================
            // Stage 1: MonsterDefaultMod (base values)
            // ==========================================
            if (GameDBClientManager.Instance.GameDB_Monster?.MonsterDefaultMod?.MapData != null)
            {
                foreach (var entry in GameDBClientManager.Instance.GameDB_Monster.MonsterDefaultMod.MapData)
                {
                    modValues[entry.Key] = entry.Value.Default.GetValue;
                }
            }

            // ==========================================
            // Stage 2 & 3: Type-specific Level and Mod data
            // ==========================================
            if (defender.monsterType == EMonsterDefenderType.FloorBoss)
            {
                // FloorBoss Level
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterFloorBossLevel?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBossLevel.MapData.TryGetValue(selectedStage, out var floorBossLevelData))
                    {
                        foreach (var modFormula in floorBossLevelData.ModValues)
                        {
                            modValues[modFormula.Mod] = modFormula.Formula.GetValue(monsterLevel).GetValue;
                        }
                    }
                }
                // FloorBoss Mod
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterFloorBossMod?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBossMod.MapData.TryGetValue(selectedStage, out var floorBossModData))
                    {
                        foreach (var entryMod in floorBossModData.ModValues)
                        {
                            modValues[entryMod.Mod] = entryMod.Value.GetValue;
                        }
                    }
                }
            }
            else if (defender.monsterType == EMonsterDefenderType.Boss)
            {
                // Boss Level
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterBossLevel?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterBossLevel.MapData.TryGetValue(selectedStage, out var bossLevelData))
                    {
                        foreach (var modFormula in bossLevelData.ModValues)
                        {
                            modValues[modFormula.Mod] = modFormula.Formula.GetValue(monsterLevel).GetValue;
                        }
                    }
                }
                // Boss Mod
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterBossMod?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterBossMod.MapData.TryGetValue(selectedStage, out var bossModData))
                    {
                        foreach (var entryMod in bossModData.ModValues)
                        {
                            modValues[entryMod.Mod] = entryMod.Value.GetValue;
                        }
                    }
                }
            }
            else // Normal monster
            {
                // Normal Level
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterNormalLevel?.MapData != null)
                {
                    foreach (var entry in GameDBClientManager.Instance.GameDB_Monster.MonsterNormalLevel.MapData)
                    {
                        modValues[entry.Key] = entry.Value.Stat.GetValue(monsterLevel).GetValue;
                    }
                }
                // Normal Mod (use melee type as default)
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterNormalMod?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterNormalMod.MapData.TryGetValue(EMonsterAttackType.melee, out var normalModData))
                    {
                        foreach (var entryMod in normalModData.ModValues)
                        {
                            modValues[entryMod.Mod] = entryMod.Value.GetValue;
                        }
                    }
                }
            }

            // ==========================================
            // Stage 4: MonsterType Multiplier
            // ==========================================
            // Convert EMonsterDefenderType to EMonsterType for multiplier lookup
            EMonsterType monsterTypeEnum = defender.monsterType switch
            {
                EMonsterDefenderType.Normal => EMonsterType.monster_type_normal,
                EMonsterDefenderType.Boss => EMonsterType.monster_type_boss,
                EMonsterDefenderType.FloorBoss => EMonsterType.monster_type_floorboss,
                _ => EMonsterType.monster_type_normal
            };

            if (GameDBClientManager.Instance.GameDB_Monster?.MonsterType?.MapData != null)
            {
                if (GameDBClientManager.Instance.GameDB_Monster.MonsterType.MapData.TryGetValue(monsterTypeEnum, out var monsterTypeData))
                {
                    if (monsterTypeData.ModMultipliers != null)
                    {
                        foreach (var multiplier in monsterTypeData.ModMultipliers)
                        {
                            if (modValues.ContainsKey(multiplier.Key))
                            {
                                modValues[multiplier.Key] *= multiplier.Value;
                            }
                        }
                    }
                }
            }

            // Extract final values
            double hp = modValues.ContainsKey(EMod.mod_life) ? modValues[EMod.mod_life] : 0;
            double damage = modValues.ContainsKey(EMod.mod_all_damage) ? modValues[EMod.mod_all_damage] : 0;

            // Calculate average of 5 resistances
            double totalResistance = 0;
            int resistCount = 0;
            foreach (var resistMod in ResistanceMods)
            {
                if (modValues.ContainsKey(resistMod))
                {
                    totalResistance += modValues[resistMod];
                    resistCount++;
                }
            }
            double averageResistance = resistCount > 0 ? totalResistance / resistCount : 10;

            return (monsterLevel, hp, damage, averageResistance);
        }



        /// <summary>
        /// 공식 파라미터 정보 수집
        /// </summary>
        private void CollectFormulaInfos()
        {
            cachedFormulaInfos.Clear();

            // 1. StageLevel 공식 (stage_main)
            if (GameDBClientManager.Instance?.GameDB_Stage?.Stage?.MapData != null)
            {
                if (GameDBClientManager.Instance.GameDB_Stage.Stage.MapData.TryGetValue(EStage.stage_main, out var stageData))
                {
                    if (stageData.StageLevel != null)
                    {
                        cachedFormulaInfos.Add(CreateFormulaInfo("Stage.StageLevel (stage_main)", stageData.StageLevel));
                    }
                    if (stageData.IncreaseInStageLevel != null)
                    {
                        cachedFormulaInfos.Add(CreateFormulaInfo("Stage.IncreaseInStageLevel (stage_main)", stageData.IncreaseInStageLevel));
                    }
                }
            }

            // 2. MonsterBossLevel 공식
            if (GameDBClientManager.Instance?.GameDB_Monster?.MonsterBossLevel?.MapData != null)
            {
                if (GameDBClientManager.Instance.GameDB_Monster.MonsterBossLevel.MapData.TryGetValue(EStage.stage_main, out var bossLevelData))
                {
                    foreach (var modFormula in bossLevelData.ModValues)
                    {
                        cachedFormulaInfos.Add(CreateFormulaInfo($"MonsterBossLevel.{modFormula.Mod}", modFormula.Formula));
                    }
                }
            }

            // 3. MonsterNormalLevel 공식
            if (GameDBClientManager.Instance?.GameDB_Monster?.MonsterNormalLevel?.MapData != null)
            {
                foreach (var kvp in GameDBClientManager.Instance.GameDB_Monster.MonsterNormalLevel.MapData)
                {
                    cachedFormulaInfos.Add(CreateFormulaInfo($"MonsterNormalLevel.{kvp.Key}", kvp.Value.Stat));
                }
            }

            // 4. 플레이어 성장 공식 (AwakenElement, PlayerGrowth, PlayerReinforce, Nebula)
            // AwakenElement
            if (GameDBClientManager.Instance?.GameDB_Awaken?.AwakenElement?.MapData != null)
            {
                var sampleKey = GameDBClientManager.Instance.GameDB_Awaken.AwakenElement.MapData.Keys.FirstOrDefault();
                if (sampleKey != default)
                {
                    var data = GameDBClientManager.Instance.GameDB_Awaken.AwakenElement.MapData[sampleKey];
                    if (data.Stat != null)
                    {
                        cachedFormulaInfos.Add(CreateFormulaInfo($"AwakenElement.Stat ({sampleKey})", data.Stat));
                    }
                }
            }

            // PlayerGrowth
            if (GameDBClientManager.Instance?.GameDB_Player?.Growth?.MapData != null)
            {
                var sampleKey = GameDBClientManager.Instance.GameDB_Player.Growth.MapData.Keys.FirstOrDefault();
                if (sampleKey != default)
                {
                    var data = GameDBClientManager.Instance.GameDB_Player.Growth.MapData[sampleKey];
                    if (data.Stat != null)
                    {
                        cachedFormulaInfos.Add(CreateFormulaInfo($"PlayerGrowth.Stat ({sampleKey})", data.Stat));
                    }
                }
            }

            // PlayerReinforce
            if (GameDBClientManager.Instance?.GameDB_Player?.Reinforce?.MapData != null)
            {
                var sampleKey = GameDBClientManager.Instance.GameDB_Player.Reinforce.MapData.Keys.FirstOrDefault();
                if (sampleKey != default)
                {
                    var data = GameDBClientManager.Instance.GameDB_Player.Reinforce.MapData[sampleKey];
                    if (data.Stat != null)
                    {
                        cachedFormulaInfos.Add(CreateFormulaInfo($"PlayerReinforce.Stat ({sampleKey})", data.Stat));
                    }
                }
            }

            // Nebula
            if (GameDBClientManager.Instance?.GameDB_Nebula?.Nebula?.MapData != null)
            {
                var sampleKey = GameDBClientManager.Instance.GameDB_Nebula.Nebula.MapData.Keys.FirstOrDefault();
                if (sampleKey != default)
                {
                    var data = GameDBClientManager.Instance.GameDB_Nebula.Nebula.MapData[sampleKey];
                    if (data.Stat != null)
                    {
                        cachedFormulaInfos.Add(CreateFormulaInfo($"Nebula.Stat ({sampleKey})", data.Stat));
                    }
                }
            }
        }

        /// <summary>
        /// FormulaData에서 FormulaInfo 생성
        /// </summary>
        private FormulaInfo CreateFormulaInfo(string name, FormulaData formula)
        {
            return new FormulaInfo
            {
                Name = name,
                FormulaType = formula.FormulaType.ToString(),
                Start = formula.Start,
                Multiplier = formula.Multiplier,
                ExpBase = formula.ExpBase,
                ValueAt1 = formula.GetValue(1).GetValue,
                ValueAt100 = formula.GetValue(100).GetValue,
                ValueAt500 = formula.GetValue(500).GetValue,
                ValueAt1000 = formula.GetValue(1000).GetValue
            };
        }

        #endregion

        #region 성장 곡선 리포트 출력

        /// <summary>
        /// 성장 곡선 분석 리포트 출력
        /// </summary>
        private void ExportGrowthCurveReport()
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            // ====================================
            // 헤더
            // ====================================
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("                         성장 곡선 분석 리포트");
            sb.AppendLine($"                         생성일시: {timestamp}");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"  분석 스테이지: {string.Join(", ", FixedSamplingStages)}");
            sb.AppendLine($"  목표 전투 시간: {growthCurveTargetTime}초");
            string sourceStr = useCurrentBuildDPS ? "현재 빌드" : $"평균 ({GetCachedBuildCount()}개 빌드)";
            sb.AppendLine($"  플레이어 DPS: {FormatLargeNumber(averageBuildDPS)} [{sourceStr}] (몬스터 방어 적용 후)");
            sb.AppendLine($"  플레이어 HP: {FormatLargeNumber(averageBuildHP)} [{sourceStr}]");
            sb.AppendLine($"  플레이어 평균 저항 (5속성): {averageBuildResistance * 100:F1}% [{sourceStr}] (생존시간 계산에 적용)");
            sb.AppendLine();

            // ====================================
            // 플레이어 기본값 정보 섹션
            // ====================================
            AppendPlayerDefaultModSection(sb);

            // ====================================
            // 몬스터 타입 및 최종 Stat Mod 정보 섹션
            // ====================================
            AppendMonsterStatModSection(sb, cachedGrowthCurveData.LastOrDefault()?.StageLevel ?? 1000);

            // ====================================
            // 섹션 1A: 플레이어 DPS vs 보스 HP (처치 시간)
            // ====================================
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("【섹션 1A: 플레이어 DPS → 보스 HP (처치 시간)】");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();

            sb.AppendLine(" 스테이지 │ 몬스터 Lv │ 보스 HP           │ 처치 시간      │ 상태");
            sb.AppendLine("──────────┼──────────┼──────────────────┼───────────────┼──────────");

            foreach (var data in cachedGrowthCurveData)
            {
                string timeStr = FormatTimeString(data.TimeToKill);
                sb.AppendLine($" {data.StageLevel,8} │ {data.MonsterLevel,8} │ {FormatLargeNumber(data.MonsterHP),-16} │ {timeStr,13} │ {data.KillStatus}");
            }

            sb.AppendLine("──────────┴──────────┴──────────────────┴───────────────┴──────────");
            sb.AppendLine();

            // ====================================
            // 섹션 1B: 보스 DPS vs 플레이어 HP (생존 시간) - Defense 적용
            // ====================================
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("【섹션 1B: 보스 DPS → 플레이어 HP (생존 시간) - 저항 적용】");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();
            sb.AppendLine($"  💡 플레이어 평균 저항 (5속성) {averageBuildResistance * 100:F1}% 적용 후 최종 받는 DPS 기준");
            sb.AppendLine();

            sb.AppendLine(" 스테이지 │ 몬스터 Lv │ Raw DPS          │ 최종 DPS        │ 생존 시간      │ 상태");
            sb.AppendLine("──────────┼──────────┼─────────────────┼────────────────┼───────────────┼──────────");

            foreach (var data in cachedGrowthCurveData)
            {
                string timeStr = FormatTimeString(data.TimeToSurvive);
                sb.AppendLine($" {data.StageLevel,8} │ {data.MonsterLevel,8} │ {FormatLargeNumber(data.MonsterDPS),-15} │ {FormatLargeNumber(data.MonsterDPSAfterDefense),-14} │ {timeStr,13} │ {data.SurviveStatus}");
            }

            sb.AppendLine("──────────┴──────────┴─────────────────┴────────────────┴───────────────┴──────────");
            sb.AppendLine();

            // ====================================
            // 섹션 2: 밸런스 분석
            // ====================================
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("【섹션 2: 밸런스 분석】");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();

            // 2A: 처치 시간 분석
            sb.AppendLine("  📌 [2A] 처치 시간 분석 (플레이어 → 보스)");
            sb.AppendLine("  ─────────────────────────────────────────");
            var killWarningStart = cachedGrowthCurveData.FirstOrDefault(d => d.TimeToKill > growthCurveTargetTime);
            var killImpossibleStart = cachedGrowthCurveData.FirstOrDefault(d => d.TimeToKill > growthCurveTargetTime * 2);

            if (killWarningStart != null)
            {
                sb.AppendLine($"  🚨 처치 시간 > {growthCurveTargetTime}초 시작 구간: 스테이지 {killWarningStart.StageLevel}");
                sb.AppendLine($"     → 이 구간부터 밸런스 조정 필요");
            }
            else
            {
                sb.AppendLine($"  ✅ 모든 구간에서 처치 시간이 {growthCurveTargetTime}초 이내입니다.");
            }

            if (killImpossibleStart != null)
            {
                sb.AppendLine($"  🚨 처치 시간 > {growthCurveTargetTime * 2}초 시작 구간: 스테이지 {killImpossibleStart.StageLevel}");
                sb.AppendLine($"     → 이 구간부터 클리어 불가능");
            }
            sb.AppendLine();

            // 2B: 생존 시간 분석
            sb.AppendLine("  📌 [2B] 생존 시간 분석 (보스 → 플레이어)");
            sb.AppendLine("  ─────────────────────────────────────────");
            var surviveWarningStart = cachedGrowthCurveData.FirstOrDefault(d => d.TimeToSurvive < growthCurveTargetTime);
            var surviveDangerStart = cachedGrowthCurveData.FirstOrDefault(d => d.TimeToSurvive < growthCurveTargetTime / 2);

            if (surviveDangerStart != null)
            {
                sb.AppendLine($"  🚨 생존 시간 < {growthCurveTargetTime / 2}초 시작 구간: 스테이지 {surviveDangerStart.StageLevel}");
                sb.AppendLine($"     → 이 구간부터 생존 불가능 (힐/회피 없이)");
            }
            else if (surviveWarningStart != null)
            {
                sb.AppendLine($"  ⚠️ 생존 시간 < {growthCurveTargetTime}초 시작 구간: 스테이지 {surviveWarningStart.StageLevel}");
                sb.AppendLine($"     → 이 구간부터 생존에 주의 필요");
            }
            else
            {
                sb.AppendLine($"  ✅ 모든 구간에서 생존 시간이 {growthCurveTargetTime}초 이상입니다.");
            }
            sb.AppendLine();

            // 성장률 비교
            if (cachedGrowthCurveData.Count >= 2)
            {
                var first = cachedGrowthCurveData.First();
                var last = cachedGrowthCurveData.Last();

                sb.AppendLine("  📌 [2C] 성장률 비교");
                sb.AppendLine("  ─────────────────────────────────────────");

                double hpGrowthRate = last.MonsterHP > 0 && first.MonsterHP > 0 ? last.MonsterHP / first.MonsterHP : 0;
                double dpsGrowthRate = last.MonsterDPS > 0 && first.MonsterDPS > 0 ? last.MonsterDPS / first.MonsterDPS : 0;

                sb.AppendLine($"  📊 보스 HP 성장률 (스테이지 {first.StageLevel} → {last.StageLevel}):");
                sb.AppendLine($"     → {FormatLargeNumber(first.MonsterHP)} → {FormatLargeNumber(last.MonsterHP)}");
                sb.AppendLine($"     → {hpGrowthRate:E2}배 증가");
                sb.AppendLine();
                sb.AppendLine($"  📊 보스 DPS 성장률 (스테이지 {first.StageLevel} → {last.StageLevel}):");
                sb.AppendLine($"     → {FormatLargeNumber(first.MonsterDPS)} → {FormatLargeNumber(last.MonsterDPS)}");
                sb.AppendLine($"     → {dpsGrowthRate:E2}배 증가");
                sb.AppendLine();
            }

            // ====================================
            // 섹션 3: 현재 적용된 공식 파라미터
            // ====================================
            AppendFormulaParametersSection(sb);

            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

            // 파일 저장
            string fileName = $"GrowthCurveAnalysis_{timestamp}.txt";
            string filePath = System.IO.Path.Combine(
                "Assets/Editor/BattleSimulator/Reports/GrowthCurve",
                fileName);

            string directory = System.IO.Path.GetDirectoryName(filePath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log($"[GrowthCurve] 리포트 저장됨: {filePath}");
            EditorUtility.RevealInFinder(filePath);
        }

        /// <summary>
        /// 공식 파라미터 섹션 추가
        /// </summary>
        private void AppendFormulaParametersSection(StringBuilder sb)
        {
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("【섹션 3: 현재 적용된 공식 파라미터】");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();

            sb.AppendLine(" 공식명                                  │ 타입        │ Start    │ Multiplier │ ExpBase │ @Lv1      │ @Lv100    │ @Lv1000");
            sb.AppendLine("─────────────────────────────────────────┼────────────┼──────────┼────────────┼─────────┼───────────┼───────────┼───────────");

            foreach (var info in cachedFormulaInfos)
            {
                string name = info.Name.Length > 40 ? info.Name.Substring(0, 37) + "..." : info.Name;
                sb.AppendLine($" {name,-40} │ {info.FormulaType,-10} │ {info.Start,8:F2} │ {info.Multiplier,10:F4} │ {info.ExpBase,7:F4} │ {FormatLargeNumber(info.ValueAt1),-9} │ {FormatLargeNumber(info.ValueAt100),-9} │ {FormatLargeNumber(info.ValueAt1000),-9}");
            }

            sb.AppendLine("─────────────────────────────────────────┴────────────┴──────────┴────────────┴─────────┴───────────┴───────────┴───────────");
            sb.AppendLine();
        }

        /// <summary>
        /// 공식 파라미터 리포트 출력 (단독)
        /// </summary>
        private void ExportFormulaParametersReport()
        {
            var sb = new StringBuilder();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("                         공식 파라미터 리포트");
            sb.AppendLine($"                         생성일시: {timestamp}");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine();

            sb.AppendLine("💡 공식 타입 설명:");
            sb.AppendLine("   CONST        : f(x) = Start");
            sb.AppendLine("   LINEAR       : f(x) = Start + (Multiplier × x)");
            sb.AppendLine("   EXP1         : f(x) = Start + (Multiplier × x × ExpBase^x)");
            sb.AppendLine("   EXP2         : f(x) = Start + (Multiplier × x) + ExpBase^x");
            sb.AppendLine("   EXP3         : f(x) = Start + (Multiplier × ExpBase^x)");
            sb.AppendLine("   POLY         : f(x) = Start + (Multiplier × x^ExpBase)");
            sb.AppendLine("   POLY_SECTION : f(x) = Start + (Multiplier × x^SectionExpBase)  // 구간별 ExpBase");
            sb.AppendLine();

            AppendFormulaParametersSection(sb);

            // 파일 저장
            string fileName = $"FormulaParameters_{timestamp}.txt";
            string filePath = System.IO.Path.Combine(
                "Assets/Editor/BattleSimulator/Reports/GrowthCurve",
                fileName);

            string directory = System.IO.Path.GetDirectoryName(filePath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log($"[GrowthCurve] 공식 파라미터 리포트 저장됨: {filePath}");
            EditorUtility.RevealInFinder(filePath);

            EditorUtility.DisplayDialog("완료", "공식 파라미터 리포트가 생성되었습니다.", "확인");
        }

        /// <summary>
        /// 시간을 보기 좋은 문자열로 변환
        /// </summary>
        private string FormatTimeString(double time)
        {
            if (time >= 1e10)
                return "∞";
            else if (time >= 86400)
                return $"{time / 86400:F1}일";
            else if (time >= 3600)
                return $"{time / 3600:F1}시간";
            else if (time >= 60)
                return $"{time / 60:F1}분";
            else
                return $"{time:F1}초";
        }

        /// <summary>
        /// 플레이어 기본값(PlayerDefaultMod) 정보 섹션 추가
        /// </summary>
        private void AppendPlayerDefaultModSection(StringBuilder sb)
        {
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("【플레이어 기본값 (PlayerDefaultMod)】");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();
            sb.AppendLine("  💡 GameDBClientManager.GameDB_Player.DefaultMod.MapData 에서 가져온 플레이어 기본 스탯입니다.");
            sb.AppendLine("     이 값들은 장비, 성장, 각성 등 어떤 콘텐츠 없이도 플레이어가 기본으로 가지는 스탯입니다.");
            sb.AppendLine();

            var defaultModMap = GameDBClientManager.Instance?.GameDB_Player?.DefaultMod?.MapData;
            if (defaultModMap == null || defaultModMap.Count == 0)
            {
                sb.AppendLine("  ⚠️ PlayerDefaultMod 데이터를 찾을 수 없습니다.");
                sb.AppendLine();
                return;
            }

            // 중요한 MOD 타입들을 먼저 표시하기 위한 정렬
            var importantMods = new[]
            {
                EMod.mod_life,
                EMod.mod_all_damage,
                EMod.mod_physical_damage,
                EMod.mod_crit_chance,
                EMod.mod_crit_multiplier,
                EMod.mod_crit_blow_chance,
                EMod.mod_crit_blow_multiplier,
                EMod.mod_physical_resistance,
                EMod.mod_fire_resistance,
                EMod.mod_cold_resistance,
                EMod.mod_lightning_resistance,
                EMod.mod_life_regen,
                EMod.mod_life_leech_all_damage
            };

            // 중요한 MOD 먼저, 나머지는 이름순으로 정렬
            var sortedMods = defaultModMap.Keys
                .OrderBy(mod =>
                {
                    int idx = Array.IndexOf(importantMods, mod);
                    return idx >= 0 ? idx : 100 + (int)mod;
                })
                .ToList();

            sb.AppendLine(" MOD 타입                          │ 기본값           │ 최소값           │ 최대값");
            sb.AppendLine("───────────────────────────────────┼─────────────────┼─────────────────┼─────────────────");

            foreach (var mod in sortedMods)
            {
                var data = defaultModMap[mod];

                // ModValue에서 값 추출
                double defaultValue = data.Default?.GetValue ?? 0;
                double minValue = data.Min?.GetValue ?? 0;
                double maxValue = data.Max?.GetValue ?? 0;

                // MOD 이름 (최대 35자)
                string modName = mod.ToString();
                if (modName.Length > 35)
                    modName = modName.Substring(0, 32) + "...";

                // 값 포맷팅 (SimulatorCalculator.FormatModValueForUI 사용)
                string defaultStr = FormatModValue(mod, defaultValue);
                string minStr = FormatModValue(mod, minValue);
                string maxStr = FormatModValue(mod, maxValue);

                sb.AppendLine($" {modName,-34} │ {defaultStr,-15} │ {minStr,-15} │ {maxStr,-15}");
            }

            sb.AppendLine("───────────────────────────────────┴─────────────────┴─────────────────┴─────────────────");
            sb.AppendLine();
            sb.AppendLine($"  📊 총 {sortedMods.Count}개의 기본 MOD 정의됨");
            sb.AppendLine();
        }

        /// <summary>
        /// 몬스터 타입 및 최종 Stat Mod 정보 섹션 추가
        /// </summary>
        private void AppendMonsterStatModSection(StringBuilder sb, int stageLevel)
        {
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"【몬스터 타입 및 최종 Stat Mod (스테이지 {stageLevel})】");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();

            // 현재 선택된 몬스터 타입
            string monsterTypeStr = defender.monsterType switch
            {
                EMonsterDefenderType.Normal => "Normal (일반 몬스터)",
                EMonsterDefenderType.Boss => "Boss (보스)",
                EMonsterDefenderType.FloorBoss => "FloorBoss (층 보스)",
                _ => defender.monsterType.ToString()
            };

            sb.AppendLine($"  📌 현재 몬스터 타입: {monsterTypeStr}");
            sb.AppendLine();

            // 몬스터 스탯 계산 (4단계 계산 과정 상세 출력)
            sb.AppendLine($"  💡 몬스터 스탯 계산은 4단계로 진행됩니다:");
            sb.AppendLine($"     1단계: MonsterDefaultMod (기본값)");
            sb.AppendLine($"     2단계: Level 데이터 (Normal/Boss/FloorBoss Level 공식)");
            sb.AppendLine($"     3단계: Type Mod 데이터 (Normal/Boss/FloorBoss Mod 고정값)");
            sb.AppendLine($"     4단계: MonsterType 배율 (타입별 곱연산)");
            sb.AppendLine();

            // 스테이지 데이터 가져오기
            if (GameDBClientManager.Instance == null || GameDBClientManager.Instance.GameDB_Stage == null)
            {
                sb.AppendLine("  ⚠️ GameDB 데이터를 찾을 수 없습니다.");
                sb.AppendLine();
                return;
            }

            EStage selectedStage = EStage.stage_main;
            if (!GameDBClientManager.Instance.GameDB_Stage.Stage.MapData.TryGetValue(selectedStage, out GameDB_Client_Stage stageData))
            {
                sb.AppendLine("  ⚠️ 스테이지 데이터를 찾을 수 없습니다.");
                sb.AppendLine();
                return;
            }

            // 몬스터 레벨 계산
            int monsterLevel = stageLevel;
            if (stageData.StageLevel != null)
            {
                double multiplier = stageData.StageLevel.GetValue(1).GetValue;
                monsterLevel = (int)(stageLevel * multiplier);
            }

            sb.AppendLine($"  📊 스테이지 레벨: {stageLevel} → 몬스터 레벨: {monsterLevel}");
            sb.AppendLine();

            // 최종 modValues 계산
            Dictionary<EMod, double> modValues = new Dictionary<EMod, double>();

            // 1단계: MonsterDefaultMod
            if (GameDBClientManager.Instance.GameDB_Monster?.MonsterDefaultMod?.MapData != null)
            {
                foreach (var entry in GameDBClientManager.Instance.GameDB_Monster.MonsterDefaultMod.MapData)
                {
                    modValues[entry.Key] = entry.Value.Default.GetValue;
                }
            }

            // 2단계 & 3단계: 타입별 Level 및 Mod 데이터
            if (defender.monsterType == EMonsterDefenderType.FloorBoss)
            {
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterFloorBossLevel?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBossLevel.MapData.TryGetValue(selectedStage, out var floorBossLevelData))
                    {
                        foreach (var modFormula in floorBossLevelData.ModValues)
                        {
                            modValues[modFormula.Mod] = modFormula.Formula.GetValue(monsterLevel).GetValue;
                        }
                    }
                }
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterFloorBossMod?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBossMod.MapData.TryGetValue(selectedStage, out var floorBossModData))
                    {
                        foreach (var entryMod in floorBossModData.ModValues)
                        {
                            modValues[entryMod.Mod] = entryMod.Value.GetValue;
                        }
                    }
                }
            }
            else if (defender.monsterType == EMonsterDefenderType.Boss)
            {
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterBossLevel?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterBossLevel.MapData.TryGetValue(selectedStage, out var bossLevelData))
                    {
                        foreach (var modFormula in bossLevelData.ModValues)
                        {
                            modValues[modFormula.Mod] = modFormula.Formula.GetValue(monsterLevel).GetValue;
                        }
                    }
                }
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterBossMod?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterBossMod.MapData.TryGetValue(selectedStage, out var bossModData))
                    {
                        foreach (var entryMod in bossModData.ModValues)
                        {
                            modValues[entryMod.Mod] = entryMod.Value.GetValue;
                        }
                    }
                }
            }
            else // Normal
            {
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterNormalLevel?.MapData != null)
                {
                    foreach (var entry in GameDBClientManager.Instance.GameDB_Monster.MonsterNormalLevel.MapData)
                    {
                        modValues[entry.Key] = entry.Value.Stat.GetValue(monsterLevel).GetValue;
                    }
                }
                if (GameDBClientManager.Instance.GameDB_Monster?.MonsterNormalMod?.MapData != null)
                {
                    if (GameDBClientManager.Instance.GameDB_Monster.MonsterNormalMod.MapData.TryGetValue(EMonsterAttackType.melee, out var normalModData))
                    {
                        foreach (var entryMod in normalModData.ModValues)
                        {
                            modValues[entryMod.Mod] = entryMod.Value.GetValue;
                        }
                    }
                }
            }

            // 4단계: MonsterType 배율
            EMonsterType monsterTypeEnum = defender.monsterType switch
            {
                EMonsterDefenderType.Normal => EMonsterType.monster_type_normal,
                EMonsterDefenderType.Boss => EMonsterType.monster_type_boss,
                EMonsterDefenderType.FloorBoss => EMonsterType.monster_type_floorboss,
                _ => EMonsterType.monster_type_normal
            };

            Dictionary<EMod, double> typeMultipliers = new Dictionary<EMod, double>();
            if (GameDBClientManager.Instance.GameDB_Monster?.MonsterType?.MapData != null)
            {
                if (GameDBClientManager.Instance.GameDB_Monster.MonsterType.MapData.TryGetValue(monsterTypeEnum, out var monsterTypeData))
                {
                    if (monsterTypeData.ModMultipliers != null)
                    {
                        foreach (var multiplier in monsterTypeData.ModMultipliers)
                        {
                            typeMultipliers[multiplier.Key] = multiplier.Value;
                            if (modValues.ContainsKey(multiplier.Key))
                            {
                                modValues[multiplier.Key] *= multiplier.Value;
                            }
                        }
                    }
                }
            }

            // 최종 Stat Mod 출력
            sb.AppendLine("  ─────────────────────────────────────────────────────────────────────────");
            sb.AppendLine("  📋 최종 몬스터 Stat Mod 값 (4단계 계산 완료 후)");
            sb.AppendLine("  ─────────────────────────────────────────────────────────────────────────");
            sb.AppendLine();

            // 중요한 MOD 먼저 표시
            var importantMods = new[]
            {
                EMod.mod_life,
                EMod.mod_all_damage,
                EMod.mod_physical_resistance,
                EMod.mod_fire_resistance,
                EMod.mod_cold_resistance,
                EMod.mod_lightning_resistance,
                EMod.mod_poison_resistance
            };

            var sortedMods = modValues.Keys
                .OrderBy(mod =>
                {
                    int idx = Array.IndexOf(importantMods, mod);
                    return idx >= 0 ? idx : 100 + (int)mod;
                })
                .ToList();

            sb.AppendLine(" MOD 타입                          │ 최종값             │ 타입 배율");
            sb.AppendLine("───────────────────────────────────┼───────────────────┼────────────");

            foreach (var mod in sortedMods)
            {
                double finalValue = modValues[mod];
                string modName = mod.ToString();
                if (modName.Length > 35)
                    modName = modName.Substring(0, 32) + "...";

                // 값 포맷팅 (SimulatorCalculator.FormatModValueForUI 사용)
                string valueStr = FormatModValue(mod, finalValue);

                // 타입 배율 표시
                string multiplierStr = typeMultipliers.ContainsKey(mod) ? $"×{typeMultipliers[mod]:F2}" : "-";

                sb.AppendLine($" {modName,-34} │ {valueStr,-17} │ {multiplierStr,-10}");
            }

            sb.AppendLine("───────────────────────────────────┴───────────────────┴────────────");
            sb.AppendLine();
            sb.AppendLine($"  📊 총 {sortedMods.Count}개의 MOD 적용됨");
            sb.AppendLine();
        }

        /// <summary>
        /// MOD 값 포맷팅 - SimulatorCalculator.FormatModValueForUI 사용
        /// FLOAT_PER 타입인 경우 백분율(%)로 표시, 그 외는 큰 숫자 포맷 사용
        /// </summary>
        private string FormatModValue(EMod mod, double value)
        {
            if (value == 0)
                return "0";

            // SimulatorCalculator의 IsFloatPerMod를 사용하여 백분율 타입 여부 확인
            if (SimulatorCalculator.IsFloatPerMod(mod))
            {
                // FormatModValueForUI: FLOAT_PER 타입이면 *100하여 백분율로 변환
                return SimulatorCalculator.FormatModValueForUI(mod, value, "F1", "%");
            }
            else
            {
                return FormatLargeNumber(value);
            }
        }

        #endregion

        #region 리포트 정리 기능

        /// <summary>
        /// 리포트 정리 버튼
        /// Reports 폴더 내 각 서브폴더에서 가장 최신 파일만 남기고 나머지를 삭제합니다.
        /// </summary>
        [TabGroup("Tabs", "📈 밸런싱 리포트", Order = 7)]
        [FoldoutGroup("Tabs/📈 밸런싱 리포트/🗑️ 리포트 정리", Expanded = true, Order = 999)]
        [InfoBox("📌 리포트 정리 기능\n\n" +
                 "Reports 폴더 내 각 서브폴더에서 가장 최신 날짜의 파일만 남기고 나머지를 삭제합니다.\n\n" +
                 "대상 폴더 (폴더당 최신 1개 유지):\n" +
                 "  • AllBuildsComparison/\n" +
                 "  • BalanceReport/\n" +
                 "  • Constellation/\n" +
                 "  • DamageVerification/\n" +
                 "  • EquipmentCommonMod/\n" +
                 "  • EquipmentPossessionMod/\n" +
                 "  • Formula/\n" +
                 "  • GrowthCurve/\n" +
                 "  • ModPercentage/\n" +
                 "  • SkillSpec/\n\n" +
                 "대상 폴더 (빌드별 최신 1개 유지):\n" +
                 "  • IndividualBuildReports/ (빌드명_날짜.txt 형식)\n\n" +
                 "⚠️ 삭제된 파일은 복구할 수 없으니 주의하세요!", InfoMessageType.Warning)]
        [Button(ButtonSizes.Large, Name = "🗑️ 리포트 정리 (최신 파일만 유지)")]
        [GUIColor(1f, 0.5f, 0.5f)]
        private void CleanupReports()
        {
            string reportsPath = "Assets/Editor/BattleSimulator/Reports";

            if (!System.IO.Directory.Exists(reportsPath))
            {
                EditorUtility.DisplayDialog("오류", "Reports 폴더를 찾을 수 없습니다.", "확인");
                return;
            }

            int totalDeleted = 0;
            int totalKept = 0;
            var deletedDetails = new StringBuilder();

            // 대상 서브폴더 목록
            string[] subFolders = new string[]
            {
                "AllBuildsComparison",
                "BalanceReport",
                "Constellation",
                "DamageVerification",
                "EquipmentCommonMod",
                "EquipmentPossessionMod",
                "Formula",
                "GrowthCurve",
                "ModPercentage",
                "SimVsInGameDps",
                "SkillSpec"
            };

            foreach (string subFolder in subFolders)
            {
                string folderPath = System.IO.Path.Combine(reportsPath, subFolder);

                if (!System.IO.Directory.Exists(folderPath))
                    continue;

                // .txt 파일만 대상으로 (meta 파일 제외)
                var files = System.IO.Directory.GetFiles(folderPath, "*.txt")
                    .Where(f => !f.EndsWith(".meta"))
                    .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
                    .ToList();

                if (files.Count <= 1)
                {
                    // 파일이 1개 이하면 삭제할 것이 없음
                    if (files.Count == 1)
                        totalKept++;
                    continue;
                }

                // 가장 최신 파일 유지
                string newestFile = files[0];
                totalKept++;

                // 나머지 파일 삭제
                for (int i = 1; i < files.Count; i++)
                {
                    string fileToDelete = files[i];
                    string metaFile = fileToDelete + ".meta";

                    try
                    {
                        // txt 파일 삭제
                        System.IO.File.Delete(fileToDelete);
                        totalDeleted++;

                        // meta 파일도 함께 삭제
                        if (System.IO.File.Exists(metaFile))
                        {
                            System.IO.File.Delete(metaFile);
                        }

                        string fileName = System.IO.Path.GetFileName(fileToDelete);
                        deletedDetails.AppendLine($"  • {subFolder}/{fileName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ReportCleanup] 파일 삭제 실패: {fileToDelete}\n{ex.Message}");
                    }
                }
            }

            // IndividualBuildReports 폴더 특별 처리 (빌드별로 최신 파일만 유지)
            string individualReportsPath = System.IO.Path.Combine(reportsPath, "IndividualBuildReports");
            if (System.IO.Directory.Exists(individualReportsPath))
            {
                var allFiles = System.IO.Directory.GetFiles(individualReportsPath, "*.txt")
                    .Where(f => !f.EndsWith(".meta"))
                    .ToList();

                // 파일명에서 빌드명 추출하여 그룹화 (빌드명_YYYY-MM-DD_HH-MM-SS.txt 형식)
                var buildGroups = new Dictionary<string, List<string>>();
                var buildNamePattern = new System.Text.RegularExpressions.Regex(@"^(.+)_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.txt$");

                foreach (string file in allFiles)
                {
                    string fileName = System.IO.Path.GetFileName(file);
                    var match = buildNamePattern.Match(fileName);

                    if (match.Success)
                    {
                        string buildName = match.Groups[1].Value;
                        if (!buildGroups.ContainsKey(buildName))
                            buildGroups[buildName] = new List<string>();
                        buildGroups[buildName].Add(file);
                    }
                }

                // 각 빌드 그룹에서 최신 파일만 유지
                foreach (var kvp in buildGroups)
                {
                    var filesInGroup = kvp.Value
                        .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
                        .ToList();

                    if (filesInGroup.Count <= 1)
                    {
                        if (filesInGroup.Count == 1)
                            totalKept++;
                        continue;
                    }

                    // 최신 파일 유지
                    totalKept++;

                    // 나머지 파일 삭제
                    for (int i = 1; i < filesInGroup.Count; i++)
                    {
                        string fileToDelete = filesInGroup[i];
                        string metaFile = fileToDelete + ".meta";

                        try
                        {
                            System.IO.File.Delete(fileToDelete);
                            totalDeleted++;

                            if (System.IO.File.Exists(metaFile))
                                System.IO.File.Delete(metaFile);

                            string fileName = System.IO.Path.GetFileName(fileToDelete);
                            deletedDetails.AppendLine($"  • IndividualBuildReports/{fileName}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[ReportCleanup] 파일 삭제 실패: {fileToDelete}\n{ex.Message}");
                        }
                    }
                }
            }

            // AssetDatabase 갱신
            AssetDatabase.Refresh();

            // 결과 표시
            string message = $"리포트 정리 완료!\n\n" +
                           $"• 유지된 파일: {totalKept}개\n" +
                           $"• 삭제된 파일: {totalDeleted}개\n";

            if (totalDeleted > 0)
            {
                message += $"\n삭제된 파일 목록:\n{deletedDetails}";
            }

            EditorUtility.DisplayDialog("리포트 정리 완료", message, "확인");
            Debug.Log($"[ReportCleanup] {message}");
        }

        #endregion
    }
}
