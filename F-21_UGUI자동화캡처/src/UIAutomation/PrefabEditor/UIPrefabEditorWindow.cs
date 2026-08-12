// UI 자동화 시스템 - 프리팹 편집 EditorWindow
// JSON Import를 통해 프리팹을 수정하는 UI 제공
// Preview 기능 통합 (런타임 캡처 JSON 기반 더미 생성)

using System;
using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PX.UIAutomation
{
    /// <summary>
    /// UI 프리팹 편집 EditorWindow
    /// Claude Code가 생성한 JSON을 Import하여 프리팹 수정
    /// Preview 기능 통합
    /// </summary>
    public class UIPrefabEditorWindow : OdinEditorWindow
    {
        private const string DataFolder = "Assets/Editor/UIAutomation/Data";
        private const string StructureFolder = "Assets/Editor/UIAutomation/Data/Structure";
        private const string ClipboardFolder = "Assets/Editor/UIAutomation/Data/Clipboard";
        private const string ScreenshotFolder = "Assets/Editor/UIAutomation/Data/Screenshot";
        private const string CaptureFolder = "Assets/Editor/UIAutomation/Data/RuntimeCaptures";

        private const string ClipboardFileName = "modification_clipboard.json";
        private const string ScreenshotFileName = "clipboard_screenshot.png";
        private const string PreviewScreenshotFolder = "Assets/Editor/UIAutomation/Data/Screenshot/Preview";

        // 마지막으로 수정한 프리팹 경로 (되돌리기용)
        private string _lastModifiedPrefabPath;

        private string ClipboardFilePath => $"{ClipboardFolder}/{ClipboardFileName}";
        private string ScreenshotFilePath => $"{ScreenshotFolder}/{ScreenshotFileName}";

        // Preview 관련
        private UIPreviewRenderer _previewRenderer;
        private GameObject _prefabInstance;
        private List<string> _recentCaptureFiles = new List<string>();

        [MenuItem("PX Editor/UI Automation/Prefab Editor")]
        private static void OpenWindow()
        {
            var window = GetWindow<UIPrefabEditorWindow>();
            window.titleContent = new GUIContent("UI Prefab Editor");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();

            clipboardPath = ClipboardFilePath;
            screenshotPath = ScreenshotFilePath;

            // Preview 렌더러 초기화
            _previewRenderer = new UIPreviewRenderer();
            RefreshRecentCaptureFiles();

            // Prefab Stage 이벤트 구독
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;

            // 현재 Prefab Stage 확인
            var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (currentStage != null)
            {
                _prefabInstance = currentStage.prefabContentsRoot;
                targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(currentStage.assetPath);
                OnPrefabChanged();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            ClearPreview();

            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
        }

        private void OnPrefabStageOpened(PrefabStage stage)
        {
            _prefabInstance = stage.prefabContentsRoot;
            targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            OnPrefabChanged();

            // 자동으로 매칭되는 캡처 파일 찾기
            AutoMatchCaptureFile();

            Repaint();
        }

        private void OnPrefabStageClosing(PrefabStage stage)
        {
            ClearPreview();
            _prefabInstance = null;
            Repaint();
        }

        #endregion

        #region Prefab Selection

        [BoxGroup("Main", LabelText = "프리팹 수정")]
        [LabelText("대상 프리팹")]
        [AssetSelector(Paths = "Assets/GameAssets/UI/Prefab")]
        [OnValueChanged("OnPrefabChanged")]
        public GameObject targetPrefab;

        [BoxGroup("Main")]
        [LabelText("프리팹 경로")]
        [ReadOnly]
        [ShowIf("@targetPrefab != null")]
        public string prefabPath;

        [BoxGroup("Main")]
        [LabelText("구조 파일")]
        [ReadOnly]
        [ShowIf("@targetPrefab != null")]
        public string structureJsonPath;

        [BoxGroup("Main")]
        [LabelText("클립보드 파일")]
        [ReadOnly]
        public string clipboardPath;

        [BoxGroup("Main")]
        [LabelText("스크린샷 파일")]
        [ReadOnly]
        public string screenshotPath;

        private void OnPrefabChanged()
        {
            if (targetPrefab != null)
            {
                prefabPath = AssetDatabase.GetAssetPath(targetPrefab);
                structureJsonPath = GetStructureFilePath();
            }
            else
            {
                prefabPath = string.Empty;
                structureJsonPath = string.Empty;
            }

            // 프리팹 변경 시 상태 메시지 초기화
            _lastStatusMessage = "";
        }

        private string GetStructureFilePath()
        {
            if (targetPrefab == null) return string.Empty;
            return $"{StructureFolder}/{targetPrefab.name}_structure.json";
        }

        #endregion

        #region Capture JSON (Preview 연결)

        [BoxGroup("Capture", LabelText = "런타임 캡처 연결")]
        [LabelText("캡처 JSON")]
        [Sirenix.OdinInspector.FilePath(Extensions = "json")]
        [OnValueChanged("OnCaptureJsonChanged")]
        public string captureJsonPath;

        [BoxGroup("Capture")]
        [LabelText("캡처 데이터")]
        [ReadOnly]
        [ShowIf("@_captureData != null")]
        [DisplayAsString]
        public string captureDataInfo;

        private RuntimeCaptureData _captureData;

        private void OnCaptureJsonChanged()
        {
            if (!string.IsNullOrEmpty(captureJsonPath) && File.Exists(captureJsonPath))
            {
                LoadCaptureJson();
            }
            else
            {
                _captureData = null;
                captureDataInfo = "";
            }
        }

        [BoxGroup("Capture")]
        [HorizontalGroup("Capture/Buttons")]
        [Button("JSON 로드", ButtonSizes.Medium)]
        [EnableIf("@!string.IsNullOrEmpty(captureJsonPath)")]
        private void LoadCaptureJsonButton()
        {
            LoadCaptureJson(showDialog: true);
        }

        [HorizontalGroup("Capture/Buttons")]
        [Button("최근 파일", ButtonSizes.Medium)]
        private void ShowRecentCaptureFiles()
        {
            RefreshRecentCaptureFiles();

            var menu = new GenericMenu();
            foreach (var filePath in _recentCaptureFiles)
            {
                var fileName = Path.GetFileName(filePath);
                var path = filePath; // 클로저용
                menu.AddItem(new GUIContent(fileName), false, () =>
                {
                    captureJsonPath = path;
                    LoadCaptureJson();
                });
            }

            if (_recentCaptureFiles.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("캡처 파일 없음"));
            }

            menu.ShowAsContext();
        }

        private void LoadCaptureJson(bool showDialog = false)
        {
            if (string.IsNullOrEmpty(captureJsonPath) || !File.Exists(captureJsonPath))
            {
                _captureData = null;
                captureDataInfo = "";
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("오류", "캡처 JSON 파일이 없거나 경로가 비어있습니다.", "확인");
                }
                return;
            }

            try
            {
                var json = File.ReadAllText(captureJsonPath);
                _captureData = UIJsonParser.ParseRuntimeCapture(json);

                if (_captureData != null)
                {
                    var containerCount = CountDynamicContainers(_captureData);
                    captureDataInfo = $"루트: {_captureData.RootName}, 컨테이너: {containerCount}개, 시간: {_captureData.CaptureTime}";

                    if (showDialog)
                    {
                        var fileName = Path.GetFileName(captureJsonPath);
                        EditorUtility.DisplayDialog("완료",
                            $"캡처 JSON 로드 완료\n\n파일: {fileName}\n루트: {_captureData.RootName}\n컨테이너: {containerCount}개",
                            "확인");
                    }
                }
                else
                {
                    captureDataInfo = "파싱 실패";
                    if (showDialog)
                    {
                        EditorUtility.DisplayDialog("오류", "캡처 JSON 파싱 실패: 데이터가 null입니다.", "확인");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabEditor] 캡처 JSON 로드 실패: {ex.Message}");
                _captureData = null;
                captureDataInfo = "로드 실패";
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("오류", $"캡처 JSON 로드 실패\n\n{ex.Message}", "확인");
                }
            }
        }

        #endregion

        #region Preview

        [BoxGroup("Preview", LabelText = "프리뷰")]
        [LabelText("프리뷰 활성화")]
        [OnValueChanged("OnPreviewEnabledChanged")]
        public bool previewEnabled;

        [BoxGroup("Preview")]
        [LabelText("더미 개수")]
        [ReadOnly]
        [ShowIf("@previewEnabled")]
        public int dummyCount;

        private void OnPreviewEnabledChanged()
        {
            if (previewEnabled)
            {
                CreatePreview();
            }
            else
            {
                ClearPreview();
            }
        }

        [BoxGroup("Preview")]
        [HorizontalGroup("Preview/Buttons")]
        [Button("프리뷰 생성", ButtonSizes.Medium)]
        [GUIColor(0.4f, 0.7f, 0.9f)]
        [EnableIf("@_captureData != null && _prefabInstance != null")]
        private void CreatePreviewButton()
        {
            CreatePreview();
            previewEnabled = true;
        }

        [HorizontalGroup("Preview/Buttons")]
        [Button("프리뷰 제거", ButtonSizes.Medium)]
        [GUIColor(0.9f, 0.6f, 0.4f)]
        private void ClearPreviewButton()
        {
            ClearPreview();
            previewEnabled = false;
        }

        [HorizontalGroup("Preview/Buttons")]
        [Button("새로고침", ButtonSizes.Medium)]
        [EnableIf("@previewEnabled && _captureData != null")]
        private void RefreshPreview()
        {
            ClearPreview();
            CreatePreview();
        }

        private void CreatePreview()
        {
            if (_captureData == null)
            {
                EditorUtility.DisplayDialog("오류", "먼저 캡처 JSON을 로드해주세요.", "확인");
                previewEnabled = false;
                return;
            }

            if (_prefabInstance == null)
            {
                // Prefab Stage가 열려있지 않으면 열기
                if (targetPrefab != null)
                {
                    AssetDatabase.OpenAsset(targetPrefab);
                    return;
                }

                EditorUtility.DisplayDialog("오류", "프리팹을 열어주세요.", "확인");
                previewEnabled = false;
                return;
            }

            try
            {
                _previewRenderer.Render(_prefabInstance, _captureData);
                dummyCount = _previewRenderer.DummyCount;
                SceneView.RepaintAll();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabEditor] 프리뷰 생성 실패: {ex.Message}");
                previewEnabled = false;
            }
        }

        private void ClearPreview()
        {
            _previewRenderer?.Clear();
            dummyCount = 0;
            SceneView.RepaintAll();
        }

        #endregion

        #region Actions

        [BoxGroup("Actions", LabelText = "작업")]
        [HorizontalGroup("Actions/Buttons1")]
        [Button("구조 파일 생성", ButtonSizes.Large)]
        [GUIColor(0.3f, 0.6f, 0.8f)]
        [EnableIf("@targetPrefab != null")]
        public void GenerateStructureJson()
        {
            if (targetPrefab == null)
            {
                EditorUtility.DisplayDialog("오류", "프리팹을 선택해주세요.", "확인");
                return;
            }

            // 폴더 확인 및 생성
            EnsureFoldersExist();

            try
            {
                // 메인 프리팹 구조 파일 생성
                string json = UIYamlParser.GetPrefabStructureAsJson(prefabPath);
                string filePath = GetStructureFilePath();
                File.WriteAllText(filePath, json);

                // Nested Prefab들의 구조 파일도 함께 생성
                var nestedPrefabs = UIYamlParser.CollectNestedPrefabPaths(prefabPath);
                int nestedCount = 0;

                foreach (string nestedPath in nestedPrefabs)
                {
                    string nestedJson = UIYamlParser.GetPrefabStructureAsJson(nestedPath);
                    string nestedName = System.IO.Path.GetFileNameWithoutExtension(nestedPath);
                    string nestedFilePath = $"{StructureFolder}/{nestedName}_structure.json";

                    // 이미 존재하는 파일은 덮어쓰기
                    File.WriteAllText(nestedFilePath, nestedJson);
                    nestedCount++;
                }

                AssetDatabase.Refresh();

                string message = $"구조 파일 생성 완료\n{filePath}";
                if (nestedCount > 0)
                {
                    message += $"\n\nNested Prefab {nestedCount}개 구조 파일도 함께 생성됨";
                }

                // 캡처 데이터가 있고 Preview가 활성화되어 있으면 알림
                if (_captureData != null && previewEnabled)
                {
                    message += "\n\n[참고] Preview 더미는 구조 파일에 포함되지 않습니다.\n수정 JSON 적용 시 동적 아이템은 원본 프리팹에 분리 적용됩니다.";
                }

                EditorUtility.DisplayDialog("완료", message, "확인");

                // 생성된 파일 선택
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(filePath);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("오류", $"파일 저장 실패: {ex.Message}", "확인");
            }
        }

        private void EnsureFoldersExist()
        {
            if (!Directory.Exists(DataFolder)) Directory.CreateDirectory(DataFolder);
            if (!Directory.Exists(StructureFolder)) Directory.CreateDirectory(StructureFolder);
            if (!Directory.Exists(ClipboardFolder)) Directory.CreateDirectory(ClipboardFolder);
            if (!Directory.Exists(ScreenshotFolder)) Directory.CreateDirectory(ScreenshotFolder);
            if (!Directory.Exists(CaptureFolder)) Directory.CreateDirectory(CaptureFolder);
            if (!Directory.Exists(PreviewScreenshotFolder)) Directory.CreateDirectory(PreviewScreenshotFolder);
        }

        [HorizontalGroup("Actions/Buttons1")]
        [Button("스크린샷(클립보드) 캡처", ButtonSizes.Large)]
        [GUIColor(0.3f, 0.6f, 0.8f)]
        [EnableIf("@targetPrefab != null")]
        public void CaptureScreenshot()
        {
            if (targetPrefab == null)
            {
                EditorUtility.DisplayDialog("오류", "프리팹을 선택해주세요.", "확인");
                return;
            }

            // 폴더 확인 및 생성
            EnsureFoldersExist();

            // Prefab Instance가 있으면 Preview 포함 스크린샷
            if (_prefabInstance != null)
            {
                bool success = UIPrefabScreenshotCapture.Capture(_prefabInstance, ScreenshotFilePath);

                if (success)
                {
                    AssetDatabase.Refresh();
                    string message = $"스크린샷 캡처 완료\n{ScreenshotFilePath}";
                    if (previewEnabled && dummyCount > 0)
                    {
                        message += $"\n\n(Preview 더미 {dummyCount}개 포함)";
                    }
                    EditorUtility.DisplayDialog("완료", message, "확인");

                    var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(ScreenshotFilePath);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("오류", "스크린샷 캡처 실패\n콘솔 로그 확인", "확인");
                }
            }
            else
            {
                // 프리팹 경로로 캡처
                bool success = UIPrefabScreenshotCapture.Capture(prefabPath, ScreenshotFilePath);

                if (success)
                {
                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog("완료", $"스크린샷 캡처 완료\n{ScreenshotFilePath}", "확인");

                    var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(ScreenshotFilePath);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("오류", "스크린샷 캡처 실패\n콘솔 로그 확인", "확인");
                }
            }
        }

        [BoxGroup("Actions")]
        [Button("수정 JSON 적용하기", ButtonSizes.Large, ButtonStyle.Box)]
        [GUIColor(0.3f, 0.8f, 0.4f)]
        [EnableIf("@targetPrefab != null")]
        public void ApplyClipboard()
        {
            if (targetPrefab == null)
            {
                EditorUtility.DisplayDialog("오류", "프리팹을 선택해주세요.", "확인");
                return;
            }

            if (!File.Exists(ClipboardFilePath))
            {
                EditorUtility.DisplayDialog("오류", $"클립보드 파일이 없습니다.\n{ClipboardFilePath}", "확인");
                return;
            }

            try
            {
                string json = File.ReadAllText(ClipboardFilePath);
                var data = UIJsonParser.ParseModification(json);

                if (data == null)
                {
                    EditorUtility.DisplayDialog("오류", "JSON 파싱 실패", "확인");
                    return;
                }

                if (data.Modifications == null || data.Modifications.Count == 0)
                {
                    EditorUtility.DisplayDialog("오류", "적용할 수정사항이 없습니다.", "확인");
                    return;
                }

                // 프리팹 경로 확인 (클립보드에 지정된 경로 또는 선택된 프리팹)
                string targetPath = !string.IsNullOrEmpty(data.PrefabPath) ? data.PrefabPath : prefabPath;

                // 수정 전 백업 생성
                string backupPath = UIPrefabBackup.CreateBackup(targetPath);
                if (string.IsNullOrEmpty(backupPath))
                {
                    bool proceed = EditorUtility.DisplayDialog("경고",
                        "백업 생성에 실패했습니다.\n그래도 계속 진행하시겠습니까?",
                        "계속", "취소");
                    if (!proceed) return;
                }

                bool success;

                // 캡처 데이터가 있으면 분리 적용
                if (_captureData != null)
                {
                    success = UIPrefabModifier.ApplyModificationsWithCapture(data, _captureData);

                    if (success)
                    {
                        UpdateStatusMessage($"수정 완료: {data.Modifications.Count}개 항목 (분리 적용됨)");
                    }
                }
                else
                {
                    success = UIPrefabModifier.ApplyModifications(targetPath, data);

                    if (success)
                    {
                        UpdateStatusMessage($"수정 완료: {data.Modifications.Count}개 항목 적용");
                    }
                }

                if (success)
                {
                    _lastModifiedPrefabPath = targetPath;

                    // 수정 후 상태 저장 (Redo용)
                    UIPrefabBackup.SaveAfterModification(targetPath);

                    AssetDatabase.ImportAsset(targetPath);

                    // 수정된 프리팹 선택
                    var modifiedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
                    if (modifiedPrefab != null)
                    {
                        Selection.activeObject = modifiedPrefab;
                    }

                    // Preview 새로고침
                    if (previewEnabled)
                    {
                        RefreshPreview();
                    }
                }
                else
                {
                    _lastModifiedPrefabPath = targetPath;
                    UpdateStatusMessage("일부 수정사항 적용 실패 - 콘솔 로그 확인");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("오류", $"적용 실패: {ex.Message}", "확인");
            }
        }

        [HorizontalGroup("Actions/UndoRedo")]
        [Button("◀ Undo", ButtonSizes.Large)]
        [GUIColor(0.9f, 0.6f, 0.3f)]
        [EnableIf("@CanUndo()")]
        public void UndoModification()
        {
            string targetPath = GetTargetPrefabPath();
            if (string.IsNullOrEmpty(targetPath)) return;

            bool success = UIPrefabBackup.Undo(targetPath);

            if (success)
            {
                UpdateStatusMessage("Undo 완료 - 이전 상태로 복원됨");
                var restoredPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
                if (restoredPrefab != null)
                {
                    Selection.activeObject = restoredPrefab;
                }

                // Preview 새로고침
                if (previewEnabled)
                {
                    RefreshPreview();
                }
            }
        }

        [HorizontalGroup("Actions/UndoRedo")]
        [Button("Redo ▶", ButtonSizes.Large)]
        [GUIColor(0.9f, 0.6f, 0.3f)]
        [EnableIf("@CanRedo()")]
        public void RedoModification()
        {
            string targetPath = GetTargetPrefabPath();
            if (string.IsNullOrEmpty(targetPath)) return;

            bool success = UIPrefabBackup.Redo(targetPath);

            if (success)
            {
                UpdateStatusMessage("Redo 완료 - 다음 상태로 복원됨");
                var restoredPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
                if (restoredPrefab != null)
                {
                    Selection.activeObject = restoredPrefab;
                }

                // Preview 새로고침
                if (previewEnabled)
                {
                    RefreshPreview();
                }
            }
        }

        [HorizontalGroup("Actions/UndoRedo")]
        [Button("히스토리 정리", ButtonSizes.Large)]
        [GUIColor(0.9f, 0.6f, 0.3f)]
        public void ClearAllBackups()
        {
            bool confirm = EditorUtility.DisplayDialog("히스토리 정리",
                "모든 프리팹의 백업 파일을 삭제합니다.\n이 작업은 되돌릴 수 없습니다.\n\n계속하시겠습니까?",
                "삭제", "취소");

            if (confirm)
            {
                int count = UIPrefabBackup.ClearAllBackups();
                UpdateStatusMessage($"백업 정리 완료: {count}개 파일 삭제");
            }
        }

        private string GetTargetPrefabPath()
        {
            return !string.IsNullOrEmpty(_lastModifiedPrefabPath) ? _lastModifiedPrefabPath : prefabPath;
        }

        private bool CanUndo()
        {
            string targetPath = GetTargetPrefabPath();
            return !string.IsNullOrEmpty(targetPath) && UIPrefabBackup.CanUndo(targetPath);
        }

        private bool CanRedo()
        {
            string targetPath = GetTargetPrefabPath();
            return !string.IsNullOrEmpty(targetPath) && UIPrefabBackup.CanRedo(targetPath);
        }

        #endregion

        #region Utility Screenshot

        [BoxGroup("UtilScreenshot", LabelText = "유틸리티 - 스크린샷")]
        [Button("스크린샷(Preview) 캡처", ButtonSizes.Large)]
        [GUIColor(0.6f, 0.5f, 0.8f)]
        [EnableIf("@targetPrefab != null")]
        public void CapturePreviewScreenshot()
        {
            if (targetPrefab == null)
            {
                EditorUtility.DisplayDialog("오류", "프리팹을 선택해주세요.", "확인");
                return;
            }

            // 폴더 확인 및 생성
            EnsureFoldersExist();

            // 파일명 생성: preview_{프리팹명}_{타임스탬프}.png
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var prefabName = targetPrefab.name.Replace(" ", "_").Replace("(Clone)", "");
            var fileName = $"preview_{prefabName}_{timestamp}.png";
            var filePath = Path.Combine(PreviewScreenshotFolder, fileName);

            bool success;

            // Prefab Instance가 있으면 Preview 포함 스크린샷
            if (_prefabInstance != null)
            {
                success = UIPrefabScreenshotCapture.Capture(_prefabInstance, filePath);
            }
            else
            {
                // 프리팹 경로로 캡처
                success = UIPrefabScreenshotCapture.Capture(prefabPath, filePath);
            }

            if (success)
            {
                AssetDatabase.Refresh();
                string message = $"Preview 스크린샷 저장 완료\n{filePath}";
                if (previewEnabled && dummyCount > 0)
                {
                    message += $"\n\n(Preview 더미 {dummyCount}개 포함)";
                }
                EditorUtility.DisplayDialog("완료", message, "확인");

                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                }
            }
            else
            {
                EditorUtility.DisplayDialog("오류", "스크린샷 캡처 실패\n콘솔 로그 확인", "확인");
            }
        }

        #endregion

        #region Help

        [BoxGroup("Help", LabelText = "사용 방법")]
        [DisplayAsString, HideLabel]
        [InfoBox(
            "1. 대상 프리팹을 선택합니다 (또는 Prefab Stage에서 자동 감지)\n" +
            "2. [선택] 런타임 캡처 JSON을 로드하여 Preview 생성\n" +
            "3. '구조 파일 생성' 클릭 → 구조 JSON 생성\n" +
            "4. '스크린샷 캡처' 클릭 → PNG 이미지 생성 (Preview 포함)\n" +
            "5. Claude Code에 수정 요청 → 클립보드 파일에 자동 기입\n" +
            "6. '수정 JSON 적용하기' 클릭 → 프리팹 수정 완료\n" +
            "   - 캡처 JSON 연결 시: 동적 아이템은 원본 프리팹에 분리 적용\n" +
            "7. Undo/Redo로 히스토리 탐색 (최대 10단계)",
            InfoMessageType.Info
        )]
        public string helpPlaceholder = "";

        #endregion

        #region Status Info

        private string _lastStatusMessage = "";

        [BoxGroup("Status", LabelText = "상태", Order = 100)]
        [OnInspectorGUI]
        private void DrawStatusInfo()
        {
            string statusText = GetStatusDisplay();

            var style = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 12,
                padding = new RectOffset(10, 10, 8, 8),
                richText = true,
                wordWrap = true
            };

            GUILayout.Label(statusText, style);
        }

        private string GetStatusDisplay()
        {
            var lines = new List<string>();

            // Prefab Stage 상태
            var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (currentStage != null)
            {
                lines.Add($"Prefab Stage: {Path.GetFileName(currentStage.assetPath)}");
            }
            else if (targetPrefab != null)
            {
                lines.Add("Prefab Stage: 닫힘 (더블클릭하여 열기)");
            }
            else
            {
                lines.Add("프리팹을 선택해주세요.");
            }

            // Preview 상태
            if (previewEnabled && dummyCount > 0)
            {
                lines.Add($"Preview: 활성화 ({dummyCount}개 더미)");
            }

            // 히스토리 상태
            string targetPath = GetTargetPrefabPath();
            if (!string.IsNullOrEmpty(targetPath))
            {
                var state = UIPrefabBackup.GetUndoRedoState(targetPath);
                if (state.total > 0)
                {
                    string historyInfo = $"히스토리: {state.current}/{state.total}";

                    string undoRedo = "";
                    if (CanUndo() && CanRedo())
                        undoRedo = " [Undo/Redo 가능]";
                    else if (CanUndo())
                        undoRedo = " [Undo 가능]";
                    else if (CanRedo())
                        undoRedo = " [Redo 가능]";

                    lines.Add(historyInfo + undoRedo);
                }
            }

            // 마지막 상태 메시지
            if (!string.IsNullOrEmpty(_lastStatusMessage))
            {
                lines.Add(_lastStatusMessage);
            }

            return string.Join("\n", lines);
        }

        private void UpdateStatusMessage(string message)
        {
            _lastStatusMessage = message;
        }

        #endregion

        #region Utility

        private void RefreshRecentCaptureFiles()
        {
            _recentCaptureFiles.Clear();

            if (Directory.Exists(CaptureFolder))
            {
                var files = Directory.GetFiles(CaptureFolder, "*.json");

                // 최신순 정렬 (최대 10개)
                var sortedFiles = new List<string>(files);
                sortedFiles.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));

                for (int i = 0; i < Mathf.Min(sortedFiles.Count, 10); i++)
                {
                    // 상대 경로로 변환
                    var relativePath = sortedFiles[i].Replace("\\", "/");
                    if (relativePath.StartsWith(Application.dataPath.Replace("\\", "/")))
                    {
                        relativePath = "Assets" + relativePath.Substring(Application.dataPath.Length);
                    }
                    _recentCaptureFiles.Add(relativePath);
                }
            }
        }

        private void AutoMatchCaptureFile()
        {
            if (targetPrefab == null) return;

            var prefabName = targetPrefab.name;

            // 프리팹 이름과 매칭되는 캡처 파일 찾기
            foreach (var filePath in _recentCaptureFiles)
            {
                var fileName = Path.GetFileName(filePath);
                if (fileName.Contains(prefabName))
                {
                    captureJsonPath = filePath;
                    LoadCaptureJson();
                    break;
                }
            }
        }

        private int CountDynamicContainers(RuntimeCaptureData data)
        {
            int count = 0;
            if (data?.Hierarchy != null)
            {
                foreach (var node in data.Hierarchy)
                {
                    count += CountDynamicContainersRecursive(node);
                }
            }
            return count;
        }

        private int CountDynamicContainersRecursive(RuntimeHierarchyNode node)
        {
            int count = node.DynamicContainer != null ? 1 : 0;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    count += CountDynamicContainersRecursive(child);
                }
            }
            return count;
        }

        #endregion
    }
}
