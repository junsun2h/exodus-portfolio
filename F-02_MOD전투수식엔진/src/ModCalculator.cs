using System.Text;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>
    /// PoE 스타일 MOD 계산 엔진
    /// - Flat: 합산 (additive)
    /// - Increased: 합산 후 곱셈 (additive)
    /// - More: 개별 곱셈 (multiplicative)
    ///
    /// 공식: 최종 데미지 = Flat × (1 + Total_Inc) × ∏(1 + More_i)
    /// 주의: TotalModValue()는 이미 비율 값 반환 (예: 37.1855 = 3718.55%)
    ///
    /// 특징:
    /// - Struct로 제로 할당 (Zero Allocation)
    /// - 16개 More MOD 지원 (현재 15개 + 예비 1개)
    /// - Fluent API (메서드 체이닝)
    /// </summary>
    public struct ModCalculator
    {
        private double flat;
        private double totalInc;

        // More는 최대 16개까지 지원 (15개 More MOD + 예비)
        private double m0, m1, m2, m3, m4, m5, m6, m7;
        private double m8, m9, m10, m11, m12, m13, m14, m15;
        private byte moreCount;  // 0-16

        /// <summary>
        /// Flat 값 추가
        /// </summary>
        /// <param name="value">추가할 Flat 값</param>
        /// <returns>체이닝을 위한 this</returns>
        public ModCalculator AddFlat(double value)
        {
            flat += value;
            return this;
        }

        /// <summary>
        /// Increased 비율 추가 (합산)
        /// </summary>
        /// <param name="incRatio">증가 비율 (예: 0.30 = 30%, 37.1855 = 3718.55%)</param>
        /// <returns>체이닝을 위한 this</returns>
        public ModCalculator AddInc(double incRatio)
        {
            totalInc += incRatio;
            return this;
        }

        /// <summary>
        /// More 비율 추가 (개별 보관)
        /// </summary>
        /// <param name="moreRatio">배율 비율 (예: 0.40 = 40%, 0.15 = 15%)</param>
        /// <returns>체이닝을 위한 this</returns>
        public ModCalculator AddMore(double moreRatio)
        {
            if (moreRatio == 0) return this;

            switch (moreCount)
            {
                case 0: m0 = moreRatio; break;
                case 1: m1 = moreRatio; break;
                case 2: m2 = moreRatio; break;
                case 3: m3 = moreRatio; break;
                case 4: m4 = moreRatio; break;
                case 5: m5 = moreRatio; break;
                case 6: m6 = moreRatio; break;
                case 7: m7 = moreRatio; break;
                case 8: m8 = moreRatio; break;
                case 9: m9 = moreRatio; break;
                case 10: m10 = moreRatio; break;
                case 11: m11 = moreRatio; break;
                case 12: m12 = moreRatio; break;
                case 13: m13 = moreRatio; break;
                case 14: m14 = moreRatio; break;
                case 15: m15 = moreRatio; break;
                default:
                    Debug.LogError($"[ModCalculator] More than 16 multipliers! Current: {moreCount + 1}");
                    return this;
            }
            moreCount++;
            return this;
        }

        /// <summary>
        /// 최종 계산
        /// 공식: Flat × (1 + Total_Inc) × ∏(1 + More_i)
        /// TotalModValue()가 이미 비율 값을 반환하므로 0.01 곱셈 불필요
        /// </summary>
        /// <returns>최종 계산된 값</returns>
        public double Calculate()
        {
            double result = flat;

            // Step 1: Increased 적용 (합산 후 한 번만 곱함)
            // TotalModValue()가 비율 반환 (예: 37.1855 = 3718.55%)
            result *= (1 + totalInc);

            // Step 2: More 적용 (각각 개별 곱셈)
            // TotalModValue()가 비율 반환 (예: 0.15 = 15%)
            if (moreCount > 0) result *= (1 + m0);
            if (moreCount > 1) result *= (1 + m1);
            if (moreCount > 2) result *= (1 + m2);
            if (moreCount > 3) result *= (1 + m3);
            if (moreCount > 4) result *= (1 + m4);
            if (moreCount > 5) result *= (1 + m5);
            if (moreCount > 6) result *= (1 + m6);
            if (moreCount > 7) result *= (1 + m7);
            if (moreCount > 8) result *= (1 + m8);
            if (moreCount > 9) result *= (1 + m9);
            if (moreCount > 10) result *= (1 + m10);
            if (moreCount > 11) result *= (1 + m11);
            if (moreCount > 12) result *= (1 + m12);
            if (moreCount > 13) result *= (1 + m13);
            if (moreCount > 14) result *= (1 + m14);
            if (moreCount > 15) result *= (1 + m15);

            return result;
        }

        /// <summary>
        /// 디버깅용 계산 과정 표시
        /// </summary>
        /// <returns>계산 과정 문자열</returns>
        public string GetCalculationBreakdown()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== ModCalculator Breakdown ===");
            sb.AppendLine($"Flat: {flat:N2}");
            sb.AppendLine($"Total Inc: {totalInc * 100:F2}% (비율: {totalInc:F4}) → ×{(1 + totalInc):F4}");
            sb.AppendLine($"More Count: {moreCount}");

            double afterInc = flat * (1 + totalInc);
            sb.AppendLine($"After Inc: {afterInc:N2}");

            if (moreCount > 0)
            {
                sb.AppendLine("More Multipliers:");
                double runningTotal = afterInc;
                for (int i = 0; i < moreCount; i++)
                {
                    double more = GetMoreAt(i);
                    runningTotal *= (1 + more);
                    sb.AppendLine($"  [{i}] {more * 100:F2}% (비율: {more:F4}) → ×{(1 + more):F4} = {runningTotal:N2}");
                }
            }

            sb.AppendLine($"Final Result: {Calculate():N2}");
            sb.AppendLine("==============================");
            return sb.ToString();
        }

        /// <summary>
        /// 지정된 인덱스의 More 값 반환
        /// </summary>
        private double GetMoreAt(int index)
        {
            return index switch
            {
                0 => m0, 1 => m1, 2 => m2, 3 => m3,
                4 => m4, 5 => m5, 6 => m6, 7 => m7,
                8 => m8, 9 => m9, 10 => m10, 11 => m11,
                12 => m12, 13 => m13, 14 => m14, 15 => m15,
                _ => 0
            };
        }

        /// <summary>
        /// Inc 배율 반환 (1 + Total_Inc)
        /// </summary>
        public double GetIncMultiplier()
        {
            return 1 + totalInc;
        }

        /// <summary>
        /// More 배율 반환 ∏(1 + More_i)
        /// </summary>
        public double GetMoreMultiplier()
        {
            double result = 1.0;
            if (moreCount > 0) result *= (1 + m0);
            if (moreCount > 1) result *= (1 + m1);
            if (moreCount > 2) result *= (1 + m2);
            if (moreCount > 3) result *= (1 + m3);
            if (moreCount > 4) result *= (1 + m4);
            if (moreCount > 5) result *= (1 + m5);
            if (moreCount > 6) result *= (1 + m6);
            if (moreCount > 7) result *= (1 + m7);
            if (moreCount > 8) result *= (1 + m8);
            if (moreCount > 9) result *= (1 + m9);
            if (moreCount > 10) result *= (1 + m10);
            if (moreCount > 11) result *= (1 + m11);
            if (moreCount > 12) result *= (1 + m12);
            if (moreCount > 13) result *= (1 + m13);
            if (moreCount > 14) result *= (1 + m14);
            if (moreCount > 15) result *= (1 + m15);
            return result;
        }
    }
}
