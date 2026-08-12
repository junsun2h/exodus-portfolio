using System.ComponentModel;
using UnityEngine;

namespace PX
{
    //---------------------------------------------------------------------------
    // Game
    //---------------------------------------------------------------------------
    public enum EBattleMode
    {
        None = 0,
        [Description("로그인 모드")]
        login,
        [Description("스테이지 메인")]
        stage_main,
        [Description("스테이지 마계")]
        stage_rift,
        [Description("스테이지 고대마물사냥")]
        stage_greatrift,
        [Description("스테이지 침공")]
        stage_invasion,
        [Description("스테이지 습격")]
        stage_incursion,
        [Description("스테이지 타락사원")]
        stage_corruption,
        [Description("스테이지 보스러쉬")]
        stage_bossrush,
        [Description("스테이지 훈련")]
        stage_training,
    }
    public enum ECoreDataChangeType
    {
        None,
        Added,
        Deleted,
        Updated,
    }

    public enum ECharacterSingleDataType
    {
        None,
        Player,
        Monster,
        Pet,
    }
    public enum EModStateType
    {
        None,
        Character,
        Equip,
        Skill,
        Buff,
    }

    //public enum ESkillSlotIndex
    //{
    //    [Description("메인스킬")]
    //    MainSlot = 0,
    //    [Description("서브스킬1")]
    //    SubSlot1 = 1,
    //    [Description("서브스킬2")]
    //    SubSlot2 = 2,
    //    [Description("서브스킬3")]
    //    SubSlot3 = 3,
    //}

    /*
    public enum ESkillSpellType
    {
        None = 0,
        Skill_Basic,

        Skill_Fire_BurningRay,
        Skill_Fire_DragonBreath,
        Skill_Fire_FireBall,
        Skill_Fire_RainOfFire,
        Skill_Ice_Blizzard,
        Skill_Ice_FrostNova,
        Skill_Ice_IceLance,
        Skill_Ice_IceShatter,
        Skill_Lightning_ChainLightning,
        Skill_Lightning_LightningArrow,
        Skill_Lightning_LightningOrb,
        Skill_Lightning_StaticDischarge,
        Skill_Physic_BladeBoomerang,
        Skill_Physic_Cyclone,
        Skill_Physic_EarthQuake,
        Skill_Physic_FanOfKnives,
        Skill_Poison_Contagion,
        Skill_Poison_DeadlyPoison,
        Skill_Poison_PoisonArrow,
        Skill_Poison_PoisonCloud,
    }
    */

    public enum EEffectType
    {
        None = 0,
        SpriteAni,
        Particle
    }

    public enum EAnimatorState
    {
        None = 0,
        AnimatorID_Idle,
        AnimatorID_Run,
        AnimatorID_NormalAttack,
        AnimatorID_Skill_1,
        AnimatorID_Skill_2,
        AnimatorID_Dead,
    }

    public enum ERestRewardType
    {
        None = 0,
        RestAuto = 1,
        RestEquip = 2,
        RestCharacter = 3,
        RestInchant = 4,
        RestDia = 5,
        Max = 6,
    }

    /// <summary>
    /// Reddot 타입 정의 (컨텐츠별 알림 표시)
    /// 각 컨텐츠의 특정 조건 달성 시 Reddot 표시
    /// </summary>
    public enum EReddotType
    {
        None = 0,

        // ========================================
        // 성운 (Nebula) - 1000번대
        // ========================================
        [Description("성운을 레벨업할 수 있는 재화가 충분함")]
        Nebula_LevelUp = 1000,

        [Description("성운 광고 버프를 활성화할 수 있음")]
        Nebula_AdBoost = 1001,

        // ========================================
        // 장비 (Equipment) - 2000번대
        // ========================================
        [Description("장비를 승급할 수 있는 재료가 충분함")]
        Equipment_CanPromotion = 2000,

        // ========================================
        // 스킬 (Skill) - 3000번대
        // ========================================
        [Description("스킬을 승급할 수 있는 재료가 충분함")]
        Skill_Spell_CanPromotion = 3000,
        Skill_Aura_CanPromotion = 3001,
        Skill_Curse_CanPromotion = 3002,
        Skill_Rune_CanPromotion = 3003,

        // ========================================
        // 플레이어 (Player) - 5000번대
        // ========================================
        [Description("플레이어를 성장 레벨업할 수 있는 경험치가 충분함")]
        Player_Growth_CanLevelUp = 5000,

        [Description("플레이어를 강화 레벨업할 수 있는 재화가 충분함")]
        Player_Reinforce_CanLevelUp = 5001,

        [Description("플레이어를 각성 레벨업할 수 있는 재화가 충분함")]
        Player_Awaken_CanLevelUp = 5002,

