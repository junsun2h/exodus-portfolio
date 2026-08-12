using DG.Tweening;
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PX
{
    public class PXTabButton : PXMonoBehaviour, IPointerClickHandler, IPXUITemplate
    {
        [SerializeField] private EContent _ContentsType;

        #region Bind Widgets
        [Header("[탭 요소]")]
        [SerializeField] private Image _buttonImage;
        [SerializeField] private Image _bottomBorder;
        [SerializeField] private PXText _pxText;
        [SerializeField] private PXReddot _pxReddot;
        [SerializeField] private PXWidget_ContentsLock _PXWidget_ContentsLock;

        [Header("[탭 컬러 — 선택]")]
        [SerializeField] private Color _bgColorSelected = new Color(0.314f, 0.784f, 1f, 0.08f);
        [SerializeField] private Color _borderColorSelected = new Color(0.314f, 0.784f, 1f, 1f);
        [SerializeField] private Color _textColorSelected = new Color(0.314f, 0.784f, 1f, 1f);

        [Header("[탭 컬러 — 비선택]")]
        [SerializeField] private Color _bgColorNormal = new Color(0f, 0f, 0f, 0f);
        [SerializeField] private Color _borderColorNormal = new Color(0f, 0f, 0f, 0f);
        [SerializeField] private Color _textColorNormal = new Color(0.549f, 0.627f, 0.725f, 1f);
        #endregion

        #region Events
        [Serializable]
        public class PXTabButtonEvent : UnityEvent<bool, string> { }
        [SerializeField]
        private PXTabButtonEvent m_OnTabClick = new PXTabButtonEvent();

        public PXTabButtonEvent onTabClickEvent
        {
            get { return m_OnTabClick; }
            set { m_OnTabClick = value; }
        }
        #endregion

        public string SourceTabParam { get; set; }
        public PXText PXText { get { return _pxText; } }
        public PXReddot PXReddot { get { return _pxReddot; } }
        public bool IsSelected { get; private set; }

        protected override void Awake()
        {
            base.Awake();

            ApplyStyle(false, true);
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            _PXWidget_ContentsLock?.SetContentsType(_ContentsType);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!isActiveAndEnabled)
                return;

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            GameSoundManager.Instance.PlaySFX_UI_Global("sfx_button_click_1");

            SetSelected(true);
        }

        public void SetSelected(bool inOn, bool inWithEvent = true)
        {
            if (IsSelected == inOn)
                return;

            IsSelected = inOn;
            ApplyStyle(inOn, false);

            if (inWithEvent)
            {
                onTabClickEvent.Invoke(inOn, SourceTabParam);
            }
        }

        public void SetText(string inText)
        {
            if (_pxText != null)
                _pxText.SetText(inText);
        }

        public void SetReddot(bool inOn)
        {
            if (_pxReddot != null)
                _pxReddot.SetVisible(inOn);
        }

        void ApplyStyle(bool inOn, bool inImmediate)
        {
            Color targetBG = inOn ? _bgColorSelected : _bgColorNormal;
            Color targetBorder = inOn ? _borderColorSelected : _borderColorNormal;
            Color targetText = inOn ? _textColorSelected : _textColorNormal;

            if (inImmediate)
            {
                if (_buttonImage != null) _buttonImage.color = targetBG;
                if (_bottomBorder != null) _bottomBorder.color = targetBorder;
                if (_pxText != null && _pxText.Tmp_text != null) _pxText.Tmp_text.color = targetText;
            }
            else
            {
                if (_buttonImage != null)
                {
                    _buttonImage.DOKill();
                    _buttonImage.DOColor(targetBG, 0.15f).SetEase(Ease.OutQuad);
                }

                if (_bottomBorder != null)
                {
                    _bottomBorder.DOKill();
                    _bottomBorder.DOColor(targetBorder, 0.15f).SetEase(Ease.OutQuad);
                }

                if (_pxText != null && _pxText.Tmp_text != null)
                {
                    _pxText.Tmp_text.DOKill();
                    _pxText.Tmp_text.DOColor(targetText, 0.15f).SetEase(Ease.OutQuad);
                }
            }
        }
    }
}
