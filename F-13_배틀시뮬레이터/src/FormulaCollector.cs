using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// 공식 수집 결과 데이터
    /// </summary>
    public class FormulaCollectionResult
    {
        /// <summary>소스 클래스명 (AwakenElement, PlayerGrowth 등)</summary>
        public string SourceClass { get; set; }

        /// <summary>프로퍼티명 (Cost, Stat, Value 등)</summary>
        public string PropertyName { get; set; }

        /// <summary>데이터 ID (element_fire, mod_all_damage 등)</summary>
        public string ItemId { get; set; }

        /// <summary>실제 공식 데이터</summary>
        public FormulaData Formula { get; set; }

        /// <summary>공식 타입</summary>
        public EFormulaType FormulaType => Formula?.FormulaType ?? EFormulaType.None;

        /// <summary>레벨 1에서의 값</summary>
        public double ValueAtLevel1 { get; set; }

        /// <summary>레벨 100에서의 값</summary>
        public double ValueAtLevel100 { get; set; }

        /// <summary>레벨 500에서의 값</summary>
        public double ValueAtLevel500 { get; set; }

        /// <summary>레벨 1000에서의 값</summary>
        public double ValueAtLevel1000 { get; set; }

        /// <summary>추가 정보 (Currency 등)</summary>
        public string ExtraInfo { get; set; }

        /// <summary>
        /// 샘플 레벨에서의 값 계산
        /// </summary>
        public void CalculateSampleValues()
        {
            if (Formula == null) return;

            // 각 레벨별로 개별 계산 (오버플로우 방지)
            ValueAtLevel1 = SafeGetFormulaValue(1);
            ValueAtLevel100 = SafeGetFormulaValue(100);
            ValueAtLevel500 = SafeGetFormulaValue(500);
            ValueAtLevel1000 = SafeGetFormulaValue(1000);
        }

        /// <summary>
        /// 오버플로우 없이 공식 값을 안전하게 계산
        /// </summary>
        private double SafeGetFormulaValue(int level)
        {
            if (Formula == null) return 0;

            try
            {
                // FormulaData의 내부 계산 로직을 직접 사용
                // GetValue()는 ModValue로 변환 시 오버플로우 발생 가능
                return CalculateRawFormulaValue(level);
            }
            catch
            {
                // 오버플로우 발생 시 double.MaxValue 반환
                return double.MaxValue;
            }
        }

        /// <summary>
        /// 공식의 원시 값 계산 (ModValue 변환 없이)
        /// FormulaData.GetValue()와 동일한 로직 사용
        /// </summary>
        private double CalculateRawFormulaValue(int level)
        {
            var ft = Formula.FormulaType;
            double start = Formula.Start;
            double multiplier = Formula.Multiplier;
            double expBase = Formula.ExpBase;

            switch (ft)
            {
                case EFormulaType.CONST:
                    return start;

                case EFormulaType.LINEAR:
                    // Start + (Multiplier * level)
                    return start + (multiplier * level);

                case EFormulaType.EXP1:
                    // Start + (Multiplier * level * ExpBase^level)
                    return start + (multiplier * level * Math.Pow(expBase, level));

                case EFormulaType.EXP2:
                    // Start + (Multiplier * level) + ExpBase^level
                    return start + (multiplier * level) + Math.Pow(expBase, level);

                case EFormulaType.EXP3:
                    // Start + (Multiplier * ExpBase^level)
                    return start + (multiplier * Math.Pow(expBase, level));

                case EFormulaType.POLY:
                    // Start + (Multiplier * (level-1)^ExpBase)
                    {
                        int adjustedLevel = Math.Max(0, level - 1);
                        return start + (multiplier * Math.Pow(adjustedLevel, expBase));
                    }

                default:
                    return 0;
            }
        }

        /// <summary>
        /// EXP 타입 여부 확인
        /// </summary>
        public bool IsExpType =>
            FormulaType == EFormulaType.EXP1 ||
            FormulaType == EFormulaType.EXP2 ||
            FormulaType == EFormulaType.EXP3;

        /// <summary>
        /// 표시용 문자열
        /// </summary>
        public override string ToString()
        {
            return $"{SourceClass}.{ItemId}.{PropertyName} ({FormulaType})";
        }
    }

    /// <summary>
    /// 공식 통계 데이터
    /// </summary>
    public class FormulaStatistics
    {
        public int TotalCount { get; set; }
        public Dictionary<EFormulaType, int> CountByType { get; set; } = new Dictionary<EFormulaType, int>();
        public Dictionary<string, int> CountBySource { get; set; } = new Dictionary<string, int>();
        public int ExpCount => CountByType.Where(kvp =>
            kvp.Key == EFormulaType.EXP1 ||
            kvp.Key == EFormulaType.EXP2 ||
            kvp.Key == EFormulaType.EXP3).Sum(kvp => kvp.Value);
    }

    /// <summary>
    /// GameDB에서 FormulaData를 수집하는 유틸리티 클래스
    /// Play 모드에서만 사용 가능
    /// </summary>
    public static class FormulaCollector
    {
        // 캐시된 수집 결과
        private static List<FormulaCollectionResult> _cachedResults;
        private static FormulaStatistics _cachedStatistics;
        private static bool _isCacheValid = false;

        /// <summary>
        /// 캐시 무효화
        /// </summary>
        public static void InvalidateCache()
        {
            _cachedResults = null;
            _cachedStatistics = null;
            _isCacheValid = false;
        }

        /// <summary>
        /// Play 모드 확인
        /// </summary>
        public static bool IsPlayMode => Application.isPlaying;

        /// <summary>
        /// GameDBClientManager 사용 가능 여부
        /// </summary>
        public static bool IsGameDBAvailable
        {
            get
            {
                if (!IsPlayMode) return false;
                try
                {
                    return GameDBClientManager.Instance != null &&
                           GameDBClientManager.Instance.IsAllComplete;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 모든 GameDB에서 FormulaData 수집
        /// </summary>
        /// <param name="forceRefresh">캐시 무시하고 새로 수집</param>
        /// <returns>수집된 공식 목록</returns>
        public static List<FormulaCollectionResult> CollectAllFormulas(bool forceRefresh = false)
        {
            if (!IsGameDBAvailable)
            {
                Debug.LogError("[FormulaCollector] Play 모드에서 GameDB가 로드된 후 사용해주세요.");
                return new List<FormulaCollectionResult>();
            }

            if (!forceRefresh && _isCacheValid && _cachedResults != null)
            {
                return _cachedResults;
            }

            var results = new List<FormulaCollectionResult>();

            // 각 소스에서 수집
            results.AddRange(CollectFromAwakenElement());
            results.AddRange(CollectFromNebula());
            results.AddRange(CollectFromPlayerGrowth());
            results.AddRange(CollectFromPlayerReinforce());
            results.AddRange(CollectFromStageReward());
            results.AddRange(CollectFromFormula());

            // 샘플 값 계산
            foreach (var result in results)
            {
                result.CalculateSampleValues();
            }

            // 캐시 갱신
            _cachedResults = results;
            _isCacheValid = true;

            Debug.Log($"[FormulaCollector] 총 {results.Count}개 공식 수집 완료");

            return results;
        }

        /// <summary>
        /// 통계 계산
        /// </summary>
        public static FormulaStatistics GetStatistics(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedStatistics != null && _isCacheValid)
            {
                return _cachedStatistics;
            }

            var results = CollectAllFormulas(forceRefresh);
            var stats = new FormulaStatistics
            {
                TotalCount = results.Count
            };

            // 타입별 카운트
            foreach (var result in results)
            {
                if (!stats.CountByType.ContainsKey(result.FormulaType))
                    stats.CountByType[result.FormulaType] = 0;
                stats.CountByType[result.FormulaType]++;

                if (!stats.CountBySource.ContainsKey(result.SourceClass))
                    stats.CountBySource[result.SourceClass] = 0;
                stats.CountBySource[result.SourceClass]++;
            }

            _cachedStatistics = stats;
            return stats;
        }

        /// <summary>
        /// 특정 소스에서만 수집
        /// </summary>
        public static List<FormulaCollectionResult> CollectBySource(string sourceClass)
        {
            var all = CollectAllFormulas();
            return all.Where(r => r.SourceClass == sourceClass).ToList();
        }

        /// <summary>
        /// 특정 타입으로 필터링
        /// </summary>
        public static List<FormulaCollectionResult> FilterByType(EFormulaType type)
        {
            var all = CollectAllFormulas();
            return all.Where(r => r.FormulaType == type).ToList();
        }

        /// <summary>
        /// EXP 타입만 필터링
        /// </summary>
        public static List<FormulaCollectionResult> GetExpFormulas()
        {
            var all = CollectAllFormulas();
            return all.Where(r => r.IsExpType).ToList();
        }

        #region 개별 소스 수집

        /// <summary>
        /// AwakenElement에서 수집
        /// </summary>
        private static List<FormulaCollectionResult> CollectFromAwakenElement()
        {
            var results = new List<FormulaCollectionResult>();

            try
            {
                var mapData = GameDBClientManager.Instance.GameDB_Awaken?.AwakenElement?.MapData;
                if (mapData == null) return results;

                foreach (var kvp in mapData)
                {
                    var key = kvp.Key.ToString();
                    var data = kvp.Value;

                    // Cost
                    if (data.Cost != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "AwakenElement",
                            PropertyName = "Cost",
                            ItemId = key,
                            Formula = data.Cost,
                            ExtraInfo = data.CostCurrency.ToString()
                        });
                    }

                    // Stat
                    if (data.Stat != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "AwakenElement",
                            PropertyName = "Stat",
                            ItemId = key,
                            Formula = data.Stat,
                            ExtraInfo = data.Mod.ToString()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FormulaCollector] AwakenElement 수집 실패: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Nebula에서 수집
        /// </summary>
        private static List<FormulaCollectionResult> CollectFromNebula()
        {
            var results = new List<FormulaCollectionResult>();

            try
            {
                var mapData = GameDBClientManager.Instance.GameDB_Nebula?.Nebula?.MapData;
                if (mapData == null) return results;

                foreach (var kvp in mapData)
                {
                    var key = kvp.Key.ToString();
                    var data = kvp.Value;

                    // Cost
                    if (data.Cost != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "Nebula",
                            PropertyName = "Cost",
                            ItemId = key,
                            Formula = data.Cost,
                            ExtraInfo = data.CostCurrency.ToString()
                        });
                    }

                    // Stat
                    if (data.Stat != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "Nebula",
                            PropertyName = "Stat",
                            ItemId = key,
                            Formula = data.Stat,
                            ExtraInfo = data.Mod.ToString()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FormulaCollector] Nebula 수집 실패: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// PlayerGrowth에서 수집
        /// </summary>
        private static List<FormulaCollectionResult> CollectFromPlayerGrowth()
        {
            var results = new List<FormulaCollectionResult>();

            try
            {
                var mapData = GameDBClientManager.Instance.GameDB_Player?.Growth?.MapData;
                if (mapData == null) return results;

                foreach (var kvp in mapData)
                {
                    var key = kvp.Key.ToString();
                    var data = kvp.Value;

                    // Cost
                    if (data.Cost != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "PlayerGrowth",
                            PropertyName = "Cost",
                            ItemId = key,
                            Formula = data.Cost,
                            ExtraInfo = data.CostCurrency.ToString()
                        });
                    }

                    // Stat
                    if (data.Stat != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "PlayerGrowth",
                            PropertyName = "Stat",
                            ItemId = key,
                            Formula = data.Stat
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FormulaCollector] PlayerGrowth 수집 실패: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// PlayerReinforce에서 수집
        /// </summary>
        private static List<FormulaCollectionResult> CollectFromPlayerReinforce()
        {
            var results = new List<FormulaCollectionResult>();

            try
            {
                var mapData = GameDBClientManager.Instance.GameDB_Player?.Reinforce?.MapData;
                if (mapData == null) return results;

                foreach (var kvp in mapData)
                {
                    var key = kvp.Key.ToString();
                    var data = kvp.Value;

                    // Cost
                    if (data.Cost != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "PlayerReinforce",
                            PropertyName = "Cost",
                            ItemId = key,
                            Formula = data.Cost,
                            ExtraInfo = data.CostCurrency.ToString()
                        });
                    }

                    // Stat
                    if (data.Stat != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "PlayerReinforce",
                            PropertyName = "Stat",
                            ItemId = key,
                            Formula = data.Stat
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FormulaCollector] PlayerReinforce 수집 실패: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// StageReward에서 수집 (Main, MainEndless, Rift, GreatRift, BossRush)
        /// </summary>
        private static List<FormulaCollectionResult> CollectFromStageReward()
        {
            var results = new List<FormulaCollectionResult>();

            try
            {
                var stageDB = GameDBClientManager.Instance.GameDB_Stage;
                if (stageDB == null) return results;

                // StageReward_Main
                if (stageDB.StageReward_Main?.MapData != null)
                {
                    foreach (var kvp in stageDB.StageReward_Main.MapData)
                    {
                        var stageKey = kvp.Key.ToString();
                        var stageData = kvp.Value;
                        if (stageData.StageRewardDatas != null)
                        {
                            results.AddRange(CollectFromStageRewardDataList(
                                stageData.StageRewardDatas, "StageReward_Main", stageKey));
                        }
                    }
                }

                // StageReward_MainEndless
                if (stageDB.StageReward_MainEndless?.MapData != null)
                {
                    foreach (var kvp in stageDB.StageReward_MainEndless.MapData)
                    {
                        var stageKey = kvp.Key.ToString();
                        var stageData = kvp.Value;
                        if (stageData.StageRewardDatas != null)
                        {
                            results.AddRange(CollectFromStageRewardDataList(
                                stageData.StageRewardDatas, "StageReward_MainEndless", stageKey));
                        }
                    }
                }

                // StageReward_Rift
                if (stageDB.StageReward_Rift?.MapData != null)
                {
                    foreach (var kvp in stageDB.StageReward_Rift.MapData)
                    {
                        var stageKey = kvp.Key.ToString();
                        var stageData = kvp.Value;
                        if (stageData.StageRewardDatas != null)
                        {
                            results.AddRange(CollectFromStageRewardDataList(
                                stageData.StageRewardDatas, "StageReward_Rift", stageKey));
                        }
                    }
                }

                // StageReward_GreatRift
                if (stageDB.StageReward_GreatRift?.MapData != null)
                {
                    foreach (var kvp in stageDB.StageReward_GreatRift.MapData)
                    {
                        var stageKey = kvp.Key.ToString();
                        var stageData = kvp.Value;
                        if (stageData.StageRewardDatas != null)
                        {
                            results.AddRange(CollectFromStageRewardDataList(
                                stageData.StageRewardDatas, "StageReward_GreatRift", stageKey));
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Debug.LogError($"[FormulaCollector] StageReward 수집 실패: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// StageRewardData 리스트에서 수집
        /// </summary>
        private static List<FormulaCollectionResult> CollectFromStageRewardDataList(
            List<GameDB_Client_StageRewardData> dataList, string sourceClass, string stageKey)
        {
            var results = new List<FormulaCollectionResult>();

            for (int i = 0; i < dataList.Count; i++)
            {
                var data = dataList[i];
                var itemId = $"{stageKey}_{data.Currency}_{i}";

                // Count
                if (data.Count != null)
                {
                    results.Add(new FormulaCollectionResult
                    {
                        SourceClass = sourceClass,
                        PropertyName = "Count",
                        ItemId = itemId,
                        Formula = data.Count,
                        ExtraInfo = $"{data.Currency}, {data.Payment}"
                    });
                }

                // Chance
                if (data.Chance != null)
                {
                    results.Add(new FormulaCollectionResult
                    {
                        SourceClass = sourceClass,
                        PropertyName = "Chance",
                        ItemId = itemId,
                        Formula = data.Chance,
                        ExtraInfo = $"{data.Currency}, {data.Payment}"
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Formula (GameDB_Client_Formula)에서 수집
        /// </summary>
        private static List<FormulaCollectionResult> CollectFromFormula()
        {
            var results = new List<FormulaCollectionResult>();

            try
            {
                var mapData = GameDBClientManager.Instance.GameDB_Etc?.Formula?.MapData;
                if (mapData == null) return results;

                foreach (var kvp in mapData)
                {
                    var key = kvp.Key.ToString();
                    var data = kvp.Value;

                    if (data.Value != null)
                    {
                        results.Add(new FormulaCollectionResult
                        {
                            SourceClass = "Formula",
                            PropertyName = "Value",
                            ItemId = key,
                            Formula = data.Value,
                            ExtraInfo = $"{data.Tier}, {data.Grade}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FormulaCollector] Formula 수집 실패: {ex.Message}");
            }

            return results;
        }

        #endregion
    }
}