        [Description("성좌를 언락할 수 있음")]
        Player_Constellation_CanUnlock = 5003,

        [Description("성좌를 계약할 수 있음")]
        Player_Constellation_CanActivate = 5004,

        [Description("더 좋은 성좌를 계약할 수 있음")]
        Player_Constellation_MoreGoodConstellation = 5005,

        // ========================================
        // 퀘스트 (Quest) - 6000번대
        // ========================================
        [Description("메인 퀘스트의 보상을 수령할 수 있음")]
        Quest_Main_CanReward = 6000,

        [Description("일일 퀘스트의 보상을 수령할 수 있음")]
        Quest_Daily_CanReward = 6001,

        [Description("주간 퀘스트의 보상을 수령할 수 있음")]
        Quest_Weekly_CanReward = 6002,


        // ========================================
        // 메일 (Mailbox) - 8000번대
        // ========================================        
        [Description("메일의 보상을 수령할 수 있음")]
        Mailbox_CanReward = 8000,


        // ========================================
        // 상점 (Store) - 10000번대
        // ========================================        
        [Description("무료로 받을 수 있는 상품이 있음")]
        Store_HasFreeItem = 10000,
        [Description("패키지 상품이 있음")]
        Store_HasPackageItem = 10001,
        [Description("월정액 상품이 있음")]
        Store_HasSubscriptionItem = 10002,
        [Description("마일리지 상품이 있음")]
        Store_HasMileageItem = 10003,


        // ========================================
        // 배틀패스 (BattlePass) - 13000번대
        // ========================================
        [Description("출석 패스 수령 가능")]
        BattlePass_CanAttendance = 13000,

        [Description("스테이지 패스 수령 가능")]
        BattlePass_CanStage = 13002,

        [Description("시즌 패스 수령 가능")]
        BattlePass_CanSeason = 13004,

        [Description("각성 패스 수령 가능")]
        BattlePass_CanAwakening = 13005,

        [Description("장비컬렉션 패스 수령 가능")]
        BattlePass_CanEquipCollection = 13006,

        [Description("스킬강화도 패스 수령 가능")]
        BattlePass_CanSkillReinforce = 13007,

    }

    public enum EUnionType
    {
        All = -1,
        None = 0,
        Crusader = 1,
        Darkside = 2,
        Wonderer = 3,
        Bist = 4,
        Guardian = 5,
        Evel = 6,
        Max,

    }

    public enum EClassType
    {
        All = -1,
        None = 0,
        Defender = 1,
        Attacker = 2,
        Ranger = 3,
        Wizard = 4,
        Buffer = 5,
        Assassin = 6,
        Max,
    }

    public enum EItemType_Legacy
    {
        None = 0,
        Equip = 1,
        Item = 2,
    }

    public enum EEquipType
    {
        None = 0,
        Weapone = 1,
        Armor = 2,
        Helmet = 3,
        Shose = 4,
        Magic = 5,
        Cloak = 6,
        Max = 7,
    }
    public enum EStateType
    {
        None = 0,
        Character,
    }
    public enum ESABTeamType
    {
        None = 0,
        Aliance,
        Enemy,
    }

    public class FCommonDefine
    {
        public const string ConstBundleEffectBuff = "effect_buff";
        public const string ConstBundleEffectHit = "effect_hit";
        public const string ConstBundleEffectSkill = "effect_skill";
        public const string ConstBundleEffectProjectile = "effect_projectile";
        public const string ConstBundleEffectAura = "effect_aura";
        public const string ConstBundleEffectEtc = "effect_etc";


        public const int AlianceTeamNo = 1;
        public const int EnemyTeamNo = 2;

        public const string ConstCustomData = "CustomData";
        public const string ConstUUID = "uuid";
        public const string ConstPUID = "puid";
        public const string ConstSuccess = "success";
        public const string ConstResponse = "response";
        public const string ConstError = "error";
        public const string ConstStage = "stage";
        public const string ConstStageReady = "stageReady";
        public const string ConstPath = "path";
        public const string ConstType = "type";
        public const string ConstData = "data";
        public const string ConstNull = "null";
        public const string ConstFuncRuntime = "funcRuntime";
        public const string ConstServerID = "serverID";

        public const string ConstWin = "win";
        public const string ConstBattleKey = "battleKey";
        public const string ConstBattleOtherUUID = "battleOtherUUID";

        //테이블 키로 사용되니 변경불가
        public const string CONST_TEAM_battleTeam1 = "_battleTeam1";
        public const string CONST_TEAM_battleTeam2 = "_battleTeam2";
        public const string CONST_TEAM_battleTeam3 = "_battleTeam3";
        public const string CONST_TEAM_battleTeam4 = "_battleTeam4";
        public const string CONST_TEAM_battleTeam5 = "_battleTeam5";


