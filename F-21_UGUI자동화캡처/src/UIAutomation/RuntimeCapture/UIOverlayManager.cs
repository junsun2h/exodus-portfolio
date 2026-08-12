// 스크린샷 오버레이 매니저
// Edit Mode에서 프리팹 캔버스 위에 스크린샷을 오버레이로 표시

using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace PX.UIAutomation
{
    /// <summary>
    /// 스크린샷 오버레이 매니저
    /// Edit Mode에서 프리팹 편집 시 스크린샷을 캔버스 위에 직접 표시
    /// 프리팹과 함께 이동/확대되어 1:1 비교 가능
    /// </summary>
    [InitializeOnLoad]
    public static class UIOverlayManager
    {
        #region Fields

        private static Texture2D _overlayTexture;
        private static GameObject _overlayObject;
        private static RawImage _overlayImage;
        private static CanvasGroup _canvasGroup;

        private static bool _isEnabled;
        private static float _opacity = 0.5f;
        private static string _currentImagePath;

        // EditorPrefs 키
        private const string PREF_ENABLED = "UIOverlay_Enabled";
        private const string PREF_IMAGE_PATH = "UIOverlay_ImagePath";
        private const string PREF_OPACITY = "UIOverlay_Opacity";

        private const string OVERLAY_OBJECT_NAME = "__UIOverlay_Reference__";

        // UI Toolkit (UI Builder) 오버레이 필드
        private static VisualElement _toolkitOverlayElement;   // panel-root 자식
        private static VisualElement _trackedPanelRoot;        // 어떤 panel-root에 붙였는지 추적
        private static double _nextToolkitTick;                // EditorApplication.update 폴링 throttle
        private const double TOOLKIT_TICK_INTERVAL = 0.5;      // 0.5초마다 재부착 체크
        private const string OVERLAY_ELEMENT_NAME = "__UIOverlay_Reference_VE__";
        private const string UI_BUILDER_TYPE_FULL_NAME = "Unity.UI.Builder.Builder";

        #endregion

        #region Properties

        /// <summary>
        /// 오버레이 활성화 여부
        /// </summary>
        public static bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    EditorPrefs.SetBool(PREF_ENABLED, value);
                    UpdateOverlay();
                }
            }
        }

        /// <summary>
        /// 오버레이 투명도 (0~1)
        /// </summary>
        public static float Opacity
        {
            get => _opacity;
            set
            {
                float newValue = Mathf.Clamp01(value);
                if (!Mathf.Approximately(_opacity, newValue))
                {
                    _opacity = newValue;
                    EditorPrefs.SetFloat(PREF_OPACITY, newValue);
                    UpdateOpacity();
                }
            }
        }

        /// <summary>
        /// 현재 로드된 이미지 경로
        /// </summary>
        public static string CurrentImagePath => _currentImagePath;

        /// <summary>
        /// 이미지가 로드되어 있는지 여부
        /// </summary>
        public static bool HasImage => _overlayTexture != null;

        /// <summary>
        /// 현재 PrefabStage 캔버스에 오버레이가 부착되어 있는지
        /// </summary>
        public static bool IsAttachedToPrefabStage => _overlayObject != null;

        /// <summary>
        /// 현재 UI Builder panel-root에 오버레이가 부착되어 있는지
        /// </summary>
        public static bool IsAttachedToUIBuilder =>
            _toolkitOverlayElement != null && _toolkitOverlayElement.parent != null;

        #endregion

        #region Static Constructor

        static UIOverlayManager()
        {
            // 저장된 설정 로드
            _isEnabled = EditorPrefs.GetBool(PREF_ENABLED, false);
            _opacity = EditorPrefs.GetFloat(PREF_OPACITY, 0.5f);
            _currentImagePath = EditorPrefs.GetString(PREF_IMAGE_PATH, "");

            // Prefab Stage 이벤트 등록
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;

            // UI Builder는 open/close 이벤트가 없어 폴링으로 panel-root 부착/재부착을 추적
            EditorApplication.update += ToolkitTick;

            // 이전에 로드했던 이미지 복원
            if (!string.IsNullOrEmpty(_currentImagePath) && File.Exists(_currentImagePath))
            {
                LoadImageInternal(_currentImagePath);
            }

            // 현재 열려있는 Prefab Stage가 있으면 오버레이 생성
            var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (currentStage != null && _isEnabled && _overlayTexture != null)
            {
                CreateOverlayInPrefab(currentStage.prefabContentsRoot);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 이미지 파일을 로드
        /// </summary>
        public static bool LoadImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                Debug.LogWarning("[UIOverlayManager] 이미지 경로가 비어있습니다.");
                return false;
            }

            if (!File.Exists(imagePath))
            {
                Debug.LogWarning($"[UIOverlayManager] 이미지 파일을 찾을 수 없습니다: {imagePath}");
                return false;
            }

            bool success = LoadImageInternal(imagePath);

            if (success)
            {
                _currentImagePath = imagePath;
                EditorPrefs.SetString(PREF_IMAGE_PATH, imagePath);
                UpdateOverlay();
                Debug.Log($"[UIOverlayManager] 이미지 로드 완료: {imagePath}");
            }

            return success;
        }

        /// <summary>
        /// 오버레이 정리 및 제거
        /// </summary>
        public static void Cleanup()
        {
            DestroyOverlayObject();
            DestroyToolkitOverlay();

            if (_overlayTexture != null)
            {
                Object.DestroyImmediate(_overlayTexture);
                _overlayTexture = null;
            }

            _currentImagePath = "";
            _isEnabled = false;

            EditorPrefs.SetString(PREF_IMAGE_PATH, "");
            EditorPrefs.SetBool(PREF_ENABLED, false);
        }

        /// <summary>
        /// On/Off 토글
        /// </summary>
        public static void Toggle()
        {
            IsEnabled = !IsEnabled;
        }

        /// <summary>
        /// 오버레이 새로고침 (Prefab Stage 변경 시)
        /// </summary>
        public static void Refresh()
        {
            UpdateOverlay();
        }

        #endregion

        #region Prefab Stage Events

        private static void OnPrefabStageOpened(PrefabStage stage)
        {
            if (_isEnabled && _overlayTexture != null)
            {
                // 약간의 딜레이 후 생성 (Stage가 완전히 로드된 후)
                EditorApplication.delayCall += () =>
                {
                    if (stage != null && stage.prefabContentsRoot != null)
                    {
                        CreateOverlayInPrefab(stage.prefabContentsRoot);
                    }
                };
            }
        }

        private static void OnPrefabStageClosing(PrefabStage stage)
        {
            DestroyOverlayObject();
        }

        #endregion

        #region Private Methods

        private static bool LoadImageInternal(string imagePath)
        {
            try
            {
                // 기존 텍스처 해제
                if (_overlayTexture != null)
                {
                    Object.DestroyImmediate(_overlayTexture);
                    _overlayTexture = null;
                }

                // 이미지 로드
                var imageBytes = File.ReadAllBytes(imagePath);
                _overlayTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                if (!_overlayTexture.LoadImage(imageBytes))
                {
                    Debug.LogError($"[UIOverlayManager] 이미지 로드 실패: {imagePath}");
                    Object.DestroyImmediate(_overlayTexture);
                    _overlayTexture = null;
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[UIOverlayManager] 이미지 로드 중 오류: {ex.Message}");
                return false;
            }
        }

        private static void UpdateOverlay()
        {
            if (_isEnabled && _overlayTexture != null)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null)
                {
                    CreateOverlayInPrefab(stage.prefabContentsRoot);
                }

                // UI Builder가 열려 있으면 panel-root에 VisualElement 오버레이 부착
                var builderWindow = FindUIBuilderWindow();
                var panelRoot = FindBuilderPanelRoot(builderWindow);
                if (panelRoot != null)
                {
                    CreateToolkitOverlay(panelRoot);
                }
                else
                {
                    DestroyToolkitOverlay();
                }
            }
            else
            {
                DestroyOverlayObject();
                DestroyToolkitOverlay();
            }

            SceneView.RepaintAll();
        }

        private static void UpdateOpacity()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = _opacity;
            }
            if (_toolkitOverlayElement != null)
            {
                _toolkitOverlayElement.style.opacity = _opacity;
            }
            SceneView.RepaintAll();
        }

        private static void CreateOverlayInPrefab(GameObject prefabRoot)
        {
            if (prefabRoot == null || _overlayTexture == null) return;

            // 기존 오버레이 제거
            DestroyOverlayObject();

            // 루트 캔버스 찾기
            var canvas = prefabRoot.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = prefabRoot.GetComponentInChildren<Canvas>();
            }

            if (canvas == null)
            {
                Debug.LogWarning("[UIOverlayManager] 프리팹에서 Canvas를 찾을 수 없습니다.");
                return;
            }

            // 캔버스의 RectTransform 크기 가져오기
            var canvasRect = canvas.GetComponent<RectTransform>();
            Vector2 canvasSize = canvasRect.sizeDelta;

            // 오버레이 GameObject 생성
            _overlayObject = new GameObject(OVERLAY_OBJECT_NAME);
            _overlayObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            _overlayObject.transform.SetParent(canvas.transform, false);

            // 맨 위에 표시되도록 sibling index 설정
            _overlayObject.transform.SetAsLastSibling();

            // RectTransform 설정 - 캔버스 전체 크기
            var rectTransform = _overlayObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;
            rectTransform.localPosition = Vector3.zero;

            // CanvasGroup - 투명도 조절 및 상호작용 차단
            _canvasGroup = _overlayObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = _opacity;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            // RawImage
            _overlayImage = _overlayObject.AddComponent<RawImage>();
            _overlayImage.texture = _overlayTexture;
            _overlayImage.raycastTarget = false;

            Debug.Log($"[UIOverlayManager] 오버레이 생성 완료 - 캔버스: {canvas.name}, 크기: {canvasSize}");
        }

        private static void DestroyOverlayObject()
        {
            if (_overlayObject != null)
            {
                Object.DestroyImmediate(_overlayObject);
                _overlayObject = null;
                _overlayImage = null;
                _canvasGroup = null;
            }

            // 혹시 남아있는 오버레이 오브젝트도 정리
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
            {
                var existingOverlay = stage.prefabContentsRoot.transform.Find(OVERLAY_OBJECT_NAME);
                if (existingOverlay != null)
                {
                    Object.DestroyImmediate(existingOverlay.gameObject);
                }

                // Canvas 아래에서도 찾기
                var canvas = stage.prefabContentsRoot.GetComponentInChildren<Canvas>();
                if (canvas != null)
                {
                    var overlayInCanvas = canvas.transform.Find(OVERLAY_OBJECT_NAME);
                    if (overlayInCanvas != null)
                    {
                        Object.DestroyImmediate(overlayInCanvas.gameObject);
                    }
                }
            }
        }

        #endregion

        #region UI Toolkit (UI Builder) Overlay

        /// <summary>
        /// 현재 열려있는 UI Builder EditorWindow를 찾는다.
        /// (UIBuilderTabPreviewWindow / UIBuilderDesignPreviewToggle와 동일한 패턴)
        /// </summary>
        private static EditorWindow FindUIBuilderWindow()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && w.GetType().FullName == UI_BUILDER_TYPE_FULL_NAME)
                    return w;
            }
            return null;
        }

        /// <summary>
        /// UI Builder 윈도우 내부의 panel-root VisualElement를 가져온다.
        /// (UIBuilderTabPreviewOverlay와 동일한 패턴)
        /// </summary>
        private static VisualElement FindBuilderPanelRoot(EditorWindow builderWindow)
        {
            if (builderWindow == null) return null;
            return builderWindow.rootVisualElement?.Q<VisualElement>("panel-root");
        }

        /// <summary>
        /// panel-root에 VisualElement 오버레이를 생성/부착한다.
        /// 이미 같은 panel-root에 동일 이름의 자식이 있으면 먼저 제거한다.
        /// </summary>
        private static void CreateToolkitOverlay(VisualElement panelRoot)
        {
            if (panelRoot == null || _overlayTexture == null) return;

            // 이미 같은 panel-root에 정상 부착되어 있으면 갱신만
            if (_toolkitOverlayElement != null
                && _trackedPanelRoot == panelRoot
                && _toolkitOverlayElement.parent == panelRoot)
            {
                _toolkitOverlayElement.style.backgroundImage = new StyleBackground(_overlayTexture);
                _toolkitOverlayElement.style.opacity = _opacity;
                return;
            }

            // 다른 panel-root에 부착돼 있던 element는 제거
            DestroyToolkitOverlay();

            // 같은 이름의 잔존 자식이 있으면 제거 (도메인 리로드 등)
            var existing = panelRoot.Q<VisualElement>(OVERLAY_ELEMENT_NAME);
            if (existing != null)
            {
                existing.RemoveFromHierarchy();
            }

            var element = new VisualElement
            {
                name = OVERLAY_ELEMENT_NAME,
                pickingMode = PickingMode.Ignore,
            };
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.right = 0;
            element.style.top = 0;
            element.style.bottom = 0;
            element.style.backgroundImage = new StyleBackground(_overlayTexture);
            element.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            element.style.opacity = _opacity;

            panelRoot.Add(element); // 마지막 자식이라 z-order 최상단

            _toolkitOverlayElement = element;
            _trackedPanelRoot = panelRoot;

            Debug.Log($"[UIOverlayManager] UI Toolkit 오버레이 부착 - panel-root size: {panelRoot.resolvedStyle.width}x{panelRoot.resolvedStyle.height}");
        }

        /// <summary>
        /// UI Toolkit 오버레이를 제거한다.
        /// </summary>
        private static void DestroyToolkitOverlay()
        {
            if (_toolkitOverlayElement != null)
            {
                _toolkitOverlayElement.RemoveFromHierarchy();
                _toolkitOverlayElement = null;
            }
            _trackedPanelRoot = null;
        }

        /// <summary>
        /// EditorApplication.update 폴링 루프.
        /// UI Builder 창 open/close 이벤트가 없으므로 0.5초마다 panel-root를 점검해 부착/재부착/해제한다.
        /// </summary>
        private static void ToolkitTick()
        {
            if (EditorApplication.timeSinceStartup < _nextToolkitTick) return;
            _nextToolkitTick = EditorApplication.timeSinceStartup + TOOLKIT_TICK_INTERVAL;

            if (!_isEnabled || _overlayTexture == null)
            {
                if (_toolkitOverlayElement != null)
                    DestroyToolkitOverlay();
                return;
            }

            var builderWindow = FindUIBuilderWindow();
            if (builderWindow == null)
            {
                // UI Builder 창이 닫힘
                if (_toolkitOverlayElement != null)
                    DestroyToolkitOverlay();
                return;
            }

            var panelRoot = FindBuilderPanelRoot(builderWindow);
            if (panelRoot == null)
            {
                if (_toolkitOverlayElement != null)
                    DestroyToolkitOverlay();
                return;
            }

            // panel-root가 새로 교체되었거나(UXML 새로 열기), 우리 element가 부모 트리를 잃었으면 재부착
            if (_trackedPanelRoot != panelRoot
                || _toolkitOverlayElement == null
                || _toolkitOverlayElement.parent != panelRoot)
            {
                CreateToolkitOverlay(panelRoot);
            }
        }

        #endregion
    }
}
