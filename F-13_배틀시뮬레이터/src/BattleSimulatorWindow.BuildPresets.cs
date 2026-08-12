using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using PX;
using Newtonsoft.Json;

namespace BattleSimulator
{
    public partial class BattleSimulatorWindow
    {
        #region 빌드 프리셋 관리

        // 10개 빌드 프리셋 슬롯
        private static BuildPresetsContainer buildPresetsContainer;

        // Application.dataPath를 사용하여 절대 경로로 변경
        private static string BUILD_PRESETS_PATH => System.IO.Path.Combine(UnityEngine.Application.dataPath, "Editor/BattleSimulator/Data/Reports/BuildPresets.json");
        private static string PRESETS_FOLDER => System.IO.Path.Combine(UnityEngine.Application.dataPath, "Editor/BattleSimulator/Data/Presets");

        /// <summary>
        /// 빌드 프리셋 컨테이너 초기화
        /// </summary>
        private void InitializeBuildPresets()
        {
            if (buildPresetsContainer == null)
            {
                // 저장된 슬롯 정보가 있으면 로드, 없으면 새로 생성
                if (File.Exists(BUILD_PRESETS_PATH))
                {
                    try
                    {
                        string json = File.ReadAllText(BUILD_PRESETS_PATH);
                        buildPresetsContainer = JsonConvert.DeserializeObject<BuildPresetsContainer>(json);

                        // 20개 슬롯 중복 감지 (첫 10개가 비어있고 중복 슬롯이 있는 경우에만 정리)
                        if (buildPresetsContainer != null && buildPresetsContainer.slots != null && buildPresetsContainer.slots.Count >= 20)
                        {
                            // 첫 10개가 모두 비어있는지 확인 (실제 중복 패턴 감지)
                            bool firstTenEmpty = true;
                            for (int i = 0; i < 10 && i < buildPresetsContainer.slots.Count; i++)
                            {
                                if (buildPresetsContainer.slots[i] != null && !string.IsNullOrEmpty(buildPresetsContainer.slots[i].presetFilePath))
                                {
                                    firstTenEmpty = false;
                                    break;
                                }
                            }

                            // 실제 중복 패턴이 감지된 경우에만 정리
                            if (firstTenEmpty)
                            {
                                buildPresetsContainer.slots = buildPresetsContainer.slots.GetRange(10, 10);

                                // 정리된 데이터를 파일에 저장
                                SaveBuildPresetsSlots();
                            }
                        }

                        // 역직렬화 후 검증 및 보완
                        if (buildPresetsContainer == null)
                        {
                            buildPresetsContainer = new BuildPresetsContainer();

                            // 10개 기본 슬롯 추가
                            for (int i = 0; i < 10; i++)
                            {
                                buildPresetsContainer.slots.Add(new BuildPresetSlot((EBuildPresetType)i));
                            }
                        }
                        else
                        {
                            // 10개 기본 슬롯이 부족하면 추가
                            while (buildPresetsContainer.slots.Count < 10)
                            {
                                int index = buildPresetsContainer.slots.Count;
                                buildPresetsContainer.slots.Add(new BuildPresetSlot((EBuildPresetType)index));
                            }

                            // 각 슬롯의 누락된 필드 보완
                            for (int i = 0; i < buildPresetsContainer.slots.Count; i++)
                            {
                                var slot = buildPresetsContainer.slots[i];
                                if (slot == null)
                                {
                                    buildPresetsContainer.slots[i] = new BuildPresetSlot((EBuildPresetType)i);
                                    continue;
                                }

                                // customBuildName이 null이면 빈 문자열로 초기화
                                if (slot.customBuildName == null)
                                {
                                    slot.customBuildName = "";
                                }

                                // buildName이 비어있으면 재생성
                                if (string.IsNullOrEmpty(slot.buildName))
                                {
                                    if (slot.buildType == EBuildPresetType.Custom)
                                    {
                                        slot.buildName = slot.customBuildName;
                                    }
                                    else
                                    {
                                        slot.buildName = BuildPresetSlot.GetBuildTypeName(slot.buildType);
                                    }
                                }
                            }
                        }

                        // UI 필드들에도 값 로드
                        LoadPresetsToUI();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[BuildPresets] 슬롯 정보 로드 실패: {e.Message}");
                        buildPresetsContainer = new BuildPresetsContainer();

                        // 10개 기본 슬롯 추가
                        for (int i = 0; i < 10; i++)
                        {
                            buildPresetsContainer.slots.Add(new BuildPresetSlot((EBuildPresetType)i));
                        }
                    }
                }
                else
                {
                    buildPresetsContainer = new BuildPresetsContainer();

                    // 10개 기본 슬롯 추가
                    for (int i = 0; i < 10; i++)
                    {
                        buildPresetsContainer.slots.Add(new BuildPresetSlot((EBuildPresetType)i));
                    }
                }
            }
        }