        public const string CONST_KEY_bAbleLogin = "_bAbleLogin";

        public const string CONST_AESKEY = "aesKey";
        public const string CONST_AESIV = "aesIV";

        // 보안 검증 키
        public const string ConstSessionKey = "sessionKey";
        public const string ConstFunctionCallKey = "functionCallKey";

        public const string ConstServerVersionCheckKey = "serverVersionCheck";

        public static string GetTeamBodyKey(int InTeamNo)
        {
            switch (InTeamNo)
            {
                case 1: { return FCommonDefine.CONST_TEAM_battleTeam1; }
                case 2: { return FCommonDefine.CONST_TEAM_battleTeam2; }
                case 3: { return FCommonDefine.CONST_TEAM_battleTeam3; }
                case 4: { return FCommonDefine.CONST_TEAM_battleTeam4; }
                case 5: { return FCommonDefine.CONST_TEAM_battleTeam5; }
            }

            Debug.LogError(string.Format("GetTeamBodyKey, InTeamNo Key Not Exist, InTeamNo = {0}", InTeamNo));

            return "";
        }


        public static string GetBuffText(EStatusEffect InType)
        {
            /*
            switch (InType)
            {
                
                case EStatusEffectType_Legacy.Shield: { return "보호막"; }
                case EStatusEffectType_Legacy.AddForce: { return "충격"; }
                case EStatusEffectType_Legacy.Vamp: { return "흡혈"; }
                case EStatusEffectType_Legacy.Stun: { return "기절"; }

                case EStatusEffectType_Legacy.Heal: { return "체력 회복"; }

                case EStatusEffectType_Legacy.Status_Inc_Hp: { return "체력 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_Hp: { return "체력 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_AttPhysics: { return "물리 공격력 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_AttPhysics: { return "물리 공격력 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_AttMagic: { return "주문력 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_AttMagic: { return "주문력 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_DefPhysics: { return "물리 방어력 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_DefPhysics: { return "물리 방여력 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_DefMagic: { return "주문 방어력 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_DefMagic: { return "주문 방어력 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_DefPhysicsByRate: { return "물리 방어율 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_DefPhysicsByRate: { return "물리 방어율 감소"; }

                //case EStatusEffectType.Status_Inc_MoveSpeed: { return "이동 속도 증가"; }
                //case EStatusEffectType.Status_Dec_MoveSpeed: { return "이동 속도 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_NormalAttackSpeed: { return "공격 속도 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_NormalAttackSpeed: { return "공격 속도 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_CriticalPhysicsRate: { return "물리 치명타 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_CriticalPhysicsRate: { return "물리 치명타 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_CriticalMagicRate: { return "주문 치명타 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_CriticalMagicRate: { return "주문 치명타 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_CriticalPhysicsAddDamageRate: { return "물리 치명타 데미지 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_CriticalPhysicsAddDamageRate: { return "물리 치명타 데미지 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_CriticalMagicAddDamageRate: { return "주문 치명타 데미지 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_CriticalMagicAddDamageRate: { return "주문 치명타 데미지 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_HitPhysicsRate: { return "물리 적중률 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_HitPhysicsRate: { return "물리 적중률 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_HitMagicRate: { return "주문 적중률 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_HitMagicRate: { return "주문 적중률 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_AvoidPhysicsRate: { return "물리 회피율 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_AvoidPhysicsRate: { return "물리 회피율 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_AvoidMagicRate: { return "주문 회피 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_AvoidMagicRate: { return "주문 회피 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_ReducePhysicsRate: { return "물리 피해 감면율 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_ReducePhysicsRate: { return "물리 피해 감면율 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_ReduceMagicRate: { return "주문 피해 감면율 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_ReduceMagicRate: { return "주문 피해 감면율 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_RecoverHpSec: { return "초당 체력 회복 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_RecoverHpSec: { return "초당 체력 회복 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_RecoverHpRate: { return "체력 회복률 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_RecoverHpRate: { return "체력 회복률 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_RecoverMpPer: { return "마나 회복 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_RecoverMpPer: { return "마나 회복 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_RecoverMpRate: { return "마나 회복율 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_RecoverMpRate: { return "마나 회복율 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_VampPhysicsRate: { return "흡혈율 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_VampPhysicsRate: { return "흡혈율 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_VampMagicRate: { return "주문 흡혈율 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_VampMagicRate: { return "주문 흡혈율 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_VampPhysicsValue: { return "물리 흡혈 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_VampPhysicsValue: { return "물리 흡혈 감소"; }

                case EStatusEffectType_Legacy.Status_Inc_VampMagicValue: { return "주문 흡혈 증가"; }
                case EStatusEffectType_Legacy.Status_Dec_VampMagicValue: { return "주문 흡혈 감소"; }
                
            }
            */

            return InType.ToString();
        }
    }

}