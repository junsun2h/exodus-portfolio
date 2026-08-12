using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// CoreData 변경사항 추적을 위한 경량 스냅샷 관리자
    /// DeepCopy/Reflection 대신 Count 값만 캡처하여 직접 비교
    /// </summary>
    public class CoreDataSnapshotManager
    {
        #region 스냅샷 구조체

        /// <summary>
        /// Currency 스냅샷 - BigInt와 일반 재화 Count 값 저장
        /// </summary>
        public struct CurrencySnapshot
        {
            public Dictionary<ECurrency, BigInteger> BigIntCounts;
            public Dictionary<(ECurrency, ETier, EGrade), long> NormalCounts;

            public static CurrencySnapshot Create()
            {
                return new CurrencySnapshot
                {
                    BigIntCounts = new Dictionary<ECurrency, BigInteger>(),
                    NormalCounts = new Dictionary<(ECurrency, ETier, EGrade), long>()
                };
            }
        }

        /// <summary>
        /// Equipment 스냅샷 - 일반/신화 장비 Count/Grade 값 저장
        /// </summary>
        public struct EquipmentSnapshot
        {
            public Dictionary<EEquipmentNormal, int> NormalCounts;
            public Dictionary<EEquipmentMythic, int> MythicCounts;
            public Dictionary<EEquipmentMythic, EGrade> MythicGrades;

            public static EquipmentSnapshot Create()
            {
                return new EquipmentSnapshot
                {
                    NormalCounts = new Dictionary<EEquipmentNormal, int>(),
                    MythicCounts = new Dictionary<EEquipmentMythic, int>(),
                    MythicGrades = new Dictionary<EEquipmentMythic, EGrade>()
                };
            }
        }

        /// <summary>
        /// Skill 스냅샷 - 4가지 스킬 타입 Count 값 저장
        /// </summary>
        public struct SkillSnapshot
        {
            public Dictionary<(ESkill, ETier), int> SpellCounts;
            public Dictionary<(ESkill, ETier), int> AuraCounts;
            public Dictionary<(ESkill, ETier), int> CurseCounts;
            public Dictionary<(ESkill, ETier), int> RuneCounts;

            public static SkillSnapshot Create()
            {
                return new SkillSnapshot
                {
                    SpellCounts = new Dictionary<(ESkill, ETier), int>(),
                    AuraCounts = new Dictionary<(ESkill, ETier), int>(),
                    CurseCounts = new Dictionary<(ESkill, ETier), int>(),
                    RuneCounts = new Dictionary<(ESkill, ETier), int>()
                };
            }
        }

        /// <summary>
        /// Pet 스냅샷 - 펫 Count 값 저장
        /// </summary>
        public struct PetSnapshot
        {
            public Dictionary<(EPet, ETier), int> Counts;

            public static PetSnapshot Create()
            {
                return new PetSnapshot
                {
                    Counts = new Dictionary<(EPet, ETier), int>()
                };
            }
        }

        #endregion

        #region 스냅샷 저장소

        private CurrencySnapshot? currencySnapshot;
        private EquipmentSnapshot? equipmentSnapshot;
        private SkillSnapshot? skillSnapshot;
        private PetSnapshot? petSnapshot;
        private Dictionary<ECurrencyTicket, int>? ticketSnapshot;

        #endregion

        #region 공개 메서드

        /// <summary>
        /// 모든 스냅샷 생성 (PreCachedCoreData 대체)
        /// </summary>
        public void CreateSnapshots(UserData userData)
        {
            if (userData == null) return;

            currencySnapshot = CreateCurrencySnapshot(userData.currencyData);
            equipmentSnapshot = CreateEquipmentSnapshot(userData.equipmentData);
            skillSnapshot = CreateSkillSnapshot(userData.skillData);
            petSnapshot = CreatePetSnapshot(userData.petData);
            ticketSnapshot = CreateTicketSnapshot(userData.ticketData);
        }

        /// <summary>
        /// 스냅샷 정리
        /// </summary>
        public void ClearSnapshots()
        {
            currencySnapshot = null;
            equipmentSnapshot = null;
            skillSnapshot = null;
            petSnapshot = null;
            ticketSnapshot = null;
        }

        /// <summary>
        /// 스냅샷과 현재 데이터의 차이 계산 (DiffChangedCoreData 대체)
        /// </summary>
        public List<FCommonItemSlotData> CalculateDiff(UserData currentData)
        {
            List<FCommonItemSlotData> result = new List<FCommonItemSlotData>();

            if (currentData == null) return result;

            // Currency 비교
            if (currencySnapshot.HasValue)
            {
                CalculateCurrencyDiff(currencySnapshot.Value, currentData.currencyData, result);
            }

            // Equipment 비교
            if (equipmentSnapshot.HasValue)
            {
                CalculateEquipmentDiff(equipmentSnapshot.Value, currentData.equipmentData, result);
            }

            // Skill 비교
            if (skillSnapshot.HasValue)
            {
                CalculateSkillDiff(skillSnapshot.Value, currentData.skillData, result);
            }

            // Pet 비교
            if (petSnapshot.HasValue)
            {
                CalculatePetDiff(petSnapshot.Value, currentData.petData, result);
            }

            // Ticket 비교
            CalculateTicketDiff(currentData, result);

#if UNITY_EDITOR
            foreach (var item in result)
            {
                Debug.Log($"[CoreDataSnapshot] Diff: {item}");
            }
#endif

            return result;
        }

        #endregion

        #region Currency 스냅샷

        private CurrencySnapshot CreateCurrencySnapshot(CurrencyData data)
        {
            var snapshot = CurrencySnapshot.Create();

            if (data?.CoreData == null) return snapshot;

            // BigInt 재화 (Gold, XP)
            foreach (var kvp in data.CoreData.BigIntCurrencies)
            {
                snapshot.BigIntCounts[kvp.Key] = kvp.Value.Count.Value;
            }

            // 일반 재화
            foreach (var kvp in data.CoreData.Currencies)
            {
                var currency = kvp.Value;
                var currencyType = kvp.Key;

                if (currency.HasTier && currency.HasGrade)
                {
                    // 티어 + 등급 있는 재화
                    foreach (var tierKvp in currency.Tiers)
                    {
                        foreach (var gradeKvp in tierKvp.Value.Grades)
                        {
                            var key = (currencyType, tierKvp.Key, gradeKvp.Key);
                            snapshot.NormalCounts[key] = gradeKvp.Value.Count.Value;
                        }
                    }
                }
                else if (currency.HasTier)
                {
                    // 티어만 있는 재화
                    foreach (var tierKvp in currency.Tiers)
                    {
                        var key = (currencyType, tierKvp.Key, EGrade.None);
                        snapshot.NormalCounts[key] = tierKvp.Value.Count.Value;
                    }
                }
                else
                {
                    // 티어/등급 없는 일반 재화
                    var key = (currencyType, ETier.None, EGrade.None);
                    snapshot.NormalCounts[key] = currency.Count.Value;
                }
            }

            return snapshot;
        }

        private void CalculateCurrencyDiff(CurrencySnapshot before, CurrencyData current, List<FCommonItemSlotData> result)
        {
            if (current?.CoreData == null) return;

            // BigInt 재화 비교
            foreach (var kvp in current.CoreData.BigIntCurrencies)
            {
                var afterCount = kvp.Value.Count.Value;
                before.BigIntCounts.TryGetValue(kvp.Key, out var beforeCount);
                var diff = afterCount - beforeCount;

                if (diff > 0)
                {
                    result.Add(new FCurrencySlotData(kvp.Key, PXBigInt.Create(diff)));
                }
            }

            // 일반 재화 비교
            foreach (var kvp in current.CoreData.Currencies)
            {
                var currency = kvp.Value;
                var currencyType = kvp.Key;

                if (currency.HasTier && currency.HasGrade)
                {
                    foreach (var tierKvp in currency.Tiers)
                    {
                        foreach (var gradeKvp in tierKvp.Value.Grades)
                        {
                            var key = (currencyType, tierKvp.Key, gradeKvp.Key);
                            var afterCount = gradeKvp.Value.Count.Value;
                            before.NormalCounts.TryGetValue(key, out var beforeCount);
                            var diff = afterCount - beforeCount;

                            if (diff > 0)
                            {
                                result.Add(new FCurrencySlotData(currencyType, PXBigInt.Create((BigInteger)diff), tierKvp.Key, gradeKvp.Key));
                            }
                        }
                    }
                }
                else if (currency.HasTier)
                {
                    foreach (var tierKvp in currency.Tiers)
                    {
                        var key = (currencyType, tierKvp.Key, EGrade.None);
                        var afterCount = tierKvp.Value.Count.Value;
                        before.NormalCounts.TryGetValue(key, out var beforeCount);
                        var diff = afterCount - beforeCount;

                        if (diff > 0)
                        {
                            result.Add(new FCurrencySlotData(currencyType, PXBigInt.Create((BigInteger)diff), tierKvp.Key));
                        }
                    }
                }
                else
                {
                    var key = (currencyType, ETier.None, EGrade.None);
                    var afterCount = currency.Count.Value;
                    before.NormalCounts.TryGetValue(key, out var beforeCount);
                    var diff = afterCount - beforeCount;

                    if (diff > 0)
                    {
                        result.Add(new FCurrencySlotData(currencyType, PXBigInt.Create((BigInteger)diff)));
                    }
                }
            }
        }

        #endregion

        #region Equipment 스냅샷

        private EquipmentSnapshot CreateEquipmentSnapshot(EquipmentData data)
        {
            var snapshot = EquipmentSnapshot.Create();

            if (data?.CoreData == null) return snapshot;

            // 일반 장비
            foreach (var kvp in data.CoreData.NormalEquipments)
            {
                snapshot.NormalCounts[kvp.Key] = kvp.Value.Count.Value;
            }

            // 신화 장비
            foreach (var kvp in data.CoreData.MythicEquipments)
            {
                snapshot.MythicCounts[kvp.Key] = kvp.Value.Count.Value;
                snapshot.MythicGrades[kvp.Key] = kvp.Value.Grade;
            }

            return snapshot;
        }

        private void CalculateEquipmentDiff(EquipmentSnapshot before, EquipmentData current, List<FCommonItemSlotData> result)
        {
            if (current?.CoreData == null) return;

            // 일반 장비 비교
            foreach (var kvp in current.CoreData.NormalEquipments)
            {
                var afterCount = kvp.Value.Count.Value;
                before.NormalCounts.TryGetValue(kvp.Key, out var beforeCount);
                var diff = afterCount - beforeCount;

                if (diff > 0)
                {
                    var slotData = new FEquipmentSlotData(kvp.Key, kvp.Value);
                    slotData.SetShowCount(diff);
                    result.Add(slotData);
                }
            }

            // 신화 장비 비교
            foreach (var kvp in current.CoreData.MythicEquipments)
            {
                var afterCount = kvp.Value.Count.Value;
                before.MythicCounts.TryGetValue(kvp.Key, out var beforeCount);
                var diff = afterCount - beforeCount;

                // Grade 변경 감지 (신화 등급 승급: grade0→1→...→5)
                // EGrade enum 값이 순차적이지 않으므로(grade0=10, grade1=1, ..., grade5=11) != 비교
                before.MythicGrades.TryGetValue(kvp.Key, out var beforeGrade);
                bool gradeUpgraded = kvp.Value.Grade != beforeGrade;

                if (gradeUpgraded)
                {
                    // 신화 등급 승급: Grade가 올랐으면 승급 결과로 표시
                    var slotData = new FEquipmentSlotData(kvp.Key, kvp.Value);
                    slotData.SetShowCount(1);
                    result.Add(slotData);
                }
                else if (diff > 0)
                {
                    // 신화 장비 수량 증가 (전설→신화 승급 등)
                    var slotData = new FEquipmentSlotData(kvp.Key, kvp.Value);
                    slotData.SetShowCount(diff);
                    result.Add(slotData);
                }
            }
        }

        #endregion

        #region Skill 스냅샷

        private SkillSnapshot CreateSkillSnapshot(SkillData data)
        {
            var snapshot = SkillSnapshot.Create();

            if (data?.CoreData == null) return snapshot;

            // Spell
            foreach (var skillKvp in data.CoreData.Spells)
            {
                foreach (var tierKvp in skillKvp.Value.Tiers)
                {
                    var key = (skillKvp.Key, tierKvp.Key);
                    snapshot.SpellCounts[key] = tierKvp.Value.Count.Value;
                }
            }

            // Aura
            foreach (var skillKvp in data.CoreData.Auras)
            {
                foreach (var tierKvp in skillKvp.Value.Tiers)
                {
                    var key = (skillKvp.Key, tierKvp.Key);
                    snapshot.AuraCounts[key] = tierKvp.Value.Count.Value;
                }
            }

            // Curse
            foreach (var skillKvp in data.CoreData.Curses)
            {
                foreach (var tierKvp in skillKvp.Value.Tiers)
                {
                    var key = (skillKvp.Key, tierKvp.Key);
                    snapshot.CurseCounts[key] = tierKvp.Value.Count.Value;
                }
            }

            // Rune
            foreach (var skillKvp in data.CoreData.Runes)
            {
                foreach (var tierKvp in skillKvp.Value.Tiers)
                {
                    var key = (skillKvp.Key, tierKvp.Key);
                    snapshot.RuneCounts[key] = tierKvp.Value.Count.Value;
                }
            }

            return snapshot;
        }

        private void CalculateSkillDiff(SkillSnapshot before, SkillData current, List<FCommonItemSlotData> result)
        {
            if (current?.CoreData == null) return;

            // Spell 비교
            foreach (var skillKvp in current.CoreData.Spells)
            {
                foreach (var tierKvp in skillKvp.Value.Tiers)
                {
                    var key = (skillKvp.Key, tierKvp.Key);
                    var afterCount = tierKvp.Value.Count.Value;
                    before.SpellCounts.TryGetValue(key, out var beforeCount);
                    var diff = afterCount - beforeCount;

                    if (diff > 0)
                    {
                        var dbSkill = GameAPISkillManager.Instance.GetDBClientSkillData(ESkillType.skill_spell, skillKvp.Key);
                        result.Add(new FSkillSlotData(skillKvp.Key, tierKvp.Key, dbSkill, tierKvp.Value.Grade, diff));
                    }
                }
            }

            // Aura 비교
            foreach (var skillKvp in current.CoreData.Auras)
            {
                foreach (var tierKvp in skillKvp.Value.Tiers)
                {
                    var key = (skillKvp.Key, tierKvp.Key);
                    var afterCount = tierKvp.Value.Count.Value;
                    before.AuraCounts.TryGetValue(key, out var beforeCount);
                    var diff = afterCount - beforeCount;

                    if (diff > 0)
                    {
                        var dbSkill = GameAPISkillManager.Instance.GetDBClientSkillData(ESkillType.skill_aura, skillKvp.Key);
                        result.Add(new FSkillSlotData(skillKvp.Key, tierKvp.Key, dbSkill, tierKvp.Value.Grade, diff));
                    }
                }
            }

            // Curse 비교
            foreach (var skillKvp in current.CoreData.Curses)
            {
                foreach (var tierKvp in skillKvp.Value.Tiers)
                {
                    var key = (skillKvp.Key, tierKvp.Key);
                    var afterCount = tierKvp.Value.Count.Value;
                    before.CurseCounts.TryGetValue(key, out var beforeCount);
                    var diff = afterCount - beforeCount;

                    if (diff > 0)
                    {
                        var dbSkill = GameAPISkillManager.Instance.GetDBClientSkillData(ESkillType.skill_curse, skillKvp.Key);
                        result.Add(new FSkillSlotData(skillKvp.Key, tierKvp.Key, dbSkill, tierKvp.Value.Grade, diff));
                    }
                }
            }

            // Rune 비교
            foreach (var skillKvp in current.CoreData.Runes)
            {
                foreach (var tierKvp in skillKvp.Value.Tiers)
                {
                    var key = (skillKvp.Key, tierKvp.Key);
                    var afterCount = tierKvp.Value.Count.Value;
                    before.RuneCounts.TryGetValue(key, out var beforeCount);
                    var diff = afterCount - beforeCount;

                    if (diff > 0)
                    {
                        var dbSkill = GameAPISkillManager.Instance.GetDBClientSkillData(ESkillType.skill_rune, skillKvp.Key);
                        result.Add(new FSkillSlotData(skillKvp.Key, tierKvp.Key, dbSkill, tierKvp.Value.Grade, diff));
                    }
                }
            }
        }

        #endregion

        #region Pet 스냅샷

        private PetSnapshot CreatePetSnapshot(PetData data)
        {
            var snapshot = PetSnapshot.Create();

            if (data?.CoreData == null) return snapshot;

            foreach (var petKvp in data.CoreData.Pets)
            {
                foreach (var tierKvp in petKvp.Value.Tiers)
                {
                    var key = (petKvp.Key, tierKvp.Key);
                    snapshot.Counts[key] = tierKvp.Value.Count.Value;
                }
            }

            return snapshot;
        }

        private void CalculatePetDiff(PetSnapshot before, PetData current, List<FCommonItemSlotData> result)
        {
            if (current?.CoreData == null) return;

            foreach (var petKvp in current.CoreData.Pets)
            {
                foreach (var tierKvp in petKvp.Value.Tiers)
                {
                    var key = (petKvp.Key, tierKvp.Key);
                    var afterCount = tierKvp.Value.Count.Value;
                    before.Counts.TryGetValue(key, out var beforeCount);
                    var diff = afterCount - beforeCount;

                    if (diff > 0)
                    {
                        // Pet은 CurseSkill을 통해 FPetSlotData 생성
                        var curseSkill = tierKvp.Value.CurseSkill;
                        result.Add(new FPetSlotData(petKvp.Key, curseSkill, tierKvp.Key, diff));
                    }
                }
            }
        }

        #endregion

        #region Ticket 스냅샷

        private Dictionary<ECurrencyTicket, int> CreateTicketSnapshot(TicketData data)
        {
            var snapshot = new Dictionary<ECurrencyTicket, int>();

            if (data?.CoreData == null) return snapshot;

            foreach (var kv in data.CoreData.Tickets)
            {
                snapshot[kv.Key] = kv.Value.Count.Value;
            }

            return snapshot;
        }

        private void CalculateTicketDiff(UserData currentData, List<FCommonItemSlotData> result)
        {
            if (currentData?.ticketData?.CoreData == null) return;

            foreach (var kv in currentData.ticketData.CoreData.Tickets)
            {
                ECurrencyTicket ticket = kv.Key;
                int prev = ticketSnapshot != null && ticketSnapshot.TryGetValue(ticket, out int p) ? p : 0;
                int diff = kv.Value.Count.Value - prev;
                if (diff <= 0) continue;

                var info = TicketHelper.GetTicketInfo(ticket);
                if (info == null) continue;

                ECurrency currency = info.TicketType.ToCurrency();
                result.Add(new FCurrencySlotData(currency, PXBigInt.Create((System.Numerics.BigInteger)diff), ticket));
            }
        }

        #endregion
    }
}
