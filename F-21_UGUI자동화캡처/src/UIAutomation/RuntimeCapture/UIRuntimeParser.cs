// UI 런타임 파서
// Play Mode에서 UI 계층 구조를 파싱하여 RuntimeCaptureData 생성

using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using PX;
#if UNITY_EDITOR
using UnityEditor;
using BattleSimulator;
#endif

namespace PX.UIAutomation
{
    /// <summary>
    /// 런타임 UI 구조 파서
    /// Play Mode에서 UI 계층 구조와 실제 데이터를 파싱
    /// </summary>
    public class UIRuntimeParser
    {
        private DynamicContainerDetector _containerDetector;
        private HashSet<string> _processedPaths;

        public UIRuntimeParser()
        {
            _containerDetector = new DynamicContainerDetector();
            _processedPaths = new HashSet<string>();
        }

        #region Public API

        /// <summary>
        /// GameObject 계층 구조를 RuntimeCaptureData로 파싱
        /// </summary>
        /// <param name="root">파싱할 루트 GameObject</param>
        /// <returns>파싱된 RuntimeCaptureData</returns>
        public RuntimeCaptureData Parse(GameObject root)
        {
            if (root == null)
            {
                Debug.LogError("[UIRuntimeParser] 루트 GameObject가 null입니다.");
                return null;
            }

            _processedPaths.Clear();

            var data = new RuntimeCaptureData
            {
                CaptureTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                RootName = root.name
            };

            // 루트 프리팹 경로 추출
#if UNITY_EDITOR
            var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(root);
            if (prefabSource != null)
            {
                data.RootPrefabPath = AssetDatabase.GetAssetPath(prefabSource);
            }
#endif

            // PopupKey 추출 시도
            var popup = root.GetComponent<UIPopup>();
            if (popup != null)
            {
                data.SourcePopupKey = popup.PopupKey;
            }

            // 계층 구조 파싱
            var rootNode = ParseNode(root, null, "");
            if (rootNode != null)
            {
                data.Hierarchy.Add(rootNode);
            }

            return data;
        }

        #endregion

        #region Node Parsing

        /// <summary>
        /// 단일 노드 파싱
        /// </summary>
        private RuntimeHierarchyNode ParseNode(GameObject obj, DynamicContainerInfo parentContainer, string parentPath)
        {
            if (obj == null) return null;

            // 같은 이름의 sibling이 있을 수 있으므로 sibling index로 구분
            var siblingIndex = obj.transform.GetSiblingIndex();
            var uniqueName = obj.name;

            // 부모의 자식 중 같은 이름이 있는지 확인
            if (obj.transform.parent != null)
            {
                int sameNameCount = 0;
                for (int i = 0; i < obj.transform.parent.childCount; i++)
                {
                    if (obj.transform.parent.GetChild(i).name == obj.name)
                    {
                        sameNameCount++;
                        if (sameNameCount > 1) break;
                    }
                }
                // 같은 이름이 2개 이상이면 인덱스 추가
                if (sameNameCount > 1)
                {
                    uniqueName = $"{obj.name}[{siblingIndex}]";
                }
            }

            var currentPath = string.IsNullOrEmpty(parentPath) ? uniqueName : $"{parentPath}/{uniqueName}";

            // 중복 방지
            if (_processedPaths.Contains(currentPath))
            {
                return null;
            }
            _processedPaths.Add(currentPath);

            var node = new RuntimeHierarchyNode
            {
                Name = obj.name,
                Path = currentPath,
                Active = obj.activeInHierarchy
            };

            // 컴포넌트 목록 추출
            node.Components = ExtractComponentNames(obj);

            // RectTransform 정보 추출
            var rectTransform = obj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                node.RectTransform = ExtractRectTransform(rectTransform);
            }

            // 동적 컨테이너 감지
            var containerInfo = _containerDetector.Detect(obj);
            if (containerInfo != null)
            {
                node.DynamicContainer = containerInfo;
            }

            // 부모가 동적 컨테이너인 경우, 동적 아이템 여부 확인
            if (parentContainer != null)
            {
                var parent = obj.transform.parent?.gameObject;
                if (parent != null && _containerDetector.IsDynamicItem(obj, parent))
                {
                    node.IsDynamicItem = true;
                    node.ItemIndex = _containerDetector.GetDynamicItemIndex(obj);
                }
            }

            // 런타임 데이터 추출
            node.RuntimeData = ExtractRuntimeData(obj);

            // 자식 노드 파싱
            var effectiveContainer = containerInfo ?? parentContainer;
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                var child = obj.transform.GetChild(i).gameObject;

                // 비활성 오브젝트도 포함 (선택적)
                var childNode = ParseNode(child, effectiveContainer, currentPath);
                if (childNode != null)
                {
                    node.Children.Add(childNode);
                }
            }

