using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using UnityEngine;

namespace PX
{
    #region  ClientStageReward
    public class GachaRewardParameters
    {
        public HashPRNG PRNG;
        public ECurrency Currency;
        public int Count;
        public PXBigInt BigIntCount;
        public ETier Tier;
        public EGrade Grade;
    }

    public class ClientStageReward : IDisposable
    {
        private bool _disposed;
        private bool IsLoggingMode = false;

        public ClientStageReward()
        {
#if UNITY_EDITOR
            // 로컬 에뮬레이터 접속일 때만 난수 로그를 남긴다.
            IsLoggingMode = TemplateLocalData.IsEditorDebugMode;
#endif
        }
        ~ClientStageReward() { }

        // 명시적 자원 해제
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
            }
            _disposed = true;
        }

        public List<CommonCoreData> DoClientMainStageReward(HashPRNG PRNG, EStage stage, int stageLevel)
        {
            PRNG.CreatePRNG();

            // 기본, 추가 보상 파라미터 추가
            var rewardParams = new List<GachaRewardParameters>();
            AddBasicStageRewardParameters(rewardParams, PRNG, stage, stageLevel);
            AddExtraStageRewardParameters(rewardParams, PRNG, stage, stageLevel);

            // 보상 그룹화 및 합산
            var mergedRewardParams = MergeRewardParameters(rewardParams);

            // CoreData로 변환
            var coreDataList = CreateRewardToCoreData(PRNG, mergedRewardParams);

#if UNITY_EDITOR
            PRNG.WriteLogsToFile(stage.ToString() + "_" + stageLevel.ToString());
#endif
            return coreDataList;
        }

        // 스테이지 보상을 InGameCoreData에 적용
        public void DoClientApplyCoreData(IEnumerable<CommonCoreData> newDataList)
        {
            new CoreDataApplyer().Apply(newDataList);
        }

        bool RollChanceHash(HashPRNG PRNG, double chance)
        {
            float randVal = PRNG.NextRandom();          // ① 난수 추출
            bool success = randVal < chance;           // ② 성공/실패 판단

#if UNITY_EDITOR
            PRNG.LogRandAndSuccess(randVal, success);
#endif
            return success;
        }

        bool IsBigintCurrency(ECurrency currency)
        {
            return GameDBClientManager.Instance.GameDB_Currency.Currency.MapData[currency].BigInt.Value;
        }

        // 재화를 GachaProduct 으로 변환
        private EGachaProduct ConvertCurrencyToGachaProduct(ECurrency currency)
        {
            switch (currency)
            {
                case ECurrency.currency_equipment_weapon:
                case ECurrency.currency_equipment_armour:
                    return EGachaProduct.EquipmentWeaponAndArmour;
                case ECurrency.currency_equipment_accessory:
                    return EGachaProduct.EquipmentAccessory;
                case ECurrency.currency_skill_spell:
                case ECurrency.currency_skill_aura:
                    return EGachaProduct.SkillSpellAndAura;
                case ECurrency.currency_pet:
                    return EGachaProduct.Pet;
                case ECurrency.currency_skill_rune:
                    return EGachaProduct.SkillRune;
                case ECurrency.currency_equipment_reinforce:
                    return EGachaProduct.EquipmentReinforce;
                case ECurrency.currency_equipment_reforging:
                    return EGachaProduct.EquipmentReforging;
                default:
                    throw new Exception($"Invalid currency for gacha type conversion: {currency}");
            }
        }

        private bool IsGachaProduct(ECurrency currency)
        {
            return GameDBClientManager.Instance.GameDB_Currency.Currency.MapData[currency].IsGachaProduct.Value;
        }



        // 보상 파라미터 계산 함수로 분리
        private GachaRewardParameters CreateStageRewardParameter(
            HashPRNG PRNG,
            GameDB_Client_StageRewardData item,
            int stageLevel,
            int numMonsters
        )
        {
            double chance = item.Chance.GetValue(stageLevel).GetValue;

            int totalCount = 0;
            PXBigInt bigIntMV = PXBigInt.Create(0);

            switch (item.Payment)
            {
                case EStageRewardPayment.complete:
                    if (RollChanceHash(PRNG, chance))
                    {
                        if (IsBigintCurrency(item.Currency))
                        {
                            bigIntMV = PXBigInt.Create(item.Count.GetValue(stageLevel).GetValue);
                        }
                        else
                        {
                            totalCount = (int)item.Count.GetValue(stageLevel).GetValue;
                        }
                    }
                    break;

                case EStageRewardPayment.perkill:
                    if (chance >= 1.0)
                    {
                        if (IsBigintCurrency(item.Currency))
                        {
                            bigIntMV = PXBigInt.Create(item.Count.GetValue(stageLevel).GetValue * numMonsters);
                        }
                        else
                        {
                            totalCount = (int)item.Count.GetValue(stageLevel).GetValue * numMonsters;
                        }
                    }
                    else
                    {
                        // 이항 분포 샘플링 (2회 PRNG 호출) - 서버와 동일한 로직
                        float rand1 = PRNG.NextRandom();
                        float rand2 = PRNG.NextRandom();

#if UNITY_EDITOR
                        PRNG.LogRandAndEnum(rand1, 0);
                        PRNG.LogRandAndEnum(rand2, 0);
#endif

                        int successCount = GameUtility.SampleBinomial(numMonsters, chance, rand1, rand2);

                        if (IsBigintCurrency(item.Currency))
                        {
                            bigIntMV = PXBigInt.Create(successCount);
                        }
                        else
                        {
                            totalCount = successCount;
                        }
                    }
                    break;

                default:
                    throw new Exception($"Not Defined Reward Payment: {item.Payment.ToString()}");
            }

            return new GachaRewardParameters()
            {
                PRNG = PRNG,
                Currency = item.Currency,
                Count = totalCount,
                BigIntCount = bigIntMV,
                Tier = item.Tier,
                Grade = item.Grade
            };
        }

        private void AddBasicStageRewardParameters(List<GachaRewardParameters> rewardParams, HashPRNG PRNG, EStage stage, int stageLevel)
        {
            var stageRewardData = GameDBClientManager.Instance.GameDB_Stage.StageReward.MapData[stage];
            int numMonsters = GetMonsterCount(stage);

            // 정렬된 리스트로 순회 (Currency -> Tier -> Grade 순) - 서버와 동일한 순서 보장
            var sortedRewards = stageRewardData.StageRewardDatas
                .OrderBy(r => (int)r.Currency)
                .ThenBy(r => (int)r.Tier)
                .ThenBy(r => GameUtility.ConvertGradeToIndex(r.Grade))
                .ToList();

            foreach (var item in sortedRewards)
            {
                var reward = CreateStageRewardParameter(PRNG, item, stageLevel, numMonsters);
                rewardParams.Add(reward);
            }
        }

        private void AddExtraStageRewardParameters(List<GachaRewardParameters> rewardParams, HashPRNG PRNG, EStage stage, int stageLevel)
        {
            EStageMain eStageMain = StageLevelToStageMainIndex(stageLevel);
            var extraRewardData = GameDBClientManager.Instance.GameDB_Stage.StageReward_Main.MapData[eStageMain];
            int numMonsters = GetMonsterCount(stage);

            // 정렬된 리스트로 순회 (Currency -> Tier -> Grade 순) - 서버와 동일한 순서 보장
            var sortedRewards = extraRewardData.StageRewardDatas
                .OrderBy(r => (int)r.Currency)
                .ThenBy(r => (int)r.Tier)
                .ThenBy(r => GameUtility.ConvertGradeToIndex(r.Grade))
                .ToList();

            foreach (var item in sortedRewards)
            {
                var reward = CreateStageRewardParameter(PRNG, item, stageLevel, numMonsters);
                rewardParams.Add(reward);
            }
        }

        public EStageMain StageLevelToStageMainIndex(int stage_level)
        {
            int mapKey = Mathf.FloorToInt(stage_level / (GameGlobalConfig.num_stage_per_area + 1)) + 1;
            EStageMain retKey = (EStageMain)mapKey;

            // key가 Enum의 범위 내에 있는지 검사
            GameUtility.ValidateUnionRange(retKey);

            return retKey;
        }


        private int GetMonsterCount(EStage stage)
        {
            return GameDBClientManager.Instance.GameDB_Stage.Stage.MapData[stage].AllMonsterCount.Value;
        }

        private List<GachaRewardParameters> MergeRewardParameters(List<GachaRewardParameters> rewardParams)
        {
            // 1) 처음부터 Count == 0 이면서 BigIntCount == 0 인 항목은 필터링.
            //    (BigIntCount 가 null 일 가능성도 가정해 null 체크 포함)
            var filtered = rewardParams
                .Where(r => r.Count != 0 || r.BigIntCount.Value != 0)
                .ToList();

            var groupedRewards = filtered
                .GroupBy(r => new { r.Currency, r.Tier, r.Grade });

            // 그룹화 결과를 정렬하여 순회 (Currency -> Tier -> Grade 순) - 서버와 동일한 순서 보장
            var sortedGroups = groupedRewards
                .OrderBy(g => (int)g.Key.Currency)
                .ThenBy(g => (int)g.Key.Tier)
                .ThenBy(g => GameUtility.ConvertGradeToIndex(g.Key.Grade));

            var mergedParams = new List<GachaRewardParameters>();

            foreach (var group in sortedGroups)
            {
                int totalCount = group.Sum(r => r.Count);

                PXBigInt totalBigInt = PXBigInt.Create(0);
                foreach (var r in group)
                {
                    if (r.BigIntCount.Value != 0)
                        totalBigInt.Add(r.BigIntCount.Value);
                }

                // 2) 병합 결과도 둘 다 0이면 버린다.
                if (totalCount == 0 && totalBigInt.Value == 0)
                    continue;

                var first = group.First();   // 공통 메타데이터용

                // 경험치/골드 획득 증가 mod 적용
                totalBigInt = ApplyGainModifiers(first.Currency, ref totalCount, totalBigInt);

                mergedParams.Add(new GachaRewardParameters
                {
                    PRNG = first.PRNG,
                    Currency = first.Currency,
                    Count = totalCount,
                    BigIntCount = totalBigInt,
                    Tier = first.Tier,
                    Grade = first.Grade
                });
            }
            return mergedParams;
        }

        // 서버가 Stage_Enter에서 계산한 MOD 스냅샷 (외부에서 주입, 소수 배율 50% → 0.5)
        private double _modXp;
        private double _modGold;
        private double _modRewardQty;

        // 서버 MOD 스냅샷 설정 (Stage_Enter 응답에서 받은 값)
        public void SetModSnapshot(double modXp, double modGold, double modRewardQty)
        {
            _modXp = modXp;
            _modGold = modGold;
            _modRewardQty = modRewardQty;
        }

        // 탑 클리어 보상 획득량 증가 mod 적용
        // 서버가 Enter 시점에 확정한 MOD 스냅샷 사용
        // 스냅샷(StageMod*)은 퍼센트 원본이 아니라 이미 변환된 소수 배율이다.
        private PXBigInt ApplyGainModifiers(ECurrency currency, ref int count, PXBigInt bigIntCount)
        {
            double modGainRate = 0;

            switch (currency)
            {
                case ECurrency.currency_xp:
                    modGainRate = _modXp;
                    break;
                case ECurrency.currency_gold:
                    modGainRate = _modGold;
                    break;
                default:
                    modGainRate = _modRewardQty;
                    break;
            }

            if (modGainRate <= 0)
                return bigIntCount;

            // 계산: 최종값 = 기본값 * (1 + modGainRate)
            // modGainRate는 이미 소수 배율(50% → 0.5)이므로 여기서 추가 변환하지 않는다.
            double multiplier = 1.0 + modGainRate;

            // BigInt 재화인 경우 (경험치, 골드) - 서버와 동일하게 Math.Floor 사용
            if (bigIntCount.Value != 0)
            {
                double newValue = Math.Floor((double)bigIntCount.Value * multiplier);
                var result = PXBigInt.Create((double)newValue);
                return result;
            }
            // 일반 재화인 경우
            else if (count > 0)
            {
                count = (int)(count * multiplier);
            }

            return bigIntCount;
        }

        // 경험치/골드 획득 증가 mod 동적 합산 (진단/폴백용)
        // GameDB의 Nebula/AwakenElement Stat은 FLOAT_PER라 GetValue가 퍼센트를 소수로 변환한다.
        // 따라서 반환값은 퍼센트(50)가 아니라 소수 배율(0.5)이다.
        private double GetTotalGainModifier(EMod modType)
        {
            double nebulaMod = GetNebulaModValue(modType);
            double awakenMod = GetAwakenElementModValue(modType);
            return nebulaMod + awakenMod;
        }

        // Nebula에서 mod 값 수집 (광고/VIP 조건부)
        private double GetNebulaModValue(EMod modType)
        {
            var nebulaData = GameAPIUserManager.Instance.userData.nebulaData;
            bool isVipAdRemove = GameAPIUserManager.Instance.userData.productData.CoreData.IsVIP_AdRemove();

            foreach (var nebula in nebulaData.CoreData.Nebulas)
            {
                bool isActive = isVipAdRemove || IsNebulaAdBoostActive(nebula.Value);
                if (isActive && nebula.Value.Level.Value > 0)
                {
                    if (GameDBClientManager.Instance.GameDB_Nebula.Nebula.MapData.TryGetValue(
                        nebula.Key, out var client_Nebula))
                    {
                        if (client_Nebula.Mod == modType)
                        {
                            double modValue = client_Nebula.Stat.GetValue(nebula.Value.Level.Value).GetValue;
                            return modValue;
                        }
                    }
                }
            }
            return 0;
        }

        // Awaken Element에서 mod 값 수집 (레벨 기반, 항상 활성화)
        private double GetAwakenElementModValue(EMod modType)
        {
            var awakenData = GameAPIUserManager.Instance.userData.playerData.CoreData.Awaken;

            foreach (var element in awakenData.AwakenModLevels)
            {
                if (element.Value.Value > 0)
                {
                    if (GameDBClientManager.Instance.GameDB_Awaken.AwakenElement.MapData.TryGetValue(
                        element.Key, out var client_Element))
                    {
                        if (client_Element.Mod == modType)
                        {
                            double modValue = client_Element.Stat.GetValue(element.Value.Value).GetValue;
                            return modValue;
                        }
                    }
                }
            }
            return 0;
        }

        private bool IsNebulaAdBoostActive(CoreData_Nebula nebula)
        {
            if (string.IsNullOrEmpty(nebula.AdBoostExpirationTime)) return false;
            if (System.DateTime.TryParse(nebula.AdBoostExpirationTime,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out System.DateTime expirationTime))
            {
                return System.DateTime.UtcNow < expirationTime;
            }
            return false;
        }

        private List<CommonCoreData> CreateRewardToCoreData(HashPRNG PRNG, List<GachaRewardParameters> rewardParams)
        {
            var newCoreDataList = new List<CommonCoreData>();

            foreach (var reward in rewardParams)
            {
                bool isGachaProduct = IsGachaProduct(reward.Currency);
                EGachaProduct gachaProduct = EGachaProduct.None;
                if (isGachaProduct)
                    gachaProduct = ConvertCurrencyToGachaProduct(reward.Currency);

                switch (reward.Currency)
                {
                    case ECurrency.currency_equipment_weapon:
                    case ECurrency.currency_equipment_armour:
                    case ECurrency.currency_equipment_accessory:
                        {
                            var roller = new StageEquipmentRewardRoller(
                                reward,
                                gachaProduct,
                                GameAPIUserManager.Instance.userData.equipmentData.CoreData
                            );
                            var coreDataList = roller.Roll(PRNG, reward.Count);
                            newCoreDataList.AddRange(coreDataList);
                            break;
                        }
                    case ECurrency.currency_skill_spell:
                    case ECurrency.currency_skill_aura:
                    case ECurrency.currency_skill_rune:
                        {
                            var roller = new StageSkillRewardRoller(
                                reward,
                                gachaProduct,
                                GameAPIUserManager.Instance.userData.skillData.CoreData
                            );
                            var coreDataList = roller.Roll(PRNG, reward.Count);
                            newCoreDataList.AddRange(coreDataList);
                            break;
                        }

                    case ECurrency.currency_pet:
                        {
                            var roller = new StagePetRewardRoller(
                                reward,
                                gachaProduct,
                                GameAPIUserManager.Instance.userData.petData.CoreData
                            );
                            var coreDataList = roller.Roll(PRNG, reward.Count);
                            newCoreDataList.AddRange(coreDataList);
                            break;
                        }

                    // 그 외 재화(골드, 경험치 등)
                    default:
                        {
                            VerifyStageRewardCurrency(reward.Currency);

                            var roller = new StageCurrencyRewardRoller(
                                reward,
                                gachaProduct,
                                GameAPIUserManager.Instance.userData.currencyData.CoreData,
                                GameDBClientManager.Instance.GameDB_Currency.Currency.MapData[reward.Currency]
                            );
                            var coreDataList = roller.Roll(PRNG, reward.Count);
                            newCoreDataList.AddRange(coreDataList);
                            break;
                        }
                }
            }

            return newCoreDataList;
        }

        private bool VerifyStageRewardCurrency(ECurrency currency)
        {
            switch (currency)
            {
                case ECurrency.currency_equipment_reinforce:
                case ECurrency.currency_equipment_reforging:
                case ECurrency.currency_skill_reinforce:
                case ECurrency.currency_awakening_stone:
                case ECurrency.currency_neblua_offering_point:
                case ECurrency.currency_redsoulstone:
                case ECurrency.currency_diamond:
                case ECurrency.currency_gold:
                case ECurrency.currency_xp:
                case ECurrency.currency_ticket_rift:
                case ECurrency.currency_ticket_greatrift:
                case ECurrency.currency_ticket_bossrush:
                case ECurrency.currency_ticket_corruption:
                case ECurrency.currency_ticket_incursion:
                case ECurrency.currency_ticket_invasion:
                    return true;
                default:
                    throw new Exception($"VerifyStageRewardCurrency(): Currency({currency}) is not valid");
            }
        }
    }
    #endregion

    #region StageEquipmentRewardRoller
    public class StageEquipmentRewardRoller : EquipmentGachaRoller
    {
        private readonly ECurrency _currency;
        private readonly ETier _tier;
        private readonly EGrade _grade;

        public StageEquipmentRewardRoller(
            GachaRewardParameters reward,
            EGachaProduct gachaProduct,
            CoreData_Equipments equipmentCoreData)
            : base(gachaProduct, equipmentCoreData)
        {
            _currency = reward.Currency;
            _tier = reward.Tier;
            _grade = reward.Grade;
            _equipmentCoreData = equipmentCoreData;
        }

        public class EquipmentRollParam
        {
            public EEquipmentType Type;
            public ETier Tier;
            public EGrade Grade;
            public EEquipmentNormal EquipKey;
        }

        private EEquipmentLargeType GetEquipmentLargeTypeByCurrency(ECurrency currency)
        {
            switch (currency)
            {
                case ECurrency.currency_equipment_weapon:
                    return EEquipmentLargeType.weapon;
                case ECurrency.currency_equipment_armour:
                    return EEquipmentLargeType.armour;
                case ECurrency.currency_equipment_accessory:
                    return EEquipmentLargeType.accessory;
                default:
                    throw new Exception($"Invalid currency for equipment large type conversion: {currency}");
            }
        }

        public override EquipmentRollResult RollOne(HashPRNG prng)
        {
            // largeType은 Currency에 따라 정해진다.
            var largeType = GetEquipmentLargeTypeByCurrency(_currency);

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
                Tier = _tier != ETier.None ? _tier : tier,
                Grade = _grade != EGrade.None ? _grade : grade,
                EquipKey = key,
            };
        }
    }
    #endregion

    #region StageSkillRewardRoller
    public class StageSkillRewardRoller : SkillGachaRoller
    {
        private readonly ECurrency _currency;
        private readonly ETier _tier;
        private readonly EGrade _grade;

        public StageSkillRewardRoller(
            GachaRewardParameters reward,
            EGachaProduct gachaProduct,
            CoreData_Skills skillCoreData)
            : base(gachaProduct, skillCoreData)
        {
            _currency = reward.Currency;
            _tier = reward.Tier;
            _grade = reward.Grade;
            _skillCoreData = skillCoreData;
        }

        private ESkillType GetSkillTypeByCurrency(ECurrency currency)
        {
            switch (currency)
            {
                case ECurrency.currency_skill_spell:
                    return ESkillType.skill_spell;
                case ECurrency.currency_skill_aura:
                    return ESkillType.skill_aura;
                case ECurrency.currency_skill_rune:
                    return ESkillType.skill_rune;
                default:
                    throw new Exception($"getSkillTypeByCurrency: {currency} is not kind of skill");
            }
        }

        public override SkillRollResult RollOne(HashPRNG prng)
        {
            var skillType = GetSkillTypeByCurrency(_currency);

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
                Tier = _tier != ETier.None ? _tier : tier,
                Grade = _grade != EGrade.None ? _grade : grade,
            };
        }
    }
    #endregion

    #region StagePetRewardRoller
    public class StagePetRewardRoller : PetGachaRoller
    {
        private readonly ECurrency _currency;
        private readonly ETier _tier;
        private readonly EGrade _grade;

        public StagePetRewardRoller(
            GachaRewardParameters reward,
            EGachaProduct gachaProduct,
            CoreData_Pets petCoreData)
            : base(gachaProduct, petCoreData)
        {
            _currency = reward.Currency;
            _tier = reward.Tier;
            _grade = reward.Grade;
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
                Tier = _tier != ETier.None ? _tier : tier,
                Grade = _grade != EGrade.None ? _grade : grade,
            };
        }
    }
    #endregion

    #region StageCurrencyRewardRoller
    public class StageCurrencyRewardRoller : CurrencyGachaRoller
    {
        private readonly GachaRewardParameters _reward;

        public StageCurrencyRewardRoller(
            GachaRewardParameters reward,
            EGachaProduct gachaProduct,
            CoreData_Currencies currencyCoreData,
            GameDB_Client_Currency currencyDB)
            : base(reward.Currency, gachaProduct, currencyCoreData, currencyDB)
        {
            _reward = reward;
        }

        public override List<CommonCoreData> Roll(HashPRNG prng, int count)
        {
            if (_gachaProduct == EGachaProduct.None)
            {
                var fixedKey = (ETier.common, EGrade.grade1);

                if (_currencyCoreData.IsBigIntCurrency(_currencyType))
                {
                    return new List<CommonCoreData>()
                    {
                        CreateCoreDataForResult(fixedKey, _reward.BigIntCount)
                    };
                }
                else
                {
                    return new List<CommonCoreData>()
                    {
                        CreateCoreDataForResult(fixedKey, count)
                    };
                }
            }

            return base.Roll(prng, count);
        }

        public override CurrencyRollResult RollOne(HashPRNG prng)
        {
            if (_gachaProduct == EGachaProduct.None)
            {
                return new CurrencyRollResult
                {
                    Tier = ETier.common,
                    Grade = EGrade.grade1,
                };
            }

            float randVal = prng.NextRandom();
            (ETier tier, EGrade grade) = RollTierAndGrade(_gachaProduct, _gachaLevel, randVal);
            prng.LogRandAndTwoEnums(randVal, (int)tier, (int)grade);

            return new CurrencyRollResult
            {
                Tier = _reward.Tier != ETier.None ? _reward.Tier : tier,
                Grade = _reward.Grade != EGrade.None ? _reward.Grade : grade,
            };
        }
    }
    #endregion
}
