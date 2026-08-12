using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Unity.EditorCoroutines.Editor;
using PX;

namespace BattleSimulator
{
    /// <summary>
    /// BattleSimulatorWindow의 액션 버튼 메서드들
    /// </summary>
    public partial class BattleSimulatorWindow
    {
        #region 액션 버튼
        // 빌드 탭 버튼들 - 맨 위로 배치
        [TabGroup("Tabs", "👤 플레이어", Order = 1)]
        [PropertyOrder(-100)]
        [PropertySpace(10)]
        [Button(ButtonSizes.Gigantic, Name = "시뮬레이션 시작")]
        [GUIColor(0.3f, 1f, 0.3f)]
        public void StartSimulation()
        {
            EditorCoroutineUtility.StartCoroutine(SimulationRoutine(), this);
        }

        [TabGroup("Tabs", "👤 플레이어", Order = 1)]
        [PropertyOrder(-99)]
        [HorizontalGroup("Tabs/👤 플레이어/Actions")]
        [Button(ButtonSizes.Large, Name = "💾 프리셋 저장")]
        public void SavePreset()
        {
            // 1. 현재 설정을 SimulatorPresetData로 변환
            SimulatorPresetData data = new SimulatorPresetData
            {
                presetName = "My Preset", // 기본값

                // 일반 장비 (~전설)
                weapon = this.weapon,
                helmet = this.helmet,
                bodyArmor = this.bodyArmor,
                gloves = this.gloves,
                boots = this.boots,
                amulet = this.amulet,
                belt = this.belt,
                ring1 = this.ring1,

                // 신화 장비
                weaponMythic = this.weaponMythic,
                helmetMythic = this.helmetMythic,
                bodyArmorMythic = this.bodyArmorMythic,
                glovesMythic = this.glovesMythic,
                bootsMythic = this.bootsMythic,
                amuletMythic = this.amuletMythic,
                beltMythic = this.beltMythic,
                ring1Mythic = this.ring1Mythic,

                // 스킬 및 티어
                mainSpell = this.mainSpell,
                spellTier = this.spellTier,
                spellGrade = this.spellGrade,
                runeSockets = ConvertRuneSocketsToDict(),
                aura = this.aura,
                auraTier = this.auraTier,
                pet = this.pet,
                petTier = this.petTier,

                // 성좌 3슬롯
                constellationSlot1 = this.constellationSlot1,
                constellationSlot2 = this.constellationSlot2,
                constellationSlot3 = this.constellationSlot3,

                // 최적화 설정
                buildType = this.buildType,

                // 조건부 MOD: 적 상태이상 상태
                targetHasBleeding = defender.targetHasBleeding,
                targetHasIgnite = defender.targetHasIgnite,
                targetHasArctic = defender.targetHasArctic,
                targetHasChill = defender.targetHasChill,
                targetHasShock = defender.targetHasShock,
                targetHasParalyze = defender.targetHasParalyze,
                targetHasPoisoning = defender.targetHasPoisoning,
                targetHasStun = defender.targetHasStun
            };

            // 2. 저장 경로 선택 다이얼로그
            string defaultPath = "Assets/Editor/BattleSimulator/Data/Presets";

            // 디렉토리가 없으면 생성
            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }

            string path = EditorUtility.SaveFilePanel(
                "프리셋 저장",
                defaultPath,
                "preset_default",
                "json"
            );

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // 3. JSON 직렬화
            string json = JsonUtility.ToJson(data, prettyPrint: true);

