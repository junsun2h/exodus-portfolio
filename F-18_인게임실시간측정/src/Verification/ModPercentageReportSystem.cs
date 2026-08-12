using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// MOD 값 사용 방식 (BattleStatus에는 이미 GetValue 변환이 적용된 값이 저장됨)
    /// </summary>
    public enum EModPercentageHandling
    {
        /// <summary>
        /// 저장된 값 그대로 사용
        /// </summary>
        Direct,

        /// <summary>
        /// 1 + value 형태로 배율 사용
        /// </summary>
        OnePlusValue,

        /// <summary>
        /// 1 - value 형태로 감소 배율 사용
        /// </summary>
        OneMinusValue,

        /// <summary>
        /// 참고 정보 (계산에 직접 사용되지 않음)
        /// </summary>
        Reference
    }

    /// <summary>
    /// 개별 MOD 정보
    /// 주의: BattleStatus에 저장된 값은 이미 GetValue 변환이 적용된 상태
    /// (FLOAT_PER 타입은 이미 /100이 적용되어 비율값으로 저장됨)
    /// </summary>
    public class ModPercentageInfo
    {
        public EMod ModType { get; set; }
        public string ModName { get; set; }
        public EModValueType ValueType { get; set; }
        public double StoredValue { get; set; }     // BattleStatus에 저장된 값 (이미 GetValue 변환 적용됨)
        public double DisplayValue { get; set; }    // UI 표시용 값 (백분율은 *100)
        public EModPercentageHandling Handling { get; set; }
        public string UsageContext { get; set; }    // 사용 맥락 (어떤 계산 단계에서)
        public string Formula { get; set; }         // 적용 공식

        /// <summary>
        /// 경고 메시지 (문제가 있을 경우)
        /// </summary>
        public string Warning
        {
            get
            {
                // 참고 정보는 경고 대상 제외
                if (Handling == EModPercentageHandling.Reference || ModType == EMod.None)
                {
                    return null;
                }

                // 접미사와 ValueType 불일치 검사
                string modName = ModType.ToString();
                bool hasIncSuffix = modName.Contains("_inc");
                bool hasMoreSuffix = modName.Contains("_more");
                bool shouldBePercentage = hasIncSuffix || hasMoreSuffix;

                if (shouldBePercentage && ValueType != EModValueType.FLOAT_PER)
                {
                    return $"⚠️ _inc/_more 접미사가 있지만 FLOAT_PER 타입이 아님 (현재: {ValueType})";
                }

                if (!shouldBePercentage && ValueType == EModValueType.FLOAT_PER && !modName.Contains("chance") && !modName.Contains("resistance"))
                {
                    return $"⚠️ 접미사가 없는데 FLOAT_PER 타입임 (의도적인지 확인 필요)";
                }

                return null;
            }
        }

        public override string ToString()
        {
            string warning = Warning;
            string warningStr = string.IsNullOrEmpty(warning) ? "" : $"\n      {warning}";

            // 백분율 타입은 % 표시, 그 외는 그대로
            string displayStr = ValueType == EModValueType.FLOAT_PER
                ? $"{DisplayValue:F2}%"
                : $"{StoredValue:F6}";

            return $"{ModName} ({ValueType})\n" +
                   $"      저장값: {StoredValue:F6} | 표시값: {displayStr}\n" +
                   $"      처리: {Handling} | 공식: {Formula}{warningStr}";
        }
    }

    /// <summary>
    /// 피해 계산 단계별 MOD 분석 결과
    /// </summary>
    public class DamageStageModAnalysis
    {
        public string StageName { get; set; }
        public int StageOrder { get; set; }
        public string Description { get; set; }
        public string CalculationFormula { get; set; }
        public List<ModPercentageInfo> ModInfos { get; set; } = new List<ModPercentageInfo>();
        public double StageResult { get; set; }

        public bool HasWarnings => ModInfos.Any(m => !string.IsNullOrEmpty(m.Warning));

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[단계 {StageOrder}] {StageName}");
            sb.AppendLine($"  설명: {Description}");
            sb.AppendLine($"  공식: {CalculationFormula}");
            sb.AppendLine($"  결과: {StageResult:F6}");

            if (ModInfos.Count > 0)
            {
                sb.AppendLine($"  사용 MOD ({ModInfos.Count}개):");
                foreach (var mod in ModInfos)
                {
                    sb.AppendLine($"    - {mod}");
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 전체 MOD 백분율 분석 결과
    /// </summary>
    public class ModPercentageReport
    {
        public DateTime Timestamp { get; set; }
        public ESkillTag DamageType { get; set; }
        public List<DamageStageModAnalysis> Stages { get; set; } = new List<DamageStageModAnalysis>();

        public int TotalModCount => Stages.Sum(s => s.ModInfos.Count);
        public int WarningCount => Stages.Sum(s => s.ModInfos.Count(m => !string.IsNullOrEmpty(m.Warning)));

        public string GenerateReport()
        {
            var sb = new StringBuilder();

            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("🔢 MOD 백분율 처리 리포트");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"분석 시각: {Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"피해 속성: {DamageType}");
            sb.AppendLine($"총 MOD 수: {TotalModCount}개");
            sb.AppendLine($"경고 수: {WarningCount}개");
            sb.AppendLine();

            // 값 처리 방식 범례
            sb.AppendLine("════════════════════════════════════════════════════════════════════");
            sb.AppendLine("📖 값 처리 방식 범례");
            sb.AppendLine("════════════════════════════════════════════════════════════════════");
            sb.AppendLine("  ※ BattleStatus에 저장된 값은 이미 GetValue 변환이 적용된 상태입니다.");
            sb.AppendLine("    (FLOAT_PER 타입은 이미 /100이 적용되어 비율값으로 저장됨)");
            sb.AppendLine();
            sb.AppendLine("  Direct          : 저장된 값 그대로 사용");
            sb.AppendLine("  OnePlusValue    : (1 + value) 형태 배율");
            sb.AppendLine("  OneMinusValue   : (1 - value) 형태 감소 배율");
            sb.AppendLine("  Reference       : 참고 정보 (계산에 직접 사용되지 않음)");
            sb.AppendLine();

            // ValueType 범례
            sb.AppendLine("════════════════════════════════════════════════════════════════════");
            sb.AppendLine("📖 ValueType 범례");
            sb.AppendLine("════════════════════════════════════════════════════════════════════");
            sb.AppendLine("  FLOAT_PER : 백분율 값 (저장 시 이미 /100 적용됨, 표시값은 *100)");
            sb.AppendLine("  FLOAT     : 실수 값");
            sb.AppendLine("  INT       : 정수 값");
            sb.AppendLine("  DOUBLE    : 배정밀도 실수");
            sb.AppendLine();

            // 단계별 분석
            foreach (var stage in Stages)
            {
                sb.AppendLine("════════════════════════════════════════════════════════════════════");
                sb.AppendLine(stage.ToString());
            }

            // 경고 요약
            if (WarningCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════════");
                sb.AppendLine("⚠️ 경고 요약");
                sb.AppendLine("════════════════════════════════════════════════════════════════════");

                foreach (var stage in Stages)
                {
                    foreach (var mod in stage.ModInfos)
                    {
                        if (!string.IsNullOrEmpty(mod.Warning))
                        {
                            sb.AppendLine($"  [{stage.StageName}] {mod.ModName}: {mod.Warning}");
                        }
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📋 종합: {(WarningCount == 0 ? "✅ 문제 없음" : $"❌ {WarningCount}개 경고 발견")}");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            return sb.ToString();
        }
    }

    /// <summary>
    /// MOD 백분율 리포트 시스템
    /// 피해 계산의 각 단계에서 MOD가 어떻게 백분율 처리되는지 분석
    /// </summary>
    public static class ModPercentageReportSystem
    {
        /// <summary>
        /// 전체 피해 계산 단계별 MOD 백분율 분석 실행
        /// </summary>
        public static ModPercentageReport AnalyzeDamageCalculation(ESkillTag damageType, SimulatorDefender defender = null)
        {
            var report = new ModPercentageReport
            {
                Timestamp = DateTime.Now,
                DamageType = damageType
            };

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[MOD 백분율 분석] 플레이어 캐릭터를 찾을 수 없습니다.");
                return report;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[MOD 백분율 분석] BattleStatus를 찾을 수 없습니다.");
                return report;
            }

            FCharacterStatus defenderStatus = defender?.ToCharacterStatus();

            // 1. Flat Damage 단계
            report.Stages.Add(AnalyzeFlatDamageStage(battleStatus, damageType));

            // 2. Inc 배율 단계
            report.Stages.Add(AnalyzeIncMultiplierStage(battleStatus, damageType, defender));

            // 3. More 배율 단계
            report.Stages.Add(AnalyzeMoreMultiplierStage(battleStatus, damageType));

            // 4. 크리티컬 확률 단계
            report.Stages.Add(AnalyzeCriticalChanceStage(battleStatus, defenderStatus, defender));

            // 5. 크리티컬 배율 단계
            report.Stages.Add(AnalyzeCriticalMultiplierStage(battleStatus, defenderStatus));

            // 6. 시전 속도 단계
            report.Stages.Add(AnalyzeCastSpeedStage(battleStatus));

            // 7. 저항 관련 단계 (방어자가 있을 경우)
            if (defender != null)
            {
                report.Stages.Add(AnalyzeResistanceStage(battleStatus, damageType, defender));
                report.Stages.Add(AnalyzeDamageTakenStage(battleStatus, defender));
            }

            return report;
        }

        #region 단계별 분석

        /// <summary>
        /// 1. Flat Damage 단계 분석
        /// </summary>
        private static DamageStageModAnalysis AnalyzeFlatDamageStage(FBattleStatus battleStatus, ESkillTag damageType)
        {
            var stage = new DamageStageModAnalysis
            {
                StageOrder = 1,
                StageName = "Flat Damage (기본 피해)",
                Description = "속성별 기본 피해 + 공통 피해를 합산",
                CalculationFormula = "∑(mod_xxx_damage)"
            };

            var flatMods = new List<EMod>();

            // 속성별 Flat Damage
            switch (damageType)
            {
                case ESkillTag.skilltag_physical:
                    flatMods.Add(EMod.mod_physical_damage);
                    break;
                case ESkillTag.skilltag_fire:
                    flatMods.Add(EMod.mod_fire_damage);
                    flatMods.Add(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_cold:
                    flatMods.Add(EMod.mod_cold_damage);
                    flatMods.Add(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_lightning:
                    flatMods.Add(EMod.mod_lightning_damage);
                    flatMods.Add(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_poison:
                    flatMods.Add(EMod.mod_poison_damage);
                    flatMods.Add(EMod.mod_elemental_damage);
                    break;
            }

            // 공통 피해
            flatMods.Add(EMod.mod_all_skill_damage);
            flatMods.Add(EMod.mod_all_damage);

            double total = 0;
            foreach (var mod in flatMods)
            {
                var info = AnalyzeMod(battleStatus, mod, "Flat Damage 합산", "total += value");
                if (info != null && Math.Abs(info.StoredValue) > 0.0001)
                {
                    stage.ModInfos.Add(info);
                    total += info.StoredValue;
                }
            }

            stage.StageResult = total;
            return stage;
        }

        /// <summary>
        /// 2. Inc 배율 단계 분석
        /// </summary>
        private static DamageStageModAnalysis AnalyzeIncMultiplierStage(FBattleStatus battleStatus, ESkillTag damageType, SimulatorDefender defender)
        {
            var stage = new DamageStageModAnalysis
            {
                StageOrder = 2,
                StageName = "Inc 배율 (Increased)",
                Description = "증가 MOD들을 합산 후 (1 + 합계)로 배율 계산",
                CalculationFormula = "1 + ∑(mod_xxx_inc)"
            };

            var incMods = new List<EMod>();

            // 속성별 Inc
            switch (damageType)
            {
                case ESkillTag.skilltag_physical:
                    incMods.Add(EMod.mod_physical_damage_inc);
                    break;
                case ESkillTag.skilltag_fire:
                    incMods.Add(EMod.mod_fire_damage_inc);
                    incMods.Add(EMod.mod_elemental_damage_inc);
                    break;
                case ESkillTag.skilltag_cold:
                    incMods.Add(EMod.mod_cold_damage_inc);
                    incMods.Add(EMod.mod_elemental_damage_inc);
                    break;
                case ESkillTag.skilltag_lightning:
                    incMods.Add(EMod.mod_lightning_damage_inc);
                    incMods.Add(EMod.mod_elemental_damage_inc);
                    break;
                case ESkillTag.skilltag_poison:
                    incMods.Add(EMod.mod_poison_damage_inc);
                    incMods.Add(EMod.mod_elemental_damage_inc);
                    break;
            }

            // 공통 Inc
            incMods.Add(EMod.mod_all_skill_damage_inc);
            incMods.Add(EMod.mod_all_damage_inc);
            incMods.Add(EMod.mod_projectile_damage_inc);
            incMods.Add(EMod.mod_damage_inc_on_full_life);
            incMods.Add(EMod.mod_damage_inc_on_low_life);

            // 조건부 Inc (Ailment 상태)
            if (defender != null)
            {
                if (defender.targetHasArctic) incMods.Add(EMod.mod_inc_damage_arctic);
                if (defender.targetHasBleeding) incMods.Add(EMod.mod_inc_damage_bleeding);
                if (defender.targetHasChill) incMods.Add(EMod.mod_inc_damage_chill);
                if (defender.targetHasIgnite) incMods.Add(EMod.mod_inc_damage_ignite);
                if (defender.targetHasPoisoning) incMods.Add(EMod.mod_inc_damage_poisoning);
                if (defender.targetHasShock) incMods.Add(EMod.mod_inc_damage_shock);
            }

            double totalInc = 0;
            foreach (var mod in incMods)
            {
                var info = AnalyzeMod(battleStatus, mod, "Inc 합산", "totalInc += value");
                if (info != null && Math.Abs(info.StoredValue) > 0.0001)
                {
                    stage.ModInfos.Add(info);
                    totalInc += info.StoredValue;
                }
            }

            stage.StageResult = 1 + totalInc; // Inc 배율 = 1 + 합계
            return stage;
        }

        /// <summary>
        /// 3. More 배율 단계 분석
        /// </summary>
        private static DamageStageModAnalysis AnalyzeMoreMultiplierStage(FBattleStatus battleStatus, ESkillTag damageType)
        {
            var stage = new DamageStageModAnalysis
            {
                StageOrder = 3,
                StageName = "More 배율 (More)",
                Description = "증폭 MOD들을 개별적으로 (1 + value)를 곱셈",
                CalculationFormula = "∏(1 + mod_xxx_more)"
            };

            var moreMods = new List<EMod>();

            // 속성별 More
            switch (damageType)
            {
                case ESkillTag.skilltag_physical:
                    moreMods.Add(EMod.mod_physical_damage_more);
                    break;
                case ESkillTag.skilltag_fire:
                    moreMods.Add(EMod.mod_fire_damage_more);
                    break;
                case ESkillTag.skilltag_cold:
                    moreMods.Add(EMod.mod_cold_damage_more);
                    break;
                case ESkillTag.skilltag_lightning:
                    moreMods.Add(EMod.mod_lightning_damage_more);
                    break;
                case ESkillTag.skilltag_poison:
                    moreMods.Add(EMod.mod_poison_damage_more);
                    break;
            }

            // 공통 More
            moreMods.Add(EMod.mod_all_skill_damage_more);
            moreMods.Add(EMod.mod_all_damage_more);
            moreMods.Add(EMod.mod_projectile_damage_more);

            double moreMultiplier = 1.0;
            foreach (var mod in moreMods)
            {
                var info = AnalyzeMod(battleStatus, mod, "More 곱셈", "multiplier *= (1 + value)");
                if (info != null && Math.Abs(info.StoredValue) > 0.0001)
                {
                    // More는 개별적으로 (1 + value)를 곱함
                    info.Handling = EModPercentageHandling.OnePlusValue;
                    info.Formula = $"× (1 + {info.StoredValue:F6}) = × {1 + info.StoredValue:F6}";
                    stage.ModInfos.Add(info);
                    moreMultiplier *= (1 + info.StoredValue);
                }
            }

            stage.StageResult = moreMultiplier;
            return stage;
        }

        /// <summary>
        /// 4. 크리티컬 확률 단계 분석
        /// </summary>
        private static DamageStageModAnalysis AnalyzeCriticalChanceStage(FBattleStatus battleStatus, FCharacterStatus defenderStatus, SimulatorDefender defender)
        {
            var stage = new DamageStageModAnalysis
            {
                StageOrder = 4,
                StageName = "크리티컬 확률",
                Description = "기본 확률 × (1 + Inc 합계)",
                CalculationFormula = "base × (1 + ∑(crit_chance_inc))"
            };

            // 기본 크리티컬 확률
            var baseInfo = AnalyzeMod(battleStatus, EMod.mod_crit_chance, "기본 확률", "base = value");
            if (baseInfo != null)
            {
                baseInfo.Handling = EModPercentageHandling.Direct;
                stage.ModInfos.Add(baseInfo);
            }

            // 크리티컬 확률 Inc
            var incInfo = AnalyzeMod(battleStatus, EMod.mod_crit_chance_inc, "확률 Inc", "× (1 + value)");
            if (incInfo != null && Math.Abs(incInfo.StoredValue) > 0.0001)
            {
                incInfo.Handling = EModPercentageHandling.OnePlusValue;
                stage.ModInfos.Add(incInfo);
            }

            // 조건부 크리티컬 확률 Inc (Ailment 상태)
            if (defender != null)
            {
                var ailmentCritMods = new List<(EMod mod, bool active, string condition)>
                {
                    (EMod.mod_inc_crit_chance_arctic, defender.targetHasArctic, "극빙 상태"),
                    (EMod.mod_inc_crit_chance_bleeding, defender.targetHasBleeding, "출혈 상태"),
                    (EMod.mod_inc_crit_chance_chill, defender.targetHasChill, "오한 상태"),
                    (EMod.mod_inc_crit_chance_ignite, defender.targetHasIgnite, "점화 상태"),
                    (EMod.mod_inc_crit_chance_poisoning, defender.targetHasPoisoning, "중독 상태"),
                    (EMod.mod_inc_crit_chance_shock, defender.targetHasShock, "감전 상태"),
                };

                foreach (var (mod, active, condition) in ailmentCritMods)
                {
                    var info = AnalyzeMod(battleStatus, mod, $"조건부 Inc ({condition})", active ? "적용" : "미적용");
                    if (info != null && Math.Abs(info.StoredValue) > 0.0001)
                    {
                        info.UsageContext = active ? $"✅ {condition} 적용" : $"❌ {condition} 미적용";
                        info.Handling = EModPercentageHandling.Direct;
                        stage.ModInfos.Add(info);
                    }
                }
            }

            // 실제 크리티컬 확률 계산
            double resultCritChance = battleStatus.ResultCriticalChance(defenderStatus);
            stage.StageResult = resultCritChance * 100; // 백분율로 표시

            return stage;
        }

        /// <summary>
        /// 5. 크리티컬 배율 단계 분석
        /// </summary>
        private static DamageStageModAnalysis AnalyzeCriticalMultiplierStage(FBattleStatus battleStatus, FCharacterStatus defenderStatus)
        {
            var stage = new DamageStageModAnalysis
            {
                StageOrder = 5,
                StageName = "크리티컬 배율",
                Description = "(기본 + Flat) × (1 + Inc 합계)",
                CalculationFormula = "(base + flat) × (1 + ∑(crit_mult_inc))"
            };

            // 기본 크리티컬 배율
            var baseInfo = AnalyzeMod(battleStatus, EMod.mod_crit_multiplier, "기본 배율", "base = value");
            if (baseInfo != null)
            {
                baseInfo.Handling = EModPercentageHandling.Direct;
                stage.ModInfos.Add(baseInfo);
            }

            // 크리티컬 배율 Inc
            var incInfo = AnalyzeMod(battleStatus, EMod.mod_crit_multiplier_inc, "배율 Inc", "× (1 + value)");
            if (incInfo != null && Math.Abs(incInfo.StoredValue) > 0.0001)
            {
                incInfo.Handling = EModPercentageHandling.OnePlusValue;
                stage.ModInfos.Add(incInfo);
            }

            // 실제 크리티컬 배율 계산
            double resultCritMult = battleStatus.ResultCriticalMultiplier(defenderStatus);
            stage.StageResult = resultCritMult * 100; // 백분율로 표시

            return stage;
        }

        /// <summary>
        /// 6. 시전 속도 단계 분석
        /// </summary>
        private static DamageStageModAnalysis AnalyzeCastSpeedStage(FBattleStatus battleStatus)
        {
            var stage = new DamageStageModAnalysis
            {
                StageOrder = 6,
                StageName = "시전 속도",
                Description = "기본 속도 × (1 + Inc 합계)",
                CalculationFormula = "base × (1 + ∑(castspeed_inc))"
            };

            // 시전 속도 Inc
            var incMods = new List<EMod>
            {
                EMod.mod_castspeed_inc,
                EMod.mod_castspeed_inc_on_full_life,
                EMod.mod_castspeed_inc_on_low_life,
                EMod.mod_castspeed_inc_when_kill_recently
            };

            foreach (var mod in incMods)
            {
                var info = AnalyzeMod(battleStatus, mod, "속도 Inc", "× (1 + value)");
                if (info != null && Math.Abs(info.StoredValue) > 0.0001)
                {
                    info.Handling = EModPercentageHandling.OnePlusValue;
                    stage.ModInfos.Add(info);
                }
            }

            // 실제 시전 속도 계산
            double resultCastSpeed = battleStatus.ResultSkillCastSpeed();
            stage.StageResult = resultCastSpeed;

            return stage;
        }

        /// <summary>
        /// 7. 저항 관련 단계 분석
        /// </summary>
        private static DamageStageModAnalysis AnalyzeResistanceStage(FBattleStatus battleStatus, ESkillTag damageType, SimulatorDefender defender)
        {
            var stage = new DamageStageModAnalysis
            {
                StageOrder = 7,
                StageName = "저항 계산",
                Description = "적 저항 - 관통 - 감소, 최종 저항으로 피해 배율 계산",
                CalculationFormula = "damageMultiplier = 1 - (finalResistance / 100)"
            };

            // 관통 MOD
            var penetrationMods = GetPenetrationMods(damageType);
            foreach (var mod in penetrationMods)
            {
                var info = AnalyzeMod(battleStatus, mod, "저항 관통", "resistance -= value");
                if (info != null && Math.Abs(info.StoredValue) > 0.0001)
                {
                    info.Handling = EModPercentageHandling.Direct;
                    info.Formula = $"저항 -= {info.DisplayValue:F2}%";
                    stage.ModInfos.Add(info);
                }
            }

            // 저항 감소 MOD
            var reductionMods = GetResistanceReductionMods(damageType);
            foreach (var mod in reductionMods)
            {
                var info = AnalyzeMod(battleStatus, mod, "저항 감소", "resistance -= value");
                if (info != null && Math.Abs(info.StoredValue) > 0.0001)
                {
                    info.Handling = EModPercentageHandling.Direct;
                    info.Formula = $"저항 -= {info.DisplayValue:F2}%";
                    stage.ModInfos.Add(info);
                }
            }

            // 최종 저항 계산
            // defenderResistance: 백분율 (35 = 35%)
            // TotalModValue: ratio (0.1 = 10%) → 백분율로 변환 필요 (*100)
            double defenderResistance = GetDefenderResistance(defender, damageType);
            double penetration = penetrationMods.Sum(m => battleStatus.TotalModValue(m)) * 100.0;
            double reduction = reductionMods.Sum(m => battleStatus.TotalModValue(m)) * 100.0;
            double finalResistance = Math.Max(-100, Math.Min(75, defenderResistance - penetration - reduction));

            // 저항에 의한 피해 배율
            stage.StageResult = 1.0 - (finalResistance / 100.0);

            // 추가 정보
            var resistanceNote = new ModPercentageInfo
            {
                ModType = EMod.None,
                ModName = "[참고] 최종 저항 계산",
                ValueType = EModValueType.FLOAT_PER,
                StoredValue = finalResistance / 100.0,
                DisplayValue = finalResistance,
                Handling = EModPercentageHandling.Reference,
                UsageContext = "최종 저항 → 피해 배율",
                Formula = $"적 저항({defenderResistance:F0}%) - 관통/감소 = {finalResistance:F2}% → 배율 {stage.StageResult:F4}"
            };
            stage.ModInfos.Add(resistanceNote);

            return stage;
        }

        /// <summary>
        /// 8. 받는 피해 증감 단계 분석
        /// </summary>
        private static DamageStageModAnalysis AnalyzeDamageTakenStage(FBattleStatus battleStatus, SimulatorDefender defender)
        {
            var stage = new DamageStageModAnalysis
            {
                StageOrder = 8,
                StageName = "받는 피해 증감",
                Description = "적이 받는 피해 증가/감소 적용",
                CalculationFormula = "(1 + inc/100) × (1 - dec/100)"
            };

            // 적이 받는 피해 증가 MOD
            var incMods = new List<EMod>
            {
                EMod.mod_enemy_take_inc_physical_damage,
                EMod.mod_cursed_enemy_take_inc_physical_damage,
            };

            // 상태이상 피해 증가
            if (defender.targetHasArctic) incMods.Add(EMod.mod_inc_damage_arctic);
            if (defender.targetHasChill) incMods.Add(EMod.mod_inc_damage_chill);
            if (defender.targetHasBleeding) incMods.Add(EMod.mod_inc_damage_bleeding);
            if (defender.targetHasIgnite) incMods.Add(EMod.mod_inc_damage_ignite);
            if (defender.targetHasShock) incMods.Add(EMod.mod_inc_damage_shock);
            if (defender.targetHasPoisoning) incMods.Add(EMod.mod_inc_damage_poisoning);

            double totalInc = 0;
            foreach (var mod in incMods)
            {
                var info = AnalyzeMod(battleStatus, mod, "받는 피해 Inc", "inc += value");
                if (info != null && Math.Abs(info.StoredValue) > 0.0001)
                {
                    info.Handling = EModPercentageHandling.Direct;
                    stage.ModInfos.Add(info);
                    totalInc += info.StoredValue;
                }
            }

            // 최종 배율 계산 (저장값은 이미 비율이므로 그대로 사용)
            double damageTakenMultiplier = 1.0 + totalInc;
            stage.StageResult = damageTakenMultiplier;

            // 계산 정보 추가
            var calcNote = new ModPercentageInfo
            {
                ModType = EMod.None,
                ModName = "[참고] 받는 피해 배율 계산",
                ValueType = EModValueType.FLOAT_PER,
                StoredValue = totalInc,
                DisplayValue = totalInc * 100,
                Handling = EModPercentageHandling.Reference,
                UsageContext = "받는 피해 증가 → 배율",
                Formula = $"(1 + {totalInc:F6}) = {damageTakenMultiplier:F4}"
            };
            stage.ModInfos.Add(calcNote);

            return stage;
        }

        #endregion

        #region 헬퍼 메서드

        /// <summary>
        /// 개별 MOD 분석
        /// 주의: BattleStatus에 저장된 값은 이미 GetValue 변환이 적용된 상태
        /// </summary>
        private static ModPercentageInfo AnalyzeMod(FBattleStatus battleStatus, EMod modType, string usageContext, string formula)
        {
            try
            {
                // TotalModValue로 저장된 값 가져오기 (이미 GetValue 변환 적용됨)
                double storedValue = battleStatus.TotalModValue(modType);

                // ValueType 추론 (네이밍 규칙 기반)
                EModValueType valueType = InferValueType(modType);

                // UI 표시용 값 (백분율 타입은 *100)
                double displayValue = valueType == EModValueType.FLOAT_PER ? storedValue * 100.0 : storedValue;

                return new ModPercentageInfo
                {
                    ModType = modType,
                    ModName = modType.ToString(),
                    ValueType = valueType,
                    StoredValue = storedValue,
                    DisplayValue = displayValue,
                    Handling = EModPercentageHandling.Direct,
                    UsageContext = usageContext,
                    Formula = formula
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// MOD 이름 규칙에 따라 ValueType 추론
        /// </summary>
        private static EModValueType InferValueType(EMod modType)
        {
            string modName = modType.ToString();

            // _inc, _more 접미사가 있으면 FLOAT_PER
            if (modName.Contains("_inc") || modName.Contains("_more"))
            {
                return EModValueType.FLOAT_PER;
            }

            // chance, resistance 등 백분율 관련 MOD
            if (modName.Contains("_chance") || modName.Contains("_resistance") || modName.Contains("_penetration"))
            {
                return EModValueType.FLOAT_PER;
            }

            // 그 외는 FLOAT (절대값)
            return EModValueType.FLOAT;
        }

        /// <summary>
        /// 관통 MOD 목록 가져오기
        /// </summary>
        private static List<EMod> GetPenetrationMods(ESkillTag damageType)
        {
            var mods = new List<EMod>();

            switch (damageType)
            {
                case ESkillTag.skilltag_physical:
                    mods.Add(EMod.mod_physical_resistance_penetration);
                    break;
                case ESkillTag.skilltag_fire:
                    mods.Add(EMod.mod_fire_resistance_penetration);
                    mods.Add(EMod.mod_elemental_resistance_penetration);
                    break;
                case ESkillTag.skilltag_cold:
                    mods.Add(EMod.mod_cold_resistance_penetration);
                    mods.Add(EMod.mod_elemental_resistance_penetration);
                    break;
                case ESkillTag.skilltag_lightning:
                    mods.Add(EMod.mod_lightning_resistance_penetration);
                    mods.Add(EMod.mod_elemental_resistance_penetration);
                    break;
                case ESkillTag.skilltag_poison:
                    mods.Add(EMod.mod_poison_resistance_penetration);
                    mods.Add(EMod.mod_elemental_resistance_penetration);
                    break;
            }

            return mods;
        }

        /// <summary>
        /// 저항 감소 MOD 목록 가져오기
        /// </summary>
        private static List<EMod> GetResistanceReductionMods(ESkillTag damageType)
        {
            var mods = new List<EMod>();

            switch (damageType)
            {
                case ESkillTag.skilltag_physical:
                    mods.Add(EMod.mod_reduction_enemy_physical_resistance);
                    break;
                case ESkillTag.skilltag_fire:
                    mods.Add(EMod.mod_reduction_enemy_fire_resistance);
                    mods.Add(EMod.mod_reduction_enemy_elemental_resistance);
                    break;
                case ESkillTag.skilltag_cold:
                    mods.Add(EMod.mod_reduction_enemy_cold_resistance);
                    mods.Add(EMod.mod_reduction_enemy_elemental_resistance);
                    break;
                case ESkillTag.skilltag_lightning:
                    mods.Add(EMod.mod_reduction_enemy_lightning_resistance);
                    mods.Add(EMod.mod_reduction_enemy_elemental_resistance);
                    break;
                case ESkillTag.skilltag_poison:
                    mods.Add(EMod.mod_reduction_enemy_poison_resistance);
                    mods.Add(EMod.mod_reduction_enemy_elemental_resistance);
                    break;
            }

            return mods;
        }

        /// <summary>
        /// 방어자 저항값 가져오기
        /// </summary>
        private static double GetDefenderResistance(SimulatorDefender defender, ESkillTag damageType)
        {
            if (defender == null) return 0;

            return damageType switch
            {
                ESkillTag.skilltag_physical => defender.physicalResistance,
                ESkillTag.skilltag_fire => defender.fireResistance,
                ESkillTag.skilltag_cold => defender.coldResistance,
                ESkillTag.skilltag_lightning => defender.lightningResistance,
                ESkillTag.skilltag_poison => defender.poisonResistance,
                _ => 0
            };
        }

        #endregion
    }
}
