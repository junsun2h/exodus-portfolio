using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PX.Editor.UIAutomation
{
    /// <summary>
    /// UXML의 <c>.design-preview</c> 상태를 <b>headless</b>로 PNG 렌더하는 static 유틸.
    ///
    /// 배경(R5b 픽셀/미적 검증):
    /// - UGUI→UXML 변환 표준 워크플로(ui-ugui-to-uxml)의 렌더-비교 루프 중,
    ///   색·그라데이션·질감·발광처럼 rect(숫자)로 안 잡히는 미적 요소를 비교하려면
    ///   실제 픽셀이 필요하다. 그 픽셀을 사람 개입(UI Builder 창 오픈/[캡처] 클릭) 없이
    ///   MCP execute_code 한 번으로 얻기 위한 인프라다.
    ///
    /// 동작 원리(방향 A — 임시 EditorWindow 호스트):
    /// - 순수 Edit 모드 인라인 오프스크린 렌더는 호스트(GUIView)가 없어 레이아웃은 계산되나
    ///   픽셀이 안 나온다(검정/단색). 임시 <see cref="EditorWindow"/>를 띄우면 실제 GUIView가
    ///   생겨 <see cref="UnityEngine.UIElements.Panel"/>이 유효한 RenderChain/device를 가진다.
    /// - 그 상태에서 RenderTexture를 active로 걸고 panel 내부 <c>UpdateForRepaint()</c>(레이아웃·스타일
    ///   동기 처리) → <c>Render()</c>(텍스트·이미지·solid 메시 flush)를 호출하면, OS 화면을
    ///   스크래핑하지 않고 RT에 직접 그려진다. 즉 창이 화면에 보이지 않아도 픽셀을 얻는다.
    ///
    /// 동기 1-스텝(★ MCP 표준 호출 패턴):
    /// - <c>UpdateForRepaint()</c>가 레이아웃을 같은 호출에서 동기 처리하므로 <c>delayCall</c>
    ///   1프레임 대기가 필요 없다. <see cref="Capture(string,string,int,int)"/> 한 번 호출이
    ///   반환되는 시점에 PNG가 이미 디스크에 존재한다.
    /// - 기존 화면 스크래핑 방식(UIBuilderDesignPreviewToggle)은 delayCall 때문에 "Capture 트리거 →
    ///   다음 호출에서 PNG 확인"이라는 2-스텝이 강제됐지만, 본 유틸은 그 제약이 없다. 폴링도 불필요.
    /// - MCP 사용 흐름(2개 도구 호출, 비동기 대기 없음):
    ///   <code>
    ///   // ① execute_code — 단일 동기 호출. 반환되면 PNG가 이미 존재한다.
    ///   bool ok = PX.Editor.UIAutomation.UxmlDesignPreviewCapture.Capture(
    ///       "Assets/GameAssets/UI/UIToolkit/Content/Lobby/TK_Lobby_Hud.uxml",
    ///       "Assets/Editor/UIAutomation/Data/Screenshot/Evaluate/UXML/TK_Lobby_Hud.png");
    ///   return ok;
    ///   // ② Read(vision) — 위 outPng를 그대로 읽어 시안과 색·그라데이션·질감을 비교한다.
    ///   </code>
    /// - outPng를 생략(null)하면 DefaultDir/{UXML이름}_{타임스탬프}.png로 저장되지만, 그 경우 반환
    ///   문자열에 실제 경로가 없으므로 MCP에서는 outPng를 명시해 ②에서 읽을 경로를 고정하는 것을 권장.
    ///
    /// 범위:
    /// - <c>.design-preview</c>(더미 데이터)가 보이는 상태 그대로 캡처한다. StripDesignPreview류를
    ///   호출하지 않는다. "design-preview ≠ 런타임" 괴리는 본 유틸 범위 밖이다.
    ///
    /// 참조: <c>Assets/Editor/Script/UIBuilderDesignPreviewToggle.cs</c>(기존 화면 스크래핑 방식),
    /// <c>Docs/plans/...R5b_Headless_DesignPreview_캡처_인프라.md</c>.
    /// 검증 환경: Unity 6000.3.14. 내부 API(EditorPanel.UpdateForRepaint/Panel.Render) 의존 —
    /// 버전 업 시 점검 포인트.
    /// </summary>
    public static class UxmlDesignPreviewCapture
    {
        /// <summary>기존 관행과 동일한 기본 저장 경로(UIBuilderDesignPreviewToggle.CaptureDir).</summary>
        public const string DefaultDir = "Assets/Editor/UIAutomation/Data/Screenshot/Evaluate/UXML";

        /// <summary>공통 클래스(letterbox 등)의 themeStyleSheet를 가져올 표준 PanelSettings 경로.</summary>
        public const string DefaultPanelSettingsPath = "Assets/GameAssets/UI/UIToolkit/PXPanelSettings.asset";

        /// <summary>임시 호스트 창 식별용 타이틀(잔존 창 정리/감지에 사용).</summary>
        public const string HostTitle = "__UxmlCaptureHost__";

        /// <summary>
        /// 기본 배경색. HUD 등 투명 배경 UXML도 밝은 텍스트가 보이도록 게임 내 유사 어두운 회색.
        /// 알파 보존(투명) 캡처가 필요하면 background에 alpha 0 색을 명시.
        /// </summary>
        public static readonly Color DefaultBackground = new Color(0.16f, 0.16f, 0.18f, 1f);

        // EditorPanel 내부 메서드 reflection 캐시
        static Type _cachedPanelType;
        static MethodInfo _miUpdateForRepaint;
        static MethodInfo _miRender;

        // PanelSettings.themeStyleSheet 캐시
        static StyleSheet _cachedTheme;
        static bool _themeResolved;

        /// <summary>
        /// UXML의 design-preview를 headless로 렌더해 <paramref name="outPng"/>에 PNG로 저장한다(동기).
        /// </summary>
        /// <param name="uxmlPath">대상 UXML 에셋 경로(예: Assets/.../TK_Lobby_Hud.uxml).</param>
        /// <param name="outPng">저장 경로. null/빈값이면 <see cref="DefaultDir"/>/{UXML이름}_{타임스탬프}.png 자동 생성.</param>
        /// <param name="width">렌더 가로(기본 1920 — PXPanelSettings referenceResolution).</param>
        /// <param name="height">렌더 세로(기본 1080).</param>
        /// <returns>성공 시 true. 반환 시점에 PNG가 디스크에 존재한다.</returns>
        public static bool Capture(string uxmlPath, string outPng = null, int width = 1920, int height = 1080)
            => Capture(uxmlPath, outPng, width, height, DefaultBackground);

        /// <summary>
        /// 배경색을 지정하는 오버로드. <paramref name="background"/>에 alpha 0 색을 주면 투명 캡처.
        /// </summary>
        public static bool Capture(string uxmlPath, string outPng, int width, int height, Color background)
        {
            if (string.IsNullOrEmpty(uxmlPath))
            {
                Debug.LogError("[UxmlCapture] uxmlPath가 비어있습니다.");
                return false;
            }

            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (vta == null)
            {
                Debug.LogError($"[UxmlCapture] VisualTreeAsset 로드 실패: {uxmlPath}");
                return false;
            }

            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            outPng = ResolveOutPath(uxmlPath, outPng);

            // 직전 실행에서 남았을 수 있는 잔존 호스트 정리(유령 창 방지)
            CloseStaleHosts();

            EditorWindow host = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            var prevActive = RenderTexture.active;
            var ok = false;
            try
            {
                // 1) 임시 호스트 창 — 실제 GUIView를 생성해 panel이 유효한 RenderChain을 갖게 한다.
                //    화면 밖 음수 좌표는 OS가 (0,0)으로 클램프하지만, Render()가 RT에 직접 그리므로
                //    창이 화면에 실제로 그려지기 전에 같은 호출 내에서 Close된다(노출 최소화).
                host = ScriptableObject.CreateInstance<EditorWindow>();
                host.titleContent = new GUIContent(HostTitle);
                host.ShowPopup();
                host.minSize = new Vector2(width, height);
                host.maxSize = new Vector2(width, height);
                host.position = new Rect(0, 0, width, height);

                // 2) PanelSettings의 theme(예: PXTheme)를 먼저 적용한다. letterbox-bg(position:absolute)·
                //    letterbox-content(1920×1080 고정)·top-bar 등 공통 클래스 스타일은 UXML의 <Style src>가
                //    아니라 PanelSettings.themeStyleSheet로 전역 적용된다. 이게 없으면 letterbox-bg의
                //    position:absolute조차 안 먹어 레이아웃이 무너진다(루트가 콘텐츠 크기로 줄거나 몰림).
                var root = host.rootVisualElement;
                ApplyTheme(root);

                // 2-1) UXML 클론 — <Style src> 체인(화면별 USS)이 적용된다. design-preview는 표시 상태 유지.
                //      theme의 letterbox-bg가 absolute로 패널을 채우므로 flexGrow 강제는 불필요.
                vta.CloneTree(root);

                // 2-2) 불투명 배경 요청 시: 콘텐츠 뒤에 풀스크린 배경 element를 깐다.
                //      갓 생성한 호스트 panel은 첫 Render에서 GL.Clear 배경을 흰색으로 덮어쓰는 현상이
                //      있어, 배경을 element로 직접 그리는 편이 색공간/타이밍에 무관하게 안정적이다.
                //      alpha 0(투명) 요청이면 깔지 않아 RT 알파가 그대로 보존된다.
                if (background.a > 0f)
                {
                    var bg = new VisualElement { name = "__capture_bg" };
                    bg.style.position = Position.Absolute;
                    bg.style.left = 0;
                    bg.style.top = 0;
                    bg.style.right = 0;
                    bg.style.bottom = 0;
                    bg.style.backgroundColor = background;
                    root.Insert(0, bg); // 맨 뒤(z-order 최하단)
                }

                var panel = root.panel;
                if (panel == null)
                {
                    Debug.LogError("[UxmlCapture] panel 생성 실패(호스트 attach 안 됨).");
                    return false;
                }

                if (!EnsurePanelMethods(panel.GetType()))
                {
                    Debug.LogError("[UxmlCapture] EditorPanel 내부 메서드(UpdateForRepaint/Render)를 찾지 못함 — Unity 버전 점검 필요.");
                    return false;
                }

                // 3) 레이아웃·스타일 동기 처리(이 호출이 끝나면 worldBound가 확정 → delayCall 불필요)
                _miUpdateForRepaint.Invoke(panel, null);

                // 4) RT로 직접 렌더(텍스트·이미지·solid 메시 모두 flush)
                //    ★ Linear RT 필수: 프로젝트가 Linear 색공간이라 UI Toolkit은 메시를 linear 색으로 그린다.
                //    sRGB RT로 받으면 linear→sRGB 이중 변환이 일어나 색이 밝아지고 채도가 빠진다(바램·흐림).
                //    Linear RT는 그 변환을 막아 실제 표시 색과 일치한다.
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                rt.Create();
                RenderTexture.active = rt;
                // RT를 투명으로 초기화 — 실제 배경색은 위 배경 element(__capture_bg)가 그린다.
                GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
                _miRender.Invoke(panel, null);

                // 5) RT → Texture2D → PNG
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                File.WriteAllBytes(outPng, tex.EncodeToPNG());
                ok = true;
                Debug.Log($"[UxmlCapture] 캡처 완료: {outPng} ({width}x{height})");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UxmlCapture] 캡처 실패: {e.Message}\n{e.StackTrace}");
                return false;
            }
            finally
            {
                // 6) 정리 — 실패 경로에서도 RT/Texture/창을 확실히 회수(유령 창 방지)
                RenderTexture.active = prevActive;
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (host != null) host.Close();

                // PNG는 파일시스템에 직접 기록됨(외부 Read/vision으로 바로 사용 가능).
                // AssetDatabase.Refresh()는 도메인 리로드/리임포트 비용 + MCP 세션 끊김 위험이 있어
                // 호출하지 않는다. Project 창에 에셋으로 노출이 필요하면 호출 측에서 명시적으로 Refresh.
                _ = ok;
            }
        }

        /// <summary>outPng가 비었으면 기본 경로/이름을 만들고, 어느 경우든 부모 디렉토리를 보장한다.</summary>
        static string ResolveOutPath(string uxmlPath, string outPng)
        {
            if (string.IsNullOrEmpty(outPng))
            {
                var name = Path.GetFileNameWithoutExtension(uxmlPath);
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                Directory.CreateDirectory(DefaultDir);
                return $"{DefaultDir}/{name}_{ts}.png";
            }

            var dir = Path.GetDirectoryName(outPng);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            return outPng;
        }

        /// <summary>
        /// 표준 PanelSettings의 themeStyleSheet(공통 클래스 USS — letterbox/top-bar 등)를 root에 적용.
        /// UXML의 <c>&lt;Style src&gt;</c>(화면별 USS)보다 먼저 Add해 화면별 스타일이 오버라이드되게 한다.
        /// </summary>
        static void ApplyTheme(VisualElement root)
        {
            if (!_themeResolved)
            {
                _themeResolved = true;
                var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(DefaultPanelSettingsPath);
                _cachedTheme = ps != null ? ps.themeStyleSheet : null;
                if (_cachedTheme == null)
                    Debug.LogWarning($"[UxmlCapture] PanelSettings/themeStyleSheet를 찾지 못함({DefaultPanelSettingsPath}) — " +
                                     "공통 클래스(letterbox 등) 미적용으로 레이아웃이 UI Builder와 다를 수 있음.");
            }
            if (_cachedTheme != null && !root.styleSheets.Contains(_cachedTheme))
                root.styleSheets.Add(_cachedTheme);
        }

        /// <summary>EditorPanel의 UpdateForRepaint()/Render()를 reflection으로 확보(타입별 캐시).</summary>
        static bool EnsurePanelMethods(Type panelType)
        {
            if (_cachedPanelType == panelType && _miUpdateForRepaint != null && _miRender != null)
                return true;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _miUpdateForRepaint = panelType.GetMethod("UpdateForRepaint", flags, null, Type.EmptyTypes, null);
            _miRender = panelType.GetMethod("Render", flags, null, Type.EmptyTypes, null);
            _cachedPanelType = panelType;
            return _miUpdateForRepaint != null && _miRender != null;
        }

        /// <summary>같은 타이틀의 잔존 호스트 창을 모두 닫는다.</summary>
        static void CloseStaleHosts()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && w.titleContent != null && w.titleContent.text == HostTitle)
                    w.Close();
            }
        }
    }
}
