using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PX.Editor
{
    /// <summary>
    /// UI Builder의 디자인 프리뷰(.design-preview) 토글 + 캡처 도구.
    ///
    /// 기능:
    /// - 프리뷰 ON/OFF: UI Builder에서 .design-preview 요소 표시/숨김
    /// - 캡처: UI Builder 뷰포트 영역을 PNG로 저장 (ui-evaluate에 전달용)
    ///
    /// 사용법:
    /// 1. 메뉴: PX Editor > UI Toolkit > Design Preview
    /// 2. UI Builder 옆에 도킹
    /// 3. 프리뷰 ON 상태에서 원하는 UXML을 띄운 뒤 [캡처] 클릭
    /// </summary>
    public class UIBuilderDesignPreviewToggle : EditorWindow
    {
        const string CaptureDir = "Assets/Editor/UIAutomation/Data/Screenshot/Evaluate/UXML";

        bool _isPreviewVisible = true;
        EditorWindow _cachedBuilderWindow;
        int _previewCount;
        string _lastCapturePath;

        [MenuItem("PX Editor/UI Toolkit/Design Preview")]
        static void ShowWindow()
        {
            var window = GetWindow<UIBuilderDesignPreviewToggle>();
            window.titleContent = new GUIContent("Design Preview");
            window.minSize = new Vector2(280, 60);
            window.maxSize = new Vector2(600, 60);
            window.Show();
        }

        EditorWindow FindUIBuilderWindow()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && w.GetType().FullName == "Unity.UI.Builder.Builder")
                    return w;
            }
            return null;
        }

        List<VisualElement> FindPreviewElements()
        {
            if (_cachedBuilderWindow == null)
                _cachedBuilderWindow = FindUIBuilderWindow();

            if (_cachedBuilderWindow == null)
                return new List<VisualElement>();

            var root = _cachedBuilderWindow.rootVisualElement;
            if (root == null)
                return new List<VisualElement>();

            return root.Query(className: "design-preview").ToList();
        }

        void ApplyVisibility(bool visible)
        {
            _isPreviewVisible = visible;
            var elements = FindPreviewElements();
            _previewCount = elements.Count;

            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var el in elements)
            {
                el.style.display = display;
            }
        }

        /// <summary>
        /// UI Builder 뷰포트 영역을 캡처해 PNG로 저장.
        /// 뷰포트 element를 찾지 못하면 UI Builder 윈도우 전체를 캡처.
        /// </summary>
        void Capture()
        {
            if (_cachedBuilderWindow == null)
                _cachedBuilderWindow = FindUIBuilderWindow();

            if (_cachedBuilderWindow == null)
            {
                Debug.LogError("[DesignPreview] UI Builder 창을 찾을 수 없습니다.");
                return;
            }

            // UI Builder를 포커스해서 화면에 그려지게 함
            _cachedBuilderWindow.Focus();
            _cachedBuilderWindow.Repaint();

            // 캡처는 다음 GUI 프레임에서 수행
            EditorApplication.delayCall += DoCapture;
        }

        void DoCapture()
        {
            if (_cachedBuilderWindow == null) return;

            var winPos = _cachedBuilderWindow.position; // OS 가상 화면 좌표

            // 뷰포트 element 찾기 (실패 시 전체 윈도우)
            Rect captureRect = winPos;
            var viewport = FindViewportElement(_cachedBuilderWindow);
            if (viewport != null)
            {
                var local = viewport.worldBound; // EditorWindow 내부 좌표
                captureRect = new Rect(
                    winPos.x + local.x,
                    winPos.y + local.y,
                    local.width,
                    local.height
                );
            }

            int w = Mathf.Max(1, Mathf.RoundToInt(captureRect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(captureRect.height));

            try
            {
                // InternalEditorUtility.ReadScreenPixel: OS 화면에서 픽셀 읽기
                var pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                    new Vector2(captureRect.x, captureRect.y), w, h);

                if (pixels == null || pixels.Length == 0)
                {
                    Debug.LogError("[DesignPreview] ReadScreenPixel 실패 (null/empty)");
                    return;
                }

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels(pixels);
                tex.Apply();

                Directory.CreateDirectory(CaptureDir);
                var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var docName = TryGetDocumentName(_cachedBuilderWindow) ?? "UXML";
                var path = $"{CaptureDir}/{docName}_{timestamp}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);

                _lastCapturePath = path;
                AssetDatabase.Refresh();
                Debug.Log($"[DesignPreview] 캡처 완료: {path} ({w}x{h})");
                Repaint();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DesignPreview] 캡처 실패: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// UI Builder 내부의 뷰포트 element 찾기.
        /// 클래스명 후보를 순서대로 시도.
        /// </summary>
        VisualElement FindViewportElement(EditorWindow builder)
        {
            var root = builder.rootVisualElement;
            if (root == null) return null;

            string[] candidates = new[]
            {
                "unity-builder-viewport",
                "unity-builder-canvas",
                "unity-builder-viewport__container",
            };

            foreach (var cls in candidates)
            {
                var found = root.Q(className: cls);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// UI Builder에 로드된 UXML 파일명을 reflection으로 가져온다.
        /// </summary>
        string TryGetDocumentName(EditorWindow builder)
        {
            try
            {
                var t = builder.GetType();
                var docProp = t.GetProperty("document", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (docProp == null) return null;
                var doc = docProp.GetValue(builder);
                if (doc == null) return null;

                var docType = doc.GetType();
                var fileNameProp = docType.GetProperty("uxmlFileName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fileNameProp != null)
                {
                    var name = fileNameProp.GetValue(doc) as string;
                    if (!string.IsNullOrEmpty(name))
                        return Path.GetFileNameWithoutExtension(name);
                }
            }
            catch { }
            return null;
        }

        void OnGUI()
        {
            if (_cachedBuilderWindow == null)
                _cachedBuilderWindow = FindUIBuilderWindow();

            if (_cachedBuilderWindow == null)
            {
                EditorGUILayout.HelpBox("UI Builder를 열어주세요", MessageType.Info);
                return;
            }

            // 1행: 토글 + 카운트
            EditorGUILayout.BeginHorizontal();
            var bgColor = _isPreviewVisible
                ? new Color(0.2f, 0.7f, 0.4f)
                : new Color(0.5f, 0.5f, 0.5f);
            var label = _isPreviewVisible ? "프리뷰 ON" : "프리뷰 OFF";

            GUI.backgroundColor = bgColor;
            if (GUILayout.Button(label, GUILayout.Height(24), GUILayout.Width(100)))
            {
                ApplyVisibility(!_isPreviewVisible);
            }
            GUI.backgroundColor = Color.white;

            var miniStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            EditorGUILayout.LabelField($".design-preview: {_previewCount}개", miniStyle);
            EditorGUILayout.EndHorizontal();

            // 2행: 캡처 + 결과 경로
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.85f);
            if (GUILayout.Button("캡처", GUILayout.Height(22), GUILayout.Width(100)))
            {
                Capture();
            }
            GUI.backgroundColor = Color.white;
            // 캡처 전 Fit viewport 누르라는 안내
            EditorGUILayout.LabelField("← 캡처 전 [Fit viewport] 한 번 누르세요", miniStyle);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            if (!string.IsNullOrEmpty(_lastCapturePath))
            {
                if (GUILayout.Button(Path.GetFileName(_lastCapturePath), miniStyle))
                {
                    EditorUtility.RevealInFinder(_lastCapturePath);
                }
            }
            else
            {
                EditorGUILayout.LabelField("(아직 캡처 없음)", miniStyle);
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
