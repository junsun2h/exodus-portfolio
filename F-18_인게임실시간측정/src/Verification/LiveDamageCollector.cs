using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 샘플 수준 열거형
    /// </summary>
    public enum ESampleLevel
    {
        Minimum,    // 100회 타격 / ~60초
        Normal,     // 300회 타격 / ~180초 (3분)
        Precise     // 500회 타격 / ~360초 (6분)
    }

    /// <summary>
    /// 실시간 피해 수집기
    /// 에디터에서 인게임 피해 데이터를 수집하고 통계 분석
    /// </summary>
    public class LiveDamageCollector
    {
        // 싱글톤
        private static LiveDamageCollector _instance;
        public static LiveDamageCollector Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new LiveDamageCollector();
                return _instance;
            }
        }

        // 수집 상태
        public bool IsCollecting { get; private set; }
        public double StartTime { get; private set; }
        public double EndTime { get; private set; }
        public double Duration => IsCollecting ? (Time.time - StartTime) : (EndTime - StartTime);

        // 샘플 설정
        public ESampleLevel SampleLevel { get; set; } = ESampleLevel.Normal;
        public int TargetHits => SampleLevel switch
        {
            ESampleLevel.Minimum => 100,
            ESampleLevel.Normal => 300,
            ESampleLevel.Precise => 500,
            _ => 300
        };
        public double TargetDuration => SampleLevel switch
        {
            ESampleLevel.Minimum => 60,
            ESampleLevel.Normal => 180,
            ESampleLevel.Precise => 360,
            _ => 180
        };

        // 수집된 이벤트
        private List<LiveDamageEvent> _events = new List<LiveDamageEvent>();
        public IReadOnlyList<LiveDamageEvent> Events => _events;

        // 유형별 이벤트 캐시
        private List<LiveDamageEvent> _spellEvents = new List<LiveDamageEvent>();
        private List<LiveDamageEvent> _auraEvents = new List<LiveDamageEvent>();
        private List<LiveDamageEvent> _ailmentEvents = new List<LiveDamageEvent>();
        private List<LiveDamageEvent> _dotEvents = new List<LiveDamageEvent>();

        // DamageGroupID별 첫 피해만 기록하기 위한 추적 (투사체×타겟 합산 방지)
        private HashSet<int> _recordedDamageGroupIDs = new HashSet<int>();

        // 이벤트
        public event Action<LiveDamageEvent> OnDamageEventReceived;
        public event Action OnCollectionStarted;
        public event Action OnCollectionStopped;
        public event Action OnCollectionCompleted;

        // 진행률
        public float Progress
        {
            get
            {
                int totalHits = _events.Count(e => e.IsHit);
                float hitProgress = (float)totalHits / TargetHits;
                float timeProgress = (float)(Duration / TargetDuration);
                return Mathf.Max(hitProgress, timeProgress);
            }
        }

        public bool IsCompleted => Progress >= 1.0f;

        // 현재 통계 요약
        public int TotalHits => _events.Count(e => e.IsHit);
        public int SpellHits => _spellEvents.Count(e => e.IsHit);
        public int AuraHits => _auraEvents.Count(e => e.IsHit);
        public int AilmentHits => _ailmentEvents.Count(e => e.IsHit);
        public int DotHits => _dotEvents.Count(e => e.IsHit);

        public double TotalDamage => _events.Where(e => e.IsHit).Sum(e => e.FinalDamage);
        public double CurrentDPS => Duration > 0 ? TotalDamage / Duration : 0;

        // 크리티컬 통계
        public int CriticalHits => _events.Count(e => e.IsHit && e.IsCritical);
        public int CriticalBlowHits => _events.Count(e => e.IsHit && e.IsCriticalBlow);
        public double MeasuredCritRate => TotalHits > 0 ? (double)CriticalHits / TotalHits * 100 : 0;

        /// <summary>
        /// 수집 시작
        /// </summary>
        public void StartCollection()
        {
            if (IsCollecting) return;

            Clear();
            IsCollecting = true;
            StartTime = Time.time;
            EndTime = 0;

            OnCollectionStarted?.Invoke();
            Debug.Log($"[LiveDamageCollector] 수집 시작 - 목표: {TargetHits}회 타격 / {TargetDuration}초");
        }

        /// <summary>
        /// 수집 중단
        /// </summary>
        public void StopCollection()
        {
            if (!IsCollecting) return;

            IsCollecting = false;
            EndTime = Time.time;

            OnCollectionStopped?.Invoke();
            Debug.Log($"[LiveDamageCollector] 수집 중단 - {TotalHits}회 타격 / {Duration:F1}초 수집됨");
        }

        /// <summary>
        /// 수집 초기화
        /// </summary>
        public void Clear()
        {
            _events.Clear();
            _spellEvents.Clear();
            _auraEvents.Clear();
            _ailmentEvents.Clear();
            _dotEvents.Clear();
            _recordedDamageGroupIDs.Clear();
            StartTime = 0;
            EndTime = 0;
            IsCollecting = false;
        }

        /// <summary>
        /// 피해 이벤트 추가
        /// </summary>
        public void AddEvent(LiveDamageEvent evt)
        {
            if (!IsCollecting) return;

            // DamageGroupID별 첫 피해만 기록 (투사체×타겟 합산 방지)
            // DamageGroupID가 0보다 크면 유효한 그룹 ID이므로 중복 체크
            if (evt.DamageGroupID > 0)
            {
                if (!_recordedDamageGroupIDs.Add(evt.DamageGroupID))
                {
                    // 이미 기록된 그룹 ID - 스킵 (같은 시전의 다른 투사체/타겟 피해)
                    return;
                }
            }

            _events.Add(evt);

            // 유형별 캐시에 추가
            switch (evt.DamageType)
            {
                case ELiveDamageType.Spell:
                    _spellEvents.Add(evt);
                    break;
                case ELiveDamageType.Aura:
                    _auraEvents.Add(evt);
                    break;
                case ELiveDamageType.Ailment:
                    _ailmentEvents.Add(evt);
                    break;
                case ELiveDamageType.DoT:
                    _dotEvents.Add(evt);
                    break;
            }

            OnDamageEventReceived?.Invoke(evt);

            // 완료 체크
            if (IsCompleted && IsCollecting)
            {
                StopCollection();
                OnCollectionCompleted?.Invoke();
                Debug.Log($"[LiveDamageCollector] 수집 완료! {TotalHits}회 타격 / {Duration:F1}초");
            }
        }

        #region 유형별 이벤트 조회

        /// <summary>
        /// Spell 이벤트 조회
        /// </summary>
        public IEnumerable<LiveDamageEvent> GetSpellEvents() => _spellEvents.Where(e => e.IsHit);

        /// <summary>
        /// Aura 이벤트 조회
        /// </summary>
        public IEnumerable<LiveDamageEvent> GetAuraEvents() => _auraEvents.Where(e => e.IsHit);

        /// <summary>
        /// Ailment 이벤트 조회
        /// </summary>
        public IEnumerable<LiveDamageEvent> GetAilmentEvents() => _ailmentEvents.Where(e => e.IsHit);

        /// <summary>
        /// 특정 Ailment 이벤트 조회
        /// </summary>
        public IEnumerable<LiveDamageEvent> GetAilmentEvents(EStatusEffect ailmentType)
        {
            return _ailmentEvents.Where(e => e.IsHit && e.AilmentType == ailmentType);
        }

        /// <summary>
        /// DoT 이벤트 조회
        /// </summary>
        public IEnumerable<LiveDamageEvent> GetDotEvents() => _dotEvents.Where(e => e.IsHit);

        #endregion

        #region 통계 계산

        /// <summary>
        /// Spell 통계 계산
        /// </summary>
        public DamageTypeStatistics CalculateSpellStatistics()
        {
            var hits = GetSpellEvents().ToList();
            return CalculateStatistics(ELiveDamageType.Spell, hits);
        }

        /// <summary>
        /// Aura 통계 계산
        /// </summary>
        public DamageTypeStatistics CalculateAuraStatistics()
        {
            var hits = GetAuraEvents().ToList();
            return CalculateStatistics(ELiveDamageType.Aura, hits);
        }

        /// <summary>
        /// DoT 통계 계산
        /// </summary>
        public DamageTypeStatistics CalculateDotStatistics()
        {
            var hits = GetDotEvents().ToList();
            return CalculateStatistics(ELiveDamageType.DoT, hits);
        }

        /// <summary>
        /// Ailment 통계 계산
        /// </summary>
        public Dictionary<EStatusEffect, AilmentStatistics> CalculateAilmentStatistics()
        {
            var result = new Dictionary<EStatusEffect, AilmentStatistics>();

            // 활성화된 Ailment 유형 찾기
            var ailmentTypes = _ailmentEvents
                .Where(e => e.IsHit)
                .Select(e => e.AilmentType)
                .Distinct();

            foreach (var ailmentType in ailmentTypes)
            {
                var hits = GetAilmentEvents(ailmentType).ToList();
                if (hits.Count == 0) continue;

                var stats = new AilmentStatistics
                {
                    AilmentType = ailmentType,
                    TotalAttempts = hits.Count,  // 정확한 시도 횟수는 별도 추적 필요
                    TotalProcs = hits.Count,
                    MeasuredProcRate = 0,  // 별도 계산 필요
                    TotalDamage = hits.Sum(e => e.FinalDamage),
                    MeasuredDPS = Duration > 0 ? hits.Sum(e => e.FinalDamage) / Duration : 0
                };

                result[ailmentType] = stats;
            }

            return result;
        }

        /// <summary>
        /// 공통 통계 계산
        /// </summary>
        private DamageTypeStatistics CalculateStatistics(ELiveDamageType damageType, List<LiveDamageEvent> hits)
        {
            if (hits.Count == 0)
            {
                return new DamageTypeStatistics { DamageType = damageType };
            }

            int totalHits = hits.Count;
            double totalDamage = hits.Sum(e => e.FinalDamage);
            double avgDamage = totalDamage / totalHits;

            // 표준편차 계산
            double variance = hits.Sum(e => Math.Pow(e.FinalDamage - avgDamage, 2)) / totalHits;
            double stdDev = Math.Sqrt(variance);

            // 크리티컬 통계
            int critHits = hits.Count(e => e.IsCritical);
            int critBlowHits = hits.Count(e => e.IsCriticalBlow);
            double measuredCritRate = (double)critHits / totalHits;
            double measuredCritBlowRate = critHits > 0 ? (double)critBlowHits / critHits : 0;

            // 크리티컬 배율 평균
            var critEvents = hits.Where(e => e.IsCritical && e.AppliedCritMultiplier > 0).ToList();
            double avgCritMult = critEvents.Count > 0 ? critEvents.Average(e => e.AppliedCritMultiplier) : 0;

            // 신뢰구간 계산 (95%)
            var critCI = StatisticalAnalyzer.CalculateWilsonInterval(critHits, totalHits);
            var avgDamageCI = StatisticalAnalyzer.CalculateNormalInterval(avgDamage, stdDev, totalHits);

            return new DamageTypeStatistics
            {
                DamageType = damageType,
                TotalHits = totalHits,
                TotalDamage = totalDamage,
                AverageDamage = avgDamage,
                StandardDeviation = stdDev,
                MeasuredDPS = Duration > 0 ? totalDamage / Duration : 0,
                CriticalHits = critHits,
                CriticalBlowHits = critBlowHits,
                MeasuredCritRate = measuredCritRate * 100,
                MeasuredCritBlowRate = measuredCritBlowRate * 100,
                AverageCritMultiplier = avgCritMult,
                CritRateLowerBound = critCI.lower * 100,
                CritRateUpperBound = critCI.upper * 100,
                AvgDamageLowerBound = avgDamageCI.lower,
                AvgDamageUpperBound = avgDamageCI.upper
            };
        }

        #endregion

        #region 현황 문자열

        /// <summary>
        /// 현재 수집 현황 문자열
        /// </summary>
        public string GetStatusString()
        {
            return $"⏱ 경과: {Duration:F1}초 / {TargetDuration}초\n" +
                   $"📈 수집: {TotalHits}회 / {TargetHits}회 ({Progress * 100:F0}%)\n" +
                   $"├─ Spell: {SpellHits}회 (크리티컬 {_spellEvents.Count(e => e.IsCritical)}회)\n" +
                   $"├─ Aura: {AuraHits}회\n" +
                   $"├─ Ailment: {AilmentHits}회\n" +
                   $"└─ DoT: {DotHits}회\n" +
                   $"💰 총 피해: {TotalDamage:N0}\n" +
                   $"📊 현재 DPS: {CurrentDPS:N0}";
        }

        #endregion
    }

    /// <summary>
    /// 유형별 통계 분석 결과
    /// </summary>
    [Serializable]
    public class DamageTypeStatistics
    {
        public ELiveDamageType DamageType;

        // 기본 통계
        public int TotalHits;                       // 총 타격 횟수
        public double TotalDamage;                  // 총 피해량
        public double AverageDamage;                // 평균 피해
        public double StandardDeviation;            // 표준편차
        public double MeasuredDPS;                  // 측정된 DPS

        // 크리티컬 통계
        public int CriticalHits;                    // 크리티컬 횟수
        public int CriticalBlowHits;                // 크리티컬 블로우 횟수
        public double MeasuredCritRate;             // 측정된 크리티컬 확률 (%)
        public double MeasuredCritBlowRate;         // 측정된 크리티컬 블로우 확률 (%)
        public double AverageCritMultiplier;        // 평균 크리티컬 배율

        // 신뢰구간 (95%)
        public double CritRateLowerBound;           // 크리티컬 확률 하한 (%)
        public double CritRateUpperBound;           // 크리티컬 확률 상한 (%)
        public double AvgDamageLowerBound;          // 평균 피해 하한
        public double AvgDamageUpperBound;          // 평균 피해 상한
    }

    /// <summary>
    /// 상태이상별 통계
    /// </summary>
    [Serializable]
    public class AilmentStatistics
    {
        public EStatusEffect AilmentType;

        public int TotalAttempts;                   // 총 시도 횟수 (스킬 시전)
        public int TotalProcs;                      // 발동 횟수
        public double MeasuredProcRate;             // 측정된 발동률 (%)

        public double TotalDamage;                  // 총 피해
        public double MeasuredDPS;                  // 측정된 DPS

        // 신뢰구간
        public double ProcRateLowerBound;
        public double ProcRateUpperBound;
    }
}
