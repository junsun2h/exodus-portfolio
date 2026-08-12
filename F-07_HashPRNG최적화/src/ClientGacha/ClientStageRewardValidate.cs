using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using UnityEngine;

namespace PX
{
    public class ClientStageRewardValidate
    {
        private readonly bool _isDebugMode = false;

        public ClientStageRewardValidate()
        {
#if UNITY_EDITOR
            // 로컬 에뮬레이터 접속일 때만 검증한다. 실서버는 서버 보상 덤프 파일 자체가 없다.
            _isDebugMode = TemplateLocalData.IsEditorDebugMode;
#endif
        }

        // Stage 보상만 파일로 저장 (Stage_Enter에서 미리 계산한 보상 데이터 사용)
        // 다른 API (Gacha 등) 영향을 받지 않음
        public void WriteStageRewardToFile(EStage stage, int stageLevel, string hash, List<CommonCoreData> stageRewardCoreDatas)
        {
            if (!_isDebugMode) return;

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string folder = Path.Combine(projectRoot, "Temp", "StageReward");
                Directory.CreateDirectory(folder);

                string shortHash = hash.Substring(0, 8);
                string fileName = $"{shortHash}_client_coredata.json";
                string fullPath = Path.Combine(folder, fileName);

                Dictionary<string, string> totals = BuildStageRewardTotals(stageRewardCoreDatas);
                var json = JsonConvert.SerializeObject(
                    new
                    {
                        stage = stage.ToString(),
                        stageLevel,
                        coredatas = totals
                    },
                    Formatting.Indented);

                File.WriteAllText(fullPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WriteStageRewardToFile] Error: {e.Message}");
            }
        }

        // Stage 보상 데이터에서 합계 계산
        public Dictionary<string, string> BuildStageRewardTotals(List<CommonCoreData> stageRewardCoreDatas)
        {
            var totals = new Dictionary<string, BigInteger>();

            foreach (var coreData in stageRewardCoreDatas)
            {
                if (coreData is CoreData_EquipmentNormal equipment)
                {
                    string key = "NormalEquipment";
                    if (!totals.ContainsKey(key)) totals[key] = BigInteger.Zero;
                    totals[key] += equipment.Count.Value;
                }
                else if (coreData is CoreData_Skill skill)
                {
                    string key = skill.SkillType switch
                    {
                        ESkillType.skill_spell => "SkillSpell",
                        ESkillType.skill_aura => "SkillAura",
                        ESkillType.skill_rune => "SkillRune",
                        _ => null
                    };
                    if (key != null)
                    {
                        if (!totals.ContainsKey(key)) totals[key] = BigInteger.Zero;
                        totals[key] += skill.Count.Value;
                    }
                }
                else if (coreData is CoreData_Pet pet)
                {
                    string key = "Pet";
                    if (!totals.ContainsKey(key)) totals[key] = BigInteger.Zero;
                    totals[key] += pet.Count.Value;
                }
                else if (coreData is CoreData_BigIntCurrency bigIntCurrency)
                {
                    string key = $"Currency_{(int)bigIntCurrency.CurrencyType}";
                    if (!totals.ContainsKey(key)) totals[key] = BigInteger.Zero;
                    totals[key] += bigIntCurrency.Count.Value;
                }
                else if (coreData is CoreData_Currency currency)
                {
                    string key = $"Currency_{(int)currency.CurrencyType}";
                    if (!totals.ContainsKey(key)) totals[key] = BigInteger.Zero;
                    totals[key] += currency.Count.Value;
                }
            }

            return totals.ToDictionary(k => k.Key, v => v.Value.ToString());
        }

        // [Legacy] 현재 CoreData의 합계를 기록 - 더 이상 사용하지 않음
        public void WriteCoreDataToFile(EStage stage, int stageLevel, string hash)
        {
            try
            {
                // <프로젝트 루트>\Temp\StageReward
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string folder = Path.Combine(projectRoot, "Temp", "StageReward");
                Directory.CreateDirectory(folder);

                string shortHash = hash.Substring(0, 8);
                string fileName = $"{shortHash}_client_coredata.json";
                string fullPath = Path.Combine(folder, fileName);

                Dictionary<string, string> totals = BuildCoreDataTotals();
                var json = JsonConvert.SerializeObject(
                    new
                    {
                        stage = stage.ToString(),
                        stageLevel,
                        coredatas = totals                // { key : "value" }
                    },
                    Formatting.Indented);

                File.WriteAllText(fullPath, json);
                //Debug.Log($"[WriteCoreDataToFile] client saved → {fullPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WriteCoreDataToFile] Error: {e.Message}");
            }
        }

