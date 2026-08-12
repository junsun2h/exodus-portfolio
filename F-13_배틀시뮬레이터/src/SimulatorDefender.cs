using PX;
using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace BattleSimulator
{
    /// <summary>
    /// 시뮬레이터용 가상 적 캐릭터 데이터
    /// Phase 7: GameDBClient_Monster 기반 정확한 Defender 스펙 계산
    /// </summary>
    [Serializable]
    public class SimulatorDefender
    {
        // ========================================
        // 몬스터 타입 선택
        // ========================================
        [PropertyOrder(1)]
        [LabelText("몬스터 타입")]
        [OnValueChanged("OnMonsterTypeChanged")]
        public EMonsterDefenderType monsterType = EMonsterDefenderType.FloorBoss;

        [PropertyOrder(2)]
        [LabelText("스테이지 (Stage)")]
        [OnValueChanged("OnStageChanged")]
        public EStage selectedStage = EStage.stage_main;

        [PropertyOrder(3)]
        [LabelText("스테이지 레벨")]
        [Range(1, 1000)]
        [OnValueChanged("OnStageLevelChanged")]
        public int stageLevel = 1000;

        [PropertyOrder(4)]
        [ReadOnly, LabelText("스테이지 레벨 계수 (level_factor)")]
        [DisplayAsString]
        public string stageLevelFactorDisplay = "5.0";

        [PropertyOrder(5)]
        [ReadOnly, LabelText("최종 스테이지 레벨 (level * factor)")]
        [DisplayAsString]
        public string finalStageLevelDisplay = "5,000";

        // ========================================
        // Normal 몬스터 설정
        // ========================================
        [PropertyOrder(10)]
        [FoldoutGroup("Normal Monster Settings", true)]
        [LabelText("Normal Monster")]
        [ShowIf("@monsterType == EMonsterDefenderType.Normal")]
        [OnValueChanged("OnMonsterSelectionChanged")]
        public EMonsterNormal selectedNormalMonster = EMonsterNormal.monster_normal_magic_evilmage;

        // ========================================
        // Boss 몬스터 설정
        // ========================================
        [PropertyOrder(20)]
        [FoldoutGroup("Boss Monster Settings", true)]
        [LabelText("Boss Monster")]
        [ShowIf("@monsterType == EMonsterDefenderType.Boss")]
        [OnValueChanged("OnMonsterSelectionChanged")]
        public EMonsterBoss selectedBossMonster = EMonsterBoss.monster_normalboss_magic_golemfire;

        // ========================================
        // FloorBoss 몬스터 설정
        // ========================================
        [PropertyOrder(25)]
        [FoldoutGroup("FloorBoss Monster Settings", true)]
        [LabelText("FloorBoss Monster")]
        [ShowIf("@monsterType == EMonsterDefenderType.FloorBoss")]
        [OnValueChanged("OnMonsterSelectionChanged")]
        public EMonsterFloorBoss selectedFloorBossMonster = EMonsterFloorBoss.monster_floorboss_1000;

        // ========================================
        // 계산된 Defender 스펙 (ReadOnly)
        // ========================================
        [PropertyOrder(30)]
        [FoldoutGroup("Calculated Stats", true)]
        [ReadOnly, LabelText("이름")]
        public string defenderName = "Boss Monster (Lv 1000)";

        [FoldoutGroup("Calculated Stats")]
        [ReadOnly, LabelText("공격 타입")]
        public EMonsterAttackType attackType = EMonsterAttackType.melee;

        [FoldoutGroup("Calculated Stats")]
        [ReadOnly, LabelText("공격 속성")]
        [DisplayAsString]
        public string attackElementDisplay = "Unknown";

        [FoldoutGroup("Calculated Stats")]
        [ReadOnly, LabelText("사용 스킬")]
        [DisplayAsString]
        public string attackSkillDisplay = "-";

        // 방어 스탯
        [FoldoutGroup("Calculated Stats")]
        [ReadOnly, LabelText("물리 저항 (%)")]
        [DisplayAsString]
        public string physicalResistanceDisplay = "0%";

        [FoldoutGroup("Calculated Stats")]
        [ReadOnly, LabelText("화염/냉기/번개/독 저항 (%)")]
        [DisplayAsString]
        public string resistancesDisplay = "0% / 0% / 0% / 0%";

        // 회피/막기
        [FoldoutGroup("Calculated Stats")]
        [ReadOnly, LabelText("회피 / 막기 확률 (%)")]
        [DisplayAsString]
        public string evadeBlockDisplay = "0% / 0%";

        // HP
        [FoldoutGroup("Calculated Stats")]
        [ReadOnly, LabelText("최대 생명력")]
        [DisplayAsString]
        public string maxLifeDisplay = "0";

        // 내부 계산값 (실제 사용)
        [HideInInspector]
        public double stageLevelFactor; // 스테이지 레벨 계수 (stage multiplier, e.g., 5.0 for Invasion)

        [HideInInspector]
        public int finalStageLevel; // 최종 스테이지 레벨 (stage_level * level_factor)

        [HideInInspector]
        public double physicalResistance;
        [HideInInspector]
        public double fireResistance;
        [HideInInspector]
        public double coldResistance;
        [HideInInspector]
        public double lightningResistance;
        [HideInInspector]
        public double poisonResistance;
        [HideInInspector]
        public double evadeChance;
        [HideInInspector]
        public double blockChance;
        [HideInInspector]
        public double maxLife;

        // 저항 최대치 (CalculateResistanceMax()에서 GameGlobalConfig.maxResistance 기반으로 계산됨)
        [HideInInspector]
        public double physicalResistanceMax;
        [HideInInspector]
        public double fireResistanceMax;
        [HideInInspector]
        public double coldResistanceMax;
        [HideInInspector]
        public double lightningResistanceMax;
        [HideInInspector]
        public double poisonResistanceMax;

        // MonsterType Multipliers 정보
        [HideInInspector]
        public Dictionary<EMod, float> monsterTypeMultipliers = new();

        // 공격 속성 정보
        [HideInInspector]
        public EDamageType attackElement = EDamageType.physical;
        [HideInInspector]
        public ESkill attackSkill = ESkill.None;

        // 내부 MOD Dictionary
        [HideInInspector]
        public Dictionary<EMod, double> calculatedMods = new Dictionary<EMod, double>();

        // MOD 계산 과정 추적 (각 MOD별 소스 추적)
        [HideInInspector]
        public Dictionary<EMod, List<(string source, double value)>> modCalculationSteps = new Dictionary<EMod, List<(string source, double value)>>();

        // ========================================
        // 조건부 MOD: 적 상태이상 상태
        // ========================================
        [HideInInspector]
        public bool targetHasBleeding = false;
        [HideInInspector]
        public bool targetHasIgnite = false;
        [HideInInspector]
        public bool targetHasArctic = false;
        [HideInInspector]
        public bool targetHasChill = false;
        [HideInInspector]
        public bool targetHasShock = false;
        [HideInInspector]
        public bool targetHasParalyze = false;
        [HideInInspector]
        public bool targetHasPoisoning = false;
        [HideInInspector]
        public bool targetHasStun = false;

        /// <summary>
        /// 몬스터 타입 변경 콜백
        /// </summary>
        private void OnMonsterTypeChanged()
        {
            CalculateDefenderStats();
        }

        /// <summary>
        /// 스테이지 변경 콜백
        /// </summary>
        private void OnStageChanged()
        {
            CalculateDefenderStats();
        }

        /// <summary>
        /// 스테이지 레벨 변경 콜백
        /// </summary>
        private void OnStageLevelChanged()
        {
            CalculateDefenderStats();
        }

        /// <summary>
        /// 몬스터 선택 변경 콜백
        /// </summary>
        private void OnMonsterSelectionChanged()
        {
            CalculateDefenderStats();
        }

        /// <summary>
        /// Defender 스펙 계산 (실제 게임 로직 사용)
        /// </summary>
        public void CalculateDefenderStats()
        {
            // GameDB 초기화 확인 (에디터 모드 대응)
            if (!UnityEngine.Application.isPlaying)
            {
                Debug.LogWarning("[SimulatorDefender] GameDB는 Play Mode에서만 사용할 수 있습니다. 기본값으로 설정합니다.");
                SetDefaultStats();
                return;
            }

            if (GameDBClientManager.Instance == null || GameDBClientManager.Instance.GameDB_Monster == null)
            {
                Debug.LogWarning("[SimulatorDefender] GameDBClientManager가 초기화되지 않았습니다. 기본값으로 설정합니다.");
                SetDefaultStats();
                return;
            }

            calculatedMods.Clear();
            modCalculationSteps.Clear();

            // 스테이지 레벨 계수 계산
            CalculateStageLevelFactor();

            // 실제 게임 로직을 사용하여 FBattleStatus 생성
            FBattleStatus battleStatus = CreateBattleStatusWithGameLogic();

            if (battleStatus == null)
            {
                Debug.LogWarning("[SimulatorDefender] FBattleStatus 생성 실패, 기본값 사용");
                SetDefaultStats();
                return;
            }

            // 계산된 MOD 값 추출
            ExtractModsFromBattleStatus(battleStatus);

            // 주요 스탯 추출
            ExtractMainStats();
        }

        /// <summary>
        /// 스테이지 레벨 계수 계산
        /// level_factor = stage multiplier (e.g., 5.0 for Invasion)
        /// final_stage_level = stage_level * level_factor
        /// </summary>
        private void CalculateStageLevelFactor()
        {
            // GameDB_Stage 접근
            if (GameDBClientManager.Instance == null || GameDBClientManager.Instance.GameDB_Stage == null)
            {
                Debug.LogWarning("[SimulatorDefender] GameDB_Stage가 초기화되지 않았습니다.");
                stageLevelFactor = 1.0; // 기본값: 1배수
                finalStageLevel = stageLevel;
                stageLevelFactorDisplay = $"{stageLevelFactor:F1}";
                finalStageLevelDisplay = $"{finalStageLevel:N0}";
                return;
            }

            var stageDB = GameDBClientManager.Instance.GameDB_Stage;

            // 선택된 스테이지의 데이터 가져오기
            if (!stageDB.Stage.MapData.TryGetValue(selectedStage, out GameDB_Client_Stage stageData))
            {
                Debug.LogWarning($"[SimulatorDefender] Stage data not found for {selectedStage}");
                stageLevelFactor = 1.0; // 기본값: 1배수
                finalStageLevel = stageLevel;
                stageLevelFactorDisplay = $"{stageLevelFactor:F1}";
                finalStageLevelDisplay = $"{finalStageLevel:N0}";
                return;
            }

            // StageLevel Formula를 사용하여 level_factor (배수) 계산
            // Formula: stage_level * multiplier
            // GetValue(1)을 호출하면 multiplier 값만 얻을 수 있음
            if (stageData.StageLevel != null)
            {
                var multiplierValue = stageData.StageLevel.GetValue(1); // 1레벨의 결과 = multiplier
                stageLevelFactor = multiplierValue.GetValue;
                finalStageLevel = (int)(stageLevel * stageLevelFactor);
                stageLevelFactorDisplay = $"{stageLevelFactor:F1}";
                finalStageLevelDisplay = $"{finalStageLevel:N0}";
            }
            else
            {
                // StageLevel Formula가 없으면 기본값
                stageLevelFactor = 1.0;
                finalStageLevel = stageLevel;
                stageLevelFactorDisplay = $"{stageLevelFactor:F1}";
                finalStageLevelDisplay = $"{finalStageLevel:N0}";
            }
        }

        /// <summary>
        /// 기본값 설정 (GameDB 없이 사용 가능)
        /// </summary>
        private void SetDefaultStats()
        {
            defenderName = monsterType switch
            {
                EMonsterDefenderType.Boss => $"Boss Monster (Lv {stageLevel})",
                EMonsterDefenderType.FloorBoss => $"FloorBoss Monster (Lv {stageLevel})",
                _ => $"Normal Monster (Lv {stageLevel})"
            };

            // 스테이지 레벨 계수 기본값
            stageLevelFactor = 1.0;
            finalStageLevel = stageLevel;
            stageLevelFactorDisplay = $"{stageLevelFactor:F1}";
            finalStageLevelDisplay = $"{finalStageLevel:N0}";

            // 기본 스탯 설정 (비율 값: 0.1 = 10%)
            physicalResistance = 0.1;  // 기본 물리 저항 10%
            fireResistance = 0;
            coldResistance = 0;
            lightningResistance = 0;
            poisonResistance = 0;
            evadeChance = 0;
            blockChance = 0;
            maxLife = 100000;

            // Display 업데이트 (비율 → 백분율 변환)
            physicalResistanceDisplay = $"{physicalResistance * 100:F3}%";
            resistancesDisplay = $"{fireResistance * 100:F3}% / {coldResistance * 100:F3}% / {lightningResistance * 100:F3}% / {poisonResistance * 100:F3}%";
            evadeBlockDisplay = $"{evadeChance * 100:F3}% / {blockChance * 100:F3}%";
            maxLifeDisplay = $"{BattleSimulatorWindow.FormatNumber(maxLife)}";
        }

        /// <summary>
        /// 실제 게임 로직을 사용하여 FBattleStatus 생성
        /// </summary>
        private FBattleStatus CreateBattleStatusWithGameLogic()
        {
            FCharacterSingleData monsterData;
            var monsterDB = GameDBClientManager.Instance.GameDB_Monster;

            if (monsterType == EMonsterDefenderType.Normal)
            {
                // Normal 몬스터 데이터 가져오기
                if (!monsterDB.MonsterNormal.MapData.TryGetValue(selectedNormalMonster, out GameDB_Client_MonsterNormal normalData))
                {
                    Debug.LogError($"[SimulatorDefender] MonsterNormal data not found for {selectedNormalMonster}");
                    return null;
                }

                defenderName = $"{normalData.StringKeyName} (Lv {stageLevel})";
                attackType = normalData.AttackType;

                // 공격 속성 추출 (SkillList → Spell → DamageType)
                ExtractAttackElement(normalData.SkillList);

                // 최종 스테이지 레벨을 사용하여 FMonsterNormalSingleData 생성
                monsterData = new FMonsterNormalSingleData(
                    inLevel: finalStageLevel,
                    inTeamNo: 1,
                    inSpawnFieldID: 0,
                    inMonsterGroup: 0,
                    InNormalType: selectedNormalMonster,
                    InMonsterNormalData: normalData
                );
            }
            else if (monsterType == EMonsterDefenderType.FloorBoss)
            {
                // None이면 기본값 사용
                var floorBossType = selectedFloorBossMonster;
                if (floorBossType == EMonsterFloorBoss.None)
                {
                    floorBossType = EMonsterFloorBoss.monster_floorboss_1000;
                }

                // FloorBoss 몬스터 데이터 가져오기
                if (!monsterDB.MonsterFloorBoss.MapData.TryGetValue(floorBossType, out GameDB_Client_MonsterFloorBoss floorBossData))
                {
                    Debug.LogError($"[SimulatorDefender] MonsterFloorBoss data not found for {floorBossType}");
                    return null;
                }

                defenderName = $"{floorBossData.StringKeyName} (Lv {stageLevel})";
                attackType = floorBossData.AttackType;

                // 공격 속성 추출 (SkillList → Spell → DamageType)
                ExtractAttackElement(floorBossData.SkillList);

                // 최종 스테이지 레벨을 사용하여 FMonsterFloorBossSingleData 생성
                monsterData = new FMonsterFloorBossSingleData(
                    inLevel: finalStageLevel,
                    inTeamNo: 1,
                    inSpawnFieldID: 0,
                    inMonsterGroup: 0,
                    InFloorBossType: floorBossType,
                    InMonsterFloorBossData: floorBossData
                );
            }
            else // Boss
            {
                // Boss 몬스터 데이터 가져오기
                if (!monsterDB.MonsterBoss.MapData.TryGetValue(selectedBossMonster, out GameDB_Client_MonsterBoss bossData))
                {
                    Debug.LogError($"[SimulatorDefender] MonsterBoss data not found for {selectedBossMonster}");
                    return null;
                }

                defenderName = $"{bossData.StringKeyName} (Lv {stageLevel})";
                attackType = bossData.AttackType;

                // 공격 속성 추출 (SkillList → Spell → DamageType)
                ExtractAttackElement(bossData.SkillList);

                // 최종 스테이지 레벨을 사용하여 FMonsterBossSingleData 생성
                monsterData = new FMonsterBossSingleData(
                    inLevel: finalStageLevel,
                    inTeamNo: 1,
                    inSpawnFieldID: 0,
                    inMonsterGroup: 0,
                    InBossType: selectedBossMonster,
                    InMonsterBossData: bossData
                );
            }

            // FBattleStatus 생성
            FBattleStatus battleStatus = new FBattleStatus(monsterData);

            // FBattleStatusMonsterData에 추적 활성화 및 스테이지 정보 전달
            if (battleStatus.characterData is FBattleStatusMonsterData monsterStatusData)
            {
                monsterStatusData.enableCalculationTracking = true;
                monsterStatusData.calculationSteps = new Dictionary<EMod, List<(string, double)>>();

                // 시뮬레이터용: 직접 MOD 계산 수행
                // UpdateStatus는 GameBattleControlManager.Instance.CurrentStageType을 사용하므로
                // 시뮬레이터 환경에서는 selectedStage를 직접 전달하여 계산
                UpdateMonsterStatsDirectly(monsterStatusData, monsterData, selectedStage);
            }
            else
            {
                Debug.LogWarning($"[SimulatorDefender] battleStatus.characterData가 FBattleStatusMonsterData가 아닙니다. Type: {battleStatus.characterData?.GetType().Name}");
            }

            return battleStatus;
        }

        /// <summary>
        /// 시뮬레이터용: 스테이지 정보를 직접 전달하여 몬스터 스탯 업데이트
        /// BattleStatusMonsterData.UpdateStatus()의 로직을 복사하되 selectedStage 사용
        /// </summary>
        private void UpdateMonsterStatsDirectly(FBattleStatusMonsterData monsterStatusData, FCharacterSingleData characterData, EStage targetStage)
        {
            // base.UpdateStatus() 대신 직접 Clear 호출
            // monsterStatusData.ClearData()는 protected이므로 직접 호출 불가

            FMonsterSingleData monsterData = (FMonsterSingleData)characterData;
            EMonsterType monsterTypeEnum = monsterData.MonsterType;
            int level = monsterData.Level;

            // 계산 추적 초기화
            if (monsterStatusData.enableCalculationTracking && monsterStatusData.calculationSteps != null)
            {
                monsterStatusData.calculationSteps.Clear();
            }

            if (monsterTypeEnum == EMonsterType.monster_type_boss)
            {
                // selectedStage 사용 (GameBattleControlManager 대신)
                EStage currentStage = targetStage;

                // 기본 능력치 (GetValue로 변환된 값 사용 - FLOAT_PER는 비율로 변환)
                foreach (KeyValuePair<EMod, GameDB_Client_MonsterDefaultMod> entry in GameDBClientManager.Instance.GameDB_Monster.MonsterDefaultMod.MapData)
                {
                    double convertedValue = entry.Value.Default.GetValue;
                    monsterStatusData.AddModValue(entry.Key, CryptoValueDouble.Create(convertedValue));
                    TrackCalculationForMonster(monsterStatusData, entry.Key, "MonsterDefaultMod", convertedValue);
                }

                // 레벨 능력치 덮어씀 (GetValue로 변환된 값 사용)
                if (GameDBClientManager.Instance.GameDB_Monster.MonsterBossLevel.MapData.TryGetValue(currentStage, out GameDB_Client_MonsterBossLevel bossLevelData))
                {
                    foreach (GameDB_Client_ModFormula modFormula in bossLevelData.ModValues)
                    {
                        double convertedValue = modFormula.Formula.GetValue(level).GetValue;
                        monsterStatusData.SetModValue(modFormula.Mod, CryptoValueDouble.Create(convertedValue));
                        TrackCalculationForMonster(monsterStatusData, modFormula.Mod, $"MonsterBossLevel ({currentStage}, Lv{level})", convertedValue);
                    }
                }

                // BossMod 덮어씀 (GetValue로 변환된 값 사용)
                if (GameDBClientManager.Instance.GameDB_Monster.MonsterBossMod.MapData.TryGetValue(currentStage, out GameDB_Client_MonsterBossMod bossModData))
                {
                    foreach (GameDB_Client_ModValue entryMod in bossModData.ModValues)
                    {
                        double convertedValue = entryMod.Value.GetValue;
                        monsterStatusData.SetModValue(entryMod.Mod, CryptoValueDouble.Create(convertedValue));
                        TrackCalculationForMonster(monsterStatusData, entryMod.Mod, $"MonsterBossMod ({currentStage})", convertedValue);
                    }
                }
            }
            else if (monsterTypeEnum == EMonsterType.monster_type_floorboss)
            {
                // selectedStage 사용 (GameBattleControlManager 대신)
                EStage currentStage = targetStage;

                // 기본 능력치 (GetValue로 변환된 값 사용 - FLOAT_PER는 비율로 변환)
                foreach (KeyValuePair<EMod, GameDB_Client_MonsterDefaultMod> entry in GameDBClientManager.Instance.GameDB_Monster.MonsterDefaultMod.MapData)
                {
                    double convertedValue = entry.Value.Default.GetValue;
                    monsterStatusData.AddModValue(entry.Key, CryptoValueDouble.Create(convertedValue));
                    TrackCalculationForMonster(monsterStatusData, entry.Key, "MonsterDefaultMod", convertedValue);
                }

                // 레벨 능력치 덮어씀 (GetValue로 변환된 값 사용)
                if (GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBossLevel.MapData.TryGetValue(currentStage, out GameDB_Client_MonsterFloorBossLevel floorBossLevelData))
                {
                    foreach (GameDB_Client_ModFormula modFormula in floorBossLevelData.ModValues)
                    {
                        double convertedValue = modFormula.Formula.GetValue(level).GetValue;
                        monsterStatusData.SetModValue(modFormula.Mod, CryptoValueDouble.Create(convertedValue));
                        TrackCalculationForMonster(monsterStatusData, modFormula.Mod, $"MonsterFloorBossLevel ({currentStage}, Lv{level})", convertedValue);
                    }
                }

                // FloorBossMod 덮어씀 (GetValue로 변환된 값 사용)
                if (GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBossMod.MapData.TryGetValue(currentStage, out GameDB_Client_MonsterFloorBossMod floorBossModData))
                {
                    foreach (GameDB_Client_ModValue entryMod in floorBossModData.ModValues)
                    {
                        double convertedValue = entryMod.Value.GetValue;
                        monsterStatusData.SetModValue(entryMod.Mod, CryptoValueDouble.Create(convertedValue));
                        TrackCalculationForMonster(monsterStatusData, entryMod.Mod, $"MonsterFloorBossMod ({currentStage})", convertedValue);
                    }
                }
            }
            else // Normal
            {
                EMonsterAttackType attackTypeEnum = monsterData.MonsterAttackType;

                // 기본 능력치 (GetValue로 변환된 값 사용 - FLOAT_PER는 비율로 변환)
                foreach (KeyValuePair<EMod, GameDB_Client_MonsterDefaultMod> entry in GameDBClientManager.Instance.GameDB_Monster.MonsterDefaultMod.MapData)
                {
                    double convertedValue = entry.Value.Default.GetValue;
                    monsterStatusData.AddModValue(entry.Key, CryptoValueDouble.Create(convertedValue));
                    TrackCalculationForMonster(monsterStatusData, entry.Key, "MonsterDefaultMod", convertedValue);
                }

                // 레벨 능력치 덮어씀 (GetValue로 변환된 값 사용)
                foreach (KeyValuePair<EMod, GameDB_Client_MonsterNormalLevel> entry in GameDBClientManager.Instance.GameDB_Monster.MonsterNormalLevel.MapData)
                {
                    double convertedValue = entry.Value.Stat.GetValue(level).GetValue;

                    // 디버깅: 비정상적인 값 감지
                    if (double.IsInfinity(convertedValue) || double.IsNaN(convertedValue) || convertedValue > 1e20)
                    {
                        Debug.LogWarning($"[SimulatorDefender] 비정상적인 MonsterNormalLevel 값 감지! MOD: {entry.Key}, Level: {level}, Value: {convertedValue}");
                    }

                    monsterStatusData.SetModValue(entry.Key, CryptoValueDouble.Create(convertedValue));
                    TrackCalculationForMonster(monsterStatusData, entry.Key, $"MonsterNormalLevel (Lv{level})", convertedValue);
                }

                // NormalMod 덮어씀 (GetValue로 변환된 값 사용)
                if (GameDBClientManager.Instance.GameDB_Monster.MonsterNormalMod.MapData.TryGetValue(attackTypeEnum, out GameDB_Client_MonsterNormalMod normalModData))
                {
                    foreach (GameDB_Client_ModValue entryMod in normalModData.ModValues)
                    {
                        double convertedValue = entryMod.Value.GetValue;
                        monsterStatusData.SetModValue(entryMod.Mod, CryptoValueDouble.Create(convertedValue));
                        TrackCalculationForMonster(monsterStatusData, entryMod.Mod, $"MonsterNormalMod ({attackTypeEnum})", convertedValue);
                    }
                }
            }

            // MonsterType별 ModMultipliers 적용 (최종 단계)
            ApplyMonsterTypeMultipliers(monsterStatusData, monsterData.MonsterType);
        }

        /// <summary>
        /// MonsterType별 ModMultipliers 적용
        /// Normal: 100%, Boss: 500% 등
        /// </summary>
        private void ApplyMonsterTypeMultipliers(FBattleStatusMonsterData monsterStatusData, EMonsterType monsterType)
        {
            // MonsterType Multipliers 정보 초기화
            monsterTypeMultipliers.Clear();

            if (!GameDBClientManager.Instance.GameDB_Monster.MonsterType.MapData.TryGetValue(monsterType, out GameDB_Client_MonsterType monsterTypeData))
            {
                return;
            }

            if (monsterTypeData.ModMultipliers == null || monsterTypeData.ModMultipliers.Count == 0)
            {
                return;
            }

            // ModMultipliers에 정의된 각 MOD에 대해 배수 적용
            foreach (KeyValuePair<EMod, float> multiplier in monsterTypeData.ModMultipliers)
            {
                EMod mod = multiplier.Key;
                float multiplierValue = multiplier.Value;

                // Multiplier 정보 저장 (UI 표시용)
                monsterTypeMultipliers[mod] = multiplierValue;

                if (monsterStatusData.IsExistModValue(mod))
                {
                    CryptoValueDouble currentValue = monsterStatusData.GetModValue(mod);
                    double newValue = currentValue.Value * multiplierValue;
                    monsterStatusData.SetModValue(mod, CryptoValueDouble.Create(newValue));

                    TrackCalculationForMonster(monsterStatusData, mod, $"MonsterType Multiplier ({monsterType}, ×{multiplierValue:F1})", newValue);
                }
            }
        }

        /// <summary>
        /// 몬스터 스탯 계산 추적 헬퍼 메서드
        /// </summary>
        private void TrackCalculationForMonster(FBattleStatusMonsterData monsterStatusData, EMod mod, string source, double value)
        {
            if (!monsterStatusData.enableCalculationTracking || monsterStatusData.calculationSteps == null)
                return;

            if (!monsterStatusData.calculationSteps.ContainsKey(mod))
                monsterStatusData.calculationSteps[mod] = new List<(string, double)>();

            monsterStatusData.calculationSteps[mod].Add((source, value));
        }

        /// <summary>
        /// FBattleStatus에서 MOD 값과 추적 정보 추출
        /// </summary>
        private void ExtractModsFromBattleStatus(FBattleStatus battleStatus)
        {
            if (battleStatus == null || battleStatus.characterData == null)
                return;

            // MOD 값 추출 - 모든 EMod 타입을 순회하며 값 추출
            foreach (EMod mod in System.Enum.GetValues(typeof(EMod)))
            {
                if (battleStatus.characterData.IsExistModValue(mod))
                {
                    calculatedMods[mod] = battleStatus.characterData.GetModValue(mod).Value;
                }
            }

            // 계산 추적 정보 추출
            if (battleStatus.characterData is FBattleStatusMonsterData monsterStatusData)
            {
                if (monsterStatusData.calculationSteps != null)
                {
                    modCalculationSteps = monsterStatusData.calculationSteps;
                }
            }
        }

        /// <summary>
        /// 계산된 MOD 값에서 주요 스탯 추출
        /// </summary>
        private void ExtractMainStats()
        {
            // Physical Resistance (물리 저항 - 인게임 값 저장, UI는 FormatModValueForUI로 표시)
            physicalResistance = GetModValue(EMod.mod_physical_resistance);
            physicalResistanceDisplay = SimulatorCalculator.FormatModValueForUI(EMod.mod_physical_resistance, physicalResistance, "F3");

            // Resistances (인게임 값 저장)
            fireResistance = GetModValue(EMod.mod_fire_resistance);
            coldResistance = GetModValue(EMod.mod_cold_resistance);
            lightningResistance = GetModValue(EMod.mod_lightning_resistance);
            poisonResistance = GetModValue(EMod.mod_poison_resistance);

            // 원소 저항 (모든 원소 저항에 적용)
            double elementalResistance = GetModValue(EMod.mod_elemental_resistance);
            if (elementalResistance != 0)
            {
                fireResistance += elementalResistance;
                coldResistance += elementalResistance;
                lightningResistance += elementalResistance;
                poisonResistance += elementalResistance;
            }

            // UI 표시는 FormatModValueForUI로 변환
            resistancesDisplay = $"{SimulatorCalculator.FormatModValueForUI(EMod.mod_fire_resistance, fireResistance, "F3")} / {SimulatorCalculator.FormatModValueForUI(EMod.mod_cold_resistance, coldResistance, "F3")} / {SimulatorCalculator.FormatModValueForUI(EMod.mod_lightning_resistance, lightningResistance, "F3")} / {SimulatorCalculator.FormatModValueForUI(EMod.mod_poison_resistance, poisonResistance, "F3")}";

            // 저항 최대치 계산
            CalculateResistanceMax();

            // Evade & Block (GameDB에 이미 백분율로 저장됨, 소수점 3자리)
            evadeChance = GetModValue(EMod.mod_evade_chance);
            blockChance = GetModValue(EMod.mod_block_chance);
            evadeBlockDisplay = $"{evadeChance:F3}% / {blockChance:F3}%";

            // Max Life
            maxLife = GetModValue(EMod.mod_life);
            maxLifeDisplay = $"{BattleSimulatorWindow.FormatNumber(maxLife)}";
        }

        /// <summary>
        /// MOD 소스 추적 생성 (플레이어와 유사한 형태로)
        /// </summary>
        /// <summary>
        /// MOD 값 가져오기 (없으면 0 반환)
        /// </summary>
        private double GetModValue(EMod mod)
        {
            return calculatedMods.ContainsKey(mod) ? calculatedMods[mod] : 0.0;
        }

        /// <summary>
        /// FCharacterStatus로 변환 (시뮬레이션용)
        /// </summary>
        public FCharacterStatus ToCharacterStatus()
        {
            // 스펙 계산이 안되어 있으면 계산
            if (calculatedMods.Count == 0)
            {
                CalculateDefenderStats();
            }

            FCharacterSingleData monsterData;

            if (monsterType == EMonsterDefenderType.Normal)
            {
                // Normal Monster Data 생성
                GameDB_Client_MonsterNormal normalData = null;
                if (GameDBClientManager.Instance != null &&
                    GameDBClientManager.Instance.GameDB_Monster != null &&
                    GameDBClientManager.Instance.GameDB_Monster.MonsterNormal.MapData.ContainsKey(selectedNormalMonster))
                {
                    normalData = GameDBClientManager.Instance.GameDB_Monster.MonsterNormal.MapData[selectedNormalMonster];
                }

                monsterData = new FMonsterNormalSingleData(
                    inLevel: stageLevel,
                    inTeamNo: 1,
                    inSpawnFieldID: 0,
                    inMonsterGroup: 0,
                    InNormalType: selectedNormalMonster,
                    InMonsterNormalData: normalData
                );
            }
            else if (monsterType == EMonsterDefenderType.FloorBoss)
            {
                // None이면 기본값 사용
                var floorBossType = selectedFloorBossMonster;
                if (floorBossType == EMonsterFloorBoss.None)
                {
                    floorBossType = EMonsterFloorBoss.monster_floorboss_1000;
                }

                // FloorBoss Monster Data 생성
                GameDB_Client_MonsterFloorBoss floorBossData = null;
                if (GameDBClientManager.Instance != null &&
                    GameDBClientManager.Instance.GameDB_Monster != null &&
                    GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBoss.MapData.ContainsKey(floorBossType))
                {
                    floorBossData = GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBoss.MapData[floorBossType];
                }

                monsterData = new FMonsterFloorBossSingleData(
                    inLevel: stageLevel,
                    inTeamNo: 1,
                    inSpawnFieldID: 0,
                    inMonsterGroup: 0,
                    InFloorBossType: floorBossType,
                    InMonsterFloorBossData: floorBossData
                );
            }
            else // Boss
            {
                // Boss Monster Data 생성
                GameDB_Client_MonsterBoss bossData = null;
                if (GameDBClientManager.Instance != null &&
                    GameDBClientManager.Instance.GameDB_Monster != null &&
                    GameDBClientManager.Instance.GameDB_Monster.MonsterBoss.MapData.ContainsKey(selectedBossMonster))
                {
                    bossData = GameDBClientManager.Instance.GameDB_Monster.MonsterBoss.MapData[selectedBossMonster];
                }

                monsterData = new FMonsterBossSingleData(
                    inLevel: stageLevel,
                    inTeamNo: 1,
                    inSpawnFieldID: 0,
                    inMonsterGroup: 0,
                    InBossType: selectedBossMonster,
                    InMonsterBossData: bossData
                );
            }

            // FCharacterStatus 생성
            FCharacterStatus defenderStatus = new FCharacterStatus();

            // FBattleStatus 생성 및 설정
            FBattleStatus battleStatus = new FBattleStatus(monsterData);
            defenderStatus.UpdateBattleStatus(battleStatus);

            // 계산된 MOD 값을 BattleStatus에 적용
            foreach (var modEntry in calculatedMods)
            {
                battleStatus.characterData.SetModValue(modEntry.Key, CryptoValueDouble.Create(modEntry.Value));
            }

            // UpdateAllData() 호출하여 MOD 값 통합
            defenderStatus.UpdateBattleStatus(battleStatus);

            return defenderStatus;
        }

        /// <summary>
        /// 몬스터 공격 속성 추출 (SkillList → Spell → DamageType)
        /// </summary>
        private void ExtractAttackElement(Dictionary<ESkillType, GameDB_Client_SkillList> skillList)
        {
            // 기본값 설정
            attackElement = EDamageType.physical;
            attackSkill = ESkill.None;
            attackElementDisplay = "Physical";
            attackSkillDisplay = "-";

            if (skillList == null || skillList.Count == 0)
            {
                return;
            }

            // spell 타입 스킬 우선 검색
            if (skillList.TryGetValue(ESkillType.skill_spell, out GameDB_Client_SkillList spellList) &&
                spellList.Skills != null && spellList.Skills.Count > 0)
            {
                ESkill skill = spellList.Skills[0];
                attackSkill = skill;
                attackSkillDisplay = skill.ToString();

                // GameDB_Skill에서 DamageType 조회
                if (GameDBClientManager.Instance?.GameDB_Skill?.Spell?.MapData != null &&
                    GameDBClientManager.Instance.GameDB_Skill.Spell.MapData.TryGetValue(skill, out GameDB_Client_Skill skillData))
                {
                    attackElement = skillData.DamageType;
                    attackElementDisplay = GetDamageTypeDisplayName(attackElement);
                }
            }
            // aura 타입 스킬 검색
            else if (skillList.TryGetValue(ESkillType.skill_aura, out GameDB_Client_SkillList auraList) &&
                     auraList.Skills != null && auraList.Skills.Count > 0)
            {
                ESkill skill = auraList.Skills[0];
                attackSkill = skill;
                attackSkillDisplay = skill.ToString();

                if (GameDBClientManager.Instance?.GameDB_Skill?.Aura?.MapData != null &&
                    GameDBClientManager.Instance.GameDB_Skill.Aura.MapData.TryGetValue(skill, out GameDB_Client_Skill skillData))
                {
                    attackElement = skillData.DamageType;
                    attackElementDisplay = GetDamageTypeDisplayName(attackElement);
                }
            }
        }

        /// <summary>
        /// DamageType을 한글 표시명으로 변환
        /// </summary>
        private string GetDamageTypeDisplayName(EDamageType damageType)
        {
            return damageType switch
            {
                EDamageType.physical => "물리 (Physical)",
                EDamageType.fire => "화염 (Fire)",
                EDamageType.cold => "냉기 (Cold)",
                EDamageType.lightning => "번개 (Lightning)",
                EDamageType.poison => "독 (Poison)",
                _ => damageType.ToString()
            };
        }

        /// <summary>
        /// 저항 최대치 계산
        /// 기본값 75% (GameGlobalConfig.maxResistance) + mod_*_resistance_max
        /// </summary>
        private void CalculateResistanceMax()
        {
            // 기본 저항 최대치 (75%, GameGlobalConfig.maxResistance는 이미 비율 0.75)
            double baseMax = GameGlobalConfig.maxResistance; // 0.75 (비율)

            // 물리 저항 최대치 (비율 + 비율)
            double physicalResMaxMod = GetModValue(EMod.mod_physical_resistance_max);
            physicalResistanceMax = baseMax + physicalResMaxMod;

            // 화염 저항 최대치
            fireResistanceMax = baseMax + GetModValue(EMod.mod_fire_resistance_max);

            // 냉기 저항 최대치
            coldResistanceMax = baseMax + GetModValue(EMod.mod_cold_resistance_max);

            // 번개 저항 최대치
            lightningResistanceMax = baseMax + GetModValue(EMod.mod_lightning_resistance_max);

            // 독 저항 최대치
            poisonResistanceMax = baseMax + GetModValue(EMod.mod_poison_resistance_max);

            // 원소 저항 최대치 (모든 원소 저항에 적용)
            double elementalResistanceMax = GetModValue(EMod.mod_elemental_resistance_max);
            if (elementalResistanceMax != 0)
            {
                fireResistanceMax += elementalResistanceMax;
                coldResistanceMax += elementalResistanceMax;
                lightningResistanceMax += elementalResistanceMax;
                poisonResistanceMax += elementalResistanceMax;
            }
        }

        /// <summary>
        /// 최종 저항 계산 (공격자의 관통, % 감소 적용)
        /// 모든 입출력은 ratio (0.75 = 75%)
        ///
        /// 저항 최대치 작동 방식:
        /// - 기본 최대치: 0.75 (75%, GameGlobalConfig.maxResistance)
        /// - 저항 최대치 MOD: mod_*_resistance_max로 최대치 증가 가능
        /// - 예: 저항 합계 0.8 (80%), 기본 최대치 0.75 (75%) → 0.75만 적용
        /// - 예: 저항 합계 0.8 (80%), 최대치 0.85 (85%) → 0.8 전부 적용
        /// </summary>
        /// <param name="baseResistance">방어자의 기본 저항 (ratio: 0.75 = 75%)</param>
        /// <param name="resistanceMax">저항 최대치 (ratio: 0.75 = 75%)</param>
        /// <param name="attackerPenetration">공격자의 저항 관통 (ratio: 0.1 = 10%)</param>
        /// <param name="attackerReduction">공격자의 적 저항 % 감소 (ratio: 0.3 = 30%)</param>
        /// <returns>최종 저항 (ratio: -1.0 ~ 최대치)</returns>
        public static double CalculateFinalResistance(
            double baseResistance,
            double resistanceMax,
            double attackerPenetration,
            double attackerReduction)
        {
            // 1단계: 저항 최대치 적용 (저항 합계가 최대치를 초과하면 최대치로 제한)
            double clampedResistance = System.Math.Min(baseResistance, resistanceMax);

            // 2단계: 공격자의 적 저항 % 감소 적용 (ratio)
            double reducedResistance = clampedResistance * (1.0 - attackerReduction);

            // 3단계: 공격자의 저항 관통 차감 (ratio)
            double effectiveResistance = reducedResistance - attackerPenetration;

            // 4단계: 최종 저항 클램핑 (-100% ~ 최대치)
            double finalResistance = System.Math.Clamp(effectiveResistance, -1.0, resistanceMax);

            return finalResistance;
        }

        /// <summary>
        /// 프리셋 생성: 1000층 보스 (GameDB 계산 포함)
        /// Play Mode에서 호출 시 실제 GameDB를 사용하여 정확한 스펙 계산
        /// Edit Mode에서 호출 시 기본값 사용
        /// </summary>
        public static SimulatorDefender CreateDefault1000FloorBoss()
        {
            var defender = new SimulatorDefender
            {
                monsterType = EMonsterDefenderType.FloorBoss,
                selectedFloorBossMonster = EMonsterFloorBoss.monster_floorboss_1000,
                selectedStage = EStage.stage_main,
                stageLevel = 1000
            };

            // 기본 이름만 설정 (실제 스탯은 UpdateFromBattleStatus에서 GameDB로부터 계산)
            defender.defenderName = "Boss Monster (Lv 1000)";

            return defender;
        }
    }

    /// <summary>
    /// 몬스터 Defender 타입 Enum
    /// </summary>
    public enum EMonsterDefenderType
    {
        Normal,    // 일반 몬스터
        Boss,      // 보스 몬스터
        FloorBoss  // 계층 보스 몬스터
    }
}
