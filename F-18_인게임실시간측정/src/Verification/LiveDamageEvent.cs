using System;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 실시간 피해 수집용 피해 유형 열거형
    /// </summary>
    public enum ELiveDamageType
    {
        Spell,      // 스킬 직접 피해
        Aura,       // 오라 피해
        Ailment,    // 상태이상 피해 (점화, 중독 등)
        DoT         // 지속 피해 (skill_contagion)
    }

    /// <summary>
    /// 단일 피해 이벤트 데이터
    /// 인게임에서 발생하는 모든 피해를 기록
    /// </summary>
    [Serializable]
    public class LiveDamageEvent
    {
        // 시간 정보
        public double Timestamp;                    // 발생 시각 (Time.time)

        // 피해 유형
        public ELiveDamageType DamageType;          // Spell/Aura/Ailment/DoT
        public ESkillTag Element;                   // 속성 (Fire/Cold/Lightning/Poison/Physical)

        // 피해량
        public double RawDamage;                    // 방어 적용 전 피해
        public double FinalDamage;                  // 최종 피해 (방어 적용 후)

        // 크리티컬 정보
        public bool IsCritical;                     // 크리티컬 여부
        public bool IsCriticalBlow;                 // 크리티컬 블로우 여부
        public double AppliedCritMultiplier;        // 적용된 크리티컬 배율

        // 스킬 정보
        public ESkill Skill;                        // 스킬 종류
        public ETier SkillTier;                     // 스킬 티어

        // 상태이상 정보 (Ailment인 경우)
        public EStatusEffect AilmentType;                   // 상태이상 종류
        public int CurrentStack;                    // 현재 스택

        // 타격 정보
        public bool IsHit;                          // 명중 여부
        public bool IsEvaded;                       // 회피됨
        public bool IsBlocked;                      // 블록됨

        // 피해 그룹 ID (시전당 고유 ID, 투사체별 첫 피해만 추적용)
        public int DamageGroupID;

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public LiveDamageEvent()
        {
            Timestamp = UnityEngine.Time.time;
        }

        /// <summary>
        /// Spell 피해 이벤트 생성
        /// </summary>
        public static LiveDamageEvent CreateSpellEvent(
            ESkill skill, ETier tier, ESkillTag element,
            double rawDamage, double finalDamage,
            bool isCritical, bool isCriticalBlow, double critMultiplier,
            int damageGroupID = 0)
        {
            return new LiveDamageEvent
            {
                DamageType = ELiveDamageType.Spell,
                Skill = skill,
                SkillTier = tier,
                Element = element,
                RawDamage = rawDamage,
                FinalDamage = finalDamage,
                IsCritical = isCritical,
                IsCriticalBlow = isCriticalBlow,
                AppliedCritMultiplier = critMultiplier,
                IsHit = true,
                DamageGroupID = damageGroupID
            };
        }

        /// <summary>
        /// Aura 피해 이벤트 생성
        /// </summary>
        public static LiveDamageEvent CreateAuraEvent(
            ESkill aura, ETier tier, ESkillTag element,
            double rawDamage, double finalDamage,
            bool isCritical, double critMultiplier)
        {
            return new LiveDamageEvent
            {
                DamageType = ELiveDamageType.Aura,
                Skill = aura,
                SkillTier = tier,
                Element = element,
                RawDamage = rawDamage,
                FinalDamage = finalDamage,
                IsCritical = isCritical,
                AppliedCritMultiplier = critMultiplier,
                IsHit = true
            };
        }

        /// <summary>
        /// Ailment 피해 이벤트 생성
        /// </summary>
        public static LiveDamageEvent CreateAilmentEvent(
            EStatusEffect ailmentType, ESkillTag element,
            double damage, int stack)
        {
            return new LiveDamageEvent
            {
                DamageType = ELiveDamageType.Ailment,
                AilmentType = ailmentType,
                Element = element,
                RawDamage = damage,
                FinalDamage = damage,
                CurrentStack = stack,
                IsHit = true
            };
        }

        /// <summary>
        /// DoT 피해 이벤트 생성
        /// </summary>
        public static LiveDamageEvent CreateDotEvent(
            EStatusEffect dotType, ESkill sourceSkill, ETier tier, ESkillTag element,
            double damage)
        {
            return new LiveDamageEvent
            {
                DamageType = ELiveDamageType.DoT,
                AilmentType = dotType,
                Skill = sourceSkill,
                SkillTier = tier,
                Element = element,
                RawDamage = damage,
                FinalDamage = damage,
                IsHit = true
            };
        }

        /// <summary>
        /// 회피 이벤트 생성
        /// </summary>
        public static LiveDamageEvent CreateEvadedEvent(ESkill skill, ETier tier, ESkillTag element)
        {
            return new LiveDamageEvent
            {
                DamageType = ELiveDamageType.Spell,
                Skill = skill,
                SkillTier = tier,
                Element = element,
                IsHit = false,
                IsEvaded = true
            };
        }

        /// <summary>
        /// 블록 이벤트 생성
        /// </summary>
        public static LiveDamageEvent CreateBlockedEvent(ESkill skill, ETier tier, ESkillTag element)
        {
            return new LiveDamageEvent
            {
                DamageType = ELiveDamageType.Spell,
                Skill = skill,
                SkillTier = tier,
                Element = element,
                IsHit = false,
                IsBlocked = true
            };
        }

        public override string ToString()
        {
            string critInfo = IsCritical ? (IsCriticalBlow ? "[크리티컬 블로우]" : "[크리티컬]") : "";
            return $"[{DamageType}] {Element} {critInfo} 피해: {FinalDamage:N0}";
        }
    }
}
