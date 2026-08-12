using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PX;
using Newtonsoft.Json;

namespace BattleSimulator
{
    /// <summary>
    /// BattleSimulatorWindow의 이론적 밸런스 계산 관련 기능
    /// 빌드에 관계없이 모든 MOD의 실제 기여도를 공정하게 측정
    /// </summary>
    public partial class BattleSimulatorWindow
    {
        #region 이론적 밸런스 계산

        /// <summary>
        /// 현재 배틀 상태에서 모든 MOD 값을 수집
        /// </summary>
        /// <returns>EMod → 값 매핑</returns>
        private Dictionary<EMod, double> CollectAllModStats()
        {
            var stats = new Dictionary<EMod, double>();

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null || player.CharacterStatus == null)
            {
                Debug.LogError("[TheoreticalBalance] player 또는 CharacterStatus가 null입니다.");
                return stats;
            }

            var battleStatus = player.CharacterStatus.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[TheoreticalBalance] BattleStatus가 null입니다.");
                return stats;
            }

            // 모든 EMod 열거형 값을 순회하며 현재 값 수집
            foreach (EMod mod in Enum.GetValues(typeof(EMod)))
            {
                double value = battleStatus.TotalModValue(mod);
                stats[mod] = value;
            }

            return stats;
        }

        /// <summary>
        /// Spell DPS 계산 (More % 개별 곱연산)
        /// 참조: SimulatorCalculator.cs:311-367 (Physical), 444-520 (Elemental)
        /// </summary>
        private double CalculateSpellDPS(Dictionary<EMod, double> stats)
        {
            // Step 1: 모든 Flat Damage 수집
            double totalFlat =
                stats[EMod.mod_physical_damage] +
                stats[EMod.mod_fire_damage] +
                stats[EMod.mod_cold_damage] +
                stats[EMod.mod_lightning_damage] +
                stats[EMod.mod_poison_damage] +
                stats[EMod.mod_elemental_damage] +
                stats[EMod.mod_all_skill_damage] +
                stats[EMod.mod_all_damage];

            if (totalFlat <= 0)
                return 0; // Flat이 0이면 모든 계산 = 0

            // Step 2: Skill Effectiveness 적용
            // 참고: skill_effectiveness는 EMod가 아니라 스킬별 속성이므로 기본값 1.0 사용
            double skillEffectiveness = 1.0;
            double baseDamage = totalFlat * skillEffectiveness;

            // Step 3: Inc % 합산
            double totalInc =
                stats[EMod.mod_physical_damage_inc] +
                stats[EMod.mod_fire_damage_inc] +
                stats[EMod.mod_cold_damage_inc] +
                stats[EMod.mod_lightning_damage_inc] +
                stats[EMod.mod_poison_damage_inc] +
                stats[EMod.mod_elemental_damage_inc] +
                stats[EMod.mod_all_skill_damage_inc] +
                stats[EMod.mod_all_damage_inc];

            // TotalModValue는 GetValue가 적용된 값 반환 (FLOAT_PER: 20 → 0.2)
            double afterInc = baseDamage * (1.0 + totalInc);

            // Step 4: More % 개별 곱연산 (SimulatorCalculator.cs:341-367 참조)
            double physicalMore = stats[EMod.mod_physical_damage_more];
            double fireMore = stats[EMod.mod_fire_damage_more];
            double coldMore = stats[EMod.mod_cold_damage_more];
            double lightningMore = stats[EMod.mod_lightning_damage_more];
            double poisonMore = stats[EMod.mod_poison_damage_more];
            double allSkillMore = stats[EMod.mod_all_skill_damage_more];
            double allDamageMore = stats[EMod.mod_all_damage_more];

            // TotalModValue는 이미 비율 값 (0.2 = 20%)
            double afterMore = afterInc;
            afterMore *= (1.0 + physicalMore);
            afterMore *= (1.0 + fireMore);
            afterMore *= (1.0 + coldMore);
            afterMore *= (1.0 + lightningMore);
            afterMore *= (1.0 + poisonMore);
            afterMore *= (1.0 + allSkillMore);
            afterMore *= (1.0 + allDamageMore);

            // Step 5: Critical 평균 피해
            double critChance = stats[EMod.mod_crit_chance] + stats[EMod.mod_crit_blow_chance];
            double critMulti = 150.0 + stats[EMod.mod_crit_multiplier] + stats[EMod.mod_crit_blow_multiplier];

            double avgCritMultiplier = 1.0 + (critChance / 100.0) * (critMulti / 100.0);
            double avgHitDamage = afterMore * avgCritMultiplier;

            // Step 6: Cast Speed → DPS
            // TotalModValue는 이미 비율 값 (0.2 = 20%)
            double castSpeed = 1.0 * (1.0 + stats[EMod.mod_castspeed_inc]);
            double spellDPS = avgHitDamage * castSpeed;

            // 참고: 추가 발사체(mod_projectile_additional_fire)는 피해 배율에 포함하지 않음
            // 각 발사체가 개별 피해를 주므로 1발 기준으로 계산

            return spellDPS;
        }

        /// <summary>
        /// Ailment DPS 계산 (100% 발동 가정)
        /// 참조: SimulatorCalculator.cs:891-894, 1097-1272
        /// </summary>
        private double CalculateAilmentDPS(Dictionary<EMod, double> stats)
        {
            // 8가지 Ailment 타입 정의
            var ailmentInfos = new[]
            {
                new { Type = EStatusEffect.ailment_bleeding, SkillTag = ESkillTag.skilltag_physical, DamagePercent = 0.50 },
                new { Type = EStatusEffect.ailment_ignite, SkillTag = ESkillTag.skilltag_fire, DamagePercent = 0.50 },
                new { Type = EStatusEffect.ailment_arctic, SkillTag = ESkillTag.skilltag_cold, DamagePercent = 0.30 },
                new { Type = EStatusEffect.ailment_chill, SkillTag = ESkillTag.skilltag_cold, DamagePercent = 0.20 },
                new { Type = EStatusEffect.ailment_shock, SkillTag = ESkillTag.skilltag_lightning, DamagePercent = 0.40 },
                new { Type = EStatusEffect.ailment_paralyze, SkillTag = ESkillTag.skilltag_lightning, DamagePercent = 0.20 },
                new { Type = EStatusEffect.ailment_poisoning, SkillTag = ESkillTag.skilltag_poison, DamagePercent = 0.50 },
                new { Type = EStatusEffect.ailment_stun, SkillTag = ESkillTag.skilltag_physical, DamagePercent = 0.10 }
            };

            double totalAilmentDPS = 0;

            foreach (var info in ailmentInfos)
            {
                // 1. Flat Damage 계산 (속성별)
                double flatDamage = CalculateAilmentFlatDamage(stats, info.SkillTag);

                if (flatDamage <= 0)
                    continue; // Flat이 0이면 해당 Ailment는 무의미

                // 2. Inc % 배율 계산
                double incMultiplier = CalculateAilmentIncMultiplier(stats, info.Type);

                // 3. More % 배율 계산 (개별 곱연산)
                double moreMultiplier = CalculateAilmentMoreMultiplier(stats, info.Type);

                // 4. Ailment DPS = Flat × 피해% × Inc × More
                // 참조: SimulatorCalculator.cs:1268-1269 (procChance는 100% 가정)
                double ailmentDPS = flatDamage * info.DamagePercent * incMultiplier * moreMultiplier;

                totalAilmentDPS += ailmentDPS;
            }

            return totalAilmentDPS;
        }

        /// <summary>
        /// Ailment Flat Damage 계산 (속성별)
        /// 참조: SimulatorCalculator.cs:793-826
        /// </summary>
        private double CalculateAilmentFlatDamage(Dictionary<EMod, double> stats, ESkillTag skillTagDamageType)
        {
            double flatDamage = 0;

            // 속성별 Flat 데미지
            switch (skillTagDamageType)
            {
                case ESkillTag.skilltag_physical:
                    flatDamage = stats[EMod.mod_physical_damage];
                    break;
                case ESkillTag.skilltag_fire:
                    flatDamage = stats[EMod.mod_fire_damage]
                               + stats[EMod.mod_elemental_damage];
                    break;
                case ESkillTag.skilltag_cold:
                    flatDamage = stats[EMod.mod_cold_damage]
                               + stats[EMod.mod_elemental_damage];
                    break;
                case ESkillTag.skilltag_lightning:
                    flatDamage = stats[EMod.mod_lightning_damage]
                               + stats[EMod.mod_elemental_damage];
                    break;
                case ESkillTag.skilltag_poison:
                    flatDamage = stats[EMod.mod_poison_damage]
                               + stats[EMod.mod_elemental_damage];
                    break;
            }

            // 모든 타입 공통: AllSkill + AllDamage
            flatDamage += stats[EMod.mod_all_skill_damage];
            flatDamage += stats[EMod.mod_all_damage];

            return flatDamage;
        }

        /// <summary>
        /// Ailment Inc % 배율 계산
        /// 참조: SimulatorCalculator.cs:930-1094
        /// </summary>
        private double CalculateAilmentIncMultiplier(Dictionary<EMod, double> stats, EStatusEffect ailmentType)
        {
            double totalInc = 0;

            // 모든 피해 증가
            totalInc += stats[EMod.mod_all_damage_inc];

            // 모든 스킬 피해 증가
            totalInc += stats[EMod.mod_all_skill_damage_inc];

            // 모든 Ailment 피해 증가
            totalInc += stats[EMod.mod_all_ailment_damage_inc];

            // 속성별 피해 증가
            switch (ailmentType)
            {
                case EStatusEffect.ailment_bleeding:
                    totalInc += stats[EMod.mod_physical_damage_inc];
                    totalInc += stats[EMod.mod_bleeding_damage_inc];
                    break;
                case EStatusEffect.ailment_ignite:
                    totalInc += stats[EMod.mod_fire_damage_inc];
                    totalInc += stats[EMod.mod_elemental_damage_inc];
                    totalInc += stats[EMod.mod_ignite_damage_inc];
                    break;
                case EStatusEffect.ailment_arctic:
                    totalInc += stats[EMod.mod_cold_damage_inc];
                    totalInc += stats[EMod.mod_elemental_damage_inc];
                    totalInc += stats[EMod.mod_arctic_damage_inc];
                    break;
                case EStatusEffect.ailment_chill:
                    totalInc += stats[EMod.mod_cold_damage_inc];
                    totalInc += stats[EMod.mod_elemental_damage_inc];
                    // chill 전용 inc는 없음
                    break;
                case EStatusEffect.ailment_shock:
                    totalInc += stats[EMod.mod_lightning_damage_inc];
                    totalInc += stats[EMod.mod_elemental_damage_inc];
                    totalInc += stats[EMod.mod_shock_damage_inc];
                    break;
                case EStatusEffect.ailment_paralyze:
                    totalInc += stats[EMod.mod_lightning_damage_inc];
                    totalInc += stats[EMod.mod_elemental_damage_inc];
                    // paralyze 전용 inc는 없음
                    break;
                case EStatusEffect.ailment_poisoning:
                    totalInc += stats[EMod.mod_poison_damage_inc];
                    totalInc += stats[EMod.mod_elemental_damage_inc];
                    totalInc += stats[EMod.mod_poisoning_damage_inc];
                    break;
                case EStatusEffect.ailment_stun:
                    totalInc += stats[EMod.mod_physical_damage_inc];
                    // stun 전용 inc는 없음
                    break;
            }

            // TotalModValue는 이미 비율 값 (0.2 = 20%)
            return 1.0 + totalInc;
        }

        /// <summary>
        /// Ailment More % 배율 계산 (개별 곱연산)
        /// 참조: SimulatorCalculator.cs:1097-1187
        /// </summary>
        private double CalculateAilmentMoreMultiplier(Dictionary<EMod, double> stats, EStatusEffect ailmentType)
        {
            double result = 1.0;

            // TotalModValue는 이미 비율 값 (0.2 = 20%)
            // 모든 피해 증폭 (개별 곱셈)
            double allDamageMore = stats[EMod.mod_all_damage_more];
            if (allDamageMore != 0) result *= (1.0 + allDamageMore);

            // 모든 스킬 피해 증폭 (개별 곱셈)
            double allSkillMore = stats[EMod.mod_all_skill_damage_more];
            if (allSkillMore != 0) result *= (1.0 + allSkillMore);

            // 모든 Ailment 피해 증폭 (개별 곱셈)
            double allAilmentMore = stats[EMod.mod_all_ailment_damage_more];
            if (allAilmentMore != 0) result *= (1.0 + allAilmentMore);

            // 속성별 피해 증폭 (개별 곱셈)
            switch (ailmentType)
            {
                case EStatusEffect.ailment_bleeding:
                    {
                        double physicalMore = stats[EMod.mod_physical_damage_more];
                        if (physicalMore != 0) result *= 1.0 + physicalMore;

                        double bleedingMore = stats[EMod.mod_bleeding_damage_more];
                        if (bleedingMore != 0) result *= 1.0 + bleedingMore;
                    }
                    break;
                case EStatusEffect.ailment_ignite:
                    {
                        double fireMore = stats[EMod.mod_fire_damage_more];
                        if (fireMore != 0) result *= 1.0 + fireMore;

                        double igniteMore = stats[EMod.mod_ignite_damage_more];
                        if (igniteMore != 0) result *= 1.0 + igniteMore;
                    }
                    break;
                case EStatusEffect.ailment_arctic:
                    {
                        double coldMore = stats[EMod.mod_cold_damage_more];
                        if (coldMore != 0) result *= 1.0 + coldMore;

                        double arcticMore = stats[EMod.mod_arctic_damage_more];
                        if (arcticMore != 0) result *= 1.0 + arcticMore;
                    }
                    break;
                case EStatusEffect.ailment_chill:
                    {
                        double coldMore = stats[EMod.mod_cold_damage_more];
                        if (coldMore != 0) result *= 1.0 + coldMore;
                        // chill 전용 more는 없음
                    }
                    break;
                case EStatusEffect.ailment_shock:
                    {
                        double lightningMore = stats[EMod.mod_lightning_damage_more];
                        if (lightningMore != 0) result *= 1.0 + lightningMore;

                        double shockMore = stats[EMod.mod_shock_damage_more];
                        if (shockMore != 0) result *= 1.0 + shockMore;
                    }
                    break;
                case EStatusEffect.ailment_paralyze:
                    {
                        double lightningMore = stats[EMod.mod_lightning_damage_more];
                        if (lightningMore != 0) result *= 1.0 + lightningMore;
                        // paralyze 전용 more는 없음
                    }
                    break;
                case EStatusEffect.ailment_poisoning:
                    {
                        double poisonMore = stats[EMod.mod_poison_damage_more];
                        if (poisonMore != 0) result *= 1.0 + poisonMore;

                        double poisoningMore = stats[EMod.mod_poisoning_damage_more];
                        if (poisoningMore != 0) result *= 1.0 + poisoningMore;
                    }
                    break;
                case EStatusEffect.ailment_stun:
                    // stun은 more 없음
                    break;
            }

            return result;
        }

        /// <summary>
        /// Aura DPS 계산
        /// 참조: SimulatorCalculator.cs:1848-1857
        /// </summary>
        private double CalculateAuraDPS(Dictionary<EMod, double> stats)
        {
            // TODO: Aura DPS 계산 로직 구현 필요
            return 0;
        }

        /// <summary>
        /// DoT 버프 DPS (skill_contagion 등, 100% 활성화 가정)
        /// </summary>
        private double CalculateDotBuffDPS(Dictionary<EMod, double> stats)
        {
            // TODO: DoT 버프 DPS 계산 로직 구현 필요
            return 0;
        }

        /// <summary>
        /// Curse 효과 배율 (100% 적용 가정)
        /// </summary>
        private double CalculateCurseMultiplier(Dictionary<EMod, double> stats)
        {
            // 적이 받는 피해 증가
            // TotalModValue는 이미 비율 값 (0.2 = 20%)
            double enemyTakeIncDamage = stats[EMod.mod_cursed_enemy_take_inc_physical_damage];

            return 1.0 + enemyTakeIncDamage;
        }

        /// <summary>
        /// Defense 배율 (저항 관통, 받는 피해 증가 등)
        /// - 저항 관통: 적의 저항을 감소시켜 피해 증폭
        /// - 받는 피해 증가: 적이 받는 피해를 직접 증폭 (More-like 효과)
        /// </summary>
        private double CalculateDefenseMultiplier(Dictionary<EMod, double> stats)
        {
            // 기본 몬스터 저항 15% (비율로 변환: 0.15)
            double baseResistance = 0.15;

            // 저항 관통 (Penetration) - TotalModValue는 이미 비율 값
            double penetration =
                stats[EMod.mod_fire_resistance_penetration] +
                stats[EMod.mod_cold_resistance_penetration] +
                stats[EMod.mod_lightning_resistance_penetration] +
                stats[EMod.mod_poison_resistance_penetration] +
                stats[EMod.mod_elemental_resistance_penetration];

            // 저항 감소 (Reduction from Curse) - TotalModValue는 이미 비율 값
            double reduction =
                stats[EMod.mod_reduction_enemy_fire_resistance] +
                stats[EMod.mod_reduction_enemy_cold_resistance] +
                stats[EMod.mod_reduction_enemy_lightning_resistance] +
                stats[EMod.mod_reduction_enemy_poison_resistance] +
                stats[EMod.mod_reduction_enemy_elemental_resistance];

            // 최종 저항 (모두 비율 값)
            double finalResistance = Math.Max(0, baseResistance - penetration - reduction);

            // 저항 기반 배율 (이미 비율이므로 /100 불필요)
            double resistanceMultiplier = 1.0 - finalResistance;

            // 받는 피해 증가 (More-like 효과) - TotalModValue는 이미 비율 값
            double enemyTakeDamageInc = stats[EMod.mod_enemy_take_inc_physical_damage];
            double takeDamageMultiplier = 1.0 + enemyTakeDamageInc;

            return resistanceMultiplier * takeDamageMultiplier;
        }

        /// <summary>
        /// 즉사 효과를 DPS 배율로 계산 (전투 시간 단축 효과)
        /// </summary>
        private double CalculateInstantKillMultiplier(Dictionary<EMod, double> stats)
        {
            // 즉사 임계값 (적 HP의 X% 이하일 때 즉사)
            // TotalModValue는 이미 비율 값 (0.1 = 10%)
            double instantKillThreshold = stats.ContainsKey(EMod.mod_instantkill_lowerlife)
                ? stats[EMod.mod_instantkill_lowerlife]
                : 0;

            if (instantKillThreshold <= 0)
                return 1.0; // 즉사 없음

            // 전투 시간 단축 = threshold (이미 비율)
            // DPS 배율 = 1 / (1 - threshold)
            double multiplier = 1.0 / (1.0 - instantKillThreshold);

            return multiplier;
        }

        /// <summary>
        /// 이론적 총 DPS 계산
        /// SimulatorCalculator의 실제 계산 로직 활용
        /// </summary>
        private double CalculateTheoreticalTotalDPS(Dictionary<EMod, double> stats)
        {
            // 1. Spell Damage 계산
            double spellDPS = CalculateSpellDPS(stats);

            // 2. Ailment DPS 계산 (100% 발동 가정)
            double ailmentDPS = CalculateAilmentDPS(stats);

            // 3. Aura DPS 계산
            double auraDPS = CalculateAuraDPS(stats);

            // 4. DoT 버프 DPS (100% 활성화 가정)
            double dotDPS = CalculateDotBuffDPS(stats);

            // 5. Curse 효과 (100% 적용 가정)
            double curseMultiplier = CalculateCurseMultiplier(stats);

            // 6. Defense 적용 (저항, 관통, 받는 피해 증가)
            double defenseMultiplier = CalculateDefenseMultiplier(stats);

            // 7. 즉사 효과 (전투 시간 단축)
            double instantKillMultiplier = CalculateInstantKillMultiplier(stats);

            // 8. 총 DPS
            double totalDPS = (spellDPS + ailmentDPS + auraDPS + dotDPS)
                              * curseMultiplier
                              * defenseMultiplier
                              * instantKillMultiplier;

            return totalDPS;
        }

        /// <summary>
        /// 특정 MOD의 DPS 기여도 계산
        /// 제거 기반 마지널 기여도 (Removal-based Marginal Contribution)
        /// </summary>
        /// <param name="fullStats">전체 MOD 상태</param>
        /// <param name="modType">측정할 MOD 타입</param>
        /// <param name="modValue">MOD 값</param>
        /// <returns>DPS 기여도</returns>
        private double CalculateModContribution(
            Dictionary<EMod, double> fullStats,
            EMod modType,
            double modValue)
        {
            // 1. 전체 MOD 포함 DPS
            double fullDPS = CalculateTheoreticalTotalDPS(fullStats);

            // 2. 해당 MOD만 제거
            var statsWithoutMod = new Dictionary<EMod, double>(fullStats);
            statsWithoutMod[modType] -= modValue;

            // 3. MOD 제거 후 DPS
            double dpsWithout = CalculateTheoreticalTotalDPS(statsWithoutMod);

            // 4. 기여도 = DPS 차이
            double contribution = fullDPS - dpsWithout;

            return contribution;
        }

        /// <summary>
        /// 콘텐츠별 MOD 매핑 구축
        /// ModSources에서 각 MOD의 값을 콘텐츠별로 그룹화
        /// </summary>
        private Dictionary<string, Dictionary<EMod, double>> BuildContentModMapping()
        {
            var contentModMapping = new Dictionary<string, Dictionary<EMod, double>>();

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null || player.CharacterStatus == null)
            {
                Debug.LogError("[TheoreticalBalance] player가 null입니다.");
                return contentModMapping;
            }

            var battleStatus = player.CharacterStatus.BattleStatus;
            if (battleStatus == null || battleStatus.characterData?.ModSources == null)
            {
                Debug.LogError("[TheoreticalBalance] BattleStatus 또는 ModSources가 null입니다.");
                return contentModMapping;
            }

            // 콘텐츠 카테고리 초기화 (ModAnalysis.cs:37-51 참조)
            var categories = new[]
            {
                "캐릭터-기본", "캐릭터-성장", "캐릭터-강화", "장비",
                "스킬-주문", "스킬-오라", "각성", "성운", "성좌",
                "펫", "펫-저주", "몬스터", "버프", "디버프", "조합형MOD"
            };

            foreach (var category in categories)
            {
                contentModMapping[category] = new Dictionary<EMod, double>();
            }

            try
            {
                // 모든 EMod 순회하면서 각 소스 확인
                foreach (EMod mod in Enum.GetValues(typeof(EMod)))
                {
                    if (battleStatus.characterData.ModSources.HasSources((int)mod))
                    {
                        var sources = battleStatus.characterData.ModSources.GetSources(mod);
                        foreach (var source in sources)
                        {
                            string category = GetContentCategoryFromSourceType(source.sourceType);
                            double value = source.value;

                            // 해당 콘텐츠에 MOD 값 누적
                            if (!contentModMapping[category].ContainsKey(mod))
                                contentModMapping[category][mod] = 0;

                            contentModMapping[category][mod] += value;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TheoreticalBalance] BuildContentModMapping 실패: {ex.Message}");
            }

            return contentModMapping;
        }

        /// <summary>
        /// EModSourceType에서 콘텐츠 카테고리 이름 추출
        /// ModAnalysis.cs:GetContentCategory() 참조
        /// </summary>
        private string GetContentCategoryFromSourceType(EModSourceType sourceType)
        {
            switch (sourceType)
            {
                case EModSourceType.PlayerDefault:
                    return "캐릭터-기본";
                case EModSourceType.PlayerGrowth:
                    return "캐릭터-성장";
                case EModSourceType.PlayerReinforce:
                    return "캐릭터-강화";

                case EModSourceType.Equipment_Normal:
                case EModSourceType.Equipment_Common:
                case EModSourceType.Equipment_MythicUnique:
                case EModSourceType.Equipment_Possession:
                    return "장비";

                case EModSourceType.Skill_Spell:
                case EModSourceType.Skill_SpellMaster:
                case EModSourceType.Skill_Rune:
                    return "스킬-주문";

                case EModSourceType.Skill_Aura:
                case EModSourceType.Skill_AuraMaster:
                    return "스킬-오라";

                case EModSourceType.Awaken_Grade:
                case EModSourceType.Awaken_Element:
                    return "각성";

                case EModSourceType.Nebula:
                    return "성운";

                case EModSourceType.Constellation:
                    return "성좌";

                case EModSourceType.Pet:
                    return "펫";

                case EModSourceType.Skill_PetCurse:
                    return "펫-저주";

                case EModSourceType.Monster:
                    return "몬스터";

                case EModSourceType.Buff:
                    return "버프";

                case EModSourceType.Debuff:
                    return "디버프";

                case EModSourceType.CombineMod_Buff:
                case EModSourceType.CombineMod_General:
                    return "조합형MOD";

                default:
                    return "기타";
            }
        }

        /// <summary>
        /// 콘텐츠별 이론적 기여도 계산
        /// </summary>
        private Dictionary<string, double> CalculateContentTheoreticalContributions()
        {
            // 1. 현재 전체 MOD 상태 수집
            var fullStats = CollectAllModStats();

            // 2. 콘텐츠별 MOD 매핑
            var contentModMapping = BuildContentModMapping();

            // 3. 콘텐츠별 총 기여도 계산
            var contentContributions = new Dictionary<string, double>();

            foreach (var content in contentModMapping)
            {
                string contentName = content.Key;
                double totalContribution = 0;

                foreach (var mod in content.Value)
                {
                    EMod modType = mod.Key;
                    double modValue = mod.Value;

                    // 각 MOD의 기여도 계산
                    double contribution = CalculateModContribution(fullStats, modType, modValue);
                    totalContribution += contribution;
                }

                contentContributions[contentName] = totalContribution;
            }

            return contentContributions;
        }

        /// <summary>
        /// 콘텐츠별 기여도를 비율로 변환
        /// </summary>
        private Dictionary<string, double> CalculateContentPercentages(
            Dictionary<string, double> contributions)
        {
            double total = contributions.Values.Sum();

            var percentages = new Dictionary<string, double>();
            foreach (var kvp in contributions)
            {
                percentages[kvp.Key] = total > 0 ? (kvp.Value / total) * 100.0 : 0;
            }

            return percentages;
        }

        /// <summary>
        /// 이론적 밸런스 리포트 출력 (독립 실행)
        /// </summary>
        public void ExportTheoreticalBalanceReport()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[TheoreticalBalance] Play Mode에서만 이론적 밸런스 리포트를 출력할 수 있습니다.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            // ====================================
            // 헤더
            // ====================================
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine("           이론적 밸런스 분석 리포트 (빌드 무관)");
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine($"생성 시간: {timestamp}");
            sb.AppendLine();
            sb.AppendLine("💡 목적: 모든 MOD 타입을 공정하게 평가하여 밸런스 조정");
            sb.AppendLine("   - 특정 빌드에 편향되지 않은 이론적 기여도 측정");
            sb.AppendLine("   - Ailment/Curse/DoT는 100% 발동 가정");
            sb.AppendLine("   - MOD 간 시너지 자동 반영 (제거 기반 계산)");
            sb.AppendLine();

            // ====================================
            // 1. 실제 DPS 계산 (SimulatorCalculator 사용)
            // ====================================
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("1. 현재 빌드 총 DPS (실제 게임 로직 기반)");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();

            // SimulationRoutine과 동일한 방식으로 BattleStatus 업데이트
            UpdateBattleStatusForAnalysis();

            // SimulatorCalculator를 사용하여 정확한 DPS 계산 (버프, Combine Mod 등 모두 반영)
            SimulatorDamageStats stats = SimulatorCalculator.CalculateStatisticalDPS(
                mainSpell,
                spellTier,
                defender ?? SimulatorDefender.CreateDefault1000FloorBoss(),
                auraReinforceLevel,
                aura  // 장착된 Aura (DoT 조건 확인용)
            );
            float realDPS = (float)stats.realDPS;

            sb.AppendLine($"  총 DPS: {realDPS:N2}");
            sb.AppendLine($"  💡 실제 게임 로직 기반 (버프, Combine Mod, 시너지 모두 반영)");
            sb.AppendLine();

            // ====================================
            // 2. 콘텐츠별 이론적 기여도 (프리셋 기반으로 재구현 예정)
            // ====================================
            // TODO: 프리셋 기반 콘텐츠별 기여도 계산
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("2. 현재 빌드 정보");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();
            sb.AppendLine("  💡 상세한 콘텐츠별 기여도 분석은 프리셋 기반으로 재구현 예정입니다.");
            sb.AppendLine();

            // ====================================
            // 3. 리포트 완료
            // ====================================
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("리포트 완료");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();
            sb.AppendLine("💡 SimulatorCalculator를 사용하여 정확한 DPS를 계산했습니다.");
            sb.AppendLine("💡 상세한 분석은 프리셋 기반으로 재구현 예정입니다.");
            sb.AppendLine();

            // ====================================
            // 파일 저장
            // ====================================
            string directory = "Assets/Editor/BattleSimulator/Reports/TheoreticalBalance";
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            string fileName = $"TheoreticalBalanceReport_{timestamp}.txt";
            string fullPath = System.IO.Path.Combine(directory, fileName);

            try
            {
                System.IO.File.WriteAllText(fullPath, sb.ToString(), System.Text.Encoding.UTF8);

                // TODO: MOD 상세 분해 리포트는 프리셋 기반으로 재구현 예정
                // ExportModBreakdownReport(timestamp, fullStats, contentModMapping, directory);

                // 파일 탐색기에서 메인 파일 열기
                EditorUtility.RevealInFinder(fullPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TheoreticalBalance] ❌ 리포트 출력 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 모든 DPS 관련 MOD의 콘텐츠별 상세 분해 리포트 (별도 파일)
        /// </summary>
        private void ExportModBreakdownReport(
            string timestamp,
            Dictionary<EMod, double> fullStats,
            Dictionary<string, Dictionary<EMod, double>> contentModMapping,
            string directory)
        {
            var sb = new System.Text.StringBuilder();

            // ====================================
            // 헤더
            // ====================================
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine("        MOD 콘텐츠별 상세 분해 리포트");
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine($"생성 시간: {timestamp}");
            sb.AppendLine();
            sb.AppendLine("💡 목적: 모든 DPS 관련 MOD가 어느 콘텐츠에서 얼마씩 오는지 상세 분석");
            sb.AppendLine();

            // DPS 관련 MOD를 카테고리별로 그룹화
            var modCategories = new Dictionary<string, EMod[]>
            {
                ["Flat Damage"] = new[]
                {
                    EMod.mod_physical_damage, EMod.mod_fire_damage, EMod.mod_cold_damage,
                    EMod.mod_lightning_damage, EMod.mod_poison_damage, EMod.mod_elemental_damage,
                    EMod.mod_all_skill_damage, EMod.mod_all_damage
                },
                ["Increased % (Inc)"] = new[]
                {
                    EMod.mod_physical_damage_inc, EMod.mod_fire_damage_inc, EMod.mod_cold_damage_inc,
                    EMod.mod_lightning_damage_inc, EMod.mod_poison_damage_inc, EMod.mod_elemental_damage_inc,
                    EMod.mod_all_skill_damage_inc, EMod.mod_all_damage_inc
                },
                ["More %"] = new[]
                {
                    EMod.mod_physical_damage_more, EMod.mod_fire_damage_more, EMod.mod_cold_damage_more,
                    EMod.mod_lightning_damage_more, EMod.mod_poison_damage_more,
                    EMod.mod_all_skill_damage_more, EMod.mod_all_damage_more
                },
                ["Critical"] = new[]
                {
                    EMod.mod_crit_chance, EMod.mod_crit_blow_chance,
                    EMod.mod_crit_multiplier, EMod.mod_crit_blow_multiplier
                },
                ["Cast Speed"] = new[]
                {
                    EMod.mod_castspeed_inc
                },
                ["Ailment Increased %"] = new[]
                {
                    EMod.mod_bleeding_damage_inc, EMod.mod_ignite_damage_inc, EMod.mod_arctic_damage_inc,
                    EMod.mod_shock_damage_inc, EMod.mod_poisoning_damage_inc, EMod.mod_all_ailment_damage_inc
                },
                ["Ailment More %"] = new[]
                {
                    EMod.mod_bleeding_damage_more, EMod.mod_ignite_damage_more, EMod.mod_arctic_damage_more,
                    EMod.mod_shock_damage_more, EMod.mod_poisoning_damage_more, EMod.mod_all_ailment_damage_more
                },
                ["Resistance Penetration"] = new[]
                {
                    EMod.mod_fire_resistance_penetration, EMod.mod_cold_resistance_penetration,
                    EMod.mod_lightning_resistance_penetration, EMod.mod_poison_resistance_penetration,
                    EMod.mod_elemental_resistance_penetration
                },
                ["Enemy Resistance Reduction"] = new[]
                {
                    EMod.mod_reduction_enemy_fire_resistance, EMod.mod_reduction_enemy_cold_resistance,
                    EMod.mod_reduction_enemy_lightning_resistance, EMod.mod_reduction_enemy_poison_resistance,
                    EMod.mod_reduction_enemy_elemental_resistance
                },
                ["Enemy Take Damage Inc"] = new[]
                {
                    EMod.mod_enemy_take_inc_physical_damage, EMod.mod_cursed_enemy_take_inc_physical_damage
                },
                ["Instant Kill"] = new[]
                {
                    EMod.mod_instantkill_lowerlife
                }
            };

            // 각 카테고리별로 MOD 분해 출력
            foreach (var category in modCategories)
            {
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"{category.Key}");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();

                foreach (var mod in category.Value)
                {
                    double totalValue = fullStats[mod];

                    if (totalValue == 0)
                        continue;

                    sb.AppendLine($"  【{mod}】");
                    sb.AppendLine($"    총합: {totalValue:N2}");
                    sb.AppendLine();
                    sb.AppendLine($"    콘텐츠별 분해:");

                    // 각 콘텐츠에서 이 MOD가 얼마나 기여하는지
                    var contentContributions = new Dictionary<string, double>();
                    foreach (var content in contentModMapping)
                    {
                        string contentName = content.Key;
                        if (content.Value.ContainsKey(mod))
                        {
                            double value = content.Value[mod];
                            if (value != 0)
                            {
                                contentContributions[contentName] = value;
                            }
                        }
                    }

                    if (contentContributions.Count == 0)
                    {
                        sb.AppendLine($"      (콘텐츠별 분해 정보 없음)");
                    }
                    else
                    {
                        // 기여도 높은 순으로 정렬
                        var sortedContributions = contentContributions.OrderByDescending(kvp => Math.Abs(kvp.Value));

                        foreach (var kvp in sortedContributions)
                        {
                            double percent = totalValue != 0 ? (kvp.Value / totalValue) * 100.0 : 0;
                            sb.AppendLine($"      - {kvp.Key}: {kvp.Value:N2} ({percent:F1}%)");
                        }

                        // 합계 검증
                        double sum = contentContributions.Values.Sum();
                        double diff = totalValue - sum;
                        if (Math.Abs(diff) > 0.01)
                        {
                            sb.AppendLine($"      ⚠️ 합계 불일치: {sum:N2} (차이: {diff:N2})");
                        }
                    }

                    sb.AppendLine();
                }
            }

            // ====================================
            // 파일 저장
            // ====================================
            string fileName = $"TheoreticalBalanceReport_ModBreakdown_{timestamp}.txt";
            string fullPath = System.IO.Path.Combine(directory, fileName);

            try
            {
                System.IO.File.WriteAllText(fullPath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TheoreticalBalance] ❌ MOD 분해 리포트 출력 실패: {ex.Message}");
            }
        }


        #endregion
    }
}
