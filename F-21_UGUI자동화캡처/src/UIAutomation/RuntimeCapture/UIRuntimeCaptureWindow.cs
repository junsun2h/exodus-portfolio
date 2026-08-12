// UI 런타임 캡처 에디터 윈도우
// Play Mode에서 UI 구조를 캡처하여 JSON으로 저장

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace PX.UIAutomation
{
    /// <summary>
    /// 런타임 UI 캡처 에디터 윈도우
    /// Play Mode에서 활성화된 UI를 감지하고 구조를 JSON으로 캡처
    /// </summary>
    public class UIRuntimeCaptureWindow : EditorWindow
    {
        #region Fields

        private Vector2 _scrollPosition;
        private List<UIPopup> _activePopups = new List<UIPopup>();
        private GameObject _selectedRoot;
        private RuntimeCaptureData _lastCaptureData;
        private string _lastSavePath;

        // UI
        private GUIStyle _headerStyle;
        private GUIStyle _boxStyle;
        private bool _stylesInitialized;

        // 설정
        private string _outputDirectory = "Assets/Editor/UIAutomation/Data/RuntimeCaptures";
        private string _screenshotDirectory = "Assets/Editor/UIAutomation/Data/Screenshot/Runtime";
        private bool _captureScreenshot = true;

        #endregion

        #region Menu

        [MenuItem("PX Editor/UI Automation/UI Runtime Capture", false, 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<UIRuntimeCaptureWindow>("UI Runtime Capture");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            if (Application.isPlaying)
            {
                RefreshActivePopups();
            }
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                RefreshActivePopups();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _activePopups.Clear();
                _selectedRoot = null;
            }

            Repaint();
        }

        private void OnInspectorUpdate()
        {
            // Play Mode에서 주기적으로 갱신
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            InitializeStyles();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawHeader();

            if (!Application.isPlaying)
            {
                DrawPlayModeWarning();
            }
            else
            {
                DrawActivePopupsList();
                EditorGUILayout.Space(10);
                DrawSelectedRootSection();
                EditorGUILayout.Space(10);
                DrawCaptureSettings();
                EditorGUILayout.Space(10);
                DrawCaptureButton();
                EditorGUILayout.Space(10);
                DrawScreenshotSection();
                EditorGUILayout.Space(10);
                DrawLastCaptureInfo();
            }

            EditorGUILayout.EndScrollView();
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 10, 10)
            };

            _boxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };

            _stylesInitialized = true;
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("UI Runtime Capture", _headerStyle);
            EditorGUILayout.LabelField("Play Mode에서 UI 구조를 캡처하여 JSON으로 저장합니다.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawPlayModeWarning()
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "Play Mode에서만 사용 가능합니다.\n\n" +
                "1. Play Mode로 진입하세요\n" +
                "2. 캡처할 UI 팝업을 열어주세요\n" +
                "3. 팝업을 선택하고 Capture 버튼을 누르세요",
                MessageType.Info);

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Play Mode 진입", GUILayout.Height(30)))
            {
                EditorApplication.isPlaying = true;
            }
        }

        private void DrawActivePopupsList()
        {
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("활성 팝업 목록", _headerStyle);

            // 새로고침 버튼
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("새로고침", GUILayout.Width(80)))
            {
                RefreshActivePopups();
            }
            EditorGUILayout.LabelField($"감지된 팝업: {_activePopups.Count}개");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (_activePopups.Count == 0)
            {
                EditorGUILayout.HelpBox("활성화된 팝업이 없습니다.\n게임에서 팝업을 열어주세요.", MessageType.Warning);
            }
            else
            {
                foreach (var popup in _activePopups)
                {
                    if (popup == null) continue;

                    EditorGUILayout.BeginHorizontal();

                    bool isSelected = _selectedRoot == popup.gameObject;
                    var buttonStyle = isSelected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;

                    if (GUILayout.Button(popup.gameObject.name, buttonStyle))
                    {
                        _selectedRoot = popup.gameObject;
                        Selection.activeGameObject = popup.gameObject;
                    }

                    // PopupKey 표시
                    EditorGUILayout.LabelField($"[{popup.PopupKey}]", GUILayout.Width(100));

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedRootSection()
        {
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("캡처 대상", _headerStyle);

            EditorGUI.BeginChangeCheck();
            _selectedRoot = (GameObject)EditorGUILayout.ObjectField(
                "Root GameObject",
                _selectedRoot,
                typeof(GameObject),
                true);

            if (EditorGUI.EndChangeCheck() && _selectedRoot != null)
            {
                Selection.activeGameObject = _selectedRoot;
            }

            if (_selectedRoot != null)
            {
                EditorGUILayout.Space(5);

                // 선택된 오브젝트 정보
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("경로", GetGameObjectPath(_selectedRoot));

                var childCount = _selectedRoot.transform.childCount;
                EditorGUILayout.LabelField("직속 자식 수", childCount.ToString());

                var totalCount = CountAllChildren(_selectedRoot.transform);
                EditorGUILayout.LabelField("전체 자손 수", totalCount.ToString());

                // 동적 컨테이너 감지 정보
                var detector = new DynamicContainerDetector();
                var containerInfo = detector.Detect(_selectedRoot);
                if (containerInfo != null)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("동적 컨테이너 감지됨", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("타입", containerInfo.ContainerType);
                    EditorGUILayout.LabelField("프리팹 필드", containerInfo.ItemPrefabField);
                    if (!string.IsNullOrEmpty(containerInfo.ItemPrefabPath))
                    {
                        EditorGUILayout.LabelField("프리팹 경로", containerInfo.ItemPrefabPath);
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCaptureSettings()
        {
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("캡처 설정", _headerStyle);

            _outputDirectory = EditorGUILayout.TextField("저장 폴더", _outputDirectory);
            _captureScreenshot = EditorGUILayout.Toggle("스크린샷 캡처", _captureScreenshot);

            EditorGUILayout.EndVertical();
        }

        private void DrawCaptureButton()
        {
            GUI.enabled = _selectedRoot != null;

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };

            if (GUILayout.Button("Capture UI Structure", buttonStyle, GUILayout.Height(40)))
            {
                CaptureUI();
            }

            GUI.enabled = true;
        }

        private void DrawScreenshotSection()
        {
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("스크린샷", _headerStyle);

            EditorGUILayout.HelpBox("현재 Game View 화면을 PNG로 캡처합니다.", MessageType.Info);

            if (GUILayout.Button("스크린샷 캡처", GUILayout.Height(30)))
            {
                CaptureScreenshotOnly();
            }

            EditorGUILayout.EndVertical();
        }

        private void CaptureScreenshotOnly()
        {
            // 디렉토리 생성
            if (!Directory.Exists(_screenshotDirectory))
            {
                Directory.CreateDirectory(_screenshotDirectory);
            }

            // 파일명 생성
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var targetName = _selectedRoot != null ? _selectedRoot.name.Replace(" ", "_") : "GameView";
            var fileName = $"runtime_{targetName}_{timestamp}.png";
            var filePath = Path.Combine(_screenshotDirectory, fileName);

            // 스크린샷 캡처
            ScreenCapture.CaptureScreenshot(filePath);

            Debug.Log($"[UIRuntimeCapture] 스크린샷 저장됨: {filePath}");

            // 다음 프레임에 AssetDatabase 새로고침
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("스크린샷", $"저장 완료:\n{filePath}", "확인");
            };
        }

        private void DrawLastCaptureInfo()
        {
            if (_lastCaptureData == null) return;

            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("마지막 캡처 정보", _headerStyle);

            EditorGUILayout.LabelField("캡처 시간", _lastCaptureData.CaptureTime);
            EditorGUILayout.LabelField("루트", _lastCaptureData.RootName);
            EditorGUILayout.LabelField("팝업 키", _lastCaptureData.SourcePopupKey ?? "(없음)");

            if (!string.IsNullOrEmpty(_lastSavePath))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("저장 경로", _lastSavePath, EditorStyles.wordWrappedLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("파일 선택", GUILayout.Width(80)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(_lastSavePath);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }
                if (GUILayout.Button("폴더 열기", GUILayout.Width(80)))
                {
                    var folderPath = Path.GetDirectoryName(_lastSavePath);
                    EditorUtility.RevealInFinder(folderPath);
                }
                if (GUILayout.Button("클립보드 복사", GUILayout.Width(100)))
                {
                    var json = File.ReadAllText(_lastSavePath);
                    EditorGUIUtility.systemCopyBuffer = json;
                    Debug.Log("[UIRuntimeCapture] JSON이 클립보드에 복사되었습니다.");
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Capture Logic

        private void RefreshActivePopups()
        {
            _activePopups.Clear();

            // 모든 UIPopup 찾기
            var allPopups = FindObjectsOfType<UIPopup>(true);
            foreach (var popup in allPopups)
            {
                if (popup.gameObject.activeInHierarchy)
                {
                    _activePopups.Add(popup);
                }
            }

            // 이름순 정렬
            _activePopups = _activePopups.OrderBy(p => p.gameObject.name).ToList();
        }

        private void CaptureUI()
        {
            if (_selectedRoot == null)
            {
                EditorUtility.DisplayDialog("오류", "캡처할 대상을 선택해주세요.", "확인");
                return;
            }

            try
            {
                // 파싱
                var parser = new UIRuntimeParser();
                _lastCaptureData = parser.Parse(_selectedRoot);

                if (_lastCaptureData == null)
                {
                    EditorUtility.DisplayDialog("오류", "UI 파싱에 실패했습니다.", "확인");
                    return;
                }

                // JSON 직렬화
                var json = UIJsonParser.SerializeRuntimeCapture(_lastCaptureData);
                if (string.IsNullOrEmpty(json))
                {
                    EditorUtility.DisplayDialog("오류", "JSON 직렬화에 실패했습니다.", "확인");
                    return;
                }

                // 저장
                _lastSavePath = SaveCaptureJson(json, _lastCaptureData);

                // 스크린샷
                if (_captureScreenshot)
                {
                    CaptureScreenshot(_lastSavePath);
                }

                // 클립보드에도 복사
                EditorGUIUtility.systemCopyBuffer = json;

                EditorUtility.DisplayDialog("성공",
                    $"UI 캡처가 완료되었습니다.\n\n저장 위치: {_lastSavePath}\n\nJSON이 클립보드에 복사되었습니다.",
                    "확인");

                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIRuntimeCapture] 캡처 실패: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("오류", $"캡처 중 오류가 발생했습니다:\n{ex.Message}", "확인");
            }
        }

        private string SaveCaptureJson(string json, RuntimeCaptureData data)
        {
            // 디렉토리 생성
            if (!Directory.Exists(_outputDirectory))
            {
                Directory.CreateDirectory(_outputDirectory);
            }

            // 파일명 생성
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var rootName = data.RootName.Replace(" ", "_");
            var fileName = $"capture_{rootName}_{timestamp}.json";
            var filePath = Path.Combine(_outputDirectory, fileName);

            // 저장
            File.WriteAllText(filePath, json);

            Debug.Log($"[UIRuntimeCapture] 캡처 저장됨: {filePath}");

            return filePath;
        }

        private void CaptureScreenshot(string jsonPath)
        {
            var screenshotPath = jsonPath.Replace(".json", ".png");
            ScreenCapture.CaptureScreenshot(screenshotPath);
            Debug.Log($"[UIRuntimeCapture] 스크린샷 저장됨: {screenshotPath}");
        }

        #endregion

        #region Utility

        private string GetGameObjectPath(GameObject obj)
        {
            var path = obj.name;
            var current = obj.transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private int CountAllChildren(Transform transform)
        {
            int count = transform.childCount;
            foreach (Transform child in transform)
            {
                count += CountAllChildren(child);
            }
            return count;
        }

        #endregion
    }
}
