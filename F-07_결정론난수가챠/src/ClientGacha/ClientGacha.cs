using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PX
{
    public class ClientGacha
    {
        public List<CommonCoreData> DoClientGacha(HashPRNG PRNG, EGacha eGacha)
        {
            // PRNG가 null이 아닌 경우에만 생성 (스킬 강화석 등은 PRNG 사용 안 함)
            PRNG?.CreatePRNG();

            GameDBClientManager.Instance.GameDB_Gacha.Gacha.MapData.TryGetValue(eGacha, out var gameDB_Gacha);
            EGachaProduct gachaProduct = gameDB_Gacha.GachaProduct;
            int productCount = gameDB_Gacha.Count.Value;

            GameAPIUserManager.Instance.userData.gachaData.CoreData.Gachas.TryGetValue(gachaProduct, out var coreData_Gacha);
            EGachaLevel gachaLevel = coreData_Gacha.Level;

            var newGachaCoreDataList = new List<CommonCoreData>();
            switch (gachaProduct)
            {
                case EGachaProduct.EquipmentWeaponAndArmour:
                case EGachaProduct.EquipmentAccessory:
                    {
                        var roller = new EquipmentGachaRoller(
                            gachaProduct,
                            GameAPIUserManager.Instance.userData.equipmentData.CoreData
                        );
                        var newList = roller.Roll(PRNG, productCount)
                            .Cast<CommonCoreData>()     // Roll() → List<CoreData_EquipmentNormal>
                            .ToList();

                        newGachaCoreDataList.AddRange(newList);
                        DoClientApplyCoreData(newList);             // InGame 반영

                        GameAPIUserManager.Instance.userData.equipmentData.CoreData.coreDataChangedDelegate?.Invoke("", ECoreDataChangeType.Updated);
                    }
                    break;

                case EGachaProduct.SkillSpellAndAura:
                case EGachaProduct.SkillRune:
                    {
                        var roller = new SkillGachaRoller(
                            gachaProduct,
                            GameAPIUserManager.Instance.userData.skillData.CoreData
                        );
                        var newList = roller.Roll(PRNG, productCount)
                            .Cast<CommonCoreData>()
                            .ToList();

                        newGachaCoreDataList.AddRange(newList);
                        DoClientApplyCoreData(newList);

                        GameAPIUserManager.Instance.userData.skillData.CoreData.coreDataChangedDelegate?.Invoke("", ECoreDataChangeType.Updated);
                    }
                    break;
                case EGachaProduct.Pet:
                    {
                        var roller = new PetGachaRoller(
                            gachaProduct,
                            GameAPIUserManager.Instance.userData.petData.CoreData
                        );
                        var newList = roller.Roll(PRNG, productCount)
                            .Cast<CommonCoreData>()
                            .ToList();

                        newGachaCoreDataList.AddRange(newList);
                        DoClientApplyCoreData(newList);

                        GameAPIUserManager.Instance.userData.petData.CoreData.coreDataChangedDelegate?.Invoke("", ECoreDataChangeType.Updated);
                    }
                    break;
                case EGachaProduct.EquipmentReinforce:
                case EGachaProduct.EquipmentReforging:
                    {
                        // 장비 강화/재련: PRNG로 tier 결정
                        ECurrency currencyType = gachaProduct == EGachaProduct.EquipmentReinforce
                            ? ECurrency.currency_equipment_reinforce
                            : ECurrency.currency_equipment_reforging;

                        var roller = new CurrencyGachaRoller(
                            currencyType,
                            gachaProduct,
                            GameAPIUserManager.Instance.userData.currencyData.CoreData,
                            GameDBClientManager.Instance.GameDB_Currency.Currency.MapData[currencyType]
                        );
                        var newList = roller.Roll(PRNG, productCount)
                            .Cast<CommonCoreData>()
                            .ToList();

                        newGachaCoreDataList.AddRange(newList);
                        DoClientApplyCoreData(newList);

                        GameAPIUserManager.Instance.userData.currencyData.CoreData.coreDataChangedDelegate?.Invoke("", ECoreDataChangeType.Updated);
                    }
                    break;

                default:
                    throw new Exception($"Unknown gacha product: {gachaProduct}");
            }

#if UNITY_EDITOR
            // PRNG를 사용하는 가챠만 로그 파일 작성
            PRNG?.WriteLogsToFile(eGacha.ToString());
#endif
            return newGachaCoreDataList;
        }

        public void DoClientApplyCoreData(IEnumerable<CommonCoreData> newDataList)
        {
            new CoreDataApplyer().Apply(newDataList);
        }
    }

    #region  GachaRoller

    // -------------------------------------------------------------------------
    //  GachaRoller. 뽑기를 수행하고 CoreDataList로 반환
    // -------------------------------------------------------------------------
    public abstract class GachaRoller<TInput, TKey, TCoreData>
    where TCoreData : CommonCoreData
    {
        // 한 번의 뽑기: 랜덤값 생성 및 로그 처리 등
        public abstract TInput RollOne(HashPRNG prng);

        // 그룹핑을 위한 Key 추출 (ex: EEquipmentNormal, (skill, tier, grade) 등)
        public abstract TKey GetGroupKey(TInput input);

        // 결과용 CoreData 생성 (이번 뽑기에서 얻은 정보와 수량)
        public abstract TCoreData CreateCoreDataForResult(TKey key, int count);
        // Currency 에서만 사용한다.
        public virtual TCoreData CreateCoreDataForResult(TKey key, PXBigInt count)
        {
            throw new NotSupportedException($"{GetType().Name} does not handle PXBigInt counts.");
        }

        // 실제 게임 데이터에 누적 반영
        public abstract void ApplyToInGameCoreData(TKey key, int count);

        // 전체 뽑기 흐름 (템플릿 메서드 패턴)
        public virtual List<TCoreData> Roll(HashPRNG prng, int count)
        {
            var grouped = new Dictionary<TKey, int>();
            for (int i = 0; i < count; ++i)
            {
                var roll = RollOne(prng);
                var gKey = GetGroupKey(roll);
                if (!grouped.ContainsKey(gKey)) grouped[gKey] = 0;
                grouped[gKey]++;
            }
            var list = new List<TCoreData>(grouped.Count);
            foreach (var p in grouped)
                list.Add(CreateCoreDataForResult(p.Key, p.Value));
            return list;
        }

        #region Roll
        public EEquipmentLargeType RollEquipmentLargeType(EGachaProduct gachaProductType, float randValue)
        {
            EEquipmentLargeType gachaLargeType;

            switch (gachaProductType)
            {
                case EGachaProduct.EquipmentWeaponAndArmour:
                    // 무기 vs 방어구 선택 비율
                    float weaponRatio = GameGlobalConfig.gacha_weaponarmourratio;
                    gachaLargeType = (randValue <= weaponRatio)
                        ? EEquipmentLargeType.weapon
                        : EEquipmentLargeType.armour;
                    break;

                case EGachaProduct.EquipmentAccessory:
                    gachaLargeType = EEquipmentLargeType.accessory;
                    break;

                default:
                    throw new System.Exception($"Invalid gachaProductType: {gachaProductType}");
            }

            return gachaLargeType;
        }

        public EEquipmentType RollEquipmentType(EEquipmentLargeType largeType, float randValue)
        {
            int minType, maxType;

            switch (largeType)
            {
                case EEquipmentLargeType.weapon:
                    // weapon 범위: sword(100) ~ staff(103)
                    minType = (int)EEquipmentType.sword;
                    maxType = (int)EEquipmentType.staff;
                    break;

                case EEquipmentLargeType.armour:
                    // armour 범위: helmet(200) ~ boots(203)
                    minType = (int)EEquipmentType.helmet;
                    maxType = (int)EEquipmentType.boots;
                    break;

                case EEquipmentLargeType.accessory:
                    // accessory 범위: amulet(300) ~ ring(302)
                    minType = (int)EEquipmentType.amulet;
                    maxType = (int)EEquipmentType.ring;
                    break;

                default:
                    throw new System.Exception($"Invalid largeType: {largeType}");
            }

            // 개수
            int rangeCount = maxType - minType + 1;
            int index = (int)System.Math.Floor(randValue * rangeCount);

            // 최종 enum
            int computedType = minType + index;
            EEquipmentType eqType = (EEquipmentType)computedType;

            // eqType이 EEquipmentType에 정의된 값인지 확인
            if (!Enum.IsDefined(typeof(EEquipmentType), eqType))
            {
                throw new System.Exception($"Invalid equipment type: randomValue={randValue}, minType={minType}, maxType={maxType}, index={index}, computedType={computedType}");
            }

            if (eqType == EEquipmentType.None)
            {
                throw new System.Exception("Rolled equipment type == None. Something went wrong.");
            }

            return eqType;
        }

        public (ETier, EGrade) RollTierAndGrade(EGachaProduct gachaProduct, EGachaLevel gachaLevel, float randValue)
        {
            GameDBClientManager.Instance.GameDB_Gacha.GachaChance.MapData.TryGetValue(gachaProduct, out var gachaChanceMap);
            if (gachaChanceMap == null || !gachaChanceMap.GachaChanceList.ContainsKey(gachaLevel))
            {
                throw new System.Exception($"No GachaChanceList found for product({gachaProduct}) / level({gachaLevel}).");
            }

            var chanceList = gachaChanceMap.GachaChanceList[gachaLevel].GachaChances;

            float accumulateChance = 0f;
            const float EPSILON = 1e-3f;

            foreach (var item in chanceList)
            {
                accumulateChance += (float)item.Chance.Value; // 0.1, 0.2, etc.

                if (System.Math.Abs(1f - accumulateChance) < EPSILON)
                {
                    accumulateChance = 1f;
                }

                if (accumulateChance >= randValue)
                {
                    return ((ETier)item.Tier, (EGrade)item.Grade);
                }
            }

            throw new System.Exception("RollGachaTierGrade: sum of chances < randValue? Unexpected overflow.");
        }

        public ESkillType RollSkillLargeType(EGachaProduct gachaProduct, float randValue)
        {
            switch (gachaProduct)
            {
                case EGachaProduct.SkillSpellAndAura:
                    float spellRatio = GameGlobalConfig.gacha_skillspellauraratio;
                    return (randValue <= spellRatio) ? ESkillType.skill_spell : ESkillType.skill_aura;

                case EGachaProduct.SkillRune:
                    return ESkillType.skill_rune;

                default:
                    throw new System.Exception($"gachaProduct is not Valid! {gachaProduct}");
            }
        }

        public ESkill RollSkill(ESkillType gachaSkillType, float randValue)
        {
            Dictionary<ESkill, GameDB_Client_Skill> skillMap;
            switch (gachaSkillType)
            {
                case ESkillType.skill_spell:
                    // 여기서 몬스터 스킬을 제외한 Spell만 골라서 skillMap 구성
                    skillMap = new Dictionary<ESkill, GameDB_Client_Skill>();
                    var allSpellMap = GameDBClientManager.Instance.GameDB_Skill.Spell.MapData;
                    foreach (var kvp in allSpellMap)
                    {
                        // kvp.Key: ESkill, kvp.Value: GameDB_Client_Skill
                        if (!kvp.Value.IsMonsterSkill.Value)
                        {
                            skillMap[kvp.Key] = kvp.Value;
                        }
                    }
                    break;
                case ESkillType.skill_aura:
                    skillMap = GameDBClientManager.Instance.GameDB_Skill.Aura.MapData;
                    break;
                case ESkillType.skill_rune:
                    skillMap = GameDBClientManager.Instance.GameDB_Skill.Rune.MapData;
                    break;
                default:
                    throw new Exception($"Invalid skilltype {gachaSkillType}");
            }

            int skillCount = skillMap.Count;
            if (skillCount == 0)
            {
                throw new Exception($"No valid skills found for {gachaSkillType} (maybe all were monster skills?)");
            }

            int skillIndex = (int)Math.Floor(randValue * skillCount);

            List<ESkill> skillKeys = new List<ESkill>(skillMap.Keys);
            skillKeys.Sort();
            ESkill skill = skillKeys[skillIndex];

            return skill;
        }

        public EPet RollPet(float randValue)
        {
            Dictionary<EPet, GameDB_Client_Pet> petMap = GameDBClientManager.Instance.GameDB_Pet.Pet.MapData;
            int petCount = petMap.Count;
            int petIndex = (int)System.Math.Floor(randValue * petCount);
            List<EPet> petKeys = new List<EPet>(petMap.Keys);
            petKeys.Sort();
            EPet pet = petKeys[petIndex];

            return pet;
        }
        #endregion

    }
    #endregion

    #region  EquipmentGachaRoller
    public class EquipmentRollResult
    {
        public EEquipmentLargeType LargeType;
        public EEquipmentType Type;
        public ETier Tier;
        public EGrade Grade;
        public EEquipmentNormal EquipKey;
    }

    public class EquipmentGachaRoller : GachaRoller<EquipmentRollResult, EEquipmentNormal, CoreData_EquipmentNormal>
    {
        protected EGachaProduct _gachaProduct;
        protected EGachaLevel _gachaLevel;
        protected CoreData_Equipments _equipmentCoreData;

        public EquipmentGachaRoller(EGachaProduct product, CoreData_Equipments equipmentCoreData)
        {
            _gachaProduct = product;
            _equipmentCoreData = equipmentCoreData;

            if (_gachaProduct != EGachaProduct.None)
                _gachaLevel = GameAPIUserManager.Instance.userData.gachaData.CoreData.Gachas[_gachaProduct].Level;
            else
                _gachaLevel = EGachaLevel.None;
        }

        public override EquipmentRollResult RollOne(HashPRNG prng)
        {
            float randVal1 = prng.NextRandom();
            EEquipmentLargeType largeType = RollEquipmentLargeType(_gachaProduct, randVal1);
            prng.LogRandAndEnum(randVal1, (int)largeType);

            float randVal2 = prng.NextRandom();
            EEquipmentType type = RollEquipmentType(largeType, randVal2);
            prng.LogRandAndEnum(randVal2, (int)type);

            float randVal3 = prng.NextRandom();
            (ETier tier, EGrade grade) = RollTierAndGrade(_gachaProduct, _gachaLevel, randVal3);
            prng.LogRandAndTwoEnums(randVal3, (int)tier, (int)grade);

            var key = (EEquipmentNormal)Enum.Parse(typeof(EEquipmentNormal), $"{largeType}_{type}_{tier}_{grade}");
            return new EquipmentRollResult
            {
                LargeType = largeType,
                Type = type,
                Tier = tier,
                Grade = grade,
                EquipKey = key
            };
        }

        public override EEquipmentNormal GetGroupKey(EquipmentRollResult input)
        {
            return input.EquipKey;
        }

        public override CoreData_EquipmentNormal CreateCoreDataForResult(EEquipmentNormal key, int count)
        {
            // 실제 장비 데이터가 이미 존재한다면, 그 구조에 맞게 생성
            if (_equipmentCoreData.NormalEquipments.TryGetValue(key, out var existing))
            {
                var newlyAcquired = new CoreData_EquipmentNormal
                {
                    Count = CryptoValueInt.Create(count),
                    LargeType = existing.LargeType,
                    Type = existing.Type,
                    Tier = existing.Tier,
                    Grade = existing.Grade,
                    IsActive = true,
                    IsEquipped = existing.IsEquipped,
                    Reinforce = CryptoValueInt.Create(existing.Reinforce.Value)
                };
                newlyAcquired.InitData();
                return newlyAcquired;
            }
            else
            {
                var splits = key.ToString().Split('_');
                EEquipmentLargeType largeType = (EEquipmentLargeType)Enum.Parse(typeof(EEquipmentLargeType), splits[0]);
                EEquipmentType type = (EEquipmentType)Enum.Parse(typeof(EEquipmentType), splits[1]);
                ETier tier = (ETier)Enum.Parse(typeof(ETier), splits[2]);
                EGrade grade = (EGrade)Enum.Parse(typeof(EGrade), splits[3]);

                var newEquip = new CoreData_EquipmentNormal
                {
                    Count = CryptoValueInt.Create(count),
                    LargeType = largeType,
                    Type = type,
                    Tier = tier,
                    Grade = grade,
                    IsActive = true,
                    IsEquipped = false,
                    Reinforce = CryptoValueInt.Create(0),
                };
                newEquip.InitData();
                return newEquip;
            }
        }

        public override void ApplyToInGameCoreData(EEquipmentNormal key, int count)
        {
            if (_equipmentCoreData.NormalEquipments.TryGetValue(key, out var existing))
            {
                existing.Count = CryptoValueInt.Create(existing.Count.Value + count);
            }
            else
            {
                var splits = key.ToString().Split('_');
                EEquipmentLargeType largeType = (EEquipmentLargeType)Enum.Parse(typeof(EEquipmentLargeType), splits[0]);
                EEquipmentType type = (EEquipmentType)Enum.Parse(typeof(EEquipmentType), splits[1]);
                ETier tier = (ETier)Enum.Parse(typeof(ETier), splits[2]);
                EGrade grade = (EGrade)Enum.Parse(typeof(EGrade), splits[3]);

                var newEquip = new CoreData_EquipmentNormal
                {
                    Count = CryptoValueInt.Create(count),
                    LargeType = largeType,
                    Type = type,
                    Tier = tier,
                    Grade = grade,
                    IsActive = true,
                    IsEquipped = false,
                    Reinforce = CryptoValueInt.Create(0),
                };
                newEquip.InitData();
                _equipmentCoreData.NormalEquipments.Add(key, newEquip);
            }
        }
    }
    #endregion

    #region  SkillGachaRoller
    public class SkillRollResult
    {
        public ESkillType SkillType;
        public ESkill Skill;
        public ETier Tier;
        public EGrade Grade;
    }

    public class SkillGachaRoller : GachaRoller<SkillRollResult, (ESkillType, ESkill, ETier, EGrade), CommonCoreData>
    {
        protected EGachaProduct _gachaProduct;
        protected EGachaLevel _gachaLevel;
        protected CoreData_Skills _skillCoreData;

        public SkillGachaRoller(EGachaProduct product, CoreData_Skills skillCoreData)
        {
            _gachaProduct = product;
            _skillCoreData = skillCoreData;

            if (_gachaProduct != EGachaProduct.None)
                _gachaLevel = GameAPIUserManager.Instance.userData.gachaData.CoreData.Gachas[_gachaProduct].Level;
            else
                _gachaLevel = EGachaLevel.None;
        }

        public override SkillRollResult RollOne(HashPRNG prng)
        {
            float randVal1 = prng.NextRandom();
            ESkillType skillType = RollSkillLargeType(_gachaProduct, randVal1);
            prng.LogRandAndEnum(randVal1, (int)skillType);

            float randVal2 = prng.NextRandom();
            ESkill skill = RollSkill(skillType, randVal2);
            prng.LogRandAndEnum(randVal2, (int)skill);

            float randVal3 = prng.NextRandom();
            (ETier tier, EGrade grade) = RollTierAndGrade(_gachaProduct, _gachaLevel, randVal3);
            prng.LogRandAndTwoEnums(randVal3, (int)tier, (int)grade);

            return new SkillRollResult
            {
                SkillType = skillType,
                Skill = skill,
                Tier = tier,
                Grade = grade
            };
        }

        public override (ESkillType, ESkill, ETier, EGrade) GetGroupKey(SkillRollResult input)
        {
            return (input.SkillType, input.Skill, input.Tier, input.Grade);
        }

        public override CommonCoreData CreateCoreDataForResult((ESkillType, ESkill, ETier, EGrade) key, int count)
        {
            var (skillType, skill, tier, grade) = key;
            switch (skillType)
            {
                case ESkillType.skill_spell:
                    return new CoreData_SkillSpell
                    {
                        Count = CryptoValueInt.Create(count),
                        SkillType = skillType,
                        Skill = skill,
                        Tier = tier,
                        Grade = grade,
                        IsActive = true,
                        IsEquipped = false,
                        IsMaster = false,
                        Reinforce = CryptoValueInt.Create(0)
                    };
                case ESkillType.skill_aura:
                    return new CoreData_SkillAura
                    {
                        Count = CryptoValueInt.Create(count),
                        SkillType = skillType,
                        Skill = skill,
                        Tier = tier,
                        Grade = grade,
                        IsActive = true,
                        IsEquipped = false,
                        IsMaster = false,
                        Reinforce = CryptoValueInt.Create(0)
                    };
                case ESkillType.skill_rune:
                    return new CoreData_SkillRune
                    {
                        Count = CryptoValueInt.Create(count),
                        SkillType = skillType,
                        Skill = skill,
                        Tier = tier,
                        Grade = grade,
                        IsActive = true,
                        IsEquipped = false,
                        IsMaster = false,
                        Reinforce = CryptoValueInt.Create(0)
                    };
                default:
                    throw new System.Exception($"Invalid skillType {skillType}");
            }
        }

        public override void ApplyToInGameCoreData((ESkillType, ESkill, ETier, EGrade) key, int count)
        {
            var (skillType, skill, tier, grade) = key;
            switch (skillType)
            {
                case ESkillType.skill_spell:
                    if (!_skillCoreData.Spells.TryGetValue(skill, out var spellTierDict))
                    {
                        spellTierDict = new CoreData_SkillSpellByTier();
                        _skillCoreData.Spells[skill] = spellTierDict;
                    }
                    if (spellTierDict.Tiers.TryGetValue(tier, out var spell))
                    {
                        spell.Count = CryptoValueInt.Create(spell.Count.Value + count);
                    }
                    else
                    {
                        spellTierDict.Tiers[tier] = new CoreData_SkillSpell
                        {
                            Count = CryptoValueInt.Create(count),
                            SkillType = skillType,
                            Skill = skill,
                            Tier = tier,
                            Grade = grade,
                            IsActive = true,
                            IsEquipped = false,
                            IsMaster = false,
                            Reinforce = CryptoValueInt.Create(0)
                        };
                    }
                    break;

                case ESkillType.skill_aura:
                    if (!_skillCoreData.Auras.TryGetValue(skill, out var auraTierDict))
                    {
                        auraTierDict = new CoreData_SkillAuraByTier();
                        _skillCoreData.Auras[skill] = auraTierDict;
                    }
                    if (auraTierDict.Tiers.TryGetValue(tier, out var aura))
                    {
                        aura.Count = CryptoValueInt.Create(aura.Count.Value + count);
                    }
                    else
                    {
                        auraTierDict.Tiers[tier] = new CoreData_SkillAura
                        {
                            Count = CryptoValueInt.Create(count),
                            SkillType = skillType,
                            Skill = skill,
                            Tier = tier,
                            Grade = grade,
                            IsActive = true,
                            IsEquipped = false,
                            IsMaster = false,
                            Reinforce = CryptoValueInt.Create(0)
                        };
                    }
                    break;

                case ESkillType.skill_rune:
                    if (!_skillCoreData.Runes.TryGetValue(skill, out var runeTierDict))
                    {
                        runeTierDict = new CoreData_SkillRuneByTier();
                        _skillCoreData.Runes[skill] = runeTierDict;
                    }
                    if (runeTierDict.Tiers.TryGetValue(tier, out var rune))
                    {
                        rune.Count = CryptoValueInt.Create(rune.Count.Value + count);
                    }
                    else
                    {
                        runeTierDict.Tiers[tier] = new CoreData_SkillRune
                        {
                            Count = CryptoValueInt.Create(count),
                            SkillType = skillType,
                            Skill = skill,
                            Tier = tier,
                            Grade = grade,
                            IsActive = true,
                            IsEquipped = false,
                            IsMaster = false,
                            Reinforce = CryptoValueInt.Create(0)
                        };
                    }
                    break;
            }
        }
    }
    #endregion

    #region  PetGachaRoller
    public class PetRollResult
    {
        public EPet Pet;
        public ETier Tier;
        public EGrade Grade;
    }

    public class PetGachaRoller : GachaRoller<PetRollResult, (EPet, ETier, EGrade), CoreData_Pet>
    {
        protected EGachaProduct _gachaProduct;
        protected EGachaLevel _gachaLevel;
        protected CoreData_Pets _petCoreData;

        public PetGachaRoller(EGachaProduct product, CoreData_Pets petCoreData)
        {
            _gachaProduct = product;
            _petCoreData = petCoreData;

            if (_gachaProduct != EGachaProduct.None)
                _gachaLevel = GameAPIUserManager.Instance.userData.gachaData.CoreData.Gachas[_gachaProduct].Level;
            else
                _gachaLevel = EGachaLevel.None;
        }

        public override PetRollResult RollOne(HashPRNG prng)
        {
            float randVal1 = prng.NextRandom();
            EPet pet = RollPet(randVal1);
            prng.LogRandAndEnum(randVal1, (int)pet);

            float randVal2 = prng.NextRandom();
            (ETier tier, EGrade grade) = RollTierAndGrade(_gachaProduct, _gachaLevel, randVal2);
            prng.LogRandAndTwoEnums(randVal2, (int)tier, (int)grade);

            // 등급 고정
            grade = EGrade.grade1;

            return new PetRollResult
            {
                Pet = pet,
                Tier = tier,
                Grade = grade
            };
        }

        public override (EPet, ETier, EGrade) GetGroupKey(PetRollResult input)
        {
            return (input.Pet, input.Tier, input.Grade);
        }

        public override CoreData_Pet CreateCoreDataForResult((EPet, ETier, EGrade) key, int count)
        {
            var (pet, tier, grade) = key;
            return new CoreData_Pet
            {
                Count = CryptoValueInt.Create(count),
                Pet = pet,
                Tier = tier,
                Grade = grade,
                IsActive = true,
                IsEquipped = false
            };
        }

        public override void ApplyToInGameCoreData((EPet, ETier, EGrade) key, int count)
        {
            var (pet, tier, grade) = key;
            if (!_petCoreData.Pets.TryGetValue(pet, out var petByTier))
            {
                petByTier = new CoreData_PetByTier();
                _petCoreData.Pets[pet] = petByTier;
            }

            if (petByTier.Tiers.TryGetValue(tier, out var existingPet))
            {
                existingPet.Count = CryptoValueInt.Create(existingPet.Count.Value + count);
            }
            else
            {
                petByTier.Tiers[tier] = new CoreData_Pet
                {
                    Count = CryptoValueInt.Create(count),
                    Pet = pet,
                    Tier = tier,
                    Grade = grade,
                    IsActive = true,
                    IsEquipped = false
                };
            }
        }
    }
    #endregion

    #region  CurrencyGachaRoller
    public class CurrencyRollResult
    {
        public ETier Tier;
        public EGrade Grade;
    }

    public class CurrencyGachaRoller : GachaRoller<CurrencyRollResult, (ETier, EGrade), CommonCoreData>
    {
        protected ECurrency _currencyType;
        protected EGachaProduct _gachaProduct;
        protected EGachaLevel _gachaLevel;
        protected CoreData_Currencies _currencyCoreData;
        protected GameDB_Client_Currency _currencyDB;

        public CurrencyGachaRoller(
            ECurrency currencyType,
            EGachaProduct product,
            CoreData_Currencies currencyCoreData,
            GameDB_Client_Currency currencyDB)
        {
            _currencyType = currencyType;
            _gachaProduct = product;
            _currencyCoreData = currencyCoreData;
            _currencyDB = currencyDB;

            if (_gachaProduct != EGachaProduct.None)
                _gachaLevel = GameAPIUserManager.Instance.userData.gachaData.CoreData.Gachas[_gachaProduct].Level;
            else
                _gachaLevel = EGachaLevel.None;
        }

        public override List<CommonCoreData> Roll(HashPRNG prng, int count)
        {
            return base.Roll(prng, count);
        }

        public override CurrencyRollResult RollOne(HashPRNG prng)
        {
            float randVal = prng.NextRandom();
            // 서버와 동일하게 tier만 결정하고 grade는 grade1로 고정
            // 서버: roll_GachaTierGrade로 tier만 사용, grade는 EGrade.grade1 고정
            (ETier tier, EGrade _) = RollTierAndGrade(_gachaProduct, _gachaLevel, randVal);
            prng.LogRandAndEnum(randVal, (int)tier);
            return new CurrencyRollResult
            {
                Tier = tier,
                Grade = EGrade.grade1
            };
        }

        public override (ETier, EGrade) GetGroupKey(CurrencyRollResult input)
        {
            return (input.Tier, input.Grade);
        }

        public override CommonCoreData CreateCoreDataForResult((ETier, EGrade) key, PXBigInt count)
        {
            return new CoreData_BigIntCurrency
            {
                CurrencyType = _currencyType,
                Count = PXBigInt.Create(count.Value)
            };
        }

        public override CommonCoreData CreateCoreDataForResult((ETier, EGrade) key, int count)
        {
            var (tier, grade) = key;
            var currencyData = new CoreData_Currency
            {
                CurrencyType = _currencyType,
                Count = CryptoValueInt.Create(count),
                HasTier = _currencyDB.HasTier.Value,
                HasGrade = _currencyDB.HasGrade.Value,
                HasConsumable = _currencyDB.Consumable.Value
            };

            if (currencyData.HasTier)
            {
                currencyData.Tiers[tier] = new CoreData_Currency_Tier
                {
                    Count = CryptoValueInt.Create(count)
                };
                if (currencyData.HasGrade)
                {
                    currencyData.Tiers[tier].Grades[grade] = new CoreData_Currency_Grade
                    {
                        Count = CryptoValueInt.Create(count)
                    };
                }
            }

            return currencyData;
        }

        public override void ApplyToInGameCoreData((ETier, EGrade) key, int count)
        {
            var (tier, grade) = key;
            if (_currencyCoreData.IsBigIntCurrency(_currencyType))
            {
                if (!_currencyCoreData.BigIntCurrencies.TryGetValue(_currencyType, out var bigData))
                {
                    bigData = new CoreData_BigIntCurrency
                    {
                        CurrencyType = _currencyType,
                        Count = PXBigInt.Create(0)
                    };
                    _currencyCoreData.BigIntCurrencies[_currencyType] = bigData;
                }
                bigData.Add(count);
            }
            else
            {
                if (!_currencyCoreData.Currencies.TryGetValue(_currencyType, out var currencyData))
                {
                    currencyData = new CoreData_Currency
                    {
                        CurrencyType = _currencyType,
                        Count = CryptoValueInt.Create(0),
                        HasTier = _currencyDB.HasTier.Value,
                        HasGrade = _currencyDB.HasGrade.Value,
                        HasConsumable = _currencyDB.Consumable.Value
                    };
                    _currencyCoreData.Currencies[_currencyType] = currencyData;
                }
                currencyData.Count = CryptoValueInt.Create(currencyData.Count.Value + count);

                if (currencyData.HasTier)
                {
                    if (!currencyData.Tiers.ContainsKey(tier))
                        currencyData.Tiers[tier] = new CoreData_Currency_Tier();

                    var tierData = currencyData.Tiers[tier];
                    tierData.Count = CryptoValueInt.Create(tierData.Count.Value + count);

                    if (currencyData.HasGrade)
                    {
                        if (!tierData.Grades.ContainsKey(grade))
                            tierData.Grades[grade] = new CoreData_Currency_Grade();

                        var gradeData = tierData.Grades[grade];
                        gradeData.Count = CryptoValueInt.Create(gradeData.Count.Value + count);
                    }
                }
            }
        }
    }
    #endregion

    // -------------------------------------------------------------------------
    //  CoreDataApplyer CoreDataList를 실제 게임의 CoreData에 반영
    // -------------------------------------------------------------------------
    #region CoreDataApplyer
    public class CoreDataApplyer
    {
        public void Apply(IEnumerable<CommonCoreData> list)
        {
            if (list == null) return;
            foreach (var cd in list)
            {
                switch (cd)
                {
                    case CoreData_EquipmentNormal eq: ApplyEquipment(eq); break;
                    case CoreData_SkillSpell spell: ApplySkillSpell(spell); break;
                    case CoreData_SkillAura aura: ApplySkillAura(aura); break;
                    case CoreData_SkillRune rune: ApplySkillRune(rune); break;
                    case CoreData_Pet pet: ApplyPet(pet); break;
                    case CoreData_Currency cur: ApplyCurrency(cur); break;
                    case CoreData_BigIntCurrency big: ApplyBigIntCurrency(big); break;
                    default:
                        Debug.LogError($"CoreDataApplyHelper: Unsupported CoreData type {cd.GetType()}");
                        break;
                }
            }
        }

        private static void ApplyEquipment(CoreData_EquipmentNormal eq)
        {
            bool isAdded = false;
            bool isUpdated = false;

            var equipCore = GameAPIUserManager.Instance.userData.equipmentData.CoreData;
            var key = (EEquipmentNormal)Enum.Parse(typeof(EEquipmentNormal),
                                                $"{eq.LargeType}_{eq.Type}_{eq.Tier}_{eq.Grade}");
            if (equipCore.NormalEquipments.TryGetValue(key, out var existing))
            {
                existing.Count = CryptoValueInt.Create(existing.Count.Value + eq.Count.Value);
                isUpdated = true;
            }
            else
            {
                var clone = new CoreData_EquipmentNormal
                {
                    Count = CryptoValueInt.Create(eq.Count.Value),
                    LargeType = eq.LargeType,
                    Type = eq.Type,
                    Tier = eq.Tier,
                    Grade = eq.Grade,
                    IsActive = eq.IsActive,
                    IsEquipped = eq.IsEquipped,
                    Reinforce = CryptoValueInt.Create(eq.Reinforce.Value)
                };
                clone.InitData();
                equipCore.NormalEquipments[key] = clone;
                isAdded = true;
            }
            if (isAdded)
            {
                GameAPIUserManager.Instance.userData.equipmentData.CoreData.coreDataChangedDelegate?.Invoke(key.ToString(), ECoreDataChangeType.Added);
            }
            if (isUpdated)
            {
                GameAPIUserManager.Instance.userData.equipmentData.CoreData.coreDataChangedDelegate?.Invoke(key.ToString(), ECoreDataChangeType.Updated);
            }
        }

        private static void ApplySkillSpell(CoreData_SkillSpell spell)
        {
            bool isAdded = false;
            bool isUpdated = false;

            var skillCore = GameAPIUserManager.Instance.userData.skillData.CoreData;
            if (!skillCore.Spells.TryGetValue(spell.Skill, out var byTier))
            {
                byTier = new CoreData_SkillSpellByTier();
                skillCore.Spells[spell.Skill] = byTier;
                isAdded = true;
            }
            if (byTier.Tiers.TryGetValue(spell.Tier, out var existing))
            {
                existing.Count = CryptoValueInt.Create(existing.Count.Value + spell.Count.Value);
                isUpdated = true;
            }
            else
            {
                var clone = new CoreData_SkillSpell
                {
                    Count = CryptoValueInt.Create(spell.Count.Value),
                    SkillType = spell.SkillType,
                    Skill = spell.Skill,
                    Tier = spell.Tier,
                    Grade = spell.Grade,
                    IsActive = spell.IsActive,
                    IsEquipped = spell.IsEquipped,
                    IsMaster = spell.IsMaster,
                    Reinforce = CryptoValueInt.Create(spell.Reinforce.Value)
                };
                byTier.Tiers[spell.Tier] = clone;
                isAdded = true;
            }

            if (isAdded)
            {
                GameAPIUserManager.Instance.userData.skillData.CoreData.coreDataChangedDelegate?.Invoke(spell.Skill.ToString(), ECoreDataChangeType.Added);
            }
            if (isUpdated)
            {
                GameAPIUserManager.Instance.userData.skillData.CoreData.coreDataChangedDelegate?.Invoke(spell.Skill.ToString(), ECoreDataChangeType.Updated);
            }
        }

        private static void ApplySkillAura(CoreData_SkillAura aura)
        {
            bool isAdded = false;
            bool isUpdated = false;

            var skillCore = GameAPIUserManager.Instance.userData.skillData.CoreData;
            if (!skillCore.Auras.TryGetValue(aura.Skill, out var byTier))
            {
                byTier = new CoreData_SkillAuraByTier();
                skillCore.Auras[aura.Skill] = byTier;
                isAdded = true;
            }
            if (byTier.Tiers.TryGetValue(aura.Tier, out var existing))
            {
                existing.Count = CryptoValueInt.Create(existing.Count.Value + aura.Count.Value);
                isUpdated = true;
            }
            else
            {
                var clone = new CoreData_SkillAura
                {
                    Count = CryptoValueInt.Create(aura.Count.Value),
                    SkillType = aura.SkillType,
                    Skill = aura.Skill,
                    Tier = aura.Tier,
                    Grade = aura.Grade,
                    IsActive = aura.IsActive,
                    IsEquipped = aura.IsEquipped,
                    IsMaster = aura.IsMaster,
                    Reinforce = CryptoValueInt.Create(aura.Reinforce.Value)
                };
                byTier.Tiers[aura.Tier] = clone;
                isAdded = true;
            }

            if (isAdded)
            {
                GameAPIUserManager.Instance.userData.skillData.CoreData.coreDataChangedDelegate?.Invoke(aura.Skill.ToString(), ECoreDataChangeType.Added);
            }
            if (isUpdated)
            {
                GameAPIUserManager.Instance.userData.skillData.CoreData.coreDataChangedDelegate?.Invoke(aura.Skill.ToString(), ECoreDataChangeType.Updated);
            }
        }

        private static void ApplySkillRune(CoreData_SkillRune rune)
        {
            bool isAdded = false;
            bool isUpdated = false;

            var skillCore = GameAPIUserManager.Instance.userData.skillData.CoreData;
            if (!skillCore.Runes.TryGetValue(rune.Skill, out var byTier))
            {
                byTier = new CoreData_SkillRuneByTier();
                skillCore.Runes[rune.Skill] = byTier;
                isAdded = true;
            }
            if (byTier.Tiers.TryGetValue(rune.Tier, out var existing))
            {
                existing.Count = CryptoValueInt.Create(existing.Count.Value + rune.Count.Value);
                isUpdated = true;
            }
            else
            {
                var clone = new CoreData_SkillRune
                {
                    Count = CryptoValueInt.Create(rune.Count.Value),
                    SkillType = rune.SkillType,
                    Skill = rune.Skill,
                    Tier = rune.Tier,
                    Grade = rune.Grade,
                    IsActive = rune.IsActive,
                    IsEquipped = rune.IsEquipped,
                    IsMaster = rune.IsMaster,
                    Reinforce = CryptoValueInt.Create(rune.Reinforce.Value)
                };
                byTier.Tiers[rune.Tier] = clone;
                isAdded = true;
            }

            if (isAdded)
            {
                GameAPIUserManager.Instance.userData.skillData.CoreData.coreDataChangedDelegate?.Invoke(rune.Skill.ToString(), ECoreDataChangeType.Added);
            }
            if (isUpdated)
            {
                GameAPIUserManager.Instance.userData.skillData.CoreData.coreDataChangedDelegate?.Invoke(rune.Skill.ToString(), ECoreDataChangeType.Updated);
            }
        }

        private static void ApplyPet(CoreData_Pet pet)
        {
            bool isAdded = false;
            bool isUpdated = false;

            var petCore = GameAPIUserManager.Instance.userData.petData.CoreData;
            if (!petCore.Pets.TryGetValue(pet.Pet, out var byTier))
            {
                byTier = new CoreData_PetByTier();
                petCore.Pets[pet.Pet] = byTier;
                isAdded = true;
            }
            if (byTier.Tiers.TryGetValue(pet.Tier, out var existing))
            {
                existing.Count = CryptoValueInt.Create(existing.Count.Value + pet.Count.Value);
                isUpdated = true;
            }
            else
            {
                var clone = new CoreData_Pet
                {
                    Count = CryptoValueInt.Create(pet.Count.Value),
                    Pet = pet.Pet,
                    Tier = pet.Tier,
                    Grade = pet.Grade,
                    IsActive = pet.IsActive,
                    IsEquipped = pet.IsEquipped
                };
                byTier.Tiers[pet.Tier] = clone;
                isAdded = true;
            }

            if (isAdded)
            {
                GameAPIUserManager.Instance.userData.petData.CoreData.coreDataChangedDelegate?.Invoke(pet.Pet.ToString(), ECoreDataChangeType.Added);
            }
            if (isUpdated)
            {
                GameAPIUserManager.Instance.userData.petData.CoreData.coreDataChangedDelegate?.Invoke(pet.Pet.ToString(), ECoreDataChangeType.Updated);
            }
        }

        private static void ApplyCurrency(CoreData_Currency cur)
        {
            var curCore = GameAPIUserManager.Instance.userData.currencyData.CoreData;

            // ─────────────────────── 신규 통화 완전히 새로 추가 ───────────────────────
            if (!curCore.Currencies.TryGetValue(cur.CurrencyType, out var existing))
            {
                existing = new CoreData_Currency
                {
                    CurrencyType = cur.CurrencyType,
                    Count = CryptoValueInt.Create(cur.Count.Value),
                    HasTier = cur.HasTier,
                    HasGrade = cur.HasGrade,
                    HasConsumable = cur.HasConsumable
                };

                // Tier / Grade 복사
                if (cur.HasTier)
                {
                    foreach (var kvp in cur.Tiers)
                    {
                        var newTier = new CoreData_Currency_Tier
                        {
                            Count = CryptoValueInt.Create(kvp.Value.Count.Value)
                        };

                        if (cur.HasGrade)
                        {
                            foreach (var g in kvp.Value.Grades)
                            {
                                newTier.Grades[g.Key] = new CoreData_Currency_Grade
                                {
                                    Count = CryptoValueInt.Create(g.Value.Count.Value)
                                };
                            }
                        }
                        existing.Tiers[kvp.Key] = newTier;
                    }
                }

                curCore.Currencies[cur.CurrencyType] = existing;

                GameAPIUserManager.Instance.userData.currencyData.CoreData.coreDataChangedDelegate?.Invoke(cur.CurrencyType.ToString(), ECoreDataChangeType.Added);

                return;
            }

            // ─────────────────────── 이미 존재하는 통화 누적 ───────────────────────
            existing.Count = CryptoValueInt.Create(existing.Count.Value + cur.Count.Value);

            bool isUpdated = false;
            if (existing.HasTier)
            {
                foreach (var kvp in cur.Tiers) // kvp.Key = Tier
                {
                    if (!existing.Tiers.TryGetValue(kvp.Key, out var exTier))
                    {
                        // Tier가 없으면 새로 생성 후 전체 카피
                        exTier = new CoreData_Currency_Tier
                        {
                            Count = CryptoValueInt.Create(kvp.Value.Count.Value)
                        };
                        existing.Tiers[kvp.Key] = exTier;
                        isUpdated = true;
                    }
                    else
                    {
                        exTier.Count = CryptoValueInt.Create(exTier.Count.Value + kvp.Value.Count.Value);
                        isUpdated = true;
                    }

                    if (existing.HasGrade)
                    {
                        foreach (var g in kvp.Value.Grades) // g.Key = Grade
                        {
                            if (!exTier.Grades.TryGetValue(g.Key, out var exGrade))
                            {
                                exGrade = new CoreData_Currency_Grade
                                {
                                    Count = CryptoValueInt.Create(g.Value.Count.Value)
                                };
                                exTier.Grades[g.Key] = exGrade;
                                isUpdated = true;
                            }
                            else
                            {
                                exGrade.Count = CryptoValueInt.Create(exGrade.Count.Value + g.Value.Count.Value);
                                isUpdated = true;
                            }
                        }
                    }
                }
            }

            if (isUpdated)
            {
                GameAPIUserManager.Instance.userData.currencyData.CoreData.coreDataChangedDelegate?.Invoke(cur.CurrencyType.ToString(), ECoreDataChangeType.Updated);
            }
        }

        private void ApplyBigIntCurrency(CoreData_BigIntCurrency big)
        {
            bool isAdded = false;
            bool isUpdated = false;

            var curCore = GameAPIUserManager.Instance.userData.currencyData.CoreData;
            if (!curCore.BigIntCurrencies.TryGetValue(big.CurrencyType, out var existing))
            {
                existing = new CoreData_BigIntCurrency
                {
                    CurrencyType = big.CurrencyType,
                    Count = PXBigInt.Create(big.Count.Value)
                };
                curCore.BigIntCurrencies[big.CurrencyType] = existing;
                isAdded = true;
            }
            else
            {
                existing.Add(big.Count.Value);
                isUpdated = true;
            }

            if (isAdded)
            {
                GameAPIUserManager.Instance.userData.currencyData.CoreData.coreDataChangedDelegate?.Invoke(big.CurrencyType.ToString(), ECoreDataChangeType.Added);
            }
            if (isUpdated)
            {
                GameAPIUserManager.Instance.userData.currencyData.CoreData.coreDataChangedDelegate?.Invoke(big.CurrencyType.ToString(), ECoreDataChangeType.Updated);
            }
        }

    }
    #endregion
}

