using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace PX
{
    public enum EPXCore_Listview_Type
    {
        Horizon,
        Vertical,
    }
    public class PXScrollView : PXMonoBehaviour, IPXUITemplate
    {
        public EPXCore_Listview_Type listViewType;

        #region Events
        [Serializable]
        public class PXScrollViewEvent : UnityEvent<Vector2, string> { }
        [FormerlySerializedAs("OnPXScrollViewEvent")]
        [SerializeField]
        private PXScrollViewEvent m_OnScrollView = new PXScrollViewEvent();

        public PXScrollViewEvent onScrollView
        {
            get { return m_OnScrollView; }
            set { m_OnScrollView = value; }
        }
        #endregion
        public string SourceParam { get; set; }

        #region Bind Widgets
        [SerializeField]
        private Image _image_BG;
        [SerializeField]
        private Image _image_UIMask;
        [SerializeField]
        private Mask _mask;
        [SerializeField]
        ScrollRect _scrollRect;

        public HorizontalLayoutGroup GetHorizontalLayoutGroup { get; private set; }
        public VerticalLayoutGroup GetVerticalLayoutGroup { get; private set; }
        #endregion

        RectTransform _panel_Content;
        RectTransform _rectTransform;
        public ScrollRect GetScrollRect { get { return _scrollRect; } }
        public RectTransform GetPanelContent
        {
            get
            {
                if (_panel_Content == null)
                    _panel_Content = _scrollRect.content;

                return _panel_Content;
            }
        }
        public RectTransform GetRectTransform { get { return _rectTransform; } }

        protected override void Awake()
        {
            base.Awake();

            _rectTransform = GetComponent<RectTransform>();
            //_panel_Content = _scrollRect.content;

            GetHorizontalLayoutGroup = GetPanelContent?.GetComponent<HorizontalLayoutGroup>();
            GetVerticalLayoutGroup = GetPanelContent?.GetComponent<VerticalLayoutGroup>();

            _scrollRect.onValueChanged.AddListener((Vector2 inScrollValue) =>
          {
              onScrollView.Invoke(inScrollValue, SourceParam);
          });
        }

        void OnDestroy()
        {
            _scrollRect.onValueChanged.RemoveAllListeners();
            onScrollView.RemoveAllListeners();
        }
    }
}