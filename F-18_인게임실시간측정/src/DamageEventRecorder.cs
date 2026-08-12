using System;
using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// 피해 카테고리 (실시간 검증용)
    /// </summary>
    public enum EDamageCategory
    {
        Spell,
        Aura,
        Ailment,
        DoT
    }

    /// <summary>
    /// 피해 이벤트 데이터 (런타임 -> 에디터 검증 전달용)
    /// </summary>
    public struct DamageEventData
    {
        public double Timestamp;
        public EDamageCategory DamageType;
        public ESkill Skill;
        public ETier SkillTier;
        public ESkillTag Element;
        public EStatusEffect AilmentType;
        public double RawDamage;
        public double FinalDamage;
        public bool IsCritical;
        public bool IsCriticalBlow;
        public double CritMultiplier;
        public int CurrentStack;
        public bool IsHit;
        public bool IsEvaded;
        public bool IsBlocked;
        /// <summary>
        /// 피해 그룹 ID (시전당 고유 ID, 투사체별 첫 피해만 추적용)
        /// </summary>
        public int DamageGroupID;
    }

    /// <summary>
    /// 실시간 피해 검증용 데이터 기록기
    /// 런타임 코드에서 피해 이벤트를 기록하여 에디터 검증 시스템에 전달
    /// </summary>
    public static class DamageEventRecorder
    {
        // 수집 활성화 상태
        private static bool _isCollecting = false;
        public static bool IsCollecting => _isCollecting;

        // 피해 이벤트 델리게이트
        public static Action<DamageEventData> OnDamageEvent;

        // 검증용 자체 DamageGroupID 생성 (게임 로직의 DamageGroupID와 별개)
        private static int _verificationGroupIdCounter = 0;
        private static ESkill _lastSkill = ESkill.None;
        private static double _lastTimestamp = 0;
        private const double GROUP_TIME_THRESHOLD = 0.1; // 100ms 이내 같은 스킬 = 같은 시전

        // 첫 번째 타겟만 기록하기 위한 추적 (AoE/확산 중복 피해 방지)
        private static HashSet<ESkill> _recordedAuraSkills = new HashSet<ESkill>();
        private static HashSet<int> _recordedSpellGroups = new HashSet<int>(); // Spell AoE 첫 타겟만 기록

        /// <summary>
        /// 수집 활성화
        /// </summary>
        public static void StartCollection()
        {
            _isCollecting = true;
            // 검증용 그룹 ID 초기화
            _verificationGroupIdCounter = 0;
            _lastSkill = ESkill.None;
            _lastTimestamp = 0;
            // 첫 번째 타겟 추적용 초기화
            _recordedAuraSkills.Clear();
            _recordedSpellGroups.Clear();
        }

        /// <summary>
        /// 수집 비활성화
        /// </summary>
        public static void StopCollection()
        {
            _isCollecting = false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 초기화 상태 리셋 (도메인 리로드 후 재초기화용)
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void ResetOnDomainReload()
        {
            _isCollecting = false;
            // 검증용 카운터 리셋
            _verificationGroupIdCounter = 0;
            _lastSkill = ESkill.None;
            _lastTimestamp = 0;
            // 첫 번째 타겟 추적용 초기화
            _recordedAuraSkills.Clear();
            _recordedSpellGroups.Clear();
        }
#endif

        #region Spell 피해 이벤트

        /// <summary>
        /// Spell 피해 이벤트 기록
        /// CalcResultModDamage에서 호출
        /// </summary>
        public static void RecordSpellDamage(
            ESkill skill,
            ETier tier,
            ESkillTag element,
            double rawDamage,
            double finalDamage,
            bool isCritical,
            bool isCriticalBlow,
            double critMultiplier,
            int damageGroupID = 0)
        {
#if UNITY_EDITOR
            if (!_isCollecting) return;

            // 게임 로직의 DamageGroupID가 0이면 검증용 자체 그룹 ID 생성
            int effectiveGroupID = damageGroupID;
            if (effectiveGroupID == 0)
            {
                double currentTime = Time.time;
                // 다른 스킬이거나 시간 간격이 임계값 초과하면 새 시전으로 판단
                if (skill != _lastSkill || (currentTime - _lastTimestamp) > GROUP_TIME_THRESHOLD)
                {
                    _verificationGroupIdCounter++;
                }
                effectiveGroupID = _verificationGroupIdCounter;
                _lastSkill = skill;
                _lastTimestamp = currentTime;
            }

            // 같은 시전(GroupID)의 첫 번째 타겟만 기록 (AoE 스킬 중복 피해 방지)
            if (_recordedSpellGroups.Contains(effectiveGroupID))
                return;
            _recordedSpellGroups.Add(effectiveGroupID);

            var data = new DamageEventData
            {
                Timestamp = Time.time,
                DamageType = EDamageCategory.Spell,
                Skill = skill,
                SkillTier = tier,
                Element = element,
                RawDamage = rawDamage,
                FinalDamage = finalDamage,
                IsCritical = isCritical,
                IsCriticalBlow = isCriticalBlow,
                CritMultiplier = critMultiplier,
                IsHit = true,
                DamageGroupID = effectiveGroupID
            };

            OnDamageEvent?.Invoke(data);
#endif
        }

        /// <summary>
        /// Spell 회피 이벤트 기록
        /// </summary>
        public static void RecordSpellEvaded(ESkill skill, ETier tier, ESkillTag element)
        {
#if UNITY_EDITOR
            if (!_isCollecting) return;

            var data = new DamageEventData
            {
                Timestamp = Time.time,
                DamageType = EDamageCategory.Spell,
                Skill = skill,
                SkillTier = tier,
                Element = element,
                IsHit = false,
                IsEvaded = true
            };

            OnDamageEvent?.Invoke(data);
#endif
        }

        /// <summary>
        /// Spell 블록 이벤트 기록
        /// </summary>
        public static void RecordSpellBlocked(ESkill skill, ETier tier, ESkillTag element)
        {
#if UNITY_EDITOR
            if (!_isCollecting) return;

            var data = new DamageEventData
            {
                Timestamp = Time.time,
                DamageType = EDamageCategory.Spell,
                Skill = skill,
                SkillTier = tier,
                Element = element,
                IsHit = false,
                IsBlocked = true
            };

            OnDamageEvent?.Invoke(data);
#endif
        }

        #endregion

        #region Aura 피해 이벤트

        /// <summary>
        /// Aura 피해 이벤트 기록
        /// ActionController에서 호출
        /// </summary>
        public static void RecordAuraDamage(
            ESkill aura,
            ETier tier,
            ESkillTag element,
            double rawDamage,
            double finalDamage,
            bool isCritical,
            double critMultiplier)
        {
#if UNITY_EDITOR
            if (!_isCollecting) return;

            // 첫 번째 타겟만 기록 (AoE 폭발 시 중복 피해 방지)
            if (_recordedAuraSkills.Contains(aura))
                return;
            _recordedAuraSkills.Add(aura);

            var data = new DamageEventData
            {
                Timestamp = Time.time,
                DamageType = EDamageCategory.Aura,
                Skill = aura,
                SkillTier = tier,
                Element = element,
                RawDamage = rawDamage,
                FinalDamage = finalDamage,
                IsCritical = isCritical,
                CritMultiplier = critMultiplier,
                IsHit = true
            };

            OnDamageEvent?.Invoke(data);
#endif
        }

        #endregion

        #region Ailment 피해 이벤트

        /// <summary>
        /// Ailment 피해 이벤트 기록
        /// BuffActionData_Ailment에서 호출
        /// </summary>
        public static void RecordAilmentDamage(
            EStatusEffect ailmentType,
            ESkillTag element,
            double damage,
            int stack,
            bool isCritical = false,
            bool isCriticalBlow = false)
        {
#if UNITY_EDITOR
            if (!_isCollecting) return;

            var data = new DamageEventData
            {
                Timestamp = Time.time,
                DamageType = EDamageCategory.Ailment,
                AilmentType = ailmentType,
                Element = element,
                FinalDamage = damage,
                CurrentStack = stack,
                IsCritical = isCritical,
                IsCriticalBlow = isCriticalBlow,
                IsHit = true
            };

            OnDamageEvent?.Invoke(data);
#endif
        }

        #endregion

        #region DoT 피해 이벤트

        /// <summary>
        /// DoT 피해 이벤트 기록
        /// BuffActionData_Skill_Contagion에서 호출
        /// </summary>
        public static void RecordDotDamage(
            EStatusEffect dotType,
            ESkill sourceSkill,
            ETier tier,
            ESkillTag element,
            double damage)
        {
#if UNITY_EDITOR
            if (!_isCollecting) return;

            // DoT는 매 틱마다 독립적인 피해이므로 모든 틱을 기록
            // (Aura와 달리 확산 중복 피해 개념이 없음 - 각 타겟별 독립 틱)
            var data = new DamageEventData
            {
                Timestamp = Time.time,
                DamageType = EDamageCategory.DoT,
                AilmentType = dotType,
                Skill = sourceSkill,
                SkillTier = tier,
                Element = element,
                FinalDamage = damage,
                IsHit = true
            };

            OnDamageEvent?.Invoke(data);
#endif
        }

        #endregion
    }
}
