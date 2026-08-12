using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PX
{
    public enum InfinityType
    {
        Vertical,
        Horizontal
    }

    public enum VerticalType
    {
        TopToBottom,
        BottomToTop
    }

    public enum HorizontalType
    {
        LeftToRight,
        RigthToLeft
    }

    public enum DetemineLocationType
    {
        BaseOnObjectCreate,
        OverrideLocation
    }

    public class PXListViewCore : BaseUserWidget
    {
        public class PXListViewCoreEvent : UnityEvent<int, PXListViewItem> { }

        public PXListViewCoreEvent OnListViewItemUpdate = new PXListViewCoreEvent();

        /*
        protected override void Reload(GameObject obj, int indexReload)
        {
            base.Reload(obj, indexReload);

        }
        */

        //////////////////////////////////
        ///
        public int GetCurrentLastIndex { get; private set; }

        [Header("Setting Reference object")]
        public GameObject prefab; // link object item
        public ScrollRect scrollRect;// link to UGUI scrollRect
        public RectTransform content;// link to content that contain all item in scrollrect

        [Header("Setting For Custom Scroll View")]
        public InfinityType type = InfinityType.Vertical;// type scrollview
        public VerticalType verticalType = VerticalType.TopToBottom;
        public HorizontalType horizontalType = HorizontalType.LeftToRight;
        public DetemineLocationType locationType = DetemineLocationType.OverrideLocation;
        public float overrideX = 0;
        public float overrideY = 0;
        public float extraContentLength = 0;


        [Header("Setting For Custom Data")]
        public float itemSize = 100; // size of an item 
        public int itemGenerate = 10; // number item generate, note: only need create +2 more item appear, if max item appear in screen is 5 =>itemGenerate =7 is enough
        public int totalNumberItem = 100;// total item of scrollview


        [Header("Setting if want to skip some index item")]
        public List<int> list_skip_Index = new List<int>(); // contain location of skip object
        public List<GameObject> list_skip_Object = new List<GameObject>(); // object want to skip 

        [Header("flat check auto setup references")]
        public bool isAutoLinking = true;
        public bool isOverrideSettingScrollbar = true;

        public List<GameObject> GetListItem { get { return listItem; } }
        private List<GameObject> listItem = new List<GameObject>();
        private List<RectTransform> listItemRect = new List<RectTransform>();
        private List<PXListViewItem> listItemComponent = new List<PXListViewItem>();
        private GameObject[] arrayCurrent = null;
        private int[] itemCurrentIndex = null;
        private int cacheOld = -1;
        private bool isInit = false;

        public void DestroyAllSlots()
        {
            if (listItem != null)
            {
                for (int i = 0; i < listItem.Count; i++)
                {
                    if (listItem[i] != null)
                    {
                        GameObject.Destroy(listItem[i]);
                    }
                }
                listItem.Clear();
                listItemRect.Clear();
                listItemComponent.Clear();
            }
            cacheOld = -1;
            isInit = false;
            itemCurrentIndex = null;
        }

        public void Setup(int numberItem)
        {
            totalNumberItem = numberItem;
            if (totalNumberItem < 0)
            {
                totalNumberItem = 0;
            }
            Setup();
        }


        public void Setup()
        {
            if (prefab == null)
            {
                Debug.LogWarning("No prefab/Gameobject Item linking");
                return;
            }
            if (type == InfinityType.Vertical)
            {
                int totalHeight = (int)((totalNumberItem + list_skip_Index.Count) * itemSize);
                content.SetHeight(totalHeight + extraContentLength);
            }
            else
            {
                int totalWidth = (int)((totalNumberItem + list_skip_Index.Count) * itemSize);
                content.SetWidth(totalWidth + extraContentLength);
            }

            //reset Array
            arrayCurrent = new GameObject[totalNumberItem];
            itemCurrentIndex = new int[totalNumberItem];
            for (int j = 0; j < totalNumberItem; j++) itemCurrentIndex[j] = -1;

            for (int i = 0; i < itemGenerate; i++)
            {
                GameObject obj = null;

                //if (i < totalNumberItem)
                {

                    if (i >= listItem.Count)
                    {
                        obj = GameObject.Instantiate(prefab) as GameObject;
#if UNITY_EDITOR
                        obj.name = string.Format("item_{0}", i);
#endif
                        obj.transform.SetParent(content.transform, false);
                        obj.transform.localScale = Vector3.one;
                        listItem.Add(obj);
                        listItemRect.Add(obj.GetComponent<RectTransform>());
                        listItemComponent.Add(obj.GetComponent<PXListViewItem>());
                    }
                    else
                    {
                        obj = listItem[i];
                    }

                    RectTransform cachedRect = (i < listItemRect.Count) ? listItemRect[i] : null;

                    if (type == InfinityType.Vertical)
                    {
                        RectTransform rect = cachedRect;
                        if (rect != null)
                        {
                            Vector2 anchor = rect.pivot;
                            if (verticalType == VerticalType.BottomToTop)
                            {
                                Vector2 min = rect.anchorMin;
                                Vector2 max = rect.anchorMax;
                                rect.anchorMin = new Vector2(min.x, 0);
                                rect.anchorMax = new Vector2(max.x, 0);
                                rect.pivot = new Vector2(anchor.x, 0);
                            }
                            else
                            {
                                rect.pivot = new Vector2(anchor.x, 1);
                            }
                        }
                    }
                    else if (type == InfinityType.Horizontal)
                    {
                        RectTransform rect = cachedRect;
                        if (rect != null)
                        {
                            Vector2 anchor = rect.pivot;
                            if (horizontalType == HorizontalType.RigthToLeft)
                            {
                                Vector2 min = rect.anchorMin;
                                Vector2 max = rect.anchorMax;
                                rect.anchorMin = new Vector2(1, min.y);
                                rect.anchorMax = new Vector2(1, max.y);
                                rect.pivot = new Vector2(1, anchor.y);
                            }
                            else
                            {
                                rect.pivot = new Vector2(0, anchor.y);
                            }
                        }
                    }

                    if (i < arrayCurrent.Length)
                    {
                        obj.gameObject.SetActive(true);

                        Reload(obj, i);
                        arrayCurrent[i] = obj;
                    }
                    else
                    {
                        obj.gameObject.SetActive(false);
                    }
                }

            }
            //isInit = true;
            int add = 0;
            for (int i = 0; i < list_skip_Index.Count; i++)
            {
                try
                {
                    if (i < list_skip_Object.Count && list_skip_Object[i] != null)
                    {
                        int index = list_skip_Index[i];
                        if (index > totalNumberItem)
                        {
                            index = totalNumberItem;
                        }
                        index += add;
                        add++;
                        Vector3 scale = list_skip_Object[i].transform.localScale;
                        if (list_skip_Object[i].activeInHierarchy == false)//invisible or prefab
                        {
                            GameObject obj = GameObject.Instantiate(list_skip_Object[i]) as GameObject;
                            obj.SetActive(true);
                            list_skip_Object[i] = obj;
                        }
                        list_skip_Object[i].transform.SetParent(content.transform);
                        list_skip_Object[i].transform.localScale = scale;
                        if (type == InfinityType.Vertical)
                        {
                            RectTransform rect = list_skip_Object[i].GetComponent<RectTransform>();
                            if (rect != null)
                            {
                                Vector2 anchor = rect.pivot;
                                if (verticalType == VerticalType.BottomToTop)
                                {
                                    Vector2 min = rect.anchorMin;
                                    Vector2 max = rect.anchorMax;
                                    rect.anchorMin = new Vector2(min.x, 0);
                                    rect.anchorMax = new Vector2(max.x, 0);
                                    rect.pivot = new Vector2(anchor.x, 0);
                                }
                                else
                                {
                                    rect.pivot = new Vector2(anchor.x, 1);
                                }
                            }
                        }
                        else if (type == InfinityType.Horizontal)
                        {
                            RectTransform rect = list_skip_Object[i].GetComponent<RectTransform>();
                            if (rect != null)
                            {
                                Vector2 anchor = rect.pivot;
                                if (horizontalType == HorizontalType.RigthToLeft)
                                {
                                    Vector2 min = rect.anchorMin;
                                    Vector2 max = rect.anchorMax;
                                    rect.anchorMin = new Vector2(1, min.y);
                                    rect.anchorMax = new Vector2(1, max.y);
                                    rect.pivot = new Vector2(1, anchor.y);
                                }
                                else
                                {
                                    rect.pivot = new Vector2(0, anchor.y);
                                }
                            }
                        }
                        Vector3 vec = Vector3.zero;
                        if (locationType == DetemineLocationType.BaseOnObjectCreate)
                        {
                            if (listItem.Count > 0 && listItem[0] != null)
                            {
                                vec = listItem[0].transform.localPosition;
                            }
                        }
                        else
                        {
                            vec = new Vector2(overrideX, overrideY);
                        }
                        list_skip_Object[i].transform.localPosition = GetLocationAppear(vec, index);

                    }
                }
                catch (System.Exception ex)
                {

                }
            }
        }
        private float GetContentSize()
        {
            return content.GetHeight();
        }

        public int GetLocaltionWithSkip(int index)
        {
            int location = index;
            for (int i = 0; i < list_skip_Index.Count; i++)
            {
                if (list_skip_Index[i] <= index)
                {
                    location++;
                }
            }
            return location;
        }

        public int GetIndexRejectSkip(int index)
        {
            int location = index;
            for (int i = 0; i < list_skip_Index.Count; i++)
            {
                if (list_skip_Index[i] <= index)
                {
                    location--;
                }
            }

            if (location < 0)
            {
                return 0;
            }

            return location;
        }


        protected override void Awake()
        {
            base.Awake();

            scrollRect.onValueChanged.AddListener(OnScrollChange);

            /*
            scrollRect = GetComponent<ScrollRect>();
            scrollRect.onValueChanged.AddListener(OnScrollChange);           
            if (setupOnAwake)
            {
                Setup();
            }
            */
        }

        private int GetCurrentIndex()
        {
            int index = -1;
            if (type == InfinityType.Vertical)
            {
                if (verticalType == VerticalType.TopToBottom)
                {
                    index = (int)(content.anchoredPosition.y / itemSize);
                }
                else
                {
                    index = (int)(-content.anchoredPosition.y / itemSize);
                }
            }
            else
            {
                if (horizontalType == HorizontalType.LeftToRight)
                {
                    index = (int)(-content.anchoredPosition.x / itemSize);

                }
                else
                {
                    index = (int)(content.anchoredPosition.x / itemSize);
                }
            }
            if (index < 0)
                index = 0;
            if (index > totalNumberItem - 1)
            {
                index = totalNumberItem - 1;
            }
            return index;
        }

        public void ListItemsUpdate()
        {
            for (int i = 0; i < listItemComponent.Count; i++)
            {
                if (listItemComponent[i] != null)
                {
                    listItemComponent[i].UpdateItem();
                }
            }
        }

        public void InternalReload()
        {

            int index = GetCurrentIndex();
            index = GetIndexRejectSkip(index);
            FixFastReload(index);
        }
        public void OnScrollChange(Vector2 vec)
        {
            if (arrayCurrent == null || arrayCurrent.Length < 1)
            {
                return;
            }

            int index = GetCurrentIndex();
            index = GetIndexRejectSkip(index);
            if (cacheOld != index)
            {
                cacheOld = index;
            }
            else
            {
                return;
            }
            if (!FixFastReload(index))
            {
                GameObject objIndex = arrayCurrent[index];
                if (objIndex == null)
                {// truot len
                    int next = index + itemGenerate;
                    if (next > totalNumberItem - 1)
                    {
                        return;
                    }
                    else
                    {
                        GameObject objNow = arrayCurrent[next];
                        if (objNow != null)
                        {//swap
                            arrayCurrent[next] = objIndex;
                            arrayCurrent[index] = objNow;
                            Reload(arrayCurrent[index], index);
                        }
                    }
                }
                else
                {// truot xuong
                    if (index > 0)
                    {
                        GameObject obj = arrayCurrent[index - 1];
                        if (obj == null)
                        {
                            return;
                        }
                        int next = index - 1 + itemGenerate;
                        if (next > totalNumberItem - 1)
                        {
                            return;
                        }
                        else
                        {
                            GameObject objNow = arrayCurrent[next];
                            if (objNow == null)
                            {//swap
                                arrayCurrent[next] = obj;
                                arrayCurrent[index - 1] = objNow;
                                GetCurrentLastIndex = next;
                                Reload(arrayCurrent[next], next);
                            }
                        }
                    }
                }
            }
        }

        public bool FixFastReload(int index)
        {
            bool isNeedFix = false;

            int add = index + 1;
            for (int i = add; i < add + itemGenerate - 2; i++)
            {
                if (i < totalNumberItem)
                {
                    GameObject obj = arrayCurrent[i];
                    if (obj == null)
                    {
                        isNeedFix = true;
                        break;
                    }
                    else if (itemCurrentIndex[i] != i)
                    {
                        isNeedFix = true;
                        break;
                    }
                }
            }

            if (isNeedFix)
            {
                for (int i = 0; i < totalNumberItem; i++)
                {
                    arrayCurrent[i] = null;
                }

                int start = index;
                if (start + itemGenerate > totalNumberItem)
                {
                    start = totalNumberItem - itemGenerate;
                }
                //Debug.LogError ("Fix Fast reload:"+start+","+index);
                for (int i = 0; i < itemGenerate; i++)
                {
                    arrayCurrent[start + i] = listItem[i];
                    Reload(arrayCurrent[start + i], start + i);
                }
                return true;
            }
            return false;
        }

        protected virtual void Reload(GameObject obj, int indexReload)
        {
#if UNITY_EDITOR
            obj.transform.name = string.Format("item_{0}", indexReload);
#endif
            if (indexReload >= 0 && indexReload < itemCurrentIndex.Length)
                itemCurrentIndex[indexReload] = indexReload;

            int location = GetLocaltionWithSkip(indexReload);
            Vector3 vec = Vector3.zero;
            if (locationType == DetemineLocationType.BaseOnObjectCreate)
            {
                vec.x = obj.transform.localPosition.x;
                vec.y = obj.transform.localPosition.y;
            }
            else
            {
                vec = new Vector2(overrideX, overrideY);
            }
            vec = GetLocationAppear(vec, location);
            obj.transform.localPosition = vec;

            int listIndex = listItem.IndexOf(obj);
            PXListViewItem baseItem = (listIndex >= 0 && listIndex < listItemComponent.Count) ? listItemComponent[listIndex] : obj.GetComponent<PXListViewItem>();
            if (baseItem != null)
            {
                baseItem.Reload(this, indexReload);

                OnListViewItemUpdate.Invoke(indexReload, baseItem);
            }
            else
            {
                Debug.LogError($"PXListViewCore, Invalid PXListViewItem, obj name = {obj.name}");
            }
        }

        private Vector3 GetLocationAppear(Vector2 initVec, int location)
        {
            Vector3 vec = initVec;
            if (type == InfinityType.Vertical)
            {
                if (verticalType == VerticalType.TopToBottom)
                {
                    vec = new Vector3(vec.x, -itemSize * location/* - itemSize / 2*/, 0);
                }
                else
                {
                    vec = new Vector3(vec.x, itemSize * location /*+ itemSize / 2*/, 0);
                }
            }
            else
            {
                if (horizontalType == HorizontalType.LeftToRight)
                {
                    vec = new Vector3(itemSize * location/*+itemSize/2*/, vec.y, 0);
                }
                else
                {
                    vec = new Vector3(-itemSize * location/*-itemSize/2*/, vec.y, 0);
                }
            }
            return vec;
        }

        /// <summary>
        /// 🚀 증분 업데이트: 기존 아이템은 유지하고 새 아이템만 추가
        /// 깜빡임 없이 리스트 확장 가능
        /// </summary>
        /// <param name="newTotalCount">새로운 전체 아이템 수</param>
        public void SetupIncremental(int newTotalCount)
        {
            if (newTotalCount < 0) newTotalCount = 0;

            int oldTotalCount = totalNumberItem;

            // 항목 수가 줄어든 경우는 전체 Setup 필요
            if (newTotalCount < oldTotalCount)
            {
                Setup(newTotalCount);
                return;
            }

            // 항목 수가 같으면 아무것도 하지 않음
            if (newTotalCount == oldTotalCount)
            {
                return;
            }

            // 🎯 핵심: 증분 업데이트 (기존 아이템 유지)
            totalNumberItem = newTotalCount;

            // Content 크기만 업데이트
            UpdateContentSize();

            // 기존 arrayCurrent 확장 (기존 데이터 보존)
            ExtendArrayCurrent(oldTotalCount, newTotalCount);

            // 스크롤 위치에 따라 새로운 아이템만 로딩
            RefreshVisibleItemsIncremental(oldTotalCount);

            Debug.Log($"[PXListViewCore] SetupIncremental: {oldTotalCount} → {newTotalCount} (기존 아이템 유지)");
        }

        /// <summary>
        /// Content 크기만 업데이트 (UI 재구성 없음)
        /// </summary>
        private void UpdateContentSize()
        {
            if (type == InfinityType.Vertical)
            {
                int totalHeight = (int)((totalNumberItem + list_skip_Index.Count) * itemSize);
                content.SetHeight(totalHeight + extraContentLength);
            }
            else
            {
                int totalWidth = (int)((totalNumberItem + list_skip_Index.Count) * itemSize);
                content.SetWidth(totalWidth + extraContentLength);
            }
        }

        /// <summary>
        /// 기존 arrayCurrent 배열을 확장 (기존 데이터 보존)
        /// </summary>
        private void ExtendArrayCurrent(int oldCount, int newCount)
        {
            if (arrayCurrent == null)
            {
                arrayCurrent = new GameObject[newCount];
                return;
            }

            // 🎯 핵심: 기존 배열 데이터 보존하면서 확장
            GameObject[] newArray = new GameObject[newCount];

            // 기존 데이터 복사 (깜빡임 방지)
            for (int i = 0; i < oldCount && i < arrayCurrent.Length; i++)
            {
                newArray[i] = arrayCurrent[i];
            }

            arrayCurrent = newArray;
        }

        /// <summary>
        /// 현재 보이는 영역의 새로운 아이템만 로딩
        /// </summary>
        private void RefreshVisibleItemsIncremental(int oldCount)
        {
            int currentIndex = GetCurrentIndex();
            int startIndex = currentIndex;
            int endIndex = Mathf.Min(currentIndex + itemGenerate, totalNumberItem);

            // 🔥 수정된 로직: 모든 보이는 아이템들의 위치 업데이트
            for (int i = startIndex; i < endIndex; i++)
            {
                if (i >= oldCount)
                {
                    // 새로 추가된 아이템 처리
                    GameObject availableObj = FindAvailableListItem();
                    if (availableObj != null)
                    {
                        arrayCurrent[i] = availableObj;
                        availableObj.SetActive(true);
                        Reload(availableObj, i);
                    }
                }
                else if (arrayCurrent[i] != null)
                {
                    // 🎯 핵심: 기존 아이템들의 위치만 업데이트 (데이터는 건드리지 않음)
                    UpdateItemPositionOnly(arrayCurrent[i], i);
                }
            }
        }

        /// <summary>
        /// 🎯 아이템의 위치만 업데이트 (데이터 업데이트 없이, 깜빡임 방지)
        /// </summary>
        /// <param name="obj">위치를 업데이트할 GameObject</param>
        /// <param name="indexReload">새로운 인덱스</param>
        private void UpdateItemPositionOnly(GameObject obj, int indexReload)
        {
#if UNITY_EDITOR
            obj.transform.name = string.Format("item_{0}", indexReload);
#endif

            // 위치 계산 및 업데이트 (Reload에서 가져온 로직)
            int location = GetLocaltionWithSkip(indexReload);
            Vector3 vec = Vector3.zero;
            if (locationType == DetemineLocationType.BaseOnObjectCreate)
            {
                vec.x = obj.transform.localPosition.x;
                vec.y = obj.transform.localPosition.y;
            }
            else
            {
                vec = new Vector2(overrideX, overrideY);
            }
            vec = GetLocationAppear(vec, location);
            obj.transform.localPosition = vec;

            // 🎯 핵심: PXListViewItem.Reload()나 OnListViewItemUpdate는 호출하지 않음!
            // 위치만 업데이트하여 깜빡임 방지
        }

        /// <summary>
        /// 재사용 가능한 GameObject 찾기
        /// </summary>
        private GameObject FindAvailableListItem()
        {
            // 현재 arrayCurrent에서 사용되지 않는 listItem 찾기
            for (int i = 0; i < listItem.Count; i++)
            {
                GameObject obj = listItem[i];
                if (obj != null && !IsObjectInCurrentArray(obj))
                {
                    return obj;
                }
            }

            // 사용 가능한 것이 없으면 새로 생성
            if (listItem.Count < itemGenerate)
            {
                GameObject newObj = GameObject.Instantiate(prefab) as GameObject;
#if UNITY_EDITOR
                newObj.name = string.Format("item_{0}", listItem.Count);
#endif
                newObj.transform.SetParent(content.transform, false);
                newObj.transform.localScale = Vector3.one;
                listItem.Add(newObj);
                listItemRect.Add(newObj.GetComponent<RectTransform>());
                listItemComponent.Add(newObj.GetComponent<PXListViewItem>());
                return newObj;
            }

            return null;
        }

        /// <summary>
        /// GameObject가 현재 arrayCurrent에서 사용 중인지 확인
        /// </summary>
        private bool IsObjectInCurrentArray(GameObject obj)
        {
            if (arrayCurrent == null) return false;

            for (int i = 0; i < arrayCurrent.Length; i++)
            {
                if (arrayCurrent[i] == obj)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 🎯 증분 업데이트가 가능한지 확인
        /// </summary>
        /// <param name="newCount">새로운 아이템 수</param>
        /// <returns>증분 업데이트 가능 여부</returns>
        public bool CanUseIncrementalUpdate(int newCount)
        {
            // 초기화되지 않았으면 불가능
            if (arrayCurrent == null || prefab == null) return false;

            // 아이템이 줄어들면 불가능 (전체 재구성 필요)
            if (newCount < totalNumberItem) return false;

            // 같으면 업데이트 불필요
            if (newCount == totalNumberItem) return false;

            return true;
        }
    }

}