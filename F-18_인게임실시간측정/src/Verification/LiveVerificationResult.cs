using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 실시간 검증 세션 결과
    /// </summary>
    [Serializable]
    public class LiveVerificationResult
    {
        // 세션 정보
        public DateTime Timestamp;
        public double StartTime;
        public double EndTime;
        public double Duration => EndTime - StartTime;

        // 유형별 통계
        public LiveDamageTypeResult SpellResult;
        public LiveDamageTypeResult AuraResult;
        public LiveDamageTypeResult DotResult;
        public Dictionary<EStatusEffect, LiveAilmentResult> AilmentResults = new Dictionary<EStatusEffect, LiveAilmentResult>();

        // 총합
        public double GrandTotalDamage;
        public double OverallMeasuredDPS;

        // 시뮬레이션 예측값 (비교 기준)
        public double SimulatedSpellDPS;
        public double SimulatedSpellHitDamage; // Spell 1타 평균 피해 (치명타 적용 후)
        public double SimulatedSpellNonCritDamage; // Spell 비크리 1타 피해
        public double SimulatedSpellCritMultiplier; // Spell 크리 배율 (비율, 예: 17.76)
        public double SimulatedSpellCritBlowMultiplier; // Spell 크리 일격 배율 (비율, 예: 1.6)
        public double SimulatedAuraDPS;  // Aura 1회 피해 (틱 레이트 1.0 기준)
        public double SimulatedAuraNonCritDamage; // Aura 비크리 1회 피해
        public double SimulatedAuraCritMultiplier; // Aura 크리 배율
        public double SimulatedAuraCritBlowMultiplier; // Aura 크리 일격 배율
        public double SimulatedAilmentDPS;
        public double SimulatedAilmentNonCritDamage; // Ailment 비크리 1회 피해 (방어 적용)
        public double SimulatedAilmentCritMultiplier; // Ailment 크리 배율
        public double SimulatedAilmentCritBlowMultiplier; // Ailment 크리 일격 배율
        public double SimulatedDotDPS;
        public double SimulatedDot1TickDamage; // DoT 1틱 평균 피해 (치명타 적용 후)
        public double SimulatedDotNonCritDamage; // DoT 비치명타 1틱 피해
        public double SimulatedDotCritMultiplier; // DoT 크리티컬 배율
        public double SimulatedTotalDPS;

        // 검증 결과 (시뮬 예측값이 있는데 측정 데이터가 없으면 실패)
        public bool IsSpellValid
        {
            get
            {
                // 시뮬 예측값이 있는데 측정 데이터가 없으면 실패
                if (SimulatedSpellDPS > 0 && (SpellResult == null || SpellResult.TotalHits == 0))
                    return false;
                if (SpellResult == null || SpellResult.TotalHits == 0)
                    return true;

                // 범위 검증: 비크리 피해 ≤ 측정 평균 ≤ 크리×크리일격 피해
                double measuredPerHit = SpellResult.TotalDamage / SpellResult.TotalHits;
                double minDamage = SimulatedSpellNonCritDamage;
                double maxDamage = SimulatedSpellNonCritDamage * SimulatedSpellCritMultiplier * SimulatedSpellCritBlowMultiplier;

                // 범위 정보가 없으면 ±20% 오차 방식으로 폴백
                if (minDamage <= 0 || maxDamage <= 0)
                {
                    double simPerHit = SimulatedSpellHitDamage;
                    double error = simPerHit > 0 ? Math.Abs((measuredPerHit - simPerHit) / simPerHit * 100) : 0;
                    return error <= 20.0;
                }

                // 10% 마진 적용 (게임 내 미세한 변동 허용)
                double margin = 0.1;
                double adjustedMin = minDamage * (1 - margin);
                double adjustedMax = maxDamage * (1 + margin);

                return measuredPerHit >= adjustedMin && measuredPerHit <= adjustedMax;
            }
        }

        public bool IsAuraValid
        {
            get
            {
                // 시뮬 예측값이 있는데 측정 데이터가 없으면 실패
                if (SimulatedAuraDPS > 0 && (AuraResult == null || AuraResult.TotalHits == 0))
                    return false;
                if (AuraResult == null || AuraResult.TotalHits == 0)
                    return true;

                // 범위 검증: 비크리 피해 ≤ 측정 평균 ≤ 크리×크리일격 피해
                double measuredPerHit = AuraResult.TotalDamage / AuraResult.TotalHits;
                double minDamage = SimulatedAuraNonCritDamage;
                double maxDamage = SimulatedAuraNonCritDamage * SimulatedAuraCritMultiplier * SimulatedAuraCritBlowMultiplier;

                // 범위 정보가 없으면 ±20% 오차 방식으로 폴백
                if (minDamage <= 0 || maxDamage <= 0)
                {
                    double simPerHit = SimulatedAuraDPS;
                    double error = simPerHit > 0 ? Math.Abs((measuredPerHit - simPerHit) / simPerHit * 100) : 0;
                    return error <= 20.0;
                }

                // 10% 마진 적용 (게임 내 미세한 변동 허용)
                double margin = 0.1;
                double adjustedMin = minDamage * (1 - margin);
                double adjustedMax = maxDamage * (1 + margin);

                return measuredPerHit >= adjustedMin && measuredPerHit <= adjustedMax;
            }
        }

        public bool IsAilmentValid
        {
            get
            {
                // 시뮬 예측값이 있는데 측정 데이터가 없으면 실패
                int totalProcs = AilmentResults.Values.Sum(a => a.TotalProcs);
                if (SimulatedAilmentDPS > 0 && totalProcs == 0)
                    return false;
                if (totalProcs == 0)
                    return true;

                // 범위 검증: 비크리 피해 ≤ 측정 평균 ≤ 크리×크리일격 피해
                // Ailment는 최초 적용 시 1회 계산되어 duration 동안 분할 (1틱/초)
                double totalAilmentDamage = AilmentResults.Values.Sum(a => a.TotalDamage);
                double measuredPerHit = totalAilmentDamage / totalProcs;
                double minDamage = SimulatedAilmentNonCritDamage;
                double maxDamage = SimulatedAilmentNonCritDamage * SimulatedAilmentCritMultiplier * SimulatedAilmentCritBlowMultiplier;

                // 범위 정보가 없으면 통과
                if (minDamage <= 0 || maxDamage <= 0)
                    return true;

                // 10% 마진 적용
                double margin = 0.1;
                double adjustedMin = minDamage * (1 - margin);
                double adjustedMax = maxDamage * (1 + margin);

                return measuredPerHit >= adjustedMin && measuredPerHit <= adjustedMax;
            }
        }

        public bool IsDotValid
        {
            get
            {
                // 시뮬 예측값이 있는데 측정 데이터가 없으면 실패
                if (SimulatedDotDPS > 0 && (DotResult == null || DotResult.TotalHits == 0))
                    return false;
                if (DotResult == null || DotResult.TotalHits == 0)
                    return true;

                // 샘플 부족 시 실패 (최소 10회 필요)
                if (DotResult.TotalHits < 10)
                    return false;

                // 범위 검증: 비치명타~치명타 범위 (± margin)
                // DoT도 치명타가 적용되므로 Spell과 동일한 범위 검증 사용
                double measuredPerTick = DotResult.TotalDamage / DotResult.TotalHits;
                if (SimulatedDotNonCritDamage <= 0)
                {
                    // 비치명타 피해 정보 없으면 DPS 기반 검증
                    double error = SimulatedDotDPS > 0 ? Math.Abs((DotResult.MeasuredDPS - SimulatedDotDPS) / SimulatedDotDPS * 100) : 0;
                    return error <= 20.0;
                }

                double margin = 0.1; // ±10%
                double adjustedMin = SimulatedDotNonCritDamage * (1 - margin);
                double critMultiplier = SimulatedDotCritMultiplier > 0 ? SimulatedDotCritMultiplier : 1.0;
                double adjustedMax = SimulatedDotNonCritDamage * critMultiplier * (1 + margin);

                return measuredPerTick >= adjustedMin && measuredPerTick <= adjustedMax;
            }
        }

        public bool IsOverallValid => IsSpellValid && IsAuraValid && IsAilmentValid && IsDotValid;

        /// <summary>
        /// 결과 요약 문자열
        /// </summary>
        public string GetSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"📊 실시간 검증 결과 ({Duration:F1}초 측정)");
            sb.AppendLine("════════════════════════════════════════");

            // Spell (항상 표시)
            {
                int hits = SpellResult?.TotalHits ?? 0;
                double measured = SpellResult?.MeasuredDPS ?? 0;
                if (hits > 0)
                {
                    double measuredPerHit = SpellResult.TotalDamage / hits;
                    double hitsPerSec = hits / Duration;
                    double adjustedSimDPS = SimulatedSpellHitDamage * hitsPerSec;

                    // 범위 검증: 비크리 피해 ≤ 측정 평균 ≤ 크리×크리일격 피해
                    double minDamage = SimulatedSpellNonCritDamage;
                    double maxDamage = SimulatedSpellNonCritDamage * SimulatedSpellCritMultiplier * SimulatedSpellCritBlowMultiplier;
                    double margin = 0.1;
                    double adjustedMin = minDamage * (1 - margin);
                    double adjustedMax = maxDamage * (1 + margin);

                    bool isValid = (minDamage > 0 && maxDamage > 0)
                        ? (measuredPerHit >= adjustedMin && measuredPerHit <= adjustedMax)
                        : Math.Abs((measuredPerHit - SimulatedSpellHitDamage) / SimulatedSpellHitDamage * 100) <= 20.0;
                    string status = isValid ? "✅ PASS" : "❌ FAIL";

                    sb.AppendLine($"🔮 Spell ({hits}회, {hitsPerSec:F2}/s): {status}");
                    sb.AppendLine($"   DPS: 조정(시뮬1타×인게임공속)={FormatNumber(adjustedSimDPS)} / 인게임실측={FormatNumber(measured)}");
                    sb.AppendLine($"   1타(총피해÷타격수): 측정={FormatNumber(measuredPerHit)}");
                    if (minDamage > 0 && maxDamage > 0)
                    {
                        sb.AppendLine($"   PASS범위: {FormatNumber(adjustedMin)} ~ {FormatNumber(adjustedMax)} (비크리~크리일격±10%)");
                    }
                    else
                    {
                        double error = SimulatedSpellHitDamage > 0 ? (measuredPerHit - SimulatedSpellHitDamage) / SimulatedSpellHitDamage * 100 : 0;
                        sb.AppendLine($"   시뮬1타={FormatNumber(SimulatedSpellHitDamage)}, 오차={FormatError(error)}");
                    }
                }
                else
                {
                    // 시뮬 예측값이 있는데 수집 없으면 FAIL
                    string status = SimulatedSpellDPS > 0 ? "❌ FAIL (수집 없음)" : "⬜ 수집 없음";
                    sb.AppendLine($"🔮 Spell (0회): {status}");
                    sb.AppendLine($"   시뮬: {FormatNumber(SimulatedSpellDPS)} / 측정: -");
                }
            }

            // Ailment (항상 표시) - 범위 검증: 비크리 ~ 크리×크리일격 (±10% 마진)
            // Ailment는 최초 적용 시 1회 계산 후 duration 동안 분할 (1틱/초)
            {
                int totalProcs = AilmentResults.Values.Sum(a => a.TotalProcs);
                double totalAilmentDamage = AilmentResults.Values.Sum(a => a.TotalDamage);
                double totalAilmentDPS = AilmentResults.Values.Sum(a => a.MeasuredDPS);
                if (totalProcs > 0)
                {
                    double measuredPerHit = totalAilmentDamage / totalProcs;
                    double procsPerSec = totalProcs / Duration;

                    // 범위 검증: 비크리 ~ 크리×크리일격
                    double minDamage = SimulatedAilmentNonCritDamage;
                    double maxDamage = SimulatedAilmentNonCritDamage * SimulatedAilmentCritMultiplier * SimulatedAilmentCritBlowMultiplier;
                    double margin = 0.1;
                    double adjustedMin = minDamage * (1 - margin);
                    double adjustedMax = maxDamage * (1 + margin);

                    bool isValid = (minDamage > 0 && maxDamage > 0)
                        ? (measuredPerHit >= adjustedMin && measuredPerHit <= adjustedMax)
                        : true;
                    string status = isValid ? "✅ PASS" : "❌ FAIL";

                    sb.AppendLine($"💥 Ailment ({totalProcs}회, {procsPerSec:F2}/s): {status}");
                    sb.AppendLine($"   1회(총피해÷발동수): 측정={FormatNumber(measuredPerHit)}");
                    if (minDamage > 0 && maxDamage > 0)
                    {
                        sb.AppendLine($"   PASS범위: {FormatNumber(adjustedMin)} ~ {FormatNumber(adjustedMax)} (비크리~크리일격±10%)");
                    }
                    else
                    {
                        // 범위 정보 없으면 DPS 오차 표시
                        double error = SimulatedAilmentDPS > 0 ? (totalAilmentDPS - SimulatedAilmentDPS) / SimulatedAilmentDPS * 100 : 0;
                        sb.AppendLine($"   시뮬DPS: {FormatNumber(SimulatedAilmentDPS)} / 측정DPS: {FormatNumber(totalAilmentDPS)} ({FormatError(error)})");
                    }
                }
                else
                {
                    // 시뮬 예측값이 있는데 수집 없으면 FAIL
                    string status = SimulatedAilmentDPS > 0 ? "❌ FAIL (수집 없음)" : "⬜ 수집 없음";
                    sb.AppendLine($"💥 Ailment (0회): {status}");
                    sb.AppendLine($"   시뮬: {FormatNumber(SimulatedAilmentDPS)} / 측정: -");
                }
            }

            // Aura (항상 표시) - 이벤트 기반 스킬이므로 1회 피해 비교 방식 사용
            {
                int hits = AuraResult?.TotalHits ?? 0;
                double measured = AuraResult?.MeasuredDPS ?? 0;
                if (hits > 0)
                {
                    double measuredPerHit = AuraResult.TotalDamage / hits;
                    double hitsPerSec = hits / Duration;
                    double adjustedSimDPS = SimulatedAuraDPS * hitsPerSec;

                    // 범위 검증: 비크리 피해 ≤ 측정 평균 ≤ 크리×크리일격 피해
                    double minDamage = SimulatedAuraNonCritDamage;
                    double maxDamage = SimulatedAuraNonCritDamage * SimulatedAuraCritMultiplier * SimulatedAuraCritBlowMultiplier;
                    double margin = 0.1;
                    double adjustedMin = minDamage * (1 - margin);
                    double adjustedMax = maxDamage * (1 + margin);

                    bool isValid = (minDamage > 0 && maxDamage > 0)
                        ? (measuredPerHit >= adjustedMin && measuredPerHit <= adjustedMax)
                        : Math.Abs((measuredPerHit - SimulatedAuraDPS) / SimulatedAuraDPS * 100) <= 20.0;
                    string status = isValid ? "✅ PASS" : "❌ FAIL";

                    sb.AppendLine($"🌀 Aura ({hits}회, {hitsPerSec:F2}/s): {status}");
                    sb.AppendLine($"   DPS: 조정(시뮬1회×인게임빈도)={FormatNumber(adjustedSimDPS)} / 인게임실측={FormatNumber(measured)}");
                    sb.AppendLine($"   1회(총피해÷발생수): 측정={FormatNumber(measuredPerHit)}");
                    if (minDamage > 0 && maxDamage > 0)
                    {
                        sb.AppendLine($"   PASS범위: {FormatNumber(adjustedMin)} ~ {FormatNumber(adjustedMax)} (비크리~크리일격±10%)");
                    }
                    else
                    {
                        double error = SimulatedAuraDPS > 0 ? (measuredPerHit - SimulatedAuraDPS) / SimulatedAuraDPS * 100 : 0;
                        sb.AppendLine($"   시뮬1회={FormatNumber(SimulatedAuraDPS)}, 오차={FormatError(error)}");
                    }
                }
                else
                {
                    // 시뮬 예측값이 있는데 수집 없으면 FAIL
                    string status = SimulatedAuraDPS > 0 ? "❌ FAIL (수집 없음)" : "⬜ 수집 없음";
                    sb.AppendLine($"🌀 Aura (0회): {status}");
                    sb.AppendLine($"   시뮬: {FormatNumber(SimulatedAuraDPS)} / 측정: -");
                }
            }

            // DoT (항상 표시) - 1틱 피해 범위 검증
            {
                int hits = DotResult?.TotalHits ?? 0;
                double measured = DotResult?.MeasuredDPS ?? 0;
                if (hits > 0)
                {
                    double measuredPerTick = DotResult.TotalDamage / hits;
                    double ticksPerSec = hits / Duration;

                    // 샘플 부족 여부 확인
                    bool isSampleInsufficient = hits < 10;
                    string status;
                    if (isSampleInsufficient)
                    {
                        status = $"⏳ 샘플 부족 ({hits}/10)";
                    }
                    else
                    {
                        status = IsDotValid ? "✅ PASS" : "❌ FAIL";
                    }

                    sb.AppendLine($"☠️ DoT ({hits}회, {ticksPerSec:F2}/s): {status}");
                    sb.AppendLine($"   1틱(총피해÷틱수): 측정={FormatNumber(measuredPerTick)}");

                    // PASS 범위 표시 (비치명타~치명타 범위, Spell과 동일)
                    if (SimulatedDotNonCritDamage > 0)
                    {
                        double margin = 0.1;
                        double adjustedMin = SimulatedDotNonCritDamage * (1 - margin);
                        double critMultiplier = SimulatedDotCritMultiplier > 0 ? SimulatedDotCritMultiplier : 1.0;
                        double adjustedMax = SimulatedDotNonCritDamage * critMultiplier * (1 + margin);
                        sb.AppendLine($"   PASS범위: {FormatNumber(adjustedMin)} ~ {FormatNumber(adjustedMax)} (비크리~크리1틱±10%)");
                    }
                    else
                    {
                        // 비치명타 피해 정보 없으면 DPS 오차 표시
                        double error = SimulatedDotDPS > 0 ? (measured - SimulatedDotDPS) / SimulatedDotDPS * 100 : 0;
                        sb.AppendLine($"   시뮬DPS: {FormatNumber(SimulatedDotDPS)} / 측정DPS: {FormatNumber(measured)} ({FormatError(error)})");
                    }
                }
                else
                {
                    // 시뮬 예측값이 있는데 수집 없으면 FAIL
                    string status = SimulatedDotDPS > 0 ? "❌ FAIL (수집 없음)" : "⬜ 수집 없음";
                    sb.AppendLine($"☠️ DoT (0회): {status}");
                    sb.AppendLine($"   시뮬: {FormatNumber(SimulatedDotDPS)} / 측정: -");
                }
            }

            sb.AppendLine("────────────────────────────────────────");
            sb.AppendLine("📊 총 DPS 비교");

            // Spell도 조정된 시뮬 DPS 사용 (시뮬 1타 피해 × 인게임 시전 속도)
            double adjustedSpellSimDPS = SimulatedSpellDPS;
            if (SpellResult != null && SpellResult.TotalHits > 0 && Duration > 0)
            {
                double hitsPerSec = SpellResult.TotalHits / Duration;
                adjustedSpellSimDPS = SimulatedSpellHitDamage * hitsPerSec; // 시뮬 1타 피해 × 인게임 시전 속도
            }

            // Aura는 조정된 시뮬 DPS 사용 (시뮬 1회 피해 × 인게임 발생 빈도)
            double adjustedAuraSimDPS = SimulatedAuraDPS;
            if (AuraResult != null && AuraResult.TotalHits > 0 && Duration > 0)
            {
                double hitsPerSec = AuraResult.TotalHits / Duration;
                adjustedAuraSimDPS = SimulatedAuraDPS * hitsPerSec; // 시뮬 1회 피해 × 인게임 발생 빈도
            }
            double adjustedTotalSimDPS = adjustedSpellSimDPS + adjustedAuraSimDPS + SimulatedAilmentDPS + SimulatedDotDPS;

            sb.AppendLine($"   시뮬 총 DPS: {FormatNumber(adjustedTotalSimDPS)}");
            sb.AppendLine($"   측정 총 DPS: {FormatNumber(OverallMeasuredDPS)}");
            double totalError = adjustedTotalSimDPS > 0 ? (OverallMeasuredDPS - adjustedTotalSimDPS) / adjustedTotalSimDPS * 100 : 0;
            string totalErrorStr = adjustedTotalSimDPS > 0 ? FormatError(totalError) : "N/A (시뮬값 없음)";
            sb.AppendLine($"   오차:        {totalErrorStr}");
            sb.AppendLine("════════════════════════════════════════");
            sb.AppendLine($"🏁 종합: {(IsOverallValid ? "✅ 검증 통과" : "❌ 검증 실패")}");

            return sb.ToString();
        }

        /// <summary>
        /// 큰 숫자를 읽기 쉬운 형태로 포맷
        /// </summary>
        private string FormatNumber(double value)
        {
            if (value <= 0) return "0";

            if (value >= 1_000_000_000_000)
                return $"{value / 1_000_000_000_000:N2}T";
            if (value >= 1_000_000_000)
                return $"{value / 1_000_000_000:N2}B";
            if (value >= 1_000_000)
                return $"{value / 1_000_000:N2}M";
            if (value >= 1_000)
                return $"{value / 1_000:N2}K";

            return $"{value:N0}";
        }

        /// <summary>
        /// 오차율 포맷
        /// </summary>
        private string FormatError(double error)
        {
            return $"{(error >= 0 ? "+" : "")}{error:F1}%";
        }
    }

    /// <summary>
    /// 피해 유형별 실시간 검증 결과
    /// </summary>
    [Serializable]
    public class LiveDamageTypeResult
    {
        public ELiveDamageType DamageType;

        // 기본 통계
        public int TotalHits;
        public double TotalDamage;
        public double AverageDamage;
        public double StandardDeviation;
        public double MeasuredDPS;

        // 크리티컬 통계
        public int CriticalHits;
        public int CriticalBlowHits;
        public double MeasuredCritRate;             // %
        public double MeasuredCritBlowRate;         // %
        public double AverageCritMultiplier;

        // 신뢰구간
        public double CritRateLowerBound;           // %
        public double CritRateUpperBound;           // %
        public double AvgDamageLowerBound;
        public double AvgDamageUpperBound;

        // 시뮬레이션 비교 기준
        public double ExpectedCritRate;             // %
        public double ExpectedCritBlowRate;         // %
        public double ExpectedAverageDamage;
        public double ExpectedDPS;

        // 검증 판정
        public VerificationJudgment CritRateJudgment;
        public VerificationJudgment CritBlowRateJudgment;
        public VerificationJudgment AvgDamageJudgment;
        public VerificationJudgment DPSJudgment;

        public bool IsValid
        {
            get
            {
                // 샘플 부족 시 통과로 처리
                if (TotalHits < 10) return true;

                // 모든 판정이 통과해야 유효
                bool critValid = CritRateJudgment?.IsPass ?? true;
                bool avgDamageValid = AvgDamageJudgment?.IsPass ?? true;
                bool dpsValid = DPSJudgment?.IsPass ?? true;

                return critValid && avgDamageValid && dpsValid;
            }
        }

        /// <summary>
        /// 상세 결과 문자열
        /// </summary>
        public string GetDetailString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DamageType}] 검증 결과");
            sb.AppendLine("───────────────────────────────────────────────");

            // 1. 크리티컬 확률
            sb.AppendLine("[1] 크리티컬 확률");
            sb.AppendLine($"  설정값:     {ExpectedCritRate:F1}%");
            sb.AppendLine($"  측정값:     {MeasuredCritRate:F1}% ({CriticalHits}/{TotalHits}회)");
            sb.AppendLine($"  95% CI:     [{CritRateLowerBound:F1}%, {CritRateUpperBound:F1}%]");
            sb.AppendLine($"  결과:       {CritRateJudgment?.GetStatusIcon() ?? "⬜"} {CritRateJudgment?.Reason ?? "판정 없음"}");
            sb.AppendLine();

            // 2. 평균 피해
            sb.AppendLine("[2] 평균 피해");
            sb.AppendLine($"  시뮬 예측:  {ExpectedAverageDamage:N0}");
            sb.AppendLine($"  측정 평균:  {AverageDamage:N0}");
            sb.AppendLine($"  95% CI:     [{AvgDamageLowerBound:N0}, {AvgDamageUpperBound:N0}]");
            sb.AppendLine($"  결과:       {AvgDamageJudgment?.GetStatusIcon() ?? "⬜"} {AvgDamageJudgment?.Reason ?? "판정 없음"}");
            sb.AppendLine();

            // 3. DPS
            sb.AppendLine($"[3] {DamageType} DPS");
            sb.AppendLine($"  시뮬 예측:  {ExpectedDPS:N0}");
            sb.AppendLine($"  측정 DPS:   {MeasuredDPS:N0}");
            double dpsError = ExpectedDPS > 0 ? (MeasuredDPS - ExpectedDPS) / ExpectedDPS * 100 : 0;
            sb.AppendLine($"  오차:       {(dpsError >= 0 ? "+" : "")}{dpsError:F2}%");
            sb.AppendLine($"  결과:       {DPSJudgment?.GetStatusIcon() ?? "⬜"} {DPSJudgment?.Reason ?? "판정 없음"}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Ailment 실시간 검증 결과
    /// </summary>
    [Serializable]
    public class LiveAilmentResult
    {
        public EStatusEffect AilmentType;
        public string AilmentName;

        // 통계
        public int TotalAttempts;                   // 스킬 시전 횟수 (추정)
        public int TotalProcs;                      // 발동 횟수
        public double MeasuredProcRate;             // 측정된 발동률 (%)

        public double TotalDamage;
        public double MeasuredDPS;

        // 신뢰구간
        public double ProcRateLowerBound;
        public double ProcRateUpperBound;

        // 시뮬레이션 비교 기준
        public double ExpectedProcRate;             // %
        public double ExpectedDPS;

        // 검증 판정
        public VerificationJudgment ProcRateJudgment;
        public VerificationJudgment DPSJudgment;

        public bool IsValid
        {
            get
            {
                if (TotalProcs < 5) return true;    // 샘플 부족
                return (ProcRateJudgment?.IsPass ?? true) && (DPSJudgment?.IsPass ?? true);
            }
        }

        /// <summary>
        /// 상세 결과 문자열
        /// </summary>
        public string GetDetailString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{AilmentName}] 검증 결과");
            sb.AppendLine("───────────────────────────────────────────────");

            // 1. 발동 확률
            sb.AppendLine("[1] 발동 확률");
            sb.AppendLine($"  설정값:     {ExpectedProcRate:F1}%");
            sb.AppendLine($"  측정값:     {MeasuredProcRate:F1}% ({TotalProcs}/{TotalAttempts}회)");
            sb.AppendLine($"  95% CI:     [{ProcRateLowerBound:F1}%, {ProcRateUpperBound:F1}%]");
            sb.AppendLine($"  결과:       {ProcRateJudgment?.GetStatusIcon() ?? "⬜"} {ProcRateJudgment?.Reason ?? "판정 없음"}");
            sb.AppendLine();

            // 2. DPS
            sb.AppendLine("[2] Ailment DPS");
            sb.AppendLine($"  시뮬 예측:  {ExpectedDPS:N0}");
            sb.AppendLine($"  측정 DPS:   {MeasuredDPS:N0}");
            double dpsError = ExpectedDPS > 0 ? (MeasuredDPS - ExpectedDPS) / ExpectedDPS * 100 : 0;
            sb.AppendLine($"  오차:       {(dpsError >= 0 ? "+" : "")}{dpsError:F2}%");
            sb.AppendLine($"  결과:       {DPSJudgment?.GetStatusIcon() ?? "⬜"} {DPSJudgment?.Reason ?? "판정 없음"}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// 실시간 검증 결과 빌더
    /// 수집된 데이터와 시뮬레이션 예측값을 비교하여 검증 결과 생성
    /// </summary>
    public static class LiveVerificationResultBuilder
    {
        /// <summary>
        /// 검증 결과 생성
        /// </summary>
        public static LiveVerificationResult Build(
            LiveDamageCollector collector,
            double simSpellDPS,
            double simAuraDPS,
            double simAilmentDPS,
            double simDotDPS,
            double expectedCritRate,
            double expectedCritBlowRate,
            double simCritMultiplier = 0,
            double simCastSpeed = 0,
            double simSpellNonCritDamage = 0,
            double simSpellCritMult = 0,
            double simSpellCritBlowMult = 0,
            double simAuraNonCritDamage = 0,
            double simAuraCritMult = 0,
            double simAuraCritBlowMult = 0,
            double simAilmentNonCritDamage = 0,
            double simAilmentCritMult = 0,
            double simAilmentCritBlowMult = 0,
            double simDot1TickDamage = 0,
            double simDotNonCritDamage = 0,
            double simDotCritMult = 0)
        {
            // 1타 평균 피해 계산 (DPS / 시전 속도)
            double simSpellHitDamage = simCastSpeed > 0 ? simSpellDPS / simCastSpeed : 0;

            var result = new LiveVerificationResult
            {
                Timestamp = DateTime.Now,
                StartTime = collector.StartTime,
                EndTime = collector.IsCollecting ? UnityEngine.Time.time : collector.EndTime,
                SimulatedSpellDPS = simSpellDPS,
                SimulatedSpellHitDamage = simSpellHitDamage, // 1타 평균 피해 저장
                SimulatedSpellNonCritDamage = simSpellNonCritDamage,
                SimulatedSpellCritMultiplier = simSpellCritMult,
                SimulatedSpellCritBlowMultiplier = simSpellCritBlowMult,
                SimulatedAuraDPS = simAuraDPS,  // Aura 1회 피해 (틱 레이트 1.0 기준)
                SimulatedAuraNonCritDamage = simAuraNonCritDamage,
                SimulatedAuraCritMultiplier = simAuraCritMult,
                SimulatedAuraCritBlowMultiplier = simAuraCritBlowMult,
                SimulatedAilmentDPS = simAilmentDPS,
                SimulatedAilmentNonCritDamage = simAilmentNonCritDamage,
                SimulatedAilmentCritMultiplier = simAilmentCritMult,
                SimulatedAilmentCritBlowMultiplier = simAilmentCritBlowMult,
                SimulatedDotDPS = simDotDPS,
                SimulatedDot1TickDamage = simDot1TickDamage,
                SimulatedDotNonCritDamage = simDotNonCritDamage,
                SimulatedDotCritMultiplier = simDotCritMult,
                SimulatedTotalDPS = simSpellDPS + simAuraDPS + simAilmentDPS + simDotDPS,
                GrandTotalDamage = collector.TotalDamage,
                OverallMeasuredDPS = collector.CurrentDPS
            };

            // Spell 결과
            var spellStats = collector.CalculateSpellStatistics();
            if (spellStats.TotalHits > 0)
            {
                result.SpellResult = BuildDamageTypeResult(
                    spellStats, ELiveDamageType.Spell,
                    expectedCritRate, expectedCritBlowRate,
                    simSpellDPS, collector.Duration,
                    simCritMultiplier, simSpellHitDamage);
            }

            // Aura 결과 (Aura는 별도 시전 속도 사용 가능 - 일단 DPS 기반 검증 유지)
            var auraStats = collector.CalculateAuraStatistics();
            if (auraStats.TotalHits > 0)
            {
                result.AuraResult = BuildDamageTypeResult(
                    auraStats, ELiveDamageType.Aura,
                    expectedCritRate, 0,
                    simAuraDPS, collector.Duration,
                    simCritMultiplier, 0);  // Aura는 DPS 기반
            }

            // DoT 결과
            var dotStats = collector.CalculateDotStatistics();
            if (dotStats.TotalHits > 0)
            {
                result.DotResult = BuildDamageTypeResult(
                    dotStats, ELiveDamageType.DoT,
                    0, 0,
                    simDotDPS, collector.Duration,
                    0, 0);  // DoT는 크리티컬 없음
            }

            // Ailment 결과
            var ailmentStats = collector.CalculateAilmentStatistics();
            // 전체 측정된 Ailment DPS 계산 (비율 배분용)
            double totalMeasuredAilmentDPS = ailmentStats.Values.Sum(s => s.MeasuredDPS);
            foreach (var kvp in ailmentStats)
            {
                // 측정 비율에 따라 예상 DPS 배분
                double ratio = totalMeasuredAilmentDPS > 0 ? kvp.Value.MeasuredDPS / totalMeasuredAilmentDPS : 0;
                double expectedAilmentDPS = simAilmentDPS * ratio;
                result.AilmentResults[kvp.Key] = BuildAilmentResult(kvp.Value, kvp.Key, expectedAilmentDPS);
            }

            return result;
        }

        private static LiveDamageTypeResult BuildDamageTypeResult(
            DamageTypeStatistics stats,
            ELiveDamageType damageType,
            double expectedCritRate,
            double expectedCritBlowRate,
            double expectedDPS,
            double duration,
            double simCritMultiplier = 0,
            double simExpectedHitDamage = 0)
        {
            var result = new LiveDamageTypeResult
            {
                DamageType = damageType,
                TotalHits = stats.TotalHits,
                TotalDamage = stats.TotalDamage,
                AverageDamage = stats.AverageDamage,
                StandardDeviation = stats.StandardDeviation,
                MeasuredDPS = stats.MeasuredDPS,
                CriticalHits = stats.CriticalHits,
                CriticalBlowHits = stats.CriticalBlowHits,
                MeasuredCritRate = stats.MeasuredCritRate,
                MeasuredCritBlowRate = stats.MeasuredCritBlowRate,
                AverageCritMultiplier = stats.AverageCritMultiplier,
                CritRateLowerBound = stats.CritRateLowerBound,
                CritRateUpperBound = stats.CritRateUpperBound,
                AvgDamageLowerBound = stats.AvgDamageLowerBound,
                AvgDamageUpperBound = stats.AvgDamageUpperBound,
                ExpectedCritRate = expectedCritRate,
                ExpectedCritBlowRate = expectedCritBlowRate,
                ExpectedAverageDamage = simExpectedHitDamage > 0 ? simExpectedHitDamage : (expectedDPS > 0 && duration > 0 ? expectedDPS / stats.TotalHits * duration : 0),
                ExpectedDPS = expectedDPS
            };

            // 판정 생성
            if (stats.TotalHits >= 10)
            {
                result.CritRateJudgment = StatisticalAnalyzer.VerifyCriticalRate(
                    expectedCritRate / 100,
                    stats.CriticalHits,
                    stats.TotalHits);

                result.AvgDamageJudgment = StatisticalAnalyzer.VerifyAverageDamage(
                    result.ExpectedAverageDamage,
                    stats.AverageDamage);

                // 1타 피해 검증 (시전 속도 변동 배제, 순수 피해 계산만 검증)
                if (simExpectedHitDamage > 0 && simCritMultiplier > 0 && expectedCritRate > 0)
                {
                    // 1타 피해 범위 검증: 비크리~크리 범위 내인지 확인
                    result.DPSJudgment = StatisticalAnalyzer.VerifyHitDamageRange(
                        simExpectedHitDamage,
                        stats.AverageDamage,
                        expectedCritRate,
                        simCritMultiplier);
                }
                else
                {
                    // 1타 피해 정보가 없으면 기존 DPS 오차 검증
                    result.DPSJudgment = StatisticalAnalyzer.VerifyDPS(
                        expectedDPS,
                        stats.MeasuredDPS);
                }
            }

            return result;
        }

        private static LiveAilmentResult BuildAilmentResult(AilmentStatistics stats, EStatusEffect ailmentType, double expectedDPS)
        {
            var result = new LiveAilmentResult
            {
                AilmentType = ailmentType,
                AilmentName = GetAilmentName(ailmentType),
                TotalAttempts = stats.TotalAttempts,
                TotalProcs = stats.TotalProcs,
                MeasuredProcRate = stats.MeasuredProcRate,
                TotalDamage = stats.TotalDamage,
                MeasuredDPS = stats.MeasuredDPS,
                ProcRateLowerBound = stats.ProcRateLowerBound,
                ProcRateUpperBound = stats.ProcRateUpperBound,
                ExpectedDPS = expectedDPS
            };

            // 판정 생성 (샘플이 충분할 때만)
            if (stats.TotalProcs >= 5)
            {
                // DPS 검증
                if (expectedDPS > 0)
                {
                    result.DPSJudgment = StatisticalAnalyzer.VerifyDPS(
                        expectedDPS,
                        stats.MeasuredDPS);
                }

                // 발동률 검증 (예상 발동률이 있을 때만)
                // 현재 시뮬레이터에서 개별 ailment 발동률을 제공하지 않으므로 생략
                // TODO: 시뮬레이터에서 ailment별 발동률 전달 시 활성화
                // if (expectedProcRate > 0)
                // {
                //     result.ProcRateJudgment = StatisticalAnalyzer.VerifyProcRate(
                //         expectedProcRate / 100,
                //         stats.TotalProcs,
                //         stats.TotalAttempts);
                // }
            }

            return result;
        }

        private static string GetAilmentName(EStatusEffect ailmentType)
        {
            return ailmentType switch
            {
                EStatusEffect.ailment_ignite => "점화 (Ignite)",
                EStatusEffect.ailment_bleeding => "출혈 (Bleeding)",
                EStatusEffect.ailment_poisoning => "중독 (Poisoning)",
                EStatusEffect.ailment_shock => "감전 (Shock)",
                EStatusEffect.ailment_arctic => "동결 (Arctic)",
                EStatusEffect.ailment_chill => "한기 (Chill)",
                _ => ailmentType.ToString()
            };
        }
    }
}
