using DG.Tweening;
using System;
using System.Collections;
using System.Numerics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
//
namespace PX
{
    public enum EButtonStyle
    {
        None,       // 스타일 미적용 (기존 ColorTint 방식)
        Primary,    // 주 결정 / 확인 / 강화 / 승급 / 보상 수령
        Cancel,     // 취소 / 닫기 / 뒤로가기
        Positive,   // 수락 / 적용 / 장착 / 완료 / 구매 / 교환
        Danger,     // 삭제 / 초기화 / 분해 / 포기 / 전투 / 출격
        Summon,     // 소환 / 가챠 / 뽑기
        Nav,        // 기능 탐색 / 진입 (HUD 일반 버튼)
    }

    public class PXButton : Selectable, IPointerClickHandler, ISubmitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPXUITemplate
    {
        #region Bind Widgets
        [SerializeField] private EContent _ContentsType;
        [SerializeField] private PXWidget_ContentsLock _PXWidget_ContentsLock;
        [SerializeField] private PXReddot _pxReddot;
        [SerializeField] private PXImage _pxImage_BG;
        [SerializeField] private PXText _pxText;

        RectTransform _cachedRectTransform;
        RectTransform CachedRectTransform => _cachedRectTransform != null ? _cachedRectTransform : (_cachedRectTransform = GetComponent<RectTransform>());

        [Header("[버튼 스타일]")]
        [SerializeField] private EButtonStyle _buttonStyle = EButtonStyle.None;
        [SerializeField] private Sprite _spriteNormal;
        [SerializeField] private Sprite _spritePress;

        #endregion

        #region Events

        public PXWidget_ContentsLock PXWidget_ContentsLock => _PXWidget_ContentsLock;

        public class PXButtonClickedSimpleEvent : UnityEvent { }
        private PXButtonClickedSimpleEvent m_OnClickSimple = new PXButtonClickedSimpleEvent();

        [Serializable]
        public class PXButtonClickedEvent : UnityEvent<string> { }
        [FormerlySerializedAs("OnPXButtonClickedEvent")]
        [SerializeField]
        private PXButtonClickedEvent m_OnClick = new PXButtonClickedEvent();

        [Serializable]
        public class PXButtonPressEvent : UnityEvent<string> { }
        [FormerlySerializedAs("OnPXButtonPressEvent")]
        [SerializeField]
        private PXButtonPressEvent m_OnPress = new PXButtonPressEvent();


        [Serializable]
        public class PXButtonPressFinishEvent : UnityEvent<string> { }
        [FormerlySerializedAs("OnPXButtonPressFinishEvent")]
        [SerializeField]
        private PXButtonPressFinishEvent m_OnPressFinish = new PXButtonPressFinishEvent();


        [Serializable]
        public class PXButtonPressExitEvent : UnityEvent<string> { }
        [FormerlySerializedAs("OnPXButtonPressExitEvent")]
        [SerializeField]
        private PXButtonPressExitEvent m_OnPressExit = new PXButtonPressExitEvent();

        public PXButtonClickedSimpleEvent onClickSimpleEvent
        {
            get { return m_OnClickSimple; }
            set { m_OnClickSimple = value; }
        }
        public PXButtonClickedEvent onClickEvent
        {
            get { return m_OnClick; }
            set { m_OnClick = value; }
        }
        public PXButtonPressEvent onPressEvent
        {
            get { return m_OnPress; }
            set { m_OnPress = value; }
        }
        public PXButtonPressFinishEvent onPressFinishEvent
        {
            get { return m_OnPressFinish; }
            set { m_OnPressFinish = value; }
        }

        PXButtonPressExitEvent onPressExitEvent
        {
            get { return m_OnPressExit; }
            set { m_OnPressExit = value; }
        }


        #endregion

        #region Widgets Getter
        public PXReddot Reddot { get { return _pxReddot; } }
        public PXImage Image_BG { get { return _pxImage_BG; } }
        public PXText Tmp_text { get { return _pxText; } }
        public EButtonStyle ButtonStyle => _buttonStyle;
        #endregion

        [Header("[Press 동작 세팅]")]
        // Deprecated: GameClientPlayConfig.Instance.ui.pressStartDelay 사용
        // [Tooltip("Press 시작 필요 시간")]
        // public float pressStartDelay = 0.5f;
        // [Tooltip("Press 반복 시간")]
        // public float pressRepeatDelay = 0.1f;
        [Tooltip("Click 효과 사용 여부")]
        public bool isClickEffect = true;
        private float _pressDeltaTime = PXConst.INDEX_NONE;
        private float _lastPressTime = 0f;  // 마지막 레벨업 실행 시간
        private bool isStartPressTrigger = false;


