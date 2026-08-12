using System;
using System.Collections.Generic;
using System.Linq;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 통계 분석기
    /// 피해 데이터의 통계적 검증 및 신뢰구간 계산
    /// </summary>
    public static class StatisticalAnalyzer
    {
        // Z값 (95% 신뢰수준)
        private const double Z_95 = 1.96;

        // 오차 허용 범위
        private const double CRIT_RATE_TOLERANCE = 5.0;     // 크리티컬 확률 ±5%
        private const double AVG_DAMAGE_TOLERANCE = 15.0;   // 평균 피해 ±15%
        private const double DPS_TOLERANCE = 20.0;          // DPS ±20%
        private const double PROC_RATE_TOLERANCE = 10.0;    // 발동률 ±10%

        #region 신뢰구간 계산

        /// <summary>
        /// Wilson Score Interval (이항분포 신뢰구간)
        /// 크리티컬 확률, 상태이상 발동률 등 이항 확률에 사용
        /// </summary>
        public static (double lower, double upper) CalculateWilsonInterval(int successes, int trials, double confidence = 0.95)
        {
            if (trials == 0) return (0, 1);

            double z = confidence == 0.95 ? Z_95 : GetZScore(confidence);
            double p = (double)successes / trials;
            double n = trials;

            double denominator = 1 + z * z / n;
            double center = p + z * z / (2 * n);
            double spread = z * Math.Sqrt(p * (1 - p) / n + z * z / (4 * n * n));

            double lower = (center - spread) / denominator;
            double upper = (center + spread) / denominator;

            return (Math.Max(0, lower), Math.Min(1, upper));
        }

        /// <summary>
        /// 정규분포 신뢰구간 (평균 피해 등에 사용)
        /// </summary>
        public static (double lower, double upper) CalculateNormalInterval(double mean, double stdDev, int n, double confidence = 0.95)
        {
            if (n == 0) return (0, 0);

            double z = confidence == 0.95 ? Z_95 : GetZScore(confidence);
            double se = stdDev / Math.Sqrt(n);  // 표준 오차

            double margin = z * se;
            return (mean - margin, mean + margin);
        }

        /// <summary>
        /// Z 점수 조회 (95% 외 신뢰수준)
        /// </summary>
        private static double GetZScore(double confidence)
        {
            // 근사값 사용
            if (Math.Abs(confidence - 0.90) < 0.01) return 1.645;
            if (Math.Abs(confidence - 0.95) < 0.01) return 1.96;
            if (Math.Abs(confidence - 0.99) < 0.01) return 2.576;
            return 1.96;  // 기본값
        }

        #endregion

        #region 검증 판정

        /// <summary>
        /// 크리티컬 확률 검증
        /// 설정값이 95% 신뢰구간 내에 있는지 확인
        /// </summary>
        public static VerificationJudgment VerifyCriticalRate(
            double expectedRate,        // 예상 크리티컬 확률 (0~1)
            int criticalHits,           // 크리티컬 발생 횟수
            int totalHits)              // 총 타격 횟수
        {
            var ci = CalculateWilsonInterval(criticalHits, totalHits);
            double measuredRate = totalHits > 0 ? (double)criticalHits / totalHits : 0;

            bool isPass = expectedRate >= ci.lower && expectedRate <= ci.upper;

            return new VerificationJudgment
            {
                ItemName = "크리티컬 확률",
                ExpectedValue = expectedRate * 100,
                MeasuredValue = measuredRate * 100,
                LowerBound = ci.lower * 100,
                UpperBound = ci.upper * 100,
                IsPass = isPass,
                Reason = isPass
                    ? $"설정값 {expectedRate * 100:F1}%가 95% 신뢰구간 [{ci.lower * 100:F1}%, {ci.upper * 100:F1}%] 내"
                    : $"설정값 {expectedRate * 100:F1}%가 95% 신뢰구간 [{ci.lower * 100:F1}%, {ci.upper * 100:F1}%] 밖"
            };
        }

        /// <summary>
        /// 평균 피해 검증
        /// 시뮬레이션 예측값과 ±15% 이내인지 확인
        /// </summary>
        public static VerificationJudgment VerifyAverageDamage(
            double expectedDamage,      // 시뮬레이션 예측 평균 피해
            double measuredDamage,      // 측정된 평균 피해
            double tolerance = AVG_DAMAGE_TOLERANCE)
        {
            double errorPercent = expectedDamage > 0
                ? Math.Abs(measuredDamage - expectedDamage) / expectedDamage * 100
                : 100;

            bool isPass = errorPercent <= tolerance;

            return new VerificationJudgment
            {
                ItemName = "평균 피해",
                ExpectedValue = expectedDamage,
                MeasuredValue = measuredDamage,
                ErrorPercent = errorPercent,
                Tolerance = tolerance,
                IsPass = isPass,
                Reason = isPass
                    ? $"오차 {errorPercent:F2}%가 허용 범위 ±{tolerance}% 이내"
                    : $"오차 {errorPercent:F2}%가 허용 범위 ±{tolerance}% 초과"
            };
        }

        /// <summary>
        /// DPS 검증
        /// 시뮬레이션 예측값과 ±20% 이내인지 확인
        /// </summary>
        public static VerificationJudgment VerifyDPS(
            double expectedDPS,         // 시뮬레이션 예측 DPS
            double measuredDPS,         // 측정된 DPS
            double tolerance = DPS_TOLERANCE)
        {
            double errorPercent = expectedDPS > 0
                ? Math.Abs(measuredDPS - expectedDPS) / expectedDPS * 100
                : 100;

            bool isPass = errorPercent <= tolerance;

            return new VerificationJudgment
            {
                ItemName = "DPS",
                ExpectedValue = expectedDPS,
                MeasuredValue = measuredDPS,
                ErrorPercent = errorPercent,
                Tolerance = tolerance,
                IsPass = isPass,
                Reason = isPass
                    ? $"오차 {errorPercent:F2}%가 허용 범위 ±{tolerance}% 이내"
                    : $"오차 {errorPercent:F2}%가 허용 범위 ±{tolerance}% 초과"
            };
        }

        /// <summary>
        /// 1타 피해 범위 검증 (치명타 범위)
        /// 공격 속도 변동을 제거하고 순수하게 1타 피해만 검증
        /// </summary>
        /// <param name="expectedAvgHitDamage">시뮬레이션 1타 평균 피해 (크리티컬 평균 배율 적용됨)</param>
        /// <param name="measuredAvgHitDamage">측정된 평균 1타 피해</param>
        /// <param name="critRate">크리티컬 확률 (0~100%)</param>
        /// <param name="critMultiplier">크리티컬 배율 (% 단위, 예: 1776.796%)</param>
        /// <param name="tolerance">추가 허용 오차 (0~1, 기본 0.05 = ±5%)</param>
        public static VerificationJudgment VerifyHitDamageRange(
            double expectedAvgHitDamage,
            double measuredAvgHitDamage,
            double critRate,
            double critMultiplier,
            double tolerance = 0.05)
        {
            // 크리티컬 확률을 비율로 변환
            double critRateRatio = Math.Clamp(critRate / 100.0, 0, 1);
            // 크리티컬 배율을 실제 배율로 변환 (1776.796% -> 17.77)
            double critMultRatio = critMultiplier / 100.0;

            // 평균 크리티컬 배율 계산
            double avgCritMultiplier = (1 - critRateRatio) * 1.0 + critRateRatio * critMultRatio;

            // 기본 1타 피해 (크리티컬 미적용)
            double baseHitDamage = avgCritMultiplier > 0 ? expectedAvgHitDamage / avgCritMultiplier : expectedAvgHitDamage;

            // 범위 계산 (순수 크리티컬 변동만)
            double minDamage = baseHitDamage * 1.0;           // 비크리티컬 (0% 크리티컬)
            double maxDamage = baseHitDamage * critMultRatio; // 크리티컬 (100% 크리티컬)

            // 약간의 여유 적용 (기타 확률적 변동)
            double adjustedMinDamage = minDamage * (1 - tolerance);
            double adjustedMaxDamage = maxDamage * (1 + tolerance);

            bool isPass = measuredAvgHitDamage >= adjustedMinDamage && measuredAvgHitDamage <= adjustedMaxDamage;

            return new VerificationJudgment
            {
                ItemName = "1타 피해 범위",
                ExpectedValue = expectedAvgHitDamage,
                MeasuredValue = measuredAvgHitDamage,
                LowerBound = adjustedMinDamage,
                UpperBound = adjustedMaxDamage,
                IsPass = isPass,
                Reason = isPass
                    ? $"측정값이 가능 범위 내 ({FormatLargeNumber(adjustedMinDamage)}~{FormatLargeNumber(adjustedMaxDamage)})"
                    : $"측정값이 가능 범위 밖 ({FormatLargeNumber(adjustedMinDamage)}~{FormatLargeNumber(adjustedMaxDamage)})"
            };
        }

        /// <summary>
        /// 큰 숫자를 읽기 쉬운 형태로 포맷
        /// </summary>
        private static string FormatLargeNumber(double value)
        {
            if (value >= 1_000_000_000_000) return $"{value / 1_000_000_000_000:N2}T";
            if (value >= 1_000_000_000) return $"{value / 1_000_000_000:N2}B";
            if (value >= 1_000_000) return $"{value / 1_000_000:N2}M";
            if (value >= 1_000) return $"{value / 1_000:N2}K";
            return $"{value:N0}";
        }

        /// <summary>
        /// 상태이상 발동률 검증
        /// </summary>
        public static VerificationJudgment VerifyProcRate(
            double expectedRate,        // 예상 발동률 (0~1)
            int procs,                  // 발동 횟수
            int attempts)               // 시도 횟수
        {
            var ci = CalculateWilsonInterval(procs, attempts);
            double measuredRate = attempts > 0 ? (double)procs / attempts : 0;

            bool isPass = expectedRate >= ci.lower && expectedRate <= ci.upper;

            return new VerificationJudgment
            {
                ItemName = "발동 확률",
                ExpectedValue = expectedRate * 100,
                MeasuredValue = measuredRate * 100,
                LowerBound = ci.lower * 100,
                UpperBound = ci.upper * 100,
                IsPass = isPass,
                Reason = isPass
                    ? $"설정값 {expectedRate * 100:F1}%가 95% 신뢰구간 [{ci.lower * 100:F1}%, {ci.upper * 100:F1}%] 내"
                    : $"설정값 {expectedRate * 100:F1}%가 95% 신뢰구간 [{ci.lower * 100:F1}%, {ci.upper * 100:F1}%] 밖"
            };
        }

        #endregion

        #region 상대 오차 계산

        /// <summary>
        /// 상대 오차 계산
        /// </summary>
        public static double CalculateRelativeError(double expected, double measured)
        {
            if (expected == 0) return measured == 0 ? 0 : 100;
            return (measured - expected) / expected * 100;
        }

        /// <summary>
        /// 상대 오차 절대값
        /// </summary>
        public static double CalculateAbsoluteRelativeError(double expected, double measured)
        {
            return Math.Abs(CalculateRelativeError(expected, measured));
        }

        #endregion

        #region 샘플 크기 계산

        /// <summary>
        /// 필요한 샘플 크기 계산 (이항분포)
        /// </summary>
        public static int CalculateRequiredSampleSize(
            double probability,         // 확률 (0~1)
            double marginOfError,       // 오차 범위 (0~1)
            double confidence = 0.95)   // 신뢰 수준
        {
            double z = confidence == 0.95 ? Z_95 : GetZScore(confidence);
            double n = (z * z * probability * (1 - probability)) / (marginOfError * marginOfError);
            return (int)Math.Ceiling(n);
        }

        #endregion
    }

    /// <summary>
    /// 검증 판정 결과
    /// </summary>
    [Serializable]
    public class VerificationJudgment
    {
        public string ItemName;             // 검증 항목명
        public double ExpectedValue;        // 예상값
        public double MeasuredValue;        // 측정값

        // 신뢰구간 (이항분포용)
        public double LowerBound;
        public double UpperBound;

        // 오차 (정규분포용)
        public double ErrorPercent;
        public double Tolerance;

        public bool IsPass;
        public string Reason;

        public string GetStatusIcon() => IsPass ? "✅" : "❌";

        public override string ToString()
        {
            return $"{GetStatusIcon()} {ItemName}: 예상={ExpectedValue:F2}, 측정={MeasuredValue:F2} - {Reason}";
        }
    }
}
