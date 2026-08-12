// 스크린샷 오버레이 에디터 윈도우
// Edit Mode에서 스크린샷을 프리팹 캔버스 위에 표시

using System.IO;
using UnityEngine;
using UnityEditor;

namespace PX.UIAutomation
{
    /// <summary>
    /// 스크린샷 오버레이 에디터 윈도우
    /// 디자인 시안 등의 스크린샷을 프리팹 캔버스 위에 표시하여 비교 가능
    /// </summary>
    public class UIOverlayWindow : EditorWindow
    {
        #region Fields

        // UI 스타일
        private GUIStyle _headerStyle;
        private GUIStyle _boxStyle;
        private bool _stylesInitialized;

        // 이미지 미리보기
        private Texture2D _previewTexture;
        private string _imagePath = "";

        // EditorPrefs 키
        private const string PREF_IMAGE_PATH = "UIOverlay_ImagePath";

        #endregion

        #region Menu

        [MenuItem("PX Editor/UI Automation/Screenshot Overlay", false, 101)]
        public static void OpenWindow()
        {
            var window = GetWindow<UIOverlayWindow>("Screenshot Overlay");
            window.minSize = new Vector2(300, 350);
            window.Show();
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            _imagePath = EditorPrefs.GetString(PREF_IMAGE_PATH, "");
            LoadPreviewTexture();
        }

        private void OnDisable()
        {
            if (_previewTexture != null)
            {
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }
        }

        private void OnDestroy()
        {
            if (_previewTexture != null)
            {
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            InitializeStyles();

            DrawHeader();
            EditorGUILayout.Space(10);
            DrawToggleButton();
            EditorGUILayout.Space(10);
            DrawImageSection();
            EditorGUILayout.Space(10);
            DrawImagePreview();
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
            EditorGUILayout.LabelField("Screenshot Overlay", _headerStyle);
            EditorGUILayout.LabelField("스크린샷을 프리팹 캔버스 위에 표시합니다.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("프리팹 편집 시 디자인 시안과 1:1 비교할 수 있습니다.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("UGUI 프리팹 또는 UI Builder가 열려있으면 자동으로 표시됩니다.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawToggleButton()
        {
            EditorGUILayout.BeginVertical(_boxStyle);

            bool isEnabled = UIOverlayManager.IsEnabled;
            bool hasImage = UIOverlayManager.HasImage;

            // On/Off 버튼
            var toggleStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                fixedHeight = 40
            };

            GUI.enabled = hasImage;

            var buttonText = isEnabled ? "오버레이 끄기 (OFF)" : "오버레이 켜기 (ON)";
            var buttonColor = isEnabled ? new Color(0.9f, 0.4f, 0.4f) : new Color(0.4f, 0.8f, 0.4f);

            GUI.backgroundColor = buttonColor;
            if (GUILayout.Button(buttonText, toggleStyle))
            {
                UIOverlayManager.Toggle();
            }
            GUI.backgroundColor = Color.white;

            GUI.enabled = true;

            // 투명도 조절
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("투명도", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float opacity = EditorGUILayout.Slider(UIOverlayManager.Opacity, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                UIOverlayManager.Opacity = opacity;
            }

            // 상태 표시
            EditorGUILayout.Space(5);
            string statusText;
            if (!hasImage)
            {
                statusText = "이미지를 먼저 로드해주세요";
            }
            else if (!isEnabled)
            {
                statusText = "오버레이 숨김";
            }
            else
            {
                bool toPrefab = UIOverlayManager.IsAttachedToPrefabStage;
                bool toBuilder = UIOverlayManager.IsAttachedToUIBuilder;
                if (toPrefab && toBuilder)
                    statusText = "오버레이 표시 중 (Prefab + UI Builder)";
                else if (toBuilder)
                    statusText = "오버레이 표시 중 (UI Builder)";
                else if (toPrefab)
                    statusText = "오버레이 표시 중 (Prefab Stage)";
                else
                    statusText = "오버레이 켜짐 — 대상 없음 (Prefab Stage 또는 UI Builder를 열어주세요)";
            }
            EditorGUILayout.LabelField(statusText, EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawImageSection()
        {
            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("스크린샷", _headerStyle);

            // 이미지 경로
            EditorGUILayout.BeginHorizontal();
            _imagePath = EditorGUILayout.TextField(_imagePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var initialDir = !string.IsNullOrEmpty(_imagePath) && File.Exists(_imagePath)
                    ? Path.GetDirectoryName(_imagePath)
                    : "Artwork/UIMockup";

                var path = EditorUtility.OpenFilePanel("스크린샷 선택", initialDir, "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(path))
                {
                    _imagePath = path;
                    LoadImage();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 버튼들
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !string.IsNullOrEmpty(_imagePath);
            if (GUILayout.Button("이미지 로드", GUILayout.Height(28)))
            {
                LoadImage();
            }
            GUI.enabled = true;

            GUI.enabled = UIOverlayManager.HasImage;
            if (GUILayout.Button("이미지 제거", GUILayout.Height(28)))
            {
                ClearImage();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawImagePreview()
        {
            if (_previewTexture == null) return;

            EditorGUILayout.BeginVertical(_boxStyle);
            EditorGUILayout.LabelField("미리보기", _headerStyle);

            // 가로폭에 맞춰 비율 유지
            float maxWidth = EditorGUIUtility.currentViewWidth - 40;
            float aspectRatio = (float)_previewTexture.height / _previewTexture.width;
            float previewHeight = Mathf.Min(maxWidth * aspectRatio, 180);

            var rect = GUILayoutUtility.GetRect(maxWidth, previewHeight);
            GUI.DrawTexture(rect, _previewTexture, ScaleMode.ScaleToFit);

            EditorGUILayout.LabelField($"{_previewTexture.width} x {_previewTexture.height}", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Image Loading

        private void LoadImage()
        {
            if (string.IsNullOrEmpty(_imagePath))
            {
                EditorUtility.DisplayDialog("오류", "이미지 경로를 지정해주세요.", "확인");
                return;
            }

            if (!File.Exists(_imagePath))
            {
                EditorUtility.DisplayDialog("오류", $"이미지 파일을 찾을 수 없습니다:\n{_imagePath}", "확인");
                return;
            }

            if (UIOverlayManager.LoadImage(_imagePath))
            {
                LoadPreviewTexture();
                EditorPrefs.SetString(PREF_IMAGE_PATH, _imagePath);
            }
            else
            {
                EditorUtility.DisplayDialog("오류", $"이미지 로드 실패:\n{_imagePath}", "확인");
            }
        }

        private void ClearImage()
        {
            UIOverlayManager.Cleanup();
            _imagePath = "";

            if (_previewTexture != null)
            {
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }

            Repaint();
        }

        private void LoadPreviewTexture()
        {
            if (_previewTexture != null)
            {
                DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }

            if (string.IsNullOrEmpty(_imagePath) || !File.Exists(_imagePath))
            {
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(_imagePath);
                _previewTexture = new Texture2D(2, 2);
                _previewTexture.LoadImage(bytes);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[UIOverlay] 미리보기 로드 실패: {ex.Message}");
            }
        }

        #endregion
    }
}
