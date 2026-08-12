using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BattleSimulator
{
    /// <summary>
    /// 스냅샷 회귀 테스트
    /// 고정 빌드 세트로 시뮬레이션 실행 후 핵심 지표를 JSON으로 저장.
    /// 다음 실행 시 이전 스냅샷과 diff하여 변동 감지.
    /// PlayMode 필요 (Unity 에디터 + 게임 로그인 상태)
    /// </summary>
    public static class SnapshotRegression
    {
        private static readonly string SnapshotDir = Path.Combine(
            Application.dataPath, "Editor", "BattleSimulator", "Reports", "Snapshots");

        /// <summary>
        /// 스냅샷 데이터 구조
        /// </summary>
        [Serializable]
        public class SnapshotData
        {
            public string timestamp;
            public List<BuildSnapshot> builds = new List<BuildSnapshot>();
        }

        [Serializable]
        public class BuildSnapshot
        {
            public string buildName;
            public double totalDPS;
            public double physicalDPS;
            public double elementalDPS;
            public double ailmentDPS;
            public double playerHP;
            public double physicalResistance;
            public double elementalResistance;
            public double critChance;
            public double critMultiplier;
            public double attackSpeed;
        }

        /// <summary>
        /// 현재 상태의 스냅샷 캡처 (PlayMode에서만 동작)
        /// </summary>
        public static SnapshotData CaptureSnapshot(string buildName, SimulatorCalculatorResult result)
        {
            var snapshot = new SnapshotData
            {
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };

            var build = new BuildSnapshot
            {
                buildName = buildName,
                totalDPS = result?.TotalDPS ?? 0,
                physicalDPS = result?.PhysicalDPS ?? 0,
                elementalDPS = result?.ElementalDPS ?? 0,
                ailmentDPS = result?.AilmentDPS ?? 0,
                playerHP = result?.PlayerHP ?? 0,
                physicalResistance = result?.PhysicalResistance ?? 0,
                elementalResistance = result?.ElementalResistance ?? 0,
                critChance = result?.CritChance ?? 0,
                critMultiplier = result?.CritMultiplier ?? 0,
                attackSpeed = result?.AttackSpeed ?? 0,
            };

            snapshot.builds.Add(build);
            return snapshot;
        }

        /// <summary>
        /// 스냅샷을 JSON으로 저장
        /// </summary>
        public static string SaveSnapshot(SnapshotData snapshot)
        {
            if (!Directory.Exists(SnapshotDir))
                Directory.CreateDirectory(SnapshotDir);

            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string filePath = Path.Combine(SnapshotDir, $"baseline-{date}.json");
            string json = JsonUtility.ToJson(snapshot, true);
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
            Debug.Log($"[SnapshotRegression] 스냅샷 저장: {filePath}");
            return filePath;
        }

        /// <summary>
        /// 가장 최근 스냅샷 로드
        /// </summary>
        public static SnapshotData LoadLatestSnapshot()
        {
            if (!Directory.Exists(SnapshotDir))
                return null;

            string[] files = Directory.GetFiles(SnapshotDir, "baseline-*.json");
            if (files.Length == 0)
                return null;

            Array.Sort(files);
            string latest = files[files.Length - 1];
            string json = File.ReadAllText(latest);
            return JsonUtility.FromJson<SnapshotData>(json);
        }

        /// <summary>
        /// 두 스냅샷 비교 — 변동 사항 리포트
        /// </summary>
        public static List<string> CompareSnapshots(SnapshotData baseline, SnapshotData current, double tolerancePercent = 0.1)
        {
            var diffs = new List<string>();
            if (baseline == null || current == null)
            {
                diffs.Add("비교할 스냅샷이 없습니다.");
                return diffs;
            }

            for (int i = 0; i < current.builds.Count; i++)
            {
                var curr = current.builds[i];
                BuildSnapshot prev = null;

                // 이름으로 매칭
                for (int j = 0; j < baseline.builds.Count; j++)
                {
                    if (baseline.builds[j].buildName == curr.buildName)
                    {
                        prev = baseline.builds[j];
                        break;
                    }
                }

                if (prev == null)
                {
                    diffs.Add($"[{curr.buildName}] 이전 스냅샷에 없는 빌드");
                    continue;
                }

                CompareField(diffs, curr.buildName, "TotalDPS", prev.totalDPS, curr.totalDPS, tolerancePercent);
                CompareField(diffs, curr.buildName, "PhysicalDPS", prev.physicalDPS, curr.physicalDPS, tolerancePercent);
                CompareField(diffs, curr.buildName, "ElementalDPS", prev.elementalDPS, curr.elementalDPS, tolerancePercent);
                CompareField(diffs, curr.buildName, "AilmentDPS", prev.ailmentDPS, curr.ailmentDPS, tolerancePercent);
                CompareField(diffs, curr.buildName, "PlayerHP", prev.playerHP, curr.playerHP, tolerancePercent);
                CompareField(diffs, curr.buildName, "PhysResist", prev.physicalResistance, curr.physicalResistance, tolerancePercent);
                CompareField(diffs, curr.buildName, "EleResist", prev.elementalResistance, curr.elementalResistance, tolerancePercent);
                CompareField(diffs, curr.buildName, "CritChance", prev.critChance, curr.critChance, tolerancePercent);
                CompareField(diffs, curr.buildName, "CritMult", prev.critMultiplier, curr.critMultiplier, tolerancePercent);
                CompareField(diffs, curr.buildName, "AtkSpeed", prev.attackSpeed, curr.attackSpeed, tolerancePercent);
            }

            return diffs;
        }

        private static void CompareField(List<string> diffs, string buildName, string field,
            double prev, double curr, double tolerancePercent)
        {
            if (Math.Abs(prev) < 1e-10 && Math.Abs(curr) < 1e-10)
                return; // 둘 다 0이면 변동 없음

            double delta;
            if (Math.Abs(prev) < 1e-10)
                delta = 100.0; // 0에서 변화 → 100% 변동
            else
                delta = Math.Abs((curr - prev) / prev) * 100.0;

            if (delta > tolerancePercent)
            {
                diffs.Add($"[{buildName}] {field}: {prev:F4} → {curr:F4} (변동 {delta:F2}%)");
            }
        }
    }

    /// <summary>
    /// 시뮬레이션 결과 데이터 (스냅샷용)
    /// </summary>
    public class SimulatorCalculatorResult
    {
        public double TotalDPS { get; set; }
        public double PhysicalDPS { get; set; }
        public double ElementalDPS { get; set; }
        public double AilmentDPS { get; set; }
        public double PlayerHP { get; set; }
        public double PhysicalResistance { get; set; }
        public double ElementalResistance { get; set; }
        public double CritChance { get; set; }
        public double CritMultiplier { get; set; }
        public double AttackSpeed { get; set; }
    }
}
