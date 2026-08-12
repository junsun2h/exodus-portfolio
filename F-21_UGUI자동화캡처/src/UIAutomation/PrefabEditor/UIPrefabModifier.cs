// UI 자동화 시스템 - 프리팹 수정기
// JSON 데이터를 기반으로 PrefabUtility를 사용하여 프리팹을 수정

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;

namespace PX.UIAutomation
{
    /// <summary>
    /// 프리팹 수정 유틸리티
    /// PrefabUtility.EditPrefabContentsScope를 사용하여 안전하게 프리팹 수정
    /// </summary>
    public static class UIPrefabModifier
    {
        #region Public API

        /// <summary>
        /// UIModificationData를 기반으로 프리팹 수정
        /// </summary>
        public static bool ApplyModifications(GameObject prefab, UIModificationData data)
        {
            if (prefab == null || data == null)
            {
                Debug.LogError("[UIPrefabModifier] 프리팹 또는 데이터가 null입니다.");
                return false;
            }

            // Play Mode 체크
            if (Application.isPlaying)
            {
                Debug.LogError("[UIPrefabModifier] Play Mode에서는 프리팹을 수정할 수 없습니다.");
                return false;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError("[UIPrefabModifier] 프리팹 경로를 찾을 수 없습니다.");
                return false;
            }

            return ApplyModifications(prefabPath, data);
        }

        /// <summary>
        /// 프리팹 경로와 UIModificationData를 기반으로 프리팹 수정
        /// </summary>
        public static bool ApplyModifications(string prefabPath, UIModificationData data)
        {
            if (string.IsNullOrEmpty(prefabPath) || data == null)
            {
                Debug.LogError("[UIPrefabModifier] 경로 또는 데이터가 유효하지 않습니다.");
                return false;
            }

            // Play Mode 체크
            if (Application.isPlaying)
            {
                Debug.LogError("[UIPrefabModifier] Play Mode에서는 프리팹을 수정할 수 없습니다.");
                return false;
            }

            int successCount = 0;
            int failCount = 0;

            try
            {
                // Undo 등록
                Undo.SetCurrentGroupName("UI Prefab Modification");
                int undoGroup = Undo.GetCurrentGroup();

                // 프리팹 편집 시작
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
                {
                    GameObject prefabRoot = editScope.prefabContentsRoot;

                    foreach (var mod in data.Modifications)
                    {
                        try
                        {
                            if (ApplySingleModification(prefabRoot, mod))
                            {
                                successCount++;
                            }
                            else
                            {
                                failCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[UIPrefabModifier] 수정 실패 ({mod.GameObjectPath}/{mod.ComponentType}): {ex.Message}");
                            failCount++;
                        }
                    }
                }

                // Undo 그룹 종료
                Undo.CollapseUndoOperations(undoGroup);

                // 에셋 저장
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[UIPrefabModifier] 수정 완료 - 성공: {successCount}, 실패: {failCount}");
                return failCount == 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabModifier] 프리팹 수정 중 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 런타임 캡처 데이터를 참조하여 동적 아이템 수정을 원본 프리팹에 분리 적용
        /// </summary>
        /// <param name="data">수정 데이터</param>
        /// <param name="captureData">런타임 캡처 데이터 (동적 컨테이너 정보 포함)</param>
        /// <returns>성공 여부</returns>
        public static bool ApplyModificationsWithCapture(UIModificationData data, RuntimeCaptureData captureData)
        {
            if (data == null)
            {
                Debug.LogError("[UIPrefabModifier] 수정 데이터가 null입니다.");
                return false;
            }

            // Play Mode 체크
            if (Application.isPlaying)
            {
                Debug.LogError("[UIPrefabModifier] Play Mode에서는 프리팹을 수정할 수 없습니다.");
                return false;
            }

            // 캡처 데이터가 없으면 기본 적용
            if (captureData == null)
            {
                return ApplyModifications(data.PrefabPath, data);
            }

            // 동적 컨테이너 정보 추출
            var dynamicContainers = ExtractDynamicContainers(captureData);

            // 수정 항목 분류
            var mainPrefabMods = new List<UIModificationItem>();
            var itemPrefabMods = new Dictionary<string, List<UIModificationItem>>(); // prefabPath -> mods

            foreach (var mod in data.Modifications)
            {
                // 동적 아이템 수정인지 확인
                var itemPrefabPath = FindDynamicItemPrefabPath(mod.GameObjectPath, dynamicContainers);

                if (!string.IsNullOrEmpty(itemPrefabPath))
                {
                    // 동적 아이템 → 원본 프리팹에 적용
                    if (!itemPrefabMods.ContainsKey(itemPrefabPath))
                    {
                        itemPrefabMods[itemPrefabPath] = new List<UIModificationItem>();
                    }

                    // 경로를 아이템 프리팹 내부 경로로 변환
                    var modCopy = new UIModificationItem
                    {
                        GameObjectPath = ConvertToItemPrefabPath(mod.GameObjectPath, dynamicContainers),
                        ComponentType = mod.ComponentType,
                        Properties = mod.Properties
                    };
                    itemPrefabMods[itemPrefabPath].Add(modCopy);

                    Debug.Log($"[UIPrefabModifier] 동적 아이템 수정 → 원본 프리팹: {itemPrefabPath}");
                }
                else
                {
                    // 컨테이너/부모 → 메인 프리팹에 적용
                    mainPrefabMods.Add(mod);
                }
            }

            bool allSuccess = true;

            // Undo 등록
            Undo.SetCurrentGroupName("UI Prefab Modification (Separated)");
            int undoGroup = Undo.GetCurrentGroup();

            // 1. 메인 프리팹 수정
            if (mainPrefabMods.Count > 0)
            {
                var mainData = new UIModificationData
                {
                    PrefabPath = data.PrefabPath,
                    Modifications = mainPrefabMods
                };

                if (!ApplyModifications(data.PrefabPath, mainData))
                {
                    allSuccess = false;
                }
            }

            // 2. 아이템 프리팹들 수정
            foreach (var kvp in itemPrefabMods)
            {
                var itemPrefabPath = kvp.Key;
                var itemMods = kvp.Value;

                var itemData = new UIModificationData
                {
                    PrefabPath = itemPrefabPath,
                    Modifications = itemMods
                };

                Debug.Log($"[UIPrefabModifier] 아이템 프리팹 수정 중: {itemPrefabPath} ({itemMods.Count}개 항목)");

                if (!ApplyModifications(itemPrefabPath, itemData))
                {
                    allSuccess = false;
                }
            }

            // Undo 그룹 종료
            Undo.CollapseUndoOperations(undoGroup);

            return allSuccess;
        }

        /// <summary>
        /// 캡처 데이터에서 동적 컨테이너 정보 추출
        /// </summary>
        private static Dictionary<string, DynamicContainerInfo> ExtractDynamicContainers(RuntimeCaptureData captureData)
        {
            var containers = new Dictionary<string, DynamicContainerInfo>();

            if (captureData?.Hierarchy != null)
            {
                foreach (var node in captureData.Hierarchy)
                {
                    ExtractDynamicContainersRecursive(node, containers);
                }
            }

            return containers;
        }

        private static void ExtractDynamicContainersRecursive(RuntimeHierarchyNode node, Dictionary<string, DynamicContainerInfo> containers)
        {
            if (node.DynamicContainer != null)
            {
                containers[node.Path] = node.DynamicContainer;
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    ExtractDynamicContainersRecursive(child, containers);
                }
            }
        }

        /// <summary>
        /// 경로가 동적 아이템의 자식인지 확인하고, 해당 아이템의 원본 프리팹 경로 반환
        /// </summary>
        private static string FindDynamicItemPrefabPath(string modPath, Dictionary<string, DynamicContainerInfo> containers)
        {
            foreach (var kvp in containers)
            {
                var containerPath = kvp.Key;
                var containerInfo = kvp.Value;

                // 경로가 컨테이너/item_N/... 패턴인지 확인
                if (modPath.StartsWith(containerPath + "/"))
                {
                    var relativePath = modPath.Substring(containerPath.Length + 1);
                    var parts = relativePath.Split('/');

                    // 첫 번째 부분이 동적 아이템 패턴인지 확인
                    // item_N, Item_N, [Preview], PXListViewItem_ 등
                    if (parts.Length > 0 && IsDynamicItemName(parts[0]))
                    {
                        return containerInfo.ItemPrefabPath;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 수정 경로를 아이템 프리팹 내부 경로로 변환
        /// 예: "PXListView/item_0/PXText_Name" → "PXText_Name"
        /// </summary>
        private static string ConvertToItemPrefabPath(string modPath, Dictionary<string, DynamicContainerInfo> containers)
        {
            foreach (var kvp in containers)
            {
                var containerPath = kvp.Key;

                if (modPath.StartsWith(containerPath + "/"))
                {
                    var relativePath = modPath.Substring(containerPath.Length + 1);
                    var parts = relativePath.Split(new[] { '/' }, 2);

                    // 동적 아이템 부분 제거하고 나머지 경로 반환
                    if (parts.Length > 1 && IsDynamicItemName(parts[0]))
                    {
                        return parts[1];
                    }

                    // 동적 아이템 자체가 타겟인 경우 빈 문자열 (루트)
                    if (parts.Length == 1 && IsDynamicItemName(parts[0]))
                    {
                        return "";
                    }
                }
            }

            return modPath;
        }

        #endregion

        #region 개별 수정 적용

        private static bool ApplySingleModification(GameObject root, UIModificationItem mod)
        {
            // GameObject 찾기
            Transform target = FindTransformByPath(root.transform, mod.GameObjectPath);
            if (target == null)
            {
                Debug.LogError($"[UIPrefabModifier] GameObject를 찾을 수 없음: {mod.GameObjectPath}");
                return false;
            }

            // 원본 프리팹 수정 모드인 경우
            if (mod.ModifySourcePrefab)
            {
                return ApplyModificationToSourcePrefab(target, mod);
            }

            // 로컬 Override 모드 (기본)
            return ApplyComponentModification(target, mod.ComponentType, mod.Properties);
        }

        /// <summary>
        /// 원본 프리팹을 직접 수정 (모든 인스턴스에 일괄 반영)
        /// </summary>
        private static bool ApplyModificationToSourcePrefab(Transform target, UIModificationItem mod)
        {
            // Nested Prefab인지 확인
            if (!PrefabUtility.IsPartOfPrefabInstance(target.gameObject))
            {
                Debug.LogError($"[UIPrefabModifier] Nested Prefab이 아닙니다: {mod.GameObjectPath}");
                return ApplyComponentModification(target, mod.ComponentType, mod.Properties);
            }

            // 가장 가까운 프리팹 인스턴스 루트 찾기
            var nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(target.gameObject);
            if (nearestRoot == null)
            {
                Debug.LogError($"[UIPrefabModifier] Prefab 루트를 찾을 수 없음: {mod.GameObjectPath}");
                return false;
            }

            // 원본 프리팹 경로
            string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(target.gameObject);
            if (string.IsNullOrEmpty(sourcePrefabPath))
            {
                Debug.LogError($"[UIPrefabModifier] 원본 프리팹 경로를 찾을 수 없음: {mod.GameObjectPath}");
                return false;
            }

            // 타겟이 프리팹 루트인지, 아니면 자식인지 확인
            string relativePathInSource = "";
            if (nearestRoot != target.gameObject)
            {
                // 프리팹 루트로부터의 상대 경로 계산
                relativePathInSource = GetRelativePath(nearestRoot.transform, target);
            }

            Debug.Log($"[UIPrefabModifier] 원본 프리팹 수정: {sourcePrefabPath}, 상대경로: {relativePathInSource}");

            // 원본 프리팹 수정
            try
            {
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(sourcePrefabPath))
                {
                    GameObject sourcePrefabRoot = editScope.prefabContentsRoot;
                    Transform sourceTarget;

                    if (string.IsNullOrEmpty(relativePathInSource))
                    {
                        sourceTarget = sourcePrefabRoot.transform;
                    }
                    else
                    {
                        sourceTarget = sourcePrefabRoot.transform.Find(relativePathInSource);
                    }

                    if (sourceTarget == null)
                    {
                        Debug.LogError($"[UIPrefabModifier] 원본 프리팹에서 대상을 찾을 수 없음: {relativePathInSource}");
                        return false;
                    }

                    bool result = ApplyComponentModification(sourceTarget, mod.ComponentType, mod.Properties);

                    if (result)
                    {
                        Debug.Log($"[UIPrefabModifier] 원본 프리팹 수정 성공: {sourcePrefabPath}");
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIPrefabModifier] 원본 프리팹 수정 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 부모로부터 자식까지의 상대 경로 계산
        /// </summary>
        private static string GetRelativePath(Transform parent, Transform child)
        {
            var path = new List<string>();
            Transform current = child;

            while (current != null && current != parent)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        /// <summary>
        /// 컴포넌트 타입별 수정 적용
        /// </summary>
        private static bool ApplyComponentModification(Transform target, string componentType, Dictionary<string, object> properties)
        {
            switch (componentType)
            {
                case "RectTransform":
                    return ApplyRectTransformModification(target, properties);

                case "TextMeshProUGUI":
                case "TMP_Text":
                    return ApplyTextMeshProModification(target, properties);

                case "Image":
                    return ApplyImageModification(target, properties);

                case "CanvasGroup":
                    return ApplyCanvasGroupModification(target, properties);

                case "PXButton":
                    return ApplyPXButtonModification(target, properties);

                case "PXText":
                    return ApplyPXTextModification(target, properties);

                case "PXImage":
                    return ApplyPXImageModification(target, properties);

                default:
                    Debug.LogError($"[UIPrefabModifier] 지원하지 않는 컴포넌트 타입: {componentType}");
                    return false;
            }
        }

        #endregion

        #region 컴포넌트별 수정 로직

        private static bool ApplyRectTransformModification(Transform target, Dictionary<string, object> properties)
        {
            RectTransform rect = target as RectTransform;
            if (rect == null)
            {
                rect = target.GetComponent<RectTransform>();
                if (rect == null)
                {
                    Debug.LogError($"[UIPrefabModifier] RectTransform을 찾을 수 없음: {target.name}");
                    return false;
                }
            }

            Undo.RecordObject(rect, "Modify RectTransform");

            // anchoredPosition
            if (properties.TryGetValue("anchoredPosition", out var anchoredPos))
            {
                rect.anchoredPosition = ParseVector2(anchoredPos);
            }

            // sizeDelta
            if (properties.TryGetValue("sizeDelta", out var sizeDelta))
            {
                rect.sizeDelta = ParseVector2(sizeDelta);
            }

            // anchorMin
            if (properties.TryGetValue("anchorMin", out var anchorMin))
            {
                rect.anchorMin = ParseVector2(anchorMin);
            }

            // anchorMax
            if (properties.TryGetValue("anchorMax", out var anchorMax))
            {
                rect.anchorMax = ParseVector2(anchorMax);
            }

            // pivot
            if (properties.TryGetValue("pivot", out var pivot))
            {
                rect.pivot = ParseVector2(pivot);
            }

            // offsetMin
            if (properties.TryGetValue("offsetMin", out var offsetMin))
            {
                rect.offsetMin = ParseVector2(offsetMin);
            }

            // offsetMax
            if (properties.TryGetValue("offsetMax", out var offsetMax))
            {
                rect.offsetMax = ParseVector2(offsetMax);
            }

            // localScale
            if (properties.TryGetValue("localScale", out var localScale))
            {
                rect.localScale = ParseVector3(localScale);
            }

            EditorUtility.SetDirty(rect);
            return true;
        }

        private static bool ApplyTextMeshProModification(Transform target, Dictionary<string, object> properties)
        {
            // TMP 컴포넌트 찾기 (리플렉션 사용)
            var tmp = target.GetComponent("TextMeshProUGUI");
            if (tmp == null)
            {
                tmp = target.GetComponent("TMP_Text");
            }

            if (tmp == null)
            {
                Debug.LogError($"[UIPrefabModifier] TextMeshPro를 찾을 수 없음: {target.name}");
                return false;
            }

            Undo.RecordObject(tmp, "Modify TextMeshPro");

            var type = tmp.GetType();

            // fontSize
            if (properties.TryGetValue("fontSize", out var fontSize))
            {
                var prop = type.GetProperty("fontSize");
                if (prop != null)
                {
                    prop.SetValue(tmp, Convert.ToSingle(fontSize));
                }
            }

            // color
            if (properties.TryGetValue("color", out var color))
            {
                var prop = type.GetProperty("color");
                if (prop != null)
                {
                    prop.SetValue(tmp, ParseColor(color));
                }
            }

            // alignment
            if (properties.TryGetValue("alignment", out var alignment))
            {
                var prop = type.GetProperty("alignment");
                if (prop != null)
                {
                    // TMPro.TextAlignmentOptions 열거형 파싱
                    var alignmentType = prop.PropertyType;
                    if (Enum.TryParse(alignmentType, alignment.ToString(), true, out var alignValue))
                    {
                        prop.SetValue(tmp, alignValue);
                    }
                }
            }

            // enableAutoSizing
            if (properties.TryGetValue("enableAutoSizing", out var autoSizing))
            {
                var prop = type.GetProperty("enableAutoSizing");
                if (prop != null)
                {
                    prop.SetValue(tmp, Convert.ToBoolean(autoSizing));
                }
            }

            // fontSizeMin
            if (properties.TryGetValue("fontSizeMin", out var fontSizeMin))
            {
                var prop = type.GetProperty("fontSizeMin");
                if (prop != null)
                {
                    prop.SetValue(tmp, Convert.ToSingle(fontSizeMin));
                }
            }

            // fontSizeMax
            if (properties.TryGetValue("fontSizeMax", out var fontSizeMax))
            {
                var prop = type.GetProperty("fontSizeMax");
                if (prop != null)
                {
                    prop.SetValue(tmp, Convert.ToSingle(fontSizeMax));
                }
            }

            EditorUtility.SetDirty(tmp);
            return true;
        }

        private static bool ApplyImageModification(Transform target, Dictionary<string, object> properties)
        {
            var image = target.GetComponent<Image>();
            if (image == null)
            {
                Debug.LogError($"[UIPrefabModifier] Image를 찾을 수 없음: {target.name}");
                return false;
            }

            Undo.RecordObject(image, "Modify Image");

            // color
            if (properties.TryGetValue("color", out var color))
            {
                image.color = ParseColor(color);
            }

            // raycastTarget
            if (properties.TryGetValue("raycastTarget", out var raycastTarget))
            {
                image.raycastTarget = Convert.ToBoolean(raycastTarget);
            }

            EditorUtility.SetDirty(image);
            return true;
        }

        private static bool ApplyCanvasGroupModification(Transform target, Dictionary<string, object> properties)
        {
            var canvasGroup = target.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                Debug.LogError($"[UIPrefabModifier] CanvasGroup을 찾을 수 없음: {target.name}");
                return false;
            }

            Undo.RecordObject(canvasGroup, "Modify CanvasGroup");

            // alpha
            if (properties.TryGetValue("alpha", out var alpha))
            {
                canvasGroup.alpha = Convert.ToSingle(alpha);
            }

            // interactable
            if (properties.TryGetValue("interactable", out var interactable))
            {
                canvasGroup.interactable = Convert.ToBoolean(interactable);
            }

            // blocksRaycasts
            if (properties.TryGetValue("blocksRaycasts", out var blocksRaycasts))
            {
                canvasGroup.blocksRaycasts = Convert.ToBoolean(blocksRaycasts);
            }

            EditorUtility.SetDirty(canvasGroup);
            return true;
        }

        private static bool ApplyPXButtonModification(Transform target, Dictionary<string, object> properties)
        {
            // PXButton 컴포넌트 찾기
            var pxButton = target.GetComponent("PXButton");
            if (pxButton == null)
            {
                Debug.LogError($"[UIPrefabModifier] PXButton을 찾을 수 없음: {target.name}");
                return false;
            }

            Undo.RecordObject(pxButton as UnityEngine.Object, "Modify PXButton");

            var type = pxButton.GetType();

            // interactable (Selectable 상속)
            if (properties.TryGetValue("interactable", out var interactable))
            {
                var prop = type.GetProperty("interactable");
                if (prop != null)
                {
                    prop.SetValue(pxButton, Convert.ToBoolean(interactable));
                }
            }

            EditorUtility.SetDirty(pxButton as UnityEngine.Object);
            return true;
        }

        private static bool ApplyPXTextModification(Transform target, Dictionary<string, object> properties)
        {
            // PXText는 TMP를 래핑하므로 TMP 수정과 동일
            return ApplyTextMeshProModification(target, properties);
        }

        private static bool ApplyPXImageModification(Transform target, Dictionary<string, object> properties)
        {
            // PXImage는 Image를 래핑하므로 Image 수정과 동일
            return ApplyImageModification(target, properties);
        }

        #endregion

        #region 유틸리티 메서드

        /// <summary>
        /// 이름이 동적 아이템 패턴인지 확인
        /// item_N, Item_N, [Preview], PXListViewItem_ 등
        /// </summary>
        private static bool IsDynamicItemName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            // 일반적인 동적 아이템 패턴
            if (name.StartsWith("item_") || name.StartsWith("Item_"))
                return true;

            // 프리뷰 아이템
            if (name.StartsWith("[Preview]"))
                return true;

            // PXListView 아이템 패턴 (PXListViewItem_XXX)
            if (name.StartsWith("PXListViewItem_"))
                return true;

            // 숫자로 끝나는 clone 패턴 (Clone(1), Clone(2) 등)
            if (name.Contains("(Clone)"))
                return true;

            return false;
        }

        /// <summary>
        /// 경로로 Transform 찾기
        /// </summary>
        private static Transform FindTransformByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;

            // 경로가 루트 이름과 정확히 같으면 루트 반환
            if (path == root.name)
            {
                return root;
            }

            // 루트 이름이 포함된 경우 제거
            if (path.StartsWith(root.name + "/"))
            {
                path = path.Substring(root.name.Length + 1);
            }

            return root.Find(path);
        }

        /// <summary>
        /// JSON 객체를 Vector2로 파싱
        /// 지원 형식: [x, y] 배열 또는 {"x": x, "y": y} 객체
        /// </summary>
        private static Vector2 ParseVector2(object obj)
        {
            // 배열 형식: [x, y]
            if (obj is JArray jArray && jArray.Count >= 2)
            {
                return new Vector2(jArray[0].Value<float>(), jArray[1].Value<float>());
            }

            // 객체 형식: {"x": x, "y": y}
            if (obj is JObject jObj)
            {
                float x = jObj["x"] != null ? (float)jObj["x"] : 0f;
                float y = jObj["y"] != null ? (float)jObj["y"] : 0f;
                return new Vector2(x, y);
            }

            if (obj is Dictionary<string, object> dict)
            {
                float x = dict.ContainsKey("x") ? Convert.ToSingle(dict["x"]) : 0f;
                float y = dict.ContainsKey("y") ? Convert.ToSingle(dict["y"]) : 0f;
                return new Vector2(x, y);
            }

            // List 형식 (Newtonsoft.Json이 배열을 List로 파싱할 경우)
            if (obj is IList<object> list && list.Count >= 2)
            {
                return new Vector2(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]));
            }

            return Vector2.zero;
        }

        /// <summary>
        /// JSON 객체를 Vector3로 파싱
        /// 지원 형식: [x, y, z] 배열 또는 {"x": x, "y": y, "z": z} 객체
        /// </summary>
        private static Vector3 ParseVector3(object obj)
        {
            // 배열 형식: [x, y, z]
            if (obj is JArray jArray && jArray.Count >= 3)
            {
                return new Vector3(jArray[0].Value<float>(), jArray[1].Value<float>(), jArray[2].Value<float>());
            }

            // 객체 형식: {"x": x, "y": y, "z": z}
            if (obj is JObject jObj)
            {
                float x = jObj["x"] != null ? (float)jObj["x"] : 1f;
                float y = jObj["y"] != null ? (float)jObj["y"] : 1f;
                float z = jObj["z"] != null ? (float)jObj["z"] : 1f;
                return new Vector3(x, y, z);
            }

            if (obj is Dictionary<string, object> dict)
            {
                float x = dict.ContainsKey("x") ? Convert.ToSingle(dict["x"]) : 1f;
                float y = dict.ContainsKey("y") ? Convert.ToSingle(dict["y"]) : 1f;
                float z = dict.ContainsKey("z") ? Convert.ToSingle(dict["z"]) : 1f;
                return new Vector3(x, y, z);
            }

            // List 형식
            if (obj is IList<object> list && list.Count >= 3)
            {
                return new Vector3(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));
            }

            return Vector3.one;
        }

        /// <summary>
        /// JSON 객체를 Color로 파싱
        /// </summary>
        private static Color ParseColor(object obj)
        {
            if (obj is JObject jObj)
            {
                float r = jObj["r"]?.Value<float>() ?? 1f;
                float g = jObj["g"]?.Value<float>() ?? 1f;
                float b = jObj["b"]?.Value<float>() ?? 1f;
                float a = jObj["a"]?.Value<float>() ?? 1f;
                return new Color(r, g, b, a);
            }

            if (obj is Dictionary<string, object> dict)
            {
                float r = dict.ContainsKey("r") ? Convert.ToSingle(dict["r"]) : 1f;
                float g = dict.ContainsKey("g") ? Convert.ToSingle(dict["g"]) : 1f;
                float b = dict.ContainsKey("b") ? Convert.ToSingle(dict["b"]) : 1f;
                float a = dict.ContainsKey("a") ? Convert.ToSingle(dict["a"]) : 1f;
                return new Color(r, g, b, a);
            }

            return Color.white;
        }

        #endregion
    }
}
