using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// BattleSimulatorWindow의 MOD 기여도 분석 기능
    /// </summary>
    public partial class BattleSimulatorWindow
    {
        #region MOD 분석 관련 필드

        /// <summary>
        /// 콘텐츠별 MOD 상세 기여도 (콘텐츠 → MOD → 기여도)
        /// </summary>
        private Dictionary<string, Dictionary<string, double>> contentModDetails = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// 콘텐츠별 MOD 원본 값 합계 (콘텐츠 → MOD → 원본 값 합계)
        /// </summary>
        private Dictionary<string, Dictionary<string, double>> contentModRawValues = new Dictionary<string, Dictionary<string, double>>();

        #endregion

        #region MOD 기여도 분석 메서드

        /// <summary>
        /// 콘텐츠별 MOD 기여도 분석 (DPS 기여도 기준)
        /// </summary>
        private Dictionary<string, double> AnalyzeContentContributions()
        {
            var contributions = new Dictionary<string, double>();

            // 콘텐츠 카테고리별 초기화 (14개 카테고리)
            contributions["캐릭터-기본"] = 0;
            contributions["캐릭터-성장"] = 0;
            contributions["캐릭터-강화"] = 0;
            contributions["장비"] = 0;
            contributions["스킬-주문"] = 0;
            contributions["스킬-오라"] = 0;
            contributions["각성"] = 0;
            contributions["성운"] = 0;
            contributions["성좌"] = 0;
            contributions["펫"] = 0;
            contributions["펫-저주"] = 0;
            contributions["몬스터"] = 0;
            contributions["버프"] = 0;
            contributions["디버프"] = 0;
            contributions["조합형MOD"] = 0;

            // 세부 분석 초기화
            contentModDetails.Clear();
            contentModRawValues.Clear();
            foreach (var category in contributions.Keys)
            {
                contentModDetails[category] = new Dictionary<string, double>();
                contentModRawValues[category] = new Dictionary<string, double>();
            }

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                return contributions;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null || battleStatus.characterData?.ModSources == null)
            {
                return contributions;
            }

            try
            {
                // MOD Stage 메타데이터 로드
                ModStageMetadata.Load();

                // DPS에 직접 영향을 주는 주요 MOD 카테고리별 분석 (기존 방식)
                AnalyzeDamageContribution(battleStatus, contributions);
                AnalyzeIncContribution(battleStatus, contributions);
                AnalyzeMoreContribution(battleStatus, contributions);
                AnalyzeCriticalContribution(battleStatus, contributions);
                AnalyzeSpeedContribution(battleStatus, contributions);

                // 추가 피해 요소 분석
                AnalyzeReinforceContribution(battleStatus, contributions);
                AnalyzePenetrationContribution(battleStatus, contributions);

                // 저주 효과 분석 (간접 기여도)
                AnalyzeCurseContribution(contributions);

                // ====================================
                // 신규: modAssignByAllStage.md의 모든 stage MOD 분석
                // ====================================
                AnalyzeAllStagesFromMetadata(battleStatus, contributions);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Simulator] 콘텐츠 기여도 분석 실패: {ex.Message}");
            }

            return contributions;
        }

        /// <summary>
        /// Added Damage MOD 기여도 분석 (Stage 1: 기본 플랫 수집)
        /// </summary>
        private void AnalyzeDamageContribution(FBattleStatus battleStatus, Dictionary<string, double> contributions)
        {
            // ModStageMetadata에서 Spell Damage Stage 1 (기본 플랫 수집) MOD 가져오기
            var flatDamageMods = ModStageMetadata.GetModsForStage("player_to_monster.spell_damage", "1_base_flat_collection");
            var damageModsToCheck = flatDamageMods.Select(m => (EMod)m.mod_id).ToArray();

            foreach (var mod in damageModsToCheck)
            {
                // ========================================================================
                // mod_all_damage 제외 (콘텐츠 기여도 분석에서)
                // ========================================================================
                // 이유:
                // - mod_all_damage는 모든 피해(주문, 상태이상, 오라 등)의 기반이 되는 플랫 피해
                // - 이 값이 0이면 모든 DPS가 0이 되는 필수 기반 MOD
                // - 특정 콘텐츠의 "강점"이 아닌 "전제 조건"에 해당
                // - 밸런싱 조정 시 이 MOD를 건드리면 전체 시스템이 붕괴됨
                //
                // 따라서:
                // - DPS 계산(SimulatorCalculator)에는 정상적으로 포함
                // - 콘텐츠별 비중 분석에서만 제외하여 "조정 가능한 MOD"만 비교
                // ========================================================================
                if (mod == EMod.mod_all_damage)
                    continue;

                if (battleStatus.characterData.ModSources.HasSources((int)mod))
                {
                    var sources = battleStatus.characterData.ModSources.GetSources(mod);
                    foreach (var source in sources)
                    {
                        string category = GetContentCategory(source.sourceType);
                        double contribution = System.Math.Abs(source.value);

                        // Flat Damage는 기본 데미지에 직접 더해지므로 1:1 기여
                        contributions[category] += contribution;

                        // 세부 MOD 기여도 추적
                        string modName = mod.ToString();
                        if (!contentModDetails[category].ContainsKey(modName))
                            contentModDetails[category][modName] = 0;
                        contentModDetails[category][modName] += contribution;

                        // MOD 원본 값 추적
                        if (!contentModRawValues[category].ContainsKey(modName))
                            contentModRawValues[category][modName] = 0;
                        contentModRawValues[category][modName] += System.Math.Abs(source.value);
                    }
                }
            }
        }

        /// <summary>
        /// Increased % MOD 기여도 분석 (Stage 3: Increased 적용)
        /// </summary>
        private void AnalyzeIncContribution(FBattleStatus battleStatus, Dictionary<string, double> contributions)
        {
            // ModStageMetadata에서 Spell Damage Stage 3 (Increased 적용) MOD 가져오기
            var incDamageMods = ModStageMetadata.GetModsForStage("player_to_monster.spell_damage", "3_increased_application");
            var incModsToCheck = incDamageMods.Select(m => (EMod)m.mod_id).ToArray();

            // 기본 데미지 (현재 계산된 값 사용)
            double baseDamage = ParseDisplayValue(totalBaseDamageDisplay);

            foreach (var mod in incModsToCheck)
            {
                if (battleStatus.characterData.ModSources.HasSources((int)mod))
                {
                    var sources = battleStatus.characterData.ModSources.GetSources(mod);
                    foreach (var source in sources)
                    {
                        string category = GetContentCategory(source.sourceType);
                        // Inc %는 기본 데미지에 비례해서 기여
                        // source.value는 이미 GetValue로 배수 변환됨 (0.5 = 50%)
                        // 예: 0.5 (50% Inc) with 1000 base damage = 500 contribution
                        double incContribution = baseDamage * source.value;
                        double contribution = System.Math.Abs(incContribution);

                        contributions[category] += contribution;

                        // 세부 MOD 기여도 추적
                        string modName = mod.ToString();
                        if (!contentModDetails[category].ContainsKey(modName))
                            contentModDetails[category][modName] = 0;
                        contentModDetails[category][modName] += contribution;

                        // MOD 원본 값 추적
                        if (!contentModRawValues[category].ContainsKey(modName))
                            contentModRawValues[category][modName] = 0;
                        contentModRawValues[category][modName] += System.Math.Abs(source.value);
                    }
                }
            }
        }

        /// <summary>
        /// More % MOD 기여도 분석 (Stage 4: More 적용)
        /// </summary>
        private void AnalyzeMoreContribution(FBattleStatus battleStatus, Dictionary<string, double> contributions)
        {
            // ModStageMetadata에서 Spell Damage Stage 4 (More 적용) MOD 가져오기
            var moreDamageMods = ModStageMetadata.GetModsForStage("player_to_monster.spell_damage", "4_more_application");
            var moreModsToCheck = moreDamageMods.Select(m => (EMod)m.mod_id).ToArray();

            // Inc 적용 후 데미지
            double damageAfterInc = ParseDisplayValue(resultAttackerDamageDisplay);

            foreach (var mod in moreModsToCheck)
            {
                if (battleStatus.characterData.ModSources.HasSources((int)mod))
                {
                    var sources = battleStatus.characterData.ModSources.GetSources(mod);
                    foreach (var source in sources)
                    {
                        string category = GetContentCategory(source.sourceType);
                        // More %는 곱셈이므로 현재 데미지에 비례해서 기여
                        // source.value는 이미 GetValue로 배수 변환됨 (0.5 = 50%)
                        // 예: 0.5 (50% More) with 2000 damage = 1000 contribution
                        double moreContribution = damageAfterInc * source.value;
                        double contribution = System.Math.Abs(moreContribution);

                        contributions[category] += contribution;

                        // 세부 MOD 기여도 추적
                        string modName = mod.ToString();
                        if (!contentModDetails[category].ContainsKey(modName))
                            contentModDetails[category][modName] = 0;
                        contentModDetails[category][modName] += contribution;

                        // MOD 원본 값 추적
                        if (!contentModRawValues[category].ContainsKey(modName))
                            contentModRawValues[category][modName] = 0;
                        contentModRawValues[category][modName] += System.Math.Abs(source.value);
                    }
                }
            }
        }

        /// <summary>
        /// Critical MOD 기여도 분석
        /// </summary>
        private void AnalyzeCriticalContribution(FBattleStatus battleStatus, Dictionary<string, double> contributions)
        {
            // Critical Chance
            if (battleStatus.characterData.ModSources.HasSources((int)EMod.mod_crit_blow_chance))
            {
                var sources = battleStatus.characterData.ModSources.GetSources(EMod.mod_crit_blow_chance);
                double avgDamage = ParseDisplayValue(averageHitDamageDisplay);
                double critMultiplier = ParseDisplayValue(criticalMultiplierDisplay) / 100.0;

                foreach (var source in sources)
                {
                    string category = GetContentCategory(source.sourceType);
                    // Crit Chance 기여도 = (CritMultiplier - 1) * AvgDamage * source.value
                    // source.value는 이미 GetValue로 배수 변환됨 (0.05 = 5%)
                    double critContribution = avgDamage * (critMultiplier - 1.0) * source.value;
                    double contribution = System.Math.Abs(critContribution);

                    contributions[category] += contribution;

                    // 세부 MOD 기여도 추적
                    string modName = "mod_crit_blow_chance";
                    if (!contentModDetails[category].ContainsKey(modName))
                        contentModDetails[category][modName] = 0;
                    contentModDetails[category][modName] += contribution;

                    // MOD 원본 값 추적
                    if (!contentModRawValues[category].ContainsKey(modName))
                        contentModRawValues[category][modName] = 0;
                    contentModRawValues[category][modName] += System.Math.Abs(source.value);
                }
            }

            // Critical Multiplier
            if (battleStatus.characterData.ModSources.HasSources((int)EMod.mod_crit_blow_multiplier))
            {
                var sources = battleStatus.characterData.ModSources.GetSources(EMod.mod_crit_blow_multiplier);
                double avgDamage = ParseDisplayValue(averageHitDamageDisplay);
                double critChance = ParseDisplayValue(criticalChanceDisplay) / 100.0;

                foreach (var source in sources)
                {
                    string category = GetContentCategory(source.sourceType);
                    // Crit Multi 기여도 = AvgDamage * CritChance * source.value
                    // source.value는 이미 GetValue로 배수 변환됨
                    double critMultiContribution = avgDamage * critChance * source.value;
                    double contribution = System.Math.Abs(critMultiContribution);

                    contributions[category] += contribution;

                    // 세부 MOD 기여도 추적
                    string modName = "mod_crit_blow_multiplier";
                    if (!contentModDetails[category].ContainsKey(modName))
                        contentModDetails[category][modName] = 0;
                    contentModDetails[category][modName] += contribution;

                    // MOD 원본 값 추적
                    if (!contentModRawValues[category].ContainsKey(modName))
                        contentModRawValues[category][modName] = 0;
                    contentModRawValues[category][modName] += System.Math.Abs(source.value);
                }
            }
        }

        /// <summary>
        /// Cast Speed / Attack Speed MOD 기여도 분석 (Stage 6: 시전 속도)
        /// </summary>
        private void AnalyzeSpeedContribution(FBattleStatus battleStatus, Dictionary<string, double> contributions)
        {
            // ModStageMetadata에서 Spell Damage Stage 6 (시전 속도) MOD 가져오기
            var speedDamageMods = ModStageMetadata.GetModsForStage("player_to_monster.spell_damage", "6_cast_speed");
            var speedModsToCheck = speedDamageMods.Select(m => (EMod)m.mod_id).ToArray();

            double avgDamage = ParseDisplayValue(averageHitDamageDisplay);

            foreach (var mod in speedModsToCheck)
            {
                if (battleStatus.characterData.ModSources.HasSources((int)mod))
                {
                    var sources = battleStatus.characterData.ModSources.GetSources(mod);
                    foreach (var source in sources)
                    {
                        string category = GetContentCategory(source.sourceType);
                        // Speed Inc %는 DPS에 직접 비례
                        // source.value는 이미 GetValue로 배수 변환됨 (0.1 = 10%)
                        double speedContribution = avgDamage * source.value;
                        double contribution = System.Math.Abs(speedContribution);

                        contributions[category] += contribution;

                        // 세부 MOD 기여도 추적
                        string modName = mod.ToString();
                        if (!contentModDetails[category].ContainsKey(modName))
                            contentModDetails[category][modName] = 0;
                        contentModDetails[category][modName] += contribution;

                        // MOD 원본 값 추적
                        if (!contentModRawValues[category].ContainsKey(modName))
                            contentModRawValues[category][modName] = 0;
                        contentModRawValues[category][modName] += System.Math.Abs(source.value);
                    }
                }
            }
        }

        /// <summary>
        /// 저주(Curse) 효과의 간접 기여도 분석
        /// </summary>
        private void AnalyzeCurseContribution(Dictionary<string, double> contributions)
        {
            if (currentDebuffDetails == null || currentDebuffDetails.Count == 0)
                return;

            // 현재 DPS 가져오기
            double totalDPS = ParseDisplayValue(grandTotalDpsDisplay);
            if (totalDPS <= 0)
                return;

            // 각 저주 디버프의 효과 분석
            foreach (var debuffDetail in currentDebuffDetails)
            {
                // 저주로 인한 저항 감소 효과 계산
                double resistanceReduction = 0;

                // 저항 감소 MOD들 확인
                if (debuffDetail.modValues.ContainsKey(EMod.mod_cursed_enemy_fire_resistance_red))
                    resistanceReduction += System.Math.Abs(debuffDetail.modValues[EMod.mod_cursed_enemy_fire_resistance_red]);
                if (debuffDetail.modValues.ContainsKey(EMod.mod_cursed_enemy_cold_resistance_red))
                    resistanceReduction += System.Math.Abs(debuffDetail.modValues[EMod.mod_cursed_enemy_cold_resistance_red]);
                if (debuffDetail.modValues.ContainsKey(EMod.mod_cursed_enemy_lightning_resistance_red))
                    resistanceReduction += System.Math.Abs(debuffDetail.modValues[EMod.mod_cursed_enemy_lightning_resistance_red]);
                if (debuffDetail.modValues.ContainsKey(EMod.mod_cursed_enemy_poison_resistance_red))
                    resistanceReduction += System.Math.Abs(debuffDetail.modValues[EMod.mod_cursed_enemy_poison_resistance_red]);
                if (debuffDetail.modValues.ContainsKey(EMod.mod_cursed_enemy_elemental_resistance_red))
                    resistanceReduction += System.Math.Abs(debuffDetail.modValues[EMod.mod_cursed_enemy_elemental_resistance_red]);

                if (resistanceReduction > 0)
                {
                    // 저항 감소로 인한 데미지 증가 추정
                    // resistanceReduction은 이미 GetValue로 배수 변환됨 (0.4 = 40%)
                    // 몬스터 기본 저항 15%에서 저항 감소만큼 더 들어감
                    double damageMultiplier = resistanceReduction;
                    double curseContribution = totalDPS * damageMultiplier;

                    // 펫-저주 카테고리에 간접 기여도 추가
                    contributions["펫-저주"] += curseContribution;

                    // 세부 정보 추적
                    string curseName = $"curse_effect_{debuffDetail.debuffName}";
                    if (!contentModDetails["펫-저주"].ContainsKey(curseName))
                        contentModDetails["펫-저주"][curseName] = 0;
                    contentModDetails["펫-저주"][curseName] += curseContribution;

                    // 원본 값 추적 (저항 감소 %)
                    if (!contentModRawValues["펫-저주"].ContainsKey(curseName))
                        contentModRawValues["펫-저주"][curseName] = 0;
                    contentModRawValues["펫-저주"][curseName] += resistanceReduction;
                }
            }
        }

        /// <summary>
        /// 스킬 강화도(Reinforce) MOD 기여도 분석
        ///
        /// 실제 DPS 계산에서 스킬 강화도의 역할:
        /// - totalBaseDamage = addedDamage × skillEffectMultiplier
        /// - skillEffectMultiplier = SkillModEffectDamage.Formula.GetValue(effectiveReinforceLevel)
        /// - EXP3 공식: Start × Multiplier^Level
        ///
        /// 강화도 N 추가 시 기여도:
        /// - 현재 배율: M_current = Start × Multiplier^currentLevel
        /// - 추가 후 배율: M_after = Start × Multiplier^(currentLevel + N)
        /// - 배율 증가량: deltaM = M_after - M_current = M_current × (Multiplier^N - 1)
        /// - DPS 기여도: addedDamage × deltaM
        ///
        /// 간략화: 강화도 1당 배율 증가량 ≈ currentMultiplier × (multiplier - 1)
        /// EXP3 기본 multiplier = 1.015 (강화도 1당 약 1.5% 증가)
        /// </summary>
        private void AnalyzeReinforceContribution(FBattleStatus battleStatus, Dictionary<string, double> contributions)
        {
            var reinforceModsToCheck = new EMod[]
            {
                EMod.mod_use_all_skill_reinforce_add,
                EMod.mod_use_fire_skill_reinforce_add,
                EMod.mod_use_cold_skill_reinforce_add,
                EMod.mod_use_lightning_skill_reinforce_add,
                EMod.mod_use_physical_skill_reinforce_add,
                EMod.mod_use_poison_skill_reinforce_add
            };

            double addedDamage = ParseDisplayValue(addedDamageForStep3Display);
            double skillEffectMultiplier = ParseDisplayValue(skillEffectMultiplierDisplay);

            // EXP3 공식의 기본 multiplier (강화도 1당 증가율)
            // 실제 공식에서는 스킬별로 다를 수 있으나, 일반적으로 1.015 (1.5%)
            const double formulaMultiplier = 1.015;

            // 강화도 1당 배율 증가량 = 현재 배율 × (multiplier - 1)
            double multiplierPerReinforce = skillEffectMultiplier * (formulaMultiplier - 1);

            foreach (var mod in reinforceModsToCheck)
            {
                if (battleStatus.characterData.ModSources.HasSources((int)mod))
                {
                    var sources = battleStatus.characterData.ModSources.GetSources(mod);
                    foreach (var source in sources)
                    {
                        string category = GetContentCategory(source.sourceType);

                        // 강화도 N 추가 시 DPS 기여도:
                        // = addedDamage × (N × multiplierPerReinforce / 100)
                        // 예: 강화도 +10, addedDamage 1000M, 배율 100%, multiplier 1.015
                        //    = 1000M × (10 × 100 × 0.015 / 100) = 1000M × 0.15 = 150M
                        double deltaMultiplier = source.value * multiplierPerReinforce;
                        double reinforceContribution = addedDamage * (deltaMultiplier / 100.0);
                        double contribution = System.Math.Abs(reinforceContribution);

                        contributions[category] += contribution;

                        string modName = mod.ToString();
                        if (!contentModDetails[category].ContainsKey(modName))
                            contentModDetails[category][modName] = 0;
                        contentModDetails[category][modName] += contribution;

                        if (!contentModRawValues[category].ContainsKey(modName))
                            contentModRawValues[category][modName] = 0;
                        contentModRawValues[category][modName] += System.Math.Abs(source.value);
                    }
                }
            }
        }

        /// <summary>
        /// 저항 관통(Penetration) MOD 기여도 분석
        ///
        /// 실제 DPS 계산에서 저항 관통의 역할:
        /// - 최종 저항 = Min(기본저항, 최대저항) × (1 - 감소%) - 관통
        /// - 데미지 배율 = 1 - (최종저항 / 100)
        ///
        /// 저항이 R%에서 (R - P)%로 감소하면:
        /// - 원래 배율: 1 - R/100
        /// - 변경 후 배율: 1 - (R-P)/100 = 1 - R/100 + P/100
        /// - 배율 증가량: P/100
        ///
        /// 결론: 관통 1%당 최종 데미지가 1%씩 증가
        /// DPS 기여도 = totalDPS × (penetration / 100)
        /// </summary>
        private void AnalyzePenetrationContribution(FBattleStatus battleStatus, Dictionary<string, double> contributions)
        {
            var penetrationModsToCheck = new EMod[]
            {
                EMod.mod_fire_resistance_penetration,
                EMod.mod_cold_resistance_penetration,
                EMod.mod_lightning_resistance_penetration,
                EMod.mod_poison_resistance_penetration,
                EMod.mod_elemental_resistance_penetration
            };

            double totalDPS = ParseDisplayValue(grandTotalDpsDisplay);

            foreach (var mod in penetrationModsToCheck)
            {
                if (battleStatus.characterData.ModSources.HasSources((int)mod))
                {
                    var sources = battleStatus.characterData.ModSources.GetSources(mod);
                    foreach (var source in sources)
                    {
                        string category = GetContentCategory(source.sourceType);

                        // 저항 관통의 DPS 기여도:
                        // source.value는 이미 GetValue로 배수 변환됨 (0.2 = 20%)
                        // 예: 관통 0.2 (20%), totalDPS 100B = 100B × 0.2 = 20B
                        double penetrationContribution = totalDPS * source.value;
                        double contribution = System.Math.Abs(penetrationContribution);

                        contributions[category] += contribution;

                        string modName = mod.ToString();
                        if (!contentModDetails[category].ContainsKey(modName))
                            contentModDetails[category][modName] = 0;
                        contentModDetails[category][modName] += contribution;

                        if (!contentModRawValues[category].ContainsKey(modName))
                            contentModRawValues[category][modName] = 0;
                        contentModRawValues[category][modName] += System.Math.Abs(source.value);
                    }
                }
            }
        }

        /// <summary>
        /// modAssignByAllStage.md의 모든 stage를 순회하며 MOD 기여도 분석
        /// </summary>
        private void AnalyzeAllStagesFromMetadata(FBattleStatus battleStatus, Dictionary<string, double> contributions)
        {
            // 이미 분석된 MOD 추적 (중복 방지)
            var analyzedMods = new HashSet<EMod>();

            // 분석할 formula_type 목록 (modAssignByAllStage.md 기준)
            var formulaTypesToAnalyze = new List<(string formulaType, string[] stageIds)>
            {
                // player_to_monster.spell_damage - 나머지 stage들 (1, 3, 4, 5, 6은 이미 분석됨)
                ("player_to_monster.spell_damage", new[] {
                    "7_multiple_damage",
                    "8_projectile_mechanics",
                    "9_skill_mechanics",
                    "10_on_kill_effects",
                    "11_instant_kill"
                }),

                // player_to_monster.ailment_damage - 전체 stage
                ("player_to_monster.ailment_damage", new[] {
                    "0_ailment_restrictions",
                    "2_ailment_proc_chance",
                    "3_ailment_duration",
                    "4_ailment_inc_more"
                }),

                // player_to_monster.aura_damage - 전체 stage
                ("player_to_monster.aura_damage", new[] {
                    "1_base_aura_damage",
                    "2_aura_duration_tick",
                    "3_aura_inc",
                    "4_aura_more"
                }),

                // player_to_monster.buff - 전체 stage
                ("player_to_monster.buff", new[] {
                    "1_killingspree",
                    "2_backstab",
                    "3_ignorepain",
                    "4_frenzy",
                    "5_invisibility",
                    "6_surgeofpower"
                }),

                // player_to_monster.curse - 전체 stage (일부는 AnalyzeCurseContribution에서 분석됨)
                ("player_to_monster.curse", new[] {
                    "1_curse_skill_casting",
                    "2_curse_effect_application",
                    "3_ailment_synergy",
                    "4_resistance_weakness",
                    "5_combat_debuffs"
                }),

                // player_to_monster.defense - 전체 stage
                ("player_to_monster.defense", new[] {
                    "1_hit_determination",
                    "2_immunity_check",
                    "3_armor_application",
                    "4_resistance_application",
                    "5_damage_taken_modifiers"
                }),

                // player_to_monster.movement
                ("player_to_monster.movement", new[] {
                    "1_movement_speed"
                }),

                // player_to_monster.resource
                ("player_to_monster.resource", new[] {
                    "1_life",
                    "2_mana",
                    "3_mana_cost",
                    "4_life_leech",
                    "5_mana_leech",
                    "6_leech_restrictions",
                    "7_rewards"
                })
            };

            // 각 formula_type과 stage를 순회하며 MOD 분석
            foreach (var (formulaType, stageIds) in formulaTypesToAnalyze)
            {
                foreach (var stageId in stageIds)
                {
                    try
                    {
                        AnalyzeStageMods(battleStatus, contributions, formulaType, stageId, analyzedMods);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[Simulator] Stage MOD 분석 실패: {formulaType}.{stageId} - {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 특정 stage의 MOD 기여도 분석
        /// </summary>
        private void AnalyzeStageMods(
            FBattleStatus battleStatus,
            Dictionary<string, double> contributions,
            string formulaType,
            string stageId,
            HashSet<EMod> analyzedMods)
        {
            var stageMods = ModStageMetadata.GetModsForStage(formulaType, stageId);
            if (stageMods == null || stageMods.Count == 0)
                return;

            foreach (var modMetadata in stageMods)
            {
                var mod = (EMod)modMetadata.mod_id;

                // 중복 분석 방지 (이미 분석된 MOD는 스킵)
                if (analyzedMods.Contains(mod))
                    continue;

                // MOD 소스가 없으면 스킵
                if (!battleStatus.characterData.ModSources.HasSources((int)mod))
                    continue;

                var sources = battleStatus.characterData.ModSources.GetSources(mod);
                foreach (var source in sources)
                {
                    string category = GetContentCategory(source.sourceType);

                    // MOD 타입에 따라 기여도 계산
                    double contribution = CalculateModContribution(mod, source.value, formulaType, stageId);

                    if (contribution > 0)
                    {
                        contributions[category] += contribution;

                        // 세부 MOD 기여도 추적
                        string modName = mod.ToString();
                        if (!contentModDetails[category].ContainsKey(modName))
                            contentModDetails[category][modName] = 0;
                        contentModDetails[category][modName] += contribution;

                        // MOD 원본 값 추적
                        if (!contentModRawValues[category].ContainsKey(modName))
                            contentModRawValues[category][modName] = 0;
                        contentModRawValues[category][modName] += System.Math.Abs(source.value);
                    }
                }

                // 분석 완료 표시
                analyzedMods.Add(mod);
            }
        }

        /// <summary>
        /// MOD 타입에 따른 기여도 계산
        ///
        /// 원칙:
        /// - DPS에 직접 영향을 주는 MOD만 기여도 계산
        /// - DPS에 직접 영향을 주지 않는 MOD는 0 반환
        /// - 임의의 배수 사용 금지 (실제 DPS 계산 공식 기반)
        /// </summary>
        private double CalculateModContribution(EMod mod, double modValue, string formulaType, string stageId)
        {
            // 기본 데미지 및 DPS 값
            double baseDamage = ParseDisplayValue(totalBaseDamageDisplay);
            double damageAfterInc = ParseDisplayValue(resultAttackerDamageDisplay);
            double avgDamage = ParseDisplayValue(averageHitDamageDisplay);
            double totalDPS = ParseDisplayValue(grandTotalDpsDisplay);

            // stage별 기여도 계산 방식
            if (formulaType == "player_to_monster.spell_damage")
            {
                if (stageId.Contains("base_flat") || stageId.Contains("1_base"))
                {
                    // Flat Damage: 1:1 기여
                    return System.Math.Abs(modValue);
                }
                else if (stageId.Contains("increased") || stageId.Contains("3_increased"))
                {
                    // Increased %: 기본 데미지에 비례
                    // 공식: baseDamage × (1 + Inc합) → modValue는 이미 비율 (0.2 = 20%)
                    return baseDamage * System.Math.Abs(modValue);
                }
                else if (stageId.Contains("more") || stageId.Contains("4_more"))
                {
                    // More %: Inc 적용 후 데미지에 비례
                    // 공식: damageAfterInc × (1 + More) → modValue는 이미 비율 (0.2 = 20%)
                    return damageAfterInc * System.Math.Abs(modValue);
                }
                else if (stageId.Contains("cast_speed") || stageId.Contains("6_cast"))
                {
                    // Cast Speed: DPS = 평균 데미지 × 시전속도
                    // modValue는 이미 비율 (0.2 = 20% 증가)
                    return avgDamage * System.Math.Abs(modValue);
                }
                else if (stageId.Contains("multiple") || stageId.Contains("7_multiple"))
                {
                    // Multiple Damage (더블 스트라이크 등): 평균 데미지에 비례
                    // modValue는 이미 비율 (0.2 = 20%)
                    return avgDamage * System.Math.Abs(modValue);
                }
                else if (stageId.Contains("projectile") || stageId.Contains("8_projectile"))
                {
                    // 추가 발사체(mod_projectile_additional_fire)는 기여도 계산에서 제외
                    // 이유: 각 발사체가 개별 피해를 주므로 DPS 배율에 포함하면 안 됨
                    // 기타 투사체 메커닉 (속도, 크기 등): DPS에 직접 영향 없음
                    return 0;
                }
                else if (stageId.Contains("skill_mechanics") || stageId.Contains("9_skill"))
                {
                    // Skill Mechanics (AoE, Cooldown 등): DPS에 직접 영향 없음
                    // - AoE 증가: 데미지가 아닌 범위만 증가
                    // - Cooldown 감소: DPS 계산에 이미 반영됨
                    return 0;
                }
                else if (stageId.Contains("on_kill") || stageId.Contains("10_on_kill"))
                {
                    // On Kill Effects: DPS에 직접 영향 없음 (처치 후 효과)
                    return 0;
                }
                else if (stageId.Contains("instant_kill") || stageId.Contains("11_instant"))
                {
                    // Instant Kill: 즉사 확률은 DPS 계산과 별개
                    return 0;
                }
            }
            else if (formulaType == "player_to_monster.ailment_damage")
            {
                // 상태이상 DPS 계산 공식:
                // ailmentDPS = flatDamage × damagePercent × incMultiplier × moreMultiplier × (procChance / 100)
                //
                // procChance, Duration은 이미 ailmentDPS에 반영됨
                // Inc/More만 별도 계산

                double ailmentDPS = ParseDisplayValue(ailmentTotalDpsDisplay);

                if (stageId.Contains("proc_chance") || stageId.Contains("2_ailment_proc"))
                {
                    // Ailment Proc Chance: 이미 ailmentDPS에 반영됨
                    return 0;
                }
                else if (stageId.Contains("duration") || stageId.Contains("3_ailment_duration"))
                {
                    // Ailment Duration: DPS에 직접 영향 없음 (maxStackDPS 사용)
                    return 0;
                }
                else if (stageId.Contains("inc_more") || stageId.Contains("4_ailment_inc"))
                {
                    // Ailment Inc/More: ailmentDPS 기준으로 계산
                    // modValue는 이미 비율 (0.2 = 20%)
                    if (mod.ToString().Contains("_inc"))
                        return ailmentDPS * System.Math.Abs(modValue);
                    else if (mod.ToString().Contains("_more"))
                        return ailmentDPS * System.Math.Abs(modValue);
                }
            }
            else if (formulaType == "player_to_monster.aura_damage")
            {
                // Aura DPS 계산 공식:
                // auraDPS = flatDamage × damagePercent × incMultiplier × moreMultiplier

                double auraDPS = ParseDisplayValue(auraTotalDpsDisplay);

                if (stageId.Contains("base_aura") || stageId.Contains("1_base"))
                {
                    // Base Aura Damage: Flat 추가 피해
                    return System.Math.Abs(modValue);
                }
                else if (stageId.Contains("duration") || stageId.Contains("2_aura_duration"))
                {
                    // Aura Duration: DPS에 직접 영향 없음
                    return 0;
                }
                else if (stageId.Contains("aura_inc") || stageId.Contains("3_aura_inc"))
                {
                    // Aura Inc: auraDPS 기준으로 계산
                    // modValue는 이미 비율 (0.2 = 20%)
                    return auraDPS * System.Math.Abs(modValue);
                }
                else if (stageId.Contains("aura_more") || stageId.Contains("4_aura_more"))
                {
                    // Aura More: auraDPS 기준으로 계산
                    // modValue는 이미 비율 (0.2 = 20%)
                    return auraDPS * System.Math.Abs(modValue);
                }
            }
            else if (formulaType == "player_to_monster.buff")
            {
                // Buff: DPS에 직접 영향 없음 (버프 효과는 별도 시스템)
                return 0;
            }
            else if (formulaType == "player_to_monster.curse")
            {
                // Curse: AnalyzeCurseContribution에서 별도 분석
                return 0;
            }
            else if (formulaType == "player_to_monster.defense")
            {
                // Defense stages:
                // 1_hit_determination: 명중 판정 - DPS에 직접 영향 없음 (이미 hitRate에 반영됨)
                // 2_immunity_check: 면역 체크 - DPS에 직접 영향 없음
                // 3_armor_application: 방어력 적용 - 물리 관통은 AnalyzePenetrationContribution에서 처리
                // 4_resistance_application: 저항 적용 - 저항 관통/감소는 AnalyzePenetrationContribution에서 처리
                // 5_damage_taken_modifiers: 받는 피해 증감 - DPS에 직접 영향

                if (stageId.Contains("damage_taken") || stageId.Contains("5_damage"))
                {
                    // 받는 피해 증가 MOD: DPS에 직접 영향
                    // 공식: 최종 피해 × (1 + damageTaken%)
                    // modValue는 이미 비율 (0.2 = 20%)
                    return totalDPS * System.Math.Abs(modValue);
                }

                // 나머지 defense stage는 다른 곳에서 처리되거나 DPS에 직접 영향 없음
                return 0;
            }
            else if (formulaType == "player_to_monster.movement")
            {
                // Movement: DPS에 직접 영향 없음
                return 0;
            }
            else if (formulaType == "player_to_monster.resource")
            {
                // Resource: DPS에 직접 영향 없음
                return 0;
            }

            // 기본값: DPS에 영향 없는 MOD
            return 0;
        }

        /// <summary>
        /// Phase 2: MOD 타입별 분포 분석
        /// </summary>
        private Dictionary<string, Dictionary<string, double>> AnalyzeModTypeDistribution()
        {
            // [MOD 타입][콘텐츠] = 기여도
            var typeDistributions = new Dictionary<string, Dictionary<string, double>>();

            // MOD 타입 카테고리 초기화
            var modTypes = new[] { "Flat", "Increased", "More", "Critical", "Speed", "Penetration", "Other" };
            foreach (var type in modTypes)
            {
                typeDistributions[type] = new Dictionary<string, double>();
            }

            // 각 콘텐츠별로 MOD를 타입별로 분류
            foreach (var contentKvp in contentModDetails)
            {
                string category = contentKvp.Key;
                foreach (var modKvp in contentKvp.Value)
                {
                    string modName = modKvp.Key;
                    double value = modKvp.Value;

                    // MOD 이름으로 타입 판별
                    if (modName.Contains("mod_all_damage") || modName.Contains("mod_fire_damage") ||
                        modName.Contains("mod_cold_damage") || modName.Contains("mod_lightning_damage") ||
                        modName.Contains("mod_poison_damage") || modName.Contains("mod_physical_damage") ||
                        modName.Contains("mod_elemental_damage"))
                    {
                        if (!typeDistributions["Flat"].ContainsKey(category))
                            typeDistributions["Flat"][category] = 0;
                        typeDistributions["Flat"][category] += value;
                    }
                    else if (modName.Contains("_inc") && !modName.Contains("castspeed") && !modName.Contains("crit"))
                    {
                        if (!typeDistributions["Increased"].ContainsKey(category))
                            typeDistributions["Increased"][category] = 0;
                        typeDistributions["Increased"][category] += value;
                    }
                    else if (modName.Contains("_more"))
                    {
                        if (!typeDistributions["More"].ContainsKey(category))
                            typeDistributions["More"][category] = 0;
                        typeDistributions["More"][category] += value;
                    }
                    else if (modName.Contains("castspeed"))
                    {
                        if (!typeDistributions["Speed"].ContainsKey(category))
                            typeDistributions["Speed"][category] = 0;
                        typeDistributions["Speed"][category] += value;
                    }
                    else if (modName.Contains("penetration"))
                    {
                        if (!typeDistributions["Penetration"].ContainsKey(category))
                            typeDistributions["Penetration"][category] = 0;
                        typeDistributions["Penetration"][category] += value;
                    }
                    else if (modName.Contains("crit"))
                    {
                        if (!typeDistributions["Critical"].ContainsKey(category))
                            typeDistributions["Critical"][category] = 0;
                        typeDistributions["Critical"][category] += value;
                    }
                }
            }

            return typeDistributions;
        }

        /// <summary>
        /// Phase 1: 콘텐츠 제거 시뮬레이션
        /// </summary>
        private Dictionary<string, double> SimulateContentRemoval(FBattleStatus battleStatus)
        {
            var removalImpact = new Dictionary<string, double>();

            if (battleStatus == null || battleStatus.characterData?.ModSources == null)
                return removalImpact;

            // 현재 총 DPS
            double currentTotalDPS = ParseDisplayValue(grandTotalDpsDisplay);
            if (currentTotalDPS <= 0)
                return removalImpact;

            // 각 콘텐츠별 기여도를 DPS 감소율로 변환
            // contentModDetails를 사용하여 각 콘텐츠의 MOD를 제거했을 때의 영향을 추정
            foreach (var category in contentModDetails.Keys)
            {
                double categoryContribution = 0;
                if (contentModDetails.ContainsKey(category))
                {
                    categoryContribution = contentModDetails[category].Values.Sum();
                }

                // 기여도를 DPS 감소율로 변환
                // 근사값: 기여도 / 총 DPS * 100
                double reductionPercentage = currentTotalDPS > 0 ? (categoryContribution / currentTotalDPS * 100.0) : 0;

                // 최소/최대 제한
                reductionPercentage = System.Math.Max(0, System.Math.Min(100, reductionPercentage));

                removalImpact[category] = reductionPercentage;
            }

            return removalImpact;
        }

        /// <summary>
        /// Phase 3: 밸런스 조정 가이드 계산
        /// </summary>
        private Dictionary<string, string> CalculateBalanceAdjustments(Dictionary<string, double> removalImpact)
        {
            var adjustments = new Dictionary<string, string>();

            // 목표 중요도 설정 (우선순위: 각성 > 스킬 > 장비 > 성운/성좌 > 플레이어 강화 > 플레이어 성장)
            var targetImportance = new Dictionary<string, double>
            {
                ["각성"] = 35.0,  // 주력 콘텐츠, 압도적이지만 독점 아님
                ["스킬-주문"] = 20.0,
                ["스킬-오라"] = 10.0,
                ["장비"] = 25.0,
                ["성운"] = 5.0,
                ["성좌"] = 8.0,
                ["캐릭터-강화"] = 12.0,
                ["캐릭터-성장"] = 3.0,  // 골드 성장이므로 가장 낮음
                ["캐릭터-기본"] = 5.0,
                ["펫"] = 3.0,
                ["펫-저주"] = 4.0
            };

            foreach (var kvp in removalImpact)
            {
                string category = kvp.Key;
                double currentImpact = kvp.Value;

                if (!targetImportance.ContainsKey(category))
                    continue;

                double target = targetImportance[category];
                double difference = currentImpact - target;

                // 조정이 필요한 경우
                if (System.Math.Abs(difference) > 2.0) // 2% 이상 차이
                {
                    // Inc MOD 기준으로 조정량 계산
                    if (contentModRawValues.ContainsKey(category))
                    {
                        double incSum = 0;
                        foreach (var modKvp in contentModRawValues[category])
                        {
                            if (modKvp.Key.Contains("_inc") && !modKvp.Key.Contains("castspeed") &&
                                !modKvp.Key.Contains("crit") && !modKvp.Key.Contains("curse"))
                            {
                                incSum += modKvp.Value;
                            }
                        }

                        if (incSum > 0)
                        {
                            // 조정 비율 계산
                            double adjustmentRatio = target / currentImpact;
                            double newIncValue = incSum * adjustmentRatio;
                            double changeAmount = newIncValue - incSum;
                            double changePercentage = (changeAmount / incSum) * 100.0;

                            string direction = changePercentage > 0 ? "증가" : "감소";
                            adjustments[category] = $"Inc {incSum:F1}% → {newIncValue:F1}% ({changePercentage:+0.0;-0.0}% {direction})";
                        }
                    }
                }
                else
                {
                    adjustments[category] = "적정 수준 ✓";
                }
            }

            return adjustments;
        }

        #endregion

        #region Helper 메서드

        /// <summary>
        /// 표시용 문자열에서 숫자 값 추출
        /// </summary>
        private double ParseDisplayValue(string displayValue)
        {
            if (string.IsNullOrEmpty(displayValue))
                return 0;

            // "1.23K", "4.56M", "123,456" 같은 형식 처리
            string cleaned = displayValue.Replace(",", "").Trim();

            // 단위 처리
            double multiplier = 1.0;
            if (cleaned.EndsWith("K") || cleaned.EndsWith("k"))
            {
                multiplier = 1000.0;
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }
            else if (cleaned.EndsWith("M") || cleaned.EndsWith("m"))
            {
                multiplier = 1000000.0;
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }
            else if (cleaned.EndsWith("B") || cleaned.EndsWith("b"))
            {
                multiplier = 1000000000.0;
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }

            // 숫자만 추출 (첫 번째 공백이나 괄호 전까지)
            int spaceIndex = cleaned.IndexOf(' ');
            if (spaceIndex > 0)
                cleaned = cleaned.Substring(0, spaceIndex);

            int parenIndex = cleaned.IndexOf('(');
            if (parenIndex > 0)
                cleaned = cleaned.Substring(0, parenIndex);

            if (double.TryParse(cleaned, out double result))
            {
                return result * multiplier;
            }

            return 0;
        }

        /// <summary>
        /// EModSourceType을 세분화된 콘텐츠 카테고리로 변환
        /// </summary>
        private string GetContentCategory(EModSourceType sourceType)
        {
            // 명시적으로 각 타입별 분류
            switch (sourceType)
            {
                // 캐릭터 시스템 (1-3)
                case EModSourceType.PlayerDefault:
                    return "캐릭터-기본";
                case EModSourceType.PlayerGrowth:
                    return "캐릭터-성장";
                case EModSourceType.PlayerReinforce:
                    return "캐릭터-강화";

                // 장비 시스템 (100-103) - 통합
                case EModSourceType.Equipment_Normal:
                case EModSourceType.Equipment_Common:
                case EModSourceType.Equipment_MythicUnique:
                case EModSourceType.Equipment_Possession:
                    return "장비";

                // 스킬 시스템 - 주문 (200-202)
                case EModSourceType.Skill_Spell:
                case EModSourceType.Skill_SpellMaster:
                case EModSourceType.Skill_Rune:
                    return "스킬-주문";

                // 스킬 시스템 - 오라 (210-211)
                case EModSourceType.Skill_Aura:
                case EModSourceType.Skill_AuraMaster:
                    return "스킬-오라";

                // 펫 저주 (220)
                case EModSourceType.Skill_PetCurse:
                    return "펫-저주";

                // 각성 시스템 (300-301) - 통합
                case EModSourceType.Awaken_Grade:
                case EModSourceType.Awaken_Element:
                    return "각성";

                // 성운 (400)
                case EModSourceType.Nebula:
                    return "성운";

                // 성좌 (500)
                case EModSourceType.Constellation:
                    return "성좌";

                // 펫 (600)
                case EModSourceType.Pet:
                    return "펫";

                // 몬스터 (700)
                case EModSourceType.Monster:
                    return "몬스터";

                // 버프 (800)
                case EModSourceType.Buff:
                    return "버프";

                // 디버프 (900)
                case EModSourceType.Debuff:
                    return "디버프";

                // 기타 (조합형 MOD 등)
                default:
                    return "조합형MOD";
            }
        }

        #endregion
    }
}
