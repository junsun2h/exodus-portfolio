// 동적 컨테이너 감지기
// 런타임에 아이템을 동적으로 생성하는 컨테이너를 감지하고 원본 프리팹 경로를 추출

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using PX;
#if UNITY_EDITOR
using UnityEditor;
using BattleSimulator;
#endif

namespace PX.UIAutomation
{
    /// <summary>
    /// 동적 컨테이너 감지기
    /// PXListView, 라인 아이템, 보상 슬롯 등 런타임에 아이템을 생성하는 컨테이너를 감지
    /// </summary>
    public class DynamicContainerDetector
    {
        #region 알려진 컨테이너 정의

        /// <summary>
        /// 알려진 동적 컨테이너 타입과 프리팹 필드명
        /// </summary>
        private static readonly List<ContainerDefinition> KnownContainers = new List<ContainerDefinition>
        {
            // PXListView 계열
            new ContainerDefinition
            {
                TypeName = "PX.PXListView",
                PrefabFieldName = "Prefab",
                PrefabFieldType = PrefabFieldType.SerializeField,  // Prefab은 public field
                GetTotalItemCount = GetListViewTotalCount,
                GetVisibleItemCount = GetListViewVisibleCount,
                GetItemSize = GetListViewItemSize
            },

            // 보상 슬롯
            new ContainerDefinition
            {
                TypeName = "PX.PXWidget_Reward",
                PrefabFieldName = "rewardSlotPrefab",
                PrefabFieldType = PrefabFieldType.SerializeField
            },

            // 라인 아이템 (BaseLine 계열)
            new ContainerDefinition
            {
                TypeName = "PX.PXWidget_CommonSlotAll_LineItem",
                PrefabFieldName = "_OriginItemPrefab",
                PrefabFieldType = PrefabFieldType.SerializeField
            },
            new ContainerDefinition
            {
                TypeName = "PX.PXListViewItem_StoreSmallSlotLine",
                PrefabFieldName = "originItemPrefab",
                PrefabFieldType = PrefabFieldType.SerializeField
            },

            // 옵션 데이터 컨테이너
            new ContainerDefinition
            {
                TypeName = "PX.PXScrollView_Vertical_OptionData",
                PrefabFieldName = "_PXWidget_EquipDetailSingle_Copy",
                PrefabFieldType = PrefabFieldType.SerializeField
            },
            new ContainerDefinition
            {
                TypeName = "PX.PXWidget_DetailSingle",
                PrefabFieldName = "_PXWidget_Option_Copy",
                PrefabFieldType = PrefabFieldType.SerializeField
            },

            // Stage 리워드 위젯들
            new ContainerDefinition
            {
                TypeName = "PX.PXWidget_StageRift_Right",
                PrefabFieldName = "_OriginItemPrefab",
                PrefabFieldType = PrefabFieldType.SerializeField
            },
            new ContainerDefinition
            {
                TypeName = "PX.PXWidget_StageCorruption_Right",
                PrefabFieldName = "_OriginItemPrefab",
                PrefabFieldType = PrefabFieldType.SerializeField
            },
            new ContainerDefinition
            {
                TypeName = "PX.PXWidget_StageInvasion_Right",
                PrefabFieldName = "_OriginItemPrefab",
                PrefabFieldType = PrefabFieldType.SerializeField
            },
            new ContainerDefinition
            {
                TypeName = "PX.PXWidget_StageGreatRift_Right",
                PrefabFieldName = "_OriginItemPrefab",
                PrefabFieldType = PrefabFieldType.SerializeField
            },
            new ContainerDefinition
            {
                TypeName = "PX.PXWidget_StageIncursion_Right",
                PrefabFieldName = "_OriginItemPrefab",
                PrefabFieldType = PrefabFieldType.SerializeField
            }
        };

        #endregion

        #region Public API

        /// <summary>
        /// GameObject에서 동적 컨테이너 감지
        /// </summary>
        /// <param name="obj">검사할 GameObject</param>
        /// <returns>동적 컨테이너 정보, 없으면 null</returns>
        public DynamicContainerInfo Detect(GameObject obj)
        {
            if (obj == null) return null;

            foreach (var definition in KnownContainers)
            {
                var component = GetComponentByTypeName(obj, definition.TypeName);
                if (component != null)
                {
                    return ExtractContainerInfo(component, definition);
                }
            }

            // 알려진 컨테이너가 아닌 경우, 프리팹 필드 패턴으로 감지 시도
            return DetectByFieldPattern(obj);
        }

