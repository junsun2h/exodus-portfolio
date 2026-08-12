// UI 평가용 경량 요약 데이터 추출기
// 전체 프리팹 구조 대신 평가 기준에 필요한 데이터만 추출
// 출력: ~100-200줄 JSON (vs GetPrefabStructureAsJson의 수만 줄)

using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PX.UIAutomation
{
    public static class UIEvaluationSummary
    {
        /// <summary>
        /// 평가용 요약 JSON 반환
        /// UniCli: unicli eval 'Debug.LogError(PX.UIAutomation.UIEvaluationSummary.Generate("Assets/...prefab"))' --json
        /// </summary>
        public static string Generate(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return $"{{\"error\": \"프리팹을 찾을 수 없습니다: {prefabPath}\"}}";

            var summary = new EvalSummary
            {
                Mode = "prefab",
                PrefabPath = prefabPath,
                RootName = prefab.name,
                Overview = BuildOverview(prefab),
                DynamicContainers = CollectDynamicContainers(prefab),
                Buttons = CollectButtons(prefab),
                TextHierarchy = CollectTextHierarchy(prefab),
                TouchTargets = CollectSmallTouchTargets(prefab),
                LayoutStructure = BuildLayoutStructure(prefab),
                Stats = BuildStats(prefab)
            };

            return Newtonsoft.Json.JsonConvert.SerializeObject(summary, Newtonsoft.Json.Formatting.Indented,
                new Newtonsoft.Json.JsonSerializerSettings
                {
                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                    DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore
                });
        }

        /// <summary>
        /// 스크린샷 + 요약을 한번에 생성
        /// </summary>
        public static string CaptureAndSummarize(string prefabPath, string screenshotDir)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string prefabName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
            string screenshotPath = $"{screenshotDir}/{prefabName}_{timestamp}.png";

            bool captured = UIPrefabScreenshotCapture.Capture(prefabPath, screenshotPath);
            string summary = Generate(prefabPath);

            // 스크린샷 경로를 요약에 추가
            if (captured)
            {
                // JSON의 첫 번째 { 뒤에 screenshotPath 필드 삽입
                summary = summary.Insert(1, $"\n  \"ScreenshotPath\": \"{screenshotPath.Replace("\\", "/")}\",");
            }

            return summary;
        }

        /// <summary>
        /// Runtime 모드: Play Mode에서 활성화된 팝업을 평가
        /// UniCli: return PX.UIAutomation.UIEvaluationSummary.GenerateRuntime("PXPopup_EquipmentHUD");
        /// </summary>
        public static string GenerateRuntime(string popupName)
        {
            if (!Application.isPlaying)
                return "{\"error\": \"Play Mode에서만 사용 가능합니다.\"}";

            // 활성 팝업 찾기
            var allPopups = UnityEngine.Object.FindObjectsOfType<PX.UIPopup>(true);
            GameObject target = null;

            foreach (var popup in allPopups)
            {
                if (popup.gameObject.name.Contains(popupName) && popup.gameObject.activeInHierarchy)
                {
                    target = popup.gameObject;
                    break;
                }
            }

            if (target == null)
                return $"{{\"error\": \"활성 팝업을 찾을 수 없습니다: {popupName}\"}}";

            var summary = new EvalSummary
            {
                Mode = "runtime",
                RootName = target.name,
                Overview = BuildOverviewRuntime(target),
                Buttons = CollectButtons(target),
                TextHierarchy = CollectTextHierarchy(target),
                TouchTargets = CollectSmallTouchTargets(target),
                LayoutStructure = BuildLayoutStructure(target),
                Stats = BuildStats(target)
            };

            // 런타임에서는 프리팹 경로 추출 시도
            var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(target);
            if (prefabSource != null)
                summary.PrefabPath = AssetDatabase.GetAssetPath(prefabSource);

            return Newtonsoft.Json.JsonConvert.SerializeObject(summary, Newtonsoft.Json.Formatting.Indented,
                new Newtonsoft.Json.JsonSerializerSettings
                {
                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                    DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore
                });
        }

        /// <summary>
        /// Runtime 모드: Game View 스크린샷 + 요약
        /// </summary>
        public static string CaptureRuntimeAndSummarize(string popupName, string screenshotDir)
        {
            if (!Application.isPlaying)
                return "{\"error\": \"Play Mode에서만 사용 가능합니다.\"}";

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string screenshotPath = $"{screenshotDir}/{popupName}_{timestamp}.png";

            // 디렉토리 생성
            var dir = System.IO.Path.GetDirectoryName(screenshotPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            ScreenCapture.CaptureScreenshot(screenshotPath);

            string summary = GenerateRuntime(popupName);
            summary = summary.Insert(1, $"\n  \"ScreenshotPath\": \"{screenshotPath.Replace("\\", "/")}\",");

            return summary;
        }

        #region Overview — 1단계 자식 구조 (전체 파악용)

        private static List<OverviewNode> BuildOverview(GameObject prefab)
        {
            var result = new List<OverviewNode>();

            foreach (Transform child in prefab.transform)
            {
                var node = new OverviewNode
                {
                    Name = child.name,
                    Active = child.gameObject.activeSelf,
                    ChildCount = child.childCount
                };

                // Nested Prefab 여부
                if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                {
                    var nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(child.gameObject);
                    if (nearestRoot == child.gameObject)
                    {
                        node.NestedPrefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                    }
                }

                // RectTransform 요약 (앵커 타입)
                var rect = child as RectTransform;
                if (rect != null)
                {
                    node.AnchorType = GetAnchorType(rect);
                    if (rect.sizeDelta != Vector2.zero)
                        node.Size = $"{rect.sizeDelta.x:F0}x{rect.sizeDelta.y:F0}";
                }

                // 주요 컴포넌트 타입
                var key = GetKeyComponent(child);
                if (key != null) node.Type = key;

                result.Add(node);
            }

            return result;
        }

        private static string GetAnchorType(RectTransform rect)
        {
            bool stretchH = Mathf.Abs(rect.anchorMin.x - 0f) < 0.01f && Mathf.Abs(rect.anchorMax.x - 1f) < 0.01f;
            bool stretchV = Mathf.Abs(rect.anchorMin.y - 0f) < 0.01f && Mathf.Abs(rect.anchorMax.y - 1f) < 0.01f;

            if (stretchH && stretchV) return "stretch-both";
            if (stretchH) return "stretch-h";
            if (stretchV) return "stretch-v";

            float anchorY = (rect.anchorMin.y + rect.anchorMax.y) / 2f;
            if (anchorY > 0.8f) return "top";
            if (anchorY < 0.2f) return "bottom";
            return "center";
        }

        private static string GetKeyComponent(Transform t)
        {
            if (t.GetComponent<ScrollRect>()) return "ScrollRect";
            if (t.GetComponent<LayoutGroup>()) return "LayoutGroup";
            if (t.GetComponent<Button>()) return "Button";
            if (t.GetComponent<Toggle>()) return "Toggle";
            if (t.GetComponent<TMP_InputField>()) return "InputField";

            var mb = t.GetComponents<MonoBehaviour>()
                .FirstOrDefault(m => m != null && (m.GetType().Namespace == null || !m.GetType().Namespace.StartsWith("UnityEngine")));
            return mb?.GetType().Name;
        }

        #endregion

        #region Runtime Overview — 런타임 모드 전용

        /// <summary>
        /// Runtime 모드 Overview: 활성 상태 기반
        /// </summary>
        private static List<OverviewNode> BuildOverviewRuntime(GameObject root)
        {
            var result = new List<OverviewNode>();

            foreach (Transform child in root.transform)
            {
                var node = new OverviewNode
                {
                    Name = child.name,
                    Active = child.gameObject.activeSelf,
                    ChildCount = CountActiveChildren(child)
                };

                var rect = child as RectTransform;
                if (rect != null)
                {
                    node.AnchorType = GetAnchorType(rect);
                    if (rect.sizeDelta != Vector2.zero)
                        node.Size = $"{rect.sizeDelta.x:F0}x{rect.sizeDelta.y:F0}";
                }

                var key = GetKeyComponent(child);
                if (key != null) node.Type = key;

                result.Add(node);
            }

            return result;
        }

        private static int CountActiveChildren(Transform t)
        {
            int count = 0;
            foreach (Transform child in t)
            {
                if (child.gameObject.activeSelf) count++;
            }
            return count;
        }

        #endregion

        #region DynamicContainers — 동적 컨테이너 감지 (Prefab 모드용)

        private static List<DynamicContainerInfo> CollectDynamicContainers(GameObject prefab)
        {
            var detector = new DynamicContainerDetector();
            var result = new List<DynamicContainerInfo>();

            // 모든 자식에서 동적 컨테이너 탐색
            var allTransforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                var info = detector.Detect(t.gameObject);
                if (info != null)
                {
                    result.Add(new DynamicContainerInfo
                    {
                        Path = GetRelativePath(prefab.transform, t),
                        ContainerType = info.ContainerType,
                        ItemPrefab = info.ItemPrefabPath
                    });
                }
            }

            return result.Count > 0 ? result : null;
        }

        #endregion

        #region Buttons — 색상/크기/위치 감사

        private static List<ButtonInfo> CollectButtons(GameObject prefab)
        {
            var buttons = prefab.GetComponentsInChildren<Button>(true);
            var rawList = new List<ButtonInfo>();

            foreach (var btn in buttons)
            {
                var rect = btn.GetComponent<RectTransform>();
                var image = btn.GetComponent<Image>();
                var text = btn.GetComponentInChildren<TextMeshProUGUI>(true);

                var info = new ButtonInfo
                {
                    Path = GetRelativePath(prefab.transform, btn.transform),
                    Interactable = btn.interactable
                };

                if (rect != null)
                {
                    var actualSize = GetActualSize(rect);
                    info.Size = $"{actualSize.x:F0}x{actualSize.y:F0}";
                    info.ScreenZone = GetScreenZone(rect, prefab.transform as RectTransform);
                }

                if (image != null)
                    info.BgColor = ColorToHex(image.color);

                if (text != null)
                {
                    info.TextColor = ColorToHex(text.color);
                    info.FontSize = text.fontSize;
                    info.Label = text.text;
                }

                rawList.Add(info);
            }

            // 동일 패턴 버튼 그룹화 (이름+크기+색상이 같으면 한 줄로 요약)
            var result = new List<ButtonInfo>();
            var grouped = rawList.GroupBy(b => $"{b.Path?.Split('/').LastOrDefault()}|{b.Size}|{b.BgColor}");
            foreach (var group in grouped)
            {
                var first = group.First();
                if (group.Count() > 1)
                {
                    first.RepeatCount = group.Count();
                    first.Path = first.Path + $" (x{group.Count()})";
                }
                result.Add(first);
            }

            return result;
        }

        private static string GetScreenZone(RectTransform target, RectTransform root)
        {
            // 앵커 기반 대략적 위치 판단
            float y = (target.anchorMin.y + target.anchorMax.y) / 2f;

            // 부모 체인을 통해 상대 위치 보정
            var parent = target.parent as RectTransform;
            while (parent != null && parent != root)
            {
                float parentY = (parent.anchorMin.y + parent.anchorMax.y) / 2f;
                y = parentY + (y - 0.5f) * 0.5f; // 대략적 보정
                parent = parent.parent as RectTransform;
            }

            if (y > 0.66f) return "top";
            if (y > 0.33f) return "middle";
            return "bottom";
        }

        #endregion

        #region TextHierarchy — 폰트 크기 계층

        private static List<TextInfo> CollectTextHierarchy(GameObject prefab)
        {
            var texts = prefab.GetComponentsInChildren<TextMeshProUGUI>(true);
            var result = new List<TextInfo>();

            // 폰트 크기별로 그룹화하여 요약
            var groups = texts
                .Where(t => t != null)
                .GroupBy(t => Mathf.Round(t.fontSize))
                .OrderByDescending(g => g.Key);

            foreach (var group in groups)
            {
                var samples = group.Take(3).Select(t => new TextSample
                {
                    Path = GetRelativePath(prefab.transform, t.transform),
                    Color = ColorToHex(t.color),
                    Preview = string.IsNullOrEmpty(t.text) ? "(empty)" :
                        t.text.Length > 20 ? t.text.Substring(0, 20) + "..." : t.text
                }).ToList();

                result.Add(new TextInfo
                {
                    FontSize = group.Key,
                    Count = group.Count(),
                    Samples = samples
                });
            }

            return result;
        }

        #endregion

        #region TouchTargets — 44px 미만 인터랙티브 요소 (문제만 보고)

        private static List<TouchIssue> CollectSmallTouchTargets(GameObject prefab)
        {
            var result = new List<TouchIssue>();
            const float MIN_TOUCH = 44f;

            var selectables = prefab.GetComponentsInChildren<Selectable>(true);
            foreach (var sel in selectables)
            {
                var rect = sel.GetComponent<RectTransform>();
                if (rect == null) continue;

                float w = rect.sizeDelta.x;
                float h = rect.sizeDelta.y;

                // stretch 앵커인 경우 sizeDelta가 0이거나 음수일 수 있으므로 건너뜀
                bool stretchH = Mathf.Abs(rect.anchorMin.x - rect.anchorMax.x) > 0.1f;
                bool stretchV = Mathf.Abs(rect.anchorMin.y - rect.anchorMax.y) > 0.1f;

                if (stretchH && stretchV) continue;
                if (stretchH && h >= MIN_TOUCH) continue;
                if (stretchV && w >= MIN_TOUCH) continue;

                if ((!stretchH && w > 0 && w < MIN_TOUCH) || (!stretchV && h > 0 && h < MIN_TOUCH))
                {
                    result.Add(new TouchIssue
                    {
                        Path = GetRelativePath(prefab.transform, sel.transform),
                        ActualSize = $"{w:F0}x{h:F0}",
                        Type = sel.GetType().Name
                    });
                }
            }

            return result;
        }

        #endregion

        #region LayoutStructure — 계층 깊이별 요약

        private static LayoutInfo BuildLayoutStructure(GameObject prefab)
        {
            var info = new LayoutInfo();

            // 최대 깊이 계산
            info.MaxDepth = GetMaxDepth(prefab.transform, 0);

            // LayoutGroup 사용 현황
            var layoutGroups = prefab.GetComponentsInChildren<LayoutGroup>(true);
            info.LayoutGroupCount = layoutGroups.Length;

            // ScrollRect 목록
            var scrolls = prefab.GetComponentsInChildren<ScrollRect>(true);
            if (scrolls.Length > 0)
            {
                info.ScrollRects = scrolls.Select(s =>
                    GetRelativePath(prefab.transform, s.transform)).ToList();
            }

            return info;
        }

        private static int GetMaxDepth(Transform t, int current)
        {
            int max = current;
            foreach (Transform child in t)
            {
                max = Mathf.Max(max, GetMaxDepth(child, current + 1));
            }
            return max;
        }

        #endregion

        #region Stats — 전체 통계

        private static EvalStats BuildStats(GameObject prefab)
        {
            var all = prefab.GetComponentsInChildren<Transform>(true);
            var buttons = prefab.GetComponentsInChildren<Button>(true);
            var images = prefab.GetComponentsInChildren<Image>(true);
            var texts = prefab.GetComponentsInChildren<TextMeshProUGUI>(true);

            return new EvalStats
            {
                TotalElements = all.Length,
                ButtonCount = buttons.Length,
                ImageCount = images.Length,
                TextCount = texts.Length,
                ActiveElements = all.Count(t => t.gameObject.activeInHierarchy),
                InactiveElements = all.Count(t => !t.gameObject.activeInHierarchy)
            };
        }

        #endregion

        #region 유틸리티

        private static string GetRelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();

            // 4단계 이상이면 처음 2개 + 마지막 1개로 축약
            if (parts.Count > 3)
            {
                return $"{parts[0]}/{parts[1]}/.../{parts[parts.Count - 1]}";
            }
            return string.Join("/", parts);
        }

        /// <summary>
        /// stretch anchor를 고려한 실제 크기 계산
        /// Runtime: rect.rect (레이아웃 완료 후 실제 크기)
        /// Prefab: stretch이면 부모 sizeDelta 기반 추정
        /// </summary>
        private static Vector2 GetActualSize(RectTransform rect)
        {
            // Runtime에서는 rect.rect가 정확한 렌더링 크기
            if (Application.isPlaying && rect.rect.width > 0 && rect.rect.height > 0)
                return new Vector2(rect.rect.width, rect.rect.height);

            bool stretchH = Mathf.Abs(rect.anchorMax.x - rect.anchorMin.x) > 0.01f;
            bool stretchV = Mathf.Abs(rect.anchorMax.y - rect.anchorMin.y) > 0.01f;

            if (!stretchH && !stretchV)
                return rect.sizeDelta;

            // stretch일 때 부모 크기를 기반으로 추정
            var parent = rect.parent as RectTransform;
            Vector2 parentSize = parent != null ? GetActualSize(parent) : new Vector2(1920, 1080);

            float w = stretchH ? parentSize.x * (rect.anchorMax.x - rect.anchorMin.x) + rect.sizeDelta.x : rect.sizeDelta.x;
            float h = stretchV ? parentSize.y * (rect.anchorMax.y - rect.anchorMin.y) + rect.sizeDelta.y : rect.sizeDelta.y;

            return new Vector2(Mathf.Max(0, w), Mathf.Max(0, h));
        }

        private static string ColorToHex(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }

        #endregion

        #region 데이터 클래스

        [Serializable]
        public class EvalSummary
        {
            public string Mode;
            public string PrefabPath;
            public string RootName;
            public List<OverviewNode> Overview;
            public List<DynamicContainerInfo> DynamicContainers;
            public List<ButtonInfo> Buttons;
            public List<TextInfo> TextHierarchy;
            public List<TouchIssue> TouchTargets;
            public LayoutInfo LayoutStructure;
            public EvalStats Stats;
        }

        [Serializable]
        public class DynamicContainerInfo
        {
            public string Path;
            public string ContainerType;
            public string ItemPrefab;
        }

        [Serializable]
        public class OverviewNode
        {
            public string Name;
            public string Type;
            public string AnchorType;
            public string Size;
            public int ChildCount;
            [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
            public bool Active;
            public string NestedPrefab;
        }

        [Serializable]
        public class ButtonInfo
        {
            public string Path;
            public string Size;
            public string ScreenZone;
            public string BgColor;
            public string TextColor;
            public float FontSize;
            public string Label;
            [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
            public bool Interactable;
            [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
            public int RepeatCount;
        }

        [Serializable]
        public class TextInfo
        {
            public float FontSize;
            public int Count;
            public List<TextSample> Samples;
        }

        [Serializable]
        public class TextSample
        {
            public string Path;
            public string Color;
            public string Preview;
        }

        [Serializable]
        public class TouchIssue
        {
            public string Path;
            public string ActualSize;
            public string Type;
        }

        [Serializable]
        public class LayoutInfo
        {
            public int MaxDepth;
            public int LayoutGroupCount;
            public List<string> ScrollRects;
        }

        [Serializable]
        public class EvalStats
        {
            public int TotalElements;
            public int ButtonCount;
            public int ImageCount;
            public int TextCount;
            public int ActiveElements;
            public int InactiveElements;
        }

        #endregion
    }
}