        public Dictionary<string, string> BuildCoreDataTotals()
        {
            var totals = new Dictionary<string, BigInteger>();

            // ───────────────────── Equipment ─────────────────────
            var equipCore = GameAPIUserManager.Instance.userData.equipmentData.CoreData;
            BigInteger eqSum = equipCore.NormalEquipments.Values.Aggregate(BigInteger.Zero, (s, e) => s + e.Count.Value);
            totals["NormalEquipment"] = eqSum;

            // ───────────────────── Skills ────────────────────────
            var skillCore = GameAPIUserManager.Instance.userData.skillData.CoreData;

            BigInteger spellSum = skillCore.Spells.Values.Sum(
                t => t.Tiers.Values.Sum(v => v.Count.Value));
            totals["SkillSpell"] = spellSum;

            BigInteger auraSum = skillCore.Auras.Values.Sum(
                t => t.Tiers.Values.Sum(v => v.Count.Value));
            totals["SkillAura"] = auraSum;

            BigInteger runeSum = skillCore.Runes.Values.Sum(
                t => t.Tiers.Values.Sum(v => v.Count.Value));
            totals["SkillRune"] = runeSum;

            // ───────────────────── Pet ───────────────────────────
            var petCore = GameAPIUserManager.Instance.userData.petData.CoreData;
            BigInteger petSum = petCore.Pets.Values.Sum(
                t => t.Tiers.Values.Sum(v => v.Count.Value));
            totals["Pet"] = petSum;

            /* ───────────────────────── 4. 재화 합계 ───────────────────────── */
            var curCore = GameAPIUserManager.Instance.userData.currencyData.CoreData;

            // 제외할 재화
            var excluded = new HashSet<ECurrency>
            {
                ECurrency.currency_xp,
                ECurrency.currency_level_point,
                ECurrency.currency_equipment_weapon,
                ECurrency.currency_equipment_armour,
                ECurrency.currency_equipment_accessory,
                ECurrency.currency_skill_spell,
                ECurrency.currency_skill_aura,
                ECurrency.currency_skill_curse,
                ECurrency.currency_skill_rune,
                ECurrency.currency_pet,
            };

            // BigInt 재화
            foreach (var (key, val) in curCore.BigIntCurrencies)
            {
                if (!excluded.Contains(key))
                    totals[$"Currency_{(int)key}"] = val.Count.Value;
            }

            // 일반(Int) 재화
            foreach (var (key, val) in curCore.Currencies)
            {
                if (!excluded.Contains(key))
                    totals[$"Currency_{(int)key}"] = val.Count.Value;
            }

            // ───────────────────── 문자열 변환 후 반환 ──────────────
            return totals.ToDictionary(k => k.Key, v => v.Value.ToString());
        }

        /* 덤프 구조용 record */
        private sealed class RewardDump
        {
            public string stage { get; set; }
            public int stageLevel { get; set; }
            public Dictionary<string, string> coredatas { get; set; }
        }

        public void CompareCoreDatas(string hash)
        {
            if (!_isDebugMode) return;

            try
            {
                // <프로젝트 루트>\Temp\StageReward
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string dir = Path.Combine(projectRoot, "Temp", "StageReward");

                string shortHash = hash.Substring(0, 8);
                string clientFile = Path.Combine(dir, $"{shortHash}_client_coredata.json");
                string serverFile = Path.Combine(dir, $"{shortHash}_server_coredata.json");

                if (!File.Exists(clientFile))
                {
                    Debug.LogError($"[CompareCoreDatas] client file missing: {clientFile}");
                    return;
                }

                if (!File.Exists(serverFile))
                {
                    Debug.LogError($"[CompareCoreDatas] server file missing: {serverFile}");
                    return;
                }

                // ───────── 클라이언트 파일 로드 ─────────
                var cliDump = JsonConvert.DeserializeObject<RewardDump>(File.ReadAllText(clientFile));
                var cliMap = cliDump.coredatas;

                // ───────── 서버 파일 로드 ─────────
                var srvDump = JsonConvert.DeserializeObject<RewardDump>(File.ReadAllText(serverFile));
                var srvMap = srvDump.coredatas;

                // ───────── 비교 ─────────
                var diffs = new List<string>();
                var keys = new HashSet<string>(cliMap.Keys);
                keys.UnionWith(srvMap.Keys);

                foreach (string k in keys)
                {
                    cliMap.TryGetValue(k, out string cliVal);
                    srvMap.TryGetValue(k, out string srvVal);
                    cliVal ??= "∅";
                    srvVal ??= "∅";

                    if (cliVal != srvVal)
                        diffs.Add($"{k,-16}  client={cliVal}  /  server={srvVal}");
                }

                if (diffs.Count > 0)
                {
                    Debug.LogError("[CompareCoreDatas] ❌ Mismatch ↓\n" + string.Join("\n", diffs));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CompareCoreDatas] compare failed – {ex}");
            }
        }
    }
}