// UI 자동화 시스템 - 프리팹 YAML 파서
// 프리팹의 구조를 분석하여 Claude Code가 이해할 수 있는 형태로 변환

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PX.UIAutomation
{
    /// <summary>
    /// 프리팹 YAML 파싱 유틸리티
    /// Claude Code가 프리팹 구조를 이해할 수 있도록 정보 제공
    /// </summary>
    public static class UIYamlParser
    {
        #region Public API

        /// <summary>
        /// 프리팹의 구조를 분석하여 텍스트로 반환
        /// Claude Code에서 이 결과를 사용하여 JSON 생성
        /// </summary>
        public static string ParsePrefabStructure(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return $"프리팹을 찾을 수 없습니다: {prefabPath}";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== 프리팹 구조 분석 ===");
            sb.AppendLine($"경로: {prefabPath}");
            sb.AppendLine($"루트 이름: {prefab.name}");
            sb.AppendLine();

            // 루트 스크립트 정보
            sb.AppendLine("=== 루트 스크립트 ===");
            ParseRootScript(prefab, sb);
            sb.AppendLine();

            // GameObject 계층 구조
            sb.AppendLine("=== GameObject 계층 ===");
            ParseGameObjectHierarchy(prefab.transform, sb, 0, "");
            sb.AppendLine();

            // SerializeField 바인딩 정보
            sb.AppendLine("=== SerializeField 바인딩 ===");
            ParseSerializeFields(prefab, sb);

            return sb.ToString();
        }

        /// <summary>
        /// 프리팹 구조를 JSON 스키마 형식으로 반환
        /// Claude Code가 직접 사용할 수 있는 형식
        /// </summary>
        public static string GetPrefabStructureAsJson(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return "{}";

            var structure = new PrefabStructureInfo
            {
                PrefabPath = prefabPath,
                RootName = prefab.name,
                Hierarchy = ParseHierarchyToList(prefab.transform, ""),
                Bindings = ParseBindingsToList(prefab)
            };

            return Newtonsoft.Json.JsonConvert.SerializeObject(structure, Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// GUID를 에셋 경로로 변환
        /// </summary>
        public static string ResolveGuidToPath(string guid)
        {
            return AssetDatabase.GUIDToAssetPath(guid);
        }

        /// <summary>
        /// 에셋 경로를 GUID로 변환
        /// </summary>
        public static string ResolvePathToGuid(string path)
        {
            return AssetDatabase.AssetPathToGUID(path);
        }

        /// <summary>
        /// 프리팹 내 모든 Nested Prefab의 원본 경로를 수집
        /// </summary>
        public static HashSet<string> CollectNestedPrefabPaths(string prefabPath)
        {
            var result = new HashSet<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return result;

            CollectNestedPrefabsRecursive(prefab.transform, prefabPath, result);
            return result;
        }

        private static void CollectNestedPrefabsRecursive(Transform transform, string rootPrefabPath, HashSet<string> collected)
        {
            // Nested Prefab 루트인지 확인
            if (PrefabUtility.IsPartOfPrefabInstance(transform.gameObject))
            {
                var nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(transform.gameObject);
                if (nearestRoot == transform.gameObject)
                {
                    string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject);
                    // 자기 자신(루트 프리팹)은 제외
                    if (!string.IsNullOrEmpty(sourcePath) && sourcePath != rootPrefabPath)
                    {
                        collected.Add(sourcePath);
                    }
                }
            }

            // 자식 순회
            foreach (Transform child in transform)
            {
                CollectNestedPrefabsRecursive(child, rootPrefabPath, collected);
            }
        }

        #endregion

        #region 내부 파싱 메서드

        private static void ParseRootScript(GameObject prefab, StringBuilder sb)
        {
            var monoBehaviours = prefab.GetComponents<MonoBehaviour>();
            foreach (var mb in monoBehaviours)
            {
                if (mb == null) continue;

                var type = mb.GetType();
                // Unity 기본 컴포넌트 제외
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine")) continue;

                var script = MonoScript.FromMonoBehaviour(mb);
                if (script != null)
                {
                    string scriptPath = AssetDatabase.GetAssetPath(script);
                    sb.AppendLine($"  클래스: {type.Name}");
                    sb.AppendLine($"  경로: {scriptPath}");
                    sb.AppendLine($"  베이스: {type.BaseType?.Name ?? "없음"}");
                }
            }
        }

        private static void ParseGameObjectHierarchy(Transform transform, StringBuilder sb, int depth, string path)
        {
            string indent = new string(' ', depth * 2);
            string currentPath = string.IsNullOrEmpty(path) ? transform.name : $"{path}/{transform.name}";

            sb.AppendLine($"{indent}[{transform.name}]");
            sb.AppendLine($"{indent}  경로: {currentPath}");

            // RectTransform 정보
            RectTransform rect = transform as RectTransform;
            if (rect != null)
            {
                sb.AppendLine($"{indent}  anchoredPosition: ({rect.anchoredPosition.x}, {rect.anchoredPosition.y})");
                sb.AppendLine($"{indent}  sizeDelta: ({rect.sizeDelta.x}, {rect.sizeDelta.y})");
                sb.AppendLine($"{indent}  anchorMin: ({rect.anchorMin.x}, {rect.anchorMin.y})");
                sb.AppendLine($"{indent}  anchorMax: ({rect.anchorMax.x}, {rect.anchorMax.y})");
                sb.AppendLine($"{indent}  pivot: ({rect.pivot.x}, {rect.pivot.y})");
            }

            // 주요 컴포넌트 나열
            var components = transform.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                if (comp is Transform || comp is RectTransform) continue;
                if (comp is CanvasRenderer) continue;

                string typeName = comp.GetType().Name;
                sb.AppendLine($"{indent}  [{typeName}]");

                // 특정 컴포넌트의 주요 속성 출력
                PrintComponentProperties(comp, sb, indent + "    ");
            }

            // 자식 순회
            foreach (Transform child in transform)
            {
                ParseGameObjectHierarchy(child, sb, depth + 1, currentPath);
            }
        }

        private static void PrintComponentProperties(Component comp, StringBuilder sb, string indent)
        {
            // Image 컴포넌트
            if (comp is UnityEngine.UI.Image image)
            {
                sb.AppendLine($"{indent}color: ({image.color.r:F2}, {image.color.g:F2}, {image.color.b:F2}, {image.color.a:F2})");
                sb.AppendLine($"{indent}raycastTarget: {image.raycastTarget}");
                return;
            }

            // TextMeshProUGUI 컴포넌트
            var tmpType = comp.GetType();
            if (tmpType.Name == "TextMeshProUGUI" || tmpType.Name == "TMP_Text")
            {
                var fontSizeProp = tmpType.GetProperty("fontSize");
                var colorProp = tmpType.GetProperty("color");

                if (fontSizeProp != null)
                {
                    sb.AppendLine($"{indent}fontSize: {fontSizeProp.GetValue(comp)}");
                }
                if (colorProp != null)
                {
                    var color = (Color)colorProp.GetValue(comp);
                    sb.AppendLine($"{indent}color: ({color.r:F2}, {color.g:F2}, {color.b:F2}, {color.a:F2})");
                }
                return;
            }

            // CanvasGroup 컴포넌트
            if (comp is CanvasGroup canvasGroup)
            {
                sb.AppendLine($"{indent}alpha: {canvasGroup.alpha}");
                sb.AppendLine($"{indent}interactable: {canvasGroup.interactable}");
                sb.AppendLine($"{indent}blocksRaycasts: {canvasGroup.blocksRaycasts}");
                return;
            }
        }

        private static void ParseSerializeFields(GameObject prefab, StringBuilder sb)
        {
            var monoBehaviours = prefab.GetComponents<MonoBehaviour>();
            foreach (var mb in monoBehaviours)
            {
                if (mb == null) continue;

                var type = mb.GetType();
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine")) continue;

                sb.AppendLine($"  [{type.Name}]");

                var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                foreach (var field in fields)
                {
                    var serializeAttr = field.GetCustomAttributes(typeof(SerializeField), true);
                    bool isPublic = field.IsPublic;

                    if (serializeAttr.Length > 0 || isPublic)
                    {
                        var value = field.GetValue(mb);
                        string valueStr = FormatFieldValue(value);
                        string visibility = isPublic ? "public" : "private";
                        sb.AppendLine($"    {visibility} {field.FieldType.Name} {field.Name} = {valueStr}");
                    }
                }
            }
        }

        private static string FormatFieldValue(object value)
        {
            if (value == null) return "null";

            if (value is UnityEngine.Object unityObj)
            {
                return unityObj != null ? unityObj.name : "null";
            }

            if (value is Color color)
            {
                return $"({color.r:F2}, {color.g:F2}, {color.b:F2}, {color.a:F2})";
            }

            if (value is Vector2 v2)
            {
                return $"({v2.x}, {v2.y})";
            }

            if (value is Vector3 v3)
            {
                return $"({v3.x}, {v3.y}, {v3.z})";
            }

            return value.ToString();
        }

        #endregion

        #region JSON 구조용 헬퍼

        private static List<HierarchyNodeInfo> ParseHierarchyToList(Transform transform, string parentPath)
        {
            var result = new List<HierarchyNodeInfo>();
            string currentPath = string.IsNullOrEmpty(parentPath) ? transform.name : $"{parentPath}/{transform.name}";

            var nodeInfo = new HierarchyNodeInfo
            {
                Name = transform.name,
                Path = currentPath,
                Components = new List<string>()
            };

            // Nested Prefab 정보 확인
            bool isNestedRoot = false;
            if (PrefabUtility.IsPartOfPrefabInstance(transform.gameObject))
            {
                // 이 오브젝트가 Nested Prefab의 루트인지 확인
                var nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(transform.gameObject);
                if (nearestRoot == transform.gameObject)
                {
                    isNestedRoot = true;
                    nodeInfo.IsNestedPrefab = true;
                    nodeInfo.SourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject);
                }
            }

            // RectTransform (기본값 아닐 때만 포함)
            nodeInfo.RectTransform = CreateOptimizedRectInfo(transform as RectTransform);

            // 컴포넌트 (수정 가능한 것만)
            AddRelevantComponents(transform, nodeInfo.Components);

            // 빈 리스트는 null로 설정하여 JSON에서 생략
            if (nodeInfo.Components.Count == 0)
                nodeInfo.Components = null;

            result.Add(nodeInfo);

            // Nested Prefab 루트가 아닐 때만 자식 순회 (내부 구조 생략)
            if (!isNestedRoot)
            {
                foreach (Transform child in transform)
                {
                    result.AddRange(ParseHierarchyToList(child, currentPath));
                }
            }

            return result;
        }

        /// <summary>
        /// RectTransform 정보 최적화: 기본값은 null 처리하여 JSON에서 생략
        /// </summary>
        private static RectTransformInfo CreateOptimizedRectInfo(RectTransform rect)
        {
            if (rect == null) return null;

            var info = new RectTransformInfo();
            bool hasNonDefault = false;

            // 기본값이 아닐 때만 포함
            if (rect.anchoredPosition != Vector2.zero)
            {
                info.AnchoredPosition = new[] { rect.anchoredPosition.x, rect.anchoredPosition.y };
                hasNonDefault = true;
            }

            if (rect.sizeDelta != Vector2.zero)
            {
                info.SizeDelta = new[] { rect.sizeDelta.x, rect.sizeDelta.y };
                hasNonDefault = true;
            }

            // 앵커: 기본값 [0.5, 0.5]가 아닐 때만
            if (rect.anchorMin != new Vector2(0.5f, 0.5f))
            {
                info.AnchorMin = new[] { rect.anchorMin.x, rect.anchorMin.y };
                hasNonDefault = true;
            }

            if (rect.anchorMax != new Vector2(0.5f, 0.5f))
            {
                info.AnchorMax = new[] { rect.anchorMax.x, rect.anchorMax.y };
                hasNonDefault = true;
            }

            // 피벗: 기본값 [0.5, 0.5]가 아닐 때만
            if (rect.pivot != new Vector2(0.5f, 0.5f))
            {
                info.Pivot = new[] { rect.pivot.x, rect.pivot.y };
                hasNonDefault = true;
            }

            // 모든 값이 기본값이면 null 반환 (JSON에서 완전 생략)
            return hasNonDefault ? info : null;
        }

        /// <summary>
        /// 수정 가능한 컴포넌트만 필터링하여 추가
        /// </summary>
        private static void AddRelevantComponents(Transform transform, List<string> components)
        {
            var comps = transform.GetComponents<Component>();
            foreach (var comp in comps)
            {
                if (comp == null) continue;

                // 기본 컴포넌트 제외
                if (comp is Transform || comp is RectTransform || comp is CanvasRenderer) continue;

                string typeName = comp.GetType().Name;

                // 수정 불가능한 시스템 컴포넌트 제외
                if (typeName == "Canvas" || typeName == "GraphicRaycaster") continue;

                components.Add(typeName);
            }
        }

        private static List<BindingInfo> ParseBindingsToList(GameObject prefab)
        {
            var result = new List<BindingInfo>();

            var monoBehaviours = prefab.GetComponents<MonoBehaviour>();
            foreach (var mb in monoBehaviours)
            {
                if (mb == null) continue;

                var type = mb.GetType();
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine")) continue;

                var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                foreach (var field in fields)
                {
                    var serializeAttr = field.GetCustomAttributes(typeof(SerializeField), true);
                    if (serializeAttr.Length > 0 || field.IsPublic)
                    {
                        var value = field.GetValue(mb);

                        result.Add(new BindingInfo
                        {
                            FieldName = field.Name,
                            FieldType = field.FieldType.Name,
                            TargetName = value is UnityEngine.Object obj && obj != null ? obj.name : null,
                            ScriptClass = type.Name
                        });
                    }
                }
            }

            return result;
        }

        #endregion

        #region 데이터 클래스

        [Serializable]
        public class PrefabStructureInfo
        {
            public string PrefabPath;
            public string RootName;
            public List<HierarchyNodeInfo> Hierarchy;
            public List<BindingInfo> Bindings;
        }

        [Serializable]
        public class HierarchyNodeInfo
        {
            public string Name;
            public string Path;

            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public RectTransformInfo RectTransform;

            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public List<string> Components;

            // Nested Prefab 정보
            [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
            public bool IsNestedPrefab;

            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public string SourcePrefabPath;
        }

        [Serializable]
        public class RectTransformInfo
        {
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public float[] AnchoredPosition;

            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public float[] SizeDelta;

            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public float[] AnchorMin;

            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public float[] AnchorMax;

            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public float[] Pivot;
        }

        [Serializable]
        public class BindingInfo
        {
            public string FieldName;
            public string FieldType;
            public string TargetName;
            public string ScriptClass;
        }

        #endregion
    }
}
