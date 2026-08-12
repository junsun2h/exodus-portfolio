using Sirenix.OdinInspector;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// BattleSimulatorWindow의 드롭다운 데이터 제공 메서드들
    /// </summary>
    public partial class BattleSimulatorWindow
    {
        #region ValueDropdown Methods

        // 장비 카테고리별 필터링 메서드
        private IEnumerable<EEquipmentNormal> GetWeaponList()
        {
            return GetEquipmentListByPrefix("weapon_");
        }

        private IEnumerable<EEquipmentNormal> GetHelmetList()
        {
            return GetEquipmentListByPrefix("armour_helmet_");
        }

        private IEnumerable<EEquipmentNormal> GetBodyArmorList()
        {
            return GetEquipmentListByPrefix("armour_bodyarmour_");
        }

        private IEnumerable<EEquipmentNormal> GetGlovesList()
        {
            return GetEquipmentListByPrefix("armour_glove_");
        }

        private IEnumerable<EEquipmentNormal> GetBootsList()
        {
            return GetEquipmentListByPrefix("armour_boots_");
        }

        private IEnumerable<EEquipmentNormal> GetAmuletList()
        {
            return GetEquipmentListByPrefix("accessory_amulet_");
        }

        private IEnumerable<EEquipmentNormal> GetBeltList()
        {
            return GetEquipmentListByPrefix("accessory_belt_");
        }

        private IEnumerable<EEquipmentNormal> GetRingList()
        {
            return GetEquipmentListByPrefix("accessory_ring_");
        }

        private IEnumerable<EEquipmentNormal> GetEquipmentListByPrefix(string prefix)
        {
            if (!Application.isPlaying || GameDBClientManager.Instance == null)
            {
                return new List<EEquipmentNormal> { EEquipmentNormal.None };
            }

            var equipmentDB = GameDBClientManager.Instance.GameDB_Equipment?.EquipmentNormal;
            if (equipmentDB == null || equipmentDB.MapData == null)
            {
                return new List<EEquipmentNormal> { EEquipmentNormal.None };
            }

            var result = new List<EEquipmentNormal> { EEquipmentNormal.None };
            foreach (var key in equipmentDB.MapData.Keys)
            {
                string keyStr = key.ToString();
                int enumValue = (int)key;
                // 신화 등급 제외 (~전설): enum 값 < 1000
                if (keyStr.StartsWith(prefix) && enumValue < 1000)
                {
                    result.Add(key);
                }
            }

            return result;
        }

        // 신화 등급 장비 필터링 메서드
        private IEnumerable<EEquipmentMythic> GetMythicWeaponList()
        {
            return GetMythicEquipmentListByPrefix("weapon_");
        }

        private IEnumerable<EEquipmentMythic> GetMythicHelmetList()
        {
            return GetMythicEquipmentListByPrefix("armour_helmet_");
        }

        private IEnumerable<EEquipmentMythic> GetMythicBodyArmorList()
        {
            return GetMythicEquipmentListByPrefix("armour_bodyarmour_");
        }

        private IEnumerable<EEquipmentMythic> GetMythicGlovesList()
        {
            return GetMythicEquipmentListByPrefix("armour_glove_");
        }

        private IEnumerable<EEquipmentMythic> GetMythicBootsList()
        {
            return GetMythicEquipmentListByPrefix("armour_boots_");
        }

        private IEnumerable<EEquipmentMythic> GetMythicAmuletList()
        {
            return GetMythicEquipmentListByPrefix("accessory_amulet_");
        }

        private IEnumerable<EEquipmentMythic> GetMythicBeltList()
        {
            return GetMythicEquipmentListByPrefix("accessory_belt_");
        }

        private IEnumerable<EEquipmentMythic> GetMythicRingList()
        {
            return GetMythicEquipmentListByPrefix("accessory_ring_");
        }

        private IEnumerable<EEquipmentMythic> GetMythicEquipmentListByPrefix(string prefix)
        {
            if (!Application.isPlaying || GameDBClientManager.Instance == null)
            {
                return new List<EEquipmentMythic> { EEquipmentMythic.None };
            }

            var equipmentMythicDB = GameDBClientManager.Instance.GameDB_Equipment?.EquipmentMythic;
            if (equipmentMythicDB == null || equipmentMythicDB.MapData == null)
            {
                return new List<EEquipmentMythic> { EEquipmentMythic.None };
            }

            var result = new List<EEquipmentMythic> { EEquipmentMythic.None };

            // EquipmentMythic.MapData의 구조:
            // Dictionary<EEquipmentType, GameDB_Client_EquipmentMythic>
            // 각 GameDB_Client_EquipmentMythic 안에 MythicMods: Dictionary<EEquipmentMythic, ...>
            foreach (var equipTypeEntry in equipmentMythicDB.MapData)
            {
                if (equipTypeEntry.Value.MythicMods != null)
                {
                    foreach (var mythicKey in equipTypeEntry.Value.MythicMods.Keys)
                    {
                        string keyStr = mythicKey.ToString();
                        if (keyStr.StartsWith(prefix))
                        {
                            result.Add(mythicKey);
                        }
                    }
                }
            }

            return result;
        }

        private IEnumerable<ESkill> GetSpellList()
        {
            if (!Application.isPlaying || GameDBClientManager.Instance == null)
            {
                return new List<ESkill> { ESkill.None };
            }

            var skillDB = GameDBClientManager.Instance.GameDB_Skill?.Spell;
            if (skillDB == null || skillDB.MapData == null)
            {
                return new List<ESkill> { ESkill.None };
            }

            // skillspell_만 필터링
            var result = new List<ESkill> { ESkill.None };
            foreach (var key in skillDB.MapData.Keys)
            {
                if (key.ToString().StartsWith("skillspell_"))
                {
                    result.Add(key);
                }
            }

            return result;
        }

        private IEnumerable<ESkill> GetAuraList()
        {
            if (!Application.isPlaying || GameDBClientManager.Instance == null)
            {
                return new List<ESkill> { ESkill.None };
            }

            var skillDB = GameDBClientManager.Instance.GameDB_Skill?.Aura;
            if (skillDB == null || skillDB.MapData == null)
            {
                return new List<ESkill> { ESkill.None };
            }

            return skillDB.MapData.Keys;
        }

        private IEnumerable<EPet> GetPetList()
        {
            if (!Application.isPlaying || GameDBClientManager.Instance == null)
            {
                return new List<EPet> { EPet.None };
            }

            var petDB = GameDBClientManager.Instance.GameDB_Pet?.Pet;
            if (petDB == null || petDB.MapData == null)
            {
                return new List<EPet> { EPet.None };
            }

            return petDB.MapData.Keys;
        }

        /// <summary>
        /// 중복 방지 필터링된 Legendary 티어 성좌 목록 반환
        /// </summary>
        private IEnumerable<ValueDropdownItem<EConstellation>> GetFilteredConstellationList(
            EConstellation excludeA, EConstellation excludeB)
        {
            var result = new List<ValueDropdownItem<EConstellation>>();

            // None 옵션 추가
            result.Add(new ValueDropdownItem<EConstellation>("없음", EConstellation.None));

            if (!Application.isPlaying || GameDBClientManager.Instance == null)
            {
                return result;
            }

            var constellationDB = GameDBClientManager.Instance.GameDB_Nebula?.Constellation;
            if (constellationDB == null || constellationDB.MapData == null)
            {
                return result;
            }

            // Legendary 티어 성좌만 필터링 + 다른 슬롯에서 선택된 성좌 제외
            foreach (var entry in constellationDB.MapData)
            {
                if (entry.Value.DamageTypeTier == ETier.legendary)
                {
                    // 다른 슬롯에서 이미 선택된 성좌는 제외
                    if (entry.Key != EConstellation.None &&
                        (entry.Key == excludeA || entry.Key == excludeB))
                    {
                        continue;
                    }

                    // 성좌 이름 StringKey로 가져오기
                    string stringKey = $"stringkey_{entry.Key.ToString()}_name";
                    string displayName = GameStringKeyLoader.GetKoreanText(stringKey);
                    if (string.IsNullOrEmpty(displayName) || displayName == stringKey)
                    {
                        displayName = entry.Key.ToString();
                    }

                    result.Add(new ValueDropdownItem<EConstellation>(displayName, entry.Key));
                }
            }

            return result;
        }

        /// <summary>
        /// 슬롯1용 성좌 목록 (슬롯2, 슬롯3 제외)
        /// </summary>
        private IEnumerable<ValueDropdownItem<EConstellation>> GetConstellationListSlot1()
        {
            return GetFilteredConstellationList(constellationSlot2, constellationSlot3);
        }

        /// <summary>
        /// 슬롯2용 성좌 목록 (슬롯1, 슬롯3 제외)
        /// </summary>
        private IEnumerable<ValueDropdownItem<EConstellation>> GetConstellationListSlot2()
        {
            return GetFilteredConstellationList(constellationSlot1, constellationSlot3);
        }

        /// <summary>
        /// 슬롯3용 성좌 목록 (슬롯1, 슬롯2 제외)
        /// </summary>
        private IEnumerable<ValueDropdownItem<EConstellation>> GetConstellationListSlot3()
        {
            return GetFilteredConstellationList(constellationSlot1, constellationSlot2);
        }

        private IEnumerable<EPreset> GetPresetList()
        {
            return new List<EPreset>
            {
                EPreset.preset_1,
                EPreset.preset_2,
                EPreset.preset_3
            };
        }

        #endregion

        #region 장비 상호 배타 콜백
        // Normal 장비 선택 시 Mythic 자동 해제
        private void OnWeaponNormalChanged() { if (weapon != EEquipmentNormal.None) weaponMythic = EEquipmentMythic.None; }
        private void OnHelmetNormalChanged() { if (helmet != EEquipmentNormal.None) helmetMythic = EEquipmentMythic.None; }
        private void OnBodyArmorNormalChanged() { if (bodyArmor != EEquipmentNormal.None) bodyArmorMythic = EEquipmentMythic.None; }
        private void OnGlovesNormalChanged() { if (gloves != EEquipmentNormal.None) glovesMythic = EEquipmentMythic.None; }
        private void OnBootsNormalChanged() { if (boots != EEquipmentNormal.None) bootsMythic = EEquipmentMythic.None; }
        private void OnAmuletNormalChanged() { if (amulet != EEquipmentNormal.None) amuletMythic = EEquipmentMythic.None; }
        private void OnBeltNormalChanged() { if (belt != EEquipmentNormal.None) beltMythic = EEquipmentMythic.None; }
        private void OnRing1NormalChanged() { if (ring1 != EEquipmentNormal.None) ring1Mythic = EEquipmentMythic.None; }

        // Mythic 장비 선택 시 Normal 자동 해제
        private void OnWeaponMythicChanged() { if (weaponMythic != EEquipmentMythic.None) weapon = EEquipmentNormal.None; }
        private void OnHelmetMythicChanged() { if (helmetMythic != EEquipmentMythic.None) helmet = EEquipmentNormal.None; }
        private void OnBodyArmorMythicChanged() { if (bodyArmorMythic != EEquipmentMythic.None) bodyArmor = EEquipmentNormal.None; }
        private void OnGlovesMythicChanged() { if (glovesMythic != EEquipmentMythic.None) gloves = EEquipmentNormal.None; }
        private void OnBootsMythicChanged() { if (bootsMythic != EEquipmentMythic.None) boots = EEquipmentNormal.None; }
        private void OnAmuletMythicChanged() { if (amuletMythic != EEquipmentMythic.None) amulet = EEquipmentNormal.None; }
        private void OnBeltMythicChanged() { if (beltMythic != EEquipmentMythic.None) belt = EEquipmentNormal.None; }
        private void OnRing1MythicChanged() { if (ring1Mythic != EEquipmentMythic.None) ring1 = EEquipmentNormal.None; }
        #endregion
    }
}
