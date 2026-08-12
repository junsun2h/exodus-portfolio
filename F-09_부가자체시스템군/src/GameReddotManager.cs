using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// Reddot 상태를 중앙에서 관리하는 싱글톤 매니저
    /// 컨텐츠별 조건을 체크하고 Reddot 표시 여부를 관리
    /// CoreData 변경 시 자동으로 Reddot 상태 갱신
    /// </summary>
    public class GameReddotManager : SingletonDependency<GameReddotManager>
    {
        public delegate void UpdateReddotStateEvent(EReddotType InReddotType, bool InState);

        private GameReddotManager() { }

        ~GameReddotManager() { }

        // Reddot 타입별 상태 저장
        private Dictionary<EReddotType, bool> _reddotStates = new Dictionary<EReddotType, bool>();

        public Dictionary<EReddotType, bool> ReddotStates => _reddotStates;

        // Reddot 상태 변경 이벤트
        public UpdateReddotStateEvent OnReddotStateChanged;

        Dictionary<EReddotType, EContent> _reddotTypeToContent = new Dictionary<EReddotType, EContent>();


        public override void InitData()
        {
            base.InitData();

            // 모든 Reddot 타입 초기화
            foreach (EReddotType reddotType in Enum.GetValues(typeof(EReddotType)))
            {
                if (reddotType == EReddotType.None)
                    continue;

                _reddotStates[reddotType] = false;
            }

            // Reddot 타입별 컨텐츠 매핑
            InitReddotTypeToContent();
        }


        public EContent GetReddotContent(EReddotType reddotType)
        {
            if (_reddotTypeToContent.TryGetValue(reddotType, out EContent content))
            {
                return content;
            }
            return EContent.None;
        }

        void InitReddotTypeToContent()
        {
            _reddotTypeToContent.Add(EReddotType.Nebula_LevelUp, EContent.content_nebula);
            _reddotTypeToContent.Add(EReddotType.Nebula_AdBoost, EContent.content_nebula);
            _reddotTypeToContent.Add(EReddotType.Equipment_CanPromotion, EContent.content_equipment_reinforce);
            _reddotTypeToContent.Add(EReddotType.Skill_Spell_CanPromotion, EContent.content_skill_spell);
            _reddotTypeToContent.Add(EReddotType.Skill_Aura_CanPromotion, EContent.content_skill_aura);
            _reddotTypeToContent.Add(EReddotType.Skill_Curse_CanPromotion, EContent.content_pet);
            _reddotTypeToContent.Add(EReddotType.Skill_Rune_CanPromotion, EContent.content_skill_rune);
            _reddotTypeToContent.Add(EReddotType.Player_Growth_CanLevelUp, EContent.content_player_growth);
            _reddotTypeToContent.Add(EReddotType.Player_Reinforce_CanLevelUp, EContent.content_player_reinforce);
            _reddotTypeToContent.Add(EReddotType.Player_Awaken_CanLevelUp, EContent.content_awaken);
            _reddotTypeToContent.Add(EReddotType.Player_Constellation_CanUnlock, EContent.content_constellation);
            _reddotTypeToContent.Add(EReddotType.Player_Constellation_CanActivate, EContent.content_constellation);
            _reddotTypeToContent.Add(EReddotType.Player_Constellation_MoreGoodConstellation, EContent.content_constellation);

            _reddotTypeToContent.Add(EReddotType.Quest_Main_CanReward, EContent.content_quest_main);
            _reddotTypeToContent.Add(EReddotType.Quest_Daily_CanReward, EContent.content_quest_daily);
            _reddotTypeToContent.Add(EReddotType.Quest_Weekly_CanReward, EContent.content_quest_weekly);

            _reddotTypeToContent.Add(EReddotType.Mailbox_CanReward, EContent.content_mailbox);

            _reddotTypeToContent.Add(EReddotType.Store_HasFreeItem, EContent.content_store);
            _reddotTypeToContent.Add(EReddotType.Store_HasPackageItem, EContent.content_store_package);
            _reddotTypeToContent.Add(EReddotType.Store_HasSubscriptionItem, EContent.content_store_subscription);
            _reddotTypeToContent.Add(EReddotType.Store_HasMileageItem, EContent.content_store_mileage);

            _reddotTypeToContent.Add(EReddotType.BattlePass_CanAttendance, EContent.content_pass_attendance);
            _reddotTypeToContent.Add(EReddotType.BattlePass_CanStage, EContent.content_pass_stage);
            _reddotTypeToContent.Add(EReddotType.BattlePass_CanSeason, EContent.content_pass_season);
            _reddotTypeToContent.Add(EReddotType.BattlePass_CanAwakening, EContent.content_store_pass);
            _reddotTypeToContent.Add(EReddotType.BattlePass_CanEquipCollection, EContent.content_store_pass);
            _reddotTypeToContent.Add(EReddotType.BattlePass_CanSkillReinforce, EContent.content_store_pass);

        }

        public override void InitAfterUserData()
        {
            base.InitAfterUserData();

            // UserData 로드 후 CoreData 변경 이벤트 구독
            SubscribeCoreDataEvents();

            // 모든 Reddot 조건 초기 체크
            RefreshAllReddots();
        }

        /// <summary>
        /// 특정 Reddot 상태 조회
        /// </summary>
        public bool GetReddotState(EReddotType reddotType)
        {
            if (reddotType == EReddotType.None)
                return false;

            _reddotStates.TryGetValue(reddotType, out bool state);
            return state;
        }

        /// <summary>
        /// 다중 Reddot 상태 조회 (OR 조건 - 하나라도 true면 true 반환)
        /// </summary>
        public bool GetReddotStateAny(params EReddotType[] reddotTypes)
        {
            if (reddotTypes == null || reddotTypes.Length == 0)
                return false;

            foreach (var type in reddotTypes)
            {
                if (type != EReddotType.None && GetReddotState(type))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 다중 Reddot 상태 조회 (AND 조건 - 모두 true여야 true 반환)
        /// </summary>
        public bool GetReddotStateAll(params EReddotType[] reddotTypes)
        {
            if (reddotTypes == null || reddotTypes.Length == 0)
                return false;

            foreach (var type in reddotTypes)
            {
                if (type == EReddotType.None)
                    continue;

                if (!GetReddotState(type))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 특정 Reddot 상태 설정
        /// </summary>
        public void SetReddotState(EReddotType reddotType, bool state)
        {
            if (reddotType == EReddotType.None)
                return;

            if (_reddotStates.TryGetValue(reddotType, out bool currentState))
            {
                if (currentState != state)
                {
                    _reddotStates[reddotType] = state;
                    OnReddotStateChanged?.Invoke(reddotType, state);

                }
            }
        }


        /// <summary>
        /// 모든 Reddot 조건 재평가
        /// </summary>
        public void RefreshAllReddots(bool InInitial = false)
        {
            if (InInitial)
            {
                foreach (var reddotState in _reddotStates)
                {
                    _reddotStates[reddotState.Key] = false;
                }
            }


            foreach (EReddotType reddotType in Enum.GetValues(typeof(EReddotType)))
            {
                if (reddotType == EReddotType.None)
                    continue;

                RefreshReddot(reddotType);
            }
        }

        /// <summary>
        /// 특정 Reddot 조건 재평가
        /// </summary>
        public bool RefreshReddot(EReddotType reddotType)
        {
            if (reddotType == EReddotType.None)
                return false;

            bool newState = EvaluateReddotCondition(reddotType);
            SetReddotState(reddotType, newState);
            return newState;
        }

        /// <summary>
        /// 여러 Reddot 조건 재평가
        /// </summary>
        public void RefreshReddots(params EReddotType[] reddotTypes)
        {
            foreach (var reddotType in reddotTypes)
            {
                RefreshReddot(reddotType);
            }
        }

        /// <summary>
        /// 컨텐츠 열림 조건과 연결된 모든 레드닷 재평가
        /// 플레이어 레벨/각성 변경 시 호출
        /// </summary>
        public void RefreshContentRelatedReddots()
        {
            foreach (var reddotType in _reddotTypeToContent.Keys)
            {
                RefreshReddot(reddotType);
            }
        }


        public void ShortcutClickReddot(EReddotType reddotType)
        {
            if (reddotType == EReddotType.None)
                return;

            switch (reddotType)
            {
                case EReddotType.Nebula_LevelUp:
                case EReddotType.Nebula_AdBoost:
                    {
                        GameUIManager.Instance.OpenWidget("TKPopup_Nebula");
                    }
                    break;
                case EReddotType.Equipment_CanPromotion:
                    {
                        GameUIManager.Instance.OpenWidget("TKScreen_Equipment");
                    }
                    break;
                case EReddotType.Skill_Spell_CanPromotion:
                case EReddotType.Skill_Aura_CanPromotion:
                case EReddotType.Skill_Rune_CanPromotion:
                case EReddotType.Skill_Curse_CanPromotion:
                    {
                        GameUIManager.Instance.OpenWidget("TKScreen_Skill");
                    }
                    break;
                case EReddotType.Player_Growth_CanLevelUp:
                case EReddotType.Player_Reinforce_CanLevelUp:
                case EReddotType.Player_Awaken_CanLevelUp:
                    {
                        GameUIManager.Instance.OpenWidget("TKScreen_Player");
                    }
                    break;
                case EReddotType.Player_Constellation_CanUnlock:
                case EReddotType.Player_Constellation_CanActivate:
                case EReddotType.Player_Constellation_MoreGoodConstellation:
                    {
                        // 성좌 탭은 별도 Screen으로 분리 예정 — 임시로 기존 UGUI 진입
                        GameUIManager.Instance.OpenWidget("PXPopup_PlayerHUD");
                    }
                    break;
                case EReddotType.Quest_Main_CanReward:
                case EReddotType.Quest_Daily_CanReward:
                case EReddotType.Quest_Weekly_CanReward:
                    {
                        GameUIManager.Instance.OpenWidget("TKPopup_Quest");
                    }
                    break;
                case EReddotType.Mailbox_CanReward:
                    {
                        GameUIManager.Instance.OpenWidget("TKPopup_MailBox");
                    }
                    break;
                case EReddotType.Store_HasFreeItem:
                    {
                        GameUIManager.Instance.OpenWidget("TKScreen_Store");
                    }
                    break;
                case EReddotType.BattlePass_CanAttendance:
                case EReddotType.BattlePass_CanStage:
                case EReddotType.BattlePass_CanSeason:
                case EReddotType.BattlePass_CanAwakening:
                case EReddotType.BattlePass_CanEquipCollection:
                case EReddotType.BattlePass_CanSkillReinforce:
                    {
                        GameUIManager.Instance.OpenWidget("PXPopup_BattlePass");
                    }
                    break;
                default:
                    {
                        Debug.LogError("ExecuteClickReddot Not Defined ReddotType: " + reddotType);
                    }
                    break;
            }

        }
        /// <summary>
        /// Reddot 조건 평가 (컨텐츠별 조건 체크)
        /// </summary>
        private bool EvaluateReddotCondition(EReddotType reddotType)
        {
            // 컨텐츠 열림 여부 선행 체크
            if (_reddotTypeToContent.TryGetValue(reddotType, out EContent content))
            {
                if (!GameContentManager.Instance.IsContentOpen(content))
                    return false;
            }

            switch (reddotType)
            {
                // ========================================
                // 성운 (Nebula)
                // ========================================
                case EReddotType.Nebula_LevelUp:
                    return CheckNebula_LevelUp();

                case EReddotType.Nebula_AdBoost:
                    return CheckNebula_AdBoost();

                // ========================================
                // 장비 (Equipment)
                // ========================================
                case EReddotType.Equipment_CanPromotion:
                    return CheckEquipment_CanPromotion();



                // ========================================
                // 스킬 (Skill)
                // ========================================
                case EReddotType.Skill_Spell_CanPromotion:
                    return CheckSkill_Spell_CanPromotion();

                case EReddotType.Skill_Aura_CanPromotion:
                    return CheckSkill_Aura_CanPromotion();

                case EReddotType.Skill_Curse_CanPromotion:
                    return CheckSkill_Curse_CanPromotion();

                case EReddotType.Skill_Rune_CanPromotion:
                    return CheckSkill_Rune_CanPromotion();



                // ========================================
                // 플레이어 (Player)
                // ========================================
                case EReddotType.Player_Growth_CanLevelUp:
                    return CheckPlayer_Growth_CanLevelUp();

                case EReddotType.Player_Reinforce_CanLevelUp:
                    return CheckPlayer_Reinforce_CanLevelUp();

                case EReddotType.Player_Awaken_CanLevelUp:
                    return CheckPlayer_Awaken_CanLevelUp();

                case EReddotType.Player_Constellation_CanUnlock:
                    return CheckPlayer_Constellation_CanUnlock();

                case EReddotType.Player_Constellation_CanActivate:
                    return CheckPlayer_Constellation_CanActivate();

                case EReddotType.Player_Constellation_MoreGoodConstellation:
                    return CheckPlayer_Constellation_MoreGoodConstellation();


                // ========================================
                // 퀘스트 (Quest)
                // ========================================
                case EReddotType.Quest_Main_CanReward:
                    return CheckQuest_Main_CanReward();

                case EReddotType.Quest_Daily_CanReward:
                    return CheckQuest_Daily_CanReward();

                case EReddotType.Quest_Weekly_CanReward:
                    return CheckQuest_Weekly_CanReward();

                // ========================================
                // 업적 (Achievement)
                // ========================================
                // case EReddotType.Achievement_CanClaim:
                //     return CheckAchievement_CanClaim();

                // ========================================
                // 메일 (Mailbox)
                // ========================================
                case EReddotType.Mailbox_CanReward:
                    return CheckMailbox_CanReward();




                // ========================================
                // 상점 (Store)
                // ========================================
                case EReddotType.Store_HasFreeItem:
                    return CheckStore_HasFreeItem();
                case EReddotType.Store_HasPackageItem:
                    return CheckStore_HasPackageItem();
                case EReddotType.Store_HasSubscriptionItem:
                    return CheckStore_HasSubscriptionItem();
                case EReddotType.Store_HasMileageItem:
                    return CheckStore_HasMileageItem();


                // ========================================
                // 배틀패스 (BattlePass)
                // ========================================
                case EReddotType.BattlePass_CanAttendance:
                    return CheckBattlePass_CanAttendance();

                case EReddotType.BattlePass_CanStage:
                    return CheckBattlePass_CanStage();

                case EReddotType.BattlePass_CanSeason:
                    return CheckBattlePass_CanSeason();

                case EReddotType.BattlePass_CanAwakening:
                    return CheckBattlePass_CanAwakening();

                case EReddotType.BattlePass_CanEquipCollection:
                    return CheckBattlePass_CanEquipCollection();

                case EReddotType.BattlePass_CanSkillReinforce:
                    return CheckBattlePass_CanSkillReinforce();


                default:
                    return false;
            }
        }

        /// <summary>
        /// CoreData 변경 이벤트 구독
        /// </summary>
        private void SubscribeCoreDataEvents()
        {
            if (GameAPIUserManager.Instance?.userData == null)
                return;

            // 성운 데이터 변경 감지
            if (GameAPIUserManager.Instance.userData.nebulaData?.CoreData != null)
            {
                GameAPIUserManager.Instance.userData.nebulaData.CoreData.coreDataChangedDelegate +=
                    OnNebulaDataChanged;
            }

            // 화폐 데이터 변경 감지 (여러 컨텐츠 영향)
            if (GameAPIUserManager.Instance.userData.currencyData?.CoreData != null)
            {
                GameAPIUserManager.Instance.userData.currencyData.CoreData.coreDataChangedDelegate +=
                    OnCurrencyDataChanged;
            }

            // 장비 데이터 변경 감지
            if (GameAPIUserManager.Instance.userData.equipmentData?.CoreData != null)
            {
                GameAPIUserManager.Instance.userData.equipmentData.CoreData.coreDataChangedDelegate +=
                    OnEquipmentDataChanged;
            }

            // 스킬 데이터 변경 감지
            if (GameAPIUserManager.Instance.userData.skillData?.CoreData != null)
            {
                GameAPIUserManager.Instance.userData.skillData.CoreData.coreDataChangedDelegate +=
                    OnSkillDataChanged;
            }

            // 펫 데이터 변경 감지
            if (GameAPIUserManager.Instance.userData.petData?.CoreData != null)
            {
                GameAPIUserManager.Instance.userData.petData.CoreData.coreDataChangedDelegate +=
                    OnPetDataChanged;
            }

            // 플레이어 데이터 변경 감지
            if (GameAPIUserManager.Instance.userData.playerData?.CoreData != null)
            {
                GameAPIUserManager.Instance.userData.playerData.CoreData.coreDataChangedDelegate +=
                    OnPlayerDataChanged;
            }

            // 퀘스트 데이터 변경 감지
            if (GameAPIUserManager.Instance.userData.questData?.CoreData != null)
            {
                GameAPIUserManager.Instance.userData.questData.CoreData.coreDataChangedDelegate +=
                    OnQuestDataChanged;
            }


            // 스테이지 데이터 변경 감지
            // if (GameAPIUserManager.Instance.userData.stageData?.CoreData != null)
            // {
            //     GameAPIUserManager.Instance.userData.stageData.CoreData.coreDataChangedDelegate +=
            //         OnStageDataChanged;
            // }

            // 상품 데이터 변경 감지
            if (GameAPIUserManager.Instance.userData.productData?.CoreData != null)
            {
                GameAPIUserManager.Instance.userData.productData.CoreData.coreDataChangedDelegate +=
                    OnProductDataChanged;
            }
        }

        // ========================================
        // CoreData 변경 콜백
        // ========================================

        private void OnNebulaDataChanged(string dataKey, ECoreDataChangeType changeType)
        {
            RefreshReddots(EReddotType.Nebula_LevelUp, EReddotType.Nebula_AdBoost);
        }

        private void OnCurrencyDataChanged(string dataKey, ECoreDataChangeType changeType)
        {
            // 화폐 변경 시 여러 컨텐츠 Reddot 갱신
            RefreshReddots(
                EReddotType.Nebula_LevelUp,
                EReddotType.Skill_Spell_CanPromotion,
                EReddotType.Skill_Aura_CanPromotion,
                EReddotType.Skill_Curse_CanPromotion,
                EReddotType.Skill_Rune_CanPromotion,
                EReddotType.Player_Constellation_CanUnlock,
                EReddotType.Player_Growth_CanLevelUp,
                EReddotType.Player_Reinforce_CanLevelUp

            );
        }

        private void OnEquipmentDataChanged(string dataKey, ECoreDataChangeType changeType)
        {
            RefreshReddots(
                EReddotType.Equipment_CanPromotion,
                EReddotType.BattlePass_CanEquipCollection
            );
        }

        private void OnSkillDataChanged(string dataKey, ECoreDataChangeType changeType)
        {
            RefreshReddots(
                EReddotType.Skill_Spell_CanPromotion,
                EReddotType.Skill_Aura_CanPromotion,
                EReddotType.Skill_Curse_CanPromotion,
                EReddotType.Skill_Rune_CanPromotion,
                EReddotType.BattlePass_CanSkillReinforce);
        }

        private void OnPetDataChanged(string dataKey, ECoreDataChangeType changeType)
        {
            RefreshReddots(EReddotType.Skill_Curse_CanPromotion);
        }

        private void OnPlayerDataChanged(string dataKey, ECoreDataChangeType changeType)
        {
            // 플레이어 레벨/각성 변경 시 컨텐츠 열림 조건이 변경될 수 있으므로
            // 컨텐츠와 연결된 모든 레드닷 갱신
            RefreshContentRelatedReddots();
            RefreshReddots(EReddotType.BattlePass_CanAwakening);
        }

        private void OnQuestDataChanged(string dataKey, ECoreDataChangeType changeType)
        {
            RefreshReddots(
                EReddotType.Quest_Main_CanReward,
                EReddotType.Quest_Daily_CanReward,
                EReddotType.Quest_Weekly_CanReward
            );
        }


        // private void OnStageDataChanged(string dataKey, ECoreDataChangeType changeType)
        // {
        //     RefreshReddots(
        //         EReddotType.sta,
        //         EReddotType.Stage_CanEnter,
        //         EReddotType.Stage_CanClaimReward
        //     );
        // }

        private void OnProductDataChanged(string dataKey, ECoreDataChangeType changeType)
        {
            RefreshReddots(
                EReddotType.Store_HasFreeItem
            );
        }

        // ========================================
        // 컨텐츠별 조건 체크 메서드
        // ========================================

        // ----------------------------------------
        // 성운 (Nebula)
        // ----------------------------------------
        private bool CheckNebula_LevelUp()
        {
            if (GameAPIUserManager.Instance?.userData?.nebulaData?.CoreData == null)
                return false;

            // 모든 성운 중 레벨업 가능한 것이 있는지 체크
            //CheckNebula_LevelUp
            foreach (ENebula nebula in Enum.GetValues(typeof(ENebula)))
            {
                if (CheckNebula_LevelUp(nebula))
                    return true;
            }

            return false;
        }
        public bool CheckNebula_LevelUp(ENebula nebula)
        {
            if (nebula == ENebula.None)
                return false;

            if (GameAPIUserManager.Instance?.userData?.nebulaData?.CoreData == null)
                return false; GameAPIUserManager.Instance.userData.nebulaData.CoreData.Nebulas.TryGetValue(nebula, out var nebulaCoreData);
            if (nebulaCoreData == null)
                return false;

            int currentLevel = nebulaCoreData.Level.Value;
            if (currentLevel >= GameGlobalConfig.nebula_max_level)
                return false;

            BigInteger hasCost = (BigInteger)GameAPIUserManager.Instance.userData.currencyData.GetCurrencyCount(ECurrency.currency_neblua_offering_point);
            BigInteger needCost = GetNebulaNeedLevelUpCost(currentLevel);

            if (hasCost >= needCost)
                return true;

            return false;
        }

        private bool CheckNebula_AdBoost()
        {
            if (GameAPIUserManager.Instance?.userData?.nebulaData?.CoreData == null)
                return false;

            // VIP가 아니고 광고 버프가 만료된 성운이 있는지 체크
            if (GameAPIUserManager.Instance.userData.productData.CoreData.IsVIP_AdRemove())
                return false;

            foreach (ENebula nebula in Enum.GetValues(typeof(ENebula)))
            {
                if (CheckNebula_AdBoost(nebula))
                    return true;
            }

            return false;
        }
        public bool CheckNebula_AdBoost(ENebula nebula)
        {
            if (nebula == ENebula.None)
                return false;

            if (GameAPIUserManager.Instance?.userData?.nebulaData?.CoreData == null)
                return false;

            if (GameAPIUserManager.Instance.userData.productData.CoreData.IsVIP_AdRemove())
                return false;

            GameAPIUserManager.Instance.userData.nebulaData.CoreData.Nebulas.TryGetValue(nebula, out var nebulaCoreData);
            if (nebulaCoreData == null)
                return false;

            DateTimeOffset currentTime = GameMonoTimeManager.Instance.GetCurrentTime(); ;

            DateTimeOffset expireTime = currentTime;

            if (!string.IsNullOrEmpty(nebulaCoreData.AdBoostExpirationTime))
            {
                expireTime = GameUtility.ConvertToUtcDateTimeOffset(nebulaCoreData.AdBoostExpirationTime);
            }

            if (expireTime <= currentTime)
                return true;

            return false;
        }

        // ----------------------------------------
        // 장비 (Equipment)
        // ----------------------------------------

        // 장비 승급
        private bool CheckEquipment_CanPromotion()
        {
            if (GameAPIUserManager.Instance?.userData?.equipmentData?.CoreData == null)
                return false;

            foreach (var equipment in GameAPIUserManager.Instance.userData.equipmentData.CoreData.NormalEquipments)
            {
                if (CheckEquipment_CanPromotion(equipment.Value.Tier, equipment.Value.Grade, equipment.Value.NormalName, EEquipmentMythic.None))
                    return true;
            }
            foreach (var equipment in GameAPIUserManager.Instance.userData.equipmentData.CoreData.MythicEquipments)
            {
                if (CheckEquipment_CanPromotion(equipment.Value.Tier, equipment.Value.Grade, EEquipmentNormal.None, equipment.Value.MythicName))
                    return true;
            }
            return false;
        }

        public bool CheckEquipment_CanPromotion(EEquipmentLargeType eEquipmentLargeType)
        {
            foreach (var equipment in GameAPIUserManager.Instance.userData.equipmentData.CoreData.NormalEquipments)
            {
                if (equipment.Value.LargeType == eEquipmentLargeType)
                {
                    if (CheckEquipment_CanPromotion(equipment.Value.Tier, equipment.Value.Grade, equipment.Value.NormalName, EEquipmentMythic.None))
                        return true;
                }
            }

            foreach (var equipment in GameAPIUserManager.Instance.userData.equipmentData.CoreData.MythicEquipments)
            {
                if (equipment.Value.LargeType == eEquipmentLargeType)
                {
                    if (CheckEquipment_CanPromotion(equipment.Value.Tier, equipment.Value.Grade, EEquipmentNormal.None, equipment.Value.MythicName))
                        return true;
                }
            }

            return false;
        }

        public bool CheckEquipment_CanPromotion(ETier eTier, EGrade eGrade, EEquipmentNormal eEquipmentNormal, EEquipmentMythic eEquipmentMythic)
        {
            // 최대 티어/등급 체크 (mythic + grade5이면 승급 불가)
            if (eTier == ETier.mythic && eGrade == EGrade.grade5)
            {
                return false;
            }

            int promoteCount = GameDBClientManager.Instance.GameDB_Etc.GetEquipPromoteCount(eTier, eGrade);

            // promoteCount가 0 이하면 승급 불가 (데이터 없음 또는 최대 등급)
            if (promoteCount <= 0)
            {
                return false;
            }

            if (eEquipmentNormal != EEquipmentNormal.None)
            {
                if (GameAPIUserManager.Instance.userData.equipmentData.CoreData.NormalEquipments.TryGetValue(eEquipmentNormal, out var equipment))
                {
                    int nowCount = equipment.Count.Value;
                    if (nowCount >= promoteCount)
                    {
                        return true;
                    }
                }
            }
            else if (eEquipmentMythic != EEquipmentMythic.None)
            {
                if (GameAPIUserManager.Instance.userData.equipmentData.CoreData.MythicEquipments.TryGetValue(eEquipmentMythic, out var equipment))
                {
                    int nowCount = equipment.Count.Value;
                    if (nowCount >= promoteCount)
                    {
                        return true;
                    }
                }
            }
            return false;
        }


        // ----------------------------------------
        // 스킬 (Skill)
        // ----------------------------------------
        private bool CheckSkill_Spell_CanPromotion()
        {
            foreach (var skill in GameAPIUserManager.Instance.userData.skillData.CoreData.Spells)
            {
                foreach (var tier in skill.Value.Tiers)
                {
                    if (GameAPISkillManager.Instance.IsAblePromotion(ESkillType.skill_spell, tier.Value.Skill, tier.Key))
                        return true;
                }
            }

            return false;
        }
        private bool CheckSkill_Aura_CanPromotion()
        {
            foreach (var skill in GameAPIUserManager.Instance.userData.skillData.CoreData.Auras)
            {
                foreach (var tier in skill.Value.Tiers)
                {
                    if (GameAPISkillManager.Instance.IsAblePromotion(ESkillType.skill_aura, tier.Value.Skill, tier.Key))
                        return true;
                }
            }
            return false;
        }

        private bool CheckSkill_Curse_CanPromotion()
        {
            foreach (var skill in GameAPIUserManager.Instance.userData.skillData.CoreData.Curses)
            {
                foreach (var tier in skill.Value.Tiers)
                {
                    if (GameAPISkillManager.Instance.IsAblePromotion(ESkillType.skill_curse, tier.Value.Skill, tier.Key))
                        return true;
                }
            }
            return false;
        }
        private bool CheckSkill_Rune_CanPromotion()
        {
            foreach (var skill in GameAPIUserManager.Instance.userData.skillData.CoreData.Runes)
            {
                foreach (var tier in skill.Value.Tiers)
                {
                    if (GameAPISkillManager.Instance.IsAblePromotion(ESkillType.skill_rune, tier.Value.Skill, tier.Key))
                        return true;
                }
            }
            return false;
        }
        // ----------------------------------------
        // 플레이어 (Player)
        // ----------------------------------------
        private bool CheckPlayer_Growth_CanLevelUp()
        {
            foreach (var growth in GameAPIUserManager.Instance.userData.playerData.CoreData.Growths)
            {
                if (GameAPIPlayerManager.Instance.IsAblePlayerGrowthLevelUp(growth.Key))
                {
                    return true;
                }
            }
            return false;
        }
        private bool CheckPlayer_Reinforce_CanLevelUp()
        {
            foreach (var reinforce in GameAPIUserManager.Instance.userData.playerData.CoreData.Reinforces)
            {
                if (GameAPIPlayerManager.Instance.IsAblePlayerReinforceLevelUp(reinforce.Key))
                {
                    return true;
                }
            }
            return false;
        }
        private bool CheckPlayer_Awaken_CanLevelUp()
        {
            foreach (var awakenElement in GameAPIUserManager.Instance.userData.playerData.CoreData.Awaken.AwakenModLevels)
            {
                if (GameAPIPlayerManager.Instance.IsAblePlayerAwakenLevelUp(awakenElement.Key))
                {
                    return true;
                }
            }
            return false;
        }

        // ----------------------------------------
        // 퀘스트 (Quest)
        // ----------------------------------------
        private bool CheckQuest_Main_CanReward()
        {
            if (GameAPIUserManager.Instance?.userData?.questData?.CoreData == null)
                return false;

            // 완료했지만 보상을 받지 않은 퀘스트가 있는지 체크 (메인 퀘스트)
            foreach (var questPair in GameAPIUserManager.Instance.userData.questData.CoreData.QuestMain_Completes)
            {
                // var (isCanComplete, currentValue, targetValue) = GameQuestManager.Instance.GetQuestProgress(EQuestType.quest_main, (int)questPair.Key);
                // bool isClaimed = questPair.Value.Value;

                // if (isCanComplete && !isClaimed)
                //     return true;

                if (CheckQuest_Main_CanReward(questPair.Key))
                    return true;
            }
            return false;
        }

        public bool CheckQuest_Main_CanReward(EQuestMain eQuestMain)
        {
            var questData = GameAPIUserManager.Instance.userData.questData.CoreData.QuestMain_Completes.TryGetValue(eQuestMain, out CryptoValueBool isCryptoReward);

            var (isCanComplete, currentValue, targetValue) = GameQuestManager.Instance.GetQuestProgress(EQuestType.main, (int)eQuestMain);
            bool isClaimed = isCryptoReward.Value;

            if (isCanComplete && !isClaimed)
                return true;


            // foreach (var questPair in GameAPIUserManager.Instance.userData.questData.CoreData.QuestMain_Completes)
            // {
            //     var (isCanComplete, currentValue, targetValue) = GameQuestManager.Instance.GetQuestProgress(eQuestType, (int)questPair.Key);
            //     bool isClaimed = questPair.Value.Value;

            //     if (isCanComplete && !isClaimed)
            //         return true;
            // }

            return false;
        }

        private bool CheckQuest_Daily_CanReward()
        {

            // 일일 퀘스트 체크
            foreach (var questPair in GameAPIUserManager.Instance.userData.questData.CoreData.QuestDaily_Completes)
            {
                // var (isCanComplete, currentValue, targetValue) = GameQuestManager.Instance.GetQuestProgress(EQuestType.quest_daily, (int)questPair.Key);
                // bool isClaimed = questPair.Value.Value;

                // if (isCanComplete && !isClaimed)
                //     return true;

                if (CheckQuest_Daily_CanReward(questPair.Key))
                    return true;
            }
            return false;
        }

        public bool CheckQuest_Daily_CanReward(EQuestDaily eQuestDaily)
        {
            var questData = GameAPIUserManager.Instance.userData.questData.CoreData.QuestDaily_Completes.TryGetValue(eQuestDaily, out CryptoValueBool isCryptoReward);

            var (isCanComplete, currentValue, targetValue) = GameQuestManager.Instance.GetQuestProgress(EQuestType.daily, (int)eQuestDaily);
            bool isClaimed = isCryptoReward.Value;

            if (isCanComplete && !isClaimed)
                return true;

            return false;
        }

        private bool CheckQuest_Weekly_CanReward()
        {
            // 주간 퀘스트 체크
            foreach (var questPair in GameAPIUserManager.Instance.userData.questData.CoreData.QuestWeekly_Completes)
            {
                // var (isCanComplete, currentValue, targetValue) = GameQuestManager.Instance.GetQuestProgress(EQuestType.quest_weekly, (int)questPair.Key);
                // bool isClaimed = questPair.Value.Value;

                // if (isCanComplete && !isClaimed)
                //     return true;
                if (CheckQuest_Weekly_CanReward(questPair.Key))
                    return true;
            }
            return false;
        }

        public bool CheckQuest_Weekly_CanReward(EQuestWeekly eQuestWeekly)
        {
            var questData = GameAPIUserManager.Instance.userData.questData.CoreData.QuestWeekly_Completes.TryGetValue(eQuestWeekly, out CryptoValueBool isCryptoReward);
            var (isCanComplete, currentValue, targetValue) = GameQuestManager.Instance.GetQuestProgress(EQuestType.weekly, (int)eQuestWeekly);
            bool isClaimed = isCryptoReward.Value;

            if (isCanComplete && !isClaimed)
                return true;

            return false;
        }


        // ----------------------------------------
        // 메일 (Mailbox)
        // ----------------------------------------
        private bool CheckMailbox_CanReward()
        {
            return GameAPIUserManager.Instance.userData.mailBoxData.CoreData.Items.Keys.Count > 0;
        }

        // ----------------------------------------
        // 상점 (Store)
        // ----------------------------------------

        private bool CheckStore_HasFreeItem()
        {
            //Debug.Assert(false, "무료 상품 체크 구현");
            // TODO: 무료 상품 체크 구현
            return false;
        }
        private bool CheckStore_HasPackageItem()
        {
            // TODO: 패키지 상품 체크 구현
            //Debug.Assert(false, "패키지 상품 체크 구현");
            return false;
        }
        private bool CheckStore_HasSubscriptionItem()
        {
            // TODO: 월정액 상품 체크 구현
            //Debug.Assert(false, "월정액 상품 체크 구현");
            return false;
        }
        private bool CheckStore_HasMileageItem()
        {
            // TODO: 마일리지 상품 체크 구현
            //Debug.Assert(false, "마일리지 상품 체크 구현");
            return false;
        }

        // ----------------------------------------
        // 배틀패스 (BattlePass)
        // ----------------------------------------
        public bool CheckBattlePass_CanSeason()
        {
            // TODO: 시즌 패스 체크 구현
            //Debug.Assert(false, "시즌 패스 체크 구현");
            return false;
        }

        public bool CheckBattlePass_CanAttendance()
        {
            if (GameAPIUserManager.Instance.userData.productData.CoreData.ProductPass.TryGetValue(EProductPass.product_pass_attendance, out var productPass))
            {
                if (productPass == null)
                    return false;
                if (productPass.IsActive() && productPass.IsPurchased.Value == false)
                    return true;
            }

            return false;
        }

        public bool CheckBattlePass_CanStage() => CheckBattlePass_CanByPrefix("product_pass_stage_");
        public bool CheckBattlePass_CanAwakening() => CheckBattlePass_CanByPrefix("product_pass_awakening_");
        public bool CheckBattlePass_CanEquipCollection() => CheckBattlePass_CanByPrefix("product_pass_equip_collection_");
        public bool CheckBattlePass_CanSkillReinforce() => CheckBattlePass_CanByPrefix("product_pass_skill_reinforce_");

        private bool CheckBattlePass_CanByPrefix(string prefix)
        {
            foreach (var kvp in GameAPIUserManager.Instance.userData.productData.CoreData.ProductPass)
            {
                if (kvp.Key.ToString().StartsWith(prefix))
                {
                    if (kvp.Value == null)
                        return false;
                    if (kvp.Value.IsActive() && kvp.Value.IsPurchased.Value == false)
                        return true;
                }
            }
            return false;
        }


        // ----------------------------------------
        // 성좌 (Constellation) - 슬롯 시스템
        // ----------------------------------------

        /// <summary>
        /// 빈 슬롯이 있고 장착 가능한 보유 성좌가 있는지 확인합니다.
        /// </summary>
        private bool CheckPlayer_Constellation_CanUnlock()
        {
            var constellationData = GameAPIUserManager.Instance?.userData?.constellationData?.CoreData;
            if (constellationData == null)
                return false;

            // 빈 슬롯이 있는지 확인
            EConstellationSlot emptySlot = GameAPINebulaManager.Instance.GetFirstEmptySlot();
            if (emptySlot == EConstellationSlot.None)
                return false;

            // 장착되지 않은 보유 성좌가 있는지 확인
            foreach (var constellation in constellationData.Constellations)
            {
                if (!GameAPINebulaManager.Instance.IsConstellationEquipped(constellation.Key))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 슬롯 시스템에서는 사용하지 않음 (하위 호환성 유지)
        /// </summary>
        private bool CheckPlayer_Constellation_CanActivate()
        {
            return false;
        }

        public bool CheckPlayer_Constellation_CanActivate(out EConstellation outConstellation, out ETier outTier)
        {
            outConstellation = EConstellation.None;
            outTier = ETier.None;
            return false;
        }

        /// <summary>
        /// 슬롯 시스템에서는 사용하지 않음 (하위 호환성 유지)
        /// </summary>
        private bool CheckPlayer_Constellation_MoreGoodConstellation()
        {
            return false;
        }

        // private bool CheckPlayer_Nebula_CanLevelUp()
        // {
        //     foreach (var nebulaEnum in Enum.GetValues(typeof(ENebula)))
        //     {
        //         ENebula nebula = (ENebula)nebulaEnum;
        //         if (nebula == ENebula.None)
        //             continue;

        //         if (GameAPINebulaManager.Instance.IsAbleNebulaLevelUp(nebula))
        //             return true;
        //     }
        //     return false;
        // }


        // ========================================
        // Helper 메서드들
        // ========================================

        private BigInteger GetNebulaNeedLevelUpCost(int level)
        {
            if (GlobalValue.NebulaLevel_XPtoGain != null && GlobalValue.NebulaLevel_XPtoGain.Count > level)
                return (BigInteger)GlobalValue.NebulaLevel_XPtoGain[level];

            return BigInteger.Zero;
        }
    }
}
