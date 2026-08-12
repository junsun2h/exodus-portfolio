// Prefab Mode 진입 시 발생하는 phantom dirty 자동 클리어
// [ExecuteAlways] UI 컴포넌트(ContentSizeFitter, LayoutGroup 등)가
// 레이아웃 재계산하면서 dirty를 마킹하지만 실제 파일 변경은 없는 경우 처리

using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PX.UIAutomation
{
    [InitializeOnLoad]
    public static class PrefabPhantomDirtyCleaner
    {
        private const string PREF_ENABLED = "PrefabPhantomDirtyCleaner_Enabled";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PREF_ENABLED, true);
            set => EditorPrefs.SetBool(PREF_ENABLED, value);
        }

        static PrefabPhantomDirtyCleaner()
        {
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
        }

        [MenuItem("PX Editor/UI Automation/Phantom Dirty Cleaner/Toggle")]
        private static void ToggleEnabled()
        {
            Enabled = !Enabled;
            Debug.Log($"[PhantomDirtyCleaner] {(Enabled ? "활성화" : "비활성화")}");
        }

        [MenuItem("PX Editor/UI Automation/Phantom Dirty Cleaner/Toggle", true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked("PX Editor/UI Automation/Phantom Dirty Cleaner/Toggle", Enabled);
            return true;
        }

        private static void OnPrefabStageOpened(PrefabStage stage)
        {
            if (!Enabled) return;

            string assetPath = stage.assetPath;

            // 파일 해시 기록 (열기 직전 상태)
            string hashBefore = ComputeFileHash(assetPath);

            // 레이아웃 재계산이 완료된 후 체크
            EditorApplication.delayCall += () =>
            {
                var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (currentStage == null || currentStage.assetPath != assetPath) return;
                if (!currentStage.scene.isDirty) return;

                // dirty 상태에서 임시 저장하여 파일 비교
                // → 저장하지 않고 해시만 비교하면 충분 (파일은 열기 전과 동일)
                // 열기 전 해시와 현재 파일 해시 비교
                string hashAfter = ComputeFileHash(assetPath);

                if (hashBefore == hashAfter)
                {
                    // 파일이 변경되지 않았으므로 phantom dirty
                    Undo.ClearAll();
                    EditorUtility.ClearDirty(currentStage.prefabContentsRoot);

                    // Scene dirty flag도 클리어
                    EditorSceneManager.MarkAllScenesDirty();
                    currentStage.ClearDirtiness();

                    Debug.Log($"[PhantomDirtyCleaner] Phantom dirty 클리어: {Path.GetFileName(assetPath)}");
                }
            };
        }

        private static string ComputeFileHash(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath)) return "";

            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(fullPath))
            {
                var hash = md5.ComputeHash(stream);
                return System.BitConverter.ToString(hash).Replace("-", "");
            }
        }
    }
}
