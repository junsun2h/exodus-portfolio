namespace BattleSimulator
{
    /// <summary>
    /// 시뮬레이터 순수 수학 함수 모음
    /// 싱글톤/MonoBehaviour 의존 없는 static 함수만 포함
    /// SimulatorCalculator의 인라인 계산을 정리하여 테스트 가능하게 분리
    /// </summary>
    public static class SimulatorMath
    {
        /// <summary>
        /// 크리티컬 기대 배율
        /// = (1 - critChance) × 1.0 + critChance × critMultiplier
        /// </summary>
        public static double CriticalExpectedMultiplier(double critChance, double critMultiplier)
        {
            double clampedChance = System.Math.Clamp(critChance, 0.0, 1.0);
            return (1.0 - clampedChance) * 1.0 + clampedChance * critMultiplier;
        }

        /// <summary>
        /// 기대 DPS = baseDamage × critExpected × attackSpeed × hitRate
        /// </summary>
        public static double ExpectedDPS(double baseDamage, double critChance, double critMultiplier, double attackSpeed, double hitRate)
        {
            double critExpected = CriticalExpectedMultiplier(critChance, critMultiplier);
            return baseDamage * critExpected * attackSpeed * hitRate;
        }

        /// <summary>
        /// 에일먼트 DPS = baseDamage × procChance × damagePercent × stacks / duration
        /// (지속 데미지: 총 데미지 / 지속시간 = 초당 데미지)
        /// </summary>
        public static double AilmentDPS(double baseDamage, double procChance, double damagePercent, int stacks, double duration)
        {
            if (duration <= 0) return 0;
            double clampedProc = System.Math.Clamp(procChance, 0.0, 1.0);
            return baseDamage * clampedProc * damagePercent * stacks / duration;
        }

        /// <summary>
        /// 에일먼트 총 데미지 = baseDamage × procChance × damagePercent × stacks
        /// </summary>
        public static double AilmentTotalDamage(double baseDamage, double procChance, double damagePercent, int stacks)
        {
            double clampedProc = System.Math.Clamp(procChance, 0.0, 1.0);
            return baseDamage * clampedProc * damagePercent * stacks;
        }

        /// <summary>
        /// Inc/More 합산: source × (1 + sumOfIncs) × Π(1 + more_i)
        /// incValues: Inc 배율 배열 (모두 합산)
        /// moreValues: More 배율 배열 (각각 개별 곱셈)
        /// </summary>
        public static double AggregateIncMore(double source, double[] incValues, double[] moreValues)
        {
            double incSum = 0;
            if (incValues != null)
            {
                for (int i = 0; i < incValues.Length; i++)
                    incSum += incValues[i];
            }

            double moreProduct = 1.0;
            if (moreValues != null)
            {
                for (int i = 0; i < moreValues.Length; i++)
                    moreProduct *= (1.0 + moreValues[i]);
            }

            return source * (1.0 + incSum) * moreProduct;
        }

        /// <summary>
        /// 저항 적용 데미지 배율 = 1 - resistance
        /// resistance가 음수면 데미지 증폭 (1 - (-0.2) = 1.2 → 120% 데미지)
        /// </summary>
        public static double ResistanceDamageMultiplier(double resistance)
        {
            return 1.0 - resistance;
        }

        /// <summary>
        /// 유효 HP = HP / (1 - resistance)
        /// 저항이 높을수록 유효 HP 증가
        /// </summary>
        public static double EffectiveHP(double hp, double resistance)
        {
            double multiplier = 1.0 - resistance;
            if (multiplier <= 0) return double.PositiveInfinity;
            return hp / multiplier;
        }

        /// <summary>
        /// ratio → 퍼센트 변환 (0.75 → 75.0)
        /// </summary>
        public static double ConvertRatioToPercent(double ratio)
        {
            return ratio * 100.0;
        }

        /// <summary>
        /// 킬 소요 시간 (초) = effectiveHP / dps
        /// </summary>
        public static double TimeToKill(double effectiveHP, double dps)
        {
            if (dps <= 0) return double.PositiveInfinity;
            return effectiveHP / dps;
        }
    }
}
