using DG.Tweening;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace PX
{
    public class PXListView : PXMonoBehaviour, IPXUITemplate
    {
        // [Header("ListView LineItem ����")]
        // public int lineItemCount;

        [Header("ListView Setting")]
        #region Bind SerializeValue
        /*[OnPropertyChangedCall("OnListViewInitUpdate")]*/
        public GameObject Prefab;
        /*[OnPropertyChangedCall("OnListViewInitUpdate")]*/
        public float itemSize = 100; // size of an item 
        /*[OnPropertyChangedCall("OnListViewInitUpdate")]*/
        public int itemGenerate = 10; // number item generate, note: only need create +2 more item appear, if max item appear in screen is 5 =>itemGenerate =7 is enough
        [Space(10.0f)]
        /*[OnPropertyChangedCall("OnListViewInitUpdate")]*/
        //public float overrideX = 0;
        /*[OnPropertyChangedCall("OnListViewInitUpdate")]*/
        //public float overrideY = 0;
        #endregion
        [Space(20.0f)]

        #region Bind Widgets
        [SerializeField]
        private Image _image_BG;
        [SerializeField]
        private Image _image_UIMask;
        [SerializeField]
        private Mask _mask;
        [SerializeField]
        ScrollRect _scrollRect;
        [SerializeField]
        RectTransform _panel_Content;
        [SerializeField]
        PXListViewCore _listViewCore;
        #endregion

        #region Events
        [Serializable]
        public class PXListViewEvent : UnityEvent<int, PXListViewItem, string> { }
        [FormerlySerializedAs("OnPXListViewEvent")]
        [SerializeField]
        private PXListViewEvent m_OnListView = new PXListViewEvent();

        public PXListViewEvent onListView
        {
            get { return m_OnListView; }
            set { m_OnListView = value; }
        }

        public string SourceParam { get; set; }
        public int GetDataCount { get { return _listViewCore.totalNumberItem; } }
        #endregion
        public GameObject GetPrefab { get { return _listViewCore.prefab; } } // link object item
        public ScrollRect GetScrollRect { get { return _scrollRect; } }// link to UGUI scrollRect
        public RectTransform GetContent { get { return _panel_Content; } }// link to content that contain all item in scrollrect

        public int GetCurrentLastIndex { get { return _listViewCore.GetCurrentLastIndex; } }
        public List<GameObject> GetListItem { get { return _listViewCore.GetListItem; } }
        public List<int> GetListSkipIndex { get { return _listViewCore.list_skip_Index; } } // contain location of skip object
        public List<GameObject> GetListSkipObject { get { return _listViewCore.list_skip_Object; } } // object want to skip 

        private Sequence naviagateSequence;

        private void OnListViewInitUpdate()
        {
            if (_listViewCore != null)
            {
                _listViewCore.prefab = Prefab; // link object item
                _listViewCore.scrollRect = _scrollRect;// link to UGUI scrollRect
                _listViewCore.content = _panel_Content;// link to content that contain all item in scrollrect

                // _listViewCore.overrideX = overrideX;
                // _listViewCore.overrideY = overrideY;

                _listViewCore.itemSize = itemSize;
                _listViewCore.itemGenerate = itemGenerate;
            }
        }
        public void SetupLine(int InLineCount, int InLinePerCount)
        {
            _listViewCore.itemSize = itemSize;

            _listViewCore.Setup(InLineCount);

            // _listViewCore.GetListItem.ForEach((item) =>
            // {
            //     PXListViewItem_BaseLine baseLine = item.GetComponent<PXListViewItem_BaseLine>();
            //     if (baseLine != null)
            //     {
            //         baseLine.GenerateLineItems(InLinePerCount);
            //     }
            // });
        }
        public void Setup(int numberItem)
        {
            _listViewCore.itemSize = itemSize;

            _listViewCore.Setup(numberItem);

            // _listViewCore.GetListItem.ForEach((item) =>
            // {
            //     PXListViewItem_BaseLine baseLine = item.GetComponent<PXListViewItem_BaseLine>();
            //     if (baseLine != null)
            //     {
            //         //baseLine.GenerateLineItems();
            //     }
            // });
        }

        public void SetOriginPrefab(GameObject InPrefab)
        {
            if (_listViewCore.prefab != InPrefab)
            {
                _listViewCore.DestroyAllSlots();
                _listViewCore.prefab = InPrefab;
            }
        }

        public void Setup()
        {
            _listViewCore.Setup();
        }
        public void InternalReload()
        {
            _listViewCore.InternalReload();
        }
        public bool FixFastReload(int index)
        {
            return _listViewCore.FixFastReload(index);
        }

        public void ListItemsUpdate()
        {
            _listViewCore.ListItemsUpdate();
        }

        /// <summary>
        /// 🚀 증분 업데이트: 기존 아이템은 유지하고 새 아이템만 추가
        /// 깜빡임 없이 리스트 확장 가능
        /// </summary>
        /// <param name="newTotalCount">새로운 전체 아이템 수</param>
        public void SetupIncremental(int newTotalCount)
        {
            _listViewCore.SetupIncremental(newTotalCount);
        }

        /// <summary>
        /// 🎯 증분 업데이트가 가능한지 확인
        /// </summary>
        /// <param name="newCount">새로운 아이템 수</param>
        /// <returns>증분 업데이트 가능 여부</returns>
        public bool CanUseIncrementalUpdate(int newCount)
        {
            return _listViewCore.CanUseIncrementalUpdate(newCount);
        }

        public void NavigateToIndex(int inIndex, bool inWithAnimation = true)
        {
            inIndex = Mathf.Max(0, inIndex);

            if (_listViewCore.totalNumberItem > 0)
            {
                int toIndex = _listViewCore.GetLocaltionWithSkip(inIndex);

                Debug.Log($"NavigateToIndex, inIndex = {toIndex}, totalNumberItem = {_listViewCore.totalNumberItem}");

                GetScrollRect.StopMovement();
                GetScrollRect.velocity = Vector2.zero;
                if (naviagateSequence != null)
                {
                    naviagateSequence.Kill();
                }

                if (_listViewCore.type == InfinityType.Vertical)
                {
                    Vector2 toVector = GetContent.anchoredPosition;
                    toVector.y = toIndex * _listViewCore.itemSize;

                    if (inWithAnimation)
                    {
                        naviagateSequence = DOTween.Sequence();
                        naviagateSequence.Append(GetContent.DOAnchorPos(toVector, 0.2f).SetEase(Ease.InOutQuart));
                        naviagateSequence.Play();
                    }
                    else
                    {
                        GetContent.anchoredPosition = toVector;
                    }
                }
                else
                {
                    Vector2 toVector = GetContent.anchoredPosition;
                    toVector.x = -toIndex * _listViewCore.itemSize;

                    if (inWithAnimation)
                    {
                        naviagateSequence = DOTween.Sequence();
                        naviagateSequence.Append(GetContent.DOAnchorPos(toVector, 0.2f).SetEase(Ease.InOutQuart));
                        naviagateSequence.Play();
                    }
                    else
                    {
                        GetContent.anchoredPosition = toVector;
                    }
                }
            }
        }

        protected override void Awake()
        {
            base.Awake();

            OnListViewInitUpdate();

            _listViewCore.OnListViewItemUpdate.AddListener((int inIndex, PXListViewItem inItem) =>
            {
                onListView.Invoke(inIndex, inItem, SourceParam);
            });
        }
    }
}