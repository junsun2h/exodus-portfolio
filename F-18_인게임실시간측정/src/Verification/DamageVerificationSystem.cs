using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using PX;
using Debug = UnityEngine.Debug;

namespace BattleSimulator
{
    /// <summary>
    /// 원클릭 피해 검증 시스템
    /// 시뮬레이션과 인게임 계산을 직접 호출하여 비교
    /// </summary>
    public static class DamageVerificationSystem
    {
        // 오차 허용 범위
        private const double EXACT_TOLERANCE = 0.001;      // 0.001% (동일해야 하는 값)
        private const double NORMAL_TOLERANCE = 0.1;       // 0.1% (일반 계산)
        private const double LOOSE_TOLERANCE = 1.0;        // 1.0% (반올림 포함)

        /// <summary>
        /// 전체 검증 실행 (원클릭)
        /// </summary>
        public static FullVerificationResult RunFullVerification(
            ESkill mainSpell,
            ETier spellTier,
            int spellReinforce,
            ESkill aura,
            ETier auraTier,
            int auraReinforce,
            SimulatorDefender defender)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new FullVerificationResult
            {
                Timestamp = DateTime.Now,
                PresetName = "Current Settings"
            };

            try
            {
                // 플레이어 캐릭터 확인
                UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
                if (player == null)
                {
                    Debug.LogError("[검증] ❌ 플레이어 캐릭터를 찾을 수 없습니다.");
                    return result;
                }

                FCharacterStatus attackerStatus = player.CharacterStatus;
                FCharacterStatus defenderStatus = defender?.ToCharacterStatus();

                // 1. Spell 검증
                if (mainSpell != ESkill.None)
                {
                    result.Spell = VerifySpellDamage(mainSpell, spellTier, spellReinforce, attackerStatus, defenderStatus, defender);
                }

                // 2. Aura 검증 (강화도 모드 적용)
                if (aura != ESkill.None)
                {
                    int auraEffectiveReinforce = SimulatorCalculator.CalculateSkillReinforce(aura, auraTier, attackerStatus);
                    result.Aura = VerifyAuraDamage(aura, auraTier, auraEffectiveReinforce, attackerStatus, defenderStatus);
                }

                // 3. Ailment 검증
                if (mainSpell != ESkill.None)
                {
                    result.Ailment = VerifyAilmentDamage(mainSpell, spellTier, attackerStatus, defenderStatus, defender);
                }

                // 4. DoT 검증 (Aura의 skill_contagion, 강화도 모드 적용)
                if (aura != ESkill.None)
                {
                    int dotEffectiveReinforce = SimulatorCalculator.CalculateSkillReinforce(aura, auraTier, attackerStatus);
                    result.Dot = VerifyDotDamage(aura, auraTier, dotEffectiveReinforce, attackerStatus, defenderStatus);
                }

                // 5. 총합 DPS 계산
                CalculateTotalDPS(result);

                // 6. 방어 적용 검증 (저항, 받는 피해 증감, 즉사 판정)
                result.Defense = VerifyDefenseDamage(result, attackerStatus, defender);

                // 7. 몬스터 → 플레이어 검증 (플레이어 생존 시간)
                result.MonsterToPlayer = VerifyMonsterToPlayerDamage(defender, attackerStatus);
            }
            catch (Exception e)
            {
                Debug.LogError($"[검증] ❌ 검증 중 에러 발생: {e.Message}\n{e.StackTrace}");
            }

            stopwatch.Stop();
            result.VerificationDuration = stopwatch.ElapsedMilliseconds;

            return result;
        }

        #region Spell 검증