            return node;
        }

        #endregion

        #region Data Extraction

        /// <summary>
        /// 컴포넌트 이름 목록 추출
        /// </summary>
        private List<string> ExtractComponentNames(GameObject obj)
        {
            var names = new List<string>();
            var components = obj.GetComponents<Component>();

            foreach (var component in components)
            {
                if (component == null) continue;

                var typeName = component.GetType().Name;

                // Transform/RectTransform은 제외 (별도로 처리)
                if (typeName == "Transform" || typeName == "RectTransform") continue;

                // CanvasRenderer 등 내부 컴포넌트 제외
                if (typeName == "CanvasRenderer") continue;

                names.Add(typeName);
            }

            return names;
        }

        /// <summary>
        /// RectTransform 정보 추출
        /// </summary>
        private RuntimeRectTransformData ExtractRectTransform(RectTransform rect)
        {
            return new RuntimeRectTransformData
            {
                AnchoredPosition = new float[] { rect.anchoredPosition.x, rect.anchoredPosition.y },
                SizeDelta = new float[] { rect.sizeDelta.x, rect.sizeDelta.y },
                AnchorMin = new float[] { rect.anchorMin.x, rect.anchorMin.y },
                AnchorMax = new float[] { rect.anchorMax.x, rect.anchorMax.y },
                Pivot = new float[] { rect.pivot.x, rect.pivot.y },
                LocalScale = new float[] { rect.localScale.x, rect.localScale.y, rect.localScale.z },
                LocalRotation = new float[] { rect.localEulerAngles.x, rect.localEulerAngles.y, rect.localEulerAngles.z }
            };
        }

        /// <summary>
        /// 런타임 데이터 추출 (PXUI 컴포넌트들)
        /// </summary>
        private Dictionary<string, Dictionary<string, object>> ExtractRuntimeData(GameObject obj)
        {
            var data = new Dictionary<string, Dictionary<string, object>>();

            // PXText 추출
            var pxText = obj.GetComponent<PXText>();
            if (pxText != null)
            {
                data["PXText"] = ExtractPXTextData(pxText);
            }

            // TextMeshProUGUI 추출 (PXText가 없는 경우)
            if (pxText == null)
            {
                var tmpText = obj.GetComponent<TextMeshProUGUI>();
                if (tmpText != null)
                {
                    data["TextMeshProUGUI"] = ExtractTMPData(tmpText);
                }
            }

            // PXImage 추출
            var pxImage = obj.GetComponent<PXImage>();
            if (pxImage != null)
            {
                data["PXImage"] = ExtractPXImageData(pxImage);
            }

            // PXButton 추출
            var pxButton = obj.GetComponent<PXButton>();
            if (pxButton != null)
            {
                data["PXButton"] = ExtractPXButtonData(pxButton);
            }

            // PXProgress 추출
            var pxProgress = obj.GetComponent<PXProgress>();
            if (pxProgress != null)
            {
                data["PXProgress"] = ExtractPXProgressData(pxProgress);
            }

            // PXListView 추출
            var pxListView = obj.GetComponent<PXListView>();
            if (pxListView != null)
            {
                data["PXListView"] = ExtractPXListViewData(pxListView);
            }

            return data.Count > 0 ? data : null;
        }

        #endregion

        #region Component Extractors

        private Dictionary<string, object> ExtractPXTextData(PXText text)
        {
            var data = new Dictionary<string, object>();

            // PXText의 실제 텍스트 값 추출 (TMP는 자식에 있을 수 있음)
            var tmpText = text.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
            {
                data["text"] = tmpText.text;
                data["fontSize"] = tmpText.fontSize;
                data["color"] = new float[] { tmpText.color.r, tmpText.color.g, tmpText.color.b, tmpText.color.a };
                data["alignment"] = tmpText.alignment.ToString();
            }

            return data;
        }

        private Dictionary<string, object> ExtractTMPData(TextMeshProUGUI tmp)
        {
            return new Dictionary<string, object>
            {
                { "text", tmp.text },
                { "fontSize", tmp.fontSize },
                { "color", new float[] { tmp.color.r, tmp.color.g, tmp.color.b, tmp.color.a } },
                { "alignment", tmp.alignment.ToString() }
            };
        }

        private Dictionary<string, object> ExtractPXImageData(PXImage image)
        {
            var data = new Dictionary<string, object>();

            var unityImage = image.GetComponent<UnityEngine.UI.Image>();
            if (unityImage != null)
            {
                data["color"] = new float[] { unityImage.color.r, unityImage.color.g, unityImage.color.b, unityImage.color.a };
                data["raycastTarget"] = unityImage.raycastTarget;

                if (unityImage.sprite != null)
                {
                    data["spriteName"] = unityImage.sprite.name;
#if UNITY_EDITOR
                    data["spritePath"] = AssetDatabase.GetAssetPath(unityImage.sprite);
#endif
                }
            }

            return data;
        }

        private Dictionary<string, object> ExtractPXButtonData(PXButton button)
        {
            var data = new Dictionary<string, object>
            {
                { "interactable", button.interactable }
            };

            // 버튼 텍스트 추출 (자식에서)
            var buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                data["buttonText"] = buttonText.text;
            }

            // Reddot 상태 확인
            var reddot = button.transform.Find("Reddot");
            if (reddot != null)
            {
                data["hasReddot"] = reddot.gameObject.activeSelf;
            }

            return data;
        }

        private Dictionary<string, object> ExtractPXProgressData(PXProgress progress)
        {
            var data = new Dictionary<string, object>
            {
                { "value", progress.value },
                { "minValue", progress.minValue },
                { "maxValue", progress.maxValue }
            };

            // 진행률 텍스트 추출 (있는 경우)
            var progressText = progress.GetComponentInChildren<TextMeshProUGUI>();
            if (progressText != null)
            {
                data["displayText"] = progressText.text;
            }

            return data;
        }

        private Dictionary<string, object> ExtractPXListViewData(PXListView listView)
        {
            return new Dictionary<string, object>
            {
                { "dataCount", listView.GetDataCount },
                { "itemSize", listView.itemSize },
                { "itemGenerate", listView.itemGenerate },
                { "visibleItemCount", listView.GetContent?.childCount ?? 0 }
            };
        }

        #endregion
    }
}
