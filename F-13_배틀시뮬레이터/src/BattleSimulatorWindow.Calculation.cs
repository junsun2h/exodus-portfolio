using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Unity.EditorCoroutines.Editor;
using Sirenix.OdinInspector;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// BattleSimulatorWindow의 계산 로직 및 Private 메서드
    /// </summary>
    public partial class BattleSimulatorWindow
    {
        #region Private Methods

        private Color GetDPSColor()
        {
            // 항상 초록색 반환
            return Color.green;
        }

        private IEnumerator SimulationRoutine()
        {

            // 1. 에뮬레이터 연결 확인
            if (!IsEmulatorConnected())
            {
                Debug.LogError("[Simulator] ❌ 시뮬레이션 중단: Firebase Emulator가 연결되지 않았습니다.");
                yield break;
            }

            // 2. 장비 장착
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                SimulatorAPIController.EquipAllGearRoutine(
                    weapon, helmet, bodyArmor, gloves, boots,
                    amulet, belt, ring1
                )
            );

            // 2-1. Mythic 장비 장착
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                SimulatorAPIController.EquipAllMythicGearRoutine(
                    weaponMythic, helmetMythic, bodyArmorMythic, glovesMythic, bootsMythic,
                    amuletMythic, beltMythic, ring1Mythic
                )
            );

            // 3. 스킬 장착 (룬 소켓 포함)
            var runeSockets = ConvertRuneSocketsToDict();
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                SimulatorAPIController.EquipAllSkillsRoutine(
                    mainSpell, spellTier, spellGrade, aura, pet, petTier, selectedPreset, runeSockets
                )
            );

            // 3-1. 성좌 계약 (API 호출) - 3슬롯
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                SimulatorAPIController.ContractConstellationsRoutine(
                    constellationSlot1, constellationSlot2, constellationSlot3)
            );

            // 4. userData 갱신 대기
            yield return new WaitForSeconds(0.5f);

            // 4-1. BattleStatus 명시적 갱신
            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player != null && player.CharacterStatus != null)
            {
                // UpdateBattleStatus 전에 MOD 데이터 클리어 (이전 시뮬레이션 잔여 값 방지)
                var battleStatusType = typeof(FBattleStatus);
                var allModDataField = battleStatusType.GetField("_allBattleModData", BindingFlags.NonPublic | BindingFlags.Instance);
                var allModSkillDataField = battleStatusType.GetField("_allBattleModSkillData", BindingFlags.NonPublic | BindingFlags.Instance);
                var allCombineModDataField = battleStatusType.GetField("_allCombineModData", BindingFlags.NonPublic | BindingFlags.Instance);

                var battleStatus = player.CharacterStatus.BattleStatus;

                if (allModDataField != null)
                {
                    var modData = allModDataField.GetValue(battleStatus);
                    modData?.GetType().GetMethod("ClearData")?.Invoke(modData, null);
                }
                if (allModSkillDataField != null)
                {
                    var modSkillData = allModSkillDataField.GetValue(battleStatus);
                    modSkillData?.GetType().GetMethod("ClearData")?.Invoke(modSkillData, null);
                }
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
                    var buffDataValue = buffDataProp.GetValue(battleStatus);
                    buffDataValue?.GetType().GetMethod("UpdateStatus")?.Invoke(buffDataValue, null);
                }

                player.CharacterStatus.BattleStatus.UpdateBattleStatus(player.CharacterStatus.BasicStatus);

                // 조건부 MOD 테스트를 위한 HP/마나 설정
                double maxHp = battleStatus.resultHpMax.Value;
                double maxMp = battleStatus.resultMpMax.Value;

                double targetHp = maxHp * (currentHpPercent / 100.0);
                double targetMp = maxMp * (currentManaPercent / 100.0);

                battleStatus.UpdateHP(targetHp);
                battleStatus.UpdateMP(targetMp);

                // 버프 자동 활성화 (MOD 기반) - 반드시 ApplyBuffModifiers 전에 호출!
                // AutoEnableBuffsFromMods()는 장착된 장비/스킬의 MOD를 기반으로
                // buff_xxx_enabled 상태를 재설정하므로, 버프 적용 전에 호출해야
                // 회차마다 일관된 버프 상태로 시작할 수 있음
                AutoEnableBuffsFromMods(battleStatus);

                // 버프 MOD 적용 (위에서 갱신된 버프 상태 기반)
                ApplyBuffModifiers(battleStatus);

                // CombineMod 적용 (AutoEnableBuffsFromMods는 위에서 이미 호출됨)
                ApplyCombineMods(battleStatus);

                // 버프 및 CombineMod 적용 후 _allBattleModData 업데이트
                // 이를 통해 buffData의 MOD가 피해 계산에 반영됨
                battleStatus.UpdateBattleStatus(player.CharacterStatus.BasicStatus);

                // HP/마나 재설정 (UpdateBattleStatus가 HP/MP를 초기화하므로)
                battleStatus.UpdateHP(targetHp);
                battleStatus.UpdateMP(targetMp);

                // CombineMod 효과 시뮬레이션 (CheckCombineMod_CheckState 로직 재현)
                SimulateCombineModEffects(battleStatus);

                // CombineMod 최종 상태 계산
                CalculateCombineModFinalStates(battleStatus);

                // 버프 최종 상태 계산
                CalculateBuffFinalStates(battleStatus);

            }
            else
            {
                Debug.LogError("[Simulator] ⚠️ 플레이어를 찾을 수 없어 BattleStatus를 갱신하지 못했습니다.");
            }

            // 5. DPS 계산
            CalculateResults();
        }

        /// <summary>
        /// 버프 MOD 적용 (GameDB 기반)
        /// </summary>
        private void ApplyBuffModifiers(FBattleStatus battleStatus)
        {
            if (battleStatus == null)
                return;

            var buffDB = GameDBClientManager.Instance?.GameDB_Skill?.Buff;
            if (buffDB == null || buffDB.MapData == null)
            {
                Debug.LogError("[Simulator] GameDB_Skill.Buff를 로드할 수 없습니다.");
                return;
            }

            // 1. Killing Spree (전투광)
            if (buff_killingspree_enabled)
            {
                ApplyBuffModifier_Charge(battleStatus, buffDB, EStatusEffect.buff_killingspree,
                    buff_killingspree_charges, buff_killingspree_min, buff_killingspree_max,
                    EMod.mod_buff_killingspree_cannot_gain, EMod.mod_buff_killingspree_min, EMod.mod_buff_killingspree_max,
                    "🔥 전투광");
            }

            // 2. Backstab (기습)
            if (buff_backstab_enabled)
            {
                ApplyBuffModifier_Charge(battleStatus, buffDB, EStatusEffect.buff_backstab,
                    buff_backstab_charges, buff_backstab_min, buff_backstab_max,
                    EMod.mod_buff_backstab_cannot_gain, EMod.mod_buff_backstab_min, EMod.mod_buff_backstab_max,
                    "🗡️ 기습");
            }

            // 3. Ignore Pain (고통감내)
            if (buff_ignorepain_enabled)
            {
                ApplyBuffModifier_Charge(battleStatus, buffDB, EStatusEffect.buff_ignorepain,
                    buff_ignorepain_charges, buff_ignorepain_min, buff_ignorepain_max,
                    EMod.mod_buff_ignorepain_cannot_gain, EMod.mod_buff_ignorepain_min, EMod.mod_buff_ignorepain_max,
                    "🛡️ 고통감내");
            }

            // 4. Frenzy (격노)
            if (buff_frenzy_enabled)
            {
                ApplyBuffModifier_Stack(battleStatus, buffDB, EStatusEffect.buff_frenzy,
                    buff_frenzy_stacks, buff_frenzy_min, buff_frenzy_max,
                    EMod.mod_buff_frenzy_min, EMod.mod_buff_frenzy_max,
                    "⚡ 격노");
            }

            // 5. Surge of Power (마력집중) - bufftag_highest
            if (buff_surgeofpower_enabled)
            {
                ApplyBuffModifier_Highest(battleStatus, buffDB, EStatusEffect.buff_surgeofpower,
                    buff_surgeofpower_effect, EMod.mod_buff_surgeofpower_effect,
                    "✨ 마력집중");
            }

            // 6. Invisibility (투명화)
            if (buff_invisibility_enabled)
            {
                ApplyBuffModifier_Highest(battleStatus, buffDB, EStatusEffect.buff_invisibility,
                    100.0, EMod.None,
                    "👻 투명화");
            }
        }

        /// <summary>
        /// 충전형 버프 MOD 적용 (Charge 타입)
        /// bufftag_charge: GameDB Stack 필드를 기본 최대 충전 개수로 사용
        /// </summary>
        private void ApplyBuffModifier_Charge(FBattleStatus battleStatus, GameDB_Client_BuffMap buffDB,
            EStatusEffect buffType, int userCharges, int userMin, int userMax,
            EMod cannotGainMod, EMod minMod, EMod maxMod, string debugName)
        {
            // cannot_gain 체크
            if (cannotGainMod != EMod.None && battleStatus.TotalModValue(cannotGainMod) > 0)
            {
                return;
            }

            // GameDB에서 버프 데이터 읽기
            if (!buffDB.MapData.TryGetValue(buffType, out GameDB_Client_Buff buffData))
            {
                Debug.LogError($"[Simulator] GameDB에서 {buffType} 데이터를 찾을 수 없습니다.");
                return;
            }

            // Tag 확인 (bufftag_charge여야 함)
            if (buffData.Tag != EStatusEffectTag.bufftag_charge)
            {
                Debug.LogError($"[Simulator] {debugName}: Charge 메서드를 호출했지만 Tag가 {buffData.Tag}입니다.");
            }

            // bufftag_charge는 GameDB Stack 필드를 기본 최대 충전 개수로 사용
            int gameDBMaxCharge = buffData.Stack.Value;

            // min/max 적용
            int actualMin = userMin + (int)battleStatus.TotalModValue(minMod);
            int actualMax = userMax + (int)battleStatus.TotalModValue(maxMod);

            // userMax가 0이거나 GameDB보다 크면 GameDB 값을 기본으로
            if (userMax == 0 || userMax > gameDBMaxCharge)
            {
                actualMax = gameDBMaxCharge + (int)battleStatus.TotalModValue(maxMod);
            }

            int charges = UnityEngine.Mathf.Clamp(userCharges, actualMin, actualMax);

            if (charges > 0)
            {
                ApplyBuffEffects(battleStatus, buffData, charges, debugName);
            }
        }

        /// <summary>
        /// 중첩형 버프 MOD 적용 (Stack 타입)
        /// bufftag_stack: GameDB의 Stack 필드를 최대 중첩 수로 사용
        /// </summary>
        private void ApplyBuffModifier_Stack(FBattleStatus battleStatus, GameDB_Client_BuffMap buffDB,
            EStatusEffect buffType, int userStacks, int userMin, int userMax,
            EMod minMod, EMod maxMod, string debugName)
        {
            // GameDB에서 버프 데이터 읽기
            if (!buffDB.MapData.TryGetValue(buffType, out GameDB_Client_Buff buffData))
            {
                Debug.LogError($"[Simulator] GameDB에서 {buffType} 데이터를 찾을 수 없습니다.");
                return;
            }

            // Tag 확인 (bufftag_stack이어야 함)
            if (buffData.Tag != EStatusEffectTag.bufftag_stack)
            {
                Debug.LogError($"[Simulator] {debugName}: Stack 메서드를 호출했지만 Tag가 {buffData.Tag}입니다.");
            }

            // bufftag_stack은 GameDB의 Stack 필드를 기본 최대값으로 사용
            int gameDBMaxStack = buffData.Stack.Value;

            // min/max 적용 (GameDB Stack 기준)
            int actualMin = userMin + (int)battleStatus.TotalModValue(minMod);
            int actualMax = userMax + (int)battleStatus.TotalModValue(maxMod);

            // userMax가 0이면 GameDB Stack을 기본값으로
            if (userMax == 0 || userMax > gameDBMaxStack)
            {
                actualMax = gameDBMaxStack + (int)battleStatus.TotalModValue(maxMod);
            }

            int stacks = UnityEngine.Mathf.Clamp(userStacks, actualMin, actualMax);

            if (stacks > 0)
            {
                ApplyBuffEffects(battleStatus, buffData, stacks, debugName);
            }
        }

        /// <summary>
        /// 최고 효과형 버프 MOD 적용 (Highest 타입)
        /// 가장 강한 효과 1개만 적용, stackOrCharge를 1로 간주
        /// </summary>
        private void ApplyBuffModifier_Highest(FBattleStatus battleStatus, GameDB_Client_BuffMap buffDB,
            EStatusEffect buffType, double userEffectMultiplier, EMod effectMod, string debugName)
        {
            // GameDB에서 버프 데이터 읽기
            if (!buffDB.MapData.TryGetValue(buffType, out GameDB_Client_Buff buffData))
            {
                Debug.LogError($"[Simulator] GameDB에서 {buffType} 데이터를 찾을 수 없습니다.");
                return;
            }

            // Highest 타입은 Tag 확인
            if (buffData.Tag != EStatusEffectTag.bufftag_highest)
            {
            }

            // Highest 타입은 가장 강한 효과 1개만 적용 (stackOrCharge를 1로 간주)
            // userEffectMultiplier와 effectMod는 현재 시뮬레이터에서만 사용하는 UI 편의 기능
            ApplyBuffEffects(battleStatus, buffData, 1, debugName);
        }

        /// <summary>
        /// 버프 효과 적용 (공통 로직) - Tag 기반
        /// 버프는 Effect 값이 0이므로 실제로는 곱해도 영향 없음
        /// debuff/ailment는 Effect를 사용할 수 있으므로 코드 구조는 유지
        /// 계산: (stackOrCharge / Per) * Effect * BuffMod.Value (Effect가 0이면 1로 간주)
        /// </summary>
        private void ApplyBuffEffects(FBattleStatus battleStatus, GameDB_Client_Buff buffData, int stackOrCharge, string debugName)
        {
            if (buffData.BuffMod == null || buffData.BuffMod.Count == 0)
            {
                Debug.LogError($"[Simulator] {debugName}: BuffMod가 정의되지 않았습니다.");
                return;
            }

            int per = buffData.Per.Value;
            EStatusEffectTag tag = buffData.Tag;

            // Effect/EffectMax는 코드에 유지 (debuff/ailment가 사용할 수 있음)
            // 버프의 경우 이 값이 0이므로 1로 간주
            double effectMultiplier = (double)buffData.Effect.Value;
            if (effectMultiplier == 0)
                effectMultiplier = 1.0; // 버프는 Effect가 0이므로 1로 간주

            double effectMax = (double)buffData.EffectMax.Value;

            System.Text.StringBuilder logBuilder = new System.Text.StringBuilder();
            logBuilder.Append($"[Simulator] {debugName} (Tag:{tag})");

            // Tag별 로그
            switch (tag)
            {
                case EStatusEffectTag.bufftag_charge:
                    logBuilder.Append($" {stackOrCharge}충전 적용: ");
                    break;
                case EStatusEffectTag.bufftag_stack:
                    logBuilder.Append($" {stackOrCharge}중첩 적용: ");
                    break;
                case EStatusEffectTag.bufftag_highest:
                    logBuilder.Append(" (가장 강한 효과만) 적용: ");
                    break;
                default:
                    logBuilder.Append($" 적용: ");
                    break;
            }

            // 각 BuffMod마다 개별 계산
            for (int i = 0; i < buffData.BuffMod.Count; i++)
            {
                var modData = buffData.BuffMod[i];
                double baseModValue = modData.Value.GetValue; // 위키의 5, 30 등
                double finalValue = 0;

                // 계산: (현재수량 / Per) * Effect * BuffMod.Value
                // 버프는 Effect=0이므로 effectMultiplier=1로 처리됨
                switch (tag)
                {
                    case EStatusEffectTag.bufftag_charge:
                    case EStatusEffectTag.bufftag_stack:
                        // (현재수량 / Per) * effectMultiplier * baseModValue
                        finalValue = (stackOrCharge / (double)per) * effectMultiplier * baseModValue;
                        break;

                    case EStatusEffectTag.bufftag_highest:
                        // Stack은 항상 1, 가장 강한 효과만
                        finalValue = (1 / (double)per) * effectMultiplier * baseModValue;
                        break;

                    default:
                        Debug.LogError($"[Simulator] {debugName}: 미정의 Tag {tag}");
                        finalValue = baseModValue;
                        break;
                }

                // EffectMax 제한 (버프는 0이므로 적용 안 됨, debuff/ailment용)
                if (effectMax > 0)
                {
                    finalValue = UnityEngine.Mathf.Min((float)finalValue, (float)effectMax);
                }

                // BattleStatus에 적용
                battleStatus.buffData.AddModValue(modData.Mod, CryptoValueDouble.Create(finalValue));

                // 소스 추적 정보 추가
                if (battleStatus.buffData.ModSources != null && finalValue != 0)
                {
                    string sourceDetail = "";
                    switch (tag)
                    {
                        case EStatusEffectTag.bufftag_charge:
                            sourceDetail = $"{stackOrCharge}충전";
                            break;
                        case EStatusEffectTag.bufftag_stack:
                            sourceDetail = $"{stackOrCharge}중첩";
                            break;
                        default:
                            sourceDetail = $"{stackOrCharge}";
                            break;
                    }

                    battleStatus.buffData.ModSources.AddSource(
                        modData.Mod,
                        EModSourceType.Buff,
                        finalValue,
                        debugName,
                        sourceDetail
                    );
                }

                // 로그
                if (i > 0) logBuilder.Append(", ");
                logBuilder.Append($"{modData.Mod} +{finalValue:F1}");
            }
        }

        /// <summary>
        /// 버프 충전 관련 MOD를 가지고 있으면 자동으로 버프 활성화 및 최대 충전 설정
        /// 최대 DPS 계산을 위해 버프를 최적 상태로 자동 설정
        /// </summary>
        private void AutoEnableBuffsFromMods(FBattleStatus battleStatus)
        {
            if (battleStatus == null)
                return;

            // 0. 모든 버프를 먼저 비활성화 (자동 활성화만 작동하도록)
            buff_killingspree_enabled = false;
            buff_backstab_enabled = false;
            buff_ignorepain_enabled = false;
            buff_frenzy_enabled = false;
            buff_surgeofpower_enabled = false;
            buff_invisibility_enabled = false;

            // 버프별 활성화 여부 추적
            HashSet<EStatusEffect> buffsToEnable = new HashSet<EStatusEffect>();

            // 1. EMod에서 버프 충전 관련 모드 검색
            var modSources = battleStatus.characterData?.ModSources;
            if (modSources != null)
            {
                foreach (var modType in modSources.GetAllModTypes())
                {
                    EMod eMod = (EMod)modType;
                    string modName = eMod.ToString();

                    // 활성화 조건:
                    // - mod_buff_xxx_chance_to_gain: 충전 획득
                    // - mod_buff_xxx_min: 최소 충전 (항상 활성화)
                    // 주의: mod_buff_xxx_cannot_gain은 비활성화 조건이므로 제외
                    // 주의: mod_buff_xxx_max는 활성화 조건이 아님 (최대값 증가만)
                    if (modName.StartsWith("mod_buff_") &&
                        (modName.Contains("_chance_to_gain") || modName.Contains("_min")))
                    {
                        // cannot_gain은 제외
                        if (modName.Contains("_cannot_gain"))
                        {
                            continue;
                        }

                        // 버프 이름 추출 (예: mod_buff_killingspree_chance_to_gain → killingspree)
                        string buffName = ExtractBuffNameFromMod(modName);
                        EStatusEffect buff = ConvertStringToBuff(buffName);

                        if (buff != EStatusEffect.None)
                        {
                            buffsToEnable.Add(buff);
                        }
                    }
                }
            }

            // 2. CombineMod에서 버프 충전 관련 모드 검색
            var combineModDB = GameDBClientManager.Instance?.GameDB_Mod?.CombineMod;
            if (combineModDB != null && combineModDB.MapData != null)
            {
                // 장비 CombineMod 검색
                var equipmentCoreData = GameAPIUserManager.Instance?.userData?.equipmentData?.CoreData;
                if (equipmentCoreData != null)
                {
                    HashSet<ECombineMod> allCombineMods = new HashSet<ECombineMod>();

                    // 일반/전설 장비
                    foreach (var equipment in equipmentCoreData.NormalEquipments.Values)
                    {
                        if (equipment.IsEquipped && equipment.MythicCombineModValues != null)
                        {
                            foreach (var kvp in equipment.MythicCombineModValues)
                            {
                                if (kvp.Value.GetValue != 0)
                                    allCombineMods.Add(kvp.Key);
                            }
                        }
                    }

                    // 신화 장비
                    foreach (var equipment in equipmentCoreData.MythicEquipments.Values)
                    {
                        if (equipment.IsEquipped && equipment.MythicCombineModValues != null)
                        {
                            foreach (var kvp in equipment.MythicCombineModValues)
                            {
                                if (kvp.Value.GetValue != 0)
                                    allCombineMods.Add(kvp.Key);
                            }
                        }
                    }

                    // 수동 입력 CombineMod
                    if (manualCombineMods != null)
                    {
                        foreach (var entry in manualCombineMods)
                        {
                            if (entry.value != 0)
                                allCombineMods.Add(entry.combineModType);
                        }
                    }

                    // 활성화 조건:
                    // - charge_gain 타입: 조건부 충전 획득 (예: when_enemy_kill)
                    // 주의: charge_no 타입은 "충전이 없을 때" 효과이므로 활성화 조건이 아님
                    // 주의: charge_per, charge_max 타입은 충전당 효과/최대 효과이므로 활성화 조건이 아님
                    foreach (var combineModType in allCombineMods)
                    {
                        if (combineModDB.MapData.TryGetValue(combineModType, out var combineModData))
                        {
                            if (combineModData.ChargeType == ECombineMod_ChargeType.charge_gain)
                            {
                                EStatusEffect buff = combineModData.Buff;
                                if (buff != EStatusEffect.None)
                                {
                                    buffsToEnable.Add(buff);
                                }
                            }
                        }
                    }
                }
            }

            // 3. 버프 자동 활성화 및 최대 충전 설정
            if (buffsToEnable.Count == 0)
            {
                return;
            }

            // GameDB에서 버프 정보 가져오기
            var buffDB = GameDBClientManager.Instance?.GameDB_Skill?.Buff;
            if (buffDB == null || buffDB.MapData == null)
            {
                Debug.LogError("[Simulator] GameDB_Skill.Buff를 로드할 수 없어 버프 자동 활성화를 건너뜁니다.");
                return;
            }

            foreach (var buff in buffsToEnable)
            {
                // GameDB에서 기본 최대값 가져오기
                if (!buffDB.MapData.TryGetValue(buff, out var buffData))
                {
                    Debug.LogError($"[Simulator] GameDB에서 {buff}를 찾을 수 없습니다.");
                    continue;
                }

                int baseMaxStack = buffData.Stack.Value; // GameDB 기본 최대값
                EMod maxStackMod = GetBuffMaxStackMod(buff); // mod_buff_xxx_max
                double modMaxStack = battleStatus.TotalModValue(maxStackMod); // MOD 추가값
                int finalMaxStack = baseMaxStack + (int)modMaxStack; // 최종 최대 스택

                switch (buff)
                {
                    case EStatusEffect.buff_killingspree:
                        buff_killingspree_enabled = true;
                        buff_killingspree_charges = finalMaxStack;
                        break;

                    case EStatusEffect.buff_backstab:
                        buff_backstab_enabled = true;
                        buff_backstab_charges = finalMaxStack;
                        break;

                    case EStatusEffect.buff_ignorepain:
                        buff_ignorepain_enabled = true;
                        buff_ignorepain_charges = finalMaxStack;
                        break;

                    case EStatusEffect.buff_frenzy:
                        buff_frenzy_enabled = true;
                        buff_frenzy_stacks = finalMaxStack;
                        break;

                    case EStatusEffect.buff_surgeofpower:
                        buff_surgeofpower_enabled = true;
                        break;

                    case EStatusEffect.buff_invisibility:
                        buff_invisibility_enabled = true;
                        break;
                }
            }
        }

        /// <summary>
        /// MOD 이름에서 버프 이름 추출 (예: mod_buff_killingspree_chance_to_gain → killingspree)
        /// </summary>
        private string ExtractBuffNameFromMod(string modName)
        {
            if (!modName.StartsWith("mod_buff_"))
                return "";

            string withoutPrefix = modName.Substring("mod_buff_".Length);

            // _chance_to_gain, _max, _min, _duration_inc 등 제거
            string[] suffixes = { "_chance_to_gain", "_cannot_gain", "_max", "_min", "_duration_inc", "_effect" };
            foreach (var suffix in suffixes)
            {
                if (withoutPrefix.EndsWith(suffix))
                {
                    return withoutPrefix.Substring(0, withoutPrefix.Length - suffix.Length);
                }
            }

            return withoutPrefix;
        }

        /// <summary>
        /// 문자열을 EStatusEffect로 변환
        /// </summary>
        private EStatusEffect ConvertStringToBuff(string buffName)
        {
            string enumName = "buff_" + buffName;
            if (System.Enum.TryParse<EStatusEffect>(enumName, out EStatusEffect result))
            {
                return result;
            }
            return EStatusEffect.None;
        }

        /// <summary>
        /// CombineMod 적용 (GameDB 기반)
        /// 시뮬레이터 UI에서 선택된 CombineMod를 AllCombineModData에 등록
        /// </summary>
        private void ApplyCombineMods(FBattleStatus battleStatus)
        {
            if (battleStatus == null)
                return;

            var combineModDB = GameDBClientManager.Instance?.GameDB_Mod?.CombineMod;
            if (combineModDB == null || combineModDB.MapData == null)
            {
                Debug.LogError("[Simulator] GameDB_Mod.CombineMod를 로드할 수 없습니다.");
                return;
            }

            var allCombineModData = battleStatus.AllCombineModData;

            // AllCombineModData 초기화 (기존 데이터 클리어)
            allCombineModData.CombineModDBDic.Clear();

            // 참고: AutoEnableBuffsFromMods()는 SimulationRoutine()에서
            // ApplyBuffModifiers() 전에 먼저 호출됨 (회차별 일관성 보장)

            // 1. 장비에서 제공하는 CombineMod 자동 수집
            CollectEquipmentCombineMods(allCombineModData, combineModDB);

            // 2. 수동 입력 CombineMod 처리
            if (manualCombineMods != null && manualCombineMods.Count > 0)
            {
                foreach (var entry in manualCombineMods)
                {
                    if (entry.value == 0)
                        continue;

                    RegisterCombineMod(allCombineModData, combineModDB, entry.combineModType, entry.value, entry.sourceName);
                }
            }
        }

        /// <summary>
        /// 장착된 장비에서 CombineMod 수집 (인게임 로직의 TODO 구현)
        /// </summary>
        private void CollectEquipmentCombineMods(FBattleCombineModData allCombineModData, GameDB_Client_CombineModMap combineModDB)
        {
            if (GameAPIUserManager.Instance?.userData?.equipmentData?.CoreData == null)
                return;

            var equipmentCoreData = GameAPIUserManager.Instance.userData.equipmentData.CoreData;

            // 일반/전설 장비 CombineMod 수집
            if (equipmentCoreData.NormalEquipments != null)
            {
                foreach (var equipment in equipmentCoreData.NormalEquipments)
                {
                    // 장착된 장비만 처리
                    if (equipment.Value.IsEquipped && equipment.Value.MythicCombineModValues != null)
                    {
                        string equipName = GetEquipmentName(equipment.Key);
                        foreach (var combineModEntry in equipment.Value.MythicCombineModValues)
                        {
                            ECombineMod combineModType = combineModEntry.Key;
                            double value = combineModEntry.Value.GetValue;

                            if (value != 0)
                            {
                                RegisterCombineMod(allCombineModData, combineModDB, combineModType, value, equipName);
                            }
                        }
                    }
                }
            }

            // 신화 장비 CombineMod 수집
            if (equipmentCoreData.MythicEquipments != null)
            {
                foreach (var equipment in equipmentCoreData.MythicEquipments)
                {
                    // 장착된 장비만 처리
                    if (equipment.Value.IsEquipped && equipment.Value.MythicCombineModValues != null)
                    {
                        string equipName = GetEquipmentName(equipment.Key);
                        foreach (var combineModEntry in equipment.Value.MythicCombineModValues)
                        {
                            ECombineMod combineModType = combineModEntry.Key;
                            double value = combineModEntry.Value.GetValue;

                            if (value != 0)
                            {
                                RegisterCombineMod(allCombineModData, combineModDB, combineModType, value, equipName);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 장비 이름 가져오기 (일반/전설 장비)
        /// </summary>
        private string GetEquipmentName(EEquipmentNormal equipment)
        {
            if (equipment == EEquipmentNormal.None)
                return "없음";

            // Enum 이름에서 장비 타입 추출 (예: equipment_weapon_sword_normal_tier5 → weapon sword)
            string name = equipment.ToString();
            name = name.Replace("equipment_", "").Replace("_normal", "").Replace("_tier", " T");
            return name;
        }

        /// <summary>
        /// 장비 이름 가져오기 (신화 장비)
        /// </summary>
        private string GetEquipmentName(EEquipmentMythic equipment)
        {
            if (equipment == EEquipmentMythic.None)
                return "없음";

            // Enum 이름에서 장비 이름 추출
            string name = equipment.ToString();
            name = name.Replace("equipment_", "").Replace("_mythic", "");
            return name;
        }

        /// <summary>
        /// CombineMod를 AllCombineModData에 등록하고 소스 추적 정보 추가
        /// </summary>
        private void RegisterCombineMod(FBattleCombineModData allCombineModData, GameDB_Client_CombineModMap combineModDB,
            ECombineMod combineMod, double value, string sourceName)
        {
            if (!combineModDB.MapData.TryGetValue(combineMod, out GameDB_Client_CombineMod combineModData))
            {
                Debug.LogError($"[Simulator] GameDB에서 {combineMod}를 찾을 수 없습니다.");
                return;
            }

            // AllCombineModData에 등록
            allCombineModData.CombineModDBDic[combineMod] = combineModData;
            allCombineModData.AddModValue(combineMod, CryptoValueDouble.Create(value));

            // 소스 추적 정보 추가 (플레이어 characterData의 ModSources에 추가)
            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player != null)
            {
                var battleStatus = player.CharacterStatus?.BattleStatus;
                if (battleStatus?.characterData?.ModSources != null)
                {
                    // IsBuff 여부에 따라 다른 소스 타입 사용
                    EModSourceType sourceType = combineModData.IsBuff.Value
                        ? EModSourceType.CombineMod_Buff
                        : EModSourceType.CombineMod_General;

                    // ECombineMod는 FModSourceCollection.AddSource에서 지원됨
                    battleStatus.characterData.ModSources.AddSource(combineMod, sourceType, value, sourceName, $"{combineMod}");
                }
            }
        }

        /// <summary>
        /// CombineMod 효과 시뮬레이션 (GameBuffManager.CheckCombineMod_CheckState 로직 재현)
        /// AllCombineModData에 등록된 CombineMod를 기반으로 최종 EMod를 AllCombineBattleModData에 추가
        /// </summary>
        private void SimulateCombineModEffects(FBattleStatus battleStatus)
        {
            if (battleStatus == null)
                return;

            var allCombineModData = battleStatus.AllCombineModData;
            var allCombineBattleModData = battleStatus.AllCombineBattleModData;

            // AllCombineBattleModData 초기화 (인게임과 동일)
            allCombineBattleModData.CombineBattleModDic.Clear();

            // AllCombineModData에 등록된 모든 CombineMod 순회
            foreach (var item in allCombineModData.CombineModDBDic)
            {
                ECombineMod eCombineMod = item.Key;
                CryptoValueDouble combineModValue = allCombineModData.GetModValue(eCombineMod);
                GameDB_Client_CombineMod combineModData = item.Value;

                if (combineModData == null)
                {
                    Debug.LogError($"[Simulator] CombineMod {eCombineMod} 데이터가 없습니다.");
                    continue;
                }

                // charge_gain 타입은 이벤트 기반이므로 시뮬레이터에서는 스킵
                if (combineModData.ChargeType == ECombineMod_ChargeType.charge_gain)
                {
                    continue;
                }

                // Condition 체크 (시뮬레이터에서는 조건 없는 것만 처리)
                if (combineModData.Condition != ECombineMod_Condition.None)
                {
                    continue;
                }

                // TargetState 체크 (시뮬레이터에서는 적 상태 체크 생략)
                // 필요 시 나중에 UI로 추가 가능

                // ChargeType별 처리 (GameBuffManager.cs Line 273-297 로직 재현)
                if (combineModData.ChargeType != ECombineMod_ChargeType.None)
                {
                    // 버프 상태 체크 (CheckCombineMod_BuffState 재현)
                    bool buffStateValid = CheckSimulatorBuffState(combineModData);
                    if (!buffStateValid)
                    {
                        continue;
                    }

                    EMod targetMod = combineModData.Mod;
                    double finalValue = 0;
                    string effectDescription = "";

                    switch (combineModData.ChargeType)
                    {
                        case ECombineMod_ChargeType.charge_no:
                        case ECombineMod_ChargeType.charge_max:
                            {
                                // 조건만 만족하면 그대로 적용
                                finalValue = combineModValue.Value;
                                effectDescription = combineModData.ChargeType == ECombineMod_ChargeType.charge_no
                                    ? "충전 없을 때"
                                    : "최대 충전 시";
                            }
                            break;

                        case ECombineMod_ChargeType.charge_per:
                            {
                                // 현재 충전 개수 × combineModValue
                                int buffCount = GetSimulatorBuffCount(combineModData.Buff);
                                if (buffCount > 0)
                                {
                                    finalValue = combineModValue.Value * buffCount;
                                    effectDescription = $"{buffCount}충전 × {combineModValue.Value:F1}";
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            break;

                        default:
                            Debug.LogError($"[Simulator] CombineMod {eCombineMod}의 ChargeType {combineModData.ChargeType}은 지원되지 않습니다.");
                            continue;
                    }

                    // AllCombineBattleModData에 최종 EMod 추가 (누적)
                    if (!allCombineBattleModData.CombineBattleModDic.ContainsKey(targetMod))
                    {
                        allCombineBattleModData.CombineBattleModDic[targetMod] = CryptoValueDouble.Create(finalValue);
                    }
                    else
                    {
                        // 이미 존재하면 기존 값에 더함
                        double currentValue = allCombineBattleModData.CombineBattleModDic[targetMod].Value;
                        allCombineBattleModData.CombineBattleModDic[targetMod] = CryptoValueDouble.Create(currentValue + finalValue);
                    }

                    // 소스 추적 정보 추가
                    if (battleStatus.characterData?.ModSources != null)
                    {
                        EModSourceType sourceType = combineModData.IsBuff.Value
                            ? EModSourceType.CombineMod_Buff
                            : EModSourceType.CombineMod_General;

                        battleStatus.characterData.ModSources.AddSource(
                            targetMod,
                            sourceType,
                            finalValue,
                            $"CombineMod: {eCombineMod}",
                            effectDescription
                        );
                    }
                }
            }
        }

        /// <summary>
        /// 시뮬레이터에서 버프 상태 체크 (CheckCombineMod_BuffState 재현)
        /// </summary>
        private bool CheckSimulatorBuffState(GameDB_Client_CombineMod combineModData)
        {
            EStatusEffect buff = combineModData.Buff;
            ECombineMod_ChargeType chargeType = combineModData.ChargeType;

            // 버프별 충전 개수 가져오기
            int buffCount = GetSimulatorBuffCount(buff);
            int maxCount = GetSimulatorBuffMaxCount(buff);

            switch (chargeType)
            {
                case ECombineMod_ChargeType.charge_max:
                    // 최대 충전 상태일 때
                    return buffCount >= maxCount && maxCount > 0;

                case ECombineMod_ChargeType.charge_per:
                    // 충전 하나당 (1개 이상 있으면 OK)
                    return buffCount > 0;

                case ECombineMod_ChargeType.charge_no:
                    // 충전이 없는 동안 (0개일 때)
                    return buffCount == 0;

                default:
                    return true;
            }
        }

        /// <summary>
        /// 시뮬레이터에서 버프 충전 개수 가져오기
        /// </summary>
        private int GetSimulatorBuffCount(EStatusEffect buff)
        {
            switch (buff)
            {
                case EStatusEffect.buff_killingspree:
                    return buff_killingspree_enabled ? buff_killingspree_charges : 0;

                case EStatusEffect.buff_backstab:
                    return buff_backstab_enabled ? buff_backstab_charges : 0;

                case EStatusEffect.buff_ignorepain:
                    return buff_ignorepain_enabled ? buff_ignorepain_charges : 0;

                case EStatusEffect.buff_frenzy:
                    return buff_frenzy_enabled ? buff_frenzy_stacks : 0;

                case EStatusEffect.buff_surgeofpower:
                    return buff_surgeofpower_enabled ? 1 : 0; // Duration 타입은 0 or 1

                case EStatusEffect.buff_invisibility:
                    return buff_invisibility_enabled ? 1 : 0; // Duration 타입은 0 or 1

                default:
                    return 0;
            }
        }

        /// <summary>
        /// 시뮬레이터에서 버프 최대 충전 개수 가져오기
        /// </summary>
        private int GetSimulatorBuffMaxCount(EStatusEffect buff)
        {
            switch (buff)
            {
                case EStatusEffect.buff_killingspree:
                    return buff_killingspree_max;

                case EStatusEffect.buff_backstab:
                    return buff_backstab_max;

                case EStatusEffect.buff_ignorepain:
                    return buff_ignorepain_max;

                case EStatusEffect.buff_frenzy:
                    return buff_frenzy_max;

                case EStatusEffect.buff_surgeofpower:
                case EStatusEffect.buff_invisibility:
                    return 1; // Duration 타입은 최대 1

                default:
                    return 0;
            }
        }

        /// <summary>
        /// CombineMod 최종 상태 계산 (출처별 합계)
        /// 결과 탭에 표시할 CombineMod 최종 상태를 계산
        /// </summary>
        private void CalculateCombineModFinalStates(FBattleStatus battleStatus)
        {
            combineModFinalStates.Clear();

            if (battleStatus == null)
                return;

            // GameDB에서 CombineMod 정보 가져오기
            var combineModDB = GameDBClientManager.Instance?.GameDB_Mod?.CombineMod;
            if (combineModDB == null || combineModDB.MapData == null)
            {
                Debug.LogError("[Simulator] GameDB_Mod.CombineMod를 로드할 수 없습니다.");
                return;
            }

            // AllCombineModData에 등록된 모든 CombineMod 순회
            var allCombineModData = battleStatus.AllCombineModData;
            if (allCombineModData?.CombineModDBDic == null)
                return;

            foreach (var combineModEntry in allCombineModData.CombineModDBDic)
            {
                ECombineMod combineModType = combineModEntry.Key;
                GameDB_Client_CombineMod combineModData = combineModEntry.Value;

                // 총합 계산
                double totalValue = allCombineModData.GetModValue(combineModType).Value;

                if (totalValue == 0)
                    continue;

                // 타입 표시 (Buff / General)
                string typeDisplay = combineModData.IsBuff.Value ? "버프 관련" : "일반";

                // ECombineMod enum 이름 그대로 사용
                string displayName = combineModType.ToString();

                // CombineMod가 실제로 적용하는 MOD 타입 (UI 포맷팅용)
                EMod targetMod = combineModData.Mod;

                // MOD 타입에 따른 EModValueType 결정
                EModValueType modValueType = EModValueType.FLOAT;
                if (targetMod != EMod.None)
                {
                    modValueType = SimulatorCalculator.IsFloatPerMod(targetMod) ? EModValueType.FLOAT_PER : EModValueType.FLOAT;
                }

                // CombineMod 최종 상태 생성 (targetMod 전달)
                var finalState = new SimulatorCombineModFinalState(
                    displayName,
                    combineModType,
                    totalValue,
                    typeDisplay,
                    targetMod
                );

                // 브레이크다운 생성 (ModSources에서 출처 추적)
                if (battleStatus.characterData?.ModSources != null)
                {
                    var sources = battleStatus.characterData.ModSources.GetSources((int)combineModType);
                    if (sources != null && sources.Count > 0)
                    {
                        // 브레이크다운 값 타입 결정 (FLOAT_PER이면 Percentage)
                        EBreakdownValueType breakdownValueType = modValueType == EModValueType.FLOAT_PER
                            ? EBreakdownValueType.Percentage
                            : EBreakdownValueType.Flat;

                        finalState.breakdown = new StageBreakdown(
                            $"{displayName} 출처",
                            $"{combineModType}의 출처별 기여도",
                            breakdownValueType
                        );

                        // 출처별로 그룹화
                        var groupedBySources = new Dictionary<string, ModContribution>();
                        foreach (var source in sources)
                        {
                            string key = source.sourceName;
                            if (!groupedBySources.ContainsKey(key))
                            {
                                groupedBySources[key] = new ModContribution(key, 0, modValueType);
                            }
                            groupedBySources[key].contribution += source.value;
                            groupedBySources[key].sources.Add(new SimulatorModSource(
                                source.sourceName,
                                combineModType.ToString(),
                                source.value,
                                source.sourceType
                            ));
                        }

                        foreach (var kvp in groupedBySources)
                        {
                            finalState.breakdown.modContributions.Add(kvp.Value);
                        }

                        finalState.breakdown.finalValue = totalValue;
                    }
                }

                combineModFinalStates.Add(finalState);
            }

            // CombineMod 이름순으로 정렬
            combineModFinalStates = combineModFinalStates.OrderBy(x => x.combineModName).ToList();
        }

        /// <summary>
        /// 버프 최종 상태 계산 (기본값 + MOD 적용)
        /// 결과 탭에 표시할 버프 최종 상태를 계산
        /// </summary>
        private void CalculateBuffFinalStates(FBattleStatus battleStatus)
        {
            buffFinalStates.Clear();

            if (battleStatus == null)
                return;

            // GameDB에서 버프 정보 가져오기
            var buffDB = GameDBClientManager.Instance?.GameDB_Skill?.Buff;
            if (buffDB == null || buffDB.MapData == null)
            {
                Debug.LogError("[Simulator] GameDB_Skill.Buff를 로드할 수 없습니다.");
                return;
            }

            // 활성화된 버프들만 처리
            EStatusEffect[] activeBuffs = new EStatusEffect[]
            {
                EStatusEffect.buff_killingspree,
                EStatusEffect.buff_backstab,
                EStatusEffect.buff_ignorepain,
                EStatusEffect.buff_frenzy,
                EStatusEffect.buff_surgeofpower,
                EStatusEffect.buff_invisibility
            };

            foreach (var buffType in activeBuffs)
            {
                // 해당 버프가 활성화되어 있는지 확인
                bool isEnabled = IsBuffEnabled(buffType);
                if (!isEnabled)
                    continue;

                // GameDB에서 기본 정보 가져오기
                if (!buffDB.MapData.TryGetValue(buffType, out var buffData))
                {
                    Debug.LogError($"[Simulator] GameDB에서 {buffType}를 찾을 수 없습니다.");
                    continue;
                }

                // 기본 최대 스택 (GameDB Stack)
                int baseMaxStack = buffData.Stack.Value;

                // MOD에 의한 추가 스택 (mod_buff_xxx_max)
                EMod maxStackMod = GetBuffMaxStackMod(buffType);
                double modMaxStack = battleStatus.TotalModValue(maxStackMod);

                // 최종 최대 스택 계산
                int finalMaxStack = baseMaxStack + (int)modMaxStack;

                // 현재 충전 수 (UI 설정값)
                int currentCharge = GetSimulatorBuffCount(buffType);

                // 기본 지속시간 (GameDB Duration, 초 단위)
                double baseDuration = buffData.Duration.Value;

                // MOD에 의한 지속시간 증가 % (mod_buff_xxx_duration_inc)
                EMod durationIncMod = GetBuffDurationIncMod(buffType);
                // TotalModValue는 이미 비율 값 반환 (0.2 = 20%)
                double modDurationInc = battleStatus.TotalModValue(durationIncMod);

                // 최종 지속시간 계산 (기본 * (1 + inc))
                double finalDuration = baseDuration * (1.0 + modDurationInc);

                // 버프 이름 가져오기
                string buffName = GetBuffDisplayName(buffType);

                // 최종 상태 객체 생성
                var buffFinalState = new SimulatorBuffFinalState(
                    buffName,
                    baseMaxStack,
                    modMaxStack,
                    finalMaxStack,
                    currentCharge,
                    baseDuration,
                    modDurationInc,
                    finalDuration
                );

                // ====================================
                // 버프가 제공하는 BuffMod 계산
                // ====================================
                if (buffData.BuffMod != null && buffData.BuffMod.Count > 0 && currentCharge > 0)
                {
                    int per = buffData.Per.Value;
                    EStatusEffectTag tag = buffData.Tag;

                    // Effect/EffectMax는 버프에서는 사용 안 함 (0이면 1로 간주)
                    double effectMultiplier = (double)buffData.Effect.Value;
                    if (effectMultiplier == 0)
                        effectMultiplier = 1.0;

                    foreach (var modData in buffData.BuffMod)
                    {
                        double baseModValue = modData.Value.GetValue; // 충전당 기본 값
                        double perChargeValue = 0;
                        double finalValue = 0;

                        // Tag에 따른 계산
                        switch (tag)
                        {
                            case EStatusEffectTag.bufftag_charge:
                            case EStatusEffectTag.bufftag_stack:
                                // 충전당 값 = (1 / Per) * effectMultiplier * baseModValue
                                perChargeValue = (1.0 / per) * effectMultiplier * baseModValue;
                                // 최종 값 = (현재수량 / Per) * effectMultiplier * baseModValue
                                finalValue = (currentCharge / (double)per) * effectMultiplier * baseModValue;
                                break;

                            case EStatusEffectTag.bufftag_highest:
                                // Stack은 항상 1, 가장 강한 효과만
                                perChargeValue = (1.0 / per) * effectMultiplier * baseModValue;
                                finalValue = perChargeValue;
                                break;

                            default:
                                perChargeValue = baseModValue;
                                finalValue = baseModValue;
                                break;
                        }

                        // BuffModInfo 추가
                        buffFinalState.buffMods.Add(new BuffModInfo(
                            modData.Mod.ToString(),
                            perChargeValue,
                            finalValue
                        ));
                    }
                }

                // ====================================
                // 최대 스택 MOD 브레이크다운 생성
                // ====================================
                if (maxStackMod != EMod.None)
                {
                    buffFinalState.maxStackBreakdown = new StageBreakdown(
                        $"{buffName} 최대 스택",
                        $"{maxStackMod} MOD의 출처별 기여도",
                        EBreakdownValueType.Flat
                    );

                    if (battleStatus.characterData?.ModSources != null)
                    {
                        var sources = battleStatus.characterData.ModSources.GetSources(maxStackMod);
                        if (sources != null && sources.Count > 0)
                        {
                            // 출처별로 그룹화
                            var groupedBySources = new Dictionary<string, ModContribution>();
                            foreach (var source in sources)
                            {
                                string key = source.sourceName;
                                if (!groupedBySources.ContainsKey(key))
                                {
                                    groupedBySources[key] = new ModContribution(key, 0, EModValueType.FLOAT);
                                }
                                groupedBySources[key].contribution += source.value;
                                groupedBySources[key].sources.Add(new SimulatorModSource(
                                    source.sourceName,
                                    maxStackMod.ToString(),
                                    source.value,
                                    source.sourceType
                                ));
                            }

                            // ModContribution 추가
                            foreach (var kvp in groupedBySources)
                            {
                                buffFinalState.maxStackBreakdown.modContributions.Add(kvp.Value);
                            }
                        }
                    }

                    buffFinalState.maxStackBreakdown.finalValue = modMaxStack;
                }

                // ====================================
                // 지속시간 증가 MOD 브레이크다운 생성
                // ====================================
                if (durationIncMod != EMod.None)
                {
                    buffFinalState.durationIncBreakdown = new StageBreakdown(
                        $"{buffName} 지속시간 증가",
                        $"{durationIncMod} MOD의 출처별 기여도",
                        EBreakdownValueType.Percentage
                    );

                    if (battleStatus.characterData?.ModSources != null)
                    {
                        var sources = battleStatus.characterData.ModSources.GetSources(durationIncMod);
                        if (sources != null && sources.Count > 0)
                        {
                            // 출처별로 그룹화
                            var groupedBySources = new Dictionary<string, ModContribution>();
                            foreach (var source in sources)
                            {
                                string key = source.sourceName;
                                if (!groupedBySources.ContainsKey(key))
                                {
                                    groupedBySources[key] = new ModContribution(key, 0, EModValueType.FLOAT_PER);
                                }
                                groupedBySources[key].contribution += source.value;
                                groupedBySources[key].sources.Add(new SimulatorModSource(
                                    source.sourceName,
                                    durationIncMod.ToString(),
                                    source.value,
                                    source.sourceType
                                ));
                            }

                            // ModContribution 추가
                            foreach (var kvp in groupedBySources)
                            {
                                buffFinalState.durationIncBreakdown.modContributions.Add(kvp.Value);
                            }
                        }
                    }

                    buffFinalState.durationIncBreakdown.finalValue = modDurationInc;
                }

                // ====================================
                // 버프 기여 MOD 전체 브레이크다운 생성
                // ====================================
                var relatedMods = GetBuffRelatedMods(buffType);
                if (relatedMods != null && relatedMods.Count > 0)
                {
                    buffFinalState.contributingMods = new StageBreakdown(
                        $"{buffName} 기여 MOD",
                        $"{buffName} 버프와 관련된 모든 MOD (획득 확률, 제한, 스택, 지속시간, 효과 등)",
                        EBreakdownValueType.Flat
                    );

                    if (battleStatus.characterData?.ModSources != null)
                    {
                        // 모든 관련 MOD를 순회하며 값이 있는 것만 수집
                        foreach (var mod in relatedMods)
                        {
                            var sources = battleStatus.characterData.ModSources.GetSources(mod);
                            if (sources != null && sources.Count > 0)
                            {
                                // 출처별로 그룹화
                                var groupedBySources = new Dictionary<string, ModContribution>();
                                foreach (var source in sources)
                                {
                                    string key = $"{mod} - {source.sourceName}";
                                    if (!groupedBySources.ContainsKey(key))
                                    {
                                        // MOD 타입에 따라 EModValueType 결정
                                        EModValueType valueType = mod.ToString().Contains("_inc") || mod.ToString().Contains("chance")
                                            ? EModValueType.FLOAT_PER
                                            : EModValueType.FLOAT;

                                        groupedBySources[key] = new ModContribution($"{mod}", 0, valueType);
                                    }
                                    groupedBySources[key].contribution += source.value;
                                    groupedBySources[key].sources.Add(new SimulatorModSource(
                                        source.sourceName,
                                        mod.ToString(),
                                        source.value,
                                        source.sourceType
                                    ));
                                }

                                // ModContribution 추가
                                foreach (var kvp in groupedBySources)
                                {
                                    buffFinalState.contributingMods.modContributions.Add(kvp.Value);
                                }
                            }
                        }
                    }

                    // 총 기여 MOD 개수를 finalValue에 저장
                    buffFinalState.contributingMods.finalValue = buffFinalState.contributingMods.modContributions.Count;
                }

                // ====================================
                // 연관 CombineMod 수집
                // ====================================
                // combineModFinalStates에서 이 버프와 관련된 CombineMod들을 찾아서 추가
                // 패턴: combinemod_buff_{buffname}_xxx
                string buffNameLowerCase = buffType.ToString().Replace("buff_", ""); // "killingspree", "backstab" 등
                foreach (var combineModState in combineModFinalStates)
                {
                    string combineModNameLower = combineModState.combineModName.ToLower();

                    // combinemod_buff_{buffname} 패턴 체크
                    if (combineModNameLower.Contains($"buff_{buffNameLowerCase}"))
                    {
                        buffFinalState.relatedCombineMods.Add(combineModState);
                    }
                }

                // 최종 상태 추가
                buffFinalStates.Add(buffFinalState);
            }
        }

        /// <summary>
        /// 버프가 활성화되어 있는지 확인
        /// </summary>
        private bool IsBuffEnabled(EStatusEffect buff)
        {
            switch (buff)
            {
                case EStatusEffect.buff_killingspree:
                    return buff_killingspree_enabled;
                case EStatusEffect.buff_backstab:
                    return buff_backstab_enabled;
                case EStatusEffect.buff_ignorepain:
                    return buff_ignorepain_enabled;
                case EStatusEffect.buff_frenzy:
                    return buff_frenzy_enabled;
                case EStatusEffect.buff_surgeofpower:
                    return buff_surgeofpower_enabled;
                case EStatusEffect.buff_invisibility:
                    return buff_invisibility_enabled;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 버프의 최대 스택 MOD 가져오기
        /// </summary>
        private EMod GetBuffMaxStackMod(EStatusEffect buff)
        {
            switch (buff)
            {
                case EStatusEffect.buff_killingspree:
                    return EMod.mod_buff_killingspree_max;
                case EStatusEffect.buff_backstab:
                    return EMod.mod_buff_backstab_max;
                case EStatusEffect.buff_ignorepain:
                    return EMod.mod_buff_ignorepain_max;
                case EStatusEffect.buff_frenzy:
                    return EMod.mod_buff_frenzy_max;
                default:
                    return EMod.None;
            }
        }

        /// <summary>
        /// 버프의 지속시간 증가 MOD 가져오기
        /// </summary>
        private EMod GetBuffDurationIncMod(EStatusEffect buff)
        {
            switch (buff)
            {
                case EStatusEffect.buff_killingspree:
                    return EMod.mod_buff_killingspree_duration_inc;
                case EStatusEffect.buff_backstab:
                    return EMod.mod_buff_backstab_duration_inc;
                case EStatusEffect.buff_ignorepain:
                    return EMod.mod_buff_ignorepain_duration_inc;
                case EStatusEffect.buff_frenzy:
                    // Frenzy는 charge 타입이므로 duration_inc MOD가 없음
                    return EMod.None;
                case EStatusEffect.buff_surgeofpower:
                    return EMod.mod_buff_surgeofpower_duration_inc;
                case EStatusEffect.buff_invisibility:
                    return EMod.mod_buff_invisibility_duration_inc;
                default:
                    return EMod.None;
            }
        }

        /// <summary>
        /// 버프 관련 모든 MOD 가져오기 (cannot_gain, chance_to_gain, duration_inc, max, min, effect 등)
        /// </summary>
        private List<EMod> GetBuffRelatedMods(EStatusEffect buff)
        {
            var mods = new List<EMod>();

            switch (buff)
            {
                case EStatusEffect.buff_killingspree:
                    mods.Add(EMod.mod_buff_killingspree_cannot_gain);
                    mods.Add(EMod.mod_buff_killingspree_chance_to_gain);
                    mods.Add(EMod.mod_buff_killingspree_duration_inc);
                    mods.Add(EMod.mod_buff_killingspree_max);
                    mods.Add(EMod.mod_buff_killingspree_min);
                    break;

                case EStatusEffect.buff_backstab:
                    mods.Add(EMod.mod_buff_backstab_cannot_gain);
                    mods.Add(EMod.mod_buff_backstab_chance_to_gain);
                    mods.Add(EMod.mod_buff_backstab_duration_inc);
                    mods.Add(EMod.mod_buff_backstab_max);
                    mods.Add(EMod.mod_buff_backstab_min);
                    break;

                case EStatusEffect.buff_ignorepain:
                    mods.Add(EMod.mod_buff_ignorepain_cannot_gain);
                    mods.Add(EMod.mod_buff_ignorepain_chance_to_gain);
                    mods.Add(EMod.mod_buff_ignorepain_duration_inc);
                    mods.Add(EMod.mod_buff_ignorepain_max);
                    mods.Add(EMod.mod_buff_ignorepain_min);
                    break;

                case EStatusEffect.buff_frenzy:
                    mods.Add(EMod.mod_buff_frenzy_chance_to_gain);
                    mods.Add(EMod.mod_buff_frenzy_max);
                    mods.Add(EMod.mod_buff_frenzy_min);
                    break;

                case EStatusEffect.buff_invisibility:
                    mods.Add(EMod.mod_buff_invisibility_chance_to_gain);
                    mods.Add(EMod.mod_buff_invisibility_duration_inc);
                    break;

                case EStatusEffect.buff_surgeofpower:
                    mods.Add(EMod.mod_buff_surgeofpower_chance_to_gain);
                    mods.Add(EMod.mod_buff_surgeofpower_duration_inc);
                    mods.Add(EMod.mod_buff_surgeofpower_effect);
                    break;
            }

            return mods;
        }

        /// <summary>
        /// 버프 표시 이름 가져오기
        /// </summary>
        private string GetBuffDisplayName(EStatusEffect buff)
        {
            // 원래 EStatusEffect enum 이름 그대로 반환
            return buff.ToString();
        }

        /// <summary>
        /// DPS 계산 및 결과 표시
        /// </summary>
        private void CalculateResults()
        {
            // Phase 8: Skill Stats 업데이트
            UpdateSkillStats();
            UpdateAuraStats();
            UpdateCurseStats();

            // Phase 10: Ailment Stats 업데이트
            UpdateAilmentStats();

            // Phase 9: Spell Damage 계산
            CalculateSkillHitDamage();

            // Phase 11: Aura Damage 메타 정보는 항상 표시
            CalculateDotDamage();

            // Phase 6.2 & 6.3: 실제 전투 계산 및 통계 (defender가 있을 때만)
            if (defender != null)
            {

                // Phase 6.3: 통계 기반 DPS 계산
                lastStats = SimulatorCalculator.CalculateStatisticalDPS(
                    mainSpell,
                    spellTier,
                    defender,
                    auraReinforceLevel,  // Aura 스킬 강화도
                    aura  // 장착된 Aura (DoT 조건 확인용)
                );

                // Phase 9: Curse & Debuff 정보 동기화 (lastStats 생성 후)
                if (lastStats != null && currentDebuffDetails.Count > 0)
                {
                    lastStats.debuffDetails.Clear();
                    lastStats.debuffDetails.AddRange(currentDebuffDetails);
                }

                // Phase 10: Ailment 상세 정보 업데이트
                activeAilmentCount = lastStats.ailmentDetails.Count;

                // Phase 10: Ailment Damage 결과 계산
                CalculateAilmentDamage();

                // Phase 11: Aura Damage DoT 정보 업데이트 (두 번째 호출)
                CalculateDotDamage();
            }
            else
            {
                defender = SimulatorDefender.CreateDefault1000FloorBoss();
            }

            // 플레이어 시스템 MOD 정보 업데이트
            UpdatePlayerInfo();
            UpdateNebulaMods();
            UpdateConstellationMods();
            UpdateAwakenMods();

            // 최종 Resource 값 업데이트
            UpdateFinalResourceValues();

            // Mana Cost 값 계산 및 표시
            CalculateManaCost();

            // Leech 값 계산 및 표시
            CalculateLeechValues();

            // Instant Kill 값 계산 및 표시는 Defense 계산 내부에서 수행됨

            // Inspector 리페인트
            Repaint();
        }

        /// <summary>
        /// Firebase Emulator 연결 상태 확인
        /// </summary>
        private bool IsEmulatorConnected()
        {
            // Play Mode 확인
            if (!Application.isPlaying)
            {
                Debug.LogError("[Simulator] ❌ Unity Play Mode에서만 시뮬레이터를 실행할 수 있습니다.");
                EditorUtility.DisplayDialog(
                    "Play Mode 필요",
                    "시뮬레이터는 Unity Play Mode에서만 실행할 수 있습니다.\n\n" +
                    "Unity Editor 상단의 Play 버튼(▶)을 눌러 Play Mode로 진입한 후 다시 시도해주세요.",
                    "확인"
                );
                return false;
            }

            // 로컬 에뮬레이터 활성화 확인
            if (!TemplateLocalData.UseLocalEmulator)
            {
                Debug.LogError("[Simulator] ❌ 로컬 에뮬레이터가 활성화되지 않았습니다.");
                EditorUtility.DisplayDialog(
                    "에뮬레이터 미활성화",
                    "Firebase 로컬 에뮬레이터가 활성화되지 않았습니다.\n\n" +
                    "Tools > Firebase Editor에서 'Use Local Emulator'를 활성화해주세요.",
                    "확인"
                );
                return false;
            }

            // Firebase 초기화 확인
            if (FBMainManager.Instance == null || FBMainManager.Instance.firebaseFirestore == null)
            {
                Debug.LogError("[Simulator] ❌ Firebase가 초기화되지 않았습니다.");
                EditorUtility.DisplayDialog(
                    "Firebase 미초기화",
                    "Firebase가 초기화되지 않았습니다.\n\n" +
                    "게임을 재시작하거나 잠시 후 다시 시도해주세요.",
                    "확인"
                );
                return false;
            }

            return true;
        }

        /// <summary>
        /// BattleStatus를 시뮬레이션 상태로 업데이트 (버프, CombineMod 포함)
        /// TheoreticalBalance 등에서 정확한 DPS 계산을 위해 사용
        /// </summary>
        public void UpdateBattleStatusForAnalysis()
        {
            UCharacterActor player = GameCharacterManager.Instance?.GetPlayerCharacter();
            if (player == null || player.CharacterStatus == null)
            {
                Debug.LogError("[Simulator] ⚠️ 플레이어를 찾을 수 없어 BattleStatus를 갱신하지 못했습니다.");
                return;
            }

            var battleStatus = player.CharacterStatus.BattleStatus;
            if (battleStatus == null)
            {
                Debug.LogError("[Simulator] ⚠️ BattleStatus가 null입니다.");
                return;
            }

            // 1. BattleStatus 초기 업데이트
            battleStatus.UpdateBattleStatus(player.CharacterStatus.BasicStatus);

            // 2. 조건부 MOD 테스트를 위한 HP/마나 설정
            double maxHp = battleStatus.resultHpMax.Value;
            double maxMp = battleStatus.resultMpMax.Value;

            double targetHp = maxHp * (currentHpPercent / 100.0);
            double targetMp = maxMp * (currentManaPercent / 100.0);

            battleStatus.UpdateHP(targetHp);
            battleStatus.UpdateMP(targetMp);

            // 3. 버프 자동 활성화 (MOD 기반) - 반드시 ApplyBuffModifiers 전에 호출!
            AutoEnableBuffsFromMods(battleStatus);

            // 4. 버프 MOD 적용 (위에서 갱신된 버프 상태 기반)
            ApplyBuffModifiers(battleStatus);

            // 5. CombineMod 적용 (AutoEnableBuffsFromMods는 위에서 이미 호출됨)
            ApplyCombineMods(battleStatus);

            // 6. 버프 및 CombineMod 적용 후 BattleStatus 재업데이트
            battleStatus.UpdateBattleStatus(player.CharacterStatus.BasicStatus);

            // 7. HP/마나 재설정 (UpdateBattleStatus가 HP/MP를 초기화하므로)
            battleStatus.UpdateHP(targetHp);
            battleStatus.UpdateMP(targetMp);

            // 8. CombineMod 효과 시뮬레이션
            SimulateCombineModEffects(battleStatus);

            // 9. CombineMod 최종 상태 계산
            CalculateCombineModFinalStates(battleStatus);

            // 10. 버프 최종 상태 계산
            CalculateBuffFinalStates(battleStatus);
        }

        #endregion
    }
}
