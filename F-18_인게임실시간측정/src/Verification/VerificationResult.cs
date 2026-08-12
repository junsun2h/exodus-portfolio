using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 단일 값 비교 결과
    /// </summary>
    [Serializable]
    public class ValueComparison
    {
        public string Name;              // 항목명
        public string Unit;              // 단위 (%, 배율, 등)
        public double SimValue;          // 시뮬레이션 값
        public double IngameValue;       // 인게임 값
        public double Difference;        // 차이 (절대값)
        public double ErrorPercent;      // 오차율 (%)
        public bool IsMatch;             // 허용 범위 내 일치 여부

        public ValueComparison(string name, double simValue, double ingameValue, double tolerancePercent = 0.1, string unit = "")
        {
            Name = name;
            Unit = unit;
            SimValue = simValue;
            IngameValue = ingameValue;
            Difference = Math.Abs(simValue - ingameValue);

            // 오차율 계산
            if (simValue == 0 && ingameValue == 0)
            {
                ErrorPercent = 0;
                IsMatch = true;
            }
            else if (simValue == 0)
            {
                ErrorPercent = 100;
                IsMatch = false;
            }
            else
            {
                ErrorPercent = Math.Abs((ingameValue - simValue) / simValue) * 100;
                IsMatch = ErrorPercent <= tolerancePercent;
            }
        }

        public string GetStatusIcon() => IsMatch ? "✅" : "❌";

        public string FormatValue(double value)
        {
            if (Unit == "%")
                return $"{value:F2}%";
            else if (Unit == "배율")
                return $"{value:F4}x";
            else
                return $"{value:N0}";
        }

        public override string ToString()
        {
            return $"{GetStatusIcon()} {Name}: 시뮬={FormatValue(SimValue)}, 인게임={FormatValue(IngameValue)}, 오차={ErrorPercent:F2}%";
        }
    }

    /// <summary>
    /// 계산 단계 정보
    /// </summary>
    [Serializable]
    public class CalculationStep
    {
        public int StepNumber;           // 단계 번호
        public string StepName;          // 단계 이름
        public string Formula;           // 계산 공식
        public double SimValue;          // 시뮬레이션 결과값
        public double IngameValue;       // 인게임 결과값
        public double ErrorPercent;      // 오차율
        public bool IsMatch;             // 일치 여부
        public List<VerificationModContribution> ModContributions; // MOD별 기여도

        public CalculationStep(int step, string name, string formula = "")
        {
            StepNumber = step;
            StepName = name;
            Formula = formula;
            ModContributions = new List<VerificationModContribution>();
        }

        public void SetValues(double simValue, double ingameValue, double tolerance = 0.1)
        {
            SimValue = simValue;
            IngameValue = ingameValue;

            if (simValue == 0 && ingameValue == 0)
            {
                ErrorPercent = 0;
                IsMatch = true;
            }
            else if (simValue == 0)
            {
                ErrorPercent = 100;
                IsMatch = false;
            }
            else
            {
                ErrorPercent = Math.Abs((ingameValue - simValue) / simValue) * 100;
                IsMatch = ErrorPercent <= tolerance;
            }
        }

        public string GetStatusIcon() => IsMatch ? "✅" : "❌";
    }

    /// <summary>
    /// 검증용 MOD 기여도 비교
    /// </summary>
    [Serializable]
    public class VerificationModContribution
    {
        public EMod ModType;
        public string ModName;
        public double SimValue;
        public double IngameValue;
        public bool IsMatch;

        public VerificationModContribution(EMod mod, double simValue, double ingameValue)
        {
            ModType = mod;
            ModName = mod.ToString();
            SimValue = simValue;
            IngameValue = ingameValue;
            IsMatch = Math.Abs(simValue - ingameValue) < 0.01;
        }
    }

    /// <summary>
    /// Spell 피해 검증 결과
    /// </summary>
    [Serializable]
    public class SpellVerificationResult
    {
        public bool IsEnabled;                      // Spell 사용 여부
        public ESkill SkillType;                    // 스킬 종류
        public ETier SkillTier;                     // 스킬 티어
        public ESkillTag DamageType;                // 피해 속성

        // 강화도 정보
        public int CoreReinforce;                   // 기본 강화도 (userData)
        public int ReinforceModAdd;                 // 강화도 모드 증가량 (mod_use_xxx_skill_reinforce_add)
        public int EffectiveReinforce;              // 최종 강화도 = Core + ModAdd

        // 단계별 계산 결과
        public List<CalculationStep> Steps = new List<CalculationStep>();

        // 주요 비교 항목
        public ValueComparison FlatDamage;          // 1. 플랫 피해 (Added)
        public ValueComparison SkillEffectiveness;  // 2. 스킬 효율
        public ValueComparison BaseDamage;          // 3. 기본 피해 (Flat × Effectiveness)
        public ValueComparison IncMultiplier;       // 4. Inc 배율
        public ValueComparison MoreMultiplier;      // 5. More 배율
        public ValueComparison PreCritDamage;       // 6. 크리티컬 전 피해
        public ValueComparison CriticalChance;      // 7. 크리티컬 확률
        public ValueComparison CriticalMultiplier;  // 8. 크리티컬 배율
        public ValueComparison CritBlowChance;      // 9. 치명타 일격 확률
        public ValueComparison CritBlowMultiplier;  // 10. 치명타 일격 배율
        public ValueComparison AverageCritMultiplier; // 11. 평균 크리티컬 배율
        public ValueComparison CastSpeed;           // 12. 시전 속도
        public ValueComparison FinalDPS;            // 13. 최종 DPS

        // 브레이크다운 (불일치 시 원인 분석용)
        public Dictionary<EMod, double> SimIncBreakdown = new Dictionary<EMod, double>();
        public Dictionary<EMod, double> IngameIncBreakdown = new Dictionary<EMod, double>();
        public Dictionary<EMod, double> SimMoreBreakdown = new Dictionary<EMod, double>();
        public Dictionary<EMod, double> IngameMoreBreakdown = new Dictionary<EMod, double>();

        public bool AllPassed
        {
            get
            {
                if (!IsEnabled) return true;
                return (FlatDamage?.IsMatch ?? true) &&
                       (IncMultiplier?.IsMatch ?? true) &&
                       (MoreMultiplier?.IsMatch ?? true) &&
                       (PreCritDamage?.IsMatch ?? true) &&
                       (CriticalChance?.IsMatch ?? true) &&
                       (CriticalMultiplier?.IsMatch ?? true);
            }
        }

        public int PassedCount => GetComparisons().Count(c => c?.IsMatch == true);
        public int TotalCount => GetComparisons().Count(c => c != null);

        private IEnumerable<ValueComparison> GetComparisons()
        {
            yield return FlatDamage;
            yield return SkillEffectiveness;
            yield return BaseDamage;
            yield return IncMultiplier;
            yield return MoreMultiplier;
            yield return PreCritDamage;
            yield return CriticalChance;
            yield return CriticalMultiplier;
            yield return CritBlowChance;
            yield return CritBlowMultiplier;
            yield return AverageCritMultiplier;
            yield return CastSpeed;
            yield return FinalDPS;
        }
    }

    /// <summary>
    /// Aura 피해 검증 결과
    /// </summary>
    [Serializable]
    public class AuraVerificationResult
    {
        public bool IsEnabled;                      // Aura 사용 여부
        public ESkill AuraType;                     // 오라 종류
        public ETier AuraTier;                      // 오라 티어
        public ESkillTag DamageType;                // 피해 속성

        // 단계별 계산 결과
        public List<CalculationStep> Steps = new List<CalculationStep>();

        // 주요 비교 항목
        public ValueComparison BaseDamage;          // 1. 기본 피해
        public ValueComparison DamagePercent;       // 2. 피해 비율 (Effectiveness)
        public ValueComparison Duration;            // 3. 지속시간
        public ValueComparison TickRate;            // 4. 틱 빈도
        public ValueComparison IncMultiplier;       // 5. Inc 배율
        public ValueComparison MoreMultiplier;      // 6. More 배율
        public ValueComparison CriticalChance;      // 7. 크리티컬 확률
        public ValueComparison CriticalMultiplier;  // 8. 크리티컬 배율
        public ValueComparison AverageDamage;       // 9. 평균 피해
        public ValueComparison FinalDPS;            // 10. 최종 DPS

        public bool AllPassed
        {
            get
            {
                if (!IsEnabled) return true;
                return (BaseDamage?.IsMatch ?? true) &&
                       (IncMultiplier?.IsMatch ?? true) &&
                       (MoreMultiplier?.IsMatch ?? true) &&
                       (FinalDPS?.IsMatch ?? true);
            }
        }

        public int PassedCount => GetComparisons().Count(c => c?.IsMatch == true);
        public int TotalCount => GetComparisons().Count(c => c != null);

        private IEnumerable<ValueComparison> GetComparisons()
        {
            yield return BaseDamage;
            yield return DamagePercent;
            yield return Duration;
            yield return TickRate;
            yield return IncMultiplier;
            yield return MoreMultiplier;
            yield return CriticalChance;
            yield return CriticalMultiplier;
            yield return AverageDamage;
            yield return FinalDPS;
        }
    }

    /// <summary>
    /// 개별 상태이상 검증 결과
    /// </summary>
    [Serializable]
    public class SingleAilmentVerificationResult
    {
        public EStatusEffect AilmentType;                   // 상태이상 종류
        public string AilmentName;                  // 상태이상 이름
        public ESkillTag DamageType;                // 피해 속성
        public bool IsEnabled;                      // 활성화 여부

        // 주요 비교 항목
        public ValueComparison ProcChance;          // 1. 발동 확률
        public ValueComparison Duration;            // 2. 지속시간
        public ValueComparison MaxStacks;           // 3. 최대 스택
        public ValueComparison DamagePercent;       // 4. 피해 비율
        public ValueComparison FlatDamage;          // 5. 기본 피해
        public ValueComparison IncMultiplier;       // 6. Inc 배율
        public ValueComparison MoreMultiplier;      // 7. More 배율
        public ValueComparison CriticalChance;      // 8. 크리티컬 확률
        public ValueComparison CriticalMultiplier;  // 9. 크리티컬 배율
        public ValueComparison AverageCritMultiplier; // 10. 평균 크리티컬 배율
        public ValueComparison TickDamage;          // 11. 틱당 피해
        public ValueComparison DPS;                 // 12. DPS

        public bool AllPassed
        {
            get
            {
                if (!IsEnabled) return true;
                return (ProcChance?.IsMatch ?? true) &&
                       (FlatDamage?.IsMatch ?? true) &&
                       (IncMultiplier?.IsMatch ?? true) &&
                       (MoreMultiplier?.IsMatch ?? true) &&
                       (DPS?.IsMatch ?? true);
            }
        }
    }

    /// <summary>
    /// Ailment 전체 검증 결과
    /// </summary>
    [Serializable]
    public class AilmentVerificationResult
    {
        public bool IsEnabled;                      // Ailment 사용 여부

        // 6종 상태이상 개별 결과
        public Dictionary<EStatusEffect, SingleAilmentVerificationResult> AilmentResults
            = new Dictionary<EStatusEffect, SingleAilmentVerificationResult>();

        // 총합
        public ValueComparison TotalDPS;

        public bool AllPassed
        {
            get
            {
                if (!IsEnabled) return true;
                return AilmentResults.Values.All(a => a.AllPassed);
            }
        }

        public int EnabledCount => AilmentResults.Values.Count(a => a.IsEnabled);
        public int PassedCount => AilmentResults.Values.Count(a => a.AllPassed);
    }

    /// <summary>
    /// DoT 피해 검증 결과 (skill_contagion 등)
    /// </summary>
    [Serializable]
    public class DotVerificationResult
    {
        public bool IsEnabled;                      // DoT 사용 여부
        public EStatusEffect DotType;                       // DoT 종류
        public string DotName;                      // DoT 이름
        public ESkillTag DamageType;                // 피해 속성

        // 주요 비교 항목
        public ValueComparison BaseDamage;          // 1. 기본 피해
        public ValueComparison DamagePercent;       // 2. 피해 비율
        public ValueComparison Duration;            // 3. 지속시간
        public ValueComparison TickInterval;        // 4. 틱 간격
        public ValueComparison IncMultiplier;       // 5. Inc 배율
        public ValueComparison MoreMultiplier;      // 6. More 배율
        public ValueComparison CriticalChance;      // 7. 크리티컬 확률
        public ValueComparison CriticalMultiplier;  // 8. 크리티컬 배율
        public ValueComparison TickDamage;          // 9. 틱당 피해
        public ValueComparison FinalDPS;            // 10. 최종 DPS

        public bool AllPassed
        {
            get
            {
                if (!IsEnabled) return true;
                return (BaseDamage?.IsMatch ?? true) &&
                       (IncMultiplier?.IsMatch ?? true) &&
                       (MoreMultiplier?.IsMatch ?? true) &&
                       (FinalDPS?.IsMatch ?? true);
            }
        }

        public int PassedCount => GetComparisons().Count(c => c?.IsMatch == true);
        public int TotalCount => GetComparisons().Count(c => c != null);

        private IEnumerable<ValueComparison> GetComparisons()
        {
            yield return BaseDamage;
            yield return DamagePercent;
            yield return Duration;
            yield return TickInterval;
            yield return IncMultiplier;
            yield return MoreMultiplier;
            yield return CriticalChance;
            yield return CriticalMultiplier;
            yield return TickDamage;
            yield return FinalDPS;
        }
    }

    /// <summary>
    /// 몬스터 → 플레이어 피해 검증 결과
    /// </summary>
    [Serializable]
    public class MonsterToPlayerVerificationResult
    {
        public bool IsEnabled;                      // 검증 활성화 여부

        // 몬스터 정보
        public string MonsterName;                  // 몬스터 이름
        public int MonsterLevel;                    // 몬스터 레벨
        public string MonsterTypeDisplay;           // 몬스터 타입 표시 (Normal/Boss/FloorBoss)
        public ESkillTag AttackElement;             // 몬스터 공격 속성

        // 몬스터 공격력
        public ValueComparison MonsterRawDamage;    // 몬스터 기본 공격력 (mod_all_damage)
        public ValueComparison MonsterAttackSpeed;  // 몬스터 공격 속도 (초당 공격 횟수)
        public ValueComparison MonsterRawDPS;       // 몬스터 순수 DPS (공격력 × 공격속도)

        // 플레이어 방어 스탯
        public ValueComparison PlayerLife;          // 플레이어 최대 생명력
        public ValueComparison PlayerPhysicalResistance;    // 물리 저항
        public ValueComparison PlayerFireResistance;        // 화염 저항
        public ValueComparison PlayerColdResistance;        // 냉기 저항
        public ValueComparison PlayerLightningResistance;   // 번개 저항
        public ValueComparison PlayerPoisonResistance;      // 독 저항
        public ValueComparison PlayerDamageReduction;       // 피해 감소 (mod_physical_damage_reduction 등)

        // 적용되는 저항 (몬스터 공격 속성 기준)
        public ValueComparison AppliedResistance;   // 몬스터 공격 속성에 해당하는 저항

        // 최종 받는 피해 계산
        public ValueComparison AfterResistanceDPS;  // 저항 적용 후 DPS
        public ValueComparison FinalDamageTaken;    // 최종 받는 DPS

        // 생존 시간
        public ValueComparison SurvivalTime;        // 예상 생존 시간 (초)

        // 1회 피격 피해 (몬스터 1회 공격당 피해)
        public ValueComparison DamagePerHit;        // 1회 피격 피해
        public ValueComparison HitsToKill;          // 플레이어 사망까지 필요한 피격 횟수

        public bool AllPassed =>
            !IsEnabled ||
            ((MonsterRawDamage?.IsMatch ?? true) &&
             (PlayerLife?.IsMatch ?? true) &&
             (SurvivalTime?.IsMatch ?? true));
    }

    /// <summary>
    /// 방어 적용 검증 결과 (Defense + 즉사 판정 포함)
    /// </summary>
    [Serializable]
    public class DefenseVerificationResult
    {
        public bool IsEnabled;                      // 방어 계산 활성화 여부

        // 방어자 정보
        public string DefenderName;
        public int DefenderLevel;
        public double DefenderMaxLife;              // 방어자 최대 생명력

        // 저항 정보 (방어자 기본값)
        public ValueComparison PhysicalResistance;
        public ValueComparison FireResistance;
        public ValueComparison ColdResistance;
        public ValueComparison LightningResistance;
        public ValueComparison PoisonResistance;

        // 최종 저항 (공격자 관통/감소 적용 후)
        public ValueComparison FinalResistance;     // 적용되는 저항 (피해 속성 기준)
        public double ResistanceReduction;          // 저항에 의한 피해 감소율

        // 받는 피해 증감
        public ValueComparison DamageTakenInc;      // 받는 피해 증가 %
        public ValueComparison DamageTakenDec;      // 받는 피해 감소 %
        public ValueComparison DamageTakenMultiplier; // 총 받는 피해 배율

        // 즉사 판정
        public ValueComparison InstantKillThreshold; // 즉사 임계값 (%)
        public ValueComparison InstantKillMultiplier; // 즉사 피해 배율

        // 단계별 DPS (총합)
        public ValueComparison BeforeDefenseDPS;    // 순수 피해 (방어 적용 전)
        public ValueComparison AfterResistanceDPS;  // 저항 적용 후
        public ValueComparison AfterDamageTakenDPS; // 받는 피해 증감 후
        public ValueComparison AfterInstantKillDPS; // 즉사 판정 후 최종 DPS

        // 개별 피해 유형별 최종 DPS (방어 적용 후)
        public ValueComparison SpellBeforeDefenseDPS;   // Spell 순수 DPS
        public ValueComparison SpellFinalDPS;           // Spell 최종 DPS
        public ValueComparison AuraBeforeDefenseDPS;    // Aura 순수 DPS
        public ValueComparison AuraFinalDPS;            // Aura 최종 DPS
        public ValueComparison AilmentBeforeDefenseDPS; // Ailment 순수 DPS
        public ValueComparison AilmentFinalDPS;         // Ailment 최종 DPS
        public ValueComparison DotBeforeDefenseDPS;     // DoT 순수 DPS
        public ValueComparison DotFinalDPS;             // DoT 최종 DPS

        // 처치 시간
        public ValueComparison TimeToKill;          // 처치 시간 (초)

        public bool AllPassed =>
            !IsEnabled ||
            ((BeforeDefenseDPS?.IsMatch ?? true) &&
             (AfterInstantKillDPS?.IsMatch ?? true));
    }

    /// <summary>
    /// 전체 검증 결과
    /// </summary>
    [Serializable]
    public class FullVerificationResult
    {
        // 검증 메타 정보
        public DateTime Timestamp;
        public string PresetName;
        public double VerificationDuration;         // 검증 소요 시간 (ms)

        // 4가지 피해 유형별 결과
        public SpellVerificationResult Spell = new SpellVerificationResult();
        public AuraVerificationResult Aura = new AuraVerificationResult();
        public AilmentVerificationResult Ailment = new AilmentVerificationResult();
        public DotVerificationResult Dot = new DotVerificationResult();

        // 방어 적용 결과
        public DefenseVerificationResult Defense = new DefenseVerificationResult();

        // 몬스터 → 플레이어 피해 검증 결과
        public MonsterToPlayerVerificationResult MonsterToPlayer = new MonsterToPlayerVerificationResult();

        // 총합 DPS 비교
        public ValueComparison TotalSimDPS;
        public ValueComparison TotalIngameDPS;
        public ValueComparison GrandTotalDPS;

        // DPS 구성 비율
        public double SpellDPSRatio;
        public double AuraDPSRatio;
        public double AilmentDPSRatio;
        public double DotDPSRatio;

        public bool AllPassed =>
            Spell.AllPassed &&
            Aura.AllPassed &&
            Ailment.AllPassed &&
            Dot.AllPassed &&
            Defense.AllPassed &&
            MonsterToPlayer.AllPassed;

        /// <summary>
        /// 요약 문자열 생성
        /// </summary>
        public string GetSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🔮 Spell: {(Spell.IsEnabled ? (Spell.AllPassed ? "✅ 통과" : "❌ 실패") : "⬜ 미사용")}");
            sb.AppendLine($"🌀 Aura: {(Aura.IsEnabled ? (Aura.AllPassed ? "✅ 통과" : "❌ 실패") : "⬜ 미사용")}");
            sb.AppendLine($"💥 Ailment: {(Ailment.IsEnabled ? (Ailment.AllPassed ? "✅ 통과" : "❌ 실패") : "⬜ 미사용")}");
            sb.AppendLine($"☠️ DoT: {(Dot.IsEnabled ? (Dot.AllPassed ? "✅ 통과" : "❌ 실패") : "⬜ 미사용")}");
            sb.AppendLine($"🛡️ Defense: {(Defense.IsEnabled ? (Defense.AllPassed ? "✅ 통과" : "❌ 실패") : "⬜ 미사용")}");
            sb.AppendLine($"👹 Monster→Player: {(MonsterToPlayer.IsEnabled ? (MonsterToPlayer.AllPassed ? "✅ 통과" : "❌ 실패") : "⬜ 미사용")}");
            sb.AppendLine();
            sb.AppendLine($"📊 전체: {(AllPassed ? "✅ 모든 검증 통과" : "❌ 일부 검증 실패")}");
            return sb.ToString();
        }
    }
}
