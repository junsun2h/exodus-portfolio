using DG.Tweening;
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace PX
{
    public class PXCheckBox : PXMonoBehaviour, IPointerClickHandler, IPXUITemplate
    {
        [SerializeField] private EContent _ContentsType;

        #region Bind Widgets
        [SerializeField] private Image _image_Check;
        [SerializeField] private PXReddot _pxReddot;
        [SerializeField] private PXText _pxText;
        [SerializeField] private PXWidget_ContentsLock _PXWidget_ContentsLock;

        [Header("[탭 스타일]")]
        [SerializeField] private Image _image_TabBG;
        [SerializeField] private Color _colorSelected = new Color(0.05f, 0.44f, 0.95f, 1f);
        [SerializeField] private Color _colorNormal = new Color(0.15f, 0.15f, 0.15f, 1f);
        [SerializeField] private Color _textColorSelected = Color.white;
        [SerializeField] private Color _textColorNormal = new Color(0.6f, 0.6f, 0.6f, 1f);
        #endregion

        #region Events
        [Serializable]
        public class PXCheckBoxEvent : UnityEvent<bool, string> { }
        [FormerlySerializedAs("OnPXCheckBoxEvent")]
        [SerializeField]
        private PXCheckBoxEvent m_OnCheck = new PXCheckBoxEvent();

        public PXCheckBoxEvent onCheckEvent
        {
            get { return m_OnCheck; }
            set { m_OnCheck = value; }
        }
        #endregion
        public string SourceCheckParam { get; set; }
        public Image Image_Check { get { return _image_Check; } }
        public PXReddot PXReddot { get { return _pxReddot; } }
        public PXText PXText { get { return _pxText; } }
        public bool IsAbleCheck { get { return isActiveAndEnabled; } }
        public bool IsCheckOn { get; private set; }


        Vector3 checkOriginScale = Vector3.one;

        protected override void Awake()
        {
            base.Awake();

            RectTransform imageRect = Image_Check.GetComponent<RectTransform>();
            checkOriginScale = imageRect.localScale;
            imageRect.localScale = Vector3.zero;

            //_pxReddot.gameObject.SetActive(false);
            _pxText.gameObject.SetActive(false);
        }

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (!IsAbleCheck)
                return;

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            GameSoundManager.Instance.PlaySFX_UI_Global("sfx_button_click_1");

            SetToggle();
        }

        public void SetCheck(bool InOn, bool InWithEvent = true)
        {
            if (IsCheckOn == InOn)
                return;

            IsCheckOn = InOn;
            Vector3 toScale = InOn ? checkOriginScale : Vector3.zero;

            _image_Check.rectTransform.DOKill();

            if (InOn)
            {
                _image_Check.rectTransform.localScale = toScale * 0.9f;
                _image_Check.rectTransform.DOScale(toScale, 0.15f).SetEase(Ease.OutBack);
            }
            else
            {
                _image_Check.rectTransform.localScale = toScale;
            }

            UpdateTabStyle(InOn);

            if (InWithEvent)
            {
                onCheckEvent.Invoke(InOn, SourceCheckParam);
            }
        }

        public void SetToggle()
        {
            if (!IsAbleCheck)
                return;

            SetCheck(!IsCheckOn);
        }

        public void SetText(string InText)
        {
            PXText.SetText(InText);
        }

        public void SetReddot(bool InOn)
        {
            PXReddot.SetVisible(InOn);
        }

        void UpdateTabStyle(bool InOn)
        {
            if (_image_TabBG == null)
                return;

            Color targetBG = InOn ? _colorSelected : _colorNormal;
            Color targetText = InOn ? _textColorSelected : _textColorNormal;

            _image_TabBG.DOKill();
            _image_TabBG.DOColor(targetBG, 0.15f).SetEase(Ease.OutQuad);

            if (_pxText != null && _pxText.Tmp_text != null)
            {
                _pxText.Tmp_text.color = targetText;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            _PXWidget_ContentsLock?.SetContentsType(_ContentsType);
        }
    }
}