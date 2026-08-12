using System;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 시뮬레이터 DPS 계산 및 결과 분석
    /// </summary>
    public static class SimulatorCalculator
    {

        #region Phase 6.2: 실제 전투 로직 통합

        /// <summary>
        /// 실제 전투 로직을 사용한 데미지 계산
        /// BattleModeBase.CalcResultModDamage() 호출
        /// </summary>
        public static FCalcResultModDamage CalculateRealDamage(
            ESkill spellSkill,
            ETier spellTier,
            SimulatorDefender defender)
        {
            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Simulator] ❌ 플레이어 캐릭터를 찾을 수 없습니다.");
                return new FCalcResultModDamage();
            }

            try
            {
                // 1. 공격자 Status
                FCharacterStatus attackerStatus = player.CharacterStatus;

                // 2. 방어자 Status
                FCharacterStatus defenderStatus = defender.ToCharacterStatus();

                // 3. FSkillData 생성
                FSkillData skillData = CreateSkillData(player, spellSkill, spellTier);
                if (skillData == null)
                {
                    Debug.LogError("[Simulator] ❌ FSkillData 생성 실패");
                    return new FCalcResultModDamage();
                }

                // 시뮬레이터 환경: AttackerCUID가 GameCharacterManager에 등록되어 있는지 확인
                string attackerUID = skillData.AttackerCUID;
                FCharacterBaseStatus registeredAttacker = GameCharacterManager.Instance.GetCharacterStatusByUID(attackerUID);
                if (registeredAttacker == null)
                {
                    Debug.LogError($"[Simulator] ⚠️ 공격자가 GameCharacterManager에 등록되지 않음. UID: {attackerUID}");
                    Debug.LogError($"[Simulator] ⚠️ 크리티컬 계산이 스킵될 수 있습니다.");
                }

                // 4. 실제 전투 계산 호출
                FCalcResultModDamage result = GameBattleUtilityManager.Instance.CalcResultModDamage(
                    defenderStatus,
                    skillData
                );

                return result;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Simulator] ❌ 실제 전투 계산 중 에러: {e.Message}\n{e.StackTrace}");
                return new FCalcResultModDamage();
            }
        }

        /// <summary>
        /// FSkillData 생성 (주문 스킬 기반)
        /// </summary>
        private static FSkillData CreateSkillData(
            UCharacterActor player,
            ESkill spellSkill,
            ETier spellTier)
        {
            // 시뮬레이터 전용 더미 ActionController 사용
            // FSkillData 생성 시 ActionController가 null이면 에러 로그가 발생하므로
            // 빈 더미 객체를 제공하여 에러를 방지합니다.
            BaseActionController actionController = new SimulatorDummyActionController();

            // 실제 강화도 계산
            int reinforceLevel = CalculateSkillReinforce(spellSkill, spellTier, player.CharacterStatus);

            // FSkillData.CreateSkillData() 정적 메서드 사용
            FSkillData skillData = FSkillData.CreateSkillData(
                spellSkill,
                spellTier,
                reinforceLevel, // 실제 강화도 사용
                player.CharacterStatus,
                actionController, // 더미 ActionController
                null // targetDeadEvent
            );

            if (skillData == null)
            {
                Debug.LogError($"[Simulator] ❌ FSkillData 생성 실패: {spellSkill}");
                return null;
            }

            return skillData;
        }

        /// <summary>
        /// 스킬 강화도 계산
        /// mod_use_xxx_skill_reinforce_add를 포함한 실제 강화도를 계산합니다
        /// </summary>
        public static int CalculateSkillReinforce(ESkill skill, ETier tier, FCharacterStatus characterStatus)
        {
            // 스킬 DB 가져오기
            if (!GameDBUtility.TryGetSkillDBData(skill, out ESkillType skillType, out GameDB_Client_Skill skillDB))
            {
                return 0;
            }

            // Core 강화도 가져오기 (userData에서)
            int coreReinforce = 0;
            var userData = GameAPIUserManager.Instance?.userData;
            if (userData != null && userData.skillData != null)
            {
                // 스킬 타입에 따라 적절한 데이터 가져오기
                if (skillType == ESkillType.skill_spell)
                {
                    var spells = userData.skillData.CoreData.Spells;
                    if (spells.TryGetValue(skill, out var spellData))
                    {
                        if (spellData.Tiers.TryGetValue(tier, out var tierData))
                        {
                            coreReinforce = tierData.Reinforce.Value;
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
                            coreReinforce = tierData.Reinforce.Value;
                        }
                    }
                }
            }

            // GameBattleUtilityManager를 통해 실제 강화도 계산
            // (mod_use_all_skill_reinforce_add + mod_use_xxx_skill_reinforce_add + coreReinforce)
            if (GameBattleUtilityManager.Instance != null)
            {
                return GameBattleUtilityManager.Instance.ResultSkillReinforce(characterStatus, skillDB, coreReinforce);
            }

            return coreReinforce;
        }

        #endregion

        #region Phase 6.3: 통계 기반 DPS 계산

        /// <summary>
        /// 통계적 DPS 계산
        /// </summary>
        public static SimulatorDamageStats CalculateStatisticalDPS(
            ESkill spellSkill,
            ETier spellTier,
            SimulatorDefender defender,
            int auraReinforce = 0,
            ESkill aura = ESkill.None)
        {
            SimulatorDamageStats stats = new SimulatorDamageStats();
            stats.totalIterations = 1;

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Simulator] ❌ 플레이어 캐릭터를 찾을 수 없습니다.");
                return stats;
            }

            FCharacterStatus attackerStatus = player.CharacterStatus;
            FCharacterStatus defenderStatus = defender.ToCharacterStatus();

            // Cast Speed 계산
            double castSpeed = attackerStatus.BattleStatus.ResultSkillCastSpeed();
            stats.castSpeed = castSpeed;

            FSkillData skillData = CreateSkillData(player, spellSkill, spellTier);

            // ============================================================
            // 크리티컬 없이 기본 데미지 계산 (일관성 보장)
            // ============================================================
            // 1. 현재 crit_chance 백업
            double originalCritChance = attackerStatus.BattleStatus.TotalModValue(EMod.mod_crit_chance);

            // 2. Reflection으로 crit_chance를 0으로 설정 (크리티컬 비활성화)
            FieldInfo field = typeof(FBattleStatus).GetField("_allBattleModData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                object modData = field.GetValue(attackerStatus.BattleStatus);
                if (modData != null)
                {
                    MethodInfo setMethod = modData.GetType().GetMethod("SetModValue", new[] { typeof(EMod), typeof(CryptoValueDouble) });
                    if (setMethod != null)
                    {
                        setMethod.Invoke(modData, new object[] { EMod.mod_crit_chance, CryptoValueDouble.Create(0) });
                    }
                }
            }

            // 3. 크리티컬 없이 기본 데미지 계산
            FCalcResultModDamage baseResult = GameBattleUtilityManager.Instance.CalcResultModDamage(
                defenderStatus,
                skillData
            );
            double baseDamageNoCrit = baseResult.ResultDamage;  // 크리티컬 없는 순수 기본 데미지

            // 4. crit_chance 복원
            if (field != null)
            {
                object modData = field.GetValue(attackerStatus.BattleStatus);
                if (modData != null)
                {
                    MethodInfo setMethod = modData.GetType().GetMethod("SetModValue", new[] { typeof(EMod), typeof(CryptoValueDouble) });
                    if (setMethod != null)
                    {
                        setMethod.Invoke(modData, new object[] { EMod.mod_crit_chance, CryptoValueDouble.Create(originalCritChance) });
                    }
                }
            }

            // 5. 통계 수집용 일반 시뮬레이션 (크리티컬 포함)
            FCalcResultModDamage result = GameBattleUtilityManager.Instance.CalcResultModDamage(
                defenderStatus,
                skillData
            );

            // 조건부 MOD: 적 상태이상에 따른 피해 증가
            // BattleModeBase의 ApplyConditionalDamageModifiers와 동일한 로직을 시뮬레이터에서 수동 적용
            double conditionalIncPercent = 0;
            if (defender.targetHasBleeding)
            {
                conditionalIncPercent += attackerStatus.BattleStatus.TotalModValue(EMod.mod_inc_damage_bleeding);
            }
            if (defender.targetHasIgnite)
            {
                conditionalIncPercent += attackerStatus.BattleStatus.TotalModValue(EMod.mod_inc_damage_ignite);
            }
            if (defender.targetHasChill)
            {
                conditionalIncPercent += attackerStatus.BattleStatus.TotalModValue(EMod.mod_inc_damage_chill);
            }
            if (defender.targetHasArctic)
            {
                conditionalIncPercent += attackerStatus.BattleStatus.TotalModValue(EMod.mod_inc_damage_arctic);
            }
            if (defender.targetHasParalyze)
            {
                conditionalIncPercent += attackerStatus.BattleStatus.TotalModValue(EMod.mod_inc_damage_paralyze);
            }
            if (defender.targetHasPoisoning)
            {
                conditionalIncPercent += attackerStatus.BattleStatus.TotalModValue(EMod.mod_inc_damage_poisoning);
            }
            if (defender.targetHasShock)
            {
                conditionalIncPercent += attackerStatus.BattleStatus.TotalModValue(EMod.mod_inc_damage_shock);
            }
            if (defender.targetHasStun)
            {
                conditionalIncPercent += attackerStatus.BattleStatus.TotalModValue(EMod.mod_inc_damage_stun);
            }

            // 조건부 Inc가 있으면 데미지에 적용: damage × (1 + conditionalInc)
            // TotalModValue는 이미 비율을 반환 (FLOAT_PER: 20 → 0.2)
            if (conditionalIncPercent != 0 && !result.IsEvade && !result.IsImmune)
            {
                double adjustedDamage = result.ResultDamage * (1 + conditionalIncPercent);
                result.SetResultDamage(adjustedDamage, result.AttackerCUID);
            }

            // 회피/블록/면역 추적
            if (result.IsEvade)
            {
                stats.evadeCount = 1;
                stats.evadeRate = 1.0;
            }
            else if (result.IsImmune)
            {
                stats.immuneCount = 1;
                stats.immuneRate = 1.0;
            }
            else
            {
                if (result.IsBlock)
                {
                    stats.blockCount = 1;
                    stats.blockRate = 1.0;
                }

                // 유효 히트
                stats.hitCount = 1;
                stats.hitRate = 1.0;

                // 크리티컬 추적
                if (result.IsCritical)
                {
                    stats.criticalCount = 1;
                    stats.criticalRate = 1.0;
                }

                // 치명타 일격 추적
                if (result.IsCriticalBlow)
                {
                    stats.fatalBlowCount = 1;
                }

                // 데미지 통계 - 확률 기반 평균 데미지 계산
                // ✅ 크리티컬 없이 계산한 일관된 기본 데미지 사용
                double baseDamage = baseDamageNoCrit;

                // 치명타 확률 및 배율 가져오기
                double critChance = attackerStatus.BattleStatus.ResultCriticalChance(defenderStatus);
                double critBlowChance = attackerStatus.BattleStatus.ResultCriticalBlowChance();

                // 치명타 배율: FLOAT_PER 타입이므로 이미 비율(ratio)로 반환됨
                // 예: 시트 150 → 저장/반환 1.5 → 계산에서 바로 사용
                double critMultiplierRatio = attackerStatus.BattleStatus.ResultCriticalMultiplier(defenderStatus);
                double critBlowMultiplierRatio = attackerStatus.BattleStatus.ResultCriticalBlowmultiplier();

                // 방어 코드: crit_multiplier가 1.0 미만이면 문제 발생 (100% 미만 = 피해 감소)
                if (critMultiplierRatio <= 0)
                {
                    Debug.LogError($"[DPS-Crit-Error] crit_multiplier가 0 또는 음수입니다! " +
                        $"critMultiplierRatio={critMultiplierRatio}, DefaultMods를 확인하세요.");
                    critMultiplierRatio = 1.5;  // 기본값 1.5 (150%)
                }

                // 방어 코드: critBlowMultiplier가 1.0 미만이면 문제 발생
                if (critBlowMultiplierRatio <= 0)
                {
                    Debug.LogError($"[DPS-Crit-Warning] critBlowMultiplier가 0입니다. " +
                        $"critBlowMultiplierRatio={critBlowMultiplierRatio}, 최소값으로 설정합니다.");
                    critBlowMultiplierRatio = 1.0;  // 최소값 1.0 (100%, 효과 없음)
                }

                // ResultCriticalChance(), ResultCriticalBlowChance()는 이미 비율(0.3 = 30%)을 반환
                // 추가 변환 불필요
                double critChanceRatio = critChance;
                double critBlowChanceRatio = critBlowChance;

                // 각 케이스의 발생 확률 계산 (치명타 확률 >= 100% 조건 체크)
                double normalChance;
                double critOnlyChance;
                double critBlowActualChance;

                if (critChance >= 1.0)  // 1.0 = 100%
                {
                    // 100% 치명타 확정 + 추가 일격 판정
                    normalChance = 0;
                    critOnlyChance = 1.0 - critBlowChanceRatio;
                    critBlowActualChance = critBlowChanceRatio;
                }
                else
                {
                    // 일반 치명타만 (치명타 일격 불가)
                    normalChance = 1.0 - critChanceRatio;
                    critOnlyChance = critChanceRatio;
                    critBlowActualChance = 0;
                }

                // 평균 배율 계산 (확률 기반)
                // 평균 배율 = (일반 확률 × 1.0) + (치명타 확률 × 치명타 배율) + (치명타 일격 확률 × 치명타 배율 × 치명타 일격 배율)
                double averageMultiplier = (normalChance * 1.0)
                                         + (critOnlyChance * critMultiplierRatio)
                                         + (critBlowActualChance * critMultiplierRatio * critBlowMultiplierRatio);

                // 평균 데미지 = 기본 데미지 × 평균 배율
                double averageDamage = baseDamage * averageMultiplier;

                stats.totalDamage = averageDamage;
                stats.minDamage = baseDamage;  // 최소 = 기본 (크리티컬 없음)
                stats.maxDamage = baseDamage * critMultiplierRatio * critBlowMultiplierRatio;  // 최대 = 치명타 일격
                stats.averageDamage = averageDamage;

                // Phase 10: Ailment 발동 체크
                CheckAndApplyAilments(skillData, attackerStatus, result, ref stats);
            }

            // 실제 Hit DPS = 평균 데미지 × 시전 속도 × 히트율
            double hitDPS = stats.averageDamage * castSpeed * stats.hitRate;

            // Phase 10: Ailment 상세 정보 계산 (스킬 태그 필터링 포함)
            // 인게임과 동일하게 치명타 적용
            FSkillData skillDataForAilment = CreateSkillData(player, spellSkill, spellTier);
            CalculateAilmentDetails(attackerStatus, defenderStatus, skillDataForAilment, ref stats);

            // Phase 11: Aura 버프 상세 정보 계산 (skill_contagion 등)
            // skillaura_inevitable가 장착된 경우에만 DoT 계산
            CalculateDotBuffDetails(attackerStatus, defenderStatus, skillDataForAilment, auraReinforce, aura, ref stats);

            // 총 DPS = Hit DPS + Ailment DPS 합산
            double totalAilmentDPS = 0;
            foreach (var ailmentDetail in stats.ailmentDetails)
            {
                // 최대 스택 DPS 사용 (실제 게임에서 달성 가능한 최대 데미지)
                totalAilmentDPS += ailmentDetail.maxStackDps;
            }

            // Aura DoT 버프 DPS 추가
            foreach (var dotBuff in stats.dotBuffDetails)
            {
                totalAilmentDPS += dotBuff.dps;
            }

            // 최종 realDPS = Hit DPS + Ailment DPS
            stats.realDPS = hitDPS + totalAilmentDPS;

            return stats;
        }

        /// <summary>
        /// Phase 10: Ailment 발동 체크 및 통계 수집
        /// </summary>
        private static void CheckAndApplyAilments(
            FSkillData skillData,
            FCharacterStatus attackerStatus,
            FCalcResultModDamage result,
            ref SimulatorDamageStats stats)
        {
            ESkillTag skillTagDamageType = skillData.SkillTagDamageType;
            var skillTags = skillData.GetSkillDB.SkillTags;

            // Ailment 데미지는 Flat 합산을 기반으로 계산
            // Flat = mod_[element]_damage + mod_elemental_damage + mod_all_skill_damage + mod_all_damage
            double flatDamage = CalculateFlatDamage(attackerStatus, skillTagDamageType);

            // 위키_ailment.md: "해당 피해 유형(속성)의 스킬을 사용할 때 발생한다"
            // 스킬의 태그에 따라 해당 Ailment만 체크
            if (HasSkillTag(skillTags, ESkillTag.skilltag_physical))
            {
                CheckAilmentProc(attackerStatus, EMod.mod_chance_to_bleeding, EStatusEffect.ailment_bleeding, flatDamage, ref stats);
            }

            if (HasSkillTag(skillTags, ESkillTag.skilltag_fire))
            {
                CheckAilmentProc(attackerStatus, EMod.mod_chance_to_ignite, EStatusEffect.ailment_ignite, flatDamage, ref stats);
            }

            if (HasSkillTag(skillTags, ESkillTag.skilltag_cold))
            {
                CheckAilmentProc(attackerStatus, EMod.mod_chance_to_arctic, EStatusEffect.ailment_arctic, flatDamage, ref stats);
                CheckAilmentProc(attackerStatus, EMod.mod_chance_to_chill, EStatusEffect.ailment_chill, flatDamage, ref stats);
            }

            if (HasSkillTag(skillTags, ESkillTag.skilltag_lightning))
            {
                CheckAilmentProc(attackerStatus, EMod.mod_chance_to_shock, EStatusEffect.ailment_shock, flatDamage, ref stats);
                CheckAilmentProc(attackerStatus, EMod.mod_chance_to_paralyze, EStatusEffect.ailment_paralyze, flatDamage, ref stats);
            }

            if (HasSkillTag(skillTags, ESkillTag.skilltag_poison))
            {
                CheckAilmentProc(attackerStatus, EMod.mod_chance_to_poisoning, EStatusEffect.ailment_poisoning, flatDamage, ref stats);
            }

            // stun은 모든 타입 공통 (스킬 태그 무관)
            CheckAilmentProc(attackerStatus, EMod.mod_chance_to_stun, EStatusEffect.ailment_stun, flatDamage, ref stats);
        }

        /// <summary>
        /// 스킬이 특정 태그를 가지고 있는지 확인
        /// </summary>
        private static bool HasSkillTag(System.Collections.Generic.Dictionary<ESkillTag, bool> skillTags, ESkillTag tag)
        {
            if (skillTags == null) return false;
            return skillTags.ContainsKey(tag) && skillTags[tag];
        }

        /// <summary>
        /// Ailment 계산용 Flat 데미지 합산
        /// Flat = mod_[element]_damage + mod_elemental_damage + mod_all_skill_damage + mod_all_damage
        /// </summary>
        private static double CalculateFlatDamage(FCharacterStatus attackerStatus, ESkillTag skillTagDamageType)
        {
            var battleStatus = attackerStatus.BattleStatus;
            double flatDamage = 0;

            // 속성별 Flat 데미지
            switch (skillTagDamageType)
            {
                case ESkillTag.skilltag_physical:
                    flatDamage = battleStatus.TotalModValue(EMod.mod_physical_damage);
                    break;
                case ESkillTag.skilltag_fire:
                    flatDamage = battleStatus.TotalModValue(EMod.mod_fire_damage)
                               + battleStatus.TotalModValue(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_cold:
                    flatDamage = battleStatus.TotalModValue(EMod.mod_cold_damage)
                               + battleStatus.TotalModValue(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_lightning:
                    flatDamage = battleStatus.TotalModValue(EMod.mod_lightning_damage)
                               + battleStatus.TotalModValue(EMod.mod_elemental_damage);
                    break;
                case ESkillTag.skilltag_poison:
                    flatDamage = battleStatus.TotalModValue(EMod.mod_poison_damage)
                               + battleStatus.TotalModValue(EMod.mod_elemental_damage);
                    break;
            }

            // 모든 타입 공통: AllSkill + AllDamage
            flatDamage += battleStatus.TotalModValue(EMod.mod_all_skill_damage);
            flatDamage += battleStatus.TotalModValue(EMod.mod_all_damage);

            return flatDamage;
        }

        /// <summary>
        /// Phase 10: 개별 Ailment 발동 확률 및 DoT 데미지 계산 (기댓값 기반)
        /// DPS 시뮬레이터는 랜덤 대신 기댓값(Expected Value)을 사용하여 일관된 결과를 보장합니다.
        /// 예: 30% 확률, 1000 피해 → 기댓값 = 1000 × 0.30 = 300
        /// </summary>
        private static void CheckAilmentProc(
            FCharacterStatus attackerStatus,
            EMod chanceMod,
            EStatusEffect ailmentType,
            double flatDamage,
            ref SimulatorDamageStats stats)
        {
            // Ailment 발동 확률 가져오기
            // TotalModValue는 이미 비율(0.3 = 30%)을 반환
            float ailmentChance = (float)attackerStatus.BattleStatus.TotalModValue(chanceMod);
            if (ailmentChance <= 0) return;

            // 이미 비율이므로 추가 변환 불필요 (100% = 1.0 초과 시 1.0으로 제한)
            double procRatio = System.Math.Min(ailmentChance, 1.0);

            // Ailment 발동 횟수는 기댓값으로 계산 (예: 30% = 0.3회)
            switch (ailmentType)
            {
                case EStatusEffect.ailment_bleeding:
                    stats.ailmentBleedingCount += procRatio;
                    break;
                case EStatusEffect.ailment_ignite:
                    stats.ailmentIgniteCount += procRatio;
                    break;
                case EStatusEffect.ailment_arctic:
                    stats.ailmentArcticCount += procRatio;
                    break;
                case EStatusEffect.ailment_chill:
                    stats.ailmentChillCount += procRatio;
                    break;
                case EStatusEffect.ailment_shock:
                    stats.ailmentShockCount += procRatio;
                    break;
                case EStatusEffect.ailment_paralyze:
                    stats.ailmentParalyzeCount += procRatio;
                    break;
                case EStatusEffect.ailment_poisoning:
                    stats.ailmentPoisoningCount += procRatio;
                    break;
                case EStatusEffect.ailment_stun:
                    stats.ailmentStunCount += procRatio;
                    break;
            }

            // CC 효과(Chill, Paralyze, Stun)는 피해를 주지 않음 (BuffActionData_Ailment.cs:171-176 참조)
            // 실제 게임 로직에서도 shouldApplyDamage = false로 처리
            bool isCCEffect = ailmentType == EStatusEffect.ailment_chill ||
                             ailmentType == EStatusEffect.ailment_paralyze ||
                             ailmentType == EStatusEffect.ailment_stun;
            if (isCCEffect) return; // CC 효과는 DoT 피해 없음

            // DoT 데미지 계산 (GameDB에서 데이터 가져오기)
            if (!GameDBClientManager.Instance.GameDB_Skill.Buff.MapData.TryGetValue(ailmentType, out GameDB_Client_Buff buffDBData))
            {
                return; // 데이터 없으면 DoT 계산 안함
            }

            // DoT 데미지 계산: Flat 데미지 × 데미지 배율 (Effect) × Inc × More
            // 위키_ailment.md 267-271:
            // 점화 피해 = (속성) 플랫 피해
            //   * (1 + (모든 피해 증가 + 속성 피해 증가 + 점화 피해 증가))
            //   * (1 * (모든 피해 증폭 + 속성 피해 증폭 + 점화 피해 증폭))
            double dotDamageRate = buffDBData.Effect.Value;
            if (dotDamageRate <= 0) return; // Effect 값이 0 이하면 피해 없음

            // 기본 DoT 데미지 (Effect 적용)
            double dotDamage = flatDamage * dotDamageRate;

            // Inc/More 배율 계산
            double incMultiplier = CalculateAilmentIncMultiplier(attackerStatus, ailmentType);
            double moreMultiplier = CalculateAilmentMoreMultiplier(attackerStatus, ailmentType);

            // 최종 DoT 데미지 = 기본 × (1 + 증가합) × (1 * 증폭곱)
            dotDamage = dotDamage * incMultiplier * moreMultiplier;

            // 기댓값 적용: 최종 피해 × 발동 확률
            double expectedDotDamage = dotDamage * procRatio;

            // DoT 통계 누적 (기댓값)
            // CC 효과(Chill, Paralyze, Stun)는 위에서 이미 return되었으므로 여기에 도달하지 않음
            switch (ailmentType)
            {
                case EStatusEffect.ailment_bleeding:
                    stats.ailmentBleedingDamage += expectedDotDamage;
                    break;
                case EStatusEffect.ailment_ignite:
                    stats.ailmentIgniteDamage += expectedDotDamage;
                    break;
                case EStatusEffect.ailment_arctic:
                    stats.ailmentArcticDamage += expectedDotDamage;
                    break;
                case EStatusEffect.ailment_shock:
                    stats.ailmentShockDamage += expectedDotDamage;
                    break;
                case EStatusEffect.ailment_poisoning:
                    stats.ailmentPoisoningDamage += expectedDotDamage;
                    break;
                    // CC 효과(Chill, Paralyze, Stun)는 피해가 없으므로 case 제거
            }
        }

        /// <summary>
        /// Ailment 증가(Inc) 배율 계산
        /// 공식: (1 + (모든 피해 증가 + 모든 스킬 피해 증가 + 속성 피해 증가 + Ailment별 피해 증가) × 0.01)
        /// ModCalculator와 동일한 방식: 퍼센트 값을 합산 후 × 0.01로 변환
        /// </summary>
        private static double CalculateAilmentIncMultiplier(FCharacterStatus attackerStatus, EStatusEffect ailmentType, Dictionary<EMod, double> modContributions = null)
        {
            var battleStatus = attackerStatus.BattleStatus;
            double totalInc = 0;

            // 모든 피해 증가 (TotalModValue는 35면 35% 의미, 그대로 합산)
            double allDamageInc = battleStatus.TotalModValue(EMod.mod_all_damage_inc);
            if (allDamageInc != 0)
            {
                totalInc += allDamageInc;
                if (modContributions != null) modContributions[EMod.mod_all_damage_inc] = allDamageInc;
            }

            // 모든 스킬 피해 증가
            double allSkillInc = battleStatus.TotalModValue(EMod.mod_all_skill_damage_inc);
            if (allSkillInc != 0)
            {
                totalInc += allSkillInc;
                if (modContributions != null) modContributions[EMod.mod_all_skill_damage_inc] = allSkillInc;
            }

            // [Ailment] 4단계: 모든 Ailment 피해 증가
            double allAilmentInc = battleStatus.TotalModValue(EMod.mod_all_ailment_damage_inc);
            if (allAilmentInc != 0)
            {
                totalInc += allAilmentInc;
                if (modContributions != null) modContributions[EMod.mod_all_ailment_damage_inc] = allAilmentInc;
            }

            // 속성별 피해 증가
            switch (ailmentType)
            {
                case EStatusEffect.ailment_bleeding:
                    {
                        double physicalInc = battleStatus.TotalModValue(EMod.mod_physical_damage_inc);
                        if (physicalInc != 0)
                        {
                            totalInc += physicalInc;
                            if (modContributions != null) modContributions[EMod.mod_physical_damage_inc] = physicalInc;
                        }

                        double bleedingInc = battleStatus.TotalModValue(EMod.mod_bleeding_damage_inc);
                        if (bleedingInc != 0)
                        {
                            totalInc += bleedingInc;
                            if (modContributions != null) modContributions[EMod.mod_bleeding_damage_inc] = bleedingInc;
                        }
                    }
                    break;
                case EStatusEffect.ailment_ignite:
                    {
                        double fireInc = battleStatus.TotalModValue(EMod.mod_fire_damage_inc);
                        if (fireInc != 0)
                        {
                            totalInc += fireInc;
                            if (modContributions != null) modContributions[EMod.mod_fire_damage_inc] = fireInc;
                        }

                        double elementalInc = battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                        if (elementalInc != 0)
                        {
                            totalInc += elementalInc;
                            if (modContributions != null) modContributions[EMod.mod_elemental_damage_inc] = elementalInc;
                        }

                        double igniteInc = battleStatus.TotalModValue(EMod.mod_ignite_damage_inc);
                        if (igniteInc != 0)
                        {
                            totalInc += igniteInc;
                            if (modContributions != null) modContributions[EMod.mod_ignite_damage_inc] = igniteInc;
                        }
                    }
                    break;
                case EStatusEffect.ailment_arctic:
                    {
                        double coldInc = battleStatus.TotalModValue(EMod.mod_cold_damage_inc);
                        if (coldInc != 0)
                        {
                            totalInc += coldInc;
                            if (modContributions != null) modContributions[EMod.mod_cold_damage_inc] = coldInc;
                        }

                        double elementalInc = battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                        if (elementalInc != 0)
                        {
                            totalInc += elementalInc;
                            if (modContributions != null) modContributions[EMod.mod_elemental_damage_inc] = elementalInc;
                        }

                        double arcticInc = battleStatus.TotalModValue(EMod.mod_arctic_damage_inc);
                        if (arcticInc != 0)
                        {
                            totalInc += arcticInc;
                            if (modContributions != null) modContributions[EMod.mod_arctic_damage_inc] = arcticInc;
                        }
                    }
                    break;
                case EStatusEffect.ailment_chill:
                    {
                        double coldInc = battleStatus.TotalModValue(EMod.mod_cold_damage_inc);
                        if (coldInc != 0)
                        {
                            totalInc += coldInc;
                            if (modContributions != null) modContributions[EMod.mod_cold_damage_inc] = coldInc;
                        }

                        double elementalInc = battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                        if (elementalInc != 0)
                        {
                            totalInc += elementalInc;
                            if (modContributions != null) modContributions[EMod.mod_elemental_damage_inc] = elementalInc;
                        }
                    }
                    break;
                case EStatusEffect.ailment_shock:
                    {
                        double lightningInc = battleStatus.TotalModValue(EMod.mod_lightning_damage_inc);
                        if (lightningInc != 0)
                        {
                            totalInc += lightningInc;
                            if (modContributions != null) modContributions[EMod.mod_lightning_damage_inc] = lightningInc;
                        }

                        double elementalInc = battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                        if (elementalInc != 0)
                        {
                            totalInc += elementalInc;
                            if (modContributions != null) modContributions[EMod.mod_elemental_damage_inc] = elementalInc;
                        }

                        double shockInc = battleStatus.TotalModValue(EMod.mod_shock_damage_inc);
                        if (shockInc != 0)
                        {
                            totalInc += shockInc;
                            if (modContributions != null) modContributions[EMod.mod_shock_damage_inc] = shockInc;
                        }
                    }
                    break;
                case EStatusEffect.ailment_paralyze:
                    {
                        double lightningInc = battleStatus.TotalModValue(EMod.mod_lightning_damage_inc);
                        if (lightningInc != 0)
                        {
                            totalInc += lightningInc;
                            if (modContributions != null) modContributions[EMod.mod_lightning_damage_inc] = lightningInc;
                        }

                        double elementalInc = battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                        if (elementalInc != 0)
                        {
                            totalInc += elementalInc;
                            if (modContributions != null) modContributions[EMod.mod_elemental_damage_inc] = elementalInc;
                        }
                    }
                    break;
                case EStatusEffect.ailment_poisoning:
                    {
                        double poisonInc = battleStatus.TotalModValue(EMod.mod_poison_damage_inc);
                        if (poisonInc != 0)
                        {
                            totalInc += poisonInc;
                            if (modContributions != null) modContributions[EMod.mod_poison_damage_inc] = poisonInc;
                        }

                        double elementalInc = battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                        if (elementalInc != 0)
                        {
                            totalInc += elementalInc;
                            if (modContributions != null) modContributions[EMod.mod_elemental_damage_inc] = elementalInc;
                        }

                        double poisoningInc = battleStatus.TotalModValue(EMod.mod_poisoning_damage_inc);
                        if (poisoningInc != 0)
                        {
                            totalInc += poisoningInc;
                            if (modContributions != null) modContributions[EMod.mod_poisoning_damage_inc] = poisoningInc;
                        }
                    }
                    break;
                case EStatusEffect.ailment_stun:
                    // stun은 모든 속성 가능, inc 없음
                    break;
            }


            // 인게임 BuffActionData_Ailment.cs와 동일: (1 + totalInc)
            // TotalModValue()는 이미 비율 값 반환 (예: 0.4230 = 42.30%)
            double result = 1.0 + totalInc;

            return result;
        }

        /// <summary>
        /// Ailment 지속시간 배율 계산
        /// [Ailment] 3단계: Duration MOD 적용
        /// 공식: (1 + totalInc) - 인게임과 동일
        /// TotalModValue()는 이미 비율 값 반환
        /// </summary>
        private static double CalculateAilmentDurationMultiplier(FCharacterStatus attackerStatus, EStatusEffect ailmentType)
        {
            var battleStatus = attackerStatus.BattleStatus;
            double totalInc = 0;

            // Ailment별 지속시간 증가 (적에게)
            switch (ailmentType)
            {
                case EStatusEffect.ailment_bleeding:
                    totalInc += battleStatus.TotalModValue(EMod.mod_bleeding_duration_inc_on_enemy);
                    break;
                case EStatusEffect.ailment_ignite:
                    totalInc += battleStatus.TotalModValue(EMod.mod_ignite_duration_inc_on_enemy);
                    break;
                case EStatusEffect.ailment_arctic:
                    totalInc += battleStatus.TotalModValue(EMod.mod_arctic_duration_inc_on_enemy);
                    break;
                case EStatusEffect.ailment_chill:
                    totalInc += battleStatus.TotalModValue(EMod.mod_chill_duration_inc_on_enemy);
                    break;
                case EStatusEffect.ailment_shock:
                    totalInc += battleStatus.TotalModValue(EMod.mod_shock_duration_inc_on_enemy);
                    break;
                case EStatusEffect.ailment_paralyze:
                    totalInc += battleStatus.TotalModValue(EMod.mod_paralyze_duration_inc_on_enemy);
                    break;
                case EStatusEffect.ailment_poisoning:
                    totalInc += battleStatus.TotalModValue(EMod.mod_poisoning_duration_inc_on_enemy);
                    break;
                case EStatusEffect.ailment_stun:
                    totalInc += battleStatus.TotalModValue(EMod.mod_stun_duration_inc_on_enemy);
                    break;
            }

            // 모든 Ailment 지속시간 증가 (적에게)
            totalInc += battleStatus.TotalModValue(EMod.mod_all_ailment_duration_inc_on_enemy);

            // 모든 지속시간 증가
            totalInc += battleStatus.TotalModValue(EMod.mod_all_duration_inc);

            // 본인 Ailment 지속시간 감소 (음수 적용)
            totalInc -= battleStatus.TotalModValue(EMod.mod_all_ailment_reduce_duration_on_self);

            // 최종 배율 = (1 + totalInc)
            double result = 1.0 + totalInc;

            return result;
        }

        /// <summary>
        /// Ailment 증폭(More) 배율 계산
        /// 공식: ∏(1 + More_i) - 인게임과 동일
        /// More는 각각 개별적으로 곱해짐 (multiplicative)
        /// </summary>
        private static double CalculateAilmentMoreMultiplier(FCharacterStatus attackerStatus, EStatusEffect ailmentType, Dictionary<EMod, double> modContributions = null)
        {
            var battleStatus = attackerStatus.BattleStatus;
            double result = 1.0;

            // 인게임 BuffActionData_Ailment.cs와 동일: (1 + More)
            // TotalModValue()는 이미 비율 값 반환 (예: 0.15 = 15%)
            double allDamageMore = battleStatus.TotalModValue(EMod.mod_all_damage_more);
            if (allDamageMore != 0)
            {
                result *= (1.0 + allDamageMore);
                if (modContributions != null) modContributions[EMod.mod_all_damage_more] = allDamageMore;
            }

            // 모든 스킬 피해 증폭 (개별 곱셈)
            double allSkillMore = battleStatus.TotalModValue(EMod.mod_all_skill_damage_more);
            if (allSkillMore != 0)
            {
                result *= (1.0 + allSkillMore);
                if (modContributions != null) modContributions[EMod.mod_all_skill_damage_more] = allSkillMore;
            }

            // [Ailment] 4단계: 모든 Ailment 피해 증폭 (개별 곱셈)
            double allAilmentMore = battleStatus.TotalModValue(EMod.mod_all_ailment_damage_more);
            if (allAilmentMore != 0)
            {
                result *= (1.0 + allAilmentMore);
                if (modContributions != null) modContributions[EMod.mod_all_ailment_damage_more] = allAilmentMore;
            }

            // 속성별 피해 증폭 (개별 곱셈)
            switch (ailmentType)
            {
                case EStatusEffect.ailment_bleeding:
                    {
                        double physicalMore = battleStatus.TotalModValue(EMod.mod_physical_damage_more);
                        if (physicalMore != 0)
                        {
                            result *= 1.0 + physicalMore;
                            if (modContributions != null) modContributions[EMod.mod_physical_damage_more] = physicalMore;
                        }

                        double bleedingMore = battleStatus.TotalModValue(EMod.mod_bleeding_damage_more);
                        if (bleedingMore != 0)
                        {
                            result *= 1.0 + bleedingMore;
                            if (modContributions != null) modContributions[EMod.mod_bleeding_damage_more] = bleedingMore;
                        }
                    }
                    break;
                case EStatusEffect.ailment_ignite:
                    {
                        double fireMore = battleStatus.TotalModValue(EMod.mod_fire_damage_more);
                        if (fireMore != 0)
                        {
                            result *= 1.0 + fireMore;
                            if (modContributions != null) modContributions[EMod.mod_fire_damage_more] = fireMore;
                        }
                        // mod_elemental_damage_more는 존재하지 않음 (mod.md 참고)

                        double igniteMore = battleStatus.TotalModValue(EMod.mod_ignite_damage_more);
                        if (igniteMore != 0)
                        {
                            result *= 1.0 + igniteMore;
                            if (modContributions != null) modContributions[EMod.mod_ignite_damage_more] = igniteMore;
                        }
                    }
                    break;
                case EStatusEffect.ailment_arctic:
                    {
                        double coldMore = battleStatus.TotalModValue(EMod.mod_cold_damage_more);
                        if (coldMore != 0)
                        {
                            result *= 1.0 + coldMore;
                            if (modContributions != null) modContributions[EMod.mod_cold_damage_more] = coldMore;
                        }

                        double arcticMore = battleStatus.TotalModValue(EMod.mod_arctic_damage_more);
                        if (arcticMore != 0)
                        {
                            result *= 1.0 + arcticMore;
                            if (modContributions != null) modContributions[EMod.mod_arctic_damage_more] = arcticMore;
                        }
                    }
                    break;
                case EStatusEffect.ailment_chill:
                    {
                        double coldMore = battleStatus.TotalModValue(EMod.mod_cold_damage_more);
                        if (coldMore != 0)
                        {
                            result *= 1.0 + coldMore;
                            if (modContributions != null) modContributions[EMod.mod_cold_damage_more] = coldMore;
                        }
                        // chill 전용 more는 없음
                    }
                    break;
                case EStatusEffect.ailment_shock:
                    {
                        double lightningMore = battleStatus.TotalModValue(EMod.mod_lightning_damage_more);
                        if (lightningMore != 0)
                        {
                            result *= 1.0 + lightningMore;
                            if (modContributions != null) modContributions[EMod.mod_lightning_damage_more] = lightningMore;
                        }

                        double shockMore = battleStatus.TotalModValue(EMod.mod_shock_damage_more);
                        if (shockMore != 0)
                        {
                            result *= 1.0 + shockMore;
                            if (modContributions != null) modContributions[EMod.mod_shock_damage_more] = shockMore;
                        }
                    }
                    break;
                case EStatusEffect.ailment_paralyze:
                    {
                        double lightningMore = battleStatus.TotalModValue(EMod.mod_lightning_damage_more);
                        if (lightningMore != 0)
                        {
                            result *= 1.0 + lightningMore;
                            if (modContributions != null) modContributions[EMod.mod_lightning_damage_more] = lightningMore;
                        }
                        // paralyze 전용 more는 없음
                    }
                    break;
                case EStatusEffect.ailment_poisoning:
                    {
                        double poisonMore = battleStatus.TotalModValue(EMod.mod_poison_damage_more);
                        if (poisonMore != 0)
                        {
                            result *= 1.0 + poisonMore;
                            if (modContributions != null) modContributions[EMod.mod_poison_damage_more] = poisonMore;
                        }

                        double poisoningMore = battleStatus.TotalModValue(EMod.mod_poisoning_damage_more);
                        if (poisoningMore != 0)
                        {
                            result *= 1.0 + poisoningMore;
                            if (modContributions != null) modContributions[EMod.mod_poisoning_damage_more] = poisoningMore;
                        }
                    }
                    break;
                case EStatusEffect.ailment_stun:
                    // stun은 more 없음
                    break;
            }

            return result;
        }

        /// <summary>
        /// Phase 10: Ailment 상세 정보 계산 (스킬 태그 필터링 포함)
        /// 인게임과 동일하게 치명타 적용 (BuffActionData_Ailment.cs:309-317 참조)
        /// </summary>
        private static void CalculateAilmentDetails(FCharacterStatus attackerStatus, FCharacterStatus defenderStatus, FSkillData skillData, ref SimulatorDamageStats stats)
        {
            stats.ailmentDetails.Clear();

            // 스킬 태그 가져오기
            var skillTags = skillData?.GetSkillDB?.SkillTags;

            // 8개의 Ailment 타입별로 처리
            var ailmentInfos = new[]
            {
                new { Type = EStatusEffect.ailment_bleeding, Name = "출혈 (Bleeding)", SkillTag = ESkillTag.skilltag_physical, ChanceMod = EMod.mod_chance_to_bleeding, AlwaysMod = EMod.mod_always_bleeding_target, Count = stats.ailmentBleedingCount, Damage = stats.ailmentBleedingDamage },
                new { Type = EStatusEffect.ailment_ignite, Name = "점화 (Ignite)", SkillTag = ESkillTag.skilltag_fire, ChanceMod = EMod.mod_chance_to_ignite, AlwaysMod = EMod.mod_always_ignite_target, Count = stats.ailmentIgniteCount, Damage = stats.ailmentIgniteDamage },
                new { Type = EStatusEffect.ailment_arctic, Name = "한기 (Arctic)", SkillTag = ESkillTag.skilltag_cold, ChanceMod = EMod.mod_chance_to_arctic, AlwaysMod = EMod.mod_always_arctic_target, Count = stats.ailmentArcticCount, Damage = stats.ailmentArcticDamage },
                new { Type = EStatusEffect.ailment_chill, Name = "냉각 (Chill)", SkillTag = ESkillTag.skilltag_cold, ChanceMod = EMod.mod_chance_to_chill, AlwaysMod = EMod.mod_always_chill_target, Count = stats.ailmentChillCount, Damage = stats.ailmentChillDamage },
                new { Type = EStatusEffect.ailment_shock, Name = "감전 (Shock)", SkillTag = ESkillTag.skilltag_lightning, ChanceMod = EMod.mod_chance_to_shock, AlwaysMod = EMod.mod_always_shock_target, Count = stats.ailmentShockCount, Damage = stats.ailmentShockDamage },
                new { Type = EStatusEffect.ailment_paralyze, Name = "마비 (Paralyze)", SkillTag = ESkillTag.skilltag_lightning, ChanceMod = EMod.mod_chance_to_paralyze, AlwaysMod = EMod.mod_always_paralyze_target, Count = stats.ailmentParalyzeCount, Damage = stats.ailmentParalyzeDamage },
                new { Type = EStatusEffect.ailment_poisoning, Name = "중독 (Poisoning)", SkillTag = ESkillTag.skilltag_poison, ChanceMod = EMod.mod_chance_to_poisoning, AlwaysMod = EMod.mod_always_poisoning_target, Count = stats.ailmentPoisoningCount, Damage = stats.ailmentPoisoningDamage },
                new { Type = EStatusEffect.ailment_stun, Name = "기절 (Stun)", SkillTag = ESkillTag.skilltag_physical, ChanceMod = EMod.mod_chance_to_stun, AlwaysMod = EMod.mod_always_stun_target, Count = stats.ailmentStunCount, Damage = stats.ailmentStunDamage }
            };

            foreach (var info in ailmentInfos)
            {
                // 스킬 태그 체크 (stun은 모든 스킬에서 발동 가능)
                if (info.Type != EStatusEffect.ailment_stun)
                {
                    if (!HasSkillTag(skillTags, info.SkillTag))
                    {
                        continue; // 스킬 태그가 없으면 표시하지 않음
                    }
                }

                // 유발 확률 가져오기
                // [Ailment] 2단계: Ailment 확률
                // 1. mod_chance_to_* : 기본 유발 확률
                // 2. mod_always_*_target : 항상 100% 유발 (값 무시, 존재하면 100%)
                // TotalModValue는 이미 비율(0.28 = 28%)을 반환
                double procChanceRatio = attackerStatus.BattleStatus.TotalModValue(info.ChanceMod);
                // UI 표시용 백분율 변환
                float procChance = (float)(procChanceRatio * 100.0);

                // mod_always_*_target이 존재하면 100%로 오버라이드
                double alwaysValue = attackerStatus.BattleStatus.TotalModValue(info.AlwaysMod);

                if (alwaysValue > 0)
                {
                    procChance = 100.0f; // 항상 발동 (UI 표시용)
                    procChanceRatio = 1.0; // 항상 발동 (계산용)
                }

                // 확률이 0% 이상인 것만 처리
                if (procChanceRatio <= 0) continue;

                AilmentDetailInfo detail = new AilmentDetailInfo();
                detail.ailmentType = info.Type;
                detail.ailmentName = info.Name;
                detail.procChance = procChance;
                detail.procCount = info.Count;

                // Flat 데미지 계산 (Ailment 기본 데미지)
                detail.flatDamage = CalculateFlatDamage(attackerStatus, info.SkillTag);

                // GameDB에서 Duration, Effect, Stack 값 가져오기
                var buffDB = GameDBClientManager.Instance?.GameDB_Skill?.Buff?.MapData;
                if (buffDB != null && buffDB.ContainsKey(info.Type))
                {
                    var buffData = buffDB[info.Type];
                    float baseDuration = (float)buffData.Duration.Value;

                    // [Ailment] 3단계: Duration MOD 적용
                    double durationMultiplier = CalculateAilmentDurationMultiplier(attackerStatus, info.Type);
                    detail.duration = baseDuration * (float)durationMultiplier;

                    // GameDB_Client_Buff.Effect는 SheetDataBuff에서 이미 배율로 변환됨
                    detail.damagePercent = (float)buffData.Effect.Value;

                    // GameDB에서 최대 스택 수 가져오기
                    int stackFromDB = buffData.Stack.Value;
                    // 시뮬레이터에서는 Stack이 0이면 기본값 1로 설정 (스택 불가능한 Ailment는 없음)
                    detail.maxStacks = stackFromDB > 0 ? stackFromDB : 1;
                }
                else
                {
                    // GameDB를 찾을 수 없는 경우 기본값 설정
                    detail.maxStacks = 1;
                }

                // CC 효과(Chill, Paralyze, Stun)인지 체크
                bool isCCEffect = info.Type == EStatusEffect.ailment_chill ||
                                 info.Type == EStatusEffect.ailment_paralyze ||
                                 info.Type == EStatusEffect.ailment_stun;

                // Inc/More 배율 계산 (MOD 기여도 추적)
                detail.incModContributions.Clear();
                detail.moreModContributions.Clear();
                double incMultiplier = CalculateAilmentIncMultiplier(attackerStatus, info.Type, detail.incModContributions);
                double moreMultiplier = CalculateAilmentMoreMultiplier(attackerStatus, info.Type, detail.moreModContributions);

                // CC 효과는 피해가 없음 (BuffActionData_Ailment.cs:171-176 참조)
                // 단, flatDamage는 캐릭터 스탯이므로 유지 (검증용)
                if (isCCEffect)
                {
                    // flatDamage는 유지 (캐릭터 스탯)
                    detail.dps = 0;
                    detail.maxStackDps = 0;
                    detail.damagePercent = 0; // 피해 비율은 0으로 표시
                }
                else
                {
                    // 시전 속도 적용 (리포트용 DPS 계산)
                    // Ailment는 Spell/Aura 적중 시 유발되므로 시전 속도가 DPS에 영향
                    detail.castSpeed = stats.castSpeed;

                    // ====================================
                    // 치명타 계산 (Spell과 동일한 방식: 치명타 일격 포함)
                    // 인게임에서는 ResultCriticalChance(null)을 사용
                    // 치명타 확률 >= 100%: 무조건 치명타 + 일격 확률 판정
                    // 치명타 확률 < 100%: 일반 치명타만
                    // ====================================
                    double critChance = attackerStatus.BattleStatus.ResultCriticalChance(null);
                    double critMultiplierRatio = attackerStatus.BattleStatus.ResultCriticalMultiplier(null);
                    double critBlowChance = attackerStatus.BattleStatus.ResultCriticalBlowChance();
                    double critBlowMultiplierRatio = attackerStatus.BattleStatus.ResultCriticalBlowmultiplier();

                    // 각 케이스의 발생 확률 계산 (치명타 확률 >= 100% 조건 체크)
                    double normalChance;
                    double critOnlyChance;
                    double critBlowActualChance;

                    if (critChance >= 1.0)  // 1.0 = 100%
                    {
                        // 100% 치명타 확정 + 추가 일격 판정
                        normalChance = 0;
                        critOnlyChance = 1.0 - critBlowChance;
                        critBlowActualChance = critBlowChance;
                    }
                    else
                    {
                        // 일반 치명타만 (치명타 일격 불가)
                        normalChance = 1.0 - critChance;
                        critOnlyChance = critChance;
                        critBlowActualChance = 0;
                    }

                    // 평균 치명타 배율 = (일반 확률 × 1.0) + (치명타 확률 × 치명타 배율) + (치명타 일격 확률 × 치명타 배율 × 치명타 일격 배율)
                    double averageCritMultiplier = (normalChance * 1.0)
                                                 + (critOnlyChance * critMultiplierRatio)
                                                 + (critBlowActualChance * critMultiplierRatio * critBlowMultiplierRatio);

                    // 치명타 정보 저장 (UI 표시용)
                    detail.critChance = critChance;
                    detail.critMultiplier = critMultiplierRatio;
                    detail.critBlowChance = critBlowChance;
                    detail.critBlowMultiplier = critBlowMultiplierRatio;
                    detail.avgCritMultiplier = averageCritMultiplier;

                    // 이론적 평균 DPS 계산
                    // DPS = Flat × 피해% × Inc × More × 유발확률 × 시전속도 × 평균치명타배율
                    detail.dps = detail.flatDamage * detail.damagePercent * incMultiplier * moreMultiplier * procChanceRatio * stats.castSpeed * averageCritMultiplier;

                    // 최대 스택 DPS = 평균 DPS × 최대 스택
                    detail.maxStackDps = detail.dps * detail.maxStacks;
                }

                // 배율 정보 저장 (UI 표시용)
                detail.incMultiplier = incMultiplier;
                detail.moreMultiplier = moreMultiplier;

                stats.ailmentDetails.Add(detail);
            }
        }

        /// <summary>
        /// Aura 버프 상세 정보 계산 (skill_contagion 등)
        /// skillaura_inevitable 장착 시 skill_contagion이 발동됨
        /// skill_contagion은 skillaura_inevitable의 SkillModEffectDamage 값을 피해 비율로 사용
        /// aura DoT는 치명타 적용 (평균 치명타 배율 사용)
        /// </summary>
        private static void CalculateDotBuffDetails(FCharacterStatus attackerStatus, FCharacterStatus defenderStatus, FSkillData skillData, int auraReinforce, ESkill aura, ref SimulatorDamageStats stats)
        {
            stats.dotBuffDetails.Clear();

            // skillaura_inevitable가 장착되어 있는 경우에만 DoT 계산
            // skill_contagion은 skillaura_inevitable 전용 버프
            if (aura != ESkill.skillaura_inevitable)
            {
                return; // inevitable 오라가 아니면 DoT 없음
            }

            var buffDB = GameDBClientManager.Instance?.GameDB_Skill?.Buff?.MapData;
            if (buffDB == null) return;

            // skill_contagion Aura 버프 처리
            if (buffDB.ContainsKey(EStatusEffect.skill_contagion))
            {
                var buffData = buffDB[EStatusEffect.skill_contagion];

                DotBuffDetailInfo detail = new DotBuffDetailInfo();
                detail.dotBuffType = EStatusEffect.skill_contagion;
                detail.dotBuffName = "전염 (Contagion)";

                // GameDB에서 기본 Duration 값 가져오기
                float baseDuration = (float)buffData.Duration.Value;

                // skill_contagion의 피해 비율은 skillaura_inevitable의 SkillModEffectDamage 값을 사용
                detail.damagePercent = 0; // 기본값

                // skill_contagion을 발동시키는 스킬(skillaura_inevitable)의 SkillDB 정보가 필요
                // skillaura_inevitable의 스킬 정보 가져오기 (ESkillType 사용)
                ESkillType inevitableSkillType = ESkillType.None;
                GameDB_Client_Skill inevitableSkillDB = null;

                // ESkill.skillaura_inevitable에서 ESkillType으로 변환
                if (GameDBUtility.TryGetSkillDBData(ESkill.skillaura_inevitable, out inevitableSkillType, out inevitableSkillDB))
                {
                    // 실제 게임 로직과 동일하게 지속시간 계산 (ResultSkillDuration 사용)
                    if (inevitableSkillDB != null && GameBattleUtilityManager.Instance != null)
                    {
                        detail.duration = (float)GameBattleUtilityManager.Instance.ResultSkillDuration(attackerStatus, inevitableSkillDB);

                        // 틱 간격은 스킬의 SkillDamageTickInterval 사용
                        detail.tickInterval = (float)inevitableSkillDB.SkillDamageTickInterval.Value.GetValue;

                        // skill_contagion의 피해 비율은 skillaura_inevitable의 SkillModEffectDamage 값 사용
                        // (skill_contagion.Effect는 0으로 설정되어 있어 사용하지 않음)
                        // Formula.GetValue()는 FLOAT_PER 타입이므로 이미 비율로 변환됨 (254 → 2.54)
                        detail.damagePercent = (float)inevitableSkillDB.SkillModEffectDamage.Formula.GetValue(auraReinforce).GetValue;
                    }
                    else
                    {
                        // 스킬 DB를 찾지 못한 경우 기본값 사용
                        detail.duration = baseDuration;
                        detail.tickInterval = 1.0f; // 기본 틱 간격
                        Debug.LogError("[DoT 계산] inevitableSkillDB 또는 GameBattleUtilityManager가 null입니다.");
                    }
                }
                else
                {
                    // 스킬을 찾지 못한 경우 기본값 사용
                    detail.duration = baseDuration;
                    detail.tickInterval = 1.0f; // 기본 틱 간격
                    Debug.LogError("[DoT 계산] skillaura_inevitable 스킬 정보를 찾을 수 없습니다.");
                }

                // ====================================
                // 인게임 공식과 동일한 계산 순서
                // CalcResultDamage: 플랫 × Effectiveness × Inc × More
                // CalcResultModDamage: × 치명타
                // ====================================

                // Step 1: 기본 Flat 피해 계산
                double flatDamage = CalculateFlatDamage(attackerStatus, ESkillTag.skilltag_poison);

                // Step 2: Effectiveness 적용 (damagePercent는 이미 배율로 변환됨)
                double totalBaseDamage = flatDamage * detail.damagePercent;

                // Step 3: Inc/More 배율 계산 및 적용
                detail.incMultiplier = CalculateDotIncMultiplier(attackerStatus, ESkillTag.skilltag_poison);
                detail.moreMultiplier = CalculateDotMoreMultiplier(attackerStatus, ESkillTag.skilltag_poison);
                double afterModifiers = totalBaseDamage * detail.incMultiplier * detail.moreMultiplier;

                // ====================================
                // Step 4: 치명타 적용 (생성 시 1회, Ailment와 동일한 방식)
                // 치명타 확률 >= 100%: 무조건 치명타 + 일격 확률 판정
                // 치명타 확률 < 100%: 일반 치명타만
                // ====================================
                double critChance = attackerStatus.BattleStatus.ResultCriticalChance(defenderStatus);
                double critMultiplierRatio = attackerStatus.BattleStatus.ResultCriticalMultiplier(defenderStatus);
                double critBlowChance = attackerStatus.BattleStatus.ResultCriticalBlowChance();
                double critBlowMultiplierRatio = attackerStatus.BattleStatus.ResultCriticalBlowmultiplier();

                // 각 케이스의 발생 확률 계산 (치명타 확률 >= 100% 조건 체크)
                double normalChance;
                double critOnlyChance;
                double critBlowActualChance;

                if (critChance >= 1.0)  // 1.0 = 100%
                {
                    // 100% 치명타 확정 + 추가 일격 판정
                    normalChance = 0;
                    critOnlyChance = 1.0 - critBlowChance;
                    critBlowActualChance = critBlowChance;
                }
                else
                {
                    // 일반 치명타만 (치명타 일격 불가)
                    normalChance = 1.0 - critChance;
                    critOnlyChance = critChance;
                    critBlowActualChance = 0;
                }

                // 평균 치명타 배율 = (일반 확률 × 1.0) + (치명타 확률 × 치명타 배율) + (치명타 일격 확률 × 치명타 배율 × 치명타 일격 배율)
                double averageCritMultiplier = (normalChance * 1.0)
                                             + (critOnlyChance * critMultiplierRatio)
                                             + (critBlowActualChance * critMultiplierRatio * critBlowMultiplierRatio);

                // 치명타 정보 저장 (UI 표시용)
                detail.critChance = critChance;
                detail.critMultiplier = critMultiplierRatio;
                detail.critBlowChance = critBlowChance;
                detail.critBlowMultiplier = critBlowMultiplierRatio;
                detail.avgCritMultiplier = averageCritMultiplier;

                // 표시용 baseDamage (Effectiveness 적용 전 플랫 피해)
                detail.baseDamage = flatDamage;

                // 최종 DPS 계산: 플랫 × Effectiveness × Inc × More × 치명타
                detail.dps = afterModifiers * averageCritMultiplier;

                stats.dotBuffDetails.Add(detail);
            }
        }

        /// <summary>
        /// DoT 증가(Inc) 배율 계산
        /// </summary>
        private static double CalculateDotIncMultiplier(FCharacterStatus attackerStatus, ESkillTag skillTag)
        {
            var battleStatus = attackerStatus.BattleStatus;
            double totalInc = 0;

            // 모든 피해 증가
            totalInc += battleStatus.TotalModValue(EMod.mod_all_damage_inc);

            // 모든 스킬 피해 증가
            totalInc += battleStatus.TotalModValue(EMod.mod_all_skill_damage_inc);

            // 속성별 피해 증가
            switch (skillTag)
            {
                case ESkillTag.skilltag_physical:
                    totalInc += battleStatus.TotalModValue(EMod.mod_physical_damage_inc);
                    break;
                case ESkillTag.skilltag_fire:
                    totalInc += battleStatus.TotalModValue(EMod.mod_fire_damage_inc);
                    totalInc += battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                    break;
                case ESkillTag.skilltag_cold:
                    totalInc += battleStatus.TotalModValue(EMod.mod_cold_damage_inc);
                    totalInc += battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                    break;
                case ESkillTag.skilltag_lightning:
                    totalInc += battleStatus.TotalModValue(EMod.mod_lightning_damage_inc);
                    totalInc += battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                    break;
                case ESkillTag.skilltag_poison:
                    totalInc += battleStatus.TotalModValue(EMod.mod_poison_damage_inc);
                    totalInc += battleStatus.TotalModValue(EMod.mod_elemental_damage_inc);
                    break;
            }

            // TotalModValue는 이미 비율을 반환 (FLOAT_PER: 20 → 0.2)
            return 1.0 + totalInc;
        }

        /// <summary>
        /// DoT 증폭(More) 배율 계산
        /// TotalModValue는 이미 비율을 반환 (FLOAT_PER: 20 → 0.2)
        /// </summary>
        private static double CalculateDotMoreMultiplier(FCharacterStatus attackerStatus, ESkillTag skillTag)
        {
            var battleStatus = attackerStatus.BattleStatus;
            double result = 1.0;

            // 모든 피해 증폭
            double allDamageMore = battleStatus.TotalModValue(EMod.mod_all_damage_more);
            if (allDamageMore != 0) result *= (1.0 + allDamageMore);

            // 모든 스킬 피해 증폭
            double allSkillMore = battleStatus.TotalModValue(EMod.mod_all_skill_damage_more);
            if (allSkillMore != 0) result *= (1.0 + allSkillMore);

            // 속성별 피해 증폭
            switch (skillTag)
            {
                case ESkillTag.skilltag_physical:
                    {
                        double physicalMore = battleStatus.TotalModValue(EMod.mod_physical_damage_more);
                        if (physicalMore != 0) result *= 1.0 + physicalMore;
                    }
                    break;
                case ESkillTag.skilltag_fire:
                    {
                        double fireMore = battleStatus.TotalModValue(EMod.mod_fire_damage_more);
                        if (fireMore != 0) result *= 1.0 + fireMore;
                    }
                    break;
                case ESkillTag.skilltag_cold:
                    {
                        double coldMore = battleStatus.TotalModValue(EMod.mod_cold_damage_more);
                        if (coldMore != 0) result *= 1.0 + coldMore;
                    }
                    break;
                case ESkillTag.skilltag_lightning:
                    {
                        double lightningMore = battleStatus.TotalModValue(EMod.mod_lightning_damage_more);
                        if (lightningMore != 0) result *= 1.0 + lightningMore;
                    }
                    break;
                case ESkillTag.skilltag_poison:
                    {
                        double poisonMore = battleStatus.TotalModValue(EMod.mod_poison_damage_more);
                        if (poisonMore != 0) result *= 1.0 + poisonMore;
                    }
                    break;
            }

            return result;
        }

        #endregion

        #region 계산 단계 브레이크다운

        /// <summary>
        /// [Aura] 1: 기본 Aura 피해 브레이크다운
        /// Added Damage (Flat 합산) 계산 - 모든 기여 MOD와 출처 추적
        /// </summary>
        /// <param name="auraTagDamageType">Aura 속성 타입</param>
        public static StageBreakdown GetAuraBaseDamageBreakdown(ESkillTag auraTagDamageType)
        {
            var breakdown = new StageBreakdown(
                "[Aura] 1: 기본 Aura 피해",
                "Added Damage (Flat 합산) - Element + Elemental + AllSkill + AllDamage",
                EBreakdownValueType.Flat  // Flat 값이므로 자연수로 표시
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Aura 기본 피해 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Aura 기본 피해 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Aura 기본 피해 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>();

                // 1. 속성별 Damage (elementDamage)
                switch (auraTagDamageType)
                {
                    case ESkillTag.skilltag_physical:
                        contributingMods.Add(EMod.mod_physical_damage);
                        break;
                    case ESkillTag.skilltag_fire:
                        contributingMods.Add(EMod.mod_fire_damage);
                        break;
                    case ESkillTag.skilltag_cold:
                        contributingMods.Add(EMod.mod_cold_damage);
                        break;
                    case ESkillTag.skilltag_lightning:
                        contributingMods.Add(EMod.mod_lightning_damage);
                        break;
                    case ESkillTag.skilltag_poison:
                        contributingMods.Add(EMod.mod_poison_damage);
                        break;
                }

                // 2. 원소 Damage (elementalDamage) - Fire/Cold/Lightning/Poison만
                bool isElemental = auraTagDamageType != ESkillTag.skilltag_physical;
                if (isElemental)
                {
                    contributingMods.Add(EMod.mod_elemental_damage);
                }

                // 3. 공통 Damage (allSkillDamage, allDamage)
                contributingMods.Add(EMod.mod_all_skill_damage);
                contributingMods.Add(EMod.mod_all_damage);

                double totalDamage = 0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    totalDamage += modValue;

                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                breakdown.finalValue = totalDamage;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Aura 기본 피해 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Aura] 2: Aura 지속시간 브레이크다운
        /// 지속시간 Inc MOD 추적
        /// </summary>
        public static StageBreakdown GetAuraDurationBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Aura] 2: Aura 지속시간",
                "지속시간 증가 % 계산 - mod_skill_duration_inc + mod_all_duration_inc",
                EBreakdownValueType.Percentage  // Inc 값이므로 %로 표시
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Aura 지속시간 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Aura 지속시간 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Aura 지속시간 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new[]
                {
                    EMod.mod_skill_duration_inc,
                    EMod.mod_all_duration_inc
                };

                double totalInc = 0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    totalInc += modValue;
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                breakdown.finalValue = totalInc;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Aura 지속시간 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Aura] 3: Aura Inc 계산 브레이크다운
        /// 9499.80%를 구성하는 개별 MOD들의 기여도를 상세 추적
        /// </summary>
        /// <param name="auraTagDamageType">Aura 속성 타입 (Poison, Fire, Cold 등)</param>
        public static StageBreakdown GetAuraIncBreakdown(ESkillTag auraTagDamageType)
        {
            var breakdown = new StageBreakdown(
                "[Aura] 3: Aura Inc",
                "Aura 피해 증가 % 계산 단계 - 모든 기여 MOD와 출처 추적",
                EBreakdownValueType.Percentage  // Inc 값이므로 %로 표시
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Aura Inc 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Aura Inc 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Aura Inc 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                // Aura Inc 계산에 기여하는 MOD 목록 (BattleSimulatorWindow.CalculateAuraDamage와 동일)
                var contributingMods = new List<EMod>();

                // 1. 속성별 Inc (elementInc)
                switch (auraTagDamageType)
                {
                    case ESkillTag.skilltag_physical:
                        contributingMods.Add(EMod.mod_physical_damage_inc);
                        break;
                    case ESkillTag.skilltag_fire:
                        contributingMods.Add(EMod.mod_fire_damage_inc);
                        break;
                    case ESkillTag.skilltag_cold:
                        contributingMods.Add(EMod.mod_cold_damage_inc);
                        break;
                    case ESkillTag.skilltag_lightning:
                        contributingMods.Add(EMod.mod_lightning_damage_inc);
                        break;
                    case ESkillTag.skilltag_poison:
                        contributingMods.Add(EMod.mod_poison_damage_inc);
                        break;
                }

                // 2. 원소 Inc (elementalInc) - Fire/Cold/Lightning/Poison만
                bool isElemental = auraTagDamageType != ESkillTag.skilltag_physical;
                if (isElemental)
                {
                    contributingMods.Add(EMod.mod_elemental_damage_inc);
                }

                // 3. 공통 Inc (allSkillInc, allDamageInc)
                contributingMods.Add(EMod.mod_all_skill_damage_inc);
                contributingMods.Add(EMod.mod_all_damage_inc);

                double totalInc = 0;

                foreach (var mod in contributingMods)
                {
                    // 이 MOD의 총 값 가져오기
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    totalInc += modValue;

                    // MOD 기여도 생성
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    // 이 MOD의 출처 정보 가져오기
                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        // 출처 이름 구성
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        // SimulatorModSource 생성
                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // FLOAT_PER 타입 MOD들의 합산 = totalInc (비율값)
                // FinalValueFormatted에서 * 100하여 백분율로 표시됨
                breakdown.finalValue = totalInc;

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Aura Inc 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Aura] 4: Aura More 브레이크다운
        /// More 배율 계산 - 모든 기여 MOD 추적
        /// </summary>
        /// <param name="auraTagDamageType">Aura 속성 타입</param>
        public static StageBreakdown GetAuraMoreBreakdown(ESkillTag auraTagDamageType)
        {
            var breakdown = new StageBreakdown(
                "[Aura] 4: Aura More",
                "More 배율 계산 - 속성별 More × 공통 More (곱셈)",
                EBreakdownValueType.Percentage  // More는 %로 표시 (내부적으로 곱셈이지만 UI에서는 %)
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Aura More 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Aura More 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Aura More 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>();

                // 1. 속성별 More (elementMore)
                switch (auraTagDamageType)
                {
                    case ESkillTag.skilltag_physical:
                        contributingMods.Add(EMod.mod_physical_damage_more);
                        break;
                    case ESkillTag.skilltag_fire:
                        contributingMods.Add(EMod.mod_fire_damage_more);
                        break;
                    case ESkillTag.skilltag_cold:
                        contributingMods.Add(EMod.mod_cold_damage_more);
                        break;
                    case ESkillTag.skilltag_lightning:
                        contributingMods.Add(EMod.mod_lightning_damage_more);
                        break;
                    case ESkillTag.skilltag_poison:
                        contributingMods.Add(EMod.mod_poison_damage_more);
                        break;
                }

                // 2. 공통 More (allSkillMore, allDamageMore)
                contributingMods.Add(EMod.mod_all_skill_damage_more);
                contributingMods.Add(EMod.mod_all_damage_more);

                double totalMoreMultiplier = 1.0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    // More는 곱셈으로 적용: (1 + modValue) 형태
                    // TotalModValue()는 이미 비율 값 반환 (예: 0.15 = 15%)
                    double moreMultiplier = 1.0 + modValue;
                    totalMoreMultiplier *= moreMultiplier;

                    // 기여도는 원래 % 값으로 표시
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // More 배율에서 1을 빼서 비율값으로 저장 (기여값 합산과 일치)
                // FinalValueFormatted에서 * 100하여 백분율로 표시
                // 예: (1 + 50%) × (1 + 30%) = 1.95x → 0.95 → 95%
                breakdown.finalValue = totalMoreMultiplier - 1.0;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Aura More 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        #region Spell Damage 브레이크다운

        /// <summary>
        /// [Spell] 1: 기본 플랫 수집 브레이크다운
        /// Added Damage (Flat 합산) 계산
        /// </summary>
        /// <param name="spellTagDamageType">Spell 속성 타입</param>
        public static StageBreakdown GetSpellBaseDamageBreakdown(ESkillTag spellTagDamageType)
        {
            var breakdown = new StageBreakdown(
                "[Spell] 1: 기본 플랫 수집",
                "Added Damage (Flat 합산) - Element + Elemental + AllSkill + AllDamage",
                EBreakdownValueType.Flat
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Spell 기본 피해 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Spell 기본 피해 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Spell 기본 피해 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>();

                // 1. 속성별 Damage
                switch (spellTagDamageType)
                {
                    case ESkillTag.skilltag_physical:
                        contributingMods.Add(EMod.mod_physical_damage);
                        break;
                    case ESkillTag.skilltag_fire:
                        contributingMods.Add(EMod.mod_fire_damage);
                        break;
                    case ESkillTag.skilltag_cold:
                        contributingMods.Add(EMod.mod_cold_damage);
                        break;
                    case ESkillTag.skilltag_lightning:
                        contributingMods.Add(EMod.mod_lightning_damage);
                        break;
                    case ESkillTag.skilltag_poison:
                        contributingMods.Add(EMod.mod_poison_damage);
                        break;
                }

                // 2. 원소 Damage
                bool isElemental = spellTagDamageType != ESkillTag.skilltag_physical;
                if (isElemental)
                {
                    contributingMods.Add(EMod.mod_elemental_damage);
                }

                // 3. 공통 Damage
                contributingMods.Add(EMod.mod_all_skill_damage);
                contributingMods.Add(EMod.mod_all_damage);

                double totalDamage = 0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    totalDamage += modValue;

                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                breakdown.finalValue = totalDamage;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Spell 기본 피해 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Spell] 2: 스킬 속성 타입 배율 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellTypeMultiplierBreakdown(ESkillTag spellTagDamageType)
        {
            var breakdown = new StageBreakdown(
                "[Spell] 2: 스킬 속성 타입 배율",
                "스킬 타입별 배율 (Fixed) - Active/Passive/Support",
                EBreakdownValueType.Multiplier
            );

            // 이 값은 고정값으로 MOD가 아니므로 상세 브레이크다운 없음
            breakdown.finalValue = 1.0; // 기본값

            var contribution = new ModContribution(
                "스킬 타입 배율 (고정값)",
                1.0,
                EModValueType.FLOAT  // 고정 배율 값
            );
            breakdown.modContributions.Add(contribution);

            return breakdown;
        }

        /// <summary>
        /// [Spell] 3: 캐스팅 속도 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellCastSpeedBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Spell] 3: 캐스팅 속도",
                "캐스팅 속도 증가 % 계산 - mod_casting_speed_inc",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Spell 캐스팅 속도 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Spell 캐스팅 속도 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Spell 캐스팅 속도 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                EMod castSpeedMod = EMod.mod_castspeed_inc;
                double modValue = battleStatus.TotalModValue(castSpeedMod);

                if (modValue != 0)
                {
                    EModValueType valueType = GetModValueType(castSpeedMod);
                    var contribution = new ModContribution(castSpeedMod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, castSpeedMod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            castSpeedMod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // 실제 시전 속도 반환 (mod_castspeed_inc가 아닌 최종 시전 속도)
                breakdown.finalValue = battleStatus.ResultSkillCastSpeed();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Spell 캐스팅 속도 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Spell] 7: 멀티플 피해 (Double/Triple) 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellMultipleDamageBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Spell] 7: 멀티플 피해",
                "Double/Triple 피해 확률 계산",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Spell 멀티플 피해 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Spell 멀티플 피해 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Spell 멀티플 피해 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                // Double Damage
                EMod doubleMod = EMod.mod_chance_to_double_damage;
                double doubleValue = battleStatus.TotalModValue(doubleMod);

                if (doubleValue != 0)
                {
                    EModValueType valueType = GetModValueType(doubleMod);
                    var contribution = new ModContribution(doubleMod.ToString(), doubleValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, doubleMod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            doubleMod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // Triple Damage
                EMod tripleMod = EMod.mod_chance_to_triple_damage;
                double tripleValue = battleStatus.TotalModValue(tripleMod);

                if (tripleValue != 0)
                {
                    EModValueType valueType = GetModValueType(tripleMod);
                    var contribution = new ModContribution(tripleMod.ToString(), tripleValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, tripleMod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            tripleMod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                breakdown.finalValue = doubleValue + tripleValue;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Spell 멀티플 피해 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Spell] 4: 크리티컬 확률 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellCritChanceBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Spell] 4: 크리티컬 확률",
                "크리티컬 확률 증가 % 계산 - mod_critical_chance_inc",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Spell 크리티컬 확률 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Spell 크리티컬 확률 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Spell 크리티컬 확률 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                EMod critChanceMod = EMod.mod_crit_chance_inc;
                double modValue = battleStatus.TotalModValue(critChanceMod);

                if (modValue != 0)
                {
                    EModValueType valueType = GetModValueType(critChanceMod);
                    var contribution = new ModContribution(critChanceMod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, critChanceMod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            critChanceMod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                breakdown.finalValue = modValue;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Spell 크리티컬 확률 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Spell] 5: Spell Inc 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellIncBreakdown(ESkillTag spellTagDamageType, SimulatorDefender defender = null, Dictionary<ESkillTag, bool> skillTags = null)
        {
            var breakdown = new StageBreakdown(
                "[Spell] 5: Spell Inc",
                "Spell 피해 증가 % 계산 - Element Inc + Elemental Inc + AllSkill Inc + AllDamage Inc + 스킬태그 Inc + 조건부 Inc",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Spell Inc 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Spell Inc 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Spell Inc 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>();

                // 1. 속성별 Inc
                switch (spellTagDamageType)
                {
                    case ESkillTag.skilltag_physical:
                        contributingMods.Add(EMod.mod_physical_damage_inc);
                        break;
                    case ESkillTag.skilltag_fire:
                        contributingMods.Add(EMod.mod_fire_damage_inc);
                        break;
                    case ESkillTag.skilltag_cold:
                        contributingMods.Add(EMod.mod_cold_damage_inc);
                        break;
                    case ESkillTag.skilltag_lightning:
                        contributingMods.Add(EMod.mod_lightning_damage_inc);
                        break;
                    case ESkillTag.skilltag_poison:
                        contributingMods.Add(EMod.mod_poison_damage_inc);
                        break;
                }

                // 2. 원소 Inc
                bool isElemental = spellTagDamageType != ESkillTag.skilltag_physical;
                if (isElemental)
                {
                    contributingMods.Add(EMod.mod_elemental_damage_inc);
                }

                // 3. 공통 Inc
                contributingMods.Add(EMod.mod_all_skill_damage_inc);
                contributingMods.Add(EMod.mod_all_damage_inc);

                // 4. 스킬 태그 기반 Inc (투사체, AOE 등)
                if (skillTags != null)
                {
                    if (skillTags.ContainsKey(ESkillTag.skilltag_projectile) && skillTags[ESkillTag.skilltag_projectile])
                    {
                        contributingMods.Add(EMod.mod_projectile_damage_inc);
                    }
                    // TODO: AOE, Chain, Duration 등 추가 시 여기에 확장
                }

                // 5. HP 조건부 Inc
                if (battleStatus.IsHPMax)
                {
                    contributingMods.Add(EMod.mod_damage_inc_on_full_life);
                }
                double currentHpPercent = (battleStatus.resultHp.Value / battleStatus.resultHpMax.Value) * 100.0;
                if (currentHpPercent <= 35.0)
                {
                    contributingMods.Add(EMod.mod_damage_inc_on_low_life);
                }

                // 6. 조건부 Inc (적 상태이상에 따른 피해 증가)
                if (defender != null)
                {
                    if (defender.targetHasBleeding)
                    {
                        contributingMods.Add(EMod.mod_inc_damage_bleeding);
                    }
                    if (defender.targetHasIgnite)
                    {
                        contributingMods.Add(EMod.mod_inc_damage_ignite);
                    }
                    if (defender.targetHasChill)
                    {
                        contributingMods.Add(EMod.mod_inc_damage_chill);
                    }
                    if (defender.targetHasArctic)
                    {
                        contributingMods.Add(EMod.mod_inc_damage_arctic);
                    }
                    if (defender.targetHasParalyze)
                    {
                        contributingMods.Add(EMod.mod_inc_damage_paralyze);
                    }
                    if (defender.targetHasPoisoning)
                    {
                        contributingMods.Add(EMod.mod_inc_damage_poisoning);
                    }
                    if (defender.targetHasShock)
                    {
                        contributingMods.Add(EMod.mod_inc_damage_shock);
                    }
                    if (defender.targetHasStun)
                    {
                        contributingMods.Add(EMod.mod_inc_damage_stun);
                    }
                }

                double totalInc = 0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    totalInc += modValue;
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // FLOAT_PER 타입 MOD들의 합산 = totalInc (비율값)
                breakdown.finalValue = totalInc;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Spell Inc 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Spell] 6: Spell More 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellMoreBreakdown(ESkillTag spellTagDamageType, Dictionary<ESkillTag, bool> skillTags = null)
        {
            var breakdown = new StageBreakdown(
                "[Spell] 6: Spell More",
                "More 배율 계산 - 속성별 More × 공통 More × 스킬태그 More (곱셈)",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Spell More 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Spell More 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Spell More 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>();

                // 1. 속성별 More
                switch (spellTagDamageType)
                {
                    case ESkillTag.skilltag_physical:
                        contributingMods.Add(EMod.mod_physical_damage_more);
                        break;
                    case ESkillTag.skilltag_fire:
                        contributingMods.Add(EMod.mod_fire_damage_more);
                        break;
                    case ESkillTag.skilltag_cold:
                        contributingMods.Add(EMod.mod_cold_damage_more);
                        break;
                    case ESkillTag.skilltag_lightning:
                        contributingMods.Add(EMod.mod_lightning_damage_more);
                        break;
                    case ESkillTag.skilltag_poison:
                        contributingMods.Add(EMod.mod_poison_damage_more);
                        break;
                }

                // 2. 공통 More (원소 More MOD는 존재하지 않음)
                contributingMods.Add(EMod.mod_all_skill_damage_more);
                contributingMods.Add(EMod.mod_all_damage_more);

                // 3. 스킬 태그 기반 More (투사체, AOE 등)
                if (skillTags != null)
                {
                    if (skillTags.ContainsKey(ESkillTag.skilltag_projectile) && skillTags[ESkillTag.skilltag_projectile])
                    {
                        contributingMods.Add(EMod.mod_projectile_damage_more);
                    }
                    // TODO: AOE, Chain, Duration 등 추가 시 여기에 확장
                }

                // More 배율 = ∏(1 + more_i), 초기값 1 (MOD 없으면 배율 1)
                // TotalModValue()는 이미 비율 값 반환 (예: 0.15 = 15%)
                double moreMultiplier = 1.0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    // More는 곱셈으로 적용: (1 + 0.5) × (1 + 0.3) = 1.5 × 1.3 = 1.95x
                    moreMultiplier *= (1 + modValue);
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // More 배율에서 1을 빼서 비율값으로 저장 (기여값 합산과 일치)
                // FinalValueFormatted에서 * 100하여 백분율로 표시
                breakdown.finalValue = moreMultiplier - 1.0;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Spell More 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        #region 브레이크다운 헬퍼 함수

        /// <summary>
        /// 모든 출처(Character, Equip, Skill, Buff)에서 EMod 소스 수집
        /// </summary>
        private static List<FModSourceInfo> GetAllModSources(FBattleStatus battleStatus, EMod modType)
        {
            List<FModSourceInfo> allSources = new List<FModSourceInfo>();

            if (battleStatus.characterData?.ModSources != null)
                allSources.AddRange(battleStatus.characterData.ModSources.GetSources(modType));

            if (battleStatus.equipData?.ModSources != null)
                allSources.AddRange(battleStatus.equipData.ModSources.GetSources(modType));

            if (battleStatus.skillData?.ModSources != null)
                allSources.AddRange(battleStatus.skillData.ModSources.GetSources(modType));

            if (battleStatus.buffData?.ModSources != null)
                allSources.AddRange(battleStatus.buffData.ModSources.GetSources(modType));

            return allSources;
        }

        /// <summary>
        /// 모든 출처(Character, Equip, Skill, Buff)에서 ESkillMod 소스 수집
        /// </summary>
        private static List<FModSourceInfo> GetAllModSources(FBattleStatus battleStatus, ESkillMod modType)
        {
            List<FModSourceInfo> allSources = new List<FModSourceInfo>();

            if (battleStatus.characterData?.ModSources != null)
                allSources.AddRange(battleStatus.characterData.ModSources.GetSources(modType));

            if (battleStatus.equipData?.ModSources != null)
                allSources.AddRange(battleStatus.equipData.ModSources.GetSources(modType));

            if (battleStatus.skillData?.ModSources != null)
                allSources.AddRange(battleStatus.skillData.ModSources.GetSources(modType));

            if (battleStatus.buffData?.ModSources != null)
                allSources.AddRange(battleStatus.buffData.ModSources.GetSources(modType));

            return allSources;
        }

        /// <summary>
        /// 모든 출처(Character, Equip, Skill, Buff)에서 ECombineMod 소스 수집
        /// </summary>
        private static List<FModSourceInfo> GetAllModSources(FBattleStatus battleStatus, ECombineMod modType)
        {
            List<FModSourceInfo> allSources = new List<FModSourceInfo>();

            if (battleStatus.characterData?.ModSources != null)
                allSources.AddRange(battleStatus.characterData.ModSources.GetSources(modType));

            if (battleStatus.equipData?.ModSources != null)
                allSources.AddRange(battleStatus.equipData.ModSources.GetSources(modType));

            if (battleStatus.skillData?.ModSources != null)
                allSources.AddRange(battleStatus.skillData.ModSources.GetSources(modType));

            if (battleStatus.buffData?.ModSources != null)
                allSources.AddRange(battleStatus.buffData.ModSources.GetSources(modType));

            return allSources;
        }

        #endregion

        /// <summary>
        /// [Spell] 5_1: 치명타 확률 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellCriticalChanceBreakdown(FCharacterStatus defenderStatus, SimulatorDefender defender = null)
        {
            var breakdown = new StageBreakdown(
                "[Spell] 5_1: 치명타 확률",
                "치명타 확률 계산 - Base × (1 + Inc + Ailment Conditional Inc)",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null || defenderStatus == null)
            {
                Debug.LogError("[치명타 확률 브레이크다운] 플레이어 또는 Defender를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[치명타 확률 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[치명타 확률 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                // Base 확률
                double critChanceBase = battleStatus.TotalModValue(EMod.mod_crit_chance);
                if (critChanceBase > 0)
                {
                    EModValueType baseType = GetModValueType(EMod.mod_crit_chance);
                    var baseContribution = new ModContribution("Base (mod_crit_chance)", critChanceBase, baseType);
                    List<FModSourceInfo> baseSourceInfos = GetAllModSources(battleStatus, EMod.mod_crit_chance);
                    foreach (var sourceInfo in baseSourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        baseContribution.sources.Add(new SimulatorModSource(
                            displayName,
                            EMod.mod_crit_chance.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(baseContribution);
                }

                // Inc 확률
                double critChanceInc = battleStatus.TotalModValue(EMod.mod_crit_chance_inc);
                if (critChanceInc > 0)
                {
                    EModValueType incType = GetModValueType(EMod.mod_crit_chance_inc);
                    var incContribution = new ModContribution("Inc (mod_crit_chance_inc)", critChanceInc, incType);
                    List<FModSourceInfo> incSourceInfos = GetAllModSources(battleStatus, EMod.mod_crit_chance_inc);
                    foreach (var sourceInfo in incSourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        incContribution.sources.Add(new SimulatorModSource(
                            displayName,
                            EMod.mod_crit_chance_inc.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(incContribution);
                }

                // Ailment 조건부 Inc (defender의 상태이상에 따라 적용)
                var ailmentMods = new List<(EMod mod, string name, Func<SimulatorDefender, bool> condition)>
                {
                    (EMod.mod_inc_crit_chance_arctic, "Arctic", d => d?.targetHasArctic ?? false),
                    (EMod.mod_inc_crit_chance_bleeding, "Bleeding", d => d?.targetHasBleeding ?? false),
                    (EMod.mod_inc_crit_chance_chill, "Chill", d => d?.targetHasChill ?? false),
                    (EMod.mod_inc_crit_chance_ignite, "Ignite", d => d?.targetHasIgnite ?? false),
                    (EMod.mod_inc_crit_chance_paralyze, "Paralyze", d => d?.targetHasParalyze ?? false),
                    (EMod.mod_inc_crit_chance_poisoning, "Poisoning", d => d?.targetHasPoisoning ?? false),
                    (EMod.mod_inc_crit_chance_shock, "Shock", d => d?.targetHasShock ?? false),
                    (EMod.mod_inc_crit_chance_stun, "Stun", d => d?.targetHasStun ?? false)
                };

                double totalAilmentInc = 0;
                foreach (var (mod, name, condition) in ailmentMods)
                {
                    // defender의 상태이상이 있을 때만 적용
                    if (!condition(defender)) continue;

                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue > 0)
                    {
                        totalAilmentInc += modValue;
                        EModValueType ailmentType = GetModValueType(mod);
                        var ailmentContribution = new ModContribution($"Ailment Inc ({name})", modValue, ailmentType);
                        List<FModSourceInfo> ailmentSourceInfos = GetAllModSources(battleStatus, mod);
                        foreach (var sourceInfo in ailmentSourceInfos)
                        {
                            string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                                ? sourceInfo.sourceType.ToString()
                                : sourceInfo.sourceName;

                            if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                                displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                            ailmentContribution.sources.Add(new SimulatorModSource(
                                displayName,
                                mod.ToString(),
                                sourceInfo.value,
                                sourceInfo.sourceType
                            ));
                        }
                        breakdown.modContributions.Add(ailmentContribution);
                    }
                }

                // 최종 계산: Base × (1 + Inc + AilmentInc)
                // finalValue는 비율값으로 저장, FinalValueFormatted에서 * 100하여 백분율로 표시
                double totalInc = critChanceInc + totalAilmentInc;
                breakdown.finalValue = critChanceBase * (1.0 + totalInc);

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[치명타 확률 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Spell] 5_2: 치명타 배율 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellCriticalMultiplierBreakdown(FCharacterStatus defenderStatus)
        {
            var breakdown = new StageBreakdown(
                "[Spell] 5_2: 치명타 배율",
                "치명타 배율 계산 - Base × (1 + Inc)",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null || defenderStatus == null)
            {
                Debug.LogError("[치명타 배율 브레이크다운] 플레이어 또는 Defender를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[치명타 배율 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[치명타 배율 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                // Base 배율
                double critMultiplierBase = battleStatus.TotalModValue(EMod.mod_crit_multiplier);
                if (critMultiplierBase > 0)
                {
                    EModValueType baseType = GetModValueType(EMod.mod_crit_multiplier);
                    var baseContribution = new ModContribution("Base (mod_crit_multiplier)", critMultiplierBase, baseType);
                    List<FModSourceInfo> baseSourceInfos = GetAllModSources(battleStatus, EMod.mod_crit_multiplier);
                    foreach (var sourceInfo in baseSourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        baseContribution.sources.Add(new SimulatorModSource(
                            displayName,
                            EMod.mod_crit_multiplier.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(baseContribution);
                }

                // Inc 배율
                double critMultiplierInc = battleStatus.TotalModValue(EMod.mod_crit_multiplier_inc);
                if (critMultiplierInc > 0)
                {
                    EModValueType incType = GetModValueType(EMod.mod_crit_multiplier_inc);
                    var incContribution = new ModContribution("Inc (mod_crit_multiplier_inc)", critMultiplierInc, incType);
                    List<FModSourceInfo> incSourceInfos = GetAllModSources(battleStatus, EMod.mod_crit_multiplier_inc);
                    foreach (var sourceInfo in incSourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        incContribution.sources.Add(new SimulatorModSource(
                            displayName,
                            EMod.mod_crit_multiplier_inc.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(incContribution);
                }

                // 최종 계산 (실제 계산은 ResultCriticalMultiplier 메서드에서 수행됨)
                // 비율값 저장, FinalValueFormatted에서 * 100하여 백분율로 표시
                breakdown.finalValue = battleStatus.ResultCriticalMultiplier(defenderStatus);

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[치명타 배율 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Spell] 5_3: 치명타 일격 확률 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellCriticalBlowChanceBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Spell] 5_3: 치명타 일격 확률",
                "치명타 일격 확률 계산 - Base × (1 + Inc)",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[치명타 일격 확률 브레이크다운] 플레이어를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[치명타 일격 확률 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[치명타 일격 확률 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                // Base 확률
                double critBlowChanceBase = battleStatus.TotalModValue(EMod.mod_crit_blow_chance);
                if (critBlowChanceBase > 0)
                {
                    EModValueType baseType = GetModValueType(EMod.mod_crit_blow_chance);
                    var baseContribution = new ModContribution("Base (mod_crit_blow_chance)", critBlowChanceBase, baseType);
                    List<FModSourceInfo> baseSourceInfos = GetAllModSources(battleStatus, EMod.mod_crit_blow_chance);
                    foreach (var sourceInfo in baseSourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        baseContribution.sources.Add(new SimulatorModSource(
                            displayName,
                            EMod.mod_crit_blow_chance.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(baseContribution);
                }

                // Inc 확률
                double critBlowChanceInc = battleStatus.TotalModValue(EMod.mod_crit_blow_chance_inc);
                if (critBlowChanceInc > 0)
                {
                    EModValueType incType = GetModValueType(EMod.mod_crit_blow_chance_inc);
                    var incContribution = new ModContribution("Inc (mod_crit_blow_chance_inc)", critBlowChanceInc, incType);
                    List<FModSourceInfo> incSourceInfos = GetAllModSources(battleStatus, EMod.mod_crit_blow_chance_inc);
                    foreach (var sourceInfo in incSourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        incContribution.sources.Add(new SimulatorModSource(
                            displayName,
                            EMod.mod_crit_blow_chance_inc.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(incContribution);
                }

                // 최종 계산 (실제 계산은 ResultCriticalBlowChance 메서드에서 수행됨)
                // 비율값 저장, FinalValueFormatted에서 * 100하여 백분율로 표시
                breakdown.finalValue = battleStatus.ResultCriticalBlowChance();

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[치명타 일격 확률 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Spell] 5_4: 치명타 일격 배율 브레이크다운
        /// </summary>
        public static StageBreakdown GetSpellCriticalBlowMultiplierBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Spell] 5_4: 치명타 일격 배율",
                "치명타 일격 배율 계산 - Base × (1 + Inc)",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[치명타 일격 배율 브레이크다운] 플레이어를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[치명타 일격 배율 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[치명타 일격 배율 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                // Base 배율
                double critBlowMultiplierBase = battleStatus.TotalModValue(EMod.mod_crit_blow_multiplier);
                if (critBlowMultiplierBase > 0)
                {
                    EModValueType baseType = GetModValueType(EMod.mod_crit_blow_multiplier);
                    var baseContribution = new ModContribution("Base (mod_crit_blow_multiplier)", critBlowMultiplierBase, baseType);
                    List<FModSourceInfo> baseSourceInfos = GetAllModSources(battleStatus, EMod.mod_crit_blow_multiplier);
                    foreach (var sourceInfo in baseSourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        baseContribution.sources.Add(new SimulatorModSource(
                            displayName,
                            EMod.mod_crit_blow_multiplier.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(baseContribution);
                }

                // Inc 배율
                double critBlowMultiplierInc = battleStatus.TotalModValue(EMod.mod_crit_blow_multiplier_inc);
                if (critBlowMultiplierInc > 0)
                {
                    EModValueType incType = GetModValueType(EMod.mod_crit_blow_multiplier_inc);
                    var incContribution = new ModContribution("Inc (mod_crit_blow_multiplier_inc)", critBlowMultiplierInc, incType);
                    List<FModSourceInfo> incSourceInfos = GetAllModSources(battleStatus, EMod.mod_crit_blow_multiplier_inc);
                    foreach (var sourceInfo in incSourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        incContribution.sources.Add(new SimulatorModSource(
                            displayName,
                            EMod.mod_crit_blow_multiplier_inc.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(incContribution);
                }

                // 최종 계산 (실제 계산은 ResultCriticalBlowmultiplier 메서드에서 수행됨)
                // 비율값 저장, FinalValueFormatted에서 * 100하여 백분율로 표시
                breakdown.finalValue = battleStatus.ResultCriticalBlowmultiplier();

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[치명타 일격 배율 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        #endregion

        #region Movement & Resource 브레이크다운

        /// <summary>
        /// GameDB에서 MOD의 정확한 ValueType 조회
        /// </summary>
        private static EModValueType GetModValueType(EMod mod)
        {
            // GameDB에서 MOD 정보 조회
            var modDB = GameDBClientManager.Instance?.GameDB_Mod?.Mod;
            if (modDB != null && modDB.MapData != null && modDB.MapData.TryGetValue(mod, out var modData))
            {
                return modData.ValueType;
            }

            // GameDB 조회 실패 시 폴백: 이름 패턴으로 판별
            string modName = mod.ToString();
            return modName.Contains("_inc") || modName.Contains("_more") ||
                   modName.Contains("_red") || modName.Contains("_rate") ||
                   modName.Contains("_chance") || modName.Contains("_prob")
                ? EModValueType.FLOAT_PER  // 퍼센트
                : EModValueType.FLOAT;     // 평탄값
        }

        /// <summary>
        /// [Movement] 1: 이동속도 브레이크다운
        /// </summary>
        public static StageBreakdown GetMovementSpeedBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Movement] 1: 이동속도",
                "이동속도 기본값 및 증가 % 계산 - mod_movementspeed + mod_movementspeed_inc",
                EBreakdownValueType.Flat
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Movement 이동속도 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Movement 이동속도 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Movement 이동속도 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>
                {
                    EMod.mod_movementspeed,
                    EMod.mod_movementspeed_inc,
                    EMod.mod_movementspeed_inc_on_full_life,
                    EMod.mod_movementspeed_inc_on_low_life
                };

                double totalValue = 0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    totalValue += modValue;

                    // MOD 이름으로 ValueType 자동 판별
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // 최종 값은 ResultMovementSpeed()로 계산
                breakdown.finalValue = battleStatus.ResultMovementSpeed();

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Movement 이동속도 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Resource] 1: 생명력 브레이크다운
        /// </summary>
        public static StageBreakdown GetLifeBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Resource] 1: 생명력",
                "최대 생명력 계산 - mod_life (Flat) + mod_life_inc (%)",
                EBreakdownValueType.Flat
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Resource 생명력 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Resource 생명력 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Resource 생명력 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>
                {
                    EMod.mod_life,
                    EMod.mod_life_inc
                };

                double totalValue = 0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    totalValue += modValue;

                    // MOD 이름으로 ValueType 자동 판별
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // 최종 값은 ResultMaxLife()로 계산
                breakdown.finalValue = battleStatus.ResultMaxLife();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Resource 생명력 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Resource] 1: 생명력 재생 브레이크다운
        /// </summary>
        public static StageBreakdown GetLifeRegenBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Resource] 1: 생명력 재생",
                "초당 생명력 재생 계산 - (maxHp * 0.02 + mod_life_regen) * (1 + mod_life_regen_inc / 100)",
                EBreakdownValueType.Flat
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Resource 생명력 재생 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Resource 생명력 재생 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Resource 생명력 재생 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>
                {
                    EMod.mod_life_regen,
                    EMod.mod_life_regen_inc
                };

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    // MOD 이름으로 ValueType 자동 판별
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // 기본 재생률 (최대 생명력의 2%) - 플랫 값
                double baseRegen = battleStatus.ResultMaxLife() * 0.02;
                var baseContribution = new ModContribution("기본 재생 (최대 생명력의 2%)", baseRegen, EModValueType.FLOAT);
                breakdown.modContributions.Insert(0, baseContribution);

                // 최종 값은 ResultLifeRegen()로 계산
                breakdown.finalValue = battleStatus.ResultLifeRegen();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Resource 생명력 재생 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Resource] 2: 마나 브레이크다운
        /// </summary>
        public static StageBreakdown GetManaBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Resource] 2: 마나",
                "최대 마나 계산 - mod_mana (Flat) + mod_mana_inc (%)",
                EBreakdownValueType.Flat
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Resource 마나 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Resource 마나 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Resource 마나 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>
                {
                    EMod.mod_mana,
                    EMod.mod_mana_inc
                };

                double totalValue = 0;

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    totalValue += modValue;

                    // MOD 이름으로 ValueType 자동 판별
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // 최종 값은 ResultMaxMana()로 계산
                breakdown.finalValue = battleStatus.ResultMaxMana();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Resource 마나 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Resource] 2: 마나 재생 브레이크다운
        /// </summary>
        public static StageBreakdown GetManaRegenBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Resource] 2: 마나 재생",
                "초당 마나 재생 계산 - (maxMp * 0.02 + mod_mana_regen) * (1 + mod_mana_regen_rate_inc / 100)",
                EBreakdownValueType.Flat
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Resource 마나 재생 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Resource 마나 재생 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Resource 마나 재생 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                var contributingMods = new List<EMod>
                {
                    EMod.mod_mana_regen,
                    EMod.mod_mana_regen_rate_inc
                };

                foreach (var mod in contributingMods)
                {
                    double modValue = battleStatus.TotalModValue(mod);
                    if (modValue == 0) continue;

                    // MOD 이름으로 ValueType 자동 판별
                    EModValueType valueType = GetModValueType(mod);
                    var contribution = new ModContribution(mod.ToString(), modValue, valueType);

                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, mod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;

                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                        {
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";
                        }

                        var source = new SimulatorModSource(
                            displayName,
                            mod.ToString(),
                            sourceInfo.value,
                            sourceInfo.sourceType
                        );

                        contribution.sources.Add(source);
                    }

                    breakdown.modContributions.Add(contribution);
                }

                // 기본 재생률 (최대 마나의 2%) - 플랫 값
                double baseRegen = battleStatus.ResultMaxMana() * 0.02;
                var baseContribution = new ModContribution("기본 재생 (최대 마나의 2%)", baseRegen, EModValueType.FLOAT);
                breakdown.modContributions.Insert(0, baseContribution);

                // 최종 값은 ResultManaRegen()로 계산
                breakdown.finalValue = battleStatus.ResultManaRegen();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Resource 마나 재생 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        #endregion

        #region Ailment Damage 브레이크다운

        /// <summary>
        /// [Ailment] 1: 기본 Ailment 피해 브레이크다운
        /// Ailment는 스킬/공격으로부터 파생되므로 ailmentDetails를 파라미터로 받음
        /// </summary>
        public static StageBreakdown GetAilmentBaseDamageBreakdown(List<AilmentDetailInfo> ailmentDetails)
        {
            var breakdown = new StageBreakdown(
                "[Ailment] 1: 기본 Ailment 피해",
                "각 상태이상별 기본 피해 (Flat 합산)",
                EBreakdownValueType.Flat
            );

            if (ailmentDetails == null || ailmentDetails.Count == 0)
            {
                breakdown.finalValue = 0;
                return breakdown;
            }

            try
            {
                // 각 Ailment Detail에서 flatDamage를 합산
                foreach (var detail in ailmentDetails)
                {
                    var contribution = new ModContribution(
                        $"{detail.ailmentName} (기본 피해)",
                        detail.flatDamage,
                        EModValueType.FLOAT  // Ailment 피해는 플랫 값
                    );

                    breakdown.modContributions.Add(contribution);
                    breakdown.finalValue += detail.flatDamage;
                }

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ailment 기본 피해 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Ailment] 2: Ailment 확률 브레이크다운
        /// </summary>
        public static StageBreakdown GetAilmentChanceBreakdown(List<AilmentDetailInfo> ailmentDetails)
        {
            var breakdown = new StageBreakdown(
                "[Ailment] 2: Ailment 확률",
                "각 상태이상별 발동 확률 (평균)",
                EBreakdownValueType.Percentage
            );

            if (ailmentDetails == null || ailmentDetails.Count == 0)
            {
                breakdown.finalValue = 0;
                return breakdown;
            }

            try
            {
                double totalChance = 0;
                foreach (var detail in ailmentDetails)
                {
                    var contribution = new ModContribution(
                        $"{detail.ailmentName} (발동 확률)",
                        detail.procChance,
                        EModValueType.FLOAT_PER  // 확률은 백분율
                    );

                    breakdown.modContributions.Add(contribution);
                    totalChance += detail.procChance;
                }

                breakdown.finalValue = totalChance / ailmentDetails.Count; // 평균

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ailment 확률 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Ailment] 3: Ailment 지속시간 브레이크다운
        /// </summary>
        public static StageBreakdown GetAilmentDurationBreakdown(List<AilmentDetailInfo> ailmentDetails)
        {
            var breakdown = new StageBreakdown(
                "[Ailment] 3: Ailment 지속시간",
                "각 상태이상별 지속 시간 (평균, 초)",
                EBreakdownValueType.Flat  // 초 단위이므로 Flat
            );

            if (ailmentDetails == null || ailmentDetails.Count == 0)
            {
                breakdown.finalValue = 0;
                return breakdown;
            }

            try
            {
                double totalDuration = 0;
                foreach (var detail in ailmentDetails)
                {
                    var contribution = new ModContribution(
                        $"{detail.ailmentName} (지속시간)",
                        detail.duration,
                        EModValueType.FLOAT  // 지속시간(초)은 플랫 값
                    );

                    breakdown.modContributions.Add(contribution);
                    totalDuration += detail.duration;
                }

                breakdown.finalValue = totalDuration / ailmentDetails.Count; // 평균

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ailment 지속시간 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Ailment] 4: 최종 Ailment 피해 브레이크다운
        /// </summary>
        public static StageBreakdown GetAilmentFinalDamageBreakdown(List<AilmentDetailInfo> ailmentDetails)
        {
            var breakdown = new StageBreakdown(
                "[Ailment] 4: 최종 Ailment 피해",
                "각 상태이상별 최대 스택 DPS (합계)",
                EBreakdownValueType.Flat
            );

            if (ailmentDetails == null || ailmentDetails.Count == 0)
            {
                breakdown.finalValue = 0;
                return breakdown;
            }

            try
            {
                foreach (var detail in ailmentDetails)
                {
                    var contribution = new ModContribution(
                        $"{detail.ailmentName} (최대 스택 DPS)",
                        detail.maxStackDps,
                        EModValueType.FLOAT  // DPS는 플랫 값
                    );

                    breakdown.modContributions.Add(contribution);
                    breakdown.finalValue += detail.maxStackDps;
                }

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ailment 최종 피해 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        #endregion

        #region Curse Breakdown Methods

        /// <summary>
        /// [Curse] 1: 저주 스킬 시전 브레이크다운
        /// </summary>
        public static StageBreakdown GetCurseSkillCastingBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Curse] 1: 저주 스킬 시전",
                "저주 스킬 시전 능력 (범위, 쿨다운, 슬롯) - skill_mod_curse_cooltime 방식",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Curse 스킬 시전 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Curse 스킬 시전 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Curse 스킬 시전 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                // 범위 증가
                EMod aoeMod = EMod.mod_curse_skill_aoe_radius_inc;
                double aoeValue = battleStatus.TotalModValue(aoeMod);
                if (aoeValue != 0)
                {
                    var contribution = new ModContribution(aoeMod.ToString(), aoeValue, GetModValueType(aoeMod));
                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, aoeMod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;
                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        contribution.sources.Add(new SimulatorModSource(
                            displayName, aoeMod.ToString(), sourceInfo.value, sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(contribution);
                }

                // 쿨다운 증가/감소 (FLOAT 타입)
                EMod cooldownMod = EMod.mod_curse_skill_cooltime;
                double cooldownValue = battleStatus.TotalModValue(cooldownMod);
                if (cooldownValue != 0)
                {
                    var contribution = new ModContribution(cooldownMod.ToString(), cooldownValue, GetModValueType(cooldownMod));
                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, cooldownMod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;
                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        contribution.sources.Add(new SimulatorModSource(
                            displayName, cooldownMod.ToString(), sourceInfo.value, sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(contribution);
                }

                EMod cooldownRedMod = EMod.mod_curse_skill_cooltime_red;
                double cooldownRedValue = battleStatus.TotalModValue(cooldownRedMod);
                if (cooldownRedValue != 0)
                {
                    var contribution = new ModContribution(cooldownRedMod.ToString(), cooldownRedValue, GetModValueType(cooldownRedMod));
                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, cooldownRedMod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;
                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        contribution.sources.Add(new SimulatorModSource(
                            displayName, cooldownRedMod.ToString(), sourceInfo.value, sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(contribution);
                }

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Curse 스킬 시전 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Curse] 2: 저주 효과 적용 브레이크다운
        /// </summary>
        public static StageBreakdown GetCurseEffectApplicationBreakdown()
        {
            var breakdown = new StageBreakdown(
                "[Curse] 2: 저주 효과 적용",
                "적에게 적용되는 저주의 지속시간 및 효과 배율",
                EBreakdownValueType.Percentage
            );

            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null)
            {
                Debug.LogError("[Curse 효과 적용 브레이크다운] 플레이어 캐릭터를 찾을 수 없습니다.");
                return breakdown;
            }

            var battleStatus = player.CharacterStatus?.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Curse 효과 적용 브레이크다운] BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var characterData = battleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Curse 효과 적용 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                // 지속시간 증가
                EMod durationMod = EMod.mod_curse_skill_duration_inc_on_enemy;
                double durationValue = battleStatus.TotalModValue(durationMod);
                if (durationValue != 0)
                {
                    EModValueType valueType = GetModValueType(durationMod);
                    var contribution = new ModContribution(durationMod.ToString(), durationValue, valueType);
                    List<FModSourceInfo> sourceInfos = GetAllModSources(battleStatus, durationMod);
                    foreach (var sourceInfo in sourceInfos)
                    {
                        string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                            ? sourceInfo.sourceType.ToString()
                            : sourceInfo.sourceName;
                        if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                            displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                        contribution.sources.Add(new SimulatorModSource(
                            displayName, durationMod.ToString(), sourceInfo.value, sourceInfo.sourceType
                        ));
                    }
                    breakdown.modContributions.Add(contribution);
                }


            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Curse 효과 적용 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Curse] 3: Ailment 시너지 브레이크다운
        /// </summary>
        public static StageBreakdown GetCurseAilmentSynergyBreakdown(FCharacterStatus defenderStatus)
        {
            var breakdown = new StageBreakdown(
                "[Curse] 3: Ailment 시너지",
                "저주받은 적에게 Ailment 확률 추가",
                EBreakdownValueType.Percentage
            );

            if (defenderStatus == null || defenderStatus.BattleStatus == null)
            {
                Debug.LogError("[Curse Ailment 시너지 브레이크다운] 적 BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var enemyBattleStatus = defenderStatus.BattleStatus;
            var characterData = enemyBattleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Curse Ailment 시너지 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                EMod[] ailmentMods = new EMod[]
                {
                    EMod.mod_cursed_enemy_chance_to_arctic,
                    EMod.mod_cursed_enemy_chance_to_bleeding,
                    EMod.mod_cursed_enemy_chance_to_chill,
                    EMod.mod_cursed_enemy_chance_to_ignite,
                    EMod.mod_cursed_enemy_chance_to_paralyze,
                    EMod.mod_cursed_enemy_chance_to_poisoning,
                    EMod.mod_cursed_enemy_chance_to_shock,
                    EMod.mod_cursed_enemy_chance_to_stun
                };

                foreach (var mod in ailmentMods)
                {
                    double modValue = enemyBattleStatus.TotalModValue(mod);
                    if (modValue != 0)
                    {
                        EModValueType valueType = GetModValueType(mod);
                        var contribution = new ModContribution(mod.ToString(), modValue, valueType);
                        List<FModSourceInfo> sourceInfos = GetAllModSources(enemyBattleStatus, mod);
                        foreach (var sourceInfo in sourceInfos)
                        {
                            string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                                ? sourceInfo.sourceType.ToString()
                                : sourceInfo.sourceName;
                            if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                                displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                            contribution.sources.Add(new SimulatorModSource(
                                displayName, mod.ToString(), sourceInfo.value, sourceInfo.sourceType
                            ));
                        }
                        breakdown.modContributions.Add(contribution);
                    }
                }

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Curse Ailment 시너지 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Curse] 4: 저항 약화 브레이크다운
        /// </summary>
        public static StageBreakdown GetCurseResistanceWeaknessBreakdown(FCharacterStatus defenderStatus)
        {
            var breakdown = new StageBreakdown(
                "[Curse] 4: 저항 약화",
                "저주받은 적의 속성 저항 감소",
                EBreakdownValueType.Percentage
            );

            if (defenderStatus == null || defenderStatus.BattleStatus == null)
            {
                Debug.LogError("[Curse 저항 약화 브레이크다운] 적 BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var enemyBattleStatus = defenderStatus.BattleStatus;
            var characterData = enemyBattleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Curse 저항 약화 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                EMod[] resistanceMods = new EMod[]
                {
                    EMod.mod_cursed_enemy_cold_resistance,
                    EMod.mod_cursed_enemy_cold_resistance_red,
                    EMod.mod_cursed_enemy_fire_resistance,
                    EMod.mod_cursed_enemy_fire_resistance_red,
                    EMod.mod_cursed_enemy_lightning_resistance,
                    EMod.mod_cursed_enemy_lightning_resistance_red,
                    EMod.mod_cursed_enemy_poison_resistance,
                    EMod.mod_cursed_enemy_poison_resistance_red,
                    EMod.mod_cursed_enemy_elemental_resistance,
                    EMod.mod_cursed_enemy_elemental_resistance_red
                };

                foreach (var mod in resistanceMods)
                {
                    double modValue = enemyBattleStatus.TotalModValue(mod);
                    if (modValue != 0)
                    {
                        EModValueType valueType = GetModValueType(mod);
                        var contribution = new ModContribution(mod.ToString(), modValue, valueType);
                        List<FModSourceInfo> sourceInfos = GetAllModSources(enemyBattleStatus, mod);
                        foreach (var sourceInfo in sourceInfos)
                        {
                            string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                                ? sourceInfo.sourceType.ToString()
                                : sourceInfo.sourceName;
                            if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                                displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                            contribution.sources.Add(new SimulatorModSource(
                                displayName, mod.ToString(), sourceInfo.value, sourceInfo.sourceType
                            ));
                        }
                        breakdown.modContributions.Add(contribution);
                    }
                }

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Curse 저항 약화 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        /// <summary>
        /// [Curse] 5: 전투 능력 약화 브레이크다운
        /// </summary>
        public static StageBreakdown GetCurseCombatDebuffsBreakdown(FCharacterStatus defenderStatus)
        {
            var breakdown = new StageBreakdown(
                "[Curse] 5: 전투 능력 약화",
                "저주받은 적의 전투 능력 변화",
                EBreakdownValueType.Percentage
            );

            if (defenderStatus == null || defenderStatus.BattleStatus == null)
            {
                Debug.LogError("[Curse 전투 능력 약화 브레이크다운] 적 BattleStatus를 찾을 수 없습니다.");
                return breakdown;
            }

            var enemyBattleStatus = defenderStatus.BattleStatus;
            var characterData = enemyBattleStatus.characterData;
            if (characterData == null || characterData.ModSources == null)
            {
                Debug.LogError("[Curse 전투 능력 약화 브레이크다운] ModSources를 찾을 수 없습니다.");
                return breakdown;
            }

            try
            {
                EMod[] debuffMods = new EMod[]
                {
                    EMod.mod_cursed_enemy_action_speed_red,
                    EMod.mod_cursed_enemy_crit_chance,
                    EMod.mod_cursed_enemy_crit_chance_inc,
                    EMod.mod_cursed_enemy_crit_multiplier,
                    EMod.mod_cursed_enemy_take_inc_physical_damage
                };

                foreach (var mod in debuffMods)
                {
                    double modValue = enemyBattleStatus.TotalModValue(mod);
                    if (modValue != 0)
                    {
                        EModValueType valueType = GetModValueType(mod);
                        var contribution = new ModContribution(mod.ToString(), modValue, valueType);
                        List<FModSourceInfo> sourceInfos = GetAllModSources(enemyBattleStatus, mod);
                        foreach (var sourceInfo in sourceInfos)
                        {
                            string displayName = string.IsNullOrEmpty(sourceInfo.sourceName)
                                ? sourceInfo.sourceType.ToString()
                                : sourceInfo.sourceName;
                            if (!string.IsNullOrEmpty(sourceInfo.sourceDetail))
                                displayName = $"{displayName} ({sourceInfo.sourceDetail})";

                            contribution.sources.Add(new SimulatorModSource(
                                displayName, mod.ToString(), sourceInfo.value, sourceInfo.sourceType
                            ));
                        }
                        breakdown.modContributions.Add(contribution);
                    }
                }

            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Curse 전투 능력 약화 브레이크다운] 계산 중 에러: {e.Message}\n{e.StackTrace}");
            }

            return breakdown;
        }

        #endregion

        #region UI 표시용 유틸리티

        /// <summary>
        /// MOD 값을 UI 표시용 백분율 문자열로 변환
        /// FLOAT_PER 타입인 경우 *100하여 백분율로 표시
        /// </summary>
        /// <param name="mod">MOD 타입</param>
        /// <param name="value">표시할 값 (배수 형태, 예: 0.075)</param>
        /// <param name="format">숫자 포맷 (기본값: F3)</param>
        /// <param name="suffix">접미사 (기본값: %)</param>
        /// <returns>포맷된 문자열 (예: "7.500%")</returns>
        public static string FormatModValueForUI(EMod mod, double value, string format = "F3", string suffix = "%")
        {
            double displayValue = ConvertModValueForUI(mod, value);
            return $"{displayValue.ToString(format)}{suffix}";
        }

        /// <summary>
        /// MOD 값을 UI 표시용 숫자로 변환
        /// FLOAT_PER 타입인 경우 *100하여 백분율로 변환
        /// </summary>
        /// <param name="mod">MOD 타입</param>
        /// <param name="value">변환할 값 (배수 형태, 예: 0.075)</param>
        /// <returns>UI 표시용 값 (예: 7.5)</returns>
        public static double ConvertModValueForUI(EMod mod, double value)
        {
            if (IsFloatPerMod(mod))
            {
                return value * 100.0;
            }
            return value;
        }

        /// <summary>
        /// MOD가 FLOAT_PER 타입인지 확인
        /// </summary>
        /// <param name="mod">확인할 MOD</param>
        /// <returns>FLOAT_PER 타입이면 true</returns>
        public static bool IsFloatPerMod(EMod mod)
        {
            var modDB = GameDBClientManager.Instance?.GameDB_Mod?.Mod?.MapData;
            if (modDB != null && modDB.TryGetValue(mod, out var modData))
            {
                return modData.ValueType == EModValueType.FLOAT_PER;
            }
            return false;
        }

        /// <summary>
        /// 비율 값을 백분율로 변환 (MOD가 아닌 일반 비율값용)
        /// 예: 0.254 → 25.4, 2.5 → 250
        /// </summary>
        /// <param name="ratio">비율 값</param>
        /// <returns>백분율 값</returns>
        public static double ConvertRatioToPercent(double ratio)
        {
            return ratio * 100.0;
        }

        /// <summary>
        /// 비율 값을 백분율 문자열로 포맷 (MOD가 아닌 일반 비율값용)
        /// </summary>
        /// <param name="ratio">비율 값</param>
        /// <param name="format">숫자 포맷 (기본값: F2)</param>
        /// <param name="suffix">접미사 (기본값: %)</param>
        /// <returns>포맷된 문자열</returns>
        public static string FormatRatioToPercent(double ratio, string format = "F2", string suffix = "%")
        {
            return $"{ConvertRatioToPercent(ratio).ToString(format)}{suffix}";
        }

        /// <summary>
        /// ModValue를 UI 표시용 문자열로 포맷
        /// FLOAT_PER 타입인 경우 GetValue(배율)를 백분율로 변환하여 표시
        /// 예: GetValue=3.268 → "326.800%"
        /// </summary>
        /// <param name="modValue">ModValue 객체</param>
        /// <param name="format">숫자 포맷 (기본값: F3)</param>
        /// <returns>포맷된 문자열</returns>
        public static string FormatModValueForUI(ModValue modValue, string format = "F3")
        {
            if (modValue == null)
                return "0.000";

            if (modValue.ValueType == EModValueType.FLOAT_PER)
            {
                // FLOAT_PER: GetValue는 배율(예: 3.268), UI에는 백분율(326.8%)로 표시
                double percentValue = modValue.GetValue * 100.0;
                return $"{percentValue.ToString(format)}%";
            }
            else if (modValue.ValueType == EModValueType.INT)
            {
                return $"{(int)modValue.GetValue}";
            }
            else
            {
                return $"{modValue.GetValue.ToString(format)}";
            }
        }

        /// <summary>
        /// ModValue를 UI 표시용 문자열로 포맷 (접미사 지정 가능)
        /// </summary>
        /// <param name="modValue">ModValue 객체</param>
        /// <param name="format">숫자 포맷 (기본값: F3)</param>
        /// <param name="suffix">접미사 (FLOAT_PER가 아닌 경우 사용)</param>
        /// <returns>포맷된 문자열</returns>
        public static string FormatModValueForUI(ModValue modValue, string format, string suffix)
        {
            if (modValue == null)
                return $"0.000{suffix}";

            if (modValue.ValueType == EModValueType.FLOAT_PER)
            {
                // FLOAT_PER: GetValue는 배율(예: 3.268), UI에는 백분율(326.8%)로 표시
                double percentValue = modValue.GetValue * 100.0;
                return $"{percentValue.ToString(format)}%";
            }
            else if (modValue.ValueType == EModValueType.INT)
            {
                return $"{(int)modValue.GetValue}{suffix}";
            }
            else
            {
                return $"{modValue.GetValue.ToString(format)}{suffix}";
            }
        }

        #endregion

        #endregion
    }

    /// <summary>
    /// 데미지 통계 데이터 (Phase 6.3)
    /// </summary>
    [System.Serializable]
    /// <summary>
    /// Ailment 상세 정보 (단일 Ailment 타입)
    /// </summary>
    public class AilmentDetailInfo
    {
        public EStatusEffect ailmentType;           // Ailment 타입
        public string ailmentName;          // Ailment 이름 (한글)

        // 기본 정보
        public int maxStacks;               // 최대 스택 수
        public float duration;              // 지속 시간 (초)
        public float damagePercent;         // Aura 피해 % (Effect 값)

        // 확률 및 발동 정보
        public float procChance;            // 유발 확률 (%)
        public double procCount;            // 발동 횟수 (기댓값, 예: 30% = 0.3)

        // 데미지 정보
        public double flatDamage;           // Flat 데미지 (mod 합계)
        public double dps;                  // DPS (이론적 평균: Flat × 피해% × Inc × More × 유발 확률)
        public double maxStackDps;          // 최대 스택 DPS (이론적 평균)

        // 배율 정보
        public double incMultiplier;        // 증가(Inc) 배율 (1 + 증가합)
        public double moreMultiplier;       // 증폭(More) 배율 (1 * 증폭곱)

        // 치명타 정보
        public double critChance;           // 치명타 확률 (0.0 ~ 1.0)
        public double critMultiplier;       // 치명타 배율 (예: 1.5 = 150%)
        public double critBlowChance;       // 치명타 일격 확률 (0.0 ~ 1.0)
        public double critBlowMultiplier;   // 치명타 일격 배율 (예: 1.3 = 130%)
        public double avgCritMultiplier;    // 평균 치명타 배율 (DPS 계산용)

        // 시전 속도 정보 (DPS 계산용)
        public double castSpeed;            // 시전 속도 (회/초)

        // Inc/More 브레이크다운 정보 (UI 표시용)
        public Dictionary<EMod, double> incModContributions = new Dictionary<EMod, double>();
        public Dictionary<EMod, double> moreModContributions = new Dictionary<EMod, double>();

        public AilmentDetailInfo()
        {
            maxStacks = 1;
            duration = 0;
            damagePercent = 0;
            procChance = 0;
            procCount = 0;
            flatDamage = 0;
            dps = 0;
            maxStackDps = 0;
            incMultiplier = 1.0;
            moreMultiplier = 1.0;
            critChance = 0;
            critMultiplier = 1.0;
            critBlowChance = 0;
            critBlowMultiplier = 1.0;
            avgCritMultiplier = 1.0;
            castSpeed = 1.0;
            incModContributions = new Dictionary<EMod, double>();
            moreModContributions = new Dictionary<EMod, double>();
        }
    }

    public class SimulatorDamageStats
    {
        // 반복 설정
        public int totalIterations;

        // 히트/회피/블록/면역 통계
        public int hitCount;
        public int evadeCount;
        public int blockCount;
        public int immuneCount;

        // 크리티컬 통계
        public int criticalCount;
        public int fatalBlowCount;

        // 데미지 통계
        public double totalDamage;
        public double minDamage;
        public double maxDamage;
        public double averageDamage;

        // 확률 통계
        public double hitRate;
        public double evadeRate;
        public double blockRate;
        public double immuneRate;
        public double criticalRate;

        // DPS 계산
        public double castSpeed;
        public double realDPS;

        // Phase 10: Ailment 통계 (기댓값 기반 - 소수점 지원)
        // 예: 30% 확률 = 0.3회 기댓값
        public double ailmentBleedingCount;
        public double ailmentIgniteCount;
        public double ailmentArcticCount;
        public double ailmentChillCount;
        public double ailmentShockCount;
        public double ailmentParalyzeCount;
        public double ailmentPoisoningCount;
        public double ailmentStunCount;

        public double ailmentBleedingDamage;
        public double ailmentIgniteDamage;
        public double ailmentArcticDamage;
        public double ailmentChillDamage;
        public double ailmentShockDamage;
        public double ailmentParalyzeDamage;
        public double ailmentPoisoningDamage;
        public double ailmentStunDamage;

        // Phase 10: Ailment 상세 정보
        public List<AilmentDetailInfo> ailmentDetails = new List<AilmentDetailInfo>();

        // Phase 11: Aura 상세 정보
        public List<DotBuffDetailInfo> dotBuffDetails = new List<DotBuffDetailInfo>();

        // Phase 12: Debuff 상세 정보 (Curse로 인한 디버프)
        public List<DebuffDetailInfo> debuffDetails = new List<DebuffDetailInfo>();
    }

    /// <summary>
    /// Aura 버프 상세 정보 (skill_contagion 등)
    /// </summary>
    public class DotBuffDetailInfo
    {
        public EStatusEffect dotBuffType;           // Aura 버프 타입
        public string dotBuffName;          // Aura 버프 이름 (한글)

        // 기본 정보
        public float duration;              // 지속 시간 (초)
        public float damagePercent;         // Aura 피해 % (Effect 값)
        public float tickInterval;          // 틱 간격 (초)

        // 데미지 정보
        public double baseDamage;           // 기본 피해 (스킬 피해 기반)
        public double dps;                  // 초당 피해 (Inc/More 적용)

        // 배율 정보
        public double incMultiplier;        // 증가(Inc) 배율
        public double moreMultiplier;       // 증폭(More) 배율

        // 치명타 정보
        public double critChance;           // 치명타 확률 (0.0 ~ 1.0)
        public double critMultiplier;       // 치명타 배율 (예: 1.5 = 150%)
        public double critBlowChance;       // 치명타 일격 확률 (0.0 ~ 1.0)
        public double critBlowMultiplier;   // 치명타 일격 배율 (예: 1.3 = 130%)
        public double avgCritMultiplier;    // 평균 치명타 배율 (DPS 계산용)

        public DotBuffDetailInfo()
        {
            duration = 0;
            damagePercent = 0;
            tickInterval = 1.0f;
            baseDamage = 0;
            dps = 0;
            incMultiplier = 1.0;
            moreMultiplier = 1.0;
            critChance = 0;
            critMultiplier = 1.0;
            critBlowChance = 0;
            critBlowMultiplier = 1.0;
            avgCritMultiplier = 1.0;
        }
    }

    /// <summary>
    /// Debuff 상세 정보 (Curse 스킬로 인한 디버프)
    /// </summary>
    public class DebuffDetailInfo
    {
        public EStatusEffect debuffType;            // Debuff 타입 (debuff_weak_physical 등)
        public string debuffName;           // Debuff 이름 (한글)
        public ESkill curseSkill;           // 발동시킨 Curse 스킬
        public string curseSkillName;       // Curse 스킬 이름

        // 기본 정보
        public float duration;              // 지속 시간 (초)
        public int tierLevel;               // 스킬 티어 레벨

        // MOD 정보
        public Dictionary<EMod, double> modValues = new Dictionary<EMod, double>();  // 적용된 MOD들과 값

        public DebuffDetailInfo()
        {
            debuffType = EStatusEffect.None;
            debuffName = string.Empty;
            curseSkill = ESkill.None;
            curseSkillName = string.Empty;
            duration = 0;
            tierLevel = 0;
        }
    }
}
