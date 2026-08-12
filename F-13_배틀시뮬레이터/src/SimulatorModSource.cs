using System;
using System.Collections.Generic;
using UnityEngine;
using PX;
using Sirenix.OdinInspector;

namespace BattleSimulator
{
    /// <summary>
    /// MOD 소스 추적을 위한 데이터 클래스
    /// 각 MOD 값이 어디서 왔는지 추적
    /// </summary>
    [Serializable]
    public class SimulatorModSource
    {
        /// <summary>
        /// 소스 이름 (예: "Weapon: Fire Sword")
        /// </summary>
        public string sourceName;

        /// <summary>
        /// MOD 타입
        /// </summary>
        public string modType;

        /// <summary>
        /// 기여값 (원본)
        /// </summary>
        [HideInInspector]
        public double value;

        /// <summary>
        /// MOD 값 타입 (FLOAT, FLOAT_PER 등)
        /// </summary>
        [HideInInspector]
        public EModValueType valueType;

        /// <summary>
        /// 기여값 (포맷팅됨)
        /// </summary>
        [LabelText("기여값")]
        [ShowInInspector]
        [DisplayAsString]
        public string ValueFormatted
        {
            get
            {
                // ValueType을 기반으로 표시 형식 결정
                if (valueType == EModValueType.FLOAT_PER)
                {
                    // FLOAT_PER: 비율값이므로 * 100하여 백분율로 표시
                    double displayValue = value * 100.0;
                    return BattleSimulatorWindow.ShouldUseScientificFormat(displayValue)
                        ? $"{BattleSimulatorWindow.FormatNumber(displayValue)}%"
                        : $"{displayValue:F3}%";
                }
                else
                {
                    // FLOAT 등: 평탄값 그대로 표시
                    return BattleSimulatorWindow.ShouldUseScientificFormat(value)
                        ? BattleSimulatorWindow.FormatNumber(value)
                        : value.ToString("F3");
                }
            }
        }

        /// <summary>
        /// 소스 타입 (EModSourceType - 17가지 세부 분류)
        /// </summary>
        [LabelText("소스 타입")]
        public EModSourceType detailedSourceType;

        public SimulatorModSource(string name, string mod, double val, EModSourceType detailedType)
        {
            sourceName = name;
            modType = mod;
            value = val;
            detailedSourceType = detailedType;
            // modType(EMod.ToString())에서 GameDB를 통해 ValueType 조회
            valueType = GetModValueTypeFromString(mod);
        }

        public SimulatorModSource(string name, string mod, double val, EModSourceType detailedType, EModValueType valType)
        {
            sourceName = name;
            modType = mod;
            value = val;
            detailedSourceType = detailedType;
            valueType = valType;
        }

        /// <summary>
        /// modType 문자열에서 EMod를 파싱하여 GameDB에서 ValueType 조회
        /// </summary>
        private static EModValueType GetModValueTypeFromString(string modTypeStr)
        {
            // EMod enum으로 파싱 시도
            if (System.Enum.TryParse<EMod>(modTypeStr, out EMod mod))
            {
                var modDB = GameDBClientManager.Instance?.GameDB_Mod?.Mod;
                if (modDB != null && modDB.MapData != null && modDB.MapData.TryGetValue(mod, out var modData))
                {
                    return modData.ValueType;
                }
            }
            // 파싱 실패 시 기본값
            return EModValueType.FLOAT_PER;
        }
    }


    /// <summary>
    /// 계산 단계 추적을 위한 데이터 클래스
    /// </summary>
    [Serializable]
    public class SimulatorCalculationStep
    {
        /// <summary>
        /// 단계 이름 (예: "Physical Damage - Base")
        /// </summary>
        public string stepName;

        /// <summary>
        /// 기본값 (원본)
        /// </summary>
        [HideInInspector]
        public double baseValue;

        /// <summary>
        /// 기본값 (포맷팅됨)
        /// </summary>
        [LabelText("기본값")]
        [ShowInInspector]
        [DisplayAsString]
        public string BaseValueFormatted => BattleSimulatorWindow.ShouldUseScientificFormat(baseValue)
            ? BattleSimulatorWindow.FormatNumber(baseValue)
            : baseValue.ToString("N0");

        /// <summary>
        /// 결과값 (원본)
        /// </summary>
        [HideInInspector]
        public double resultValue;