        public bool IsAbleButtonEvent { get { return IsActive() && interactable; /*&& IsInteractable();*/ } }
        //public bool IsButtonPressed { get { return _pressDeltaTime >= 0; } }
        public bool SetButtonEnable
        {
            set
            {
                // if (interactable == value)
                //     return;


                interactable = value;
                Tmp_text.Tmp_text.color = interactable ? colors.normalColor : colors.disabledColor;

                if (interactable == false)
                {
                    Reddot.SetVisible(false);

                    _pressDeltaTime = PXConst.INDEX_NONE;

                    if (PressCount > 0)
                    {
                        PressFinish();
                    }
                }
            }
        }
        public bool IsButtonEnable { get { return interactable; } }

        public string SourceClickParam { get; set; }
        public string SourcePressParam { get; set; }

        public int PressCount { get; private set; }
        public bool IxExistPressCount { get { return PressCount > 0; } }

        protected override void Awake()
        {
            base.Awake();
        }

        protected override void Start()
        {
            base.Start();
            ApplyButtonStyle();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            _PXWidget_ContentsLock?.SetContentsType(_ContentsType);
        }

        /// <summary>
        /// 버튼 스타일이 None이 아닌 경우 스프라이트 스왑 방식으로 전환
        /// </summary>
        private void ApplyButtonStyle()
        {
            if (_buttonStyle == EButtonStyle.None)
                return;

            if (_spriteNormal == null || _pxImage_BG == null)
                return;

            _pxImage_BG.SetSprite(_spriteNormal);
            _pxImage_BG.SetColor(Color.white);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            if (_buttonStyle == EButtonStyle.None || _spriteNormal == null || _pxImage_BG == null)
            {
                base.DoStateTransition(state, instant);
                return;
            }

            switch (state)
            {
                case SelectionState.Pressed:
                case SelectionState.Selected:
                    _pxImage_BG.SetSprite(_spritePress != null ? _spritePress : _spriteNormal);
                    break;
                default:
                    _pxImage_BG.SetSprite(_spriteNormal);
                    break;
            }
        }

        /// <summary>
        /// 런타임에서 버튼 스타일 변경
        /// </summary>
        public void SetButtonStyle(EButtonStyle style, Sprite normal, Sprite press)
        {
            _buttonStyle = style;
            _spriteNormal = normal;
            _spritePress = press;
            ApplyButtonStyle();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
        }

        #region Click
        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (!IsAbleButtonEvent)
                return;

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            SetClick();
        }
        public void SetClick()
        {
            if (!IsAbleButtonEvent)
                return;

            UISystemProfilerApi.AddMarker("PXButton.onClick", this);
            GameSoundManager.Instance.PlaySFX_UI_Global("sfx_button_click_1");

            if (isClickEffect)
            {
                Sequence mySequence = DOTween.Sequence();
                mySequence.Append(CachedRectTransform.DOScale(UnityEngine.Vector3.one * 1.05f, 0.05f).SetEase(Ease.InOutQuart));
                mySequence.Append(CachedRectTransform.DOScale(UnityEngine.Vector3.one * 1.0f, 0.1f).SetEase(Ease.InOutQuart));
                mySequence.Play();
            }

            m_OnClick.Invoke(SourceClickParam);

            m_OnClickSimple.Invoke();
        }

        #endregion

        #region Submit
        public virtual void OnSubmit(BaseEventData eventData)
        {
            if (!IsAbleButtonEvent)
                return;

            SetClick();

            DoStateTransition(SelectionState.Pressed, false);
            StartCoroutine(OnFinishSubmit());
        }

        private IEnumerator OnFinishSubmit()
        {
            var fadeTime = colors.fadeDuration;
            var elapsedTime = 0f;

            while (elapsedTime < fadeTime)
            {
                elapsedTime += Time.unscaledDeltaTime;
                yield return null;
            }

            DoStateTransition(currentSelectionState, false);
        }

        #endregion

        #region PointerEvent

        public override void OnPointerDown(PointerEventData eventData)
        {
            if (!IsAbleButtonEvent)
                return;

            PressStart();
            PressTrigger();
        }
        public override void OnPointerEnter(PointerEventData eventData)
        {
            if (!IsAbleButtonEvent)
                return;

            //..
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            if (!IsAbleButtonEvent)
                return;

            PressFinish();
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            if (!IsAbleButtonEvent)
                return;

            PressExit();
        }

