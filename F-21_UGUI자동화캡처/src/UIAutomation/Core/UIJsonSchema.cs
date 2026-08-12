// UI 자동화 시스템 - JSON 스키마 데이터 클래스
// Claude Code와 Unity Editor 간 데이터 교환용

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace PX.UIAutomation
{
    #region UI 편집용 스키마

    /// <summary>
    /// UI 수정 데이터 (최상위)
    /// </summary>
    [Serializable]
    public class UIModificationData
    {
        [JsonProperty("$schema")]
        public string Schema { get; set; } = "UIModificationSchema/v1.0";

        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("prefabPath")]
        public string PrefabPath { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("modifications")]
        public List<UIModificationItem> Modifications { get; set; } = new List<UIModificationItem>();
    }

    /// <summary>
    /// 개별 수정 항목
    /// </summary>
    [Serializable]
    public class UIModificationItem
    {
        [JsonProperty("gameObjectPath")]
        public string GameObjectPath { get; set; }

        [JsonProperty("componentType")]
        public string ComponentType { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// true: 원본 프리팹을 수정 (모든 인스턴스에 일괄 반영)
        /// false: 로컬 Override만 적용 (기본값)
        /// </summary>
        [JsonProperty("modifySourcePrefab")]
        public bool ModifySourcePrefab { get; set; } = false;
    }

    #endregion

    #region 새 UI 생성용 스키마

    /// <summary>
    /// UI 생성 데이터 (최상위)
    /// </summary>
    [Serializable]
    public class UICreationData
    {
        [JsonProperty("$schema")]
        public string Schema { get; set; } = "UICreationSchema/v1.0";

        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("uiType")]
        public string UIType { get; set; } // Popup, Widget, Panel

        [JsonProperty("className")]
        public string ClassName { get; set; }

        [JsonProperty("namespace")]
        public string Namespace { get; set; } = "PX";

        [JsonProperty("baseClass")]
        public string BaseClass { get; set; }

        [JsonProperty("popupKey")]
        public string PopupKey { get; set; }

        [JsonProperty("fullScreenType")]
        public string FullScreenType { get; set; } = "UINotFull";

        [JsonProperty("outputPaths")]
        public UIOutputPaths OutputPaths { get; set; } = new UIOutputPaths();

        [JsonProperty("bindings")]
        public List<UIBindingItem> Bindings { get; set; } = new List<UIBindingItem>();

        [JsonProperty("hierarchy")]
        public List<UIHierarchyNode> Hierarchy { get; set; } = new List<UIHierarchyNode>();

        [JsonProperty("methods")]
        public List<UIMethodDefinition> Methods { get; set; } = new List<UIMethodDefinition>();
    }

    /// <summary>
    /// 출력 경로
    /// </summary>
    [Serializable]
    public class UIOutputPaths
    {
        [JsonProperty("script")]
        public string Script { get; set; }

        [JsonProperty("prefab")]
        public string Prefab { get; set; }
    }

    /// <summary>
    /// SerializeField 바인딩 항목
    /// </summary>
    [Serializable]
    public class UIBindingItem
    {
        [JsonProperty("fieldName")]
        public string FieldName { get; set; }

        [JsonProperty("fieldType")]
        public string FieldType { get; set; }

        [JsonProperty("gameObjectName")]
        public string GameObjectName { get; set; }

        [JsonProperty("required")]
        public bool Required { get; set; }

        [JsonProperty("events")]
        public List<UIEventBinding> Events { get; set; }
    }

    /// <summary>
    /// 이벤트 바인딩 정보
    /// </summary>
    [Serializable]
    public class UIEventBinding
    {
        [JsonProperty("eventName")]
        public string EventName { get; set; }

        [JsonProperty("handlerName")]
        public string HandlerName { get; set; }

        [JsonProperty("parameterType")]
        public string ParameterType { get; set; }
    }

    /// <summary>
    /// UI 계층 구조 노드
    /// </summary>
    [Serializable]
    public class UIHierarchyNode
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("prefab")]
        public string Prefab { get; set; }

        [JsonProperty("rect")]
        public UIRectSettings Rect { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, object> Properties { get; set; }

        [JsonProperty("overrides")]
        public Dictionary<string, Dictionary<string, object>> Overrides { get; set; }

        [JsonProperty("children")]
        public List<UIHierarchyNode> Children { get; set; }
    }

    /// <summary>
    /// RectTransform 설정
    /// </summary>
    [Serializable]
    public class UIRectSettings
    {
        [JsonProperty("anchorMin")]
        public float[] AnchorMin { get; set; }

        [JsonProperty("anchorMax")]
        public float[] AnchorMax { get; set; }

        [JsonProperty("pivot")]
        public float[] Pivot { get; set; }

        [JsonProperty("sizeDelta")]
        public float[] SizeDelta { get; set; }

        [JsonProperty("anchoredPosition")]
        public float[] AnchoredPosition { get; set; }

        [JsonProperty("offsetMin")]
        public float[] OffsetMin { get; set; }

        [JsonProperty("offsetMax")]
        public float[] OffsetMax { get; set; }
    }

    /// <summary>
    /// 메서드 정의
    /// </summary>
    [Serializable]
    public class UIMethodDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("visibility")]
        public string Visibility { get; set; } = "public";

        [JsonProperty("returnType")]
        public string ReturnType { get; set; } = "void";

        [JsonProperty("parameters")]
        public List<UIMethodParameter> Parameters { get; set; } = new List<UIMethodParameter>();

        [JsonProperty("body")]
        public string Body { get; set; }
    }

    /// <summary>
    /// 메서드 파라미터
    /// </summary>
    [Serializable]
    public class UIMethodParameter
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }

    #endregion

    #region 런타임 캡처용 스키마 (v2.0)

    /// <summary>
    /// 런타임 캡처 데이터 (최상위)
    /// Play Mode에서 캡처된 UI 구조와 실제 데이터
    /// </summary>
    [Serializable]
    public class RuntimeCaptureData
    {
        [JsonProperty("$schema")]
        public string Schema { get; set; } = "RuntimeCaptureSchema/v2.0";

        [JsonProperty("captureTime")]
        public string CaptureTime { get; set; }

        [JsonProperty("sourcePopupKey")]
        public string SourcePopupKey { get; set; }

        [JsonProperty("rootPrefabPath")]
        public string RootPrefabPath { get; set; }

        [JsonProperty("rootName")]
        public string RootName { get; set; }

        [JsonProperty("hierarchy")]
        public List<RuntimeHierarchyNode> Hierarchy { get; set; } = new List<RuntimeHierarchyNode>();
    }

    /// <summary>
    /// 런타임 계층 구조 노드
    /// </summary>
    [Serializable]
    public class RuntimeHierarchyNode
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; } = true;

        [JsonProperty("components")]
        public List<string> Components { get; set; } = new List<string>();

        [JsonProperty("rectTransform")]
        public RuntimeRectTransformData RectTransform { get; set; }

        /// <summary>
        /// 동적 컨테이너 정보 (PXListView 등)
        /// </summary>
        [JsonProperty("dynamicContainer")]
        public DynamicContainerInfo DynamicContainer { get; set; }

        /// <summary>
        /// 동적으로 생성된 아이템인지 여부
        /// </summary>
        [JsonProperty("isDynamicItem")]
        public bool IsDynamicItem { get; set; }

        /// <summary>
        /// 동적 아이템의 인덱스
        /// </summary>
        [JsonProperty("itemIndex")]
        public int ItemIndex { get; set; } = -1;

        /// <summary>
        /// 런타임 데이터 (컴포넌트명 → 데이터)
        /// </summary>
        [JsonProperty("runtimeData")]
        public Dictionary<string, Dictionary<string, object>> RuntimeData { get; set; }

        [JsonProperty("children")]
        public List<RuntimeHierarchyNode> Children { get; set; } = new List<RuntimeHierarchyNode>();
    }

    /// <summary>
    /// RectTransform 런타임 데이터
    /// </summary>
    [Serializable]
    public class RuntimeRectTransformData
    {
        [JsonProperty("anchoredPosition")]
        public float[] AnchoredPosition { get; set; }

        [JsonProperty("sizeDelta")]
        public float[] SizeDelta { get; set; }

        [JsonProperty("anchorMin")]
        public float[] AnchorMin { get; set; }

        [JsonProperty("anchorMax")]
        public float[] AnchorMax { get; set; }

        [JsonProperty("pivot")]
        public float[] Pivot { get; set; }

        [JsonProperty("localScale")]
        public float[] LocalScale { get; set; }

        [JsonProperty("localRotation")]
        public float[] LocalRotation { get; set; }
    }

    /// <summary>
    /// 동적 컨테이너 정보
    /// </summary>
    [Serializable]
    public class DynamicContainerInfo
    {
        /// <summary>
        /// 컨테이너 타입 (PXListView, PXWidget_Reward 등)
        /// </summary>
        [JsonProperty("containerType")]
        public string ContainerType { get; set; }

        /// <summary>
        /// 아이템 프리팹 경로
        /// </summary>
        [JsonProperty("itemPrefabPath")]
        public string ItemPrefabPath { get; set; }

        /// <summary>
        /// 아이템 프리팹 필드명
        /// </summary>
        [JsonProperty("itemPrefabField")]
        public string ItemPrefabField { get; set; }

        /// <summary>
        /// 전체 아이템 수 (데이터 기준)
        /// </summary>
        [JsonProperty("totalItemCount")]
        public int TotalItemCount { get; set; }

        /// <summary>
        /// 현재 보이는 아이템 수
        /// </summary>
        [JsonProperty("visibleItemCount")]
        public int VisibleItemCount { get; set; }

        /// <summary>
        /// 아이템 크기 (PXListView용)
        /// </summary>
        [JsonProperty("itemSize")]
        public float ItemSize { get; set; }
    }

    #endregion

    #region 유틸리티 클래스

    /// <summary>
    /// JSON 파싱 유틸리티
    /// </summary>
    public static class UIJsonParser
    {
        /// <summary>
        /// UI 수정 JSON 파싱
        /// </summary>
        public static UIModificationData ParseModification(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<UIModificationData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIJsonParser] 수정 JSON 파싱 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// UI 생성 JSON 파싱
        /// </summary>
        public static UICreationData ParseCreation(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<UICreationData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIJsonParser] 생성 JSON 파싱 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 런타임 캡처 JSON 파싱
        /// </summary>
        public static RuntimeCaptureData ParseRuntimeCapture(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<RuntimeCaptureData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIJsonParser] 런타임 캡처 JSON 파싱 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 런타임 캡처 데이터를 JSON으로 직렬화
        /// </summary>
        public static string SerializeRuntimeCapture(RuntimeCaptureData data)
        {
            try
            {
                return JsonConvert.SerializeObject(data, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIJsonParser] 런타임 캡처 JSON 직렬화 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Vector2 파싱 (Dictionary에서)
        /// </summary>
        public static Vector2 ParseVector2(object obj)
        {
            if (obj is Newtonsoft.Json.Linq.JObject jObj)
            {
                float x = jObj["x"] != null ? (float)jObj["x"] : 0f;
                float y = jObj["y"] != null ? (float)jObj["y"] : 0f;
                return new Vector2(x, y);
            }
            return Vector2.zero;
        }

        /// <summary>
        /// Color 파싱 (Dictionary에서)
        /// </summary>
        public static Color ParseColor(object obj)
        {
            if (obj is Newtonsoft.Json.Linq.JObject jObj)
            {
                float r = jObj["r"] != null ? (float)jObj["r"] : 1f;
                float g = jObj["g"] != null ? (float)jObj["g"] : 1f;
                float b = jObj["b"] != null ? (float)jObj["b"] : 1f;
                float a = jObj["a"] != null ? (float)jObj["a"] : 1f;
                return new Color(r, g, b, a);
            }
            return Color.white;
        }

        /// <summary>
        /// float[] 배열을 Vector2로 변환
        /// </summary>
        public static Vector2 ToVector2(float[] arr)
        {
            if (arr == null || arr.Length < 2) return Vector2.zero;
            return new Vector2(arr[0], arr[1]);
        }
    }

    #endregion
}