        /// <summary>
        /// 자식 GameObject가 동적으로 생성된 아이템인지 확인
        /// </summary>
        /// <param name="child">검사할 자식 GameObject</param>
        /// <param name="parent">부모 GameObject</param>
        /// <returns>동적 아이템 여부</returns>
        public bool IsDynamicItem(GameObject child, GameObject parent)
        {
            if (child == null || parent == null) return false;

            // 이름 패턴으로 확인 (item_0, item_1, ...)
            if (child.name.StartsWith("item_") || child.name.StartsWith("Item_"))
            {
                return true;
            }

            // (Clone) 접미사로 확인 - 런타임에 Instantiate된 오브젝트
            if (child.name.Contains("(Clone)"))
            {
                return true;
            }

            // PXListView의 Content 자식 확인
            var listView = parent.GetComponent<PXListView>();
            if (listView != null)
            {
                var content = listView.GetContent;
                if (content != null && child.transform.parent == content)
                {
                    return true;
                }
            }

            // PXScrollView_Vertical_OptionData의 Content 자식 확인
            var optionDataContainer = parent.GetComponentInParent<PXScrollView_Vertical_OptionData>();
            if (optionDataContainer != null)
            {
                var scrollViewField = optionDataContainer.GetType().GetField("_PXScrollView_Vertical_Right",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (scrollViewField != null)
                {
                    var scrollView = scrollViewField.GetValue(optionDataContainer) as PXScrollView;
                    if (scrollView != null)
                    {
                        var content = scrollView.GetPanelContent;
                        if (content != null && child.transform.parent == content)
                        {
                            if (!child.name.EndsWith("_Copy") && child.activeInHierarchy)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            // PXScrollView 계열의 Content 자식 확인 (일반적인 경우)
            var pxScrollView = parent.GetComponentInParent<PXScrollView>();
            if (pxScrollView != null)
            {
                var content = pxScrollView.GetPanelContent;
                if (content != null && child.transform.parent == content)
                {
                    if (!child.name.EndsWith("_Copy") && child.activeInHierarchy)
                    {
                        return true;
                    }
                }
            }

            // PXWidget_DetailSingle의 OptionArea 자식 확인
            var detailSingle = parent.GetComponentInParent<PXWidget_DetailSingle>();
            if (detailSingle != null)
            {
                // parent가 OptionArea이고, parent의 부모가 detailSingle인지 확인
                bool isOptionAreaChild = parent.name == "OptionArea" &&
                                         parent.transform.parent == detailSingle.transform;

                if (isOptionAreaChild)
                {
                    if (!child.name.EndsWith("_Copy") && child.activeInHierarchy)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 동적 아이템의 인덱스 추출
        /// </summary>
        public int GetDynamicItemIndex(GameObject item)
        {
            if (item == null) return -1;

            // 이름에서 인덱스 추출 시도 (item_0, item_1, ...)
            var name = item.name;
            if (name.StartsWith("item_") || name.StartsWith("Item_"))
            {
                var indexStr = name.Substring(5);
                if (int.TryParse(indexStr, out int index))
                {
                    return index;
                }
            }

            // 형제 순서로 인덱스 추출
            return item.transform.GetSiblingIndex();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 타입 이름으로 컴포넌트 가져오기
        /// </summary>
        private Component GetComponentByTypeName(GameObject obj, string typeName)
        {
            var components = obj.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null) continue;

                var type = component.GetType();
                if (type.FullName == typeName || type.Name == typeName.Split('.')[^1])
                {
                    return component;
                }
            }
            return null;
        }

        /// <summary>
        /// 컨테이너 정보 추출
        /// </summary>
        private DynamicContainerInfo ExtractContainerInfo(Component component, ContainerDefinition definition)
        {
            var info = new DynamicContainerInfo
            {
                ContainerType = component.GetType().Name,
                ItemPrefabField = definition.PrefabFieldName
            };

            // 프리팹 경로 추출
            var prefab = GetPrefabFromComponent(component, definition);
            if (prefab != null)
            {
#if UNITY_EDITOR
                info.ItemPrefabPath = GetPrefabAssetPath(prefab);
#endif
            }

            // 추가 정보 추출
            if (definition.GetTotalItemCount != null)
            {
                info.TotalItemCount = definition.GetTotalItemCount(component);
            }
            if (definition.GetVisibleItemCount != null)
            {
                info.VisibleItemCount = definition.GetVisibleItemCount(component);
            }
            if (definition.GetItemSize != null)
            {
                info.ItemSize = definition.GetItemSize(component);
            }

            return info;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 오브젝트에서 프리팹 에셋 경로 추출 (프리팹 내부 오브젝트도 처리)
        /// </summary>
        private string GetPrefabAssetPath(GameObject obj)
        {
            if (obj == null) return null;

            // 1. 직접 에셋 경로 시도 (독립 프리팹인 경우)
            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return assetPath;
            }

            // 2. 프리팹 원본에서 시도 (프리팹 인스턴스 내부 오브젝트인 경우)
            var sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(obj);
            if (sourceObject != null)
            {
                assetPath = AssetDatabase.GetAssetPath(sourceObject);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    return assetPath;
                }
            }

            // 3. 오브젝트 타입과 이름으로 독립 프리팹 검색 시도
            // 예: PXWidget_DetailSingle_Option 타입 → PXWidget_DetailSingle_Option.prefab 검색
            var componentType = obj.GetComponent<MonoBehaviour>();
            if (componentType != null)
            {
                var typeName = componentType.GetType().Name;
                var guids = AssetDatabase.FindAssets($"t:Prefab {typeName}");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefabName = System.IO.Path.GetFileNameWithoutExtension(path);
                    // 타입 이름과 프리팹 이름이 일치하는지 확인
                    if (prefabName == typeName || prefabName.EndsWith(typeName))
                    {
                        // 프리팹에 해당 컴포넌트가 있는지 확인
                        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefabAsset != null && prefabAsset.GetComponent(componentType.GetType()) != null)
                        {
                            return path;
                        }
                    }
                }
            }

            // 4. 오브젝트 이름으로 프리팹 검색 (fallback)
            var objName = obj.name.Replace("(Clone)", "").Replace("_Copy", "").Trim();
            var fallbackGuids = AssetDatabase.FindAssets($"t:Prefab {objName}");
            foreach (var guid in fallbackGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefabName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (prefabName == objName)
                {
                    return path;
                }
            }

            return null;
        }
#endif

        /// <summary>
        /// 컴포넌트에서 프리팹 가져오기
        /// </summary>
        private GameObject GetPrefabFromComponent(Component component, ContainerDefinition definition)
        {
            var type = component.GetType();

            if (definition.PrefabFieldType == PrefabFieldType.Property)
            {
                // Property 접근
                var property = type.GetProperty(definition.PrefabFieldName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                {
                    return property.GetValue(component) as GameObject;
                }
            }
            else
            {
                // SerializeField 접근
                var field = type.GetField(definition.PrefabFieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(component);
                    if (value is GameObject go) return go;
                    if (value is Component comp) return comp.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 필드 패턴으로 동적 컨테이너 감지 (fallback)
        /// </summary>
        private DynamicContainerInfo DetectByFieldPattern(GameObject obj)
        {
            var components = obj.GetComponents<Component>();

            // 프리팹 필드 패턴 검색
            string[] prefabFieldPatterns = { "Prefab", "prefab", "_Prefab", "_prefab",
                "OriginItemPrefab", "_OriginItemPrefab", "originItemPrefab",
                "ItemPrefab", "itemPrefab", "_ItemPrefab" };

            foreach (var component in components)
            {
                if (component == null) continue;

                var type = component.GetType();
                var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    foreach (var pattern in prefabFieldPatterns)
                    {
                        if (field.Name.Contains(pattern) &&
                            (field.FieldType == typeof(GameObject) ||
                             typeof(Component).IsAssignableFrom(field.FieldType)))
                        {
                            var value = field.GetValue(component);
                            GameObject prefab = null;

                            if (value is GameObject go) prefab = go;
                            else if (value is Component comp) prefab = comp?.gameObject;

                            if (prefab != null)
                            {
                                var info = new DynamicContainerInfo
                                {
                                    ContainerType = type.Name,
                                    ItemPrefabField = field.Name
                                };
#if UNITY_EDITOR
                                info.ItemPrefabPath = AssetDatabase.GetAssetPath(prefab);
#endif
                                return info;
                            }
                        }
                    }
                }
            }

            return null;
        }

        #endregion

        #region PXListView 전용 헬퍼

        private static int GetListViewTotalCount(Component component)
        {
            if (component is PXListView listView)
            {
                return listView.GetDataCount;
            }
            return 0;
        }

        private static int GetListViewVisibleCount(Component component)
        {
            if (component is PXListView listView)
            {
                var content = listView.GetContent;
                if (content != null)
                {
                    return content.childCount;
                }
            }
            return 0;
        }

        private static float GetListViewItemSize(Component component)
        {
            if (component is PXListView listView)
            {
                return listView.itemSize;
            }
            return 0;
        }

        #endregion

        #region 내부 클래스

        private enum PrefabFieldType
        {
            Property,
            SerializeField
        }

        private class ContainerDefinition
        {
            public string TypeName { get; set; }
            public string PrefabFieldName { get; set; }
            public PrefabFieldType PrefabFieldType { get; set; }
            public Func<Component, int> GetTotalItemCount { get; set; }
            public Func<Component, int> GetVisibleItemCount { get; set; }
            public Func<Component, float> GetItemSize { get; set; }
        }

        #endregion
    }
}