        void PressStart()
        {
            _pressDeltaTime = PXConst.INDEX_ZERO;
            _lastPressTime = 0f;
            DoStateTransition(SelectionState.Pressed, false);
        }

        void PressTrigger()
        {
            _pressDeltaTime = PXConst.INDEX_ZERO;
            onPressEvent.Invoke(SourcePressParam);
        }

        void PressExit()
        {
            PressFinish();
        }

        void PressFinish()
        {
            DoStateTransition(currentSelectionState, false);
            _pressDeltaTime = PXConst.INDEX_NONE;
            isStartPressTrigger = false;
            onPressFinishEvent.Invoke(SourcePressParam);

            //PressCount Clear
            PressCount = 0;
        }
        void PressUpdate()
        {
            if (IsAbleButtonEvent == false)
                return;

            if (_pressDeltaTime == PXConst.INDEX_NONE)
                return;

            _pressDeltaTime += Time.deltaTime;

            var uiConfig = GameClientPlayConfig.Instance.ui;
            float startDelay = uiConfig.pressStartDelay;
            float stage2RepeatDelay = uiConfig.pressRepeatDelay;
            float stage3StartDelay = uiConfig.pressStage3StartDelay;
            float stage3RepeatDelay = uiConfig.pressStage3RepeatDelay;

            // 1단계: 첫 레벨업
            if (_pressDeltaTime >= startDelay)
            {
                if (isStartPressTrigger == false)
                {
                    isStartPressTrigger = true;
                    _lastPressTime = _pressDeltaTime;
                    onPressEvent.Invoke(SourcePressParam);
                }
                else
                {
                    // 현재 단계 결정
                    float currentInterval = (_pressDeltaTime >= stage3StartDelay)
                        ? stage3RepeatDelay   // 3단계: 고속
                        : stage2RepeatDelay;  // 2단계: 일반

                    // 프레임 시간 동안 누적된 레벨업 횟수 계산
                    float elapsedTime = _pressDeltaTime - _lastPressTime;
                    int levelUpCount = Mathf.FloorToInt(elapsedTime / currentInterval);

                    // 한 프레임에 여러 번 레벨업 실행!
                    if (levelUpCount > 0)
                    {
                        for (int i = 0; i < levelUpCount; i++)
                        {
                            onPressEvent.Invoke(SourcePressParam);
                        }

                        // 실제 처리된 시간만큼 업데이트
                        _lastPressTime += levelUpCount * currentInterval;
                    }
                }
            }

            // if (_pressDeltaTime >= 0)
            // {
            //     _pressDeltaTime += Time.unscaledDeltaTime;

            //     if (isStartPressTrigger == false)
            //     {
            //         if (_pressDeltaTime >= pressStartDelay)
            //         {
            //             _pressDeltaTime = PXConst.INDEX_ZERO;
            //             isStartPressTrigger = true;
            //             PressTrigger();
            //         }
            //     }
            //     else
            //     {
            //         if (_pressDeltaTime >= pressRepeatDelay)
            //         {
            //             _pressDeltaTime = PXConst.INDEX_ZERO;
            //             PressTrigger();
            //         }
            //     }
            // }
        }

        #endregion

        #region Drag Passthrough — ScrollRect 등 부모에게 드래그 이벤트 전달

        private ScrollRect _parentScrollRect;

        private ScrollRect FindParentScrollRect()
        {
            if (_parentScrollRect != null) return _parentScrollRect;
            var parent = transform.parent;
            while (parent != null)
            {
                _parentScrollRect = parent.GetComponent<ScrollRect>();
                if (_parentScrollRect != null) return _parentScrollRect;
                parent = parent.parent;
            }
            return null;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            PressFinish();
            FindParentScrollRect()?.OnBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            _parentScrollRect?.OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _parentScrollRect?.OnEndDrag(eventData);
        }

        #endregion

        protected override void OnDestroy()
        {
            onClickSimpleEvent.RemoveAllListeners();
            onClickEvent.RemoveAllListeners();
            onPressEvent.RemoveAllListeners();
        }

        protected void Update()
        {
            PressUpdate();
        }

        public void AddPressUsedCount(ECurrency InUsedCurrency, BigInteger InUsedCurrencyValue, int InValue = 1)
        {
            PressCount += InValue;

            GameAPIUserManager.Instance.userData.currencyData.AddUsedCurrencyData(InUsedCurrency, InUsedCurrencyValue);

            Debug.Log($"!!!!!!!!!!!!! PressCount = {PressCount}, InValue = {InValue}");
        }

        public void SetReddotOn(bool InOn)
        {
            Reddot.SetVisible(InOn);
        }
    }

}