            // 5. 파일 저장
            try
            {
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                EditorUtility.DisplayDialog("저장 완료", $"프리셋이 저장되었습니다.\n{path}", "확인");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Simulator] ❌ 프리셋 저장 실패: {e.Message}");
                EditorUtility.DisplayDialog("저장 실패", $"프리셋 저장 중 오류가 발생했습니다.\n{e.Message}", "확인");
            }
        }

        /// <summary>
        /// 지정된 경로에 현재 설정을 프리셋으로 저장 (다이얼로그 없이)
        /// </summary>
        /// <param name="path">저장할 파일 경로 (전체 경로)</param>
        /// <param name="silent">true: 로그 표시 안함</param>
        /// <returns>저장 성공 여부</returns>
        public bool SavePresetToPath(string path, bool silent = false)
        {
            // 1. 현재 설정을 SimulatorPresetData로 변환
            SimulatorPresetData data = new SimulatorPresetData
            {
                presetName = Path.GetFileNameWithoutExtension(path),

                // 일반 장비 (~전설)
                weapon = this.weapon,
                helmet = this.helmet,
                bodyArmor = this.bodyArmor,
                gloves = this.gloves,
                boots = this.boots,
                amulet = this.amulet,
                belt = this.belt,
                ring1 = this.ring1,

                // 신화 장비
                weaponMythic = this.weaponMythic,
                helmetMythic = this.helmetMythic,
                bodyArmorMythic = this.bodyArmorMythic,
                glovesMythic = this.glovesMythic,
                bootsMythic = this.bootsMythic,
                amuletMythic = this.amuletMythic,
                beltMythic = this.beltMythic,
                ring1Mythic = this.ring1Mythic,

                // 스킬 및 티어
                mainSpell = this.mainSpell,
                spellTier = this.spellTier,
                spellGrade = this.spellGrade,
                runeSockets = ConvertRuneSocketsToDict(),
                aura = this.aura,
                auraTier = this.auraTier,
                pet = this.pet,
                petTier = this.petTier,

                // 성좌 3슬롯
                constellationSlot1 = this.constellationSlot1,
                constellationSlot2 = this.constellationSlot2,
                constellationSlot3 = this.constellationSlot3,

                // 최적화 설정
                buildType = this.buildType,

                // 조건부 MOD: 적 상태이상 상태
                targetHasBleeding = defender.targetHasBleeding,
                targetHasIgnite = defender.targetHasIgnite,
                targetHasArctic = defender.targetHasArctic,
                targetHasChill = defender.targetHasChill,
                targetHasShock = defender.targetHasShock,
                targetHasParalyze = defender.targetHasParalyze,
                targetHasPoisoning = defender.targetHasPoisoning,
                targetHasStun = defender.targetHasStun
            };

            // 2. JSON 직렬화
            string json = JsonUtility.ToJson(data, prettyPrint: true);

            // 4. 파일 저장
            try
            {
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Simulator] ❌ 프리셋 저장 실패: {e.Message}");
                return false;
            }
        }

        [TabGroup("Tabs", "👤 플레이어", Order = 1)]
        [PropertyOrder(-99)]
        [HorizontalGroup("Tabs/👤 플레이어/Actions")]
        [Button(ButtonSizes.Large, Name = "📂 프리셋 로드")]
        public void LoadPreset()
        {
            // 1. JSON 파일 선택 다이얼로그
            string defaultPath = "Assets/Editor/BattleSimulator/Data/Presets";

            string path = EditorUtility.OpenFilePanel(
                "프리셋 불러오기",
                defaultPath,
                "json"
            );

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // 2. 선택된 파일 경로로 로드
            LoadPresetFromPath(path, silent: false);
        }

        /// <summary>
        /// 지정된 파일 경로에서 프리셋 로드 (자동 로드용 헬퍼 메서드)
        /// </summary>
        /// <param name="path">프리셋 JSON 파일 경로</param>
        /// <param name="silent">true: 로그/다이얼로그 표시 안함, false: 정상적으로 표시</param>
        private void LoadPresetFromPath(string path, bool silent = false)
        {
            try
            {
                // 1. JSON 파일 읽기
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);

                // 2. JSON 역직렬화
                SimulatorPresetData data = JsonUtility.FromJson<SimulatorPresetData>(json);

                // 3. Inspector 필드에 데이터 적용
                // 일반 장비 (~전설)
                weapon = data.weapon;
                helmet = data.helmet;
                bodyArmor = data.bodyArmor;
                gloves = data.gloves;
                boots = data.boots;
                amulet = data.amulet;
                belt = data.belt;
                ring1 = data.ring1;

                // 신화 장비
                weaponMythic = data.weaponMythic;
                helmetMythic = data.helmetMythic;
                bodyArmorMythic = data.bodyArmorMythic;
                glovesMythic = data.glovesMythic;
                bootsMythic = data.bootsMythic;
                amuletMythic = data.amuletMythic;
                beltMythic = data.beltMythic;
                ring1Mythic = data.ring1Mythic;

                // 스킬 및 티어
                mainSpell = data.mainSpell;
                spellTier = data.spellTier;
                spellGrade = data.spellGrade;

                // legendary 이하는 Grade를 grade1로 강제 설정
                if (spellTier != ETier.mythic)
                {
                    spellGrade = EGrade.grade1;
                }

                // 룬 소켓 복원
                if (data.runeSockets != null && data.runeSockets.Count > 0)
                {
                    ConvertDictToRuneSocketSlots(data.runeSockets);
                }

                aura = data.aura;
                auraTier = data.auraTier;
                pet = data.pet;
                petTier = data.petTier;

                // 성좌 3슬롯 로드
                constellationSlot1 = data.constellationSlot1;
                constellationSlot2 = data.constellationSlot2;
                constellationSlot3 = data.constellationSlot3;

                // 하위호환: 기존 프리셋에 selectedConstellation만 있는 경우 slot1에 매핑
                if (data.selectedConstellation != EConstellation.None
                    && constellationSlot1 == EConstellation.None
                    && constellationSlot2 == EConstellation.None
                    && constellationSlot3 == EConstellation.None)
                {
                    constellationSlot1 = data.selectedConstellation;
                }

                // 최적화 설정
                buildType = data.buildType;

                // 조건부 MOD: 적 상태이상 상태
                defender.targetHasBleeding = data.targetHasBleeding;
                defender.targetHasIgnite = data.targetHasIgnite;
                defender.targetHasArctic = data.targetHasArctic;
                defender.targetHasChill = data.targetHasChill;
                defender.targetHasShock = data.targetHasShock;
                defender.targetHasParalyze = data.targetHasParalyze;
                defender.targetHasPoisoning = data.targetHasPoisoning;
                defender.targetHasStun = data.targetHasStun;

                // 4. 파일 이름 저장 및 UI 갱신
                // 파일 경로에서 파일 이름 추출 (확장자 제외)
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                currentPresetFileName = fileName;

                Repaint();

                // 5. 로그 및 다이얼로그 (silent 모드가 아닐 때만)

                if (!silent)
                {
                    EditorUtility.DisplayDialog("불러오기 완료", $"프리셋을 불러왔습니다.\n{fileName}", "확인");
                }
                else
                {
                }
            }
            catch (System.Exception e)
            {
                if (!silent)
                {
                    Debug.LogError($"[Simulator] ❌ 프리셋 불러오기 실패: {e.Message}");
                    EditorUtility.DisplayDialog("불러오기 실패", $"프리셋 불러오기 중 오류가 발생했습니다.\n{e.Message}", "확인");
                }
            }
        }

        [TabGroup("Tabs", "👤 플레이어", Order = 1)]
        [PropertyOrder(-99)]
        [HorizontalGroup("Tabs/👤 플레이어/Actions")]
        [Button(ButtonSizes.Large, Name = "🗑️ 초기화")]
        public void ClearAll()
        {
            weapon = EEquipmentNormal.None;
            weaponMythic = EEquipmentMythic.None;
            helmet = EEquipmentNormal.None;
            helmetMythic = EEquipmentMythic.None;
            bodyArmor = EEquipmentNormal.None;
            bodyArmorMythic = EEquipmentMythic.None;
            gloves = EEquipmentNormal.None;
            glovesMythic = EEquipmentMythic.None;
            boots = EEquipmentNormal.None;
            bootsMythic = EEquipmentMythic.None;
            amulet = EEquipmentNormal.None;
            amuletMythic = EEquipmentMythic.None;
            belt = EEquipmentNormal.None;
            beltMythic = EEquipmentMythic.None;
            ring1 = EEquipmentNormal.None;
            ring1Mythic = EEquipmentMythic.None;
            mainSpell = ESkill.None;
            spellTier = ETier.common;
            spellGrade = EGrade.grade1;

            // 룬 소켓 초기화
            if (runeSocketSlots != null)
            {
                runeSocketSlots.Clear();
            }
            _pendingRuneSockets = null;

            aura = ESkill.None;
            auraTier = ETier.common;
            pet = EPet.None;
            petTier = ETier.common;

            // 성좌 3슬롯 초기화
            constellationSlot1 = EConstellation.None;
            constellationSlot2 = EConstellation.None;
            constellationSlot3 = EConstellation.None;

            // 현재 프리셋 파일 이름 초기화
            currentPresetFileName = "";

            selectedPreset = EPreset.preset_1;
            defender = SimulatorDefender.CreateDefault1000FloorBoss(); // 적 설정 초기화
        }

        #endregion
    }
}