        /// <summary>
        /// 결과값 (포맷팅됨)
        /// </summary>
        [LabelText("결과값")]
        [ShowInInspector]
        [DisplayAsString]
        public string ResultValueFormatted => BattleSimulatorWindow.ShouldUseScientificFormat(resultValue)
            ? BattleSimulatorWindow.FormatNumber(resultValue)
            : resultValue.ToString("N0");

        /// <summary>
        /// 연산식 (예: "250 + 86 + 12 = 348")
        /// </summary>
        public string formula;

        /// <summary>
        /// 계산 단계 (Flat/Increased/More)
        /// </summary>
        public ECalculationStage stage;

        /// <summary>
        /// 이 단계에 기여한 소스 목록
        /// </summary>
        public List<SimulatorModSource> sources;

        public SimulatorCalculationStep()
        {
            sources = new List<SimulatorModSource>();
        }
    }

    /// <summary>
    /// 계산 단계 타입
    /// </summary>
    public enum ECalculationStage
    {
        Flat,      // 기본값 (덧셈)
        Increased, // 증가 (곱셈, 덧셈 후)
        More       // 추가 (곱셈, 곱셈)
    }

    /// <summary>
    /// MOD별 출처 정보를 그룹화한 래퍼 클래스
    /// Key (MOD 이름), TotalValue (합계), Sources (상세 출처 리스트) 표시용
    /// </summary>
    [Serializable]
    public class SimulatorModSourceGroup
    {
        /// <summary>
        /// MOD 이름 (예: "Physical Damage")
        /// </summary>
        [PropertyOrder(1)]
        [LabelText("MOD")]
        public string modName;

        /// <summary>
        /// MOD 값 타입 (INT, FLOAT, FLOAT_PER 등)
        /// </summary>
        [PropertyOrder(2)]
        [LabelText("ValueType")]
        public EModValueType valueType;

        /// <summary>
        /// 이 MOD의 총 합산 값 (소수점 3자리 또는 과학적 표기법)
        /// </summary>
        [PropertyOrder(3)]
        [LabelText("총 합계")]
        [GUIColor(0.3f, 1f, 0.3f)]
        [ShowInInspector]
        [DisplayAsString]
        public string TotalValueFormatted => BattleSimulatorWindow.ShouldUseScientificFormat(totalValue)
            ? BattleSimulatorWindow.FormatNumber(totalValue)
            : totalValue.ToString("F3");

        [HideInInspector]
        public double totalValue;

        /// <summary>
        /// 출처별 상세 리스트
        /// </summary>
        [PropertyOrder(4)]
        [LabelText("출처 상세")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = false)]
        public List<SimulatorModSource> sources;

        public SimulatorModSourceGroup(string name, EModValueType type, double total, List<SimulatorModSource> sourceList)
        {
            modName = name;
            valueType = type;
            totalValue = total;
            sources = sourceList ?? new List<SimulatorModSource>();
        }
    }

    /// <summary>
    /// 브레이크다운 값 타입
    /// </summary>
    public enum EBreakdownValueType
    {
        Flat,       // 자연수 (예: 250,000)
        Percentage, // 퍼센트 (예: 35.00%)
        Multiplier  // 배율 (예: 2.50x)
    }

    /// <summary>
    /// 계산 단계 브레이크다운 정보
    /// 특정 계산 단계(예: Aura Inc)에 기여하는 모든 MOD들의 상세 정보
    /// </summary>
    [Serializable]
    public class StageBreakdown
    {
        /// <summary>
        /// 단계 이름 (예: "Aura Inc")
        /// </summary>
        [PropertyOrder(1)]
        [LabelText("계산 단계")]
        [GUIColor(1f, 0.8f, 0.3f)]
        public string stageName;

        /// <summary>
        /// 단계 설명
        /// </summary>
        [PropertyOrder(2)]
        [LabelText("설명")]
        [TextArea(2, 5)]
        public string description;

        /// <summary>
        /// 값 타입 (Flat, Percentage, Multiplier)
        /// </summary>
        [HideInInspector]
        public EBreakdownValueType valueType;

        /// <summary>
        /// 최종 합산 값 (타입에 따라 표시 형식 변경)
        /// </summary>
        [PropertyOrder(3)]
        [LabelText("최종 값")]
        [GUIColor(0.3f, 1f, 0.3f)]
        [ShowInInspector]
        [DisplayAsString]
        public string FinalValueFormatted
        {
            get
            {
                switch (valueType)
                {
                    case EBreakdownValueType.Flat:
                        // 설정에 따라 큰 숫자 포맷 적용
                        return BattleSimulatorWindow.ShouldUseScientificFormat(finalValue)
                            ? BattleSimulatorWindow.FormatNumber(finalValue)
                            : $"{finalValue:F2}";
                    case EBreakdownValueType.Percentage:
                        // FLOAT_PER 타입 MOD들의 합산이므로 비율값 * 100으로 백분율 표시
                        double percentValue = finalValue * 100.0;
                        return BattleSimulatorWindow.ShouldUseScientificFormat(percentValue)
                            ? $"{BattleSimulatorWindow.FormatNumber(percentValue)}%"
                            : $"{percentValue:F2}%";
                    case EBreakdownValueType.Multiplier:
                        return $"{finalValue:F2}x"; // 배율
                    default:
                        return $"{finalValue:F2}";
                }
            }
        }

        [HideInInspector]
        public double finalValue;

        /// <summary>
        /// 이 단계에 기여하는 MOD 목록 (각 MOD의 출처 포함)
        /// </summary>
        [PropertyOrder(4)]
        [LabelText("기여 MOD 목록")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<ModContribution> modContributions;

        public StageBreakdown()
        {
            modContributions = new List<ModContribution>();
            valueType = EBreakdownValueType.Percentage; // 기본값
        }

        public StageBreakdown(string name, string desc, EBreakdownValueType type = EBreakdownValueType.Percentage)
        {
            stageName = name;
            description = desc;
            valueType = type;
            finalValue = 0;
            modContributions = new List<ModContribution>();
        }
    }

    /// <summary>
    /// 개별 MOD의 기여도 정보
    /// </summary>
    [Serializable]
    public class ModContribution
    {
        /// <summary>
        /// MOD 이름
        /// </summary>
        [PropertyOrder(1)]
        [LabelText("MOD")]
        [GUIColor(0.8f, 0.9f, 1f)]
        public string modName;

        /// <summary>
        /// 이 MOD의 총 기여값
        /// </summary>
        [PropertyOrder(2)]
        [LabelText("기여값")]
        [GUIColor(0.3f, 1f, 0.3f)]
        [ShowInInspector]
        [DisplayAsString]
        public string ContributionFormatted
        {
            get
            {
                // ValueType에 따라 다른 포맷 적용
                if (valueType == EModValueType.FLOAT)
                {
                    // FLOAT 타입은 % 없이 표시
                    return BattleSimulatorWindow.ShouldUseScientificFormat(contribution)
                        ? BattleSimulatorWindow.FormatNumber(contribution)
                        : $"{contribution:F3}";
                }
                else
                {
                    // FLOAT_PER 타입은 비율값이므로 * 100 하여 백분율로 표시
                    double displayValue = contribution * 100.0;
                    return BattleSimulatorWindow.ShouldUseScientificFormat(displayValue)
                        ? $"{BattleSimulatorWindow.FormatNumber(displayValue)}%"
                        : $"{displayValue:F3}%";
                }
            }
        }

        [HideInInspector]
        public double contribution;

        [HideInInspector]
        public EModValueType valueType;

        /// <summary>
        /// 출처별 상세 (어떤 장비, 스킬 등에서 왔는지)
        /// </summary>
        [PropertyOrder(3)]
        [LabelText("출처 상세")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = false)]
        public List<SimulatorModSource> sources;

        public ModContribution()
        {
            sources = new List<SimulatorModSource>();
            valueType = EModValueType.FLOAT_PER; // 기본값 (퍼센트 타입)
        }

        public ModContribution(string name, double value, EModValueType type)
        {
            modName = name;
            contribution = value;
            valueType = type;
            sources = new List<SimulatorModSource>();
        }

        /// <summary>
        /// EMod를 받아서 GameDB에서 ValueType을 자동으로 가져오는 생성자
        /// </summary>
        public ModContribution(EMod mod, double value)
        {
            modName = mod.ToString();
            contribution = value;
            valueType = GetModValueType(mod);
            sources = new List<SimulatorModSource>();
        }

        /// <summary>
        /// 표시 이름과 EMod를 따로 받는 생성자 (예: "Base (mod_crit_chance)")
        /// </summary>
        public ModContribution(string displayName, EMod mod, double value)
        {
            modName = displayName;
            contribution = value;
            valueType = GetModValueType(mod);
            sources = new List<SimulatorModSource>();
        }

        /// <summary>
        /// GameDB에서 MOD의 정확한 ValueType 조회
        /// </summary>
        private static EModValueType GetModValueType(EMod mod)
        {
            var modDB = GameDBClientManager.Instance?.GameDB_Mod?.Mod;
            if (modDB != null && modDB.MapData != null && modDB.MapData.TryGetValue(mod, out var modData))
            {
                return modData.ValueType;
            }
            // GameDB 조회 실패 시 에러 로그
            Debug.LogError($"[ModContribution] GameDB에서 MOD를 찾을 수 없음: {mod}");
            return EModValueType.FLOAT_PER;
        }
    }

    /// <summary>
    /// 버프가 제공하는 개별 BuffMod 정보
    /// </summary>
    [Serializable]
    public class BuffModInfo
    {
        /// <summary>
        /// BuffMod 타입
        /// </summary>
        [PropertyOrder(1)]
        [LabelText("MOD")]
        [DisplayAsString]
        public string modName;

        /// <summary>
        /// 기본 값 (GameDB BuffMod.Value, 충전당)
        /// </summary>
        [PropertyOrder(2)]
        [LabelText("충전당 값")]
        [DisplayAsString]
        public string perChargeValue;

        /// <summary>
        /// 최종 값 (현재 충전 수 적용)
        /// </summary>
        [PropertyOrder(3)]
        [LabelText("🎯 최종 값")]
        [DisplayAsString]
        [GUIColor(0.3f, 1f, 0.3f)]
        public string finalValue;

        public BuffModInfo(string mod, double perCharge, double final)
        {
            modName = mod;
            perChargeValue = $"{perCharge:F1}";
            finalValue = $"{final:F1}";
        }
    }

    /// <summary>
    /// 버프 최종 상태 정보 클래스
    /// 기본값 + MOD 적용 후 최종값을 표시
    /// </summary>
    [Serializable]
    public class SimulatorBuffFinalState
    {
        /// <summary>
        /// 버프 이름
        /// </summary>
        [PropertyOrder(1)]
        [LabelText("버프")]
        [GUIColor(1f, 0.8f, 0.3f)]
        public string buffName;

        /// <summary>
        /// 기본 최대 스택 (GameDB)
        /// </summary>
        [PropertyOrder(2)]
        [LabelText("기본 최대 스택")]
        [HideInInspector]
        public int baseMaxStack;

        /// <summary>
        /// 기본 최대 스택 (표시용)
        /// </summary>
        [PropertyOrder(2)]
        [LabelText("기본 최대 스택")]
        [ShowInInspector]
        [DisplayAsString]
        public string BaseMaxStackDisplay => baseMaxStack.ToString();

        /// <summary>
        /// MOD에 의한 추가 스택
        /// </summary>
        [PropertyOrder(3)]
        [LabelText("MOD 추가 스택")]
        [HideInInspector]
        public double modMaxStack;

        /// <summary>
        /// MOD에 의한 추가 스택 (표시용)
        /// </summary>
        [PropertyOrder(3)]
        [LabelText("MOD 추가 스택")]
        [ShowInInspector]
        [DisplayAsString]
        [GUIColor(0.3f, 1f, 0.3f)]
        public string ModMaxStackDisplay => modMaxStack > 0 ? $"+{modMaxStack:F0}" : "0";

        /// <summary>
        /// 최종 최대 스택
        /// </summary>
        [PropertyOrder(4)]
        [LabelText("최종 최대 스택")]
        [HideInInspector]
        public int finalMaxStack;

        /// <summary>
        /// 최종 최대 스택 (표시용)
        /// </summary>
        [PropertyOrder(4)]
        [LabelText("🎯 최종 최대 스택")]
        [ShowInInspector]
        [DisplayAsString]
        [GUIColor(0.3f, 1f, 0.3f)]
        public string FinalMaxStackDisplay => finalMaxStack.ToString();

        /// <summary>
        /// 현재 충전 수 (UI 설정값)
        /// </summary>
        [PropertyOrder(5)]
        [LabelText("현재 충전")]
        [HideInInspector]
        public int currentCharge;

        /// <summary>
        /// 현재 충전 수 (표시용)
        /// </summary>
        [PropertyOrder(5)]
        [LabelText("현재 충전")]
        [ShowInInspector]
        [DisplayAsString]
        [GUIColor(1f, 0.9f, 0.5f)]
        public string CurrentChargeDisplay => $"{currentCharge} / {finalMaxStack}";

        /// <summary>
        /// 기본 지속시간 (GameDB, 초 단위)
        /// </summary>
        [PropertyOrder(6)]
        [LabelText("기본 지속시간")]
        [HideInInspector]
        public double baseDuration;

        /// <summary>
        /// 기본 지속시간 (표시용)
        /// </summary>
        [PropertyOrder(6)]
        [LabelText("기본 지속시간")]
        [ShowInInspector]
        [DisplayAsString]
        public string BaseDurationDisplay => baseDuration > 0 ? $"{baseDuration:F1}초" : "-";

        /// <summary>
        /// MOD에 의한 지속시간 증가 %
        /// </summary>
        [PropertyOrder(7)]
        [LabelText("MOD 지속시간 증가")]
        [HideInInspector]
        public double modDurationInc;

        /// <summary>
        /// MOD에 의한 지속시간 증가 % (표시용)
        /// </summary>
        [PropertyOrder(7)]
        [LabelText("MOD 지속시간 증가")]
        [ShowInInspector]
        [DisplayAsString]
        [GUIColor(0.3f, 1f, 0.3f)]
        public string ModDurationIncDisplay => modDurationInc > 0 ? $"+{modDurationInc:F1}%" : "0%";

        /// <summary>
        /// 최종 지속시간 (초 단위)
        /// </summary>
        [PropertyOrder(8)]
        [LabelText("최종 지속시간")]
        [HideInInspector]
        public double finalDuration;

        /// <summary>
        /// 최종 지속시간 (표시용)
        /// </summary>
        [PropertyOrder(8)]
        [LabelText("🎯 최종 지속시간")]
        [ShowInInspector]
        [DisplayAsString]
        [GUIColor(0.3f, 1f, 0.3f)]
        public string FinalDurationDisplay => finalDuration > 0 ? $"{finalDuration:F1}초" : "-";

        /// <summary>
        /// 버프가 제공하는 BuffMod 목록
        /// </summary>
        [PropertyOrder(9)]
        [LabelText("💎 버프가 제공하는 최종 MOD")]
        [ListDrawerSettings(ShowIndexLabels = false, DraggableItems = false, HideAddButton = true, HideRemoveButton = true)]
        [ShowInInspector]
        [ShowIf("@buffMods != null && buffMods.Count > 0")]
        public List<BuffModInfo> buffMods;

        /// <summary>
        /// 이 버프를 유발하는 CombineMod 목록
        /// </summary>
        [PropertyOrder(10)]
        [LabelText("🔗 연관 CombineMod")]
        [ListDrawerSettings(ShowIndexLabels = false, DraggableItems = false, HideAddButton = true, HideRemoveButton = true)]
        [ShowInInspector]
        [ShowIf("@relatedCombineMods != null && relatedCombineMods.Count > 0")]
        public List<SimulatorCombineModFinalState> relatedCombineMods;

        /// <summary>
        /// 최대 스택 MOD 브레이크다운
        /// </summary>
        [PropertyOrder(11)]
        [LabelText("🔍 최대 스택 MOD 상세")]
        [InlineProperty, HideLabel]
        [ShowInInspector]
        [ShowIf("@maxStackBreakdown != null && maxStackBreakdown.modContributions.Count > 0")]
        public StageBreakdown maxStackBreakdown;

        /// <summary>
        /// 지속시간 증가 MOD 브레이크다운
        /// </summary>
        [PropertyOrder(12)]
        [LabelText("🔍 지속시간 증가 MOD 상세")]
        [InlineProperty, HideLabel]
        [ShowInInspector]
        [ShowIf("@durationIncBreakdown != null && durationIncBreakdown.modContributions.Count > 0")]
        public StageBreakdown durationIncBreakdown;

        /// <summary>
        /// 버프 관련 MOD 브레이크다운 (획득 확률, 제한, 최소 스택, 효과 등 모든 관련 MOD)
        /// </summary>
        [PropertyOrder(13)]
        [LabelText("🔍 버프 기여 MOD 전체")]
        [InlineProperty, HideLabel]
        [ShowInInspector]
        [ShowIf("@contributingMods != null && contributingMods.modContributions.Count > 0")]
        public StageBreakdown contributingMods;

        public SimulatorBuffFinalState(string name, int baseMax, double modMax, int finalMax, int current,
            double baseDur, double modDurInc, double finalDur)
        {
            buffName = name;
            baseMaxStack = baseMax;
            modMaxStack = modMax;
            finalMaxStack = finalMax;
            currentCharge = current;
            baseDuration = baseDur;
            modDurationInc = modDurInc;
            finalDuration = finalDur;
            maxStackBreakdown = null;
            durationIncBreakdown = null;
            contributingMods = null;
            buffMods = new List<BuffModInfo>();
            relatedCombineMods = new List<SimulatorCombineModFinalState>();
        }
    }

    /// <summary>
    /// CombineMod 수동 입력용 클래스
    /// 장비/스킬/콘텐츠 대신 수동으로 CombineMod 값을 입력
    /// </summary>
    [Serializable]
    public class ManualCombineModEntry
    {
        [LabelText("CombineMod")]
        [LabelWidth(200)]
        public ECombineMod combineModType;

        [LabelText("값")]
        [LabelWidth(80)]
        public double value;

        [LabelText("출처 이름")]
        [LabelWidth(100)]
        public string sourceName = "수동 입력";

        public ManualCombineModEntry()
        {
            combineModType = ECombineMod.combinemod_buff_killingspree_charge_no_mod_crit_multiplier_inc;
            value = 0;
            sourceName = "수동 입력";
        }

        public ManualCombineModEntry(ECombineMod type, double val, string source)
        {
            combineModType = type;
            value = val;
            sourceName = source;
        }
    }

    /// <summary>
    /// CombineMod 최종 상태 정보 클래스
    /// 장비/스킬/콘텐츠에서 제공하는 CombineMod 값들의 합계와 출처를 표시
    /// </summary>
    [Serializable]
    public class SimulatorCombineModFinalState
    {
        /// <summary>
        /// CombineMod 이름 (한글)
        /// </summary>
        [PropertyOrder(1)]
        [LabelText("CombineMod")]
        [GUIColor(1f, 0.8f, 0.6f)]
        public string combineModName;

        /// <summary>
        /// CombineMod Enum 값
        /// </summary>
        [PropertyOrder(2)]
        [LabelText("Enum")]
        [DisplayAsString]
        [HideInInspector]
        public ECombineMod combineModType;

        /// <summary>
        /// CombineMod가 실제로 적용하는 MOD 타입 (UI 포맷팅에 사용)
        /// </summary>
        [HideInInspector]
        public EMod targetMod;

        /// <summary>
        /// 최종 합계 값
        /// </summary>
        [PropertyOrder(3)]
        [LabelText("🎯 최종 값")]
        [HideInInspector]
        public double totalValue;

        /// <summary>
        /// 최종 합계 값 (표시용) - MOD 타입에 따라 백분율 변환
        /// </summary>
        [PropertyOrder(3)]
        [LabelText("🎯 최종 값")]
        [ShowInInspector]
        [DisplayAsString]
        [GUIColor(0.3f, 1f, 0.3f)]
        public string TotalValueDisplay => FormatValue(totalValue);

        /// <summary>
        /// CombineMod 타입 (Buff / General)
        /// </summary>
        [PropertyOrder(4)]
        [LabelText("타입")]
        [DisplayAsString]
        [ShowInInspector]
        [GUIColor(0.7f, 0.7f, 1f)]
        public string combineModTypeDisplay;

        /// <summary>
        /// 출처별 브레이크다운
        /// </summary>
        [PropertyOrder(5)]
        [LabelText("🔍 출처별 상세")]
        [InlineProperty, HideLabel]
        [ShowInInspector]
        [ShowIf("@breakdown != null && breakdown.modContributions.Count > 0")]
        public StageBreakdown breakdown;

        public SimulatorCombineModFinalState(string name, ECombineMod type, double value, string typeDisplay, EMod targetModType = EMod.None)
        {
            combineModName = name;
            combineModType = type;
            totalValue = value;
            combineModTypeDisplay = typeDisplay;
            targetMod = targetModType;
            breakdown = null;
        }

        /// <summary>
        /// MOD 타입에 따라 값을 포맷팅 (FLOAT_PER이면 백분율로 변환)
        /// </summary>
        private string FormatValue(double value)
        {
            if (value == 0)
                return "0";

            if (targetMod != EMod.None)
            {
                return SimulatorCalculator.FormatModValueForUI(targetMod, value, "F1", "%");
            }

            // targetMod가 없으면 기본 포맷
            return $"{value:F1}";
        }
    }
}
