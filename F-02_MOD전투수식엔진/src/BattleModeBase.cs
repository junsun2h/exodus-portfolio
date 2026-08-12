using Firebase.Messaging;
using PX;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public abstract class FBattleModeBase : FContextBase
{
    public class FRewardCacheDataInfo
    {
        public int AllGroupMonsterCount { get; private set; }
        public List<FCommonItemSlotData> rewardCoreDatas { get; private set; }

        public Dictionary<ECurrency, PXBigInt> currencyPerDic { get; private set; }
        public Dictionary<ECurrency, PXBigInt> currencyRemainderDic { get; private set; }
        public Dictionary<int, Dictionary<ECurrency, int>> currencyIndexDic { get; private set; }
        public Dictionary<int, PXBigInt> goldIndexDic { get; private set; }  // 골드 전용 (BigInt 지원)
        public Dictionary<int, FEquipmentSlotData> equipmentSlotDataDic { get; private set; }
        public Dictionary<int, FSkillSlotData> skillSlotDataDic { get; private set; }
        public Dictionary<int, FPetSlotData> petSlotDataDic { get; private set; }
        public Dictionary<int, FRuneSlotData> runeSlotDataDic { get; private set; }

        // 성능 최적화: 보상 분배용 캐시된 임시 리스트 (매번 new 대신 재사용)
        private readonly List<FCurrencySlotData> _tempCurrencySlotDataList = new List<FCurrencySlotData>();
        private readonly List<FEquipmentSlotData> _tempEquipmentSlotDataList = new List<FEquipmentSlotData>();
        private readonly List<FPetSlotData> _tempPetSlotDataList = new List<FPetSlotData>();
        private readonly List<FRuneSlotData> _tempRuneSlotDataList = new List<FRuneSlotData>();
        private readonly List<FSkillSlotData> _tempSkillSlotDataList = new List<FSkillSlotData>();

        public FRewardCacheDataInfo()
        {
            AllGroupMonsterCount = 0;
            rewardCoreDatas = null;
            currencyPerDic = new Dictionary<ECurrency, PXBigInt>();
            currencyRemainderDic = new Dictionary<ECurrency, PXBigInt>();
            currencyIndexDic = new Dictionary<int, Dictionary<ECurrency, int>>();
            goldIndexDic = new Dictionary<int, PXBigInt>();
            equipmentSlotDataDic = new Dictionary<int, FEquipmentSlotData>();
            skillSlotDataDic = new Dictionary<int, FSkillSlotData>();
            petSlotDataDic = new Dictionary<int, FPetSlotData>();
            runeSlotDataDic = new Dictionary<int, FRuneSlotData>();
        }

        public List<FCurrencySlotData> GetCurrencySlotRewardDataList(CommonCoreData InCurrencyCoreData)
        {
            ECurrency CheckCurrency = ECurrency.None;
            ETier CheckTier = ETier.None;
            PXBigInt CheckCount = PXBigInt.Create(0);

            List<FCurrencySlotData> currencySlotDataList = new List<FCurrencySlotData>();

            if (InCurrencyCoreData is CoreData_BigIntCurrency bigIntCurrency)
            {
                CheckCount = bigIntCurrency.Count;
                CheckCurrency = bigIntCurrency.CurrencyType;
                CheckTier = ETier.None;

                currencySlotDataList.Add(new FCurrencySlotData(CheckCurrency, CheckCount, CheckTier));
            }
            else if (InCurrencyCoreData is CoreData_Currency currency)
            {
                if (currency.HasTier)
                {
                    foreach (var currencyTier in currency.Tiers)
                    {
                        if (currencyTier.Value.Count.Value > 0)
                        {
                            CheckTier = currencyTier.Key;
                            CheckCount = PXBigInt.Create(currencyTier.Value.Count.Value);
                            CheckCurrency = currency.CurrencyType;
                            currencySlotDataList.Add(new FCurrencySlotData(CheckCurrency, CheckCount, CheckTier));
                        }
                    }
                }
                else
                {
                    CheckCount = PXBigInt.Create(currency.Count.Value);
                    CheckCurrency = currency.CurrencyType;
                    currencySlotDataList.Add(new FCurrencySlotData(CheckCurrency, CheckCount, CheckTier));

                }
            }


            return currencySlotDataList;
        }

        public void SetRewardDataInfo(List<CommonCoreData> InRewardCoreDatas)
        {
            rewardCoreDatas = new List<FCommonItemSlotData>();

            //보상 작업중, 보상 데이터 모두 별게로 전달받음

            foreach (var entry in InRewardCoreDatas)
            {
                if (entry is CoreData_Currency)
                {
                    var currencySlotDataList = GetCurrencySlotRewardDataList(entry);
                    rewardCoreDatas.AddRange(currencySlotDataList);
                }
                else if (entry is CoreData_BigIntCurrency)
                {
                    CoreData_BigIntCurrency bigIntCurrencyData = entry as CoreData_BigIntCurrency;
                    FCurrencySlotData currencySlotData = new FCurrencySlotData(bigIntCurrencyData.CurrencyType, bigIntCurrencyData);
                    rewardCoreDatas.Add(currencySlotData);
                }
                else if (entry is CoreData_EquipmentNormal equipmentNormalData)
                {
                    FEquipmentSlotData equipmentSlotData = new FEquipmentSlotData(equipmentNormalData.NormalName, equipmentNormalData);
                    rewardCoreDatas.Add(equipmentSlotData);
                }
                else if (entry is CoreData_EquipmentMythic equipmentMythicData)
                {
                    FEquipmentSlotData equipmentSlotData = new FEquipmentSlotData(equipmentMythicData.MythicName, equipmentMythicData);
                    rewardCoreDatas.Add(equipmentSlotData);
                }
                else if (entry is CoreData_Skill skillData)
                {
                    FSkillSlotData skillSlotData = new FSkillSlotData(skillData);
                    rewardCoreDatas.Add(skillSlotData);
                }
                else if (entry is CoreData_Pet petData)
                {
                    FPetSlotData petSlotData = new FPetSlotData(petData.Pet, petData.CurseSkill, petData.Tier, petData.Count.Value);
                    rewardCoreDatas.Add(petSlotData);
                }
                else if (entry is CoreData_SkillRune skillRuneData)
                {
                    FRuneSlotData runeSlotData = new FRuneSlotData(ESkillRune_SlotType.AllRune, skillRuneData);
                    rewardCoreDatas.Add(runeSlotData);
                }
                else
                {
                    Debug.LogError("error, entry is not CoreData_Currency or CoreData_BigIntCurrency or CoreData_EquipmentNormal or CoreData_EquipmentMythic");
                }
            }
        }

        public void SetAllGroupMonsterCount(int InAllGroupMonsterCount)
        {
            if (rewardCoreDatas == null)
                return;

            AllGroupMonsterCount = InAllGroupMonsterCount;
            equipmentSlotDataDic.Clear();
            currencyPerDic.Clear();
            currencyRemainderDic.Clear();
            currencyIndexDic.Clear();
            goldIndexDic.Clear();
            skillSlotDataDic.Clear();
            petSlotDataDic.Clear();
            runeSlotDataDic.Clear();

            // 성능 최적화: 캐시된 리스트 재사용 (매번 new 대신)
            _tempCurrencySlotDataList.Clear();
            _tempEquipmentSlotDataList.Clear();
            _tempPetSlotDataList.Clear();
            _tempRuneSlotDataList.Clear();
            _tempSkillSlotDataList.Clear();

            // 보상 데이터를 재화와 장비로 분리
            foreach (var rewardData in rewardCoreDatas)
            {
                if (rewardData is FCurrencySlotData currencySlotData)
                {
                    _tempCurrencySlotDataList.Add(currencySlotData);
                }
                else if (rewardData is FEquipmentSlotData equipmentSlotData)
                {
                    _tempEquipmentSlotDataList.Add(equipmentSlotData);
                }
                else if (rewardData is FRuneSlotData runeSlotData)
                {
                    _tempRuneSlotDataList.Add(runeSlotData);
                }
                else if (rewardData is FPetSlotData petSlotData)
                {
                    _tempPetSlotDataList.Add(petSlotData);
                }
                else if (rewardData is FSkillSlotData skillSlotData)
                {
                    _tempSkillSlotDataList.Add(skillSlotData);
                }
                else
                {
                    Debug.LogError("error, rewardData is not FCurrencySlotData or FEquipmentSlotData");
                }
            }

            // 재화 보상 분배: 각 재화 타입별로 몬스터 수만큼 분배
            try
            {
                DistributeCurrencyRewards(_tempCurrencySlotDataList, InAllGroupMonsterCount);
            }
            catch (Exception e)
            {
                Debug.LogError($"DistributeCurrencyRewards error = {e}");
            }
            try
            {
                // 장비 보상 분배: 적절한 몬스터 인덱스에 균등 분배
                DistributeEquipmentRewards(_tempEquipmentSlotDataList, InAllGroupMonsterCount);
            }
            catch (Exception e)
            {
                Debug.LogError($"DistributeEquipmentRewards error = {e}");
            }
            try
            {
                // 스킬, 룬, 펫 보상 분배: 적절한 몬스터 인덱스에 균등 분배
                DistributeSkillRewards(_tempSkillSlotDataList, InAllGroupMonsterCount);

            }
            catch (Exception e)
            {
                Debug.LogError($"DistributeSkillRewards error = {e}");
            }
            try
            {
                DistributeRuneRewards(_tempRuneSlotDataList, InAllGroupMonsterCount);
            }
            catch (Exception e)
            {
                Debug.LogError($"DistributeRuneRewards error = {e}");
            }
            try
            {
                DistributePetRewards(_tempPetSlotDataList, InAllGroupMonsterCount);
            }
            catch (Exception e)
            {
                Debug.LogError($"DistributePetRewards error = {e}");
            }

#if UNITY_EDITOR
            // 에디터에서 보상 분배 검증
            ValidateRewardDistribution();
#endif
        }

        /// <summary>
        /// 재화 보상 분배
        /// - Gold(currency_gold) 또는 큰 값의 재화: 모든 몬스터에게 균등 분배 (currencyPerDic 사용)
        /// - 작은 값의 재화: 특정 몬스터에만 분배 (currencyIndexDic 사용, 장비와 동일한 방식)
        /// - XP(currency_xp): 몬스터별 분배 제외 (전투 종료 후 일괄 지급)
        /// </summary>
        private void DistributeCurrencyRewards(List<FCurrencySlotData> currencySlotDataList, int monsterCount)
        {
            foreach (var currencyData in currencySlotDataList)
            {
                ECurrency currencyType = currencyData.Currency;
                PXBigInt totalAmount = currencyData.Count;

                // XP는 몬스터별 분배에서 제외 (전투 종료 후 일괄 지급)
                if (currencyType == ECurrency.currency_xp)
                {
                    continue; // 분배 건너뛰기
                }

                // Gold는 Config 빈도에 따라 합산 드랍 (이펙트/UI 과부하 방지)
                if (currencyType == ECurrency.currency_gold)
                {
                    // 골드는 goldIndexDic에 합산해서 드랍 (N마리당 1번, PXBigInt로 정확한 값 유지)
                    DistributeGoldByGroupIndex(totalAmount, monsterCount, GameClientPlayConfig.Instance.stage.goldDropFrequency);
                }
                // 값이 int 범위를 초과하는 재화는 기존 방식 유지 (currencyPerDic)
                else if (totalAmount.Value > int.MaxValue)
                {
                    // currencyPerDic에 총량 저장 (CheckCurrencyReward에서 정확히 분배)
                    if (currencyPerDic.ContainsKey(currencyType))
                    {
                        // 동일한 재화 타입이 여러 개 있다면 합계
                        currencyPerDic[currencyType].Add(totalAmount.Value);
                    }
                    else
                    {
                        // 새로운 재화 타입 추가
                        currencyPerDic[currencyType] = PXBigInt.Create(totalAmount.Value);
                    }

                    // 나머지 값 계산 (나눗셈 소수점 손실 방지)
                    PXBigInt avgAmount = PXBigInt.Create(totalAmount.Value / monsterCount);
                    PXBigInt distributedTotal = PXBigInt.Create(avgAmount.Value * monsterCount);
                    PXBigInt remainder = PXBigInt.Create(totalAmount.Value - distributedTotal.Value);

                    if (remainder.Value > 0)
                    {
                        currencyRemainderDic[currencyType] = remainder;
                    }
                }
                // 나머지 재화는 특정 몬스터에만 분배 (장비 방식)
                else
                {
                    DistributeCurrencyByIndex(currencyType, totalAmount, monsterCount);
                }
            }
        }

        /// <summary>
        /// 장비 보상을 적절한 몬스터 인덱스에 균등 분배하여 equipmentSlotDataDic에 저장
        /// </summary>
        private void DistributeEquipmentRewards(List<FEquipmentSlotData> equipmentSlotDataList, int monsterCount)
        {
            if (equipmentSlotDataList.Count == 0)
                return;

            // 장비가 지급될 몬스터 인덱스들을 균등하게 분배
            // 예: 100마리 몬스터, 3개 장비 → 25, 50, 75번째 몬스터에게 지급
            var config = GameClientPlayConfig.Instance.stage;
            int equipStartIndex = Mathf.Max(config.itemDropMinIndex, (int)(monsterCount * config.itemDropStartPercent));
            int equipMaxIndex = monsterCount;
            int equipmentCount = equipmentSlotDataList.Count;

            var distributedIndices = GameBattleUtilityManager.Instance.GenerateEvenlyDistributedNumbers(
                equipmentCount, equipStartIndex, equipMaxIndex);

            // 생성된 인덱스에 장비 배치
            for (int i = 0; i < equipmentCount && i < distributedIndices.Length; i++)
            {
                int monsterIndex = distributedIndices[i];
                FEquipmentSlotData equipment = equipmentSlotDataList[i];

                equipmentSlotDataDic[monsterIndex] = equipment;

            }
        }

        /// <summary>
        /// 골드를 2~3마리당 1번 합산 드랍하도록 분배
        /// 이펙트/UI 과부하 방지를 위해 드랍 빈도를 줄이고 개당 골드량을 증가
        /// goldIndexDic (PXBigInt) 사용으로 큰 골드량도 정확히 처리
        /// </summary>
        /// <param name="totalAmount">총 골드량</param>
        /// <param name="monsterCount">전체 몬스터 수</param>
        /// <param name="dropFrequencyDivisor">드랍 빈도 제수 (3이면 3마리당 1번 드랍)</param>
        private void DistributeGoldByGroupIndex(PXBigInt totalAmount, int monsterCount, int dropFrequencyDivisor = 3)
        {
            if (totalAmount.Value <= 0 || monsterCount <= 0)
                return;

            // 드랍 횟수 계산: 전체 몬스터 / 빈도 제수 (최소 1회)
            int goldDropCount = Mathf.Max(1, monsterCount / dropFrequencyDivisor);

            // 드랍당 골드량 계산
            System.Numerics.BigInteger goldPerDrop = totalAmount.Value / goldDropCount;
            System.Numerics.BigInteger remainder = totalAmount.Value % goldDropCount;

            // 균등 분배 인덱스 생성 (장비와 동일한 로직)
            var config = GameClientPlayConfig.Instance.stage;
            int startIndex = Mathf.Max(1, (int)(monsterCount * config.itemDropStartPercent));
            int maxIndex = monsterCount;

            var distributedIndices = GameBattleUtilityManager.Instance.GenerateEvenlyDistributedNumbers(
                goldDropCount,
                startIndex,
                maxIndex
            );

            // 생성된 인덱스에 골드 배치 (PXBigInt로 정확한 값 저장)
            for (int i = 0; i < goldDropCount && i < distributedIndices.Length; i++)
            {
                int monsterIndex = distributedIndices[i];

                // 마지막 드랍에 나머지 추가
                System.Numerics.BigInteger dropAmount = goldPerDrop;
                if (i == goldDropCount - 1)
                    dropAmount += remainder;

                // goldIndexDic에 PXBigInt로 저장 (데이터 손실 없음)
                if (!goldIndexDic.ContainsKey(monsterIndex))
                    goldIndexDic[monsterIndex] = PXBigInt.Create(dropAmount);
                else
                    goldIndexDic[monsterIndex].Add(dropAmount);
            }
        }

        /// <summary>
        /// 재화를 특정 몬스터 인덱스에 균등 분배하여 currencyIndexDic에 저장
        /// Gold가 아닌 재화(장비 파편, 보석 등)는 특정 몬스터에만 드랍
        /// </summary>
        private void DistributeCurrencyByIndex(ECurrency currencyType, PXBigInt totalAmount, int monsterCount)
        {
            int currencyCount = (int)totalAmount.Value;

            if (currencyCount <= 0)
                return;

            // 균등 분배 인덱스 생성 (장비와 동일한 로직)
            var config = GameClientPlayConfig.Instance.stage;
            int startIndex = Mathf.Max(config.itemDropMinIndex, (int)(monsterCount * config.itemDropStartPercent));
            int maxIndex = monsterCount;

            var distributedIndices = GameBattleUtilityManager.Instance.GenerateEvenlyDistributedNumbers(
                currencyCount,
                startIndex,
                maxIndex
            );

            // 생성된 인덱스에 재화 배치
            for (int i = 0; i < currencyCount && i < distributedIndices.Length; i++)
            {
                int monsterIndex = distributedIndices[i];

                // 해당 인덱스에 딕셔너리가 없으면 생성
                if (!currencyIndexDic.ContainsKey(monsterIndex))
                    currencyIndexDic[monsterIndex] = new Dictionary<ECurrency, int>();

                // 동일 인덱스에 동일 재화가 여러 개면 누적
                if (!currencyIndexDic[monsterIndex].ContainsKey(currencyType))
                    currencyIndexDic[monsterIndex][currencyType] = 0;

                currencyIndexDic[monsterIndex][currencyType]++;
            }
        }

        /// <summary>
        /// 스킬 보상을 적절한 몬스터 인덱스에 균등 분배하여 skillSlotDataDic에 저장
        /// 장비와 동일한 방식으로 특정 몬스터에만 드랍
        /// </summary>
        private void DistributeSkillRewards(List<FSkillSlotData> skillSlotDataList, int monsterCount)
        {
            if (skillSlotDataList.Count == 0)
                return;

            // 스킬이 지급될 몬스터 인덱스들을 균등하게 분배
            var config = GameClientPlayConfig.Instance.stage;
            int skillStartIndex = Mathf.Max(config.itemDropMinIndex, (int)(monsterCount * config.itemDropStartPercent));
            int skillMaxIndex = monsterCount;
            int skillCount = skillSlotDataList.Count;

            var distributedIndices = GameBattleUtilityManager.Instance.GenerateEvenlyDistributedNumbers(
                skillCount, skillStartIndex, skillMaxIndex);

            // 생성된 인덱스에 스킬 배치
            for (int i = 0; i < skillCount && i < distributedIndices.Length; i++)
            {
                int monsterIndex = distributedIndices[i];
                FSkillSlotData skill = skillSlotDataList[i];

                skillSlotDataDic[monsterIndex] = skill;
            }
        }
        /// <summary>
        /// 룬 보상을 적절한 몬스터 인덱스에 균등 분배하여 runeSlotDataDic에 저장
        /// 장비와 동일한 방식으로 특정 몬스터에만 드랍
        /// </summary>
        private void DistributeRuneRewards(List<FRuneSlotData> runeSlotDataList, int monsterCount)
        {
            if (runeSlotDataList.Count == 0)
                return;

            // 룬이 지급될 몬스터 인덱스들을 균등하게 분배
            var config = GameClientPlayConfig.Instance.stage;
            int runeStartIndex = Mathf.Max(config.itemDropMinIndex, (int)(monsterCount * config.itemDropStartPercent));
            int runeMaxIndex = monsterCount;
            int runeCount = runeSlotDataList.Count;

            var distributedIndices = GameBattleUtilityManager.Instance.GenerateEvenlyDistributedNumbers(
                runeCount, runeStartIndex, runeMaxIndex);

            // 생성된 인덱스에 룬 배치
            for (int i = 0; i < runeCount && i < distributedIndices.Length; i++)
            {
                int monsterIndex = distributedIndices[i];
                FRuneSlotData rune = runeSlotDataList[i];

                runeSlotDataDic[monsterIndex] = rune;
            }
        }
        /// <summary>
        /// 펫 보상을 적절한 몬스터 인덱스에 균등 분배하여 petSlotDataDic에 저장
        /// 장비와 동일한 방식으로 특정 몬스터에만 드랍
        /// </summary>
        private void DistributePetRewards(List<FPetSlotData> petSlotDataList, int monsterCount)
        {
            if (petSlotDataList.Count == 0)
                return;

            // 펫이 지급될 몬스터 인덱스들을 균등하게 분배
            var config = GameClientPlayConfig.Instance.stage;
            int petStartIndex = Mathf.Max(config.itemDropMinIndex, (int)(monsterCount * config.itemDropStartPercent));
            int petMaxIndex = monsterCount;
            int petCount = petSlotDataList.Count;

            var distributedIndices = GameBattleUtilityManager.Instance.GenerateEvenlyDistributedNumbers(
                petCount, petStartIndex, petMaxIndex);

            // 생성된 인덱스에 펫 배치
            for (int i = 0; i < petCount && i < distributedIndices.Length; i++)
            {
                int monsterIndex = distributedIndices[i];
                FPetSlotData pet = petSlotDataList[i];

                petSlotDataDic[monsterIndex] = pet;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 보상 분배 검증 (에디터 전용)
        /// 실제 분배된 보상이 원래 보상과 일치하는지 확인
        /// </summary>
        public void ValidateRewardDistribution()
        {
            // 검증 로그는 Unity 콘솔이 아니라 EditorLogCollector 로 보낸다.
            // 스테이지 진입마다 호출되므로 콘솔로 내보내면 스택트레이스까지 얹혀
            // 에디터 프로파일러 UI 이벤트 버퍼를 넘겨 에디터가 크래시한다.
            EditorLogCollectorClient.Log("=== 보상 분배 검증 시작 ===");

            // currencyPerDic 재화 검증 (Gold + 큰 값의 재화)
            EditorLogCollectorClient.Log("\n=== 전체 몬스터 분배 재화 (currencyPerDic) ===");
            foreach (var currencyEntry in currencyPerDic)
            {
                ECurrency currencyType = currencyEntry.Key;
                PXBigInt totalAmount = currencyEntry.Value;
                PXBigInt avgAmount = PXBigInt.Create(totalAmount.Value / AllGroupMonsterCount);
                PXBigInt expectedTotal = PXBigInt.Create(avgAmount.Value * AllGroupMonsterCount);
                System.Numerics.BigInteger remainder = 0;

                // 나머지 값 확인
                if (currencyRemainderDic.ContainsKey(currencyType))
                {
                    remainder = currencyRemainderDic[currencyType].Value;
                }

                System.Numerics.BigInteger finalTotal = expectedTotal.Value + remainder;
                System.Numerics.BigInteger difference = totalAmount.Value - finalTotal;

                EditorLogCollectorClient.Log($"[{currencyType}] 원본: {totalAmount.Value}, 분배 예상: {expectedTotal.Value}, 나머지(마지막 몬스터): {remainder}, 최종: {finalTotal}, 차이: {difference}");

                if (difference != 0)
                {
                    Debug.LogError($"[{currencyType}] 예상치 못한 차이 발생! 차이: {difference}");
                }
            }

            // goldIndexDic 골드 검증 (PXBigInt로 정확한 값)
            EditorLogCollectorClient.Log($"\n=== 골드 분배 검증 (goldIndexDic) ===");
            System.Numerics.BigInteger totalGoldDistributed = 0;
            int goldDropCount = 0;
            foreach (var goldEntry in goldIndexDic)
            {
                totalGoldDistributed += goldEntry.Value.Value;
                goldDropCount++;
            }
            EditorLogCollectorClient.Log($"[Gold] 드랍 횟수: {goldDropCount}, 총 분배량: {totalGoldDistributed}");

            // currencyIndexDic 재화 검증 (작은 값의 재화, 골드 제외)
            Dictionary<ECurrency, int> currencyCountDic = new Dictionary<ECurrency, int>();
            foreach (var indexEntry in currencyIndexDic)
            {
                foreach (var currencyEntry in indexEntry.Value)
                {
                    if (!currencyCountDic.ContainsKey(currencyEntry.Key))
                        currencyCountDic[currencyEntry.Key] = 0;
                    currencyCountDic[currencyEntry.Key] += currencyEntry.Value;
                }
            }

            EditorLogCollectorClient.Log($"\n=== 특정 몬스터 분배 재화 (currencyIndexDic, 골드 제외) ===");
            foreach (var entry in currencyCountDic)
            {
                EditorLogCollectorClient.Log($"[{entry.Key}] 분배된 개수: {entry.Value}");
            }

            // 장비 검증
            EditorLogCollectorClient.Log($"\n[Equipment] 분배된 개수: {equipmentSlotDataDic.Count}");

            // Skill 검증
            EditorLogCollectorClient.Log($"[Skill] 분배된 개수: {skillSlotDataDic.Count}");

            // Rune 검증
            EditorLogCollectorClient.Log($"[Rune] 분배된 개수: {runeSlotDataDic.Count}");

            // Pet 검증
            EditorLogCollectorClient.Log($"[Pet] 분배된 개수: {petSlotDataDic.Count}");

            EditorLogCollectorClient.Log("\n=== 보상 분배 검증 완료 ===");
        }
#endif
    }

    public FBattleModeDataBase battleModeBaseData { get; protected set; }
    public BattleInfoData battleInfoData { get { return battleModeBaseData.battleInfoData; } }

    /// <summary>
    /// 전체 웨이브(몬스터 그룹) 수. 0 이면 웨이브 개념이 없는 모드(보스러시·타락 등) — UI 는 웨이브 표시를 숨긴다.
    /// battleInfoData.SpawnGroupIndex 는 스테이지 루프마다 리셋되지 않아 웨이브 번호로 쓸 수 없으므로,
    /// 각 모드가 보유한 remainGroupCount 를 기준으로 노출한다.
    /// </summary>
    public virtual int TotalWaveCount => 0;

    /// <summary>현재 진행 중인 웨이브 번호(1-base). 웨이브 개념이 없으면 0.</summary>
    public virtual int CurrentWaveNo => 0;

    private GameDB_Client_Stage gameDB_Client_Stage;

    private List<KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal>> normalMonsterDBDataList;

    //normalMonsterDBDataList 를 구성할 때 쓴 스테이지 타입. 타입이 바뀌면 후보 목록을 다시 만든다
    private EStage normalMonsterDBDataStageType = EStage.None;

    //스테이지 내내 같은 종류의 일반 몬스터를 쓰기 위한 고정 인덱스 (-1이면 아직 안 뽑음).
    //웨이브마다 종류가 바뀌면 그때마다 Addressables 동기 로드(WaitForCompletion)와 캐릭터 풀 미스가
    //다시 발생해 소환 순간 프레임이 크게 튄다. ResetStageNormalMonsterSelection() 으로만 재추첨된다
    private int stageFixedNormalMonsterIndex = -1;
    private List<KeyValuePair<EMonsterBoss, GameDB_Client_MonsterBoss>> bossMonsterDBDataList;
    private List<KeyValuePair<EMonsterFloorBoss, GameDB_Client_MonsterFloorBoss>> floorBossMonsterDBDataList;

    //bossMonsterDBDataList 를 구성할 때 쓴 스테이지 타입. 타입이 바뀌면 후보 목록을 다시 만든다
    private EStage bossMonsterDBDataStageType = EStage.None;

    //스테이지 내내 같은 종류의 일반 보스를 쓰기 위한 고정 인덱스 (-1이면 아직 안 뽑음).
    //일반 몬스터를 고정하는 이유에 더해, 보스는 사망 파쇄용 분해 메시를 굽는 비용이 크다
    //(실측 종당 3~86ms). 종류가 정해져 있어야 그것을 스테이지 진입 때 미리 구워 둘 수 있고,
    //그러지 않으면 그 종류가 처음 죽는 프레임에 통째로 구워진다.
    //ResetStageBossMonsterSelection() 으로만 재추첨된다
    private int stageFixedBossMonsterIndex = -1;


    private HashSet<string> rutineRatorHash;

    // Config로 이동: GameClientPlayConfig.Instance.stage.remainEnemyCheckRate
    // protected const float checkRemainRate = 0.2f;


    //Spawn Monster 정보
    //private int spawnTypeRateTotal;

    //private List<KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal>> meleeNormalMonsterMap;
    //private List<KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal>> rangeNormalMonsterMap;
    //private List<KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal>> magicNormalMonsterMap;

    public FBattleModeBase()
    {
        normalMonsterDBDataList = new List<KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal>>();
        bossMonsterDBDataList = new List<KeyValuePair<EMonsterBoss, GameDB_Client_MonsterBoss>>();
        floorBossMonsterDBDataList = new List<KeyValuePair<EMonsterFloorBoss, GameDB_Client_MonsterFloorBoss>>();
        rutineRatorHash = new HashSet<string>();
        //meleeNormalMonsterMap = new List<KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal>>();
        //rangeNormalMonsterMap = new List<KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal>>();
        //magicNormalMonsterMap = new List<KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal>>();
    }

    protected override void ManagedDispose()
    {
        base.ManagedDispose();

        GameCharacterManager.Instance.ClearAllData();
        GameProjectileManager.Instance.ClearAllData();

        //이펙트는 스킬 구성에 따라 종류가 크게 달라진다. 안 비우면 안 쓰는 이펙트가 세션 내내 남는다
        GameEffectManager.Instance.ClearAllEffect();
        GameObjectPoolManager.Instance.ClearEffectPool();

        battleModeBaseData?.Dispose();
        battleModeBaseData = null;
    }

    protected GameDB_Client_Stage GetCurrentStageData()
    {

        try
        {
            if (battleModeBaseData?.battleInfoData?.stageType == EStage.None)
            {
                return null;
            }
        }
        catch (Exception e)
        {
            GameLogSystem.LogError("GetCurrentStageData", e);
        }



        if (gameDB_Client_Stage != null)
        {
            return gameDB_Client_Stage;
        }
        else
        {
            if (GameDBClientManager.Instance.GameDB_Stage.Stage != null)
            {
                if (GameDBClientManager.Instance.GameDB_Stage.Stage.MapData == null)
                {
                    //Debug.LogError("error, gameDBClientManager.GameDB_Stage.Stage.MapData NULL Data.");

                    return default;
                }

                if (GameDBClientManager.Instance.GameDB_Stage.Stage.MapData.TryGetValue(battleModeBaseData.battleInfoData.stageType, out GameDB_Client_Stage outData))
                {
                    gameDB_Client_Stage = outData;
                }
                else
                {
                    //Debug.LogError("error, gameDBClientManager.GameDB_Stage.Stage.MapData TryGetValue Failed");
                }
            }
            else
            {
                //Debug.LogError("error, gameDBClientManager.GameDB_Stage.Stage NULL Data.");
            }

            return gameDB_Client_Stage;
        }
    }

    protected virtual Dictionary<string, object> GetStageClearData()
    {
        string stageMainHash = "";
        if (GameUtility.IsMainStage(battleInfoData.stageType))
        {
            stageMainHash = GameAPIUserManager.Instance.userData.accountData.CoreData.HashInfo.StageMainHash;
        }

        Dictionary<string, object> requestData = new Dictionary<string, object>
            {
                { "stage", (int)battleInfoData.stageType },
                { "stage_level", battleInfoData.stageLevel.Value },
                { "stageMainHash", stageMainHash },

                { "totalKill", GetServerTotalKillCount() },
                { "strTotalDamage", battleInfoData.TotalDamage.ToString() },
                { "clearTime", (int)battleInfoData.battleDurationTime },
            };

        // 도전 스테이지(rift/greatrift/bossrush/corruption/invasion/incursion) 클리어 시 매핑된 프리셋 id 를 동봉.
        // 추후 도전 랭킹에서 "최대 도달 단계 클리어 시 사용한 빌드" 식별용 (서버는 현재 로깅 용도로만 사용).
        if (!GameUtility.IsMainStage(battleInfoData.stageType))
        {
            requestData["preset"] = (int)GameAPIUserManager.Instance.userData.presetData.GetChallengePreset(battleInfoData.stageType);
        }

        return requestData;
    }
    protected virtual Dictionary<string, object> GetStageFailData()
    {
        string stageMainHash = GameAPIUserManager.Instance.userData.accountData.CoreData.HashInfo.StageMainHash;
        Dictionary<string, object> requestData = new Dictionary<string, object>
            {
                { "stage", (int)battleInfoData.stageType },
                { "stage_level", battleInfoData.stageLevel.Value },
                { "stageMainHash", stageMainHash },
            };
        return requestData;
    }

    public virtual void SetModeStart(EStage InStage, int InStageLevel)
    {
        StartCamera();
        InitModeData(InStage, InStageLevel);
        InitBattleData();
        HudStart();
        UpdateField();

        GameControlManager.Instance.GCCollectForce();

        //GC 강제 수집 뒤에 둔다. 앞에 두면 방금 로드한 것을 그대로 밀어낼 수 있다
        PrepareStageSpawnAssets();
    }
    public virtual void SetModeClose()
    {
        AllClearBattleNetwork();
        ClearCoroutine();
        BattleReset();
        HudClose();

        gameDB_Client_Stage = null;
        GameBattleControlManager.Instance.SetBattleSpeed(GameClientPlayConfig.Instance.stage.battleSpeed);
    }

    public virtual void UpdateDeltaTime(float deltaTime)
    {
        UpdateBattleTime(deltaTime);
    }

    protected virtual void UpdateBattleTime(float deltaTime)
    {
        if (GameBattleControlManager.Instance.IsBattleOn)
        {
            battleModeBaseData.battleInfoData.UpdateDeltaTime(deltaTime);
            if (battleModeBaseData.battleInfoData.battleLimitTime > 0 && battleModeBaseData.battleInfoData.battleRemainTime <= 0)
            {
                GameBattleControlManager.Instance.BattleFail();
            }
        }
    }

    protected virtual void InitModeData(EStage InStage, int InStageLevel)
    {
        battleModeBaseData.battleInfoData.SetStageType(InStage);
        battleModeBaseData.battleInfoData.SetStageLevel(InStageLevel);
    }

    /// <summary>
    /// 웨이브 몬스터 소환 코루틴이 진행 중인지 여부.
    /// 소환이 프레임에 걸쳐 나뉘어 일어나므로, 그 사이 웨이브 전환/스테이지 종료 판정이 끼어들면 안 된다.
    /// </summary>
    protected bool IsWaveSpawning { get; private set; }

    protected virtual void InitBattleData()
    {
        BattleDataReset();
        IsWaveSpawning = false;
    }

    protected virtual void ClearCoroutine()
    {
        //..
    }

    public virtual void HudStart()
    {
        switch (battleModeBaseData.battleInfoData.eBattleMode)
        {
            case EBattleMode.login:
                GameSoundManager.Instance.PlayBGM("bgm_mode_login2");
                break;
            default:
                GameSoundManager.Instance.PlayBGM("bgm_mode_main");
                break;
        }
    }
    public virtual void HudClose()
    {
    }

    public virtual void UpdateField()
    {
        //if (battleModeBaseData.battleInfoData.eBattleMode != EBattleMode.login)
        {
            GameBattleControlManager.Instance.SetSpawnWorldArea(battleModeBaseData.battleInfoData.eBattleMode);
        }
    }

    public virtual void BattleReady()
    {
        //..
    }

    public virtual void BattleStart()
    {
        ClearCoroutine();

        UCharacterActor playerCharacter = GameCharacterManager.Instance.GetPlayerCharacter();
        if (playerCharacter != null)
        {
            playerCharacter.CharacterStatus.SetBattleOn(true);
        }

        // 전투 스킵 모드 (에디터 시뮬레이션용)
        // SkipBattleMode가 true일 경우 데이터만 유지하고 실제 전투는 시작하지 않음
        if (GameBattleControlManager.SkipBattleMode)
        {
            return;
        }

        battleModeBaseData.battleInfoData.BattleStartTime();

        GameBattleControlManager.Instance.SetBattleStart();
    }

    /*
    public virtual void BattleStop()
    {
        foreach (KeyValuePair<string, FCharacterStatus> entryData in GameCharacterManager.Instance.CharacterStatusAllDic)
        {
            if (entryData.Value != null)
            {
                entryData.Value.SetBattleOn(false);
            }
        }

        GameBattleControlManager.Instance.SetBattleStop();
    }
    */

    /*
    public virtual void BattleFinish()
    {
        normalMonsterList.Clear();
    }
    */



    public virtual void BattleClear()
    {
        foreach (KeyValuePair<string, FCharacterStatus> entryData in GameCharacterManager.Instance.CharacterStatusAllDic)
        {
            if (entryData.Value != null)
            {
                entryData.Value.SetBattleOn(false);
            }
        }

        GameBattleControlManager.Instance.StageClearEvent();
    }

    public virtual void BattleFail()
    {
        // 타임오버/실패 시 모든 캐릭터 전투 즉시 중지
        foreach (KeyValuePair<string, FCharacterStatus> entryData in GameCharacterManager.Instance.CharacterStatusAllDic)
        {
            if (entryData.Value != null)
            {
                entryData.Value.SetBattleOn(false);
            }
        }

        GameBattleControlManager.Instance.StageFailEvent();
    }

    public virtual void BattleReset(bool InClearCoroutine = true)
    {
        /*InitBattleBaseData();*/
        BattleDataReset();

        if (InClearCoroutine)
            ClearCoroutine();

        GameCharacterManager.Instance.ClearAllData();
        GameProjectileManager.Instance.ClearAllData();
        GameBattleControlManager.Instance.ClearAllData();

        //이펙트는 스킬 구성에 따라 종류가 크게 달라진다. 안 비우면 안 쓰는 이펙트가 세션 내내 남는다
        GameEffectManager.Instance.ClearAllEffect();
        GameObjectPoolManager.Instance.ClearEffectPool();
    }


    public virtual void SpawnABTeam(ESABTeamType InTeamType)
    {
        if (InTeamType == ESABTeamType.Enemy)
        {
            battleModeBaseData.battleInfoData.SetSpawnGroupIndex(battleModeBaseData.battleInfoData.SpawnGroupIndex + 1);
        }


    }
    public virtual void SpawnedTeamCount(FCharacterSingleData InTargetInfo)
    {
        battleModeBaseData.battleInfoData.SpawnedTeamCount(InTargetInfo);

#if UNITY_EDITOR
        ////Debug.Log("SpawnedTeamCount SpawnedTeamCount , RemainAlianceCount = " + battleModeBaseData.battleCountData.RemainAlianceCount + ", RemainEnemyCount = " + battleModeBaseData.battleCountData.RemainEnemyCount);
#endif
    }

    public virtual void DeSpawnedTeamInfo(FCharacterSingleData InTargetInfo)
    {
        battleModeBaseData.battleInfoData.DeSpawnedTeamInfo(InTargetInfo);

#if UNITY_EDITOR
        ////Debug.Log(" DeSpawnedTeamInfo , RemainAlianceCount = " + battleModeBaseData.battleCountData.RemainAlianceCount + ", RemainEnemyCount = " + battleModeBaseData.battleCountData.RemainEnemyCount);
#endif
    }

    public virtual bool OnPreDamagedCharacter(FCharacterStatus InDefenderStatus, FCalcResultModDamage InDamageData)
    {
#if UNITY_EDITOR
        // 플레이어 무적 모드 (배틀 시뮬레이터에서 설정)
        if (GameBattleControlManager.DebugPlayerInvincible && InDefenderStatus.BasicStatus.IsPlayer)
        {
            return false; // 플레이어는 피해 무시
        }
#endif
        return true;
    }

    protected virtual void CheckConditionStage(UCharacterActor InDeadActor)
    {
    }

    //스테이지 종료조건 검사
    // protected virtual bool CheckConditionOver(UCharacterActor InDeadActor)
    // {
    //     if (InDeadActor == null)
    //     {
    //         //Debug.LogError("InDeadActor == null");
    //         return false;
    //     }
    //     if (InDeadActor.CharacterStatus == null)
    //     {
    //         //Debug.LogError("InDeadActor.CharacterStatus == null");
    //         return false;
    //     }

    //     GameDB_Client_Stage sheetDataStage = GetCurrentStageData();
    //     if (sheetDataStage == null)
    //     {
    //         //Debug.LogError("sheetDataStage == null, CurrentStageDataKey = " + battleModeBaseData.battleInfoData.stageType.ToString());
    //         return false;
    //     }

    //     if (InDeadActor.IsPlayerCharacter)
    //     {
    //         battleModeBaseData.battleInfoData.StageConditionOver(false);
    //         return true;
    //     }
    //     else
    //     {
    //         //보스제거시 성공
    //         FMonsterSingleData monsterData = (FMonsterSingleData)InDeadActor.CharacterStatus.BasicStatus;
    //         if (monsterData != null)
    //         {
    //             if (monsterData.MonsterType == EMonsterType.monster_type_boss)
    //             {
    //                 //남은 모든 적 죽음
    //                 Dictionary<string, FCharacterStatus> enemyDic = GameCharacterManager.Instance.GetEnemyTeamData();

    //                 foreach (KeyValuePair<string, FCharacterStatus> entry in enemyDic)
    //                 {
    //                     if (entry.Value.IsDead == false)
    //                     {
    //                         entry.Value.CharacterActor.SetDead();
    //                     }
    //                 }

    //                 battleModeBaseData.battleInfoData.StageConditionOver(true);

    //                 return true;
    //             }
    //         }

    //         /*
    //         //기획 변경에 따른 제거
    //         switch (sheetDataStage.CompCondition)
    //         {
    //             //보스를 죽였을 때
    //             case EStageCompleteCondition.comp_kill_boss:
    //                 {
    //                     FMonsterSingleData monsterData = (FMonsterSingleData)InDeadActor.CharacterStatus.BasicStatus;
    //                     if (monsterData != null)
    //                     {
    //                         if (monsterData.MonsterType == EMonsterType.monster_type_boss)
    //                         {
    //                             //남은 모든 캐릭터 죽음

    //                             Dictionary<string, FCharacterStatus> enemyDic = GameCharacterManager.Instance.GetEnemyTeamData();

    //                             foreach (KeyValuePair<string, FCharacterStatus> entry in enemyDic)
    //                             {
    //                                 if (entry.Value.IsDead == false)
    //                                 {
    //                                     entry.Value.CharacterActor.SetDead();
    //                                 }
    //                             }

    //                             battleModeBaseData.battleInfoData.StageConditionOver(true);

    //                             return true;
    //                         }
    //                     }
    //                 }
    //                 break;
    //             //그룹 죽었을 때? comp_kill_wave 와 다른건가?
    //             case EStageCompleteCondition.comp_kill_group:
    //                 {
    //                     if (battleModeBaseData.battleInfoData.RemainEnemyCount == 0)
    //                     {
    //                         battleModeBaseData.battleInfoData.StageConditionOver(true);
    //                         return true;
    //                     }
    //                 }
    //                 break;
    //             //시간 제한
    //             case EStageCompleteCondition.comp_timeover:
    //                 {
    //                     float battleTimeOver = 20.0f;
    //                     if (battleModeBaseData.battleInfoData.battleDurationTime <= battleTimeOver)
    //                     {
    //                         battleModeBaseData.battleInfoData.StageConditionOver(false);
    //                         return true;
    //                     }
    //                 }
    //                 break;
    //         }
    //         */
    //     }

    //     return false;
    // }

    public virtual void SetCharacterDead(UCharacterActor InActor, FCalcResultModDamage InDamageData = null)
    {
        if (InActor == null || InActor.CharacterStatus == null)
            return;

        if (InActor.IsAlianceTeam == false)
        {
            TKPopup_PowerSave.AddKillMonster();

            // 플레이어가 적을 처치한 경우 - mod_*_when_kill_recently 지원
            UCharacterActor playerCharacter = GameCharacterManager.Instance.GetPlayerCharacter();
            if (playerCharacter != null && playerCharacter.CharacterStatus != null)
            {
                playerCharacter.CharacterStatus.BattleStatus.OnEnemyKilled();

                // 적 처치 시 폭발 처리
                ProcessEnemyKillExplosion(InActor, playerCharacter, InDamageData);
            }
        }

        battleModeBaseData.battleInfoData.SetCharacterDeadCount(InActor);

        GameBattleControlManager.Instance.characterDeadDelegate?.Invoke(InActor);

        //전투 종료 조건 검사
        //CheckConditionOver(InActor);
        CheckConditionStage(InActor);
    }

    /// <summary>
    /// 적 처치 시 폭발 처리
    /// 처치된 적이 확률적으로 폭발하여 최대 생명력의 10%를 속성 피해로 줌
    /// </summary>
    private void ProcessEnemyKillExplosion(UCharacterActor deadEnemy, UCharacterActor player, FCalcResultModDamage InDamageData = null)
    {
        if (deadEnemy == null || player == null)
            return;

        var playerBattleStatus = player.CharacterStatus?.BattleStatus;
        if (playerBattleStatus == null)
            return;

        // 죽은 적의 최대 생명력
        double enemyMaxLife = deadEnemy.CharacterStatus?.BattleStatus?.ResultMaxLife() ?? 0;
        if (enemyMaxLife <= 0)
            return;

        // 폭발 피해 = 최대 생명력 × Config 비율
        double baseDamage = enemyMaxLife * GameClientPlayConfig.Instance.combat.explosionDamagePercent;

        if (InDamageData != null)
        {     // 5가지 속성별 폭발 확률 확인 및 처리
            if (InDamageData.SkillData.GetSkillDB.SkillTags.ContainsKey(ESkillTag.skilltag_cold))
            {
                ProcessExplosionForElement(EMod.mod_enemy_when_kill_explode_deal_life_as_cold, "Cold", baseDamage, deadEnemy, playerBattleStatus, InDamageData);
            }
            else if (InDamageData.SkillData.GetSkillDB.SkillTags.ContainsKey(ESkillTag.skilltag_fire))
            {
                ProcessExplosionForElement(EMod.mod_enemy_when_kill_explode_deal_life_as_fire, "Fire", baseDamage, deadEnemy, playerBattleStatus, InDamageData);
            }
            else if (InDamageData.SkillData.GetSkillDB.SkillTags.ContainsKey(ESkillTag.skilltag_lightning))
            {
                ProcessExplosionForElement(EMod.mod_enemy_when_kill_explode_deal_life_as_lightning, "Lightning", baseDamage, deadEnemy, playerBattleStatus, InDamageData);
            }
            else if (InDamageData.SkillData.GetSkillDB.SkillTags.ContainsKey(ESkillTag.skilltag_physical))
            {
                ProcessExplosionForElement(EMod.mod_enemy_when_kill_explode_deal_life_as_physical, "Physical", baseDamage, deadEnemy, playerBattleStatus, InDamageData);
            }
            else if (InDamageData.SkillData.GetSkillDB.SkillTags.ContainsKey(ESkillTag.skilltag_poison))
            {
                ProcessExplosionForElement(EMod.mod_enemy_when_kill_explode_deal_life_as_poison, "Poison", baseDamage, deadEnemy, playerBattleStatus, InDamageData);
            }
        }
    }

    /// <summary>
    /// 특정 속성의 폭발 처리
    /// </summary>
    private void ProcessExplosionForElement(EMod explosionMod, string elementName, double baseDamage,
        UCharacterActor deadEnemy, FBattleStatus playerBattleStatus, FCalcResultModDamage InDamageData = null)
    {
        // 폭발 확률 (MOD 값이 %)
        double explosionChance = playerBattleStatus.TotalModValue(explosionMod);
        if (explosionChance <= 0)
            return;

        // 확률 체크 (FLOAT_PER MOD: GetRandomRatio()와 직접 비교)
        double randomValue = GameBattleUtilityManager.Instance.GetRandomRatio();
        if (randomValue > explosionChance)
            return;

        // 폭발 발생! 주변 적들에게 피해
        // 폭발 범위: Config 기본값, mod_all_aoe_radius_inc 영향 받음
        float baseRadius = GameClientPlayConfig.Instance.combat.explosionBaseRadius;
        // FLOAT_PER MOD: TotalModValue()가 이미 비율 반환 (0.3 = 30%)
        double aoeIncMod = playerBattleStatus.TotalModValue(EMod.mod_all_aoe_radius_inc);
        float explosionRadius = baseRadius * (1f + (float)aoeIncMod);

        // 죽은 적 주변의 모든 적 검색
        List<FCharacterStatus> enemiesInRange = GameEffectAreaManager.Instance?.effectAreaControl?.CalcDataCircle(
            deadEnemy.GetTransform,
            GameCharacterManager.Instance.GetTeamOrderBy(false, deadEnemy.CharacterStatus.BasicStatus.TeamNo, deadEnemy.GetTransform.position),
            explosionRadius
        );

        if (enemiesInRange == null || enemiesInRange.Count == 0)
            return;

        EffectData effectData = GameEffectManager.Instance.GetEffectParticle("effect_explosion", "KillExplosion");
        effectData.SetEffectWorldPosition(deadEnemy.WorldPosition);
        effectData.SetEffectLocalScale(Vector3.one * explosionRadius);
        effectData.PlayEffect();

        // 각 적에게 폭발 피해 적용
        foreach (var enemyStatus in enemiesInRange)
        {
            if (enemyStatus?.CharacterActor == null)
                continue;

            FCalcResultModDamage spellBattleDamage = GameBattleUtilityManager.Instance.CalcResultModDamage(enemyStatus.CharacterActor.CharacterStatus, InDamageData.SkillData);
            spellBattleDamage.SetResultDamage(baseDamage);

            enemyStatus.CharacterActor.SetDamaged(spellBattleDamage);
        }
    }

    public virtual void SetCharacterDeadFinish(UCharacterActor InActor, bool IsDestroyData = true)
    {
        if (InActor == null)
            return;

        GameCharacterManager.Instance.SetCharacterDeadFinish(InActor, IsDestroyData);
    }

    public virtual void BattleAlianceReviveAll()
    {
        GameUIManager.Instance.GetWidget("Popup_StageHud").SetVisibility(true);

        foreach (KeyValuePair<string, FCharacterSingleData> entryData in battleModeBaseData.battleCharactersData.battleCharacterData)
        {
            BattleCharacterRevive(entryData.Value);
        }

        BattleStart();
        //GetBattleControlManager.SetBattleOn(true);
    }

    public virtual void BattleCharacterRevive(FCharacterSingleData InData)
    {
        if (InData == null)
            return;

        FCharacterStatus targetStatus = GameCharacterManager.Instance.GetCharacterStatusByUID(InData.CUID);
        if (targetStatus == null)
            return;

        //부활 스탯 수정
        targetStatus.SerReviveStatus();

        //캐릭터 리스폰
        GameCharacterManager.Instance.SpawnCharacter(targetStatus);
        //전투팀 UI체력 갱신
        GameCharacterManager.Instance.OnUpdateStageHPEvent(InData.TeamNo == FCommonDefine.AlianceTeamNo);
        //전투팀 SpawnCount 갱신
        SpawnedTeamCount(InData);

        //전투 시작
        targetStatus.SetBattleOn(true);
    }
    public virtual void StartCamera()
    {
        GameCameraManager.Instance.SetModeViewTarget(battleModeBaseData.battleInfoData.eBattleMode);

        GameTextDamageManager.Instance.UpdateTextCamera(GameCameraManager.Instance.currentViewCamera);
    }
    protected void BattleDataReset()
    {
        battleModeBaseData.BattleDataReset();

        //일반 몬스터·일반 보스의 후보 목록과 종류 선택은 여기서 건드리지 않는다.
        //BattleDataReset()은 소환 직전(SetBattleStart → InitBattleData)에 불리므로 여기서 지우면
        //스테이지 진입 시점에 미리 확정해 둔 종류가 매번 날아가 프리로드가 무의미해진다.
        //재추첨은 PrepareStageSpawnAssets()가 소유한다 (스테이지 레벨마다 1회).
        //후보 목록은 스테이지 타입이 바뀔 때 EnsureBossMonsterDBDataList() 가 알아서 다시 만든다
        floorBossMonsterDBDataList.Clear();

        ClearDynamicSpell();

    }

    void ClearDynamicSpell()
    {
        var dynamicSpellList = GamePrefabManager.Instance.TemplatePrefebComp.DynamicObjectArea.GetComponentsInChildren<DynamicSpell>(true);
        for (int i = 0; i < dynamicSpellList.Length; i++)
        {
            dynamicSpellList[i].SetDestroy();
        }
    }

    //데미지 연산
    /// <summary>
    /// 피해 계산 (회피/블록/치명타 판정 포함)
    /// </summary>
    /// <param name="skipEvade">회피 판정 스킵 (Ailment/DoT용)</param>
    /// <param name="skipBlock">블록 판정 스킵 (Ailment/DoT용)</param>
    /// <param name="skipCritical">치명타 판정 스킵 (Ailment는 별도 캐시 사용)</param>
    public virtual FCalcResultModDamage CalcResultModDamage(FCharacterStatus InDefenderStatus, FSkillData InAttackerSkillData,
        bool skipEvade = false, bool skipBlock = false, bool skipCritical = false)
    {
        FCalcResultModDamage damageInfo = FCalcResultModDamage.CreateDamage(InAttackerSkillData);

        // ====================================
        // Step 1: 회피 판정 (Evade Check)
        // ====================================
        if (!skipEvade && CheckEvade(InDefenderStatus, ref damageInfo))
        {
            return damageInfo;
        }

        // ====================================
        // Step 3: 블록 판정 (Block Check)
        // ====================================
        if (!skipBlock && CheckBlock(InDefenderStatus, ref damageInfo))
        {
            return damageInfo;
        }

        // ====================================
        // Step 4: 공격자 기본 데미지
        // ====================================
        double attackerDamage = InAttackerSkillData.ResultAttackerDamage;

        // ====================================
        // Step 5: 크리티컬 데미지 적용
        // ====================================
        FCharacterBaseStatus attackerStatus = GameCharacterManager.Instance.GetCharacterStatusByUID(InAttackerSkillData.AttackerCUID);
        if (attackerStatus != null)
        {
            // 치명타 스킵이 아닐 때만 크리티컬 판정 수행
            if (!skipCritical)
            {
                attackerDamage = ApplyCriticalDamage(attackerStatus, InDefenderStatus, attackerDamage, ref damageInfo);
            }

            // 공격자 위치 정보 저장
            var attackerCharacter = GameCharacterManager.Instance.GetCharacterByUID(InAttackerSkillData.AttackerCUID);
            if (attackerCharacter != null)
            {
                damageInfo.SetAttackPos(attackerCharacter.CenterWorldPosition);
            }
        }

        // ====================================
        // Step 5.5: 적 상태이상에 따른 조건부 피해 증가
        // ====================================
        if (attackerStatus != null)
        {
            attackerDamage = ApplyConditionalDamageModifiers(InDefenderStatus, attackerStatus, attackerDamage);
        }

        // ====================================
        // Step 6: 방어력/저항 적용
        // ====================================
        if (InAttackerSkillData.SkillTagDamageType == ESkillTag.skilltag_physical)
        {
            // 물리 피해 → 방어력 적용
            attackerDamage = ApplyPhysicalDefense(InDefenderStatus, attackerStatus, attackerDamage);
        }
        else
        {
            // 원소 피해 → 저항 적용
            attackerDamage = ApplyElementalResistance(InDefenderStatus, attackerStatus, InAttackerSkillData.SkillTagDamageType, attackerDamage);
        }

        // ====================================
        // Step 6.5: [Defense] 5단계 - 받는 피해 증감 적용
        // ====================================
        if (attackerStatus != null)
        {
            attackerDamage = ApplyDamageTakenModifiers(InDefenderStatus, attackerStatus, InAttackerSkillData.SkillTagDamageType, attackerDamage);
        }

        // ====================================
        // Step 6.6: 멀티플 피해 적용 (2배/3배)
        // ====================================
        if (attackerStatus != null)
        {
            attackerDamage = ApplyMultipleDamage(attackerStatus, attackerDamage);
        }

        // ====================================
        // Step 6.7: 즉사 판정 (Instant Kill Check)
        // ====================================
        if (attackerStatus != null)
        {
            attackerDamage = ApplyInstantKill(InDefenderStatus, attackerStatus, attackerDamage);
        }

        // ====================================
        // Step 8: 최종 데미지 저장 및 반환
        // ====================================
        // SetResultDamage 내부에서 반올림 처리하므로 double 그대로 전달
        damageInfo.SetResultDamage(attackerDamage, InAttackerSkillData.AttackerCUID);

#if UNITY_EDITOR
        // 실시간 피해 검증을 위한 이벤트 기록
        if (DamageEventRecorder.IsCollecting && attackerStatus != null && attackerStatus.BasicStatus.IsPlayer)
        {
            double critMultiplier = 1.0;
            if (damageInfo.IsCriticalBlow)
            {
                critMultiplier = attackerStatus.BattleStatus.ResultCriticalMultiplier(InDefenderStatus)
                               * attackerStatus.BattleStatus.ResultCriticalBlowmultiplier();
            }
            else if (damageInfo.IsCritical)
            {
                critMultiplier = attackerStatus.BattleStatus.ResultCriticalMultiplier(InDefenderStatus);
            }

            // SkillType에 따라 Spell 또는 Aura로 분류
            if (InAttackerSkillData.SkillType == ESkillType.skill_aura)
            {
                // dotonly 타입(skillaura_inevitable 등)은 직접 피해를 주지 않으므로 Aura 피해로 기록하지 않음
                // 이 경우 피해는 버프(skill_contagion 등)에 전달되어 DoT로만 기록됨
                bool isDotOnlyAura = InAttackerSkillData.GetSkillDB != null
                    && InAttackerSkillData.GetSkillDB.AuraDamageType == EAuraDamageType.dotonly;

                if (!isDotOnlyAura)
                {
                    DamageEventRecorder.RecordAuraDamage(
                        InAttackerSkillData.Skill,
                        InAttackerSkillData.SkillTier,
                        InAttackerSkillData.SkillTagDamageType,
                        InAttackerSkillData.ResultAttackerDamage,
                        attackerDamage,
                        damageInfo.IsCritical,
                        critMultiplier);
                }
            }
            else
            {
                DamageEventRecorder.RecordSpellDamage(
                    InAttackerSkillData.Skill,
                    InAttackerSkillData.SkillTier,
                    InAttackerSkillData.SkillTagDamageType,
                    InAttackerSkillData.ResultAttackerDamage,
                    attackerDamage,
                    damageInfo.IsCritical,
                    damageInfo.IsCriticalBlow,
                    critMultiplier,
                    InAttackerSkillData.DamageGroupID);
            }
        }
#endif

        return damageInfo;
    }

    /// <summary>
    /// Step 1: 회피 판정
    /// 공식: 랜덤값(0.01~1.01) <= 회피확률 → 회피 성공
    /// 예: 회피 0.03 (3%) → randomValue <= 0.03 → 약 3% 확률로 회피
    /// ResultEvadeChange()는 이미 비율(0.03 = 3%)을 반환
    /// </summary>
    private bool CheckEvade(FCharacterStatus InDefenderStatus, ref FCalcResultModDamage damageInfo)
    {
        // ResultEvadeChange()는 FLOAT_PER 타입으로 이미 비율(0.03 = 3%)을 반환
        double evadeChance = InDefenderStatus.BattleStatus.ResultEvadeChange();
        if (evadeChance > 0)
        {
            double randomValue = GameBattleUtilityManager.Instance.GetRandomRatio(); // 0.01~1.01
            // 이미 비율이므로 추가 변환 불필요
            if (randomValue <= evadeChance)
            {
                damageInfo.SetEvade(true);
#if UNITY_EDITOR
                // 회피 이벤트 기록
                if (DamageEventRecorder.IsCollecting && damageInfo.SkillData != null)
                {
                    var attackerStatus = GameCharacterManager.Instance.GetCharacterStatusByUID(damageInfo.SkillData.AttackerCUID);
                    if (attackerStatus != null && attackerStatus.BasicStatus.IsPlayer)
                    {
                        DamageEventRecorder.RecordSpellEvaded(
                            damageInfo.SkillData.Skill,
                            damageInfo.SkillData.SkillTier,
                            damageInfo.SkillData.SkillTagDamageType);
                    }
                }
#endif
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Step 3: 블록 판정
    /// 공식: 랜덤값(0.01~1.01) <= 블록확률 → 블록 성공
    /// 예: 블록 0.03 (3%) → randomValue <= 0.03 → 약 3% 확률로 블록
    /// ResultBlockChange()는 이미 비율(0.03 = 3%)을 반환
    /// </summary>
    private bool CheckBlock(FCharacterStatus InDefenderStatus, ref FCalcResultModDamage damageInfo)
    {
        // ResultBlockChange()는 FLOAT_PER 타입으로 이미 비율(0.03 = 3%)을 반환
        double blockChance = InDefenderStatus.BattleStatus.ResultBlockChange();
        if (blockChance > 0)
        {
            double randomValue = GameBattleUtilityManager.Instance.GetRandomRatio(); // 0.01~1.01
            // 이미 비율이므로 추가 변환 불필요
            if (randomValue <= blockChance)
            {
                damageInfo.SetBlock(true);
#if UNITY_EDITOR
                // 블록 이벤트 기록
                if (DamageEventRecorder.IsCollecting && damageInfo.SkillData != null)
                {
                    var attackerStatus = GameCharacterManager.Instance.GetCharacterStatusByUID(damageInfo.SkillData.AttackerCUID);
                    if (attackerStatus != null && attackerStatus.BasicStatus.IsPlayer)
                    {
                        DamageEventRecorder.RecordSpellBlocked(
                            damageInfo.SkillData.Skill,
                            damageInfo.SkillData.SkillTier,
                            damageInfo.SkillData.SkillTagDamageType);
                    }
                }
#endif
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Step 5: 크리티컬 데미지 적용
    /// 공식:
    /// - 일반 치명타: damage × criticalMultiplier
    /// - 치명타 일격: damage × criticalMultiplier × criticalBlowMultiplier (중첩 적용)
    /// </summary>
    private double ApplyCriticalDamage(FCharacterBaseStatus attackerStatus, FCharacterStatus InDefenderStatus,
        double damage, ref FCalcResultModDamage damageInfo)
    {
        var resultCriInfo = CalcResultCritical(attackerStatus, InDefenderStatus);
        bool isCritical = resultCriInfo.Item1;
        bool isCriticalBlow = resultCriInfo.Item2;

        double critMultiplier = 1.0;

        if (isCriticalBlow)
        {
            // 치명타 일격 = 일반 치명타 × 치명타 일격 (중첩 적용)
            // ResultCriticalMultiplier/ResultCriticalBlowmultiplier는 이미 비율(예: 1.5 = 150%)을 반환
            double normalCritMultiplier = attackerStatus.BattleStatus.ResultCriticalMultiplier(InDefenderStatus);
            double critBlowMultiplier = attackerStatus.BattleStatus.ResultCriticalBlowmultiplier();
            critMultiplier = normalCritMultiplier * critBlowMultiplier;
        }
        else if (isCritical)
        {
            // 일반 치명타만 - ResultCriticalMultiplier는 이미 비율을 반환
            critMultiplier = attackerStatus.BattleStatus.ResultCriticalMultiplier(InDefenderStatus);
        }

        damageInfo.SetCritical(isCritical);
        damageInfo.SetCriticalBlow(isCriticalBlow);

        return damage * critMultiplier;
    }

    /// <summary>
    /// Step 6-1: 물리 저항 적용 (원소 저항과 동일한 방식)
    ///
    /// 계산 순서:
    /// 1. 저항 최대치 적용 (저항 합계가 최대치를 초과하면 최대치로 제한)
    /// 2. 공격자의 적 저항 % 감소 적용 (곱셈)
    /// 3. 공격자의 저항 관통 차감 (뺄셈)
    /// 4. 최종 저항 클램핑 (-100% ~ 최대치)
    /// 5. 추가 물리 피해 감소 적용
    /// 6. 최종 피해 = (1 - 최종저항/100) * 원래 피해
    /// </summary>
    private double ApplyPhysicalDefense(FCharacterStatus InDefenderStatus, FCharacterBaseStatus attackerStatus, double damage)
    {
        // 방어자의 물리 저항 및 저항 최대치
        double baseResistance = InDefenderStatus.BattleStatus.ResultPhysicalResistance();
        double resistanceMax = InDefenderStatus.BattleStatus.ResultPhysicalResistanceMax();

        // 공격자의 관통(Penetration)과 감소(Reduction)
        double penetration = 0;
        double reduction = 0;

        if (attackerStatus != null)
        {
            penetration = attackerStatus.BattleStatus.ResultPhysicalResistancePenetration();
            reduction = attackerStatus.BattleStatus.ResultReductionEnemyPhysicalResistance();
        }

        // 최종 저항 계산 (BattleStatus의 static 메서드 사용)
        double finalResistance = FBattleStatus.CalculateFinalResistance(
            baseResistance,
            resistanceMax,
            penetration,
            reduction);

        // 저항에 의한 피해 감소율 (CalculateFinalResistance는 이미 ratio 반환)
        double resistanceReduction = finalResistance;

        // 추가 물리 피해 감소 (TotalModValue는 이미 ratio 반환: FLOAT_PER 20 → 0.2)
        double additionalReduction = InDefenderStatus.BattleStatus.ResultPhysicalDamageReduction();

        // 총 감소율 = 저항 감소 + 추가 감소
        double totalReduction = resistanceReduction + additionalReduction;

        // 최대 감소율 제한 (90% 하드캡)
        totalReduction = Math.Clamp(totalReduction, -1.0, GameGlobalConfig.resistance_reduction_cap);

        // 최종 피해 = 원래 피해 * (1 - 감소율)
        return damage * (1 - totalReduction);
    }

    /// <summary>
    /// Step 6-2: 원소 저항 적용
    ///
    /// 계산 순서:
    /// 1. 저항 최대치 적용 (저항 합계가 최대치를 초과하면 최대치로 제한)
    /// 2. 공격자의 적 저항 % 감소 적용 (곱셈)
    /// 3. 공격자의 저항 관통 차감 (뺄셈)
    /// 4. 최종 저항 클램핑 (-100% ~ 최대치)
    /// 5. 최종 피해 = (1 - 최종저항/100) * 원래 피해
    /// </summary>
    private double ApplyElementalResistance(FCharacterStatus InDefenderStatus, FCharacterBaseStatus attackerStatus,
        ESkillTag damageType, double damage)
    {
        // 방어자의 저항 및 저항 최대치 (Curse MOD 포함)
        double baseResistance = GetElementalResistance(InDefenderStatus, damageType, attackerStatus);
        double resistanceMax = GetElementalResistanceMax(InDefenderStatus, damageType);

        // 공격자의 관통(Penetration)과 감소(Reduction)
        double penetration = 0;
        double reduction = 0;

        if (attackerStatus != null)
        {
            switch (damageType)
            {
                case ESkillTag.skilltag_fire:
                    penetration = attackerStatus.BattleStatus.ResultFireResistancePenetration();
                    reduction = attackerStatus.BattleStatus.ResultReductionEnemyFireResistance();
                    break;
                case ESkillTag.skilltag_cold:
                    penetration = attackerStatus.BattleStatus.ResultColdResistancePenetration();
                    reduction = attackerStatus.BattleStatus.ResultReductionEnemyColdResistance();
                    break;
                case ESkillTag.skilltag_lightning:
                    penetration = attackerStatus.BattleStatus.ResultLightningResistancePenetration();
                    reduction = attackerStatus.BattleStatus.ResultReductionEnemyLightningResistance();
                    break;
                case ESkillTag.skilltag_poison:
                    penetration = attackerStatus.BattleStatus.ResultPoisonResistancePenetration();
                    reduction = attackerStatus.BattleStatus.ResultReductionEnemyPoisonResistance();
                    break;
            }
        }

        // 최종 저항 계산 (BattleStatus의 static 메서드 사용)
        double finalResistance = FBattleStatus.CalculateFinalResistance(
            baseResistance,
            resistanceMax,
            penetration,
            reduction);

        // 최종 피해 = (1 - 최종저항) * 원래 피해
        // CalculateFinalResistance는 이미 ratio 반환 (0.75 = 75%)
        double reductionRatio = finalResistance;

        // 최대 감소율 제한 (90% 하드캡 - 물리 저항과 동일)
        reductionRatio = Math.Clamp(reductionRatio, -1.0, GameGlobalConfig.resistance_reduction_cap);

        return (1 - reductionRatio) * damage;
    }

    /// <summary>
    /// 속성별 저항값 가져오기
    /// Curse MOD 적용: 공격자의 mod_cursed_enemy_*_resistance MOD가 방어자 저항에 영향
    /// </summary>
    private double GetElementalResistance(FCharacterStatus InDefenderStatus, ESkillTag damageType, FCharacterBaseStatus attackerStatus = null)
    {
        double baseResistance = 0;
        double cursedResistance = 0;
        double cursedResistanceRed = 0;

        switch (damageType)
        {
            case ESkillTag.skilltag_fire:
                baseResistance = InDefenderStatus.BattleStatus.ResultFireResistance();
                if (attackerStatus != null)
                {
                    cursedResistance = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_fire_resistance);
                    cursedResistanceRed = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_fire_resistance_red);
                }
                break;
            case ESkillTag.skilltag_cold:
                baseResistance = InDefenderStatus.BattleStatus.ResultColdResistance();
                if (attackerStatus != null)
                {
                    cursedResistance = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_cold_resistance);
                    cursedResistanceRed = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_cold_resistance_red);
                }
                break;
            case ESkillTag.skilltag_lightning:
                baseResistance = InDefenderStatus.BattleStatus.ResultLightningResistance();
                if (attackerStatus != null)
                {
                    cursedResistance = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_lightning_resistance);
                    cursedResistanceRed = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_lightning_resistance_red);
                }
                break;
            case ESkillTag.skilltag_poison:
                baseResistance = InDefenderStatus.BattleStatus.ResultPoisonResistance();
                if (attackerStatus != null)
                {
                    cursedResistance = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_poison_resistance);
                    cursedResistanceRed = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_poison_resistance_red);
                }
                break;
            default:
                return 0;
        }

        // Curse 저항: 기본 저항 + cursed_resistance + cursed_resistance_red (음수 값으로 감소)
        // mod_cursed_enemy_elemental_resistance도 추가 적용
        if (attackerStatus != null)
        {
            double elementalResistance = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_elemental_resistance);
            double elementalResistanceRed = attackerStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_elemental_resistance_red);
            return baseResistance + cursedResistance + cursedResistanceRed + elementalResistance + elementalResistanceRed;
        }

        return baseResistance;
    }

    /// <summary>
    /// 속성별 저항 최대치 가져오기
    /// </summary>
    private double GetElementalResistanceMax(FCharacterStatus InDefenderStatus, ESkillTag damageType)
    {
        switch (damageType)
        {
            case ESkillTag.skilltag_fire:
                return InDefenderStatus.BattleStatus.ResultFireResistanceMax();
            case ESkillTag.skilltag_cold:
                return InDefenderStatus.BattleStatus.ResultColdResistanceMax();
            case ESkillTag.skilltag_lightning:
                return InDefenderStatus.BattleStatus.ResultLightningResistanceMax();
            case ESkillTag.skilltag_poison:
                return InDefenderStatus.BattleStatus.ResultPoisonResistanceMax();
            default:
                return GameGlobalConfig.maxResistance * 100.0; // 75%
        }
    }

    /// <summary>
    /// Step 6.6: 즉사 판정 (Instant Kill Check)
    /// 적의 체력이 일정 % 이하일 때 즉시 처치
    /// 공식: if (currentHP / maxHP * 100 <= threshold) → instant kill
    /// </summary>
    private double ApplyInstantKill(FCharacterStatus InDefenderStatus,
        FCharacterBaseStatus attackerStatus, double damage)
    {
        // mod_instantkill_lowerlife 값 확인 (예: 10.0 = 10%)
        double instantKillThreshold = attackerStatus.BattleStatus.TotalModValue(
            EMod.mod_instantkill_lowerlife);

        if (instantKillThreshold <= 0)
        {
            return damage; // MOD가 없으면 그대로 반환
        }

        // 적의 현재 체력과 최대 체력 확인
        double currentHP = InDefenderStatus.BattleStatus.resultHp.Value;
        double maxHP = InDefenderStatus.BattleStatus.resultHpMax.Value;

        if (maxHP <= 0)
        {
            return damage; // 최대 체력이 0이면 그대로 반환
        }

        // 현재 체력 비율 계산 (%)
        double currentHPPercent = (currentHP / maxHP) * 100.0;

        // 체력이 threshold 이하면 즉시 처치
        if (currentHPPercent <= instantKillThreshold)
        {
            // 현재 체력 + 충분한 값을 반환하여 확실히 처치
            return currentHP + 9999999.0;
        }

        return damage; // 조건 미충족 시 원래 피해 반환
    }

    /// <summary>
    /// Step 6.5: [Defense] 5단계 - 받는 피해 증감 적용
    /// 공식: final_damage = damage × (1 + ΣenemyTakeInc / 100)
    /// </summary>
    /// <param name="InDefenderStatus">방어자 상태 (피해를 받는 대상)</param>
    /// <param name="attackerStatus">공격자 상태 (공격자의 MOD 적용)</param>
    /// <param name="damageType">피해 유형 (물리/원소)</param>
    /// <param name="damage">방어력/저항 적용 후 피해</param>
    /// <returns>받는 피해 증감 적용 후 최종 피해</returns>
    private double ApplyDamageTakenModifiers(FCharacterStatus InDefenderStatus, FCharacterBaseStatus attackerStatus, ESkillTag damageType, double damage)
    {
        double enemyTakeIncTotal = 0;

        // 물리 피해: mod_enemy_take_inc_physical_damage 적용
        if (damageType == ESkillTag.skilltag_physical)
        {
            double physicalTakeInc = attackerStatus.BattleStatus.TotalModValue(EMod.mod_enemy_take_inc_physical_damage);
            enemyTakeIncTotal += physicalTakeInc;

            // Curse 효과: mod_cursed_enemy_take_inc_physical_damage
            double cursedPhysicalTakeInc = InDefenderStatus.BattleStatus.TotalModValue(EMod.mod_cursed_enemy_take_inc_physical_damage);
            enemyTakeIncTotal += cursedPhysicalTakeInc;
        }
        // 향후 원소 피해용 MOD 추가 가능:
        // - mod_enemy_take_inc_fire_damage
        // - mod_enemy_take_inc_cold_damage
        // - mod_enemy_take_inc_lightning_damage
        // - mod_enemy_take_inc_poison_damage

        // 최종 피해 = 원래 피해 × (1 + 증가합)
        // FLOAT_PER MOD: TotalModValue()가 이미 비율 반환 (0.30 = 30%)
        // 예: enemyTakeIncTotal = 0.30 → damage × 1.30
        return damage * (1 + enemyTakeIncTotal);
    }

    /// <summary>
    /// 치명타 및 치명타 일격 판정
    /// </summary>
    /// <remarks>
    /// 치명타 시스템:
    /// - critChance >= 100%: 무조건 치명타 발생 + 치명타 일격(Critical Blow) 추가 판정
    /// - critChance < 100%: 일반 치명타 확률 판정
    ///
    /// Critical Blow(치명타 일격):
    /// - 치명타 확률 100% 이상일 때만 발동 가능
    /// - "치명타의 치명타"로 추가 피해 배율 적용
    /// - Critical Blow 실패 시에도 일반 치명타는 유지됨
    /// </remarks>
    public virtual (bool, bool) CalcResultCritical(FCharacterBaseStatus InAttackerStatus, FCharacterBaseStatus InDefenderStatus)
    {
        // 치명타 확률 계산 (ResultCriticalChance는 FLOAT_PER ratio 반환, 예: 0.3 = 30%)
        // GetRandomRatio()는 0.01~1.01을 반환하므로 직접 비교 가능
        double critChance = InAttackerStatus.BattleStatus.ResultCriticalChance(InDefenderStatus);
        double randValue = GameBattleUtilityManager.Instance.GetRandomRatio();

        bool isCritical = false;
        bool isCriticalBlow = false;

        // 치명타 일격 조건: 치명타 확률 >= 100% (ratio 1.0 이상)
        if (critChance >= 1.0)
        {
            // 100% 이상이면 무조건 치명타 발생
            isCritical = true;

            // 추가로 치명타 일격(Critical Blow) 판정
            double critBlowChance = InAttackerStatus.BattleStatus.ResultCriticalBlowChance();
            if (critBlowChance > 0 && randValue <= critBlowChance)
            {
                isCriticalBlow = true;
            }
        }
        else
        {
            // 일반 치명타 판정 (critChance < 100%)
            // 예: critChance = 0.3 → randValue(0.01~1.01) <= 0.3 → 약 30% 확률
            if (critChance > 0 && randValue <= critChance)
            {
                isCritical = true;
            }
        }

        return (isCritical, isCriticalBlow);
    }
    /// <summary>
    /// 일반 보스 종류를 스테이지 단위로 굳혀 쓸지.
    ///
    /// 기본은 굳힌다. 대부분의 모드는 스테이지당 보스가 한 번 나오므로 굳혀도 보이는 것이 달라지지 않고,
    /// 대신 종류가 미리 정해져 프리팹 프리로드와 사망 파쇄 분해 메시 프리워밍이 성립한다.
    ///
    /// 보스 러시·대균열처럼 그룹마다 보스가 나오는 모드는 굳히면 같은 보스만 스무 번 나오므로
    /// 그쪽에서 false 로 덮어 예전처럼 매번 추첨한다 (그 모드는 파쇄 메시를 미리 굽지 못한다)
    /// </summary>
    protected virtual bool UseStageFixedBossMonster => true;

    /// <summary>
    /// 현재 스테이지 타입에 맞는 일반 보스 후보 목록을 준비합니다. 후보가 하나도 없으면 false.
    /// 스테이지 타입이 바뀌면 목록과 고정 선택을 함께 버립니다
    /// </summary>
    private bool EnsureBossMonsterDBDataList()
    {
        if (battleModeBaseData == null || battleModeBaseData.battleInfoData == null)
            return false;

        EStage checkStageType = battleModeBaseData.battleInfoData.stageType;

        //스테이지 타입이 바뀌면 후보 목록을 다시 구성해야 한다.
        //예전 목록 기준으로 뽑아 둔 인덱스도 같이 버려야 엉뚱한 보스를 가리키지 않는다
        if (bossMonsterDBDataStageType != checkStageType)
        {
            bossMonsterDBDataList.Clear();
            bossMonsterDBDataStageType = checkStageType;
            stageFixedBossMonsterIndex = -1;
        }

        if (bossMonsterDBDataList.Count == 0)
        {
            // 성능 최적화: 간결한 조건문으로 변경
            foreach (KeyValuePair<EMonsterBoss, GameDB_Client_MonsterBoss> entry in GameDBClientManager.Instance.GameDB_Monster.MonsterBoss.MapData)
            {
                if (entry.Value.AvaiableStages.Contains(checkStageType))
                {
                    bossMonsterDBDataList.Add(entry);
                }
            }
        }

        return bossMonsterDBDataList.Count > 0;
    }

    /// <summary>
    /// 이번 스테이지에 쓸 일반 보스 종류를 확정합니다. 멱등이라 여러 번 불러도 같은 종류가 나옵니다.
    /// 재추첨은 ResetStageBossMonsterSelection() 으로만 일어납니다
    /// </summary>
    protected bool TryResolveStageBossMonster(out KeyValuePair<EMonsterBoss, GameDB_Client_MonsterBoss> OutMonster)
    {
        OutMonster = default;

        if (EnsureBossMonsterDBDataList() == false)
            return false;

        if (stageFixedBossMonsterIndex < 0 || stageFixedBossMonsterIndex >= bossMonsterDBDataList.Count)
        {
            stageFixedBossMonsterIndex = GameBattleUtilityManager.Instance.GetIntRandomMinMax(0, bossMonsterDBDataList.Count);
        }

        OutMonster = bossMonsterDBDataList[stageFixedBossMonsterIndex];

        return true;
    }

    /// <summary>일반 보스 종류를 다음 확정 시점에 다시 뽑도록 되돌립니다</summary>
    protected void ResetStageBossMonsterSelection()
    {
        stageFixedBossMonsterIndex = -1;
    }

    protected KeyValuePair<EMonsterBoss, GameDB_Client_MonsterBoss> GetSpawnBossMonster()
    {
        //스테이지 진입 때 확정해 둔 종류를 그대로 쓴다 (UseStageFixedBossMonster 주석 참고)
        if (UseStageFixedBossMonster
            && TryResolveStageBossMonster(out KeyValuePair<EMonsterBoss, GameDB_Client_MonsterBoss> fixedBoss))
        {
            return fixedBoss;
        }

        if (EnsureBossMonsterDBDataList())
        {
            int randIndex = GameBattleUtilityManager.Instance.GetIntRandomMinMax(0, bossMonsterDBDataList.Count);
            return bossMonsterDBDataList[randIndex];
        }
        else
        {
            EStage checkStageType = battleModeBaseData != null && battleModeBaseData.battleInfoData != null
                ? battleModeBaseData.battleInfoData.stageType
                : EStage.None;

            Debug.LogError($"GetSpawnBossMonster() - tempBossMonsterList.Count == 0, checkStageType = {checkStageType}");
            return new KeyValuePair<EMonsterBoss, GameDB_Client_MonsterBoss>(EMonsterBoss.None, null);
        }
    }

    protected KeyValuePair<EMonsterFloorBoss, GameDB_Client_MonsterFloorBoss> GetSpawnFloorBossMonster(int InFloor)
    {
        string floorBossKey = $"monster_floorboss_{InFloor}";
        EMonsterFloorBoss floorBossType = EMonsterFloorBoss.monster_floorboss_100;
        try
        {
            floorBossType = Enum.Parse<EMonsterFloorBoss>(floorBossKey);
        }
        catch (Exception ex)
        {
            Debug.LogError($"GetSpawnFloorBossMonster() - Exception, InFloor = {InFloor}, floorBossKey = {floorBossKey}, ex = {ex}");
        }

        if (GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBoss.MapData.ContainsKey(floorBossType))
        {
            GameDB_Client_MonsterFloorBoss floorBossMonsterData = GameDBClientManager.Instance.GameDB_Monster.MonsterFloorBoss.MapData[floorBossType];

            return new KeyValuePair<EMonsterFloorBoss, GameDB_Client_MonsterFloorBoss>(floorBossType, floorBossMonsterData);
        }
        else
        {
            Debug.LogError($"GetSpawnFloorBossMonster() - MonsterFloorBoss.MapData Not Found, floorBossType = {floorBossType}");
            return new KeyValuePair<EMonsterFloorBoss, GameDB_Client_MonsterFloorBoss>(EMonsterFloorBoss.None, null);
        }
    }

    /// <summary>
    /// 적의 상태이상에 따른 조건부 피해 증가 MOD 적용
    /// - mod_inc_damage_bleeding: 출혈 중인 적에게 모든 피해 증가
    /// - mod_inc_damage_ignite: 점화 중인 적에게 모든 피해 증가
    /// - mod_inc_damage_chill: 냉각 중인 적에게 모든 피해 증가
    /// - mod_inc_damage_arctic: 동결 중인 적에게 모든 피해 증가
    /// - mod_inc_damage_paralyze: 마비 중인 적에게 모든 피해 증가
    /// - mod_inc_damage_poisoning: 중독 중인 적에게 모든 피해 증가
    /// </summary>
    // 성능 최적화: BuffControl, BattleStatus 로컬 변수 캐싱으로 프로퍼티 접근 비용 감소
    private double ApplyConditionalDamageModifiers(
        FCharacterStatus InDefenderStatus,
        FCharacterBaseStatus InAttackerStatus,
        double InDamage)
    {
        if (InAttackerStatus == null || InDefenderStatus == null)
            return InDamage;

        var characterActor = InDefenderStatus.CharacterActor;
        if (characterActor == null)
            return InDamage;

        var buffControl = characterActor.BuffControl;
        if (buffControl == null)
            return InDamage;

        var attackerBattleStatus = InAttackerStatus.BattleStatus;
        double additionalIncPercent = 0;

        // 출혈 중인 적에게 피해 증가
        if (buffControl.GetBuffCount(EStatusEffect.ailment_bleeding) > 0)
            additionalIncPercent += attackerBattleStatus.TotalModValue(EMod.mod_inc_damage_bleeding);

        // 점화 중인 적에게 피해 증가
        if (buffControl.GetBuffCount(EStatusEffect.ailment_ignite) > 0)
            additionalIncPercent += attackerBattleStatus.TotalModValue(EMod.mod_inc_damage_ignite);

        // 냉각 중인 적에게 피해 증가
        if (buffControl.GetBuffCount(EStatusEffect.ailment_chill) > 0)
            additionalIncPercent += attackerBattleStatus.TotalModValue(EMod.mod_inc_damage_chill);

        // 동결 중인 적에게 피해 증가
        if (buffControl.GetBuffCount(EStatusEffect.ailment_arctic) > 0)
            additionalIncPercent += attackerBattleStatus.TotalModValue(EMod.mod_inc_damage_arctic);

        // 마비 중인 적에게 피해 증가
        if (buffControl.GetBuffCount(EStatusEffect.ailment_paralyze) > 0)
            additionalIncPercent += attackerBattleStatus.TotalModValue(EMod.mod_inc_damage_paralyze);

        // 중독 중인 적에게 피해 증가
        if (buffControl.GetBuffCount(EStatusEffect.ailment_poisoning) > 0)
            additionalIncPercent += attackerBattleStatus.TotalModValue(EMod.mod_inc_damage_poisoning);

        // 조건부 Inc 적용: damage × (1 + additionalInc)
        if (additionalIncPercent != 0)
            return InDamage * (1 + additionalIncPercent);

        return InDamage;
    }

    /// <summary>
    /// Step 6.6: 멀티플 피해 적용 (2배/3배)
    /// 방어/저항 및 받는 피해 증감 이후, 즉사 판정 이전에 적용 (최종 피해 기준)
    ///
    /// 확률 판정:
    /// - 3배 피해 확률 체크 (우선순위 높음)
    /// - 2배 피해 확률 체크
    /// - 둘 다 실패 시 원래 피해 유지
    ///
    /// 공식:
    /// - randomValue <= tripleDamageChance → damage × 3.0
    /// - randomValue <= doubleDamageChance → damage × 2.0
    /// - else → damage × 1.0
    /// </summary>
    private double ApplyMultipleDamage(FCharacterBaseStatus attackerStatus, double damage)
    {
        if (attackerStatus == null)
            return damage;

        // 3배 피해 확률 (mod_chance_to_triple_damage: FLOAT_PER, 예: 10.0 = 10%)
        double tripleDamageChance = attackerStatus.BattleStatus.TotalModValue(EMod.mod_chance_to_triple_damage);

        // 2배 피해 확률 (mod_chance_to_double_damage: FLOAT_PER, 예: 20.0 = 20%)
        double doubleDamageChance = attackerStatus.BattleStatus.TotalModValue(EMod.mod_chance_to_double_damage);

        // 확률이 0이면 바로 리턴 (성능 최적화)
        if (tripleDamageChance <= 0 && doubleDamageChance <= 0)
            return damage;

        // 랜덤 값 생성 (0.01~1.01 범위)
        double randomValue = GameBattleUtilityManager.Instance.GetRandomRatio();

        // 3배 피해 판정 (우선순위 높음)
        // TotalModValue는 이미 비율 반환 (0.1 = 10%)
        if (tripleDamageChance > 0 && randomValue <= tripleDamageChance)
        {
            return damage * GameClientPlayConfig.Instance.combat.tripleDamageMultiplier;
        }

        // 2배 피해 판정
        if (doubleDamageChance > 0 && randomValue <= doubleDamageChance)
        {
            return damage * GameClientPlayConfig.Instance.combat.doubleDamageMultiplier;
        }

        // 둘 다 실패 시 원래 피해
        return damage;
    }

    /// <summary>
    /// 웨이브(그룹) 1회당 실제로 소환할 일반 몬스터 수.
    /// 시트데이터(num_monster_per_group)에 GameClientPlayConfig의 테스트 오버라이드/배수를 적용한다.
    /// 실제 스폰 루프와 진행도/보상 분배 총량이 이 값을 공통으로 사용해야 서로 어긋나지 않는다.
    /// </summary>
    protected int GetMonsterCountPerGroup()
    {
        GameDB_Client_Stage stageData = GetCurrentStageData();

        if (stageData == null)
        {
            Debug.LogError("GetMonsterCountPerGroup() - stageData is null");
            return 0;
        }

        return GameClientPlayConfig.Instance.debug.ResolveMonsterCountPerGroup(stageData.MonsterCountPerGroup.Value);
    }

    /// <summary>
    /// 스테이지 1회(루프)에 소환할 웨이브(그룹) 수.
    /// 시트데이터(num_group)에 GameClientPlayConfig의 테스트 오버라이드를 적용한다.
    ///
    /// 스테이지 진입 시 한 번만 읽히므로, 값을 바꾸면 다음 스테이지 진입부터 반영된다.
    /// 진행도 총량(_allGroupMonsterCount)도 이 값으로 계산되어야 웨이브 진행과 어긋나지 않는다.
    /// </summary>
    protected int GetWaveCount()
    {
        GameDB_Client_Stage stageData = GetCurrentStageData();

        if (stageData == null)
        {
            Debug.LogError("GetWaveCount() - stageData is null");
            return 0;
        }

        return GameClientPlayConfig.Instance.debug.ResolveWaveCount(stageData.GroupCount.Value);
    }

    /// <summary>
    /// 다음 웨이브를 소환할 잔여 적 수 기준.
    /// 이번 웨이브에 소환한 마릿수 대비 remainEnemyCheckRate 비율만큼 남으면 다음 웨이브가 나온다.
    /// (예전에는 스테이지 누적 스폰 수를 기준으로 삼아 웨이브가 진행될수록 기준값이 커졌고,
    ///  그래서 앞 웨이브가 정리되기도 전에 다음 웨이브가 겹쳐 나와 몬스터가 계속 쌓였다)
    /// </summary>
    protected int GetNextWaveSpawnRemainCount()
    {
        int waveSpawnCount = GetMonsterCountPerGroup();

        return Mathf.RoundToInt(waveSpawnCount * GameClientPlayConfig.Instance.stage.remainEnemyCheckRate);
    }

    //스폰 지점 후보를 담아두는 재사용 버퍼. 웨이브당 한 번만 쓰이므로 매번 새로 만들지 않는다
    private readonly List<int> spawnFieldCandidateBuffer = new List<int>();

    /// <summary>
    /// 이번 웨이브의 몬스터 스폰 지점을 고른다.
    ///
    /// 기본(랜덤 선택)은 네 모서리 중 무작위로 뽑되 직전 웨이브가 쓴 지점을 빼서 2연속 중복을 막는다.
    /// 플레이어와 너무 가까운 지점은 후보에서 제외하지만, 그렇게 걸러 한 곳만 남으면
    /// 매 웨이브 같은 자리에서만 나오게 되므로 거리 조건을 풀고 전체에서 뽑는다.
    /// (spawnMinDistanceFromPlayer 를 낮출수록 통과하는 후보가 늘어 방향이 다양해진다)
    ///
    /// 랜덤 선택을 끄면 예전처럼 후보 리스트를 순서대로 도는 방식으로 되돌아간다.
    /// </summary>
    /// <param name="InCandidateList">스폰 타일 후보 (필드 네 모서리)</param>
    /// <param name="InOutNextIndex">순환 방식이 쓰는 다음 인덱스. 랜덤 방식에서는 건드리지 않는다</param>
    /// <param name="InLastFieldNo">직전 웨이브가 사용한 스폰 타일</param>
    /// <param name="InForceFarthest">거리와 무관하게 무조건 가장 먼 지점을 쓸지 (스테이지 첫 웨이브용)</param>
    protected int PickSpawnFieldNo(List<int> InCandidateList, ref int InOutNextIndex, int InLastFieldNo, bool InForceFarthest)
    {
        if (InCandidateList == null || InCandidateList.Count == 0)
            return 0;

        if (InCandidateList.Count == 1)
            return InCandidateList[0];

        if (GameClientPlayConfig.Instance.spawn.spawnPointRandomPick == false)
            return PickSpawnFieldNoSequential(InCandidateList, ref InOutNextIndex, InLastFieldNo, InForceFarthest);

        //첫 웨이브는 방향 다양성보다 "멀리서 밀려오는 그림"이 우선이다
        if (InForceFarthest)
            return SelectSpawnFieldNo(InCandidateList[0], InCandidateList, true);

        CollectDistantSpawnFieldNo(InCandidateList, spawnFieldCandidateBuffer);

        //거리 조건을 통과한 곳이 하나뿐이면 결국 매번 같은 지점이 된다. 그럴 바엔 전체에서 뽑는다
        if (spawnFieldCandidateBuffer.Count < 2)
        {
            spawnFieldCandidateBuffer.Clear();
            spawnFieldCandidateBuffer.AddRange(InCandidateList);
        }

        //직전 웨이브와 같은 지점 제외. 모두 제외되면(후보가 그것뿐이면) 그대로 둔다
        for (int i = spawnFieldCandidateBuffer.Count - 1; i >= 0; i--)
        {
            if (spawnFieldCandidateBuffer.Count <= 1)
                break;

            if (spawnFieldCandidateBuffer[i] == InLastFieldNo)
                spawnFieldCandidateBuffer.RemoveAt(i);
        }

        int pickIndex = GameBattleUtilityManager.Instance.GetIntRandomMinMax(0, spawnFieldCandidateBuffer.Count);

        return spawnFieldCandidateBuffer[pickIndex];
    }

    /// <summary>
    /// 플레이어에게서 spawnMinDistanceFromPlayer 이상 떨어진 스폰 지점만 모은다.
    /// 거리 판정이 불가능한 상황(전투 필드/플레이어 미생성)에서는 전체를 그대로 돌려준다.
    /// </summary>
    private void CollectDistantSpawnFieldNo(List<int> InCandidateList, List<int> OutResultList)
    {
        OutResultList.Clear();

        float minDistance = GameClientPlayConfig.Instance.spawn.spawnMinDistanceFromPlayer;
        BattleFieldComponent fieldComp = GameBattleControlManager.Instance.GetBattleFieldComponent;
        UCharacterActor player = GameBattleControlManager.Instance.CurrentMainCharacter;

        if (minDistance <= 0 || fieldComp == null || player == null)
        {
            OutResultList.AddRange(InCandidateList);
            return;
        }

        Vector3 playerPosition = player.GetTransform.position;

        foreach (int candidateFieldNo in InCandidateList)
        {
            if (GetSpawnFieldDistance(fieldComp, candidateFieldNo, playerPosition) >= minDistance)
                OutResultList.Add(candidateFieldNo);
        }
    }

    /// <summary>
    /// 예전 방식 — 후보 리스트를 순서대로 돌면서 뽑고, 거리 조건에 걸리면 가장 먼 지점으로 교체한다.
    /// 플레이어가 필드 가장자리에 서 있으면 교체가 매번 걸려 사실상 한두 지점만 반복된다.
    /// </summary>
    private int PickSpawnFieldNoSequential(List<int> InCandidateList, ref int InOutNextIndex, int InLastFieldNo, bool InForceFarthest)
    {
        if (InOutNextIndex < 0 || InOutNextIndex >= InCandidateList.Count)
            InOutNextIndex = 0;

        int spawnFieldNo = InCandidateList[InOutNextIndex];

        InOutNextIndex++;
        if (InOutNextIndex >= InCandidateList.Count)
            InOutNextIndex = 0;

        if (spawnFieldNo == InLastFieldNo)
            spawnFieldNo = InCandidateList[InOutNextIndex];

        spawnFieldNo = SelectSpawnFieldNo(spawnFieldNo, InCandidateList, InForceFarthest);

        //매번 같은 순서로 돌지 않도록 후보 하나를 뒤로 보낸다 (기존 동작 유지)
        int randIndex = GameBattleUtilityManager.Instance.GetIntRandomMinMax(0, InCandidateList.Count);
        int rotateValue = InCandidateList[randIndex];
        InCandidateList.RemoveAt(randIndex);
        InCandidateList.Add(rotateValue);

        return spawnFieldNo;
    }

    /// <summary>
    /// 스폰 지점이 플레이어와 너무 가까우면 가장 먼 후보로 교체한다.
    /// 플레이어가 필드 가장자리에 있으면 네 모서리 중 등 뒤쪽 지점은 코앞이 되므로 그대로 쓰면 둘러싸인 형태가 된다.
    /// </summary>
    /// <param name="InPickedFieldNo">순환 로직이 1차로 뽑은 스폰 타일</param>
    /// <param name="InCandidateList">전체 스폰 타일 후보</param>
    /// <param name="InForceFarthest">거리와 무관하게 무조건 가장 먼 지점을 쓸지 (스테이지 첫 웨이브용)</param>
    protected int SelectSpawnFieldNo(int InPickedFieldNo, List<int> InCandidateList, bool InForceFarthest = false)
    {
        if (InCandidateList == null || InCandidateList.Count <= 1)
            return InPickedFieldNo;

        float minDistance = GameClientPlayConfig.Instance.spawn.spawnMinDistanceFromPlayer;

        if (InForceFarthest == false && minDistance <= 0)
            return InPickedFieldNo;

        BattleFieldComponent fieldComp = GameBattleControlManager.Instance.GetBattleFieldComponent;
        UCharacterActor player = GameBattleControlManager.Instance.CurrentMainCharacter;

        if (fieldComp == null || player == null)
            return InPickedFieldNo;

        Vector3 playerPosition = player.GetTransform.position;

        //1차 선택 지점이 이미 충분히 멀면 그대로 사용해 스폰 방향의 다양성을 유지한다
        if (InForceFarthest == false && GetSpawnFieldDistance(fieldComp, InPickedFieldNo, playerPosition) >= minDistance)
            return InPickedFieldNo;

        int farthestFieldNo = InPickedFieldNo;
        float farthestDistance = -1f;

        foreach (int candidateFieldNo in InCandidateList)
        {
            float distance = GetSpawnFieldDistance(fieldComp, candidateFieldNo, playerPosition);

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthestFieldNo = candidateFieldNo;
            }
        }

        return farthestFieldNo;
    }

    private float GetSpawnFieldDistance(BattleFieldComponent InFieldComp, int InFieldNo, Vector3 InPlayerPosition)
    {
        int tileID = GameBattleControlManager.Instance.ConvertSpawnNoToFieldNo(InFieldNo);

        if (InFieldComp.TryGetTilePosition(tileID, out Vector3 tilePosition) == false)
            return -1f;

        Vector3 delta = tilePosition - InPlayerPosition;
        delta.y = 0;

        return delta.magnitude;
    }

    /// <summary>
    /// 서버에 전송할 총 처치 수.
    /// 스폰 마릿수를 시트데이터와 다르게 조정한 상태면 클라 킬 카운트가 서버 기준과 달라지므로,
    /// 시트데이터의 총 몬스터 수(AllMonsterCount)로 대체해 클리어 검증이 깨지지 않게 한다.
    ///
    /// 마릿수 조정(GameClientPlayConfig)은 빌드에도 적용되므로 이 보정도 빌드에서 함께 동작해야 한다.
    /// 1마리 스폰 치트는 EditorPrefs 기반이라 에디터에서만 판정한다.
    /// </summary>
    protected int GetServerTotalKillCount()
    {
        int totalKill = battleInfoData.TotalKilledEnemyCount;

        bool isCountModified = GameClientPlayConfig.Instance.debug.IsMonsterCountModified;

#if UNITY_EDITOR
        isCountModified |= GameBattleControlManager.DebugSingleMonsterSpawn;
#endif

        if (isCountModified)
        {
            Dictionary<EStage, GameDB_Client_Stage> stageMapData = GameDBClientManager.Instance.GameDB_Stage.Stage.MapData;

            if (stageMapData.TryGetValue(battleInfoData.stageType, out GameDB_Client_Stage sheetStageData))
            {
                totalKill = sheetStageData.AllMonsterCount.Value;
            }
        }

        return totalKill;
    }

    /// <summary>
    /// 이번 스테이지에 쓸 일반 몬스터 종류를 확정합니다. 멱등이라 여러 번 불러도 같은 종류가 나옵니다.
    /// 재추첨은 ResetStageNormalMonsterSelection() 으로만 일어납니다
    /// </summary>
    protected bool TryResolveStageNormalMonster(out KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal> OutMonster)
    {
        OutMonster = default;

        if (battleModeBaseData == null || battleModeBaseData.battleInfoData == null)
            return false;

        //현재 스테이지 타입
        EStage checkStageType = battleModeBaseData.battleInfoData.stageType;

        //스테이지 타입이 바뀌면 후보 목록을 다시 구성해야 한다
        if (normalMonsterDBDataStageType != checkStageType)
        {
            normalMonsterDBDataList.Clear();
            normalMonsterDBDataStageType = checkStageType;
        }

        if (normalMonsterDBDataList.Count == 0)
        {
            // 성능 최적화: Contains + FindIndex 중복 제거 (람다 생성 비용 제거)
            foreach (KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal> entry in GameDBClientManager.Instance.GameDB_Monster.MonsterNormal.MapData)
            {
                if (entry.Value.AvaiableStages.Contains(checkStageType))
                {
                    normalMonsterDBDataList.Add(entry);
                }
            }
        }

        if (normalMonsterDBDataList.Count == 0)
            return false;

        //종류는 스테이지(레벨) 단위로 한 번만 뽑고 그 안의 웨이브들은 그대로 재사용한다.
        //웨이브마다 다시 뽑으면 새 종류의 프리팹을 매번 동기 로드하고 풀도 비어 있어 소환 시 프레임이 튄다
        if (stageFixedNormalMonsterIndex < 0 || stageFixedNormalMonsterIndex >= normalMonsterDBDataList.Count)
        {
            stageFixedNormalMonsterIndex = GameBattleUtilityManager.Instance.GetIntRandomMinMax(0, normalMonsterDBDataList.Count);
        }

        OutMonster = normalMonsterDBDataList[stageFixedNormalMonsterIndex];

        return true;
    }

    /// <summary>일반 몬스터 종류를 다음 확정 시점에 다시 뽑도록 되돌립니다</summary>
    protected void ResetStageNormalMonsterSelection()
    {
        stageFixedNormalMonsterIndex = -1;
    }

    #region 콜드 스폰 최적화 (프리로드 + 풀 프리워밍)

    private const string PrewarmCoroutineKey = "PrewarmCharacterPool";
    private const string PrewarmEffectCoroutineKey = "PrewarmEffectPool";

    //프리로드 대기 상태. 기다릴 것이 없으면 true
    private bool isSpawnPreloadDone = true;

    //프리워밍 대상 이펙트 키. 스테이지마다 다시 채우는 재사용 버퍼
    private readonly List<(string bundle, string name)> prewarmEffectKeyList = new List<(string bundle, string name)>();

    /// <summary>
    /// 스테이지(레벨) 진입 시 이번 판에 쓸 몬스터 종류를 확정하고 프리팹을 비동기로 미리 로드합니다.
    /// 소환 시작 전에 못 끝내도 기존 동기 로드 경로로 폴백되므로 안전합니다
    /// </summary>
    protected void PrepareStageSpawnAssets()
    {
        PerformanceSettings config = GameClientPlayConfig.Instance.performance;

        //로그인 모드처럼 스테이지 데이터가 없는 모드는 대상이 아니다
        if (GetCurrentStageData() == null)
            return;

        //이펙트 프리워밍은 몬스터 프리로드와 독립적으로 동작한다
        TryStartEffectPrewarm();

        //보스 종류도 스테이지 레벨마다 다시 뽑는다. 이 재추첨만은 프리로드 설정보다 앞에 둬야 한다 —
        //뒤에 두면 프리로드를 꺼 둔 판에서 이전 스테이지의 보스가 그대로 굳어 계속 같은 보스가 나온다
        ResetStageBossMonsterSelection();

        if (config.enableSpawnPreload == false)
            return;

        //이번 판에 나올 보스의 파쇄 분해 메시를 미리 굽는다.
        //일반 몬스터처럼 캐릭터 풀까지 프리워밍하지는 않는다 — 보스는 한 마리뿐이라 풀 미스가 한 번이고,
        //무거운 쪽은 분해 메시다
        PrepareStageBossShatterMesh();

        //몬스터 종류는 스테이지 레벨마다 다시 뽑는다 (다양성 우선). 재추첨 시점만 소환 직전에서 진입 시점으로 앞당긴 것이다
        ResetStageNormalMonsterSelection();

        if (TryResolveStageNormalMonster(out KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal> selectedMonster) == false)
            return;

        string prefabName = selectedMonster.Value != null ? selectedMonster.Value.Prefab : null;

        if (string.IsNullOrEmpty(prefabName))
            return;

        isSpawnPreloadDone = false;

        GameAssetBundleManager.Instance.LoadFromFileAsync<GameObject>("character", prefabName, loadedPrefab =>
        {
            isSpawnPreloadDone = true;

            //콜백이 오기까지 사이에 모드가 닫혔을 수 있다
            if (battleModeBaseData == null || battleModeBaseData.battleInfoData == null)
                return;

            if (loadedPrefab == null)
                return;

            //사망 파쇄용 분해 메시도 여기서 미리 만들어 둔다.
            //첫 처치 순간에 만들면 조각 분해 배열 생성이 그대로 프레임에 얹힌다.
            //스테이지 내내 일반 몬스터 종류가 1종으로 고정되므로 캐시는 이 한 종류로 유지된다.
            MonsterShatterMeshCache.PrewarmPrefab(loadedPrefab);

            TryStartPrewarm(selectedMonster, prefabName);
        });
    }

    /// <summary>
    /// 이번 스테이지에 나올 보스의 사망 파쇄용 분해 메시를 미리 굽습니다.
    ///
    /// 굽지 않고 두면 그 종류가 처음 죽는 프레임에 통째로 구워진다 (실측 보스 43종 평균 10ms, 최악 86ms).
    /// 보스가 죽는 순간은 연출이 가장 몰리는 자리라 여기서 튀면 특히 눈에 띈다.
    /// 캐시는 스테이지를 나갈 때 MonsterShatterRunner.ClearAll() 이 비우므로 판마다 다시 필요하다.
    ///
    /// 보스가 그룹마다 바뀌는 모드(UseStageFixedBossMonster == false)는 미리 정할 대상이 없어 건너뛴다.
    /// 모드마다 나오는 보스가 달라 확장 지점을 열어 둔다 (메인 스테이지는 층보스가 따로 있다)
    /// </summary>
    protected virtual void PrepareStageBossShatterMesh()
    {
        if (UseStageFixedBossMonster == false)
            return;

        if (TryResolveStageBossMonster(out KeyValuePair<EMonsterBoss, GameDB_Client_MonsterBoss> bossMonster) == false)
            return;

        PrewarmMonsterShatterMesh(bossMonster.Value != null ? bossMonster.Value.Prefab : null);
    }

    /// <summary>
    /// 몬스터 프리팹을 비동기로 받아 사망 파쇄용 분해 메시를 미리 굽습니다.
    /// 이미 구워져 있으면 아무 일도 하지 않습니다 (MonsterShatterMeshCache 가 판단)
    /// </summary>
    protected void PrewarmMonsterShatterMesh(string InPrefabName)
    {
        if (string.IsNullOrEmpty(InPrefabName))
            return;

        DeathSettings death = GameClientPlayConfig.Instance.death;

        //파쇄를 꺼 둔 판에서는 분해 메시가 아예 쓰이지 않는다
        if (death == null || death.shatterEnabled == false)
            return;

        GameAssetBundleManager.Instance.LoadFromFileAsync<GameObject>("character", InPrefabName, loadedPrefab =>
        {
            //콜백이 오기까지 사이에 모드가 닫혔을 수 있다
            if (battleModeBaseData == null || battleModeBaseData.battleInfoData == null)
                return;

            if (loadedPrefab == null)
                return;

            MonsterShatterMeshCache.PrewarmPrefab(loadedPrefab);
        });
    }

    /// <summary>
    /// 프리팹 로드/프리워밍이 끝날 때까지 한도 안에서 기다립니다.
    /// 한도를 넘으면 그냥 진행하고 기존 동기 로드 경로로 폴백됩니다
    /// </summary>
    protected IEnumerator WaitSpawnPreloadRator()
    {
        if (isSpawnPreloadDone)
            yield break;

        float maxWaitSec = GameClientPlayConfig.Instance.performance.spawnPreloadMaxWaitSec;
        float waitedSec = 0f;

        while (isSpawnPreloadDone == false && waitedSec < maxWaitSec)
        {
            //전투 배속·일시정지의 영향을 받으면 안 되는 대기다
            waitedSec += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void TryStartPrewarm(KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal> InMonster, string InPrefabName)
    {
        if (GameClientPlayConfig.Instance.performance.characterPrewarmMode == ECharacterPrewarmMode.Off)
            return;

        //같은 키로 다시 부르면 이전 코루틴이 자동으로 끊기고, 모드 종료 시 AllClearBattleNetwork()가 정리한다
        RequestBattleNetwork(PrewarmCoroutineKey, PrewarmCharacterPoolRator(InMonster, InPrefabName));
    }

    /// <summary>
    /// 캐릭터 풀을 목표 마릿수까지 조금씩 채웁니다.
    /// 캐릭터 풀은 세션 내내 비워지지 않으므로 현재 보유 수를 보고 이미 충분하면 아무것도 하지 않습니다
    /// </summary>
    private IEnumerator PrewarmCharacterPoolRator(KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal> InMonster, string InPrefabName)
    {
        PerformanceSettings config = GameClientPlayConfig.Instance.performance;

        int targetCount = config.prewarmCharacterCount > 0 ? config.prewarmCharacterCount : GetMonsterCountPerGroup();
        targetCount = Mathf.Min(targetCount, config.prewarmPoolMaxCount);

        if (targetCount <= 0)
            yield break;

        int countPerFrame = Mathf.Max(1, config.prewarmCountPerFrame);

        //첫 프레임은 1마리만 만든다.
        //프리팹의 첫 인스턴스는 메시·텍스처 GPU 업로드가 한꺼번에 일어나 실기에서 약 380ms 걸리는데(2마리째부터는 4~8ms),
        //이 비용은 나눌 수 없다. 같은 프레임에 다른 마리까지 얹으면 그 프레임만 더 길어질 뿐이라 혼자 치르게 둔다
        int currentFrameQuota = 1;

        int monsterLevel = GetMonsterLevel();
        Vector3 prewarmPosition = GetPrewarmPosition();
        int madeInFrame = 0;

        while (GameObjectPoolManager.Instance.GetPoolCharacterCount(InPrefabName) < targetCount)
        {
            //실제 소환이 시작되면 프레임 예산을 양보한다. 둘이 겹치면 오히려 악화된다
            if (IsWaveSpawning)
                yield break;

            if (battleModeBaseData == null || battleModeBaseData.battleInfoData == null)
                yield break;

            //스폰 타일과 그룹은 프리워밍에서 의미가 없다 (위치를 직접 지정하고 카운터에도 등록하지 않는다)
            FCharacterSingleData prewarmStatus = new FMonsterNormalSingleData(monsterLevel, FCommonDefine.EnemyTeamNo, 0, 0,
                                                                             InMonster.Key, InMonster.Value);

            bool isSuccess = config.characterPrewarmMode == ECharacterPrewarmMode.Full
                ? GameCharacterManager.Instance.PrewarmCharacterToPool(prewarmStatus, prewarmPosition)
                : GameCharacterManager.Instance.PrewarmCharacterPrefabToPool(prewarmStatus);

            //프리팹 로드 실패 등. 그대로 두면 무한 루프가 된다
            if (isSuccess == false)
                yield break;

            madeInFrame++;

            if (madeInFrame >= currentFrameQuota)
            {
                madeInFrame = 0;

                //첫 프레임을 넘겼으면 이후로는 설정값대로 채운다
                currentFrameQuota = countPerFrame;

                yield return null;
            }
        }
    }

    private void TryStartEffectPrewarm()
    {
        PerformanceSettings config = GameClientPlayConfig.Instance.performance;

        if (config.effectPrewarmMode == EEffectPrewarmMode.Off)
            return;

        if (config.prewarmCountPerKey <= 0)
            return;

        //같은 키로 다시 부르면 이전 코루틴이 자동으로 끊기고, 모드 종료 시 AllClearBattleNetwork()가 정리한다
        RequestBattleNetwork(PrewarmEffectCoroutineKey, PrewarmEffectPoolRator());
    }

    /// <summary>
    /// 장착 스킬이 쓰는 이펙트를 프레임에 나눠 미리 만들어 풀에 넣습니다.
    /// 첫 히트/첫 시전 프레임의 로드 + Instantiate 스파이크를 없애는 것이 목적입니다
    /// </summary>
    private IEnumerator PrewarmEffectPoolRator()
    {
        PerformanceSettings config = GameClientPlayConfig.Instance.performance;

        //장착 스킬 컨트롤러가 붙기 전에 수집하면 공용 이펙트만 모인다.
        //메인 캐릭터가 준비될 때까지 한도 안에서 기다린다 (못 기다려도 공용 키는 챙긴다)
        const int maxWaitFrame = 30;

        for (int waitFrame = 0; waitFrame < maxWaitFrame; waitFrame++)
        {
            yield return null;

            if (GameBattleControlManager.Instance.CurrentMainCharacter != null)
                break;
        }

        //사망 효과음 클립도 같이 미리 받아 둔다. 첫 사망은 몰려서 오는데
        //그때 Addressables 로드를 기다리면 타격음이 한 박자 늦게 붙는다
        PreloadDeadSound();

        GameEffectManager.Instance.CollectStagePrewarmEffects(prewarmEffectKeyList);

        if (prewarmEffectKeyList.Count == 0)
            yield break;

        int countPerFrame = Mathf.Max(1, config.prewarmMaxPerFrame);
        int madeInFrame = 0;

        for (int i = 0; i < prewarmEffectKeyList.Count; i++)
        {
            (string bundle, string name) effectKey = prewarmEffectKeyList[i];

            //목표 개수는 키마다 다르다 (ResolveEffectPrewarmCount 주석 참고)
            int targetCount = ResolveEffectPrewarmCount(effectKey);

            while (GameObjectPoolManager.Instance.GetPoolEffectCount(effectKey.bundle, effectKey.name) < targetCount)
            {
                //실제 소환이 시작되면 프레임 예산을 양보한다. 둘이 겹치면 오히려 악화된다
                if (IsWaveSpawning)
                    yield break;

                if (battleModeBaseData == null || battleModeBaseData.battleInfoData == null)
                    yield break;

                //프리팹이 없는 키는 그대로 두면 무한 루프가 된다
                if (GameObjectPoolManager.Instance.PrewarmEffectData(effectKey.bundle, effectKey.name) == false)
                    break;

                madeInFrame++;

                if (madeInFrame >= countPerFrame)
                {
                    madeInFrame = 0;
                    yield return null;
                }
            }
        }
    }

    /// <summary>
    /// 이 이펙트를 몇 개나 미리 만들어 둘지 정합니다.
    ///
    /// 기본은 전역값(performance.prewarmCountPerKey)이지만 사망 이펙트만 따로 받는다.
    /// 스킬 히트 이펙트가 한두 개씩 띄엄띄엄 쓰이는 것과 달리 사망 이펙트는 한 무리가 통째로 죽으면
    /// 스무 개가 2~3초 안에 겹친다. 풀에 없는 만큼은 죽는 그 프레임에 새로 만들어지는데,
    /// 파티클 시스템이 10개 붙은 프리팹이라 생성 비용이 1개당 1ms를 넘는다 (실측 1.14ms).
    /// 그렇다고 전역값을 그 개수에 맞춰 올리면 거의 안 쓰는 이펙트까지 같은 개수로 만들어져
    /// 전투 진입 시간과 메모리만 낭비된다.
    ///
    /// 풀 상한보다 큰 목표를 두면 AddEffectData 가 넣지 않고 파괴해 보유 수가 영영 목표에 못 미치고,
    /// 그대로 두면 호출부의 while 이 한 프레임 안에서 무한 루프가 된다. 반드시 상한으로 잘라야 한다
    /// </summary>
    private int ResolveEffectPrewarmCount((string bundle, string name) InEffectKey)
    {
        PerformanceSettings config = GameClientPlayConfig.Instance.performance;
        DeathSettings death = GameClientPlayConfig.Instance.death;

        int targetCount = config.prewarmCountPerKey;

        if (death != null
            && death.deadEffectPrewarmCount > 0
            && InEffectKey.bundle == FCommonDefine.ConstBundleEffectSkill
            && InEffectKey.name == death.deadEffectName)
        {
            targetCount = death.deadEffectPrewarmCount;
        }

        if (config.effectPoolMaxPerKey > 0)
            targetCount = Mathf.Min(targetCount, config.effectPoolMaxPerKey);

        return targetCount;
    }

    /// <summary>
    /// 설정에 선택된 사망 효과음 클립을 미리 로드합니다.
    /// 이미 캐시에 있으면 아무 일도 하지 않습니다
    /// </summary>
    private void PreloadDeadSound()
    {
        DeathSettings death = GameClientPlayConfig.Instance != null ? GameClientPlayConfig.Instance.death : null;

        if (death == null || death.deadSFXEnabled == false)
            return;

        if (GameSoundManager.Instance == null)
            return;

        GameSoundManager.Instance.PreloadSFX(death.GetDeadSFXName());
    }

    /// <summary>
    /// 프리워밍 개체를 놓을 위치. Full 모드는 NavMeshAgent 를 켜므로 반드시 NavMesh 위여야 합니다.
    /// 아니면 인스턴스마다 "Failed to create agent" 경고가 쏟아집니다
    /// </summary>
    private Vector3 GetPrewarmPosition()
    {
        UCharacterActor mainCharacter = GameBattleControlManager.Instance.CurrentMainCharacter;

        if (mainCharacter != null)
            return mainCharacter.GetTransform.position;

        BattleFieldComponent fieldComponent = GameBattleControlManager.Instance.GetBattleFieldComponent;

        if (fieldComponent != null)
            return fieldComponent.transform.position;

        return Vector3.zero;
    }

    #endregion

    /// <summary>
    /// 이번 웨이브에 소환할 일반 몬스터의 종류와 마릿수를 돌려줍니다.
    /// 예전에는 같은 KeyValuePair 를 마릿수만큼 복제한 List 를 웨이브마다 새로 만들었다
    /// </summary>
    protected bool TryGetSpawnNormalMonster(out EMonsterNormal OutType,
                                            out GameDB_Client_MonsterNormal OutData,
                                            out int OutCount)
    {
        OutType = default;
        OutData = null;
        OutCount = 0;

        if (TryResolveStageNormalMonster(out KeyValuePair<EMonsterNormal, GameDB_Client_MonsterNormal> selectedMonster) == false)
            return false;

        OutType = selectedMonster.Key;
        OutData = selectedMonster.Value;
        OutCount = GetMonsterCountPerGroup();

        return OutCount > 0;
    }

    /*
    public virtual void SetBossChallengeEvent()
    {
    }
    */

    public virtual void SetSkillSpellClickEvent()
    {

    }

    void AllClearBattleNetwork()
    {
        foreach (string key in rutineRatorHash)
        {
            GameMonoCoroutineManager.Instance.ClearCoroutine(key);
        }
        rutineRatorHash.Clear();

        //소환 코루틴도 함께 정리되므로 진행 플래그를 되돌린다
        IsWaveSpawning = false;
    }
    public virtual void RequestBattleNetwork(string InKey, IEnumerator InEnumerator)
    {
        if (rutineRatorHash.Contains(InKey))
        {
            GameMonoCoroutineManager.Instance.ClearCoroutine(InKey);
        }
        else
        {
            rutineRatorHash.Add(InKey);
        }

        GameMonoCoroutineManager.Instance.StartCoroutine_IEnumerator(InKey, InEnumerator);
    }

    protected int GetMonsterLevel(int increasePower = 0)
    {
        if (gameDB_Client_Stage == null)
        {
            Debug.LogError("GetMonsterLevel() - gameDB_Client_Stage is null");
            return 0;
        }

        int stageLevel = GameBattleControlManager.Instance.GetCurrentStageLevel;
        var monsterLevel = gameDB_Client_Stage.StageLevel.GetValue(stageLevel).GetValue;
        var monsterIncreaseLevel_byGroup = gameDB_Client_Stage.IncreaseInStageLevel.GetValue(increasePower).GetValue;
        var resultMonsterLevel = monsterLevel + monsterIncreaseLevel_byGroup;

        return (int)resultMonsterLevel;
    }

    protected virtual IEnumerator SpawnABTeamRatorNormalMonster(int monsterLevel, int spawnFieldNo)
    {
        //소환은 2프레임에 1마리씩 나눠 진행된다(60마리면 약 2초).
        //그 사이 다음 웨이브가 트리거되면 RequestBattleNetwork가 같은 키의 이 코루틴을 끊어버려 소환 마릿수가 어긋나고,
        //잔여 적이 0을 찍으면 스테이지가 조기 종료된다. 소환 중임을 알려 두 판정이 기다리게 한다
        IsWaveSpawning = true;

        try
        {
            //프리팹 로드/프리워밍이 끝나기 전에 소환하면 동기 로드 + 콜드 인스턴싱이 한 프레임에 몰린다.
            //메인 스테이지는 진입 HTTP 왕복 동안 이미 끝나 있어 보통 즉시 통과한다.
            //IsWaveSpawning = true 뒤에 두었으므로 대기 중에 다음 웨이브 트리거나 조기 종료 판정이 끼어들지 않는다
            yield return WaitSpawnPreloadRator();

            if (battleModeBaseData.battleInfoData == null)
            {
                Debug.LogError("SpawnABTeamRatorNormalMonster() - battleModeBaseData.battleInfoData is null");
                yield break;
            }

            if (TryGetSpawnNormalMonster(out EMonsterNormal eMonsterNormal,
                                         out GameDB_Client_MonsterNormal monsterData,
                                         out int spawnTotalCount) == false)
            {
                yield break;
            }

#if UNITY_EDITOR
            // 에디터 치트: 몬스터 1마리 스폰 모드
            if (GameBattleControlManager.DebugSingleMonsterSpawn)
                spawnTotalCount = Mathf.Min(spawnTotalCount, 1);
#endif

            //전장과 플레이어는 소환이 끝날 때까지 바뀌지 않으므로 마리마다 다시 조회하지 않는다
            GameCharacterManager.Instance.BeginSpawnBatch();

            //프레임당 소환 수. 웨이브 마릿수 이상이면 한 프레임에 전원이 등장한다
            int spawnCountPerFrame = Mathf.Max(1, GameClientPlayConfig.Instance.performance.spawnCountPerFrame);
            int spawnedInFrame = 0;

            for (int spawnIndex = 0; spawnIndex < spawnTotalCount; spawnIndex++)
            {
                if (battleModeBaseData.battleInfoData == null)
                {
                    //제거된 모드 데이터 이므로 종료
                    break;
                }

                int monsterGroup = battleModeBaseData.battleInfoData.SpawnGroupIndex;

                FCharacterSingleData addStatus = new FMonsterNormalSingleData(monsterLevel, FCommonDefine.EnemyTeamNo, spawnFieldNo, monsterGroup, eMonsterNormal, monsterData);
                UCharacterActor targetEnemy = GameCharacterManager.Instance.SpawnCharacter(addStatus);

                //프리팹 로드 실패 등으로 소환되지 않았다 (SpawnCharacter 안에서 이미 오류를 남긴다)
                if (targetEnemy == null)
                    continue;

                if (targetEnemy.IsBossCharacter == false)
                {
                    targetEnemy.SetMonsterAddKillCount(true);
                }

                spawnedInFrame++;

                if (spawnedInFrame >= spawnCountPerFrame)
                {
                    spawnedInFrame = 0;

                    yield return null;
                }
            }
        }
        finally
        {
            GameCharacterManager.Instance.EndSpawnBatch();
            IsWaveSpawning = false;
        }
    }
}