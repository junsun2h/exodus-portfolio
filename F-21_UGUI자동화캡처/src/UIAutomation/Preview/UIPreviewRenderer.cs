// UI 프리뷰 렌더러
// 런타임 캡처 JSON을 기반으로 Edit Mode에서 더미 아이템 생성

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using TMPro;
using PX;
using BattleSimulator;

namespace PX.UIAutomation
{
    /// <summary>
    /// 런타임 캡처 데이터를 기반으로 Edit Mode에서 프리뷰 렌더링
    /// </summary>
    public class UIPreviewRenderer
    {
        #region Fields

        private List<GameObject> _createdDummies = new List<GameObject>();
        private Dictionary<string, GameObject> _pathToGameObject = new Dictionary<string, GameObject>();

        #endregion

        #region Public API

        /// <summary>
        /// 캡처 데이터를 기반으로 프리뷰 생성
        /// </summary>
        /// <param name="prefabRoot">프리팹 인스턴스 루트</param>
        /// <param name="captureData">런타임 캡처 데이터</param>
        /// <returns>생성된 더미 오브젝트 목록</returns>
        public List<GameObject> Render(GameObject prefabRoot, RuntimeCaptureData captureData)
        {
            if (prefabRoot == null || captureData == null)
            {
                Debug.LogError("[UIPreviewRenderer] 프리팹 루트 또는 캡처 데이터가 null입니다.");
                return new List<GameObject>();
            }

            // 기존 더미 정리
            Clear();

            // 경로-오브젝트 매핑 구축
            BuildPathMapping(prefabRoot);

            // 계층 구조 순회하며 동적 컨테이너 찾기
            foreach (var node in captureData.Hierarchy)
            {
                ProcessNode(node, prefabRoot);
            }

            return _createdDummies;
        }

        /// <summary>
        /// 생성된 더미 오브젝트 모두 제거
        /// </summary>
        public void Clear()
        {
            foreach (var dummy in _createdDummies)
            {
                if (dummy != null)
                {
                    Object.DestroyImmediate(dummy);
                }
            }
            _createdDummies.Clear();
            _pathToGameObject.Clear();
        }

        /// <summary>
        /// 현재 생성된 더미 개수
        /// </summary>
        public int DummyCount => _createdDummies.Count;

        #endregion

        #region Private Methods

        /// <summary>
        /// 경로-오브젝트 매핑 구축
        /// </summary>
        private void BuildPathMapping(GameObject root)
        {
            _pathToGameObject.Clear();
            BuildPathMappingRecursive(root, root.name);
        }

        private void BuildPathMappingRecursive(GameObject obj, string currentPath)
        {
            // (Clone) 제거된 경로도 추가
            _pathToGameObject[currentPath] = obj;
            var normalizedPath = currentPath.Replace("(Clone)", "");
            if (normalizedPath != currentPath)
            {
                _pathToGameObject[normalizedPath] = obj;
            }

            foreach (Transform child in obj.transform)
            {
                var childPath = $"{currentPath}/{child.name}";
                BuildPathMappingRecursive(child.gameObject, childPath);
            }
        }

        /// <summary>
        /// 노드 처리
        /// </summary>
        private void ProcessNode(RuntimeHierarchyNode node, GameObject prefabRoot)
        {
            // [추가] 기존 오브젝트의 RuntimeData 적용 (동적 아이템이 아닌 경우)
            if (node.RuntimeData != null && !node.IsDynamicItem)
            {
                var obj = FindGameObjectByPath(prefabRoot, node.Path);
                if (obj != null)
                {
                    ApplyRuntimeData(obj, node.RuntimeData);
                }
            }

            // 동적 컨테이너 발견 시 더미 아이템 생성
            if (node.DynamicContainer != null)
            {
                CreateDummyItems(node, prefabRoot);
            }

            // 자식 노드 재귀 처리
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    // 동적 아이템은 건너뜀 (이미 위에서 처리)
                    if (!child.IsDynamicItem)
                    {
                        ProcessNode(child, prefabRoot);
                    }
                }
            }
        }

        /// <summary>
        /// 동적 컨테이너에 더미 아이템 생성
        /// </summary>
        private void CreateDummyItems(RuntimeHierarchyNode containerNode, GameObject prefabRoot)
        {
            var containerInfo = containerNode.DynamicContainer;

            // 컨테이너 오브젝트 찾기
            var containerObj = FindGameObjectByPath(prefabRoot, containerNode.Path);
            if (containerObj == null)
            {
                return;
            }

            // 아이템 프리팹 로드
            if (string.IsNullOrEmpty(containerInfo.ItemPrefabPath))
            {
                return;
            }

            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(containerInfo.ItemPrefabPath);
            if (itemPrefab == null)
            {
                return;
            }

            // Content 영역 찾기 (PXListView의 경우)
            Transform parentTransform = containerObj.transform;
            var listView = containerObj.GetComponent<PXListView>();
            if (listView != null && listView.GetContent != null)
            {
                parentTransform = listView.GetContent;
            }
            else
            {
                // Fallback: 이름으로 Content 찾기
                var content = containerObj.transform.Find("Viewport/Content");
                if (content == null) content = containerObj.transform.Find("Content");
                // PXScrollView_Vertical_OptionData의 경우
                if (content == null) content = containerObj.transform.Find("Scroll View/Viewport/Content");
                if (content != null) parentTransform = content;
            }

            // 기존 템플릿 오브젝트 비활성화 (원본과 겹침 방지)
            HideTemplateObjects(parentTransform);

            // 동적 아이템 노드들 가져오기 (모든 자손에서 검색)
            var dynamicItemNodes = FindDynamicItemNodes(containerNode);

            // 각 동적 아이템에 대해 더미 생성
            for (int i = 0; i < dynamicItemNodes.Count; i++)
            {
                var itemNode = dynamicItemNodes[i];
                var dummy = CreateSingleDummy(itemPrefab, parentTransform, itemNode, i);
                if (dummy != null)
                {
                    _createdDummies.Add(dummy);

                    // [추가] 중첩 컨테이너 처리 - 동적 아이템 내부의 컨테이너도 처리
                    ProcessDynamicItemChildren(itemNode, dummy);
                }
            }

            // 동적 아이템 노드가 없으면 기본 개수만큼 생성
            if (dynamicItemNodes.Count == 0 && containerInfo.VisibleItemCount > 0)
            {
                for (int i = 0; i < containerInfo.VisibleItemCount; i++)
                {
                    var dummy = CreateSingleDummy(itemPrefab, parentTransform, null, i);
                    if (dummy != null)
                    {
                        _createdDummies.Add(dummy);
                    }
                }
            }

            // Content 크기 조정
            UpdateContentSize(containerObj, dynamicItemNodes.Count > 0 ? dynamicItemNodes.Count : containerInfo.VisibleItemCount, containerInfo.ItemSize);

            // Canvas 및 LayoutGroup 강제 업데이트
            Canvas.ForceUpdateCanvases();
            var layoutGroup = parentTransform.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.CalculateLayoutInputHorizontal();
                layoutGroup.CalculateLayoutInputVertical();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parentTransform as RectTransform);
            }

            // ContentSizeFitter도 업데이트
            var sizeFitter = parentTransform.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (sizeFitter != null)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parentTransform as RectTransform);
            }

            // Scene View 갱신
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 컨테이너 노드에서 모든 동적 아이템 노드 찾기 (자손 포함)
        /// </summary>
        private List<RuntimeHierarchyNode> FindDynamicItemNodes(RuntimeHierarchyNode containerNode)
        {
            var result = new List<RuntimeHierarchyNode>();
            FindDynamicItemNodesRecursive(containerNode, result);
            return result;
        }

        /// <summary>
        /// 재귀적으로 동적 아이템 노드 검색
        /// </summary>
        private void FindDynamicItemNodesRecursive(RuntimeHierarchyNode node, List<RuntimeHierarchyNode> result)
        {
            if (node.Children == null) return;

            foreach (var child in node.Children)
            {
                if (child.IsDynamicItem)
                {
                    result.Add(child);
                    // 동적 아이템 내부는 더 이상 검색하지 않음 (중첩 컨테이너는 별도 처리)
                }
                else
                {
                    // 동적 아이템이 아닌 노드는 자식 계속 검색
                    FindDynamicItemNodesRecursive(child, result);
                }
            }
        }

        /// <summary>
        /// 동적 아이템 내부의 자식 노드들 처리 (중첩 컨테이너 지원)
        /// </summary>
        private void ProcessDynamicItemChildren(RuntimeHierarchyNode itemNode, GameObject dummyItem)
        {
            if (itemNode == null) return;

            // itemNode 자체가 동적 컨테이너인 경우 처리 (동적 아이템이면서 동시에 컨테이너인 경우)
            if (itemNode.DynamicContainer != null)
            {
                CreateNestedDummyItems(itemNode, dummyItem);
            }

            if (itemNode.Children == null) return;

            foreach (var childNode in itemNode.Children)
            {
                // 상대 경로로 자식 오브젝트 찾기
                var relativePath = GetRelativePath(itemNode.Path, childNode.Path);
                if (string.IsNullOrEmpty(relativePath)) continue;

                var childObj = dummyItem.transform.Find(relativePath)?.gameObject;
                if (childObj == null) continue;

                // RuntimeData 적용
                if (childNode.RuntimeData != null)
                {
                    ApplyRuntimeData(childObj, childNode.RuntimeData);
                }

                // 중첩 컨테이너가 있으면 처리 (자식 노드가 컨테이너인 경우)
                if (childNode.DynamicContainer != null && !childNode.IsDynamicItem)
                {
                    CreateNestedDummyItems(childNode, childObj);
                }

                // 재귀적으로 자식의 자식들도 처리
                ProcessDynamicItemChildren(childNode, childObj);
            }
        }

        /// <summary>
        /// 상대 경로 추출 (부모 경로로부터 자식 경로의 상대 경로)
        /// </summary>
        private string GetRelativePath(string parentPath, string childPath)
        {
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(childPath))
                return null;

            // (Clone) 제거
            parentPath = parentPath.Replace("(Clone)", "");
            childPath = childPath.Replace("(Clone)", "");

            if (!childPath.StartsWith(parentPath + "/"))
                return null;

            return childPath.Substring(parentPath.Length + 1);
        }

        /// <summary>
        /// 중첩 컨테이너에 더미 아이템 생성 (동적 아이템 내부의 컨테이너)
        /// </summary>
        private void CreateNestedDummyItems(RuntimeHierarchyNode containerNode, GameObject containerObj)
        {
            var containerInfo = containerNode.DynamicContainer;

            // 아이템 프리팹 로드
            if (string.IsNullOrEmpty(containerInfo.ItemPrefabPath))
            {
                return;
            }

            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(containerInfo.ItemPrefabPath);
            if (itemPrefab == null)
            {
                return;
            }

            // Content 영역 찾기
            Transform parentTransform = containerObj.transform;
            var listView = containerObj.GetComponent<PXListView>();
            if (listView != null && listView.GetContent != null)
            {
                parentTransform = listView.GetContent;
            }
            else
            {
                // 일반적인 Content 영역 패턴
                var content = containerObj.transform.Find("Viewport/Content");
                if (content == null) content = containerObj.transform.Find("Content");
                // PXWidget_DetailSingle의 경우 OptionArea가 동적 아이템 영역
                if (content == null) content = containerObj.transform.Find("OptionArea");
                // PXScrollView_Vertical_OptionData의 경우
                if (content == null) content = containerObj.transform.Find("Scroll View/Viewport/Content");
                if (content != null) parentTransform = content;
            }

            // 동적 아이템 노드들 가져오기 (모든 자손에서 검색)
            var dynamicItemNodes = FindDynamicItemNodes(containerNode);

            // 각 동적 아이템에 대해 더미 생성
            for (int i = 0; i < dynamicItemNodes.Count; i++)
            {
                var itemNode = dynamicItemNodes[i];
                var dummy = CreateSingleDummy(itemPrefab, parentTransform, itemNode, i);
                if (dummy != null)
                {
                    _createdDummies.Add(dummy);

                    // 재귀적으로 중첩 컨테이너 처리
                    ProcessDynamicItemChildren(itemNode, dummy);
                }
            }

            // 동적 아이템 노드가 없으면 기본 개수만큼 생성
            if (dynamicItemNodes.Count == 0 && containerInfo.VisibleItemCount > 0)
            {
                for (int i = 0; i < containerInfo.VisibleItemCount; i++)
                {
                    var dummy = CreateSingleDummy(itemPrefab, parentTransform, null, i);
                    if (dummy != null)
                    {
                        _createdDummies.Add(dummy);
                    }
                }
            }

            // LayoutGroup 업데이트
            var layoutGroup = parentTransform.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (layoutGroup != null)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parentTransform as RectTransform);
            }
        }

        /// <summary>
        /// 단일 더미 아이템 생성
        /// </summary>
        private GameObject CreateSingleDummy(GameObject prefab, Transform parent, RuntimeHierarchyNode itemNode, int index)
        {
            // 프리팹 인스턴스 생성
            var dummy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (dummy == null)
            {
                // Fallback: 일반 Instantiate
                dummy = Object.Instantiate(prefab, parent);
            }

            // HideFlags 설정 - 프리팹에 저장되지 않음
            SetHideFlagsRecursive(dummy, HideFlags.DontSave);

            // 이름 설정
            dummy.name = $"[Preview] item_{index}";

            // 캡처된 RectTransform에서 크기만 적용 (위치는 스크롤 상태라 사용 안함)
            if (itemNode?.RectTransform != null)
            {
                ApplyRectTransformSizeOnly(dummy, itemNode.RectTransform);
            }

            // 위치는 항상 index 기반으로 계산 (캡처된 위치는 스크롤된 상태라 사용 불가)
            SetItemPosition(dummy, parent, index);

            // 런타임 데이터 적용 (있는 경우)
            if (itemNode?.RuntimeData != null)
            {
                ApplyRuntimeData(dummy, itemNode.RuntimeData);
            }

            return dummy;
        }

        /// <summary>
        /// 아이템 위치 설정 (PXListViewCore와 동일한 로직 사용)
        /// </summary>
        private void SetItemPosition(GameObject item, Transform parent, int index)
        {
            var rect = item.GetComponent<RectTransform>();
            if (rect == null) return;

            // LayoutGroup이 있으면 자동 배치되므로 위치 설정 불필요
            var layoutGroup = parent.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (layoutGroup != null) return;

            // PXListView의 설정 참조
            var listView = parent.GetComponentInParent<PXListView>();
            var listViewCore = parent.GetComponentInParent<PXListViewCore>();

            // itemSize 가져오기 (기본값 100)
            float itemSize = listView != null ? listView.itemSize : 100f;

            // 초기 위치 결정 (PXListViewCore와 동일한 로직)
            // 핵심: anchoredPosition이 아닌 localPosition 사용!
            Vector3 initVec = Vector3.zero;
            if (listViewCore != null)
            {
                if (listViewCore.locationType == DetemineLocationType.BaseOnObjectCreate)
                {
                    // 프리팹 원본의 localPosition 사용
                    initVec = rect.localPosition;
                }
                else
                {
                    // override 값 사용
                    initVec = new Vector3(listViewCore.overrideX, listViewCore.overrideY, 0);
                }

                // Pivot/Anchor 설정 및 위치 계산 (PXListViewCore.cs 라인 166-205, 558-584 참조)
                if (listViewCore.type == InfinityType.Vertical)
                {
                    if (listViewCore.verticalType == VerticalType.TopToBottom)
                    {
                        // TopToBottom: pivot.y = 1, Y 음수 방향으로 배치
                        rect.pivot = new Vector2(rect.pivot.x, 1);
                        rect.localPosition = new Vector3(initVec.x, -itemSize * index, 0);
                    }
                    else // BottomToTop
                    {
                        // BottomToTop: anchor/pivot Y = 0, Y 양수 방향으로 배치
                        rect.anchorMin = new Vector2(rect.anchorMin.x, 0);
                        rect.anchorMax = new Vector2(rect.anchorMax.x, 0);
                        rect.pivot = new Vector2(rect.pivot.x, 0);
                        rect.localPosition = new Vector3(initVec.x, itemSize * index, 0);
                    }
                }
                else // Horizontal
                {
                    if (listViewCore.horizontalType == HorizontalType.LeftToRight)
                    {
                        // LeftToRight: pivot.x = 0, X 양수 방향으로 배치
                        rect.pivot = new Vector2(0, rect.pivot.y);
                        rect.localPosition = new Vector3(itemSize * index, initVec.y, 0);
                    }
                    else // RightToLeft
                    {
                        // RightToLeft: anchor/pivot X = 1, X 음수 방향으로 배치
                        rect.anchorMin = new Vector2(1, rect.anchorMin.y);
                        rect.anchorMax = new Vector2(1, rect.anchorMax.y);
                        rect.pivot = new Vector2(1, rect.pivot.y);
                        rect.localPosition = new Vector3(-itemSize * index, initVec.y, 0);
                    }
                }
            }
            else
            {
                // listViewCore가 없으면 기본 세로 배치
                rect.localPosition = new Vector3(0, -itemSize * index, 0);
            }
        }

        /// <summary>
        /// Content 크기 조정
        /// </summary>
        private void UpdateContentSize(GameObject container, int itemCount, float itemSize)
        {
            var listView = container.GetComponent<PXListView>();
            if (listView == null) return;

            var content = listView.GetContent;
            if (content == null) return;

            var listViewCore = container.GetComponentInChildren<PXListViewCore>();
            float totalSize = itemCount * itemSize;

            if (listViewCore != null)
            {
                if (listViewCore.type == InfinityType.Vertical)
                {
                    content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalSize);
                }
                else
                {
                    content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, totalSize);
                }
            }
            else
            {
                // 기본 세로
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalSize);
            }
        }

        /// <summary>
        /// 캡처된 RectTransform에서 크기/스케일만 적용 (위치는 스크롤 상태라 제외)
        /// </summary>
        private void ApplyRectTransformSizeOnly(GameObject item, RuntimeRectTransformData rtData)
        {
            var rect = item.GetComponent<RectTransform>();
            if (rect == null || rtData == null) return;

            // SizeDelta 적용 (크기)
            if (rtData.SizeDelta != null && rtData.SizeDelta.Length >= 2)
            {
                rect.sizeDelta = new Vector2(rtData.SizeDelta[0], rtData.SizeDelta[1]);
            }

            // LocalScale 적용
            if (rtData.LocalScale != null && rtData.LocalScale.Length >= 3)
            {
                rect.localScale = new Vector3(rtData.LocalScale[0], rtData.LocalScale[1], rtData.LocalScale[2]);
            }

            // LocalRotation 적용
            if (rtData.LocalRotation != null && rtData.LocalRotation.Length >= 3)
            {
                rect.localEulerAngles = new Vector3(rtData.LocalRotation[0], rtData.LocalRotation[1], rtData.LocalRotation[2]);
            }

            // 주의: anchoredPosition, anchor, pivot은 적용하지 않음
            // - anchoredPosition: 스크롤된 런타임 상태의 절대 좌표라 사용 불가
            // - anchor/pivot: SetItemPosition에서 PXListViewCore 설정에 따라 지정
        }

        /// <summary>
        /// 런타임 데이터 적용
        /// </summary>
        private void ApplyRuntimeData(GameObject item, Dictionary<string, Dictionary<string, object>> runtimeData)
        {
            foreach (var kvp in runtimeData)
            {
                var componentName = kvp.Key;
                var data = kvp.Value;

                switch (componentName)
                {
                    case "PXText":
                    case "TextMeshProUGUI":
                        ApplyTextData(item, data);
                        break;

                    case "PXProgress":
                        ApplyProgressData(item, data);
                        break;

                    case "PXButton":
                        ApplyButtonData(item, data);
                        break;
                }
            }

            // 자식에도 적용
            foreach (var childKvp in runtimeData)
            {
                var childName = childKvp.Key;
                var childTransform = item.transform.Find(childName);

                if (childTransform != null && childKvp.Value is Dictionary<string, object> childData)
                {
                    ApplyTextData(childTransform.gameObject, childData);
                }
            }
        }

        private void ApplyTextData(GameObject obj, Dictionary<string, object> data)
        {
            var tmp = obj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null && data.ContainsKey("text"))
            {
                tmp.text = data["text"]?.ToString() ?? "";
            }
        }

        private void ApplyProgressData(GameObject obj, Dictionary<string, object> data)
        {
            var progress = obj.GetComponentInChildren<PXProgress>();
            if (progress != null)
            {
                if (data.ContainsKey("value"))
                {
                    progress.value = System.Convert.ToSingle(data["value"]);
                }
                if (data.ContainsKey("maxValue"))
                {
                    progress.maxValue = System.Convert.ToSingle(data["maxValue"]);
                }
            }
        }

        private void ApplyButtonData(GameObject obj, Dictionary<string, object> data)
        {
            var button = obj.GetComponentInChildren<PXButton>();
            if (button != null)
            {
                if (data.ContainsKey("interactable"))
                {
                    button.interactable = System.Convert.ToBoolean(data["interactable"]);
                }
            }

            if (data.ContainsKey("buttonText"))
            {
                var tmp = obj.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = data["buttonText"]?.ToString() ?? "";
                }
            }
        }

        /// <summary>
        /// 재귀적으로 HideFlags 적용
        /// </summary>
        private void SetHideFlagsRecursive(GameObject obj, HideFlags flags)
        {
            obj.hideFlags = flags;
            foreach (Transform child in obj.transform)
            {
                SetHideFlagsRecursive(child.gameObject, flags);
            }
        }

        /// <summary>
        /// 경로로 GameObject 찾기
        /// </summary>
        private GameObject FindGameObjectByPath(GameObject root, string path)
        {
            // (Clone) 제거 - 런타임 캡처 시 이름에 (Clone)이 붙음
            var normalizedPath = path.Replace("(Clone)", "");
            var rootName = root.name.Replace("(Clone)", "");

            // 경로 매핑에서 찾기
            if (_pathToGameObject.TryGetValue(path, out var obj))
            {
                return obj;
            }
            if (_pathToGameObject.TryGetValue(normalizedPath, out obj))
            {
                return obj;
            }

            // 상대 경로로 시도
            var relativePath = normalizedPath;
            if (normalizedPath.StartsWith(rootName + "/"))
            {
                relativePath = normalizedPath.Substring(rootName.Length + 1);
            }
            else if (normalizedPath == rootName)
            {
                return root;
            }

            // Transform.Find로 시도
            var found = root.transform.Find(relativePath);
            return found?.gameObject;
        }

        /// <summary>
        /// 템플릿 오브젝트 비활성화 (원본과 겹침 방지)
        /// </summary>
        private void HideTemplateObjects(Transform parentTransform)
        {
            if (parentTransform == null) return;

            // _Copy 접미사가 붙은 템플릿 오브젝트 비활성화
            foreach (Transform child in parentTransform)
            {
                if (child.name.EndsWith("_Copy"))
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        #endregion
    }
}
