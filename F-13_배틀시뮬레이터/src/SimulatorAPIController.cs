using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.EditorCoroutines.Editor;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 시뮬레이터 API 호출 컨트롤러
    /// 장비/스킬 장착을 순차적으로 처리
    /// </summary>
    public static class SimulatorAPIController
    {
        #region 장비 장착

        /// <summary>
        /// 선택한 장비를 순차적으로 장착
        /// </summary>
        public static IEnumerator EquipAllGearRoutine(
            EEquipmentNormal weapon,
            EEquipmentNormal helmet,
            EEquipmentNormal bodyArmor,
            EEquipmentNormal gloves,
            EEquipmentNormal boots,
            EEquipmentNormal amulet,
            EEquipmentNormal belt,
            EEquipmentNormal ring1
        )
        {
            List<EEquipmentNormal> equipments = new List<EEquipmentNormal>
            {
                weapon, helmet, bodyArmor, gloves, boots,
                amulet, belt, ring1
            };

            int equipCount = 0;
            foreach (var equip in equipments)
            {
                if (equip == EEquipmentNormal.None)
                {
                    continue;
                }

                bool equipComplete = false;
                bool hasError = false;

                // GameAPIEquipmentManager를 통한 장비 장착 API 호출
                GameAPIEquipmentManager.Instance.Request_Equipment_Equip(
                    equip,
                    () => { equipComplete = true; }
                );

                // API 응답 대기 (최대 10초)
                float timeout = 0;
                while (!equipComplete && timeout < 10f)
                {
                    timeout += Time.deltaTime;
                    yield return null;
                }

                if (!equipComplete)
                {
                    Debug.LogError($"[Simulator] ❌ 장비 장착 타임아웃: {equip}");
                    hasError = true;
                    yield break;
                }

                if (!hasError)
                {
                    equipCount++;
                }

                // API 안정화 대기
                yield return new WaitForSeconds(0.1f);
            }
        }

        /// <summary>
        /// Mythic 장비를 순차적으로 장착
        /// </summary>
        public static IEnumerator EquipAllMythicGearRoutine(
            EEquipmentMythic weapon,
            EEquipmentMythic helmet,
            EEquipmentMythic bodyArmor,
            EEquipmentMythic gloves,
            EEquipmentMythic boots,
            EEquipmentMythic amulet,
            EEquipmentMythic belt,
            EEquipmentMythic ring1
        )
        {
            List<EEquipmentMythic> equipments = new List<EEquipmentMythic>
            {
                weapon, helmet, bodyArmor, gloves, boots,
                amulet, belt, ring1
            };

            int equipCount = 0;
            foreach (var equip in equipments)
            {
                if (equip == EEquipmentMythic.None)
                {
                    continue;
                }

                bool equipComplete = false;
                bool hasError = false;

                // GameAPIEquipmentManager를 통한 Mythic 장비 장착 API 호출
                GameAPIEquipmentManager.Instance.Request_Equipment_Equip(
                    equip,
                    () => { equipComplete = true; }
                );

                // API 응답 대기 (최대 10초)
                float timeout = 0;
                while (!equipComplete && timeout < 10f)
                {
                    timeout += Time.deltaTime;
                    yield return null;
                }

                if (!equipComplete)
                {
                    Debug.LogError($"[Simulator] ❌ Mythic 장비 장착 타임아웃: {equip}");
                    hasError = true;
                    yield break;
                }

                if (!hasError)
                {
                    equipCount++;
                }

                // API 안정화 대기
                yield return new WaitForSeconds(0.1f);
            }

        }

        /// <summary>
        /// 현재 장착된 모든 Mythic 장비를 해제
        /// </summary>
        public static IEnumerator UnequipAllMythicGearRoutine()
        {
            // 현재 장착된 Mythic 장비 목록 수집
            var equipmentData = GameAPIUserManager.Instance?.userData?.equipmentData?.CoreData;
            if (equipmentData == null)
            {
                Debug.LogError("[Simulator] ❌ 장비 데이터를 찾을 수 없습니다.");
                yield break;
            }

            var equippedMythics = new List<EEquipmentMythic>();
            foreach (var kvp in equipmentData.MythicEquipments)
            {
                if (kvp.Value.IsEquipped)
                {
                    equippedMythics.Add(kvp.Key);
                }
            }

            if (equippedMythics.Count == 0)
            {
                yield break;
            }

            int unequipCount = 0;
            foreach (var equip in equippedMythics)
            {
                bool unequipComplete = false;

                // GameAPIEquipmentManager를 통한 Mythic 장비 해제 API 호출
                GameAPIEquipmentManager.Instance.Request_Equipment_UnEquip(
                    equip,
                    () => { unequipComplete = true; }
                );

                // API 응답 대기 (최대 10초)
                float timeout = 0;
                while (!unequipComplete && timeout < 10f)
                {
                    timeout += Time.deltaTime;
                    yield return null;
                }

                if (!unequipComplete)
                {
                    Debug.LogError($"[Simulator] ❌ Mythic 장비 해제 타임아웃: {equip}");
                    yield break;
                }

                unequipCount++;

                // API 안정화 대기
                yield return new WaitForSeconds(0.1f);
            }
        }

        /// <summary>
        /// 특정 슬롯의 Mythic 장비를 해제
        /// </summary>
        public static IEnumerator UnequipMythicGearRoutine(EEquipmentType slot)
        {
            // 현재 해당 슬롯에 장착된 Mythic 장비 찾기
            var equipmentData = GameAPIUserManager.Instance?.userData?.equipmentData?.CoreData;
            if (equipmentData == null)
            {
                yield break;
            }

            EEquipmentMythic? equippedMythic = null;
            foreach (var kvp in equipmentData.MythicEquipments)
            {
                if (kvp.Value.IsEquipped && kvp.Value.Type == slot)
                {
                    equippedMythic = kvp.Key;
                    break;
                }
            }

            if (!equippedMythic.HasValue)
            {
                yield break;
            }

            bool unequipComplete = false;

            GameAPIEquipmentManager.Instance.Request_Equipment_UnEquip(
                equippedMythic.Value,
                () => { unequipComplete = true; }
            );

            // API 응답 대기 (최대 10초)
            float timeout = 0;
            while (!unequipComplete && timeout < 10f)
            {
                timeout += Time.deltaTime;
                yield return null;
            }

            if (!unequipComplete)
            {
                Debug.LogError($"[Simulator] ❌ Mythic 장비 해제 타임아웃: {equippedMythic.Value}");
            }

            // API 안정화 대기
            yield return new WaitForSeconds(0.1f);
        }

        /// <summary>
        /// prefix로 시작하는 Mythic 장비를 해제 (슬롯별 해제용)
        /// </summary>
        /// <param name="slotPrefix">장비 이름 prefix (예: "accessory_amulet_")</param>
        public static IEnumerator UnequipMythicGearByPrefixRoutine(string slotPrefix)
        {
            var equipmentData = GameAPIUserManager.Instance?.userData?.equipmentData?.CoreData;
            if (equipmentData == null)
            {
                yield break;
            }

            // 해당 prefix로 시작하고 IsEquipped=true인 장비 찾기
            EEquipmentMythic? equippedMythic = null;
            foreach (var kvp in equipmentData.MythicEquipments)
            {
                if (kvp.Value.IsEquipped && kvp.Key.ToString().StartsWith(slotPrefix))
                {
                    equippedMythic = kvp.Key;
                    break;
                }
            }

            if (!equippedMythic.HasValue)
            {
                yield break;
            }

            bool unequipComplete = false;
            bool hasError = false;
            string errorMessage = null;

            GameAPIEquipmentManager.Instance.Request_Equipment_UnEquip(
                equippedMythic.Value,
                () => { unequipComplete = true; },
                (error) => { hasError = true; errorMessage = error; unequipComplete = true; }
            );

            // API 응답 대기 (최대 10초)
            float timeout = 0;
            while (!unequipComplete && timeout < 10f)
            {
                timeout += Time.deltaTime;
                yield return null;
            }

            if (!unequipComplete)
            {
                Debug.LogError($"[Unequip] {slotPrefix}: 타임아웃 - {equippedMythic.Value}");
            }
            else if (hasError)
            {
                Debug.LogError($"[Unequip] {slotPrefix}: 실패 - {errorMessage}");
            }
            // API 안정화 대기
            yield return new WaitForSeconds(0.05f);
        }

        /// <summary>
        /// 특정 Mythic 장비를 해제 (정확한 enum으로 1번만 요청)
        /// IsEquipped 캐시에 의존하지 않고, snapshot에서 가져온 장비를 직접 해제
        /// </summary>
        public static IEnumerator UnequipMythicGearRoutine(EEquipmentMythic equippedMythic)
        {
            if (equippedMythic == EEquipmentMythic.None)
            {
                yield break;
            }

            bool complete = false;
            GameAPIEquipmentManager.Instance.Request_Equipment_UnEquip(
                equippedMythic,
                () => { complete = true; },
                (error) => { complete = true; }  // 에러도 완료 처리 (이미 해제됨)
            );

            float timeout = 0;
            while (!complete && timeout < 5f)
            {
                timeout += Time.deltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(0.05f);
        }

        #endregion

        #region 스킬 장착

        /// <summary>
        /// 선택한 스킬을 순차적으로 장착
        /// </summary>
        public static IEnumerator EquipAllSkillsRoutine(
            ESkill mainSpell,
            ETier spellTier,
            EGrade spellGrade,
            ESkill aura,
            EPet pet,
            ETier petTier,
            EPreset preset,
            List<RuneSocketEntry> runeSockets = null
        )
        {
            int skillCount = 0;

            // 1. 주문 장착
            if (mainSpell != ESkill.None)
            {
                // 이미 장착되어 있는지 확인
                bool alreadyEquipped = IsSpellAlreadyEquipped(mainSpell, spellTier, ESkillSlot.skill_slot_spell_1);

                if (alreadyEquipped)
                {
                }
                else
                {
                    bool complete = false;

                    GameAPISkillManager.Instance.Request_Skill_Equip(
                        ESkillType.skill_spell,
                        mainSpell,
                        spellTier,
                        ESkillSlot.skill_slot_spell_1,
                        () => { complete = true; }
                    );

                    float timeout = 0;
                    while (!complete && timeout < 10f)
                    {
                        timeout += Time.deltaTime;
                        yield return null;
                    }

                    if (!complete)
                    {
                        Debug.LogError($"[Simulator] ❌ 주문 장착 타임아웃: {mainSpell}");
                        yield break;
                    }

                    skillCount++;
                    yield return new WaitForSeconds(0.1f);
                }

                // 1-1. 기존 룬 모두 해제 (주문 변경 시 호환되지 않는 룬 충돌 방지)
                yield return UnequipAllRunesWithVerification(mainSpell, spellTier);

                // 1-2. 스킬룬 장착 (주문에 소켓)
                // 캐시 동기화 문제 방지: 항상 API 호출하여 서버 상태와 일치시킴
                if (runeSockets != null && runeSockets.Count > 0)
                {
                    foreach (var socket in runeSockets)
                    {
                        int socketIndex = socket.socketIndex;
                        ESkill runeSkill = socket.rune;

                        if (runeSkill == ESkill.None)
                            continue;

                        // 태그 매칭 검증: 주문과 룬의 태그가 일치하는지 확인
                        if (!IsRuneTagMatchingSpell(mainSpell, runeSkill))
                            continue;

                        bool runeComplete = false;

                        GameAPISkillManager.Instance.Request_Rune_Equip(
                            true, // InEquip
                            mainSpell,
                            spellTier,
                            socketIndex,
                            runeSkill,
                            spellTier,
                            () => { runeComplete = true; }
                        );

                        float runeTimeout = 0;
                        while (!runeComplete && runeTimeout < 10f)
                        {
                            runeTimeout += Time.deltaTime;
                            yield return null;
                        }

                        if (!runeComplete)
                        {
                            Debug.LogError($"[Simulator] ❌ 스킬룬 장착 타임아웃: Socket={socketIndex}, Rune={runeSkill}");
                        }
                        else
                        {
                            // 내부 추적 기록 (캐시와 별개로 관리)
                            TrackRuneEquipped(socketIndex, runeSkill);

                            // 캐시 갱신 대기 + 검증 (재시도 포함)
                            bool verified = false;
                            int retryCount = 0;
                            const int maxRetries = 5;
                            const float waitTime = 0.2f;

                            while (!verified && retryCount < maxRetries)
                            {
                                yield return new WaitForSeconds(waitTime);
                                verified = VerifyRuneEquippedInCache(mainSpell, spellTier, socketIndex, runeSkill);
                                if (!verified)
                                {
                                    retryCount++;
                                }
                            }

                            if (!verified)
                            {
                                Debug.LogError($"[Simulator] ⚠️ 룬 장착 캐시 검증 실패: Socket={socketIndex}, 요청된 룬={runeSkill} ({maxRetries}회 시도)");
                            }
                        }
                    }
                }

                // 최종 검증: 모든 룬이 정상 장착되었는지 확인
                VerifyAllRunesEquipped(mainSpell, spellTier, runeSockets);
            }

            // 2. 오라 장착
            if (aura != ESkill.None)
            {
                // 이미 장착되어 있는지 확인
                bool auraAlreadyEquipped = IsAuraAlreadyEquipped(aura, ESkillSlot.skill_slot_aura_1);

                if (auraAlreadyEquipped)
                {
                }
                else
                {
                    bool complete = false;

                    GameAPISkillManager.Instance.Request_Skill_Equip(
                        ESkillType.skill_aura,
                        aura,
                        ETier.legendary,
                        ESkillSlot.skill_slot_aura_1,
                        () => { complete = true; }
                    );

                    float timeout = 0;
                    while (!complete && timeout < 10f)
                    {
                        timeout += Time.deltaTime;
                        yield return null;
                    }

                    if (!complete)
                    {
                        Debug.LogError($"[Simulator] ❌ 오라 장착 타임아웃: {aura}");
                        yield break;
                    }

                    skillCount++;
                    yield return new WaitForSeconds(0.1f);
                }
            }

            // 3. 펫 장착
            if (pet != EPet.None)
            {
                // 이미 장착되어 있는지 확인
                bool petAlreadyEquipped = IsPetAlreadyEquipped(pet, petTier, ESkillSlot.skill_slot_pet_1);

                if (petAlreadyEquipped)
                {
                }
                else
                {
                    bool complete = false;

                    GameAPISkillManager.Instance.Request_Pet_Equip(
                        pet,
                        petTier,
                        ESkillSlot.skill_slot_pet_1,
                        () => { complete = true; }
                    );

                    float timeout = 0;
                    while (!complete && timeout < 10f)
                    {
                        timeout += Time.deltaTime;
                        yield return null;
                    }

                    if (!complete)
                    {
                        Debug.LogError($"[Simulator] ❌ 펫 장착 타임아웃: {pet}");
                        yield break;
                    }

                    skillCount++;
                    yield return new WaitForSeconds(0.1f);
                }
            }
        }

        #endregion

        #region 성좌 장착

        /// <summary>
        /// 시뮬레이터 성좌 3슬롯 장착 루틴 (기존 슬롯 해제 후 재장착)
        /// </summary>
        public static IEnumerator ContractConstellationsRoutine(
            EConstellation slot1, EConstellation slot2, EConstellation slot3)
        {
            // 1. 슬롯 1~3 해제
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                UnequipConstellationSlotRoutine(EConstellationSlot.constellation_slot_1));
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                UnequipConstellationSlotRoutine(EConstellationSlot.constellation_slot_2));
            yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                UnequipConstellationSlotRoutine(EConstellationSlot.constellation_slot_3));

            // 2. 선택된 슬롯만 장착
            if (slot1 != EConstellation.None)
            {
                yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                    EquipConstellationSlotRoutine(EConstellationSlot.constellation_slot_1, slot1));
            }
            if (slot2 != EConstellation.None)
            {
                yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                    EquipConstellationSlotRoutine(EConstellationSlot.constellation_slot_2, slot2));
            }
            if (slot3 != EConstellation.None)
            {
                yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                    EquipConstellationSlotRoutine(EConstellationSlot.constellation_slot_3, slot3));
            }
        }

        /// <summary>
        /// 특정 성좌 슬롯 해제 (장착된 성좌가 있을 때만 호출)
        /// </summary>
        private static IEnumerator UnequipConstellationSlotRoutine(EConstellationSlot slot)
        {
            // 해당 슬롯에 성좌가 장착되어 있는지 확인
            var equippedSlots = GameAPIUserManager.Instance?.userData?.constellationData?.CoreData?.EquippedSlots;
            if (equippedSlots == null || !equippedSlots.ContainsKey(slot))
            {
                yield break;
            }

            bool complete = false;

            GameAPINebulaManager.Instance.Request_Constellation_Unequip(
                slot,
                (response) => { complete = true; }
            );

            float timeout = 0;
            while (!complete && timeout < 10f)
            {
                timeout += Time.deltaTime;
                yield return null;
            }

            if (!complete)
            {
                Debug.LogError($"[Simulator] ❌ 성좌 슬롯 해제 타임아웃: {slot}");
                yield break;
            }

            yield return new WaitForSeconds(0.05f);
        }

        /// <summary>
        /// 특정 성좌 슬롯에 성좌 장착
        /// </summary>
        private static IEnumerator EquipConstellationSlotRoutine(EConstellationSlot slot, EConstellation constellation)
        {
            bool complete = false;

            GameAPINebulaManager.Instance.Request_Constellation_Equip(
                slot,
                constellation,
                (response) => { complete = true; }
            );

            float timeout = 0;
            while (!complete && timeout < 10f)
            {
                timeout += Time.deltaTime;
                yield return null;
            }

            if (!complete)
            {
                Debug.LogError($"[Simulator] ❌ 성좌 장착 타임아웃: {slot} → {constellation}");
                yield break;
            }

            yield return new WaitForSeconds(0.05f);
        }

        #endregion

        #region Rune Verification Helpers

        /// <summary>
        /// 개별 룬이 정상적으로 장착되었는지 검증
        /// </summary>
        private static bool VerifyRuneEquipped(ESkill mainSpell, ETier spellTier, int socketIndex, ESkill expectedRune)
        {
            try
            {
                var skillData = GameAPIUserManager.Instance?.userData?.skillData;
                if (skillData == null)
                {
                    Debug.LogError("[Simulator] ❌ 검증 실패: skillData가 null입니다.");
                    return false;
                }

                if (!skillData.GetSpells.TryGetValue(mainSpell, out var spellData))
                {
                    Debug.LogError($"[Simulator] ❌ 검증 실패: 주문 {mainSpell}을 찾을 수 없습니다.");
                    return false;
                }

                if (!spellData.Tiers.TryGetValue(spellTier, out var tierData))
                {
                    Debug.LogError($"[Simulator] ❌ 검증 실패: 티어 {spellTier}를 찾을 수 없습니다.");
                    return false;
                }

                var socketRunes = tierData.SocketRunes;
                if (socketRunes == null)
                {
                    Debug.LogError($"[Simulator] ❌ 검증 실패: SocketRunes가 null입니다.");
                    return false;
                }

                if (!socketRunes.TryGetValue(socketIndex, out ESkill actualRune))
                {
                    Debug.LogError($"[Simulator] ❌ 검증 실패: Socket[{socketIndex}]에 룬이 없습니다. 요청된 룬: {expectedRune}");
                    return false;
                }

                if (actualRune != expectedRune)
                {
                    Debug.LogError($"[Simulator] ❌ 검증 실패: Socket[{socketIndex}] 불일치! 요청={expectedRune}, 실제={actualRune}");
                    return false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Simulator] ❌ 룬 검증 중 예외 발생: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 모든 룬이 정상적으로 장착되었는지 최종 검증 및 요약 로그 출력
        /// </summary>
        private static void VerifyAllRunesEquipped(ESkill mainSpell, ETier spellTier, List<RuneSocketEntry> expectedRunes)
        {
            if (expectedRunes == null || expectedRunes.Count == 0)
                return;

            try
            {
                var skillData = GameAPIUserManager.Instance?.userData?.skillData;
                if (skillData == null)
                {
                    Debug.LogError("[Simulator] ❌ 최종 검증 실패: skillData가 null입니다.");
                    return;
                }

                if (!skillData.GetSpells.TryGetValue(mainSpell, out var spellData) ||
                    !spellData.Tiers.TryGetValue(spellTier, out var tierData))
                {
                    Debug.LogError($"[Simulator] ❌ 최종 검증 실패: 주문/티어 데이터를 찾을 수 없습니다.");
                    return;
                }

                var actualSocketRunes = tierData.SocketRunes ?? new Dictionary<int, ESkill>();

                int expectedCount = 0;
                int actualCount = 0;
                var missingRunes = new List<string>();
                var wrongRunes = new List<string>();

                foreach (var expected in expectedRunes)
                {
                    if (expected.rune == ESkill.None)
                        continue;

                    expectedCount++;

                    if (actualSocketRunes.TryGetValue(expected.socketIndex, out ESkill actualRune))
                    {
                        if (actualRune == expected.rune)
                        {
                            actualCount++;
                        }
                        else
                        {
                            wrongRunes.Add($"Socket[{expected.socketIndex}]: 요청={expected.rune}, 실제={actualRune}");
                        }
                    }
                    else
                    {
                        missingRunes.Add($"Socket[{expected.socketIndex}]: {expected.rune}");
                    }
                }

                // 검증 결과 로그
                if (actualCount != expectedCount)
                {
                    Debug.LogError($"[Simulator] ⚠️ 룬 장착 불완전: {actualCount}/{expectedCount}개만 장착됨");

                    if (missingRunes.Count > 0)
                    {
                        Debug.LogError($"[Simulator] ⚠️ 누락된 룬:\n  - {string.Join("\n  - ", missingRunes)}");
                    }

                    if (wrongRunes.Count > 0)
                    {
                        Debug.LogError($"[Simulator] ⚠️ 잘못 장착된 룬:\n  - {string.Join("\n  - ", wrongRunes)}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Simulator] ❌ 최종 검증 중 예외 발생: {e.Message}");
            }
        }

        #endregion

        #region Duplicate Check Helpers

        /// <summary>
        /// 주문이 이미 해당 슬롯에 장착되어 있는지 확인
        /// </summary>
        private static bool IsSpellAlreadyEquipped(ESkill spell, ETier tier, ESkillSlot slot)
        {
            try
            {
                var skillData = GameAPIUserManager.Instance?.userData?.skillData;
                if (skillData == null) return false;

                // 현재 해당 슬롯에 장착된 주문 가져오기
                var equippedSpell = skillData.GetEquippedSpellData(slot);
                if (equippedSpell == null) return false;

                // 주문 스킬과 티어가 일치하는지 확인
                return equippedSpell.Skill == spell && equippedSpell.Tier == tier;
            }
            catch (System.Exception e)
            {
                return false;
            }
        }

        /// <summary>
        /// 룬이 이미 해당 소켓에 장착되어 있는지 확인
        /// </summary>
        private static bool IsRuneAlreadyEquipped(ESkill spell, ETier spellTier, int socketIndex, ESkill rune)
        {
            try
            {
                var skillData = GameAPIUserManager.Instance?.userData?.skillData;
                if (skillData == null) return false;

                if (!skillData.GetSpells.TryGetValue(spell, out var spellData)) return false;
                if (!spellData.Tiers.TryGetValue(spellTier, out var tierData)) return false;

                var socketRunes = tierData.SocketRunes;
                if (socketRunes == null) return false;

                if (socketRunes.TryGetValue(socketIndex, out ESkill equippedRune))
                {
                    return equippedRune == rune;
                }
                return false;
            }
            catch (System.Exception e)
            {
                return false;
            }
        }

        /// <summary>
        /// 오라가 이미 해당 슬롯에 장착되어 있는지 확인
        /// </summary>
        private static bool IsAuraAlreadyEquipped(ESkill aura, ESkillSlot slot)
        {
            try
            {
                var skillData = GameAPIUserManager.Instance?.userData?.skillData;
                if (skillData == null) return false;

                // 오라 데이터에서 장착 상태 확인
                var auras = skillData.GetAuras;
                if (auras == null) return false;

                foreach (var auraPair in auras)
                {
                    foreach (var tierPair in auraPair.Value.Tiers)
                    {
                        if (tierPair.Value.IsEquipped &&
                            tierPair.Value.EquippedSlot == slot &&
                            auraPair.Key == aura)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (System.Exception e)
            {
                return false;
            }
        }

        /// <summary>
        /// 펫이 이미 해당 슬롯에 장착되어 있는지 확인
        /// </summary>
        private static bool IsPetAlreadyEquipped(EPet pet, ETier tier, ESkillSlot slot)
        {
            try
            {
                var petData = GameAPIUserManager.Instance?.userData?.petData;
                if (petData?.CoreData?.Pets == null) return false;

                // 펫 데이터에서 장착 상태 확인
                if (!petData.CoreData.Pets.TryGetValue(pet, out var petInfo)) return false;
                if (!petInfo.Tiers.TryGetValue(tier, out var tierInfo)) return false;

                return tierInfo.IsEquipped && tierInfo.EquippedSlot == slot;
            }
            catch (System.Exception e)
            {
                return false;
            }
        }

        #endregion

        #region Rune Management

        // 마지막으로 장착된 룬 추적 (캐시 동기화 문제 해결용)
        private static Dictionary<int, ESkill> _lastEquippedRunes = new Dictionary<int, ESkill>();

        /// <summary>
        /// 룬 장착 상태 기록 (내부 추적용)
        /// </summary>
        public static void TrackRuneEquipped(int socketIndex, ESkill rune)
        {
            if (rune == ESkill.None)
                _lastEquippedRunes.Remove(socketIndex);
            else
                _lastEquippedRunes[socketIndex] = rune;
        }

        /// <summary>
        /// 내부 추적 초기화
        /// </summary>
        public static void ClearRuneTracking()
        {
            _lastEquippedRunes.Clear();
        }

        /// <summary>
        /// 현재 주문에 장착된 모든 룬을 API를 통해 해제
        /// 캐시 + 내부 추적 데이터를 모두 사용하여 확실하게 해제
        /// </summary>
        public static IEnumerator UnequipAllRunesRoutine(ESkill mainSpell, ETier spellTier)
        {
            // 해제해야 할 룬 목록 수집 (캐시 + 내부 추적 병합)
            var runesToUnequip = new Dictionary<int, ESkill>();

            // 1. 클라이언트 캐시에서 수집
            try
            {
                var skillData = GameAPIUserManager.Instance?.userData?.skillData;
                if (skillData != null && skillData.GetSpells.TryGetValue(mainSpell, out var spellData))
                {
                    if (spellData.Tiers.TryGetValue(spellTier, out var tierData) && tierData.SocketRunes != null)
                    {
                        foreach (var kvp in tierData.SocketRunes)
                        {
                            if (kvp.Value != ESkill.None)
                                runesToUnequip[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (System.Exception) { }

            // 2. 내부 추적에서 수집 (캐시에 없는 것도 포함)
            foreach (var kvp in _lastEquippedRunes)
            {
                if (kvp.Value != ESkill.None && !runesToUnequip.ContainsKey(kvp.Key))
                    runesToUnequip[kvp.Key] = kvp.Value;
            }

            if (runesToUnequip.Count == 0)
            {
                yield break;
            }

            int unequippedCount = 0;
            int totalRunes = runesToUnequip.Count;

            // 각 소켓의 룬을 해제
            foreach (var kvp in runesToUnequip)
            {
                int socketIndex = kvp.Key;
                ESkill runeSkill = kvp.Value;

                bool complete = false;

                GameAPISkillManager.Instance.Request_Rune_Equip(
                    false, // 해제
                    mainSpell,
                    spellTier,
                    socketIndex,
                    runeSkill,
                    spellTier,
                    () => { complete = true; }
                );

                float timeout = 0;
                while (!complete && timeout < 5f)
                {
                    timeout += Time.deltaTime;
                    yield return null;
                }

                if (complete)
                {
                    unequippedCount++;
                    _lastEquippedRunes.Remove(socketIndex); // 내부 추적에서도 제거
                }

                yield return new WaitForSeconds(0.05f);
            }
        }

        /// <summary>
        /// 모든 룬이 실제로 해제되었는지 클라이언트 캐시에서 검증
        /// </summary>
        /// <returns>모든 룬이 해제되었으면 true</returns>
        public static bool VerifyAllRunesUnequipped(ESkill mainSpell, ETier spellTier)
        {
            try
            {
                var skillData = GameAPIUserManager.Instance?.userData?.skillData;
                if (skillData == null)
                {
                    return false;
                }

                if (!skillData.GetSpells.TryGetValue(mainSpell, out var spellData))
                {
                    // 주문 데이터가 없으면 룬도 없는 것
                    return true;
                }

                if (!spellData.Tiers.TryGetValue(spellTier, out var tierData))
                {
                    // 티어 데이터가 없으면 룬도 없는 것
                    return true;
                }

                var socketRunes = tierData.SocketRunes;
                if (socketRunes == null || socketRunes.Count == 0)
                {
                    // 룬 없음 - 해제 완료
                    return true;
                }

                // 아직 장착된 룬이 있는지 확인
                foreach (var kvp in socketRunes)
                {
                    if (kvp.Value != ESkill.None)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 특정 소켓에 특정 룬이 장착되었는지 클라이언트 캐시에서 검증
        /// </summary>
        public static bool VerifyRuneEquippedInCache(ESkill mainSpell, ETier spellTier, int socketIndex, ESkill expectedRune)
        {
            try
            {
                var skillData = GameAPIUserManager.Instance?.userData?.skillData;
                if (skillData == null)
                    return false;

                if (!skillData.GetSpells.TryGetValue(mainSpell, out var spellData))
                    return false;

                if (!spellData.Tiers.TryGetValue(spellTier, out var tierData))
                    return false;

                var socketRunes = tierData.SocketRunes;
                if (socketRunes == null)
                    return false;

                if (!socketRunes.TryGetValue(socketIndex, out ESkill actualRune))
                    return false;

                return actualRune == expectedRune;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 룬 해제 + 캐시 갱신 대기 + 검증 (재시도 포함)
        /// </summary>
        /// <param name="mainSpell">주문</param>
        /// <param name="spellTier">티어</param>
        /// <param name="maxRetries">최대 재시도 횟수</param>
        /// <param name="waitTimePerRetry">재시도당 대기 시간 (초)</param>
        public static IEnumerator UnequipAllRunesWithVerification(
            ESkill mainSpell,
            ETier spellTier,
            int maxRetries = 5,
            float waitTimePerRetry = 0.3f)
        {
            // 1. 기존 해제 로직 실행
            yield return UnequipAllRunesRoutine(mainSpell, spellTier);

            // 2. 캐시 갱신 대기 및 검증
            bool verified = false;
            int retryCount = 0;

            while (!verified && retryCount < maxRetries)
            {
                // 캐시 갱신 대기
                yield return new WaitForSeconds(waitTimePerRetry);

                // 검증
                verified = VerifyAllRunesUnequipped(mainSpell, spellTier);

                if (!verified)
                {
                    retryCount++;
                }
            }

            if (!verified)
            {
                // 내부 추적 강제 클리어
                ClearRuneTracking();
            }
        }

        /// <summary>
        /// 룬 장착 + 캐시 갱신 대기 + 검증 (단일 룬)
        /// </summary>
        public static IEnumerator EquipRuneWithVerification(
            ESkill mainSpell,
            ETier spellTier,
            int socketIndex,
            ESkill rune,
            int maxRetries = 5,
            float waitTimePerRetry = 0.3f)
        {
            bool complete = false;

            // 1. API 호출
            GameAPISkillManager.Instance.Request_Rune_Equip(
                true, // 장착
                mainSpell,
                spellTier,
                socketIndex,
                rune,
                spellTier,
                () => { complete = true; }
            );

            // API 응답 대기
            float timeout = 0;
            while (!complete && timeout < 10f)
            {
                timeout += Time.deltaTime;
                yield return null;
            }

            if (!complete)
            {
                yield break;
            }

            // 내부 추적 기록
            TrackRuneEquipped(socketIndex, rune);

            // 2. 캐시 갱신 대기 및 검증
            bool verified = false;
            int retryCount = 0;

            while (!verified && retryCount < maxRetries)
            {
                yield return new WaitForSeconds(waitTimePerRetry);

                verified = VerifyRuneEquippedInCache(mainSpell, spellTier, socketIndex, rune);

                if (!verified)
                {
                    retryCount++;
                }
            }
        }

        /// <summary>
        /// 현재 캐시의 룬 상태를 로그로 출력 (디버깅용) - 로그 비활성화됨
        /// </summary>
        public static void LogCurrentRuneState(ESkill mainSpell, ETier spellTier)
        {
        }

        /// <summary>
        /// 룬이 주문과 태그가 매칭되는지 검증
        /// </summary>
        public static bool IsRuneTagMatchingSpell(ESkill spell, ESkill rune)
        {
            if (spell == ESkill.None || rune == ESkill.None)
                return false;

            if (GameDBClientManager.Instance == null)
                return false;

            var spellDB = GameDBClientManager.Instance.GameDB_Skill?.Spell;
            var runeDB = GameDBClientManager.Instance.GameDB_Skill?.Rune;

            if (spellDB == null || runeDB == null)
                return false;

            if (!spellDB.MapData.TryGetValue(spell, out var spellData))
                return false;

            if (!runeDB.MapData.TryGetValue(rune, out var runeData))
                return false;

            // 주문에서 활성화된 태그 중 하나라도 룬에서 활성화되어 있으면 매칭
            if (spellData.SkillTags != null && runeData.SkillTags != null)
            {
                foreach (var spellTag in spellData.SkillTags)
                {
                    if (spellTag.Value && runeData.SkillTags.TryGetValue(spellTag.Key, out bool runeHasTag) && runeHasTag)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion
    }
}