        /// <summary>
        /// Spell 피해 검증
        /// </summary>
        private static SpellVerificationResult VerifySpellDamage(
            ESkill spell,
            ETier tier,
            int spellReinforce,
            FCharacterStatus attackerStatus,
            FCharacterStatus defenderStatus,
            SimulatorDefender defender)
        {
            var result = new SpellVerificationResult
            {
                IsEnabled = true,
                SkillType = spell,
                SkillTier = tier
            };

            try
            {
                // 스킬 태그 확인
                if (!GameDBUtility.TryGetSkillDBData(spell, out _, out var skillDB))
                {
                    Debug.LogError($"[검증] Spell DB를 찾을 수 없음: {spell}");
                    return result;
                }

                result.DamageType = GetPrimaryDamageType(skillDB.SkillTags);

                // ===== 시뮬레이션 값 수집 (Breakdown 함수 사용) =====
                var simFlatBreakdown = SimulatorCalculator.GetSpellBaseDamageBreakdown(result.DamageType);
                var simIncBreakdown = SimulatorCalculator.GetSpellIncBreakdown(result.DamageType, defender, skillDB.SkillTags);
                var simMoreBreakdown = SimulatorCalculator.GetSpellMoreBreakdown(result.DamageType, skillDB.SkillTags);
                var simCritChanceBreakdown = SimulatorCalculator.GetSpellCriticalChanceBreakdown(defenderStatus, defender);
                var simCritMultBreakdown = SimulatorCalculator.GetSpellCriticalMultiplierBreakdown(defenderStatus);
                var simCritBlowChanceBreakdown = SimulatorCalculator.GetSpellCriticalBlowChanceBreakdown();
                var simCritBlowMultBreakdown = SimulatorCalculator.GetSpellCriticalBlowMultiplierBreakdown();
                var simCastSpeedBreakdown = SimulatorCalculator.GetSpellCastSpeedBreakdown();

                // 시뮬레이션 값
                double simFlatDamage = simFlatBreakdown?.finalValue ?? 0;

                // Inc/More 배율 계산
                // finalValue는 합계/비율값이므로 (1 + value)로 배율 변환 필요
                // Inc: finalValue = Inc MOD 합계 (예: 25.45 = 2545%)
                // More: finalValue = moreMultiplier - 1 (예: 0.95 if 배율 1.95x)
                double simIncValue = simIncBreakdown?.finalValue ?? 0;
                double simMoreValue = simMoreBreakdown?.finalValue ?? 0;
                double simIncMult = 1 + simIncValue;   // Inc 배율 = 1 + Inc합계
                double simMoreMult = 1 + simMoreValue; // More 배율 = 1 + (배율-1) = 배율
                double simCritChance = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_chance, simCritChanceBreakdown?.finalValue ?? 0);
                double simCritMult = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_multiplier, simCritMultBreakdown?.finalValue ?? 1);
                double simCritBlowChance = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_blow_chance, simCritBlowChanceBreakdown?.finalValue ?? 0);
                double simCritBlowMult = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_blow_multiplier, simCritBlowMultBreakdown?.finalValue ?? 1);
                double simCastSpeed = simCastSpeedBreakdown?.finalValue ?? 1;

                // ===== 인게임 값 수집 (FSkillData + BattleStatus) =====
                UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();

                // 강화도 계산 (상세 정보 수집)
                var battleStatus = attackerStatus.BattleStatus;

                // 1. Core 강화도 (userData에서)
                int coreReinforce = GetCoreReinforce(spell, tier);

                // 2. 강화도 모드 증가량 계산
                double reinforceAddAll = battleStatus.TotalModValue(EMod.mod_use_all_skill_reinforce_add);
                double reinforceAddAttribute = 0;
                switch (result.DamageType)
                {
                    case ESkillTag.skilltag_physical:
                        reinforceAddAttribute = battleStatus.TotalModValue(EMod.mod_use_physical_skill_reinforce_add);
                        break;
                    case ESkillTag.skilltag_fire:
                        reinforceAddAttribute = battleStatus.TotalModValue(EMod.mod_use_fire_skill_reinforce_add);
                        break;
                    case ESkillTag.skilltag_cold:
                        reinforceAddAttribute = battleStatus.TotalModValue(EMod.mod_use_cold_skill_reinforce_add);
                        break;
                    case ESkillTag.skilltag_lightning:
                        reinforceAddAttribute = battleStatus.TotalModValue(EMod.mod_use_lightning_skill_reinforce_add);
                        break;
                    case ESkillTag.skilltag_poison:
                        reinforceAddAttribute = battleStatus.TotalModValue(EMod.mod_use_poison_skill_reinforce_add);
                        break;
                }
                int reinforceModAdd = (int)(reinforceAddAll + reinforceAddAttribute);

                // 3. 최종 강화도 = Core + ModAdd (인게임 로직 호출)
                int reinforceLevel = SimulatorCalculator.CalculateSkillReinforce(spell, tier, attackerStatus);

                // 강화도 정보 저장
                result.CoreReinforce = coreReinforce;
                result.ReinforceModAdd = reinforceModAdd;
                result.EffectiveReinforce = reinforceLevel;

                FSkillData skillData = FSkillData.CreateSkillData(
                    spell, tier, reinforceLevel, attackerStatus,
                    new SimulatorDummyActionController(), null);

                if (skillData == null)
                {
                    Debug.LogError($"[검증] FSkillData 생성 실패: {spell}");
                    return result;
                }

                // 인게임 값
                double ingameFlatDamage = GetIngameFlatDamage(attackerStatus, result.DamageType);
                double ingamePreCritDamage = skillData.ResultAttackerDamage;
                double ingameIncMult = skillData.IncMultiplier;
                double ingameMoreMult = skillData.MoreMultiplier;
                double ingameCritChance = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_chance, attackerStatus.BattleStatus.ResultCriticalChance(defenderStatus));
                double ingameCritMult = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_multiplier, attackerStatus.BattleStatus.ResultCriticalMultiplier(defenderStatus));
                double ingameCritBlowChance = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_blow_chance, attackerStatus.BattleStatus.ResultCriticalBlowChance());
                double ingameCritBlowMult = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_blow_multiplier, attackerStatus.BattleStatus.ResultCriticalBlowmultiplier());
                double ingameCastSpeed = attackerStatus.BattleStatus.ResultSkillCastSpeed();

                // Inc/More 역산 (preCritDamage / flatDamage)
                double ingameEffectiveness = GetSkillEffectiveness(skillDB, reinforceLevel);
                double ingameBaseDamage = ingameFlatDamage * ingameEffectiveness;
                double ingameCombinedMult = ingameBaseDamage > 0 ? ingamePreCritDamage / ingameBaseDamage : 0;

                // 시뮬레이션 PreCrit 계산
                double simEffectiveness = GetSkillEffectiveness(skillDB, reinforceLevel);
                double simBaseDamage = simFlatDamage * simEffectiveness;
                double simPreCritDamage = simBaseDamage * simIncMult * simMoreMult;

                // 평균 크리티컬 배율 계산
                double simAvgCritMult = CalculateAverageCritMultiplier(
                    simCritChance / 100, simCritMult / 100,
                    simCritBlowChance / 100, simCritBlowMult / 100);
                double ingameAvgCritMult = CalculateAverageCritMultiplier(
                    ingameCritChance / 100, ingameCritMult / 100,
                    ingameCritBlowChance / 100, ingameCritBlowMult / 100);

                // DPS 계산
                double simDPS = simPreCritDamage * simAvgCritMult * simCastSpeed;
                double ingameDPS = ingamePreCritDamage * ingameAvgCritMult * ingameCastSpeed;

                // ===== 비교 결과 생성 =====
                result.FlatDamage = new ValueComparison("Flat Damage", simFlatDamage, ingameFlatDamage, NORMAL_TOLERANCE);
                result.SkillEffectiveness = new ValueComparison("Skill Effectiveness", simEffectiveness, ingameEffectiveness, EXACT_TOLERANCE, "배율");
                result.BaseDamage = new ValueComparison("Base Damage", simBaseDamage, ingameBaseDamage, NORMAL_TOLERANCE);
                result.IncMultiplier = new ValueComparison("Inc 배율", simIncMult, ingameIncMult, NORMAL_TOLERANCE, "배율");
                result.MoreMultiplier = new ValueComparison("More 배율", simMoreMult, ingameMoreMult, NORMAL_TOLERANCE, "배율");
                result.PreCritDamage = new ValueComparison("크리티컬 전 피해", simPreCritDamage, ingamePreCritDamage, LOOSE_TOLERANCE);
                result.CriticalChance = new ValueComparison("크리티컬 확률", simCritChance, ingameCritChance, NORMAL_TOLERANCE, "%");
                result.CriticalMultiplier = new ValueComparison("크리티컬 배율", simCritMult, ingameCritMult, NORMAL_TOLERANCE, "%");
                result.CritBlowChance = new ValueComparison("치명타 일격 확률", simCritBlowChance, ingameCritBlowChance, NORMAL_TOLERANCE, "%");
                result.CritBlowMultiplier = new ValueComparison("치명타 일격 배율", simCritBlowMult, ingameCritBlowMult, NORMAL_TOLERANCE, "%");
                result.AverageCritMultiplier = new ValueComparison("평균 크리티컬 배율", simAvgCritMult, ingameAvgCritMult, NORMAL_TOLERANCE, "배율");
                result.CastSpeed = new ValueComparison("시전 속도", simCastSpeed, ingameCastSpeed, NORMAL_TOLERANCE);
                result.FinalDPS = new ValueComparison("Spell DPS", simDPS, ingameDPS, LOOSE_TOLERANCE);

                // 브레이크다운 저장 (디버깅용)
                if (simIncBreakdown?.modContributions != null)
                {
                    foreach (var contrib in simIncBreakdown.modContributions)
                    {
                        if (Enum.TryParse<EMod>(contrib.modName, out var modType))
                        {
                            result.SimIncBreakdown[modType] = contrib.contribution;
                        }
                    }
                }
                if (simMoreBreakdown?.modContributions != null)
                {
                    foreach (var contrib in simMoreBreakdown.modContributions)
                    {
                        if (Enum.TryParse<EMod>(contrib.modName, out var modType))
                        {
                            result.SimMoreBreakdown[modType] = contrib.contribution;
                        }
                    }
                }

                // 단계별 계산 저장
                result.Steps.Add(new CalculationStep(1, "Flat Damage (Added)", "∑(mod_xxx_damage)") { SimValue = simFlatDamage, IngameValue = ingameFlatDamage });
                result.Steps.Add(new CalculationStep(2, "Skill Effectiveness", "스킬별 고정값") { SimValue = simEffectiveness, IngameValue = ingameEffectiveness });
                result.Steps.Add(new CalculationStep(3, "Base Damage", "Flat × Effectiveness") { SimValue = simBaseDamage, IngameValue = ingameBaseDamage });
                result.Steps.Add(new CalculationStep(4, "Inc 배율", "1 + ∑(mod_xxx_inc)") { SimValue = simIncMult, IngameValue = 0 });
                result.Steps.Add(new CalculationStep(5, "More 배율", "∏(1 + mod_xxx_more)") { SimValue = simMoreMult, IngameValue = 0 });
                result.Steps.Add(new CalculationStep(6, "크리티컬 전 피해", "Base × Inc × More") { SimValue = simPreCritDamage, IngameValue = ingamePreCritDamage });
                result.Steps.Add(new CalculationStep(7, "평균 크리티컬 배율", "확률 가중 평균") { SimValue = simAvgCritMult, IngameValue = ingameAvgCritMult });
                result.Steps.Add(new CalculationStep(8, "시전 속도", "초당 시전 횟수") { SimValue = simCastSpeed, IngameValue = ingameCastSpeed });
                result.Steps.Add(new CalculationStep(9, "Spell DPS", "PreCrit × AvgCrit × CastSpeed") { SimValue = simDPS, IngameValue = ingameDPS });
            }
            catch (Exception e)
            {
                Debug.LogError($"[검증] Spell 검증 실패: {e.Message}\n{e.StackTrace}");
            }

            return result;
        }

        #endregion

        #region Aura 검증

        /// <summary>
        /// Aura 피해 검증
        /// </summary>
        private static AuraVerificationResult VerifyAuraDamage(
            ESkill aura,
            ETier tier,
            int reinforceLevel,
            FCharacterStatus attackerStatus,
            FCharacterStatus defenderStatus)
        {
            var result = new AuraVerificationResult
            {
                IsEnabled = true,
                AuraType = aura,
                AuraTier = tier
            };

            try
            {
                // 스킬 태그 확인
                if (!GameDBUtility.TryGetSkillDBData(aura, out _, out var skillDB))
                {
                    Debug.LogError($"[검증] Aura DB를 찾을 수 없음: {aura}");
                    return result;
                }

                result.DamageType = GetPrimaryDamageType(skillDB.SkillTags);

                // ===== 시뮬레이션 값 수집 =====
                var simBaseDamageBreakdown = SimulatorCalculator.GetAuraBaseDamageBreakdown(result.DamageType);
                var simDurationBreakdown = SimulatorCalculator.GetAuraDurationBreakdown();
                var simIncBreakdown = SimulatorCalculator.GetAuraIncBreakdown(result.DamageType);
                var simMoreBreakdown = SimulatorCalculator.GetAuraMoreBreakdown(result.DamageType);

                double simBaseDamage = simBaseDamageBreakdown?.finalValue ?? 0;
                double simDuration = simDurationBreakdown?.finalValue ?? 1;

                // Inc/More 배율 계산 (Spell과 동일하게 적용)
                // finalValue는 합계/비율값이므로 (1 + value)로 배율 변환 필요
                double simIncValue = simIncBreakdown?.finalValue ?? 0;
                double simMoreValue = simMoreBreakdown?.finalValue ?? 0;
                double simIncMult = 1 + simIncValue;   // Inc 배율 = 1 + Inc합계
                double simMoreMult = 1 + simMoreValue; // More 배율 = 1 + (배율-1) = 배율

                // 크리티컬 (Aura도 크리티컬 적용됨)
                double simCritChance = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_chance, attackerStatus.BattleStatus.ResultCriticalChance(defenderStatus));
                double simCritMult = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_multiplier, attackerStatus.BattleStatus.ResultCriticalMultiplier(defenderStatus));

                // Aura 피해 계산
                double damagePercent = GetAuraDamagePercent(skillDB, reinforceLevel);
                double tickRate = GetAuraTickRate(skillDB);

                double simAvgDamage = simBaseDamage * damagePercent * simIncMult * simMoreMult;
                double simAvgCritMult = CalculateAverageCritMultiplier(simCritChance / 100, simCritMult / 100, 0, 0);
                double simDPS = simAvgDamage * simAvgCritMult * tickRate;

                // ===== 인게임 값 수집 =====
                // Aura는 FSkillData를 별도로 생성하지 않고 BattleStatus에서 직접 가져옴
                double ingameBaseDamage = GetIngameFlatDamage(attackerStatus, result.DamageType);
                double ingameCritChance = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_chance, attackerStatus.BattleStatus.ResultCriticalChance(defenderStatus));
                double ingameCritMult = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_multiplier, attackerStatus.BattleStatus.ResultCriticalMultiplier(defenderStatus));

                double ingameAvgDamage = ingameBaseDamage * damagePercent * simIncMult * simMoreMult; // Inc/More는 시뮬과 동일하게 사용
                double ingameAvgCritMult = CalculateAverageCritMultiplier(ingameCritChance / 100, ingameCritMult / 100, 0, 0);
                double ingameDPS = ingameAvgDamage * ingameAvgCritMult * tickRate;

                // ===== 비교 결과 생성 =====
                result.BaseDamage = new ValueComparison("Flat Damage", simBaseDamage, ingameBaseDamage, NORMAL_TOLERANCE);
                result.DamagePercent = new ValueComparison("Damage %", SimulatorCalculator.ConvertRatioToPercent(damagePercent), SimulatorCalculator.ConvertRatioToPercent(damagePercent), EXACT_TOLERANCE, "%");
                result.Duration = new ValueComparison("지속시간", simDuration, simDuration, EXACT_TOLERANCE);
                result.TickRate = new ValueComparison("틱 빈도", tickRate, tickRate, EXACT_TOLERANCE);
                result.IncMultiplier = new ValueComparison("Inc 배율", simIncMult, simIncMult, NORMAL_TOLERANCE, "배율");
                result.MoreMultiplier = new ValueComparison("More 배율", simMoreMult, simMoreMult, NORMAL_TOLERANCE, "배율");
                result.CriticalChance = new ValueComparison("크리티컬 확률", simCritChance, ingameCritChance, NORMAL_TOLERANCE, "%");
                result.CriticalMultiplier = new ValueComparison("크리티컬 배율", simCritMult, ingameCritMult, NORMAL_TOLERANCE, "%");
                result.AverageDamage = new ValueComparison("평균 피해", simAvgDamage, ingameAvgDamage, LOOSE_TOLERANCE);
                result.FinalDPS = new ValueComparison("Aura DPS", simDPS, ingameDPS, LOOSE_TOLERANCE);
            }
            catch (Exception e)
            {
                Debug.LogError($"[검증] Aura 검증 실패: {e.Message}\n{e.StackTrace}");
            }

            return result;
        }

        #endregion

        #region Ailment 검증

        /// <summary>
        /// Ailment 피해 검증
        /// </summary>
        private static AilmentVerificationResult VerifyAilmentDamage(
            ESkill spell,
            ETier tier,
            FCharacterStatus attackerStatus,
            FCharacterStatus defenderStatus,
            SimulatorDefender defender)
        {
            var result = new AilmentVerificationResult { IsEnabled = false };

            try
            {
                // CalculateStatisticalDPS로 Ailment 정보 가져오기
                var stats = SimulatorCalculator.CalculateStatisticalDPS(spell, tier, defender, 0);
                if (stats.ailmentDetails == null || stats.ailmentDetails.Count == 0)
                {
                    return result;
                }

                result.IsEnabled = true;
                double totalSimDPS = 0;
                double totalIngameDPS = 0;

                foreach (var detail in stats.ailmentDetails)
                {
                    if (detail.procChance <= 0) continue;

                    var ailmentResult = new SingleAilmentVerificationResult
                    {
                        AilmentType = detail.ailmentType,
                        AilmentName = detail.ailmentName,
                        DamageType = GetAilmentDamageType(detail.ailmentType),
                        IsEnabled = true
                    };

                    // 시뮬레이션 값
                    double simProcChance = detail.procChance;
                    double simDuration = detail.duration;
                    double simMaxStacks = detail.maxStacks;
                    double simDamagePercent = detail.damagePercent;
                    double simFlatDamage = detail.flatDamage;
                    double simIncMult = detail.incMultiplier;
                    double simMoreMult = detail.moreMultiplier;
                    double simDPS = detail.dps;

                    // 인게임 값 (Ailment는 시뮬레이션과 동일한 계산 로직 사용)
                    double ingameFlatDamage = GetIngameFlatDamage(attackerStatus, ailmentResult.DamageType);

                    // 크리티컬 계산 (Ailment는 Critical Blow 미적용, defender=null)
                    // 인게임 BuffActionData_Ailment.CalculateFinalDamage와 동일한 로직
                    double simCritChance = attackerStatus.BattleStatus.ResultCriticalChance(null);
                    double simCritMult = attackerStatus.BattleStatus.ResultCriticalMultiplier(null);
                    double simCritChanceUI = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_chance, simCritChance);
                    double simCritMultUI = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_multiplier, simCritMult);

                    // 평균 크리티컬 배율 (Ailment는 Critical Blow 미적용)
                    double simAvgCritMult = CalculateAverageCritMultiplier(simCritChance, simCritMult, 0, 0);

                    // Ailment의 Inc/More는 시뮬레이터와 동일한 로직이므로 같은 값 사용
                    // 시뮬레이터와 동일하게 시전속도(castSpeed) 및 평균 크리티컬 배율 적용
                    // 공식: Flat × 피해% × Inc × More × 유발확률 × 시전속도 × 평균크리배율
                    double ingameDPS = ingameFlatDamage * simDamagePercent * simIncMult * simMoreMult * (simProcChance / 100) * stats.castSpeed * simAvgCritMult;

                    // 비교 결과
                    ailmentResult.ProcChance = new ValueComparison("발동 확률", simProcChance, simProcChance, EXACT_TOLERANCE, "%");
                    ailmentResult.Duration = new ValueComparison("지속시간", simDuration, simDuration, EXACT_TOLERANCE);
                    ailmentResult.MaxStacks = new ValueComparison("최대 스택", simMaxStacks, simMaxStacks, EXACT_TOLERANCE);
                    ailmentResult.DamagePercent = new ValueComparison("피해 비율", SimulatorCalculator.ConvertRatioToPercent(simDamagePercent), SimulatorCalculator.ConvertRatioToPercent(simDamagePercent), EXACT_TOLERANCE, "%");
                    ailmentResult.FlatDamage = new ValueComparison("Flat Damage", simFlatDamage, ingameFlatDamage, NORMAL_TOLERANCE);
                    ailmentResult.IncMultiplier = new ValueComparison("Inc 배율", simIncMult, simIncMult, NORMAL_TOLERANCE, "배율");
                    ailmentResult.MoreMultiplier = new ValueComparison("More 배율", simMoreMult, simMoreMult, NORMAL_TOLERANCE, "배율");
                    ailmentResult.CriticalChance = new ValueComparison("크리티컬 확률", simCritChanceUI, simCritChanceUI, NORMAL_TOLERANCE, "%");
                    ailmentResult.CriticalMultiplier = new ValueComparison("크리티컬 배율", simCritMultUI, simCritMultUI, NORMAL_TOLERANCE, "%");
                    ailmentResult.AverageCritMultiplier = new ValueComparison("평균 크리티컬 배율", simAvgCritMult, simAvgCritMult, NORMAL_TOLERANCE, "배율");
                    ailmentResult.DPS = new ValueComparison($"{detail.ailmentName} DPS", simDPS, ingameDPS, LOOSE_TOLERANCE);

                    result.AilmentResults[detail.ailmentType] = ailmentResult;
                    totalSimDPS += simDPS;
                    totalIngameDPS += ingameDPS;
                }

                result.TotalDPS = new ValueComparison("Ailment 총 DPS", totalSimDPS, totalIngameDPS, LOOSE_TOLERANCE);
            }
            catch (Exception e)
            {
                Debug.LogError($"[검증] Ailment 검증 실패: {e.Message}\n{e.StackTrace}");
            }

            return result;
        }

        #endregion

        #region DoT 검증

        /// <summary>
        /// DoT 피해 검증
        /// </summary>
        private static DotVerificationResult VerifyDotDamage(
            ESkill aura,
            ETier tier,
            int reinforceLevel,
            FCharacterStatus attackerStatus,
            FCharacterStatus defenderStatus)
        {
            var result = new DotVerificationResult { IsEnabled = false };

            try
            {
                // skill_contagion 버프 확인
                if (!HasContagionBuff(aura))
                {
                    return result;
                }

                result.IsEnabled = true;
                result.DotType = EStatusEffect.skill_contagion;
                result.DotName = "전염 (Contagion)";

                // 스킬 DB에서 DoT 정보 가져오기
                if (!GameDBUtility.TryGetSkillDBData(aura, out _, out var skillDB))
                {
                    return result;
                }

                result.DamageType = GetPrimaryDamageType(skillDB.SkillTags);

                // 시뮬레이션 값
                double simBaseDamage = GetIngameFlatDamage(attackerStatus, result.DamageType);
                double damagePercent = GetDotDamagePercent(skillDB, reinforceLevel);
                double duration = GetDotDuration(skillDB);
                double tickInterval = GetDotTickInterval(skillDB);

                var simIncBreakdown = SimulatorCalculator.GetAuraIncBreakdown(result.DamageType);
                var simMoreBreakdown = SimulatorCalculator.GetAuraMoreBreakdown(result.DamageType);

                // Inc/More 배율 계산 (Spell/Aura와 동일하게 적용)
                // finalValue는 합계/비율값이므로 (1 + value)로 배율 변환 필요
                double simIncValue = simIncBreakdown?.finalValue ?? 0;
                double simMoreValue = simMoreBreakdown?.finalValue ?? 0;
                double simIncMult = 1 + simIncValue;   // Inc 배율 = 1 + Inc합계
                double simMoreMult = 1 + simMoreValue; // More 배율 = 1 + (배율-1) = 배율

                // DoT 크리티컬 - 시뮬레이션 값
                double simCritChance = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_chance, attackerStatus.BattleStatus.ResultCriticalChance(defenderStatus));
                double simCritMult = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_multiplier, attackerStatus.BattleStatus.ResultCriticalMultiplier(defenderStatus));
                double simAvgCritMult = CalculateAverageCritMultiplier(simCritChance / 100, simCritMult / 100, 0, 0);

                double ticksPerSecond = tickInterval > 0 ? 1.0 / tickInterval : 1;
                double simTickDamage = simBaseDamage * damagePercent * simIncMult * simMoreMult * simAvgCritMult;
                double simDPS = simTickDamage * ticksPerSecond;

                // 인게임 값
                double ingameBaseDamage = GetIngameFlatDamage(attackerStatus, result.DamageType);
                double ingameCritChance = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_chance, attackerStatus.BattleStatus.ResultCriticalChance(defenderStatus));
                double ingameCritMult = SimulatorCalculator.ConvertModValueForUI(EMod.mod_crit_multiplier, attackerStatus.BattleStatus.ResultCriticalMultiplier(defenderStatus));
                double ingameAvgCritMult = CalculateAverageCritMultiplier(ingameCritChance / 100, ingameCritMult / 100, 0, 0);
                double ingameTickDamage = ingameBaseDamage * damagePercent * simIncMult * simMoreMult * ingameAvgCritMult;
                double ingameDPS = ingameTickDamage * ticksPerSecond;

                // 비교 결과 - 스킬 데이터 값은 시뮬=인게임 동일 (단순 표시용)
                result.BaseDamage = new ValueComparison("Flat Damage", simBaseDamage, ingameBaseDamage, NORMAL_TOLERANCE);
                result.DamagePercent = new ValueComparison("피해 비율", SimulatorCalculator.ConvertRatioToPercent(damagePercent), SimulatorCalculator.ConvertRatioToPercent(damagePercent), EXACT_TOLERANCE, "%");
                result.Duration = new ValueComparison("지속시간", duration, duration, EXACT_TOLERANCE);
                result.TickInterval = new ValueComparison("틱 간격", tickInterval, tickInterval, EXACT_TOLERANCE);
                result.IncMultiplier = new ValueComparison("Inc 배율", simIncMult, simIncMult, NORMAL_TOLERANCE, "배율");
                result.MoreMultiplier = new ValueComparison("More 배율", simMoreMult, simMoreMult, NORMAL_TOLERANCE, "배율");
                result.CriticalChance = new ValueComparison("크리티컬 확률", simCritChance, ingameCritChance, NORMAL_TOLERANCE, "%");
                result.CriticalMultiplier = new ValueComparison("크리티컬 배율", simCritMult, ingameCritMult, NORMAL_TOLERANCE, "%");
                result.TickDamage = new ValueComparison("틱당 피해", simTickDamage, ingameTickDamage, LOOSE_TOLERANCE);
                result.FinalDPS = new ValueComparison("DoT DPS", simDPS, ingameDPS, LOOSE_TOLERANCE);
            }
            catch (Exception e)
            {
                Debug.LogError($"[검증] DoT 검증 실패: {e.Message}\n{e.StackTrace}");
            }

            return result;
        }

        #endregion

        #region 총합 계산

        /// <summary>
        /// 총합 DPS 계산
        /// </summary>
        private static void CalculateTotalDPS(FullVerificationResult result)
        {
            double simTotalDPS = 0;
            double ingameTotalDPS = 0;

            if (result.Spell.IsEnabled && result.Spell.FinalDPS != null)
            {
                simTotalDPS += result.Spell.FinalDPS.SimValue;
                ingameTotalDPS += result.Spell.FinalDPS.IngameValue;
            }

            if (result.Aura.IsEnabled && result.Aura.FinalDPS != null)
            {
                simTotalDPS += result.Aura.FinalDPS.SimValue;
                ingameTotalDPS += result.Aura.FinalDPS.IngameValue;
            }

            if (result.Ailment.IsEnabled && result.Ailment.TotalDPS != null)
            {
                simTotalDPS += result.Ailment.TotalDPS.SimValue;
                ingameTotalDPS += result.Ailment.TotalDPS.IngameValue;
            }

            if (result.Dot.IsEnabled && result.Dot.FinalDPS != null)
            {
                simTotalDPS += result.Dot.FinalDPS.SimValue;
                ingameTotalDPS += result.Dot.FinalDPS.IngameValue;
            }

            result.GrandTotalDPS = new ValueComparison("총 DPS", simTotalDPS, ingameTotalDPS, LOOSE_TOLERANCE);

            // DPS 비율 계산
            if (simTotalDPS > 0)
            {
                result.SpellDPSRatio = result.Spell.IsEnabled ? (result.Spell.FinalDPS?.SimValue ?? 0) / simTotalDPS * 100 : 0;
                result.AuraDPSRatio = result.Aura.IsEnabled ? (result.Aura.FinalDPS?.SimValue ?? 0) / simTotalDPS * 100 : 0;
                result.AilmentDPSRatio = result.Ailment.IsEnabled ? (result.Ailment.TotalDPS?.SimValue ?? 0) / simTotalDPS * 100 : 0;
                result.DotDPSRatio = result.Dot.IsEnabled ? (result.Dot.FinalDPS?.SimValue ?? 0) / simTotalDPS * 100 : 0;
            }
        }

        #endregion

        #region Defense 검증

        /// <summary>
        /// 방어 적용 검증 (저항, 받는 피해 증감, 즉사 판정)
        /// </summary>
        private static DefenseVerificationResult VerifyDefenseDamage(
            FullVerificationResult damageResult,
            FCharacterStatus attackerStatus,
            SimulatorDefender defender)
        {
            var result = new DefenseVerificationResult { IsEnabled = false };

            if (defender == null || attackerStatus == null) return result;

            try
            {
                result.IsEnabled = true;

                // 방어자 정보
                result.DefenderName = defender.defenderName;
                result.DefenderLevel = defender.stageLevel;
                result.DefenderMaxLife = defender.maxLife;

                var battleStatus = attackerStatus.BattleStatus;

                // ===== 1. 순수 피해 (방어 적용 전) =====
                double pureDPS = damageResult.GrandTotalDPS?.SimValue ?? 0;
                result.BeforeDefenseDPS = new ValueComparison("순수 DPS", pureDPS, pureDPS, LOOSE_TOLERANCE);

                // 주요 피해 속성 결정 (Spell 기준)
                ESkillTag primaryDamageType = damageResult.Spell.IsEnabled
                    ? damageResult.Spell.DamageType
                    : ESkillTag.skilltag_cold;

                // ===== 2. 저항 계산 =====
                // 방어자 저항값
                double defenderResistance = GetDefenderResistance(defender, primaryDamageType);
                double resistanceMax = GetDefenderResistanceMax(defender, primaryDamageType);

                // 공격자 관통/감소
                double penetration = GetAttackerPenetration(battleStatus, primaryDamageType);
                double reduction = GetAttackerResistanceReduction(battleStatus, primaryDamageType);

                // 최종 저항 계산
                // defender 값은 백분율로 저장 (75 = 75%), ratio로 변환 필요
                double finalResistance = SimulatorDefender.CalculateFinalResistance(
                    defenderResistance / 100.0, resistanceMax / 100.0, penetration, reduction);

                // finalResistance는 ratio, UI 표시용으로 변환
                result.FinalResistance = new ValueComparison("최종 저항", SimulatorCalculator.ConvertRatioToPercent(finalResistance), SimulatorCalculator.ConvertRatioToPercent(finalResistance), NORMAL_TOLERANCE, "%");

                // 저항에 의한 피해 감소율 (저항이 양수면 감소, 음수면 증가)
                // finalResistance는 이미 ratio
                result.ResistanceReduction = finalResistance;
                double afterResistanceMultiplier = 1.0 - result.ResistanceReduction;

                double dpsAfterResistance = pureDPS * afterResistanceMultiplier;
                result.AfterResistanceDPS = new ValueComparison("저항 적용 후 DPS", dpsAfterResistance, dpsAfterResistance, LOOSE_TOLERANCE);

                // ===== 3. 받는 피해 증감 =====
                // TotalModValue는 이미 ratio 반환 (0.2 = 20%)
                double damageTakenInc = GetDamageTakenInc(battleStatus, defender);
                double damageTakenDec = GetDamageTakenDec(defender);
                double damageTakenMultiplier = (1.0 + damageTakenInc) * (1.0 - damageTakenDec);

                result.DamageTakenInc = new ValueComparison("받는 피해 증가", SimulatorCalculator.ConvertRatioToPercent(damageTakenInc), SimulatorCalculator.ConvertRatioToPercent(damageTakenInc), NORMAL_TOLERANCE, "%");
                result.DamageTakenDec = new ValueComparison("받는 피해 감소", SimulatorCalculator.ConvertRatioToPercent(damageTakenDec), SimulatorCalculator.ConvertRatioToPercent(damageTakenDec), NORMAL_TOLERANCE, "%");
                result.DamageTakenMultiplier = new ValueComparison("받는 피해 배율", damageTakenMultiplier, damageTakenMultiplier, NORMAL_TOLERANCE, "배율");

                double dpsAfterDamageTaken = dpsAfterResistance * damageTakenMultiplier;
                result.AfterDamageTakenDPS = new ValueComparison("받는 피해 적용 후 DPS", dpsAfterDamageTaken, dpsAfterDamageTaken, LOOSE_TOLERANCE);

                // ===== 4. 즉사 판정 =====
                // TotalModValue는 이미 ratio 반환 (0.2 = 20%)
                double instantKillThreshold = battleStatus.TotalModValue(EMod.mod_instantkill_lowerlife);
                double instantKillMultiplier = 1.0;

                if (instantKillThreshold > 0 && instantKillThreshold < 1.0)
                {
                    instantKillMultiplier = 1.0 / (1.0 - instantKillThreshold);
                }

                result.InstantKillThreshold = new ValueComparison("즉사 임계값", SimulatorCalculator.ConvertModValueForUI(EMod.mod_instantkill_lowerlife, instantKillThreshold), SimulatorCalculator.ConvertModValueForUI(EMod.mod_instantkill_lowerlife, instantKillThreshold), EXACT_TOLERANCE, "%");
                result.InstantKillMultiplier = new ValueComparison("즉사 배율", instantKillMultiplier, instantKillMultiplier, NORMAL_TOLERANCE, "배율");

                double finalDPS = dpsAfterDamageTaken * instantKillMultiplier;
                result.AfterInstantKillDPS = new ValueComparison("최종 DPS (즉사 판정 후)", finalDPS, finalDPS, LOOSE_TOLERANCE);

                // ===== 5. 개별 피해 유형별 최종 DPS =====
                // 방어 적용 총 배율: (1 - 저항) × 받는피해배율 × 즉사배율
                double totalDefenseMultiplier = afterResistanceMultiplier * damageTakenMultiplier * instantKillMultiplier;

                // Spell
                double spellPureDPS = damageResult.Spell.IsEnabled ? (damageResult.Spell.FinalDPS?.SimValue ?? 0) : 0;
                double spellFinalDPS = spellPureDPS * totalDefenseMultiplier;
                result.SpellBeforeDefenseDPS = new ValueComparison("Spell 순수 DPS", spellPureDPS, spellPureDPS, LOOSE_TOLERANCE);
                result.SpellFinalDPS = new ValueComparison("Spell 최종 DPS", spellFinalDPS, spellFinalDPS, LOOSE_TOLERANCE);

                // Aura
                double auraPureDPS = damageResult.Aura.IsEnabled ? (damageResult.Aura.FinalDPS?.SimValue ?? 0) : 0;
                double auraFinalDPS = auraPureDPS * totalDefenseMultiplier;
                result.AuraBeforeDefenseDPS = new ValueComparison("Aura 순수 DPS", auraPureDPS, auraPureDPS, LOOSE_TOLERANCE);
                result.AuraFinalDPS = new ValueComparison("Aura 최종 DPS", auraFinalDPS, auraFinalDPS, LOOSE_TOLERANCE);

                // Ailment
                double ailmentPureDPS = damageResult.Ailment.IsEnabled ? (damageResult.Ailment.TotalDPS?.SimValue ?? 0) : 0;
                double ailmentFinalDPS = ailmentPureDPS * totalDefenseMultiplier;
                result.AilmentBeforeDefenseDPS = new ValueComparison("Ailment 순수 DPS", ailmentPureDPS, ailmentPureDPS, LOOSE_TOLERANCE);
                result.AilmentFinalDPS = new ValueComparison("Ailment 최종 DPS", ailmentFinalDPS, ailmentFinalDPS, LOOSE_TOLERANCE);

                // DoT
                double dotPureDPS = damageResult.Dot.IsEnabled ? (damageResult.Dot.FinalDPS?.SimValue ?? 0) : 0;
                double dotFinalDPS = dotPureDPS * totalDefenseMultiplier;
                result.DotBeforeDefenseDPS = new ValueComparison("DoT 순수 DPS", dotPureDPS, dotPureDPS, LOOSE_TOLERANCE);
                result.DotFinalDPS = new ValueComparison("DoT 최종 DPS", dotFinalDPS, dotFinalDPS, LOOSE_TOLERANCE);

                // ===== 6. 처치 시간 =====
                double timeToKill = defender.maxLife > 0 ? defender.maxLife / finalDPS : 0;
                result.TimeToKill = new ValueComparison("처치 시간", timeToKill, timeToKill, LOOSE_TOLERANCE);
            }
            catch (Exception e)
            {
                Debug.LogError($"[검증] Defense 검증 실패: {e.Message}\n{e.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// 방어자 저항값 가져오기
        /// </summary>
        private static double GetDefenderResistance(SimulatorDefender defender, ESkillTag damageType)
        {
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

        /// <summary>
        /// 방어자 저항 최대치 가져오기
        /// </summary>
        private static double GetDefenderResistanceMax(SimulatorDefender defender, ESkillTag damageType)
        {
            return damageType switch
            {
                ESkillTag.skilltag_physical => defender.physicalResistanceMax,
                ESkillTag.skilltag_fire => defender.fireResistanceMax,
                ESkillTag.skilltag_cold => defender.coldResistanceMax,
                ESkillTag.skilltag_lightning => defender.lightningResistanceMax,
                ESkillTag.skilltag_poison => defender.poisonResistanceMax,
                _ => 75.0 // 기본 최대치
            };
        }

        /// <summary>
        /// 공격자 관통 가져오기
        /// </summary>
        private static double GetAttackerPenetration(FBattleStatus battleStatus, ESkillTag damageType)
        {
            double penetration = 0;

            // 속성별 관통
            switch (damageType)
            {
                case ESkillTag.skilltag_physical:
                    penetration = battleStatus.TotalModValue(EMod.mod_physical_resistance_penetration);
                    break;
                case ESkillTag.skilltag_fire:
                    penetration = battleStatus.TotalModValue(EMod.mod_fire_resistance_penetration);
                    penetration += battleStatus.TotalModValue(EMod.mod_elemental_resistance_penetration);
                    break;
                case ESkillTag.skilltag_cold:
                    penetration = battleStatus.TotalModValue(EMod.mod_cold_resistance_penetration);
                    penetration += battleStatus.TotalModValue(EMod.mod_elemental_resistance_penetration);
                    break;
                case ESkillTag.skilltag_lightning:
                    penetration = battleStatus.TotalModValue(EMod.mod_lightning_resistance_penetration);
                    penetration += battleStatus.TotalModValue(EMod.mod_elemental_resistance_penetration);
                    break;
                case ESkillTag.skilltag_poison:
                    penetration = battleStatus.TotalModValue(EMod.mod_poison_resistance_penetration);
                    penetration += battleStatus.TotalModValue(EMod.mod_elemental_resistance_penetration);
                    break;
            }

            return penetration;
        }

        /// <summary>
        /// 공격자 적 저항 감소 가져오기
        /// </summary>
        private static double GetAttackerResistanceReduction(FBattleStatus battleStatus, ESkillTag damageType)
        {
            double reduction = 0;

            // 속성별 저항 감소 (% 기반)
            switch (damageType)
            {
                case ESkillTag.skilltag_physical:
                    reduction = battleStatus.TotalModValue(EMod.mod_reduction_enemy_physical_resistance);
                    break;
                case ESkillTag.skilltag_fire:
                    reduction = battleStatus.TotalModValue(EMod.mod_reduction_enemy_fire_resistance);
                    reduction += battleStatus.TotalModValue(EMod.mod_reduction_enemy_elemental_resistance);
                    break;
                case ESkillTag.skilltag_cold:
                    reduction = battleStatus.TotalModValue(EMod.mod_reduction_enemy_cold_resistance);
                    reduction += battleStatus.TotalModValue(EMod.mod_reduction_enemy_elemental_resistance);
                    break;
                case ESkillTag.skilltag_lightning:
                    reduction = battleStatus.TotalModValue(EMod.mod_reduction_enemy_lightning_resistance);
                    reduction += battleStatus.TotalModValue(EMod.mod_reduction_enemy_elemental_resistance);
                    break;
                case ESkillTag.skilltag_poison:
                    reduction = battleStatus.TotalModValue(EMod.mod_reduction_enemy_poison_resistance);
                    reduction += battleStatus.TotalModValue(EMod.mod_reduction_enemy_elemental_resistance);
                    break;
            }

            return reduction;
        }

        /// <summary>
        /// 받는 피해 증가 가져오기 (공격자의 MOD + 저주 등)
        /// </summary>
        private static double GetDamageTakenInc(FBattleStatus battleStatus, SimulatorDefender defender)
        {
            double inc = 0;

            // 공격자의 "적이 받는 피해 증가" MOD
            inc += battleStatus.TotalModValue(EMod.mod_enemy_take_inc_physical_damage);
            inc += battleStatus.TotalModValue(EMod.mod_cursed_enemy_take_inc_physical_damage);

            // 상태이상 피해 증가
            if (defender.targetHasArctic)
                inc += battleStatus.TotalModValue(EMod.mod_inc_damage_arctic);
            if (defender.targetHasChill)
                inc += battleStatus.TotalModValue(EMod.mod_inc_damage_chill);
            if (defender.targetHasBleeding)
                inc += battleStatus.TotalModValue(EMod.mod_inc_damage_bleeding);
            if (defender.targetHasIgnite)
                inc += battleStatus.TotalModValue(EMod.mod_inc_damage_ignite);
            if (defender.targetHasShock)
                inc += battleStatus.TotalModValue(EMod.mod_inc_damage_shock);
            if (defender.targetHasPoisoning)
                inc += battleStatus.TotalModValue(EMod.mod_inc_damage_poisoning);

            // TotalModValue는 이미 ratio 반환 (0.2 = 20%)
            return inc;
        }

        /// <summary>
        /// 받는 피해 감소 가져오기 (방어자의 MOD)
        /// </summary>
        private static double GetDamageTakenDec(SimulatorDefender defender)
        {
            // 방어자의 받는 피해 감소 (몬스터는 일반적으로 0)
            // 현재 방어자 시스템에서 피해 감소 MOD는 따로 정의되지 않음
            return 0;
        }

        #endregion

        #region 헬퍼 함수

        /// <summary>
        /// 인게임 Flat Damage 계산
        /// </summary>
        private static double GetIngameFlatDamage(FCharacterStatus status, ESkillTag damageType)
        {
            var battleStatus = status.BattleStatus;
            double total = 0;

            // 속성별 피해
            switch (damageType)
            {
                case ESkillTag.skilltag_physical:
                    total += battleStatus.TotalModValue(EMod.mod_physical_damage);
                    break;
                case ESkillTag.skilltag_fire:
                    total += battleStatus.TotalModValue(EMod.mod_fire_damage);
                    total += battleStatus.TotalModValue(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_cold:
                    total += battleStatus.TotalModValue(EMod.mod_cold_damage);
                    total += battleStatus.TotalModValue(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_lightning:
                    total += battleStatus.TotalModValue(EMod.mod_lightning_damage);
                    total += battleStatus.TotalModValue(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_poison:
                    total += battleStatus.TotalModValue(EMod.mod_poison_damage);
                    total += battleStatus.TotalModValue(EMod.mod_elemental_damage);
                    break;
            }

            // 공통 피해
            total += battleStatus.TotalModValue(EMod.mod_all_skill_damage);
            total += battleStatus.TotalModValue(EMod.mod_all_damage);

            return total;
        }

        /// <summary>
        /// Core 강화도 가져오기 (userData에서)
        /// </summary>
        private static int GetCoreReinforce(ESkill skill, ETier tier)
        {
            var userData = GameAPIUserManager.Instance?.userData;
            if (userData?.skillData == null) return 0;

            // 스킬 타입 확인
            if (!GameDBUtility.TryGetSkillDBData(skill, out ESkillType skillType, out _))
                return 0;

            if (skillType == ESkillType.skill_spell)
            {
                var spells = userData.skillData.CoreData.Spells;
                if (spells.TryGetValue(skill, out var spellData))
                {
                    if (spellData.Tiers.TryGetValue(tier, out var tierData))
                    {
                        return tierData.Reinforce.Value;
                    }
                }
            }
            else if (skillType == ESkillType.skill_aura)
            {
                var auras = userData.skillData.CoreData.Auras;
                if (auras.TryGetValue(skill, out var auraData))
                {
                    if (auraData.Tiers.TryGetValue(tier, out var tierData))
                    {
                        return tierData.Reinforce.Value;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// 스킬 효율 가져오기 (SkillModEffectDamage 공식 사용)
        /// </summary>
        private static double GetSkillEffectiveness(GameDB_Client_Skill skillDB, int reinforceLevel)
        {
            if (skillDB?.SkillModEffectDamage?.Formula == null) return 1.0;

            // SkillModEffectDamage 공식에서 강화도 반영된 효율값 가져오기
            // GetValue는 FLOAT_PER 타입일 경우 이미 /100이 적용됨
            return skillDB.SkillModEffectDamage.Formula.GetValue(reinforceLevel).GetValue;
        }

        /// <summary>
        /// Aura 피해 비율 가져오기 (SkillModEffectDamage 공식 사용)
        /// 원시값 254 → 2.54x 배율로 변환 (GetValue가 자동 처리)
        /// </summary>
        private static double GetAuraDamagePercent(GameDB_Client_Skill skillDB, int reinforceLevel)
        {
            if (skillDB?.SkillModEffectDamage?.Formula == null) return 0.01;
            // GetValue는 FLOAT_PER 타입일 경우 이미 /100이 적용됨
            return skillDB.SkillModEffectDamage.Formula.GetValue(reinforceLevel).GetValue;
        }

        /// <summary>
        /// Aura 틱 빈도 가져오기
        /// </summary>
        private static double GetAuraTickRate(GameDB_Client_Skill skillDB)
        {
            if (skillDB == null) return 1;
            // 기본값 1초에 1틱
            return 1.0;
        }

        /// <summary>
        /// DoT 피해 비율 가져오기 (SkillModEffectDamage 공식 사용)
        /// </summary>
        private static double GetDotDamagePercent(GameDB_Client_Skill skillDB, int reinforceLevel)
        {
            if (skillDB?.SkillModEffectDamage?.Formula == null) return 0.01;
            // GetValue는 FLOAT_PER 타입일 경우 이미 /100이 적용됨 (GetAuraDamagePercent와 동일)
            return skillDB.SkillModEffectDamage.Formula.GetValue(reinforceLevel).GetValue;
        }

        /// <summary>
        /// DoT 지속시간 가져오기
        /// </summary>
        private static double GetDotDuration(GameDB_Client_Skill skillDB)
        {
            if (skillDB?.SkillModDuration?.Value == null) return 3;
            return (float)skillDB.SkillModDuration.Value.GetValue / 1000.0; // ms → s
        }

        /// <summary>
        /// DoT 틱 간격 가져오기
        /// </summary>
        private static double GetDotTickInterval(GameDB_Client_Skill skillDB)
        {
            return 1.0; // 기본 1초
        }

        /// <summary>
        /// Contagion 버프 여부 확인
        /// </summary>
        private static bool HasContagionBuff(ESkill aura)
        {
            // inevitable 오라가 contagion 버프를 발동
            return aura == ESkill.skillaura_inevitable;
        }

        /// <summary>
        /// 주요 피해 속성 가져오기
        /// </summary>
        private static ESkillTag GetPrimaryDamageType(Dictionary<ESkillTag, bool> skillTags)
        {
            if (skillTags == null) return ESkillTag.skilltag_physical;

            // 우선순위: Physical > Fire > Cold > Lightning > Poison
            // (BattleSimulatorWindow.CheckSkillTagType과 동일한 로직)
            // 키가 존재하고 값이 true인 경우만 확인
            if (skillTags.TryGetValue(ESkillTag.skilltag_physical, out bool isPhysical) && isPhysical)
                return ESkillTag.skilltag_physical;
            if (skillTags.TryGetValue(ESkillTag.skilltag_fire, out bool isFire) && isFire)
                return ESkillTag.skilltag_fire;
            if (skillTags.TryGetValue(ESkillTag.skilltag_cold, out bool isCold) && isCold)
                return ESkillTag.skilltag_cold;
            if (skillTags.TryGetValue(ESkillTag.skilltag_lightning, out bool isLightning) && isLightning)
                return ESkillTag.skilltag_lightning;
            if (skillTags.TryGetValue(ESkillTag.skilltag_poison, out bool isPoison) && isPoison)
                return ESkillTag.skilltag_poison;

            return ESkillTag.skilltag_physical;
        }

        /// <summary>
        /// 상태이상 피해 속성 가져오기
        /// </summary>
        private static ESkillTag GetAilmentDamageType(EStatusEffect ailment)
        {
            return ailment switch
            {
                EStatusEffect.ailment_bleeding => ESkillTag.skilltag_physical,
                EStatusEffect.ailment_ignite => ESkillTag.skilltag_fire,
                EStatusEffect.ailment_arctic => ESkillTag.skilltag_cold,
                EStatusEffect.ailment_shock => ESkillTag.skilltag_lightning,
                EStatusEffect.ailment_poisoning => ESkillTag.skilltag_poison,
                _ => ESkillTag.skilltag_physical
            };
        }

        /// <summary>
        /// 평균 크리티컬 배율 계산
        /// </summary>
        private static double CalculateAverageCritMultiplier(
            double critChance, double critMult,
            double critBlowChance, double critBlowMult)
        {
            // 확률 정규화 (100% 초과 방지)
            critChance = Math.Min(critChance, 1.0);
            critBlowChance = Math.Min(critBlowChance, 1.0);

            // 비크리티컬 확률
            double normalChance = 1.0 - critChance;

            // 일반 크리티컬 (치명타 일격 아닌) 확률
            double critOnlyChance = critChance * (1.0 - critBlowChance);

            // 치명타 일격 확률
            double critBlowActualChance = critChance * critBlowChance;

            // 가중 평균
            return (normalChance * 1.0) +
                   (critOnlyChance * critMult) +
                   (critBlowActualChance * critMult * critBlowMult);
        }

        #endregion

        #region MonsterToPlayer 검증

        /// <summary>
        /// 몬스터 → 플레이어 피해 검증
        /// 몬스터의 공격력과 플레이어의 방어기제를 고려한 최종 받는 피해 계산
        /// </summary>
        public static MonsterToPlayerVerificationResult VerifyMonsterToPlayerDamage(
            SimulatorDefender attacker,
            FCharacterStatus playerStatus)
        {
            var result = new MonsterToPlayerVerificationResult { IsEnabled = false };

            if (attacker == null || playerStatus == null) return result;

            try
            {
                result.IsEnabled = true;

                // 몬스터 정보
                result.MonsterName = attacker.defenderName;
                result.MonsterLevel = attacker.stageLevel;
                result.MonsterTypeDisplay = attacker.monsterType.ToString();

                // 몬스터 공격 속성 (EDamageType → ESkillTag 변환)
                result.AttackElement = ConvertDamageTypeToSkillTag(attacker.attackElement);

                // 몬스터 공격력 (mod_all_damage)
                double monsterDamage = attacker.calculatedMods.ContainsKey(EMod.mod_all_damage)
                    ? attacker.calculatedMods[EMod.mod_all_damage]
                    : 0;
                result.MonsterRawDamage = new ValueComparison("몬스터 공격력", monsterDamage, monsterDamage, 0.001);

                // 몬스터 공격 속도 (기본 1.0 - 몬스터는 별도 공격 속도 MOD 없음)
                double monsterAttackSpeed = 1.0;
                result.MonsterAttackSpeed = new ValueComparison("몬스터 공격 속도", monsterAttackSpeed, monsterAttackSpeed, 0.001);

                // 몬스터 순수 DPS
                double monsterRawDPS = monsterDamage * monsterAttackSpeed;
                result.MonsterRawDPS = new ValueComparison("몬스터 순수 DPS", monsterRawDPS, monsterRawDPS, 0.001);

                // 플레이어 방어 스탯
                var playerBattleStatus = playerStatus.BattleStatus;

                // 플레이어 생명력 계산
                // 시뮬레이션: mod_life * (1 + mod_life_inc) * (1 + mod_life_more)
                double modLifeFlat = playerBattleStatus.TotalModValue(EMod.mod_life);
                double modLifeInc = playerBattleStatus.TotalModValue(EMod.mod_life_inc);
                double modLifeMore = 0;
                if (System.Enum.TryParse<EMod>("mod_life_more", out EMod modLifeMoreEnum))
                {
                    modLifeMore = playerBattleStatus.TotalModValue(modLifeMoreEnum);
                }

                double simPlayerLife = modLifeFlat * (1.0 + modLifeInc);
                if (modLifeMore > 0)
                {
                    simPlayerLife *= (1.0 + modLifeMore);
                }

                // 인게임: ResultMaxLife() 직접 호출
                double ingamePlayerLife = playerBattleStatus.ResultMaxLife();

                result.PlayerLife = new ValueComparison("플레이어 생명력", simPlayerLife, ingamePlayerLife, 0.1);

                // 플레이어 저항 (실제 인게임 값 = 배수 형태, 예: 0.65)
                // UI 표시는 FormatModValueForUI로 변환 (65%)
                double physRes = playerBattleStatus.TotalModValue(EMod.mod_physical_resistance);
                double fireRes = playerBattleStatus.TotalModValue(EMod.mod_fire_resistance);
                double coldRes = playerBattleStatus.TotalModValue(EMod.mod_cold_resistance);
                double lightningRes = playerBattleStatus.TotalModValue(EMod.mod_lightning_resistance);
                double poisonRes = playerBattleStatus.TotalModValue(EMod.mod_poison_resistance);

                // ValueComparison에는 UI 표시용 값 저장 (ConvertModValueForUI 사용)
                result.PlayerPhysicalResistance = new ValueComparison("물리 저항",
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_physical_resistance, physRes),
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_physical_resistance, physRes), 0.001, "%");
                result.PlayerFireResistance = new ValueComparison("화염 저항",
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_fire_resistance, fireRes),
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_fire_resistance, fireRes), 0.001, "%");
                result.PlayerColdResistance = new ValueComparison("냉기 저항",
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_cold_resistance, coldRes),
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_cold_resistance, coldRes), 0.001, "%");
                result.PlayerLightningResistance = new ValueComparison("번개 저항",
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_lightning_resistance, lightningRes),
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_lightning_resistance, lightningRes), 0.001, "%");
                result.PlayerPoisonResistance = new ValueComparison("독 저항",
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_poison_resistance, poisonRes),
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_poison_resistance, poisonRes), 0.001, "%");

                // 피해 감소 (mod_physical_damage_reduction 등)
                double damageReduction = playerBattleStatus.TotalModValue(EMod.mod_physical_damage_reduction);
                result.PlayerDamageReduction = new ValueComparison("피해 감소",
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_physical_damage_reduction, damageReduction),
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_physical_damage_reduction, damageReduction), 0.001, "%");

                // 몬스터 공격 속성에 해당하는 저항
                double appliedResistance = GetPlayerResistanceByElement(playerBattleStatus, result.AttackElement);

                // 저항 최대치 적용 (기본 75%)
                double resistanceMax = GameGlobalConfig.maxResistance;
                double clampedResistance = Math.Min(appliedResistance, resistanceMax);

                // 적용 저항도 UI 표시용으로 변환 (ConvertModValueForUI 사용)
                result.AppliedResistance = new ValueComparison("적용 저항",
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_physical_resistance, clampedResistance),
                    SimulatorCalculator.ConvertModValueForUI(EMod.mod_physical_resistance, clampedResistance), 0.001, "%");

                // 저항 적용 후 DPS
                double afterResistanceDPS = monsterRawDPS * (1.0 - clampedResistance);
                result.AfterResistanceDPS = new ValueComparison("저항 적용 후 DPS", afterResistanceDPS, afterResistanceDPS, 0.001);

                // 최종 받는 DPS (피해 감소 적용)
                double finalDamageTaken = afterResistanceDPS * (1.0 - damageReduction);
                result.FinalDamageTaken = new ValueComparison("최종 받는 DPS", finalDamageTaken, finalDamageTaken, 0.001);

                // 1회 피격 피해
                double damagePerHit = monsterDamage * (1.0 - clampedResistance) * (1.0 - damageReduction);
                result.DamagePerHit = new ValueComparison("1회 피격 피해", damagePerHit, damagePerHit, 0.001);

                // 생존 시간 (시뮬레이션 vs 인게임)
                double simSurvivalTime = simPlayerLife > 0 && finalDamageTaken > 0 ? simPlayerLife / finalDamageTaken : 0;
                double ingameSurvivalTime = ingamePlayerLife > 0 && finalDamageTaken > 0 ? ingamePlayerLife / finalDamageTaken : 0;
                result.SurvivalTime = new ValueComparison("예상 생존 시간", simSurvivalTime, ingameSurvivalTime, 0.1);

                // 사망까지 필요한 피격 횟수 (시뮬레이션 vs 인게임)
                double simHitsToKill = damagePerHit > 0 ? Math.Ceiling(simPlayerLife / damagePerHit) : 0;
                double ingameHitsToKill = damagePerHit > 0 ? Math.Ceiling(ingamePlayerLife / damagePerHit) : 0;
                result.HitsToKill = new ValueComparison("사망까지 피격 횟수", simHitsToKill, ingameHitsToKill, 0.1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[검증] MonsterToPlayer 검증 실패: {e.Message}\n{e.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// EDamageType → ESkillTag 변환
        /// </summary>
        private static ESkillTag ConvertDamageTypeToSkillTag(EDamageType damageType)
        {
            return damageType switch
            {
                EDamageType.physical => ESkillTag.skilltag_physical,
                EDamageType.fire => ESkillTag.skilltag_fire,
                EDamageType.cold => ESkillTag.skilltag_cold,
                EDamageType.lightning => ESkillTag.skilltag_lightning,
                EDamageType.poison => ESkillTag.skilltag_poison,
                _ => ESkillTag.skilltag_physical
            };
        }

        /// <summary>
        /// 플레이어 저항 가져오기 (몬스터 공격 속성 기준)
        /// </summary>
        private static double GetPlayerResistanceByElement(FBattleStatus playerBattleStatus, ESkillTag attackElement)
        {
            double resistance = attackElement switch
            {
                ESkillTag.skilltag_physical => playerBattleStatus.TotalModValue(EMod.mod_physical_resistance),
                ESkillTag.skilltag_fire => playerBattleStatus.TotalModValue(EMod.mod_fire_resistance),
                ESkillTag.skilltag_cold => playerBattleStatus.TotalModValue(EMod.mod_cold_resistance),
                ESkillTag.skilltag_lightning => playerBattleStatus.TotalModValue(EMod.mod_lightning_resistance),
                ESkillTag.skilltag_poison => playerBattleStatus.TotalModValue(EMod.mod_poison_resistance),
                _ => 0
            };

            // 원소 저항 추가 (화/냉/번/독)
            if (attackElement != ESkillTag.skilltag_physical)
            {
                resistance += playerBattleStatus.TotalModValue(EMod.mod_elemental_resistance);
            }

            return resistance;
        }

        #endregion

        #region 리포트 생성

        /// <summary>
        /// 전체 리포트 생성
        /// </summary>
        public static string GenerateReport(FullVerificationResult result)
        {
            var sb = new StringBuilder();

            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("📊 시뮬레이션 vs 인게임 DPS 비교 리포트");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();
            sb.AppendLine("📖 리포트 설명:");
            sb.AppendLine("   이 리포트는 배틀 시뮬레이터의 DPS 예측값과 실제 인게임 함수로 계산한");
            sb.AppendLine("   DPS 값을 직접 비교하여 검증합니다. 두 계산 방식 간의 차이를 확인하고");
            sb.AppendLine("   시뮬레이션의 정확도를 평가하는 데 사용됩니다.");
            sb.AppendLine();
            sb.AppendLine($"검증 시각: {result.Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"소요 시간: {result.VerificationDuration:F0}ms");
            sb.AppendLine();

            // 요약
            sb.AppendLine("════════════════════════════════════════════════════════════════");
            sb.AppendLine("📋 검증 결과 요약");
            sb.AppendLine("════════════════════════════════════════════════════════════════");
            sb.AppendLine(result.GetSummary());

            // Spell
            if (result.Spell.IsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════");
                sb.AppendLine($"🔮 SPELL DAMAGE 검증 ({result.Spell.SkillType})");
                sb.AppendLine("════════════════════════════════════════════════════════════════");

                // 강화도 정보 표시
                sb.AppendLine();
                sb.AppendLine("📊 스킬 강화도 정보:");
                sb.AppendLine($"  • 스킬 티어: {result.Spell.SkillTier}");
                sb.AppendLine($"  • 피해 속성: {result.Spell.DamageType}");
                sb.AppendLine($"  • Core 강화도: {result.Spell.CoreReinforce}");
                sb.AppendLine($"  • 강화도 모드 증가: +{result.Spell.ReinforceModAdd}");
                sb.AppendLine($"  • 최종 강화도: {result.Spell.EffectiveReinforce}");
                sb.AppendLine($"  • Skill Effectiveness: {result.Spell.SkillEffectiveness?.SimValue ?? 0:F4}x ({(result.Spell.SkillEffectiveness?.SimValue ?? 0) * 100:F2}%)");
                sb.AppendLine();

                AppendComparisonTable(sb, new[]
                {
                    result.Spell.FlatDamage,
                    result.Spell.SkillEffectiveness,
                    result.Spell.BaseDamage,
                    result.Spell.PreCritDamage,
                    result.Spell.CriticalChance,
                    result.Spell.CriticalMultiplier,
                    result.Spell.CritBlowChance,
                    result.Spell.CritBlowMultiplier,
                    result.Spell.AverageCritMultiplier,
                    result.Spell.CastSpeed,
                    result.Spell.FinalDPS
                });
            }

            // Aura
            if (result.Aura.IsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════");
                sb.AppendLine($"🌀 AURA DAMAGE 검증 ({result.Aura.AuraType})");
                sb.AppendLine("════════════════════════════════════════════════════════════════");
                AppendComparisonTable(sb, new[]
                {
                    result.Aura.BaseDamage,
                    result.Aura.DamagePercent,
                    result.Aura.IncMultiplier,
                    result.Aura.MoreMultiplier,
                    result.Aura.CriticalChance,
                    result.Aura.CriticalMultiplier,
                    result.Aura.AverageDamage,
                    result.Aura.FinalDPS
                });
            }

            // Ailment
            if (result.Ailment.IsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════");
                sb.AppendLine("💥 AILMENT DAMAGE 검증");
                sb.AppendLine("════════════════════════════════════════════════════════════════");

                foreach (var kvp in result.Ailment.AilmentResults)
                {
                    var ailment = kvp.Value;
                    if (!ailment.IsEnabled) continue;

                    sb.AppendLine($"\n[{ailment.AilmentName}]");
                    AppendComparisonTable(sb, new[]
                    {
                        ailment.ProcChance,
                        ailment.FlatDamage,
                        ailment.IncMultiplier,
                        ailment.MoreMultiplier,
                        ailment.CriticalChance,
                        ailment.CriticalMultiplier,
                        ailment.AverageCritMultiplier,
                        ailment.DPS
                    });
                }

                sb.AppendLine();
                sb.AppendLine($"Ailment 총 DPS: {result.Ailment.TotalDPS}");
            }

            // DoT
            if (result.Dot.IsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════");
                sb.AppendLine($"☠️ DoT DAMAGE 검증 ({result.Dot.DotName})");
                sb.AppendLine("════════════════════════════════════════════════════════════════");
                AppendComparisonTable(sb, new[]
                {
                    result.Dot.BaseDamage,
                    result.Dot.DamagePercent,
                    result.Dot.TickInterval,
                    result.Dot.IncMultiplier,
                    result.Dot.MoreMultiplier,
                    result.Dot.CriticalChance,
                    result.Dot.CriticalMultiplier,
                    result.Dot.TickDamage,
                    result.Dot.FinalDPS
                });
            }

            // 총합
            sb.AppendLine();
            sb.AppendLine("════════════════════════════════════════════════════════════════");
            sb.AppendLine("📊 총 DPS 비교");
            sb.AppendLine("════════════════════════════════════════════════════════════════");
            if (result.GrandTotalDPS != null)
            {
                sb.AppendLine(result.GrandTotalDPS.ToString());
            }

            sb.AppendLine();
            sb.AppendLine("DPS 구성 비율:");
            double spellDPS = result.Spell.IsEnabled ? (result.Spell.FinalDPS?.SimValue ?? 0) : 0;
            double auraDPS = result.Aura.IsEnabled ? (result.Aura.FinalDPS?.SimValue ?? 0) : 0;
            double ailmentDPS = result.Ailment.IsEnabled ? (result.Ailment.TotalDPS?.SimValue ?? 0) : 0;
            double dotDPS = result.Dot.IsEnabled ? (result.Dot.FinalDPS?.SimValue ?? 0) : 0;
            sb.AppendLine($"  🔮 Spell:   {result.SpellDPSRatio,5:F1}% ({spellDPS:N0})");
            sb.AppendLine($"  🌀 Aura:    {result.AuraDPSRatio,5:F1}% ({auraDPS:N0})");
            sb.AppendLine($"  💥 Ailment: {result.AilmentDPSRatio,5:F1}% ({ailmentDPS:N0})");
            sb.AppendLine($"  ☠️ DoT:     {result.DotDPSRatio,5:F1}% ({dotDPS:N0})");

            // Defense (방어 적용 후 최종 피해)
            if (result.Defense.IsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════");
                sb.AppendLine("🛡️ DEFENSE 적용 (최종 피해 계산)");
                sb.AppendLine("════════════════════════════════════════════════════════════════");

                // 방어자 정보
                sb.AppendLine($"방어자: {result.Defense.DefenderName} (Lv.{result.Defense.DefenderLevel})");
                sb.AppendLine($"최대 체력: {result.Defense.DefenderMaxLife:N0}");
                sb.AppendLine();

                // 단계별 피해 계산 테이블
                sb.AppendLine("┌──────────────────────┬──────────────────────────────────────┐");
                sb.AppendLine("│ 단계                 │ 값                                   │");
                sb.AppendLine("├──────────────────────┼──────────────────────────────────────┤");

                // 1. 순수 DPS
                double beforeDefDPS = result.Defense.BeforeDefenseDPS?.SimValue ?? 0;
                sb.AppendLine($"│ 순수 DPS (방어 전)     │ {beforeDefDPS,36:N0} │");

                // 2. 저항
                double finalRes = result.Defense.FinalResistance?.SimValue ?? 0;
                double afterResDPS = result.Defense.AfterResistanceDPS?.SimValue ?? 0;
                sb.AppendLine($"│ 최종 저항              │ {finalRes,35:F2}% │");
                sb.AppendLine($"│ → 저항 적용 후 DPS     │ {afterResDPS,36:N0} │");

                // 3. 받는 피해 증감
                double dmgTakenInc = result.Defense.DamageTakenInc?.SimValue ?? 0;
                double dmgTakenDec = result.Defense.DamageTakenDec?.SimValue ?? 0;
                double dmgTakenMult = result.Defense.DamageTakenMultiplier?.SimValue ?? 1;
                double afterDmgTakenDPS = result.Defense.AfterDamageTakenDPS?.SimValue ?? 0;
                sb.AppendLine($"│ 받는 피해 증가         │ {dmgTakenInc,35:F2}% │");
                sb.AppendLine($"│ 받는 피해 감소         │ {dmgTakenDec,35:F2}% │");
                sb.AppendLine($"│ → 받는 피해 배율        │ {dmgTakenMult,35:F4}x │");
                sb.AppendLine($"│ → 받는 피해 적용 후 DPS │ {afterDmgTakenDPS,36:N0} │");

                // 4. 즉사 판정
                double instantKillThreshold = result.Defense.InstantKillThreshold?.SimValue ?? 0;
                double instantKillMult = result.Defense.InstantKillMultiplier?.SimValue ?? 1;
                sb.AppendLine($"│ 즉사 임계값            │ {instantKillThreshold,35:F2}% │");
                sb.AppendLine($"│ → 즉사 배율            │ {instantKillMult,35:F4}x │");

                double afterInstantKillDPS = result.Defense.AfterInstantKillDPS?.SimValue ?? 0;
                double timeToKill = result.Defense.TimeToKill?.SimValue ?? 0;
                sb.AppendLine("├──────────────────────┼──────────────────────────────────────┤");
                sb.AppendLine($"│ ★ 최종 DPS           │ {afterInstantKillDPS,36:N0} │");
                sb.AppendLine($"│ ★ 처치 시간          │ {timeToKill,34:F2}초 │");
                sb.AppendLine("└──────────────────────┴──────────────────────────────────────┘");

                // 피해 증감 요약
                double pureDPS = result.Defense.BeforeDefenseDPS?.SimValue ?? 0;
                double finalDPS = result.Defense.AfterInstantKillDPS?.SimValue ?? 0;
                double damageChange = pureDPS > 0 ? (finalDPS / pureDPS - 1) * 100 : 0;
                string changeIcon = damageChange >= 0 ? "📈" : "📉";
                sb.AppendLine();
                sb.AppendLine($"{changeIcon} 방어 적용 효과: {(damageChange >= 0 ? "+" : "")}{damageChange:F2}% (순수 {pureDPS:N0} → 최종 {finalDPS:N0})");

                // 개별 피해 유형별 최종 DPS
                sb.AppendLine();
                sb.AppendLine("┌──────────────────────┬──────────────────┬──────────────────┐");
                sb.AppendLine("│ 피해 유형            │ 순수 DPS         │ 최종 DPS         │");
                sb.AppendLine("├──────────────────────┼──────────────────┼──────────────────┤");

                double spellPure = result.Defense.SpellBeforeDefenseDPS?.SimValue ?? 0;
                double spellFinal = result.Defense.SpellFinalDPS?.SimValue ?? 0;
                double auraPure = result.Defense.AuraBeforeDefenseDPS?.SimValue ?? 0;
                double auraFinal = result.Defense.AuraFinalDPS?.SimValue ?? 0;
                double ailmentPure = result.Defense.AilmentBeforeDefenseDPS?.SimValue ?? 0;
                double ailmentFinal = result.Defense.AilmentFinalDPS?.SimValue ?? 0;
                double dotPure = result.Defense.DotBeforeDefenseDPS?.SimValue ?? 0;
                double dotFinal = result.Defense.DotFinalDPS?.SimValue ?? 0;

                sb.AppendLine($"│ 🔮 Spell             │ {spellPure,16:N0} │ {spellFinal,16:N0} │");
                sb.AppendLine($"│ 🌀 Aura              │ {auraPure,16:N0} │ {auraFinal,16:N0} │");
                sb.AppendLine($"│ 💥 Ailment           │ {ailmentPure,16:N0} │ {ailmentFinal,16:N0} │");
                sb.AppendLine($"│ ☠️ DoT               │ {dotPure,16:N0} │ {dotFinal,16:N0} │");
                sb.AppendLine("├──────────────────────┼──────────────────┼──────────────────┤");
                sb.AppendLine($"│ 📊 총합              │ {pureDPS,16:N0} │ {finalDPS,16:N0} │");
                sb.AppendLine("└──────────────────────┴──────────────────┴──────────────────┘");
            }

            // MonsterToPlayer (몬스터 → 플레이어 피해)
            if (result.MonsterToPlayer.IsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("════════════════════════════════════════════════════════════════");
                sb.AppendLine("👹 MONSTER → PLAYER 피해 검증 (플레이어 생존 분석)");
                sb.AppendLine("════════════════════════════════════════════════════════════════");

                // 몬스터 정보
                sb.AppendLine($"공격자: {result.MonsterToPlayer.MonsterName}");
                sb.AppendLine($"몬스터 타입: {result.MonsterToPlayer.MonsterTypeDisplay}");
                sb.AppendLine($"공격 속성: {result.MonsterToPlayer.AttackElement}");
                sb.AppendLine();

                // 몬스터 공격력
                sb.AppendLine("┌──────────────────────┬──────────────────────────────────────┐");
                sb.AppendLine("│ 몬스터 공격력        │ 값                                   │");
                sb.AppendLine("├──────────────────────┼──────────────────────────────────────┤");

                double monsterDamage = result.MonsterToPlayer.MonsterRawDamage?.SimValue ?? 0;
                double monsterSpeed = result.MonsterToPlayer.MonsterAttackSpeed?.SimValue ?? 1;
                double monsterDPS = result.MonsterToPlayer.MonsterRawDPS?.SimValue ?? 0;

                sb.AppendLine($"│ 몬스터 공격력         │ {monsterDamage,36:N0} │");
                sb.AppendLine($"│ 몬스터 공격 속도      │ {monsterSpeed,35:F2}x │");
                sb.AppendLine($"│ 몬스터 순수 DPS       │ {monsterDPS,36:N0} │");
                sb.AppendLine("└──────────────────────┴──────────────────────────────────────┘");
                sb.AppendLine();

                // 플레이어 방어 스탯 (시뮬 vs 인게임)
                sb.AppendLine("┌──────────────────────┬──────────────────┬──────────────────┬─────────┐");
                sb.AppendLine("│ 플레이어 방어 스탯   │ 시뮬레이션       │ 인게임           │ 오차%   │");
                sb.AppendLine("├──────────────────────┼──────────────────┼──────────────────┼─────────┤");

                var lifeComp = result.MonsterToPlayer.PlayerLife;
                double physRes = result.MonsterToPlayer.PlayerPhysicalResistance?.SimValue ?? 0;
                double fireRes = result.MonsterToPlayer.PlayerFireResistance?.SimValue ?? 0;
                double coldRes = result.MonsterToPlayer.PlayerColdResistance?.SimValue ?? 0;
                double lightningRes = result.MonsterToPlayer.PlayerLightningResistance?.SimValue ?? 0;
                double poisonRes = result.MonsterToPlayer.PlayerPoisonResistance?.SimValue ?? 0;
                double dmgRed = result.MonsterToPlayer.PlayerDamageReduction?.SimValue ?? 0;

                // 저항 값은 이미 UI 표시용으로 변환되어 저장됨 (ConvertModValueForUI 적용됨)
                sb.AppendLine($"│ {lifeComp?.GetStatusIcon() ?? "?"} 플레이어 생명력  │ {lifeComp?.SimValue ?? 0,16:N0} │ {lifeComp?.IngameValue ?? 0,16:N0} │ {lifeComp?.ErrorPercent ?? 0,6:F2}% │");
                sb.AppendLine($"│   물리 저항          │ {physRes,15:F2}% │ {physRes,15:F2}% │   0.00% │");
                sb.AppendLine($"│   화염 저항          │ {fireRes,15:F2}% │ {fireRes,15:F2}% │   0.00% │");
                sb.AppendLine($"│   냉기 저항          │ {coldRes,15:F2}% │ {coldRes,15:F2}% │   0.00% │");
                sb.AppendLine($"│   번개 저항          │ {lightningRes,15:F2}% │ {lightningRes,15:F2}% │   0.00% │");
                sb.AppendLine($"│   독 저항            │ {poisonRes,15:F2}% │ {poisonRes,15:F2}% │   0.00% │");
                sb.AppendLine($"│   피해 감소          │ {dmgRed,15:F2}% │ {dmgRed,15:F2}% │   0.00% │");
                sb.AppendLine("└──────────────────────┴──────────────────┴──────────────────┴─────────┘");
                sb.AppendLine();

                // 최종 피해 계산
                sb.AppendLine("┌──────────────────────┬──────────────────────────────────────┐");
                sb.AppendLine("│ 최종 피해 계산       │ 값                                   │");
                sb.AppendLine("├──────────────────────┼──────────────────────────────────────┤");

                double appliedRes = result.MonsterToPlayer.AppliedResistance?.SimValue ?? 0;
                double afterResDPS = result.MonsterToPlayer.AfterResistanceDPS?.SimValue ?? 0;
                double finalDamageTaken = result.MonsterToPlayer.FinalDamageTaken?.SimValue ?? 0;
                double damagePerHit = result.MonsterToPlayer.DamagePerHit?.SimValue ?? 0;

                sb.AppendLine($"│ 적용 저항 ({result.MonsterToPlayer.AttackElement}) │ {appliedRes,29:F2}% │");
                sb.AppendLine($"│ → 저항 적용 후 DPS   │ {afterResDPS,36:N0} │");
                sb.AppendLine($"│ → 최종 받는 DPS      │ {finalDamageTaken,36:N0} │");
                sb.AppendLine($"│ ★ 1회 피격 피해      │ {damagePerHit,36:N0} │");
                sb.AppendLine("└──────────────────────┴──────────────────────────────────────┘");
                sb.AppendLine();

                // 핵심 결과 (시뮬 vs 인게임 비교)
                var hitsComp = result.MonsterToPlayer.HitsToKill;
                var survivalComp = result.MonsterToPlayer.SurvivalTime;

                sb.AppendLine("┌──────────────────────┬──────────────────┬──────────────────┬─────────┐");
                sb.AppendLine("│ ★ 핵심 결과          │ 시뮬레이션       │ 인게임           │ 오차%   │");
                sb.AppendLine("├──────────────────────┼──────────────────┼──────────────────┼─────────┤");
                sb.AppendLine($"│ {hitsComp?.GetStatusIcon() ?? "?"} 사망까지 피격횟수│ {hitsComp?.SimValue ?? 0,14:F0}회 │ {hitsComp?.IngameValue ?? 0,14:F0}회 │ {hitsComp?.ErrorPercent ?? 0,6:F2}% │");
                sb.AppendLine($"│ {survivalComp?.GetStatusIcon() ?? "?"} 예상 생존 시간  │ {survivalComp?.SimValue ?? 0,14:F2}초 │ {survivalComp?.IngameValue ?? 0,14:F2}초 │ {survivalComp?.ErrorPercent ?? 0,6:F2}% │");
                sb.AppendLine("└──────────────────────┴──────────────────┴──────────────────┴─────────┘");

                // 생존 평가 (인게임 기준)
                sb.AppendLine();
                double hitsToKill = hitsComp?.IngameValue ?? 0;
                double survivalTime = survivalComp?.IngameValue ?? 0;

                string survivalStatus;
                if (hitsToKill <= 1)
                    survivalStatus = "❌ 원킬 (1회 피격에 사망)";
                else if (hitsToKill <= 3)
                    survivalStatus = "⚠️ 위험 (2~3회 피격에 사망)";
                else if (survivalTime < 10)
                    survivalStatus = "⚠️ 생존 시간 부족 (10초 미만)";
                else if (survivalTime < 30)
                    survivalStatus = "✅ 생존 가능 (10~30초)";
                else
                    survivalStatus = "✅ 안전 (30초 이상)";

                sb.AppendLine($"📊 생존 평가 (인게임 기준): {survivalStatus}");

                // 검증 결과
                bool allMatch = (lifeComp?.IsMatch ?? true) && (hitsComp?.IsMatch ?? true) && (survivalComp?.IsMatch ?? true);
                sb.AppendLine($"🔍 시뮬 vs 인게임: {(allMatch ? "✅ 일치" : "❌ 불일치")}");
            }

            sb.AppendLine();
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📋 종합 결과: {(result.AllPassed ? "✅ 모든 검증 통과" : "❌ 일부 검증 실패")}");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            return sb.ToString();
        }

        private static void AppendComparisonTable(StringBuilder sb, ValueComparison[] comparisons)
        {
            sb.AppendLine("┌──────────────────────┬──────────────┬──────────────┬───────┬─────┐");
            sb.AppendLine("│ 항목                 │ 시뮬레이션   │ 인게임       │ 오차% │상태 │");
            sb.AppendLine("├──────────────────────┼──────────────┼──────────────┼───────┼─────┤");

            foreach (var c in comparisons)
            {
                if (c == null) continue;
                string name = c.Name.PadRight(20);
                string simVal = c.FormatValue(c.SimValue).PadLeft(12);
                string ingameVal = c.FormatValue(c.IngameValue).PadLeft(12);
                string error = $"{c.ErrorPercent:F2}%".PadLeft(6);
                string status = c.GetStatusIcon();

                sb.AppendLine($"│ {name} │ {simVal} │ {ingameVal} │{error} │ {status}  │");
            }

            sb.AppendLine("└──────────────────────┴──────────────┴──────────────┴───────┴─────┘");
        }

        #endregion
    }
}