        /// <summary>
        /// Presets 폴더의 모든 .json 파일 목록 가져오기
        /// </summary>
        private IEnumerable<string> GetPresetFileList()
        {
            if (!Directory.Exists(PRESETS_FOLDER))
            {
                Debug.LogError($"[BuildPresets] Presets 폴더를 찾을 수 없습니다: {PRESETS_FOLDER}");
                return System.Linq.Enumerable.Empty<string>();
            }

            var files = Directory.GetFiles(PRESETS_FOLDER, "*.json")
                .Select(f => Path.GetFileName(f))
                .OrderBy(f => f)
                .ToList();

            return files;
        }

        /// <summary>
        /// 지정된 슬롯에 프리셋 파일 설정
        /// </summary>
        private void SetPresetFile(EBuildPresetType buildType, string fileName)
        {
            InitializeBuildPresets();

            int index = (int)buildType;
            buildPresetsContainer.slots[index].presetFilePath = fileName;

            // JSON으로 저장
            SaveBuildPresetsSlots();
        }

        /// <summary>
        /// 지정된 슬롯의 프리셋 로드
        /// </summary>
        private void LoadBuildPreset(EBuildPresetType buildType)
        {
            InitializeBuildPresets();

            int index = (int)buildType;
            BuildPresetSlot slot = buildPresetsContainer.slots[index];

            if (!slot.HasPreset())
            {
                return;
            }

            string fullPath = Path.Combine(PRESETS_FOLDER, slot.presetFilePath);

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[BuildPresets] 프리셋 파일을 찾을 수 없습니다: {fullPath}");
                return;
            }

            // 기존 LoadPresetFromPath 함수 사용
            LoadPresetFromPath(fullPath, silent: false);
        }

        /// <summary>
        /// 슬롯 정보를 JSON 파일로 저장
        /// </summary>
        private void SaveBuildPresetsSlots()
        {
            try
            {
                // 디렉토리 생성
                string directory = Path.GetDirectoryName(BUILD_PRESETS_PATH);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // JSON 직렬화 및 저장
                string json = JsonConvert.SerializeObject(buildPresetsContainer, Formatting.Indented);
                File.WriteAllText(BUILD_PRESETS_PATH, json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BuildPresets] 슬롯 정보 저장 실패: {e.Message}\n스택: {e.StackTrace}");
            }
        }

        /// <summary>
        /// 지정된 슬롯에 설정된 프리셋 파일명 가져오기
        /// </summary>
        private string GetSelectedPresetFile(EBuildPresetType buildType)
        {
            InitializeBuildPresets();
            int index = (int)buildType;
            return buildPresetsContainer.slots[index].presetFilePath;
        }

        /// <summary>
        /// 커스텀 빌드 추가
        /// </summary>
        private void AddCustomBuild(string customBuildName)
        {
            InitializeBuildPresets();

            if (string.IsNullOrEmpty(customBuildName))
            {
                Debug.LogError("[BuildPresets] 커스텀 빌드 이름이 비어있습니다.");
                return;
            }

            buildPresetsContainer.AddCustomSlot(customBuildName);
            SaveBuildPresetsSlots();
        }

        /// <summary>
        /// 커스텀 빌드 제거
        /// </summary>
        private void RemoveCustomBuild(int slotIndex)
        {
            InitializeBuildPresets();

            if (slotIndex < 10)
            {
                Debug.LogError("[BuildPresets] 기본 10개 빌드는 제거할 수 없습니다.");
                return;
            }

            buildPresetsContainer.RemoveCustomSlot(slotIndex);
            SaveBuildPresetsSlots();
        }

        /// <summary>
        /// 커스텀 빌드 목록 가져오기
        /// </summary>
        private List<BuildPresetSlot> GetCustomBuilds()
        {
            InitializeBuildPresets();
            return buildPresetsContainer.GetCustomSlots();
        }

        #endregion
    }
}
