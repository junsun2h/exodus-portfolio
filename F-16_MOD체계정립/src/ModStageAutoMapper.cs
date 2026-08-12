using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// MOD 자동 할당 엔진
    /// - EMod enum 네이밍 패턴 분석
    /// - 공식 단계에 자동 매핑
    /// - 379개 MOD 자동 할당
    /// </summary>
    public static class ModStageAutoMapper
    {
        /// <summary>
        /// 자동 할당 결과
        /// </summary>
        public class AutoMappingResult
        {
            public int totalMods;                           // 전체 MOD 수
            public int successCount;                        // 성공한 MOD 수
            public int failureCount;                        // 실패한 MOD 수
            public int alreadyAssignedCount;                // 이미 할당된 MOD 수
            public Dictionary<string, int> stageDistribution;  // 단계별 분포
            public List<string> failedMods;                 // 실패한 MOD 목록
            public List<string> ambiguousMods;              // 모호한 MOD 목록 (여러 단계 후보)

            public AutoMappingResult()
            {
                stageDistribution = new Dictionary<string, int>();
                failedMods = new List<string>();
                ambiguousMods = new List<string>();
            }

            public string GetSummary()
            {
                var summary = $"=== MOD 자동 할당 결과 ===\n\n";
                summary += $"📁 저장 파일: Assets/Editor/BattleSimulator/Data/ModAssigner/mod_stage_metadata.json\n\n";
                summary += $"전체 MOD: {totalMods}개\n";
                summary += $"성공: {successCount}개 ({(float)successCount / totalMods * 100:F1}%)\n";
                summary += $"실패: {failureCount}개 ({(float)failureCount / totalMods * 100:F1}%)\n";
                summary += $"이미 할당됨: {alreadyAssignedCount}개\n\n";

                summary += "=== 단계별 분포 ===\n";
                foreach (var kvp in stageDistribution.OrderBy(x => x.Key))
                {
                    summary += $"{kvp.Key}: {kvp.Value}개\n";
                }

                if (ambiguousMods.Count > 0)
                {
                    summary += $"\n⚠️ 모호한 MOD: {ambiguousMods.Count}개\n";
                    summary += string.Join("\n", ambiguousMods.Take(10));
                    if (ambiguousMods.Count > 10) summary += $"\n... 외 {ambiguousMods.Count - 10}개";
                }

                if (failedMods.Count > 0)
                {
                    summary += $"\n\n❌ 매핑 실패 MOD: {failedMods.Count}개\n";
                    summary += string.Join("\n", failedMods.Take(10));
                    if (failedMods.Count > 10) summary += $"\n... 외 {failedMods.Count - 10}개";
                }

                return summary;
            }
        }

        /// <summary>
        /// 모든 EMod를 자동으로 할당
        /// </summary>
        public static AutoMappingResult AutoAssignAllMods(bool overwriteExisting = false)
        {
            var result = new AutoMappingResult();
            var allMods = Enum.GetValues(typeof(EMod)).Cast<EMod>().Where(m => m != EMod.None).ToList();
            result.totalMods = allMods.Count;

            // 전체 재할당 시: 메타데이터 완전 초기화
            if (overwriteExisting)
            {
                ModStageMetadata.ClearAllData();
            }

            foreach (var mod in allMods)
            {
                string modName = mod.ToString();

                // 증분 할당 모드: 이미 할당된 MOD 스킵
                if (!overwriteExisting)
                {
                    var existingStages = ModStageMetadata.GetStagesForMod(mod);
                    if (existingStages != null && existingStages.Count > 0)
                    {
                        result.alreadyAssignedCount++;
                        continue;
                    }
                }
                // 전체 재할당 모드: 이미 완전 초기화 완료 (84번 줄)

                // 우선순위 1: ManualStageAssignments에서 찾기
                bool foundInManual = false;
                foreach (var kvp in ManualStageAssignments)
                {
                    if (kvp.Value.Contains(modName))
                    {
                        // "formula_type:stage_id" 형식 파싱
                        string[] parts = kvp.Key.Split(':');
                        if (parts.Length == 2)
                        {
                            string formulaType = parts[0];
                            string stageId = parts[1];
                            string stageName = GetStageNameById(stageId);

                            ModStageMetadata.AssignModToStage(mod, formulaType, stageId, stageName);

                            if (!result.stageDistribution.ContainsKey(stageName))
                                result.stageDistribution[stageName] = 0;
                            result.stageDistribution[stageName]++;
                            result.successCount++;
                            foundInManual = true;
                        }
                    }
                }

                if (foundInManual) continue;

                // 우선순위 2: 패턴 매칭으로 단계 결정
                var candidates = AnalyzeModPattern(modName);

                if (candidates.Count == 0)
                {
                    // 매핑 실패 (제외된 MOD도 여기 포함됨)
                    result.failedMods.Add(modName);
                    result.failureCount++;
                }
                else if (candidates.Count == 1)
                {
                    // 명확한 매핑
                    var stage = candidates[0];
                    // formulaType이 있으면 사용, 없으면 기본값 "spell_damage"
                    string formulaType = !string.IsNullOrEmpty(stage.formulaType) ? stage.formulaType : "spell_damage";
                    ModStageMetadata.AssignModToStage(mod, formulaType, stage.stageId, stage.stageName);

                    if (!result.stageDistribution.ContainsKey(stage.stageName))
                        result.stageDistribution[stage.stageName] = 0;
                    result.stageDistribution[stage.stageName]++;
                    result.successCount++;
                }
                else
                {
                    // 모호한 매핑 (첫 번째 후보 사용)
                    var stage = candidates[0];
                    // formulaType이 있으면 사용, 없으면 기본값 "spell_damage"
                    string formulaType = !string.IsNullOrEmpty(stage.formulaType) ? stage.formulaType : "spell_damage";
                    ModStageMetadata.AssignModToStage(mod, formulaType, stage.stageId, stage.stageName);

                    result.ambiguousMods.Add($"{mod} → {stage.stageName} (후보: {candidates.Count}개)");
                    if (!result.stageDistribution.ContainsKey(stage.stageName))
                        result.stageDistribution[stage.stageName] = 0;
                    result.stageDistribution[stage.stageName]++;
                    result.successCount++;
                }
            }

            return result;
        }

        /// <summary>
        /// 수동 할당 MOD 목록 (formula_type과 stage_id 매핑)
        /// 키 형식: "formula_type:stage_id"
        /// </summary>
        private static readonly Dictionary<string, HashSet<string>> ManualStageAssignments = new Dictionary<string, HashSet<string>>
        {
            // Spell Damage 공식
            ["player_to_monster.spell_damage:1_base_flat_collection"] = new HashSet<string>
            {
                "mod_all_damage",
                "mod_all_skill_damage",
                "mod_elemental_damage",
                "mod_physical_damage",
                "mod_fire_damage",
                "mod_lightning_damage",
                "mod_cold_damage",
                "mod_poison_damage",
                // 스킬 강화도 증가 MOD (reinforce level 상승)
                "mod_use_all_skill_reinforce_add",
                "mod_use_fire_skill_reinforce_add",
                "mod_use_cold_skill_reinforce_add",
                "mod_use_lightning_skill_reinforce_add",
                "mod_use_physical_skill_reinforce_add",
                "mod_use_poison_skill_reinforce_add"
            },
            ["player_to_monster.spell_damage:3_increased_application"] = new HashSet<string>
            {
                "mod_all_damage_inc",
                "mod_all_skill_damage_inc",
                "mod_cold_damage_inc",
                "mod_damage_inc_on_full_life",
                "mod_damage_inc_on_low_life",
                "mod_elemental_damage_inc",
                "mod_fire_damage_inc",
                "mod_lightning_damage_inc",
                "mod_lightning_damage_inc_per_lightning_resistance", // 번개 저항만큼 번개 피해 % 증가
                "mod_physical_damage_inc",
                "mod_poison_damage_inc",
                "mod_projectile_damage_inc",
                // Ailment 상태 적에게 피해 증가
                "mod_inc_damage_arctic",
                "mod_inc_damage_bleeding",
                "mod_inc_damage_chill",
                "mod_inc_damage_ignite",
                "mod_inc_damage_paralyze",
                "mod_inc_damage_poisoning",
                "mod_inc_damage_shock",
                "mod_inc_damage_stun"
            },
            ["player_to_monster.spell_damage:4_more_application"] = new HashSet<string>
            {
                "mod_all_damage_more",
                "mod_all_skill_damage_more",
                "mod_cold_damage_more",
                "mod_fire_damage_more",
                "mod_lightning_damage_more",
                "mod_physical_damage_more",
                "mod_poison_damage_more",
                "mod_projectile_damage_more"
            },
            ["player_to_monster.spell_damage:5_critical_strike"] = new HashSet<string>
            {
                // 기본 치명타 MOD
                "mod_cannot_crit",
                "mod_crit_blow_chance",
                "mod_crit_blow_chance_inc",
                "mod_crit_blow_multiplier",
                "mod_crit_blow_multiplier_inc",
                "mod_crit_chance",
                "mod_crit_chance_inc",
                "mod_crit_multiplier",
                "mod_crit_multiplier_inc",
                "mod_cursed_enemy_crit_chance",
                "mod_cursed_enemy_crit_chance_inc",
                "mod_cursed_enemy_crit_multiplier",
                // Ailment 상태 적에게 치명타 확률 증가
                "mod_inc_crit_chance_arctic",
                "mod_inc_crit_chance_bleeding",
                "mod_inc_crit_chance_chill",
                "mod_inc_crit_chance_ignite",
                "mod_inc_crit_chance_paralyze",
                "mod_inc_crit_chance_poisoning",
                "mod_inc_crit_chance_shock",
                "mod_inc_crit_chance_stun"
            },
            ["player_to_monster.spell_damage:6_cast_speed"] = new HashSet<string>
            {
                "mod_castspeed_inc",
                "mod_castspeed_inc_on_full_life",
                "mod_castspeed_inc_on_low_life",
                "mod_castspeed_inc_when_kill_recently"
            },

            // [Spell] 7단계: 멀티플 피해 (Double/Triple)
            ["player_to_monster.spell_damage:7_multiple_damage"] = new HashSet<string>
            {
                "mod_chance_to_double_damage",
                "mod_chance_to_triple_damage"
            },

            // [Spell] 8단계: 투사체 메커닉
            ["player_to_monster.spell_damage:8_projectile_mechanics"] = new HashSet<string>
            {
                "mod_projectile_additional_fire",
                "mod_projectile_cannot_chain",
                "mod_projectile_cannot_pierce",
                "mod_projectile_chain_additional_target",
                "mod_projectile_pierce_additional_target",
                "mod_projectile_pierce_all",
                "mod_projectile_speed_inc",
                "mod_skill_additional_chain",
                "mod_skill_additional_chain_speed_inc",
                "mod_skill_additional_target"
            },

            // [Spell] 9단계: 스킬 메커닉 (AoE, Cooltime)
            ["player_to_monster.spell_damage:9_skill_mechanics"] = new HashSet<string>
            {
                // AoE 범위
                "mod_all_aoe_radius",
                "mod_all_aoe_radius_inc",
                "mod_skill_aoe_radius",
                "mod_skill_aoe_radius_inc",
                // 쿨다운
                "mod_skill_cooltime",
                "mod_skill_cooltime_red"
            },

            // [Spell] 10단계: 적 처치 시 발동 효과
            ["player_to_monster.spell_damage:10_on_kill_effects"] = new HashSet<string>
            {
                // 적 처치 시 폭발 (시체폭발) - 5가지 속성
                "mod_enemy_when_kill_explode_deal_life_as_cold",
                "mod_enemy_when_kill_explode_deal_life_as_fire",
                "mod_enemy_when_kill_explode_deal_life_as_lightning",
                "mod_enemy_when_kill_explode_deal_life_as_physical",
                "mod_enemy_when_kill_explode_deal_life_as_poison",
                // 적 처치 시 회복
                "mod_recover_life_when_kill",
                "mod_recover_mana_when_kill"
            },

            // [Spell] 11단계: 즉사 판정
            ["player_to_monster.spell_damage:11_instant_kill"] = new HashSet<string>
            {
                "mod_instantkill_lowerlife"
            },

            // ========================================
            // Curse 공식
            // ========================================
            //
            // ⚠️ Curse와 Debuff의 관계:
            // - Curse 스킬 (skillcurse_weak_physical 등)은 적에게 Debuff (debuff_weak_physical 등)를 발생시킵니다.
            // - Debuff는 Curse 스킬의 Tier MOD들을 CoreBuffFata.CurseValueDic을 통해 전달받아 BattleStatus에 적용합니다.
            // - 따라서 Debuff 자체를 위한 별도 MOD 할당은 필요하지 않으며, Curse 공식의 MOD들이 Debuff에도 사용됩니다.
            // - GameDB_Client_Skill.Buff 속성으로 Curse 스킬과 Debuff가 매핑됩니다.
            //   예: skillcurse_weak_physical → debuff_weak_physical
            //       skillcurse_weak_fire → debuff_weak_fire
            //       skillcurse_weak_cold → debuff_weak_cold
            //       skillcurse_weak_lightning → debuff_weak_lightning
            //       skillcurse_weak_poison → debuff_weak_poison
            //       skillcurse_weak_crit → debuff_weak_crit

            // [Curse] 1단계: 저주 스킬 시전
            ["player_to_monster.curse:1_curse_skill_casting"] = new HashSet<string>
            {
                "mod_curse_skill_aoe_radius_inc",
                "mod_curse_skill_cooltime",
                "mod_curse_skill_cooltime_red"
            },

            // [Curse] 2단계: 저주 효과 적용
            ["player_to_monster.curse:2_curse_effect_application"] = new HashSet<string>
            {
                "mod_curse_skill_duration_inc_on_enemy"
            },

            // [Curse] 3단계: Ailment 시너지
            ["player_to_monster.curse:3_ailment_synergy"] = new HashSet<string>
            {
                "mod_cursed_enemy_chance_to_arctic",
                "mod_cursed_enemy_chance_to_bleeding",
                "mod_cursed_enemy_chance_to_chill",
                "mod_cursed_enemy_chance_to_ignite",
                "mod_cursed_enemy_chance_to_paralyze",
                "mod_cursed_enemy_chance_to_poisoning",
                "mod_cursed_enemy_chance_to_shock",
                "mod_cursed_enemy_chance_to_stun"
            },

            // [Curse] 4단계: 저항 약화
            ["player_to_monster.curse:4_resistance_weakness"] = new HashSet<string>
            {
                "mod_cursed_enemy_cold_resistance",
                "mod_cursed_enemy_cold_resistance_red",
                "mod_cursed_enemy_fire_resistance",
                "mod_cursed_enemy_fire_resistance_red",
                "mod_cursed_enemy_lightning_resistance",
                "mod_cursed_enemy_lightning_resistance_red",
                "mod_cursed_enemy_poison_resistance",
                "mod_cursed_enemy_poison_resistance_red",
                "mod_cursed_enemy_elemental_resistance",
                "mod_cursed_enemy_elemental_resistance_red"
            },

            // [Curse] 5단계: 전투 능력 약화
            ["player_to_monster.curse:5_combat_debuffs"] = new HashSet<string>
            {
                "mod_cursed_enemy_action_speed_red",
                "mod_cursed_enemy_crit_chance",
                "mod_cursed_enemy_crit_chance_inc",
                "mod_cursed_enemy_crit_multiplier"
                // mod_cursed_enemy_take_inc_physical_damage는 Defense 5 (받는 피해 증감)에 할당됨
            },

            // ========================================
            // Ailment Damage 공식
            // ========================================

            // [Ailment] 0단계: Ailment 제한
            ["player_to_monster.ailment_damage:0_ailment_restrictions"] = new HashSet<string>
            {
                "mod_cannot_cause_ailment",
                "mod_cannot_cause_arctic",
                "mod_cannot_cause_bleeing",
                "mod_cannot_cause_ignite",
                "mod_cannot_cause_poisoning",
                "mod_cannot_cause_shock"
            },

            // [Ailment] 1단계: 기본 Ailment 피해
            // Skill Hit의 base_damage 결과를 입력으로 사용하므로 별도 MOD 할당 없음
            ["player_to_monster.ailment_damage:1_base_ailment_damage"] = new HashSet<string>
            {
                // 현재 게임에 ailment 기본 피해 MOD 없음
                // mod_bleeding_damage, mod_burning_damage 같은 MOD는 EMod에 없음
                // mod_poison_damage는 spell_damage에 할당됨
            },

            // [Ailment] 2단계: Ailment 확률
            ["player_to_monster.ailment_damage:2_ailment_proc_chance"] = new HashSet<string>
            {
                "mod_chance_to_arctic",
                "mod_chance_to_bleeding",
                "mod_chance_to_chill",
                "mod_chance_to_ignite",
                "mod_chance_to_paralyze",
                "mod_chance_to_poisoning",
                "mod_chance_to_shock",
                "mod_chance_to_stun",
                "mod_always_arctic_target",
                "mod_always_bleeding_target",
                "mod_always_chill_target",
                "mod_always_ignite_target",
                "mod_always_paralyze_target",
                "mod_always_poisoning_target",
                "mod_always_shock_target",
                "mod_always_stun_target"
            },

            // [Ailment] 3단계: Ailment 지속시간
            ["player_to_monster.ailment_damage:3_ailment_duration"] = new HashSet<string>
            {
                "mod_skill_duration_inc",
                "mod_bleeding_duration_inc_on_enemy",
                "mod_ignite_duration_inc_on_enemy",
                "mod_chill_duration_inc_on_enemy",
                "mod_shock_duration_inc_on_enemy",
                "mod_paralyze_duration_inc_on_enemy",
                "mod_poisoning_duration_inc_on_enemy",
                "mod_stun_duration_inc_on_enemy",
                "mod_arctic_duration_inc_on_enemy",
                "mod_all_ailment_duration_inc_on_enemy",
                "mod_all_ailment_reduce_duration_on_self",
                "mod_all_duration_inc",
                "mod_stun_chance_double_duration"
            },

            // [Ailment] 4단계: Ailment Inc·More
            ["player_to_monster.ailment_damage:4_ailment_inc_more"] = new HashSet<string>
            {
                // Inc MOD
                "mod_all_ailment_damage_inc",
                "mod_arctic_damage_inc",
                "mod_bleeding_damage_inc",
                "mod_chill_effect_inc_on_enemy",
                "mod_ignite_damage_inc",
                "mod_poisoning_damage_inc",
                "mod_shock_damage_inc",
                // More MOD
                "mod_all_ailment_damage_more",
                "mod_arctic_damage_more",
                "mod_bleeding_damage_more",
                "mod_ignite_damage_more",
                "mod_poisoning_damage_more",
                "mod_shock_damage_more"
            },

            // ========================================
            // Aura Damage 공식
            // ========================================

            // [Aura] 1단계: 기본 Aura 피해
            ["player_to_monster.aura_damage:1_base_aura_damage"] = new HashSet<string>
            {
                "mod_all_damage",
                "mod_all_skill_damage",
                "mod_cold_damage",
                "mod_elemental_damage",
                "mod_fire_damage",
                "mod_lightning_damage",
                "mod_physical_damage",
                "mod_poison_damage",
                "mod_use_all_skill_reinforce_add",
                "mod_use_cold_skill_reinforce_add",
                "mod_use_fire_skill_reinforce_add",
                "mod_use_lightning_skill_reinforce_add",
                "mod_use_physical_skill_reinforce_add",
                "mod_use_poison_skill_reinforce_add"
            },

            // [Aura] 2단계: Aura 지속시간·틱
            ["player_to_monster.aura_damage:2_aura_duration_tick"] = new HashSet<string>
            {
                "mod_skill_duration_inc",
                "mod_all_ailment_duration_inc_on_enemy",
                "mod_all_duration_inc",
                "mod_arctic_duration_inc_on_enemy",
                "mod_bleeding_duration_inc_on_enemy",
                "mod_chill_duration_inc_on_enemy",
                "mod_ignite_duration_inc_on_enemy",
                "mod_paralyze_duration_inc_on_enemy",
                "mod_poisoning_duration_inc_on_enemy",
                "mod_shock_duration_inc_on_enemy",
                "mod_stun_duration_inc_on_enemy"
            },

            // [Aura] 3단계: Aura Inc
            ["player_to_monster.aura_damage:3_aura_inc"] = new HashSet<string>
            {
                "mod_all_damage_inc",
                "mod_all_skill_damage_inc",
                "mod_cold_damage_inc",
                "mod_damage_inc_on_full_life",
                "mod_damage_inc_on_low_life",
                "mod_elemental_damage_inc",
                "mod_fire_damage_inc",
                "mod_lightning_damage_inc",
                "mod_physical_damage_inc",
                "mod_poison_damage_inc",
                "mod_projectile_damage_inc",
                "mod_use_aura_skill_reinforce_add"
            },

            // [Aura] 4단계: Aura More
            ["player_to_monster.aura_damage:4_aura_more"] = new HashSet<string>
            {
                "mod_all_damage_more",
                "mod_all_skill_damage_more",
                "mod_cold_damage_more",
                "mod_fire_damage_more",
                "mod_lightning_damage_more",
                "mod_physical_damage_more",
                "mod_poison_damage_more",
                "mod_projectile_damage_more"
            },

            // [Aura] 5단계: Aura 치명타
            ["player_to_monster.aura_damage:5_aura_critical"] = new HashSet<string>
            {
                // 기본 치명타 MOD (Spell과 동일)
                "mod_cannot_crit",
                "mod_crit_blow_chance",
                "mod_crit_blow_chance_inc",
                "mod_crit_blow_multiplier",
                "mod_crit_blow_multiplier_inc",
                "mod_crit_chance",
                "mod_crit_chance_inc",
                "mod_crit_multiplier",
                "mod_crit_multiplier_inc",
                "mod_cursed_enemy_crit_chance",
                "mod_cursed_enemy_crit_chance_inc",
                "mod_cursed_enemy_crit_multiplier",
                // Ailment 상태 적에게 치명타 확률 증가
                "mod_inc_crit_chance_arctic",
                "mod_inc_crit_chance_bleeding",
                "mod_inc_crit_chance_chill",
                "mod_inc_crit_chance_ignite",
                "mod_inc_crit_chance_paralyze",
                "mod_inc_crit_chance_poisoning",
                "mod_inc_crit_chance_shock",
                "mod_inc_crit_chance_stun"
            },

            // ========================================
            // Defense 공식 (Player → Monster)
            // ========================================

            // [Defense] 1단계: 명중 판정 (회피·막기)
            ["player_to_monster.defense:1_hit_determination"] = new HashSet<string>
            {
                "mod_block_chance",
                "mod_block_chance_inc",
                "mod_evade_chance",
                "mod_evade_chance_inc"
            },

            // [Defense] 2단계: 면역 체크
            ["player_to_monster.defense:2_immunity_check"] = new HashSet<string>
            {
                // 면역 MOD들 (속성별 면역)
                "mod_immune_arctic",
                "mod_immune_bleeding",
                "mod_immune_chill",
                "mod_immune_cold_damage",
                "mod_immune_curse",
                "mod_immune_fire_damage",
                "mod_immune_ignite",
                "mod_immune_lightning_damage",
                "mod_immune_paralyze",
                "mod_immune_physical_damage",
                "mod_immune_poison_damage",
                "mod_immune_poisoning",
                "mod_immune_shock",
                "mod_immune_stun"
            },

            // [Defense] 4단계: 저항 적용 (물리 + 원소)
            ["player_to_monster.defense:3_resistance_application"] = new HashSet<string>
            {
                // 물리 저항
                "mod_physical_resistance",
                "mod_physical_resistance_max",
                "mod_physical_resistance_penetration",
                "mod_reduction_enemy_physical_resistance",
                "mod_physical_damage_reduction",
                // 원소 저항
                "mod_cold_resistance",
                "mod_cold_resistance_max",
                "mod_fire_resistance",
                "mod_fire_resistance_max",
                "mod_lightning_resistance",
                "mod_lightning_resistance_max",
                "mod_poison_resistance",
                "mod_poison_resistance_max",
                "mod_elemental_resistance",
                "mod_elemental_resistance_max",
                // 플레이어 저항 관통 (공격 시 사용)
                "mod_cold_resistance_penetration",
                "mod_fire_resistance_penetration",
                "mod_lightning_resistance_penetration",
                "mod_poison_resistance_penetration",
                "mod_elemental_resistance_penetration",
                // 적 저항 감소 (공격 시 사용)
                "mod_reduction_enemy_cold_resistance",
                "mod_reduction_enemy_fire_resistance",
                "mod_reduction_enemy_lightning_resistance",
                "mod_reduction_enemy_poison_resistance",
                "mod_reduction_enemy_elemental_resistance",
                // Curse 적용 시 저항 변경
                "mod_cursed_enemy_cold_resistance",
                "mod_cursed_enemy_cold_resistance_red",
                "mod_cursed_enemy_fire_resistance",
                "mod_cursed_enemy_fire_resistance_red",
                "mod_cursed_enemy_lightning_resistance",
                "mod_cursed_enemy_lightning_resistance_red",
                "mod_cursed_enemy_poison_resistance",
                "mod_cursed_enemy_poison_resistance_red",
                "mod_cursed_enemy_elemental_resistance",
                "mod_cursed_enemy_elemental_resistance_red"
                // mod_lightning_damage_inc_per_lightning_resistance는 Spell Damage 1단계에 할당됨
            },

            // [Defense] 5단계: 받는 피해 증감
            ["player_to_monster.defense:4_damage_taken_modifiers"] = new HashSet<string>
            {
                "mod_enemy_take_inc_physical_damage",
                "mod_cursed_enemy_take_inc_physical_damage"
                // mod_damage_taken_inc, mod_damage_taken_dec, mod_damage_taken_more는 현재 EMod에 없음
            },

            // ========================================
            // Monster → Player Defense 공식
            // ========================================

            // [Defense] 1단계: 명중 판정 (플레이어 회피·막기)
            ["monster_to_player.defense:1_hit_determination"] = new HashSet<string>
            {
                "mod_block_chance",
                "mod_block_chance_inc",
                "mod_evade_chance",
                "mod_evade_chance_inc"
            },

            // [Defense] 2단계: 면역 체크 (플레이어)
            ["monster_to_player.defense:2_immunity_check"] = new HashSet<string>
            {
                // 현재 게임에 플레이어 immune_to_* MOD 없음
            },

            // [Defense] 4단계: 저항 적용 (플레이어 - 물리 + 원소)
            ["monster_to_player.defense:3_resistance_application"] = new HashSet<string>
            {
                // 물리 저항
                "mod_physical_resistance",
                "mod_physical_resistance_max",
                "mod_physical_damage_reduction",
                // 원소 저항 (몬스터 공격을 받을 때)
                "mod_cold_resistance",
                "mod_cold_resistance_max",
                "mod_fire_resistance",
                "mod_fire_resistance_max",
                "mod_lightning_resistance",
                "mod_lightning_resistance_max",
                "mod_poison_resistance",
                "mod_poison_resistance_max",
                "mod_elemental_resistance",
                "mod_elemental_resistance_max",
                // 특수: 번개 저항 기반 피해 증가 (공격 시 사용, 방어 시 무관하지만 메타데이터 표시용)
                "mod_lightning_damage_inc_per_lightning_resistance"
            },

            // [Defense] 5단계: 받는 피해 증감 (플레이어)
            ["monster_to_player.defense:4_damage_taken_modifiers"] = new HashSet<string>
            {
                // 현재 게임에 플레이어가 받는 피해 증감 MOD 없음
                // mod_damage_taken_inc, mod_damage_taken_dec, mod_damage_taken_more는 EMod에 없음
            },

            // [Defense] 6단계: 상태이상 저항
            ["monster_to_player.defense:6_ailment_resistance"] = new HashSet<string>
            {
                "mod_stun_recovery_inc",
                "mod_all_ailment_reduce_duration_on_self",
                "mod_curse_duration_on_self_reduction"
            },

            // ========================================
            // Movement 공식 (Player)
            // ========================================

            // [Movement] 1단계: 이동속도
            ["player_to_monster.movement:1_movement_speed"] = new HashSet<string>
            {
                "mod_movementspeed",
                "mod_movementspeed_inc",
                "mod_movementspeed_inc_on_full_life",
                "mod_movementspeed_inc_on_low_life"
            },

            // ========================================
            // Resource 공식 (Player)
            // ========================================

            // [Resource] 1단계: 생명력
            ["player_to_monster.resource:1_life"] = new HashSet<string>
            {
                "mod_life",
                "mod_life_inc",
                "mod_life_regen",
                "mod_life_regen_inc"
            },

            // [Resource] 2단계: 마나
            ["player_to_monster.resource:2_mana"] = new HashSet<string>
            {
                "mod_mana",
                "mod_mana_inc",
                "mod_mana_regen",
                "mod_mana_regen_rate_inc"
            },

            // [Resource] 3단계: 마나 소모
            ["player_to_monster.resource:3_mana_cost"] = new HashSet<string>
            {
                "mod_mana_cost_reduction",
                "mod_mana_nocost_skill_all",
                "mod_mana_nocost_skill_cold",
                "mod_mana_nocost_skill_fire",
                "mod_mana_nocost_skill_lightning",
                "mod_mana_nocost_skill_physical",
                "mod_mana_nocost_skill_poison"
            },

            // [Resource] 4단계: 생명력 흡수
            ["player_to_monster.resource:4_life_leech"] = new HashSet<string>
            {
                "mod_life_leech_all_damage",
                "mod_life_leech_physical_damage",
                "mod_life_leech_cold_damage",
                "mod_life_leech_fire_damage",
                "mod_life_leech_lightning_damage",
                "mod_life_leech_poison_damage",
                "mod_life_leech_max_instance_count",
                "mod_life_leech_max_per_instance"
            },

            // [Resource] 5단계: 마나 흡수
            ["player_to_monster.resource:5_mana_leech"] = new HashSet<string>
            {
                "mod_mana_leech_all_damage",
                "mod_mana_leech_physical_damage",
                "mod_mana_leech_cold_damage",
                "mod_mana_leech_fire_damage",
                "mod_mana_leech_lightning_damage",
                "mod_mana_leech_poison_damage",
                "mod_mana_leech_max_instance_count",
                "mod_mana_leech_max_per_instance"
            },

            // [Resource] 6단계: 흡수 제약
            ["player_to_monster.resource:6_leech_restrictions"] = new HashSet<string>
            {
                "mod_cannotbe_lifeleeched",
                "mod_cannotbe_manaleeched"
            },

            // [Resource] 7단계: 보상 (서버 전용)
            ["player_to_monster.resource:7_rewards"] = new HashSet<string>
            {
                "mod_xp_gain_inc",
                "mod_gold_gain_inc",
                "mod_probability_items_drop_monster_inc"
            },

            // ========================================
            // Buff 공식
            // ========================================

            // [Buff] 1단계: Killing Spree (연속 처치)
            ["player_to_monster.buff:1_killingspree"] = new HashSet<string>
            {
                "mod_buff_killingspree_cannot_gain",
                "mod_buff_killingspree_chance_to_gain",
                "mod_buff_killingspree_duration_inc",
                "mod_buff_killingspree_max",
                "mod_buff_killingspree_min"
            },

            // [Buff] 2단계: Backstab (배후 공격)
            ["player_to_monster.buff:2_backstab"] = new HashSet<string>
            {
                "mod_buff_backstab_cannot_gain",
                "mod_buff_backstab_chance_to_gain",
                "mod_buff_backstab_duration_inc",
                "mod_buff_backstab_max",
                "mod_buff_backstab_min"
            },

            // [Buff] 3단계: Ignore Pain (고통 무시)
            ["player_to_monster.buff:3_ignorepain"] = new HashSet<string>
            {
                "mod_buff_ignorepain_cannot_gain",
                "mod_buff_ignorepain_chance_to_gain",
                "mod_buff_ignorepain_duration_inc",
                "mod_buff_ignorepain_max",
                "mod_buff_ignorepain_min"
            },

            // [Buff] 4단계: Frenzy (광란)
            ["player_to_monster.buff:4_frenzy"] = new HashSet<string>
            {
                "mod_buff_frenzy_chance_to_gain",
                "mod_buff_frenzy_max",
                "mod_buff_frenzy_min"
            },

            // [Buff] 5단계: Invisibility (투명화)
            ["player_to_monster.buff:5_invisibility"] = new HashSet<string>
            {
                "mod_buff_invisibility_chance_to_gain",
                "mod_buff_invisibility_duration_inc"
            },

            // [Buff] 6단계: Surge of Power (힘의 파동)
            ["player_to_monster.buff:6_surgeofpower"] = new HashSet<string>
            {
                "mod_buff_surgeofpower_chance_to_gain",
                "mod_buff_surgeofpower_duration_inc",
                "mod_buff_surgeofpower_effect"
            }
        };

        /// <summary>
        /// MOD 네이밍 패턴 분석 (수동 할당 우선, 패턴 매칭 보조)
        /// </summary>
        private static List<StageCandidate> AnalyzeModPattern(string modName)
        {
            var candidates = new List<StageCandidate>();

            // ========================================
            // 우선순위 1: 수동 할당 MOD 체크
            // ========================================
            foreach (var kvp in ManualStageAssignments)
            {
                if (kvp.Value.Contains(modName))
                {
                    // 키 형식: "formula_type:stage_id"
                    var parts = kvp.Key.Split(':');
                    if (parts.Length == 2)
                    {
                        candidates.Add(new StageCandidate
                        {
                            formulaType = parts[0],
                            stageId = parts[1],
                            stageName = GetStageNameById(parts[1]),
                            confidence = 1.0f  // 수동 할당은 최고 신뢰도
                        });
                    }
                }
            }

            // 수동 할당이 있으면 패턴 매칭 스킵
            if (candidates.Count > 0)
                return candidates;

            // ========================================
            // 우선순위 2: 기존 패턴 매칭 (수동 할당 없을 경우만)
            // ========================================

            // 1단계: 기본 플랫 수집
            // ⚠️ 수동 할당이 정의된 stage는 패턴 매칭 제외
            if (!ManualStageAssignments.ContainsKey("player_to_monster.spell_damage:1_base_flat_collection") && IsStage1_BaseFlat(modName))
            {
                candidates.Add(new StageCandidate
                {
                    formulaType = "player_to_monster.spell_damage",
                    stageId = "1_base_flat_collection",
                    stageName = "1단계: 기본 플랫 수집",
                    confidence = 0.9f
                });
            }

            // 3단계: Increased 적용
            // ⚠️ 수동 할당이 정의된 stage는 패턴 매칭 제외
            if (!ManualStageAssignments.ContainsKey("player_to_monster.spell_damage:3_increased_application") && IsStage3_Increased(modName))
            {
                candidates.Add(new StageCandidate
                {
                    formulaType = "player_to_monster.spell_damage",
                    stageId = "3_increased_application",
                    stageName = "3단계: Increased 적용",
                    confidence = 0.95f
                });
            }

            // 4단계: More 적용
            // ⚠️ 수동 할당이 정의된 stage는 패턴 매칭 제외
            if (!ManualStageAssignments.ContainsKey("player_to_monster.spell_damage:4_more_application") && IsStage4_More(modName))
            {
                candidates.Add(new StageCandidate
                {
                    formulaType = "player_to_monster.spell_damage",
                    stageId = "4_more_application",
                    stageName = "4단계: More 적용",
                    confidence = 0.95f
                });
            }

            // 5단계: 치명타
            // ⚠️ 수동 할당이 정의된 stage는 패턴 매칭 제외
            if (!ManualStageAssignments.ContainsKey("player_to_monster.spell_damage:5_critical_strike") && IsStage5_Critical(modName))
            {
                candidates.Add(new StageCandidate
                {
                    formulaType = "player_to_monster.spell_damage",
                    stageId = "5_critical_strike",
                    stageName = "5단계: 치명타",
                    confidence = 0.9f
                });
            }

            // 6단계: 시전 속도
            // ⚠️ 수동 할당이 정의된 stage는 패턴 매칭 제외
            if (!ManualStageAssignments.ContainsKey("player_to_monster.spell_damage:6_cast_speed") && IsStage6_CastSpeed(modName))
            {
                candidates.Add(new StageCandidate
                {
                    formulaType = "player_to_monster.spell_damage",
                    stageId = "6_cast_speed",
                    stageName = "6단계: 시전 속도",
                    confidence = 0.85f
                });
            }

            // 신뢰도 순으로 정렬
            return candidates.OrderByDescending(c => c.confidence).ToList();
        }

        #region 단계별 패턴 매칭 규칙

        /// <summary>
        /// 1단계: 기본 플랫 수집 판별
        /// </summary>
        private static bool IsStage1_BaseFlat(string modName)
        {
            // ========================================
            // 명시적 제외 패턴 (우선순위 최상위)
            // ========================================

            // 1. mod_immune_* 패턴 제외 (방어 전용 MOD)
            if (modName.StartsWith("mod_immune_"))
            {
                return false;
            }

            // 2. 방어 관련 MOD 제외
            if (IsDefensiveMod(modName))
                return false;

            // 3. 유틸리티/특수 기능 MOD 제외
            if (IsUtilityMod(modName))
                return false;

            // ========================================
            // 공격 피해 관련 플랫 MOD 판별
            // ========================================

            // 패턴: mod_*_damage (단, _inc, _more 제외)
            if (Regex.IsMatch(modName, @"^mod_.*_damage$") &&
                !modName.Contains("_inc") &&
                !modName.Contains("_more"))
            {
                return true;
            }

            // 예외: elemental_damage는 플랫으로 간주
            if (modName == "mod_elemental_damage")
                return true;

            return false;
        }

        /// <summary>
        /// 방어 관련 MOD 판별
        /// </summary>
        private static bool IsDefensiveMod(string modName)
        {
            // 방어 키워드
            string[] defensiveKeywords = new string[]
            {
                "armor", "defence", "defense", "block", "dodge", "evade",
                "resist", "mitigation", "reduction", "absorb",
                "shield", "barrier", "ward"
            };

            foreach (var keyword in defensiveKeywords)
            {
                if (modName.Contains(keyword))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 유틸리티/특수 기능 MOD 판별
        /// </summary>
        private static bool IsUtilityMod(string modName)
        {
            // 유틸리티 키워드
            string[] utilityKeywords = new string[]
            {
                "cooldown", "duration", "range", "radius", "area",
                "movespeed", "castspeed", "attackspeed",
                "mana", "life", "energy", "resource",
                "regen", "recovery", "leech",
                "chance_to", "probability"
            };

            foreach (var keyword in utilityKeywords)
            {
                if (modName.Contains(keyword))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 3단계: Increased 적용 판별
        /// </summary>
        private static bool IsStage3_Increased(string modName)
        {
            // 명시적 제외 패턴
            if (modName.StartsWith("mod_immune_"))
                return false;

            if (IsDefensiveMod(modName))
                return false;

            // 패턴: mod_.*_inc$ 또는 mod_.*_damage_inc.*
            if (Regex.IsMatch(modName, @"_inc($|_)"))
            {
                return true;
            }

            // 특수 케이스: castspeed_inc, duration_inc 등은 유틸리티로 간주
            // (이미 IsUtilityMod에서 걸러지므로 여기서는 제외)

            return false;
        }

        /// <summary>
        /// 4단계: More 적용 판별
        /// </summary>
        private static bool IsStage4_More(string modName)
        {
            // 명시적 제외 패턴
            if (modName.StartsWith("mod_immune_"))
                return false;

            if (IsDefensiveMod(modName))
                return false;

            // 패턴: mod_.*_more$ 또는 mod_.*_damage_more.*
            if (Regex.IsMatch(modName, @"_more($|_)"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 5단계: 치명타 판별
        /// </summary>
        private static bool IsStage5_Critical(string modName)
        {
            // 패턴: mod_crit_.*, mod_.*_crit_.*, mod_cannot_crit
            if (modName.Contains("crit_") || modName.Contains("_crit"))
            {
                return true;
            }

            // 치명타 관련 키워드
            if (modName.Contains("critical") || modName.Contains("crit_blow"))
                return true;

            return false;
        }

        /// <summary>
        /// 6단계: 시전 속도 판별
        /// </summary>
        private static bool IsStage6_CastSpeed(string modName)
        {
            // 시전 속도 관련 MOD
            if (modName.Contains("castspeed"))
            {
                return true;
            }

            return false;
        }

        #endregion

        /// <summary>
        /// Stage ID로 Stage 이름 가져오기
        /// </summary>
        private static string GetStageNameById(string stageId)
        {
            var stageNames = new Dictionary<string, string>
            {
                // Spell Damage
                ["1_base_flat_collection"] = "[Spell] 1: 기본 플랫 수집",
                ["2_skill_multiplier"] = "[Spell] 2: 스킬 배율 적용",
                ["3_increased_application"] = "[Spell] 3: Increased 적용",
                ["4_more_application"] = "[Spell] 4: More 적용",
                ["5_critical_strike"] = "[Spell] 5: 치명타",
                ["6_cast_speed"] = "[Spell] 6: 시전 속도",
                ["7_multiple_damage"] = "[Spell] 7: 멀티플 피해",
                ["8_projectile_mechanics"] = "[Spell] 8: 투사체 메커닉",
                ["9_on_kill_effects"] = "[Spell] 9: 적 처치 시 발동 효과",

                // Ailment Damage
                ["0_ailment_restrictions"] = "[Ailment] 0: Ailment 제한",
                ["1_base_ailment_damage"] = "[Ailment] 1: 기본 Ailment 피해",
                ["2_ailment_proc_chance"] = "[Ailment] 2: Ailment 확률",
                ["3_ailment_duration"] = "[Ailment] 3: Ailment 지속시간",
                ["4_ailment_inc_more"] = "[Ailment] 4: Ailment Inc·More",

                // Aura Damage
                ["1_base_aura_damage"] = "[Aura] 1: 기본 Aura 피해",
                ["2_aura_duration_tick"] = "[Aura] 2: Aura 지속시간·틱",
                ["3_aura_inc"] = "[Aura] 3: Aura Inc",
                ["4_aura_more"] = "[Aura] 4: Aura More",
                ["5_aura_critical"] = "[Aura] 5: 치명타",

                // Defense (Player → Monster)
                ["1_hit_determination"] = "[Defense] 1: 명중 판정 (회피·막기)",
                ["2_immunity_check"] = "[Defense] 2: 면역 체크",
                ["3_resistance_application"] = "[Defense] 3: 저항 적용 (물리+원소)",
                ["4_damage_taken_modifiers"] = "[Defense] 4: 받는 피해 증감",

                // Movement
                ["1_movement_speed"] = "[Movement] 1: 이동속도",

                // Resource
                ["1_life"] = "[Resource] 1: 생명력",
                ["2_mana"] = "[Resource] 2: 마나",
                ["3_mana_cost"] = "[Resource] 3: 마나 소모",
                ["4_rewards"] = "[Resource] 4: 보상 (서버 전용)",

                // Buff
                ["1_killingspree"] = "[Buff] 1: Killing Spree (연속 처치)",
                ["2_backstab"] = "[Buff] 2: Backstab (배후 공격)",
                ["3_ignorepain"] = "[Buff] 3: Ignore Pain (고통 무시)",
                ["4_frenzy"] = "[Buff] 4: Frenzy (광란)",
                ["5_invisibility"] = "[Buff] 5: Invisibility (투명화)",
                ["6_surgeofpower"] = "[Buff] 6: Surge of Power (힘의 파동)"
            };

            return stageNames.ContainsKey(stageId) ? stageNames[stageId] : stageId;
        }

        /// <summary>
        /// 단계 후보
        /// </summary>
        private class StageCandidate
        {
            public string formulaType;  // 공식 타입 (skill_hit_damage, ailment_damage 등)
            public string stageId;
            public string stageName;
            public float confidence;  // 신뢰도 (0.0 ~ 1.0)
        }
    }
}
