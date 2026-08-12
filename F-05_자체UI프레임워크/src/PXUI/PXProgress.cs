using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;
using static UnityEngine.UI.Slider;
using DG.Tweening;

namespace PX
{
    public class PXProgress : PXMonoBehaviour, IPXUITemplate
    {
        #region Bind Widgets
        [SerializeField]
        private Slider _slider;
        [SerializeField]
        private Image _image_BG;
        [SerializeField]
        private Image _image_Handle;
        [SerializeField]
        private PXText _pxText;
        #endregion

        #region Animation Settings
        [Header("Progress Bar Animation")]
        [SerializeField]
        private bool _enableAni = false;
        [SerializeField]
        private float _animationDuration = 0.3f;
        [SerializeField]
        private Ease _animationEase = Ease.OutQuad;

        private Tweener _progressTweener;
        #endregion

        #region Events
        [Serializable]
        public class PXProgressEvent : UnityEvent<float, string> { }
        [FormerlySerializedAs("OnPXProgressEvent")]
        [SerializeField]
        private PXProgressEvent m_OnProgress = new PXProgressEvent();
        public PXProgressEvent onProgressEvent
        {
            get { return m_OnProgress; }
            set { m_OnProgress = value; }
        }
        #endregion

        #region Properties
        public string sourceProgressParam { get; set; }

        public float normalizedValue
        {
            get { return _slider.normalizedValue; }
            set { SetNormalizedValue(value, _enableAni); }
        }

        public virtual float value
        {
            get { return _slider.value; }
            set { SetValue(value, _enableAni); }
        }

        public bool wholeNumbers { get { return _slider.wholeNumbers; } set { _slider.wholeNumbers = value; } }
        public float maxValue { get { return _slider.maxValue; } set { _slider.maxValue = value; } }
        public float minValue { get { return _slider.minValue; } set { _slider.minValue = value; } }
        public bool interactable { get { return _slider.interactable; } set { _slider.interactable = value; } }
        public Direction direction { get { return _slider.direction; } set { _slider.direction = value; } }
        public RectTransform handleRect { get { return _slider.handleRect; } set { _slider.handleRect = value; } }
        public RectTransform fillRect { get { return _slider.fillRect; } set { _slider.fillRect = value; } }
        public PXText GetPXText { get { return _pxText; } }

        // 애니메이션 설정 접근자
        public bool enableAnimation { get { return _enableAni; } set { _enableAni = value; } }
        public float animationDuration { get { return _animationDuration; } set { _animationDuration = value; } }
        public Ease animationEase { get { return _animationEase; } set { _animationEase = value; } }
        #endregion


        #region Unity Lifecycle
        protected override void Awake()
        {
            base.Awake();

            interactable = false;

            _slider.onValueChanged.AddListener((float value) =>
            {
                onProgressEvent.Invoke(value, sourceProgressParam);
            });
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // 진행 중인 애니메이션 정리
            _progressTweener?.Kill();
            _progressTweener = null;

            _slider.onValueChanged.RemoveAllListeners();
            onProgressEvent.RemoveAllListeners();
        }
        #endregion

        #region Progress Control Methods
        /// <summary>
        /// 진행 바 값을 설정합니다
        /// </summary>
        /// <param name="targetValue">목표 값</param>
        /// <param name="useAnimation">true: 애니메이션 적용, false: 즉시 적용</param>
        public void SetValue(float targetValue, bool useAnimation)
        {
            if (_slider == null)
                return;

            // 즉시 적용 모드 또는 애니메이션 조건 미충족 시
            if (!useAnimation || _animationDuration <= 0f)
            {
                // 진행 중인 애니메이션 중단
                _progressTweener?.Kill();
                _progressTweener = null;

                _slider.value = targetValue;
                return;
            }

            // 진행 중인 트윈 중단 및 정리
            _progressTweener?.Kill();
            _progressTweener = null;

            // 애니메이션 적용
            _progressTweener = _slider.DOValue(targetValue, _animationDuration)
                .SetEase(_animationEase)
                .SetAutoKill(true);
        }

        /// <summary>
        /// 진행 바 Normalized 값을 설정합니다 (0~1 범위)
        /// </summary>
        /// <param name="targetValue">목표 값 (0~1)</param>
        /// <param name="useAnimation">true: 애니메이션 적용, false: 즉시 적용</param>
        public void SetNormalizedValue(float targetValue, bool useAnimation)
        {
            if (_slider == null)
                return;

            // 즉시 적용 모드 또는 애니메이션 조건 미충족 시
            if (!useAnimation || _animationDuration <= 0f)
            {
                // 진행 중인 애니메이션 중단
                _progressTweener?.Kill();
                _progressTweener = null;

                _slider.normalizedValue = targetValue;
                return;
            }

            // 진행 중인 트윈 중단 및 정리
            _progressTweener?.Kill();
            _progressTweener = null;

            // 애니메이션 적용
            _progressTweener = DOTween.To(
                () => _slider.normalizedValue,
                x => _slider.normalizedValue = x,
                targetValue,
                _animationDuration
            )
            .SetEase(_animationEase)
            .SetAutoKill(true);
        }

        /// <summary>
        /// 애니메이션을 즉시 완료하고 목표 값으로 설정합니다
        /// </summary>
        public void CompleteAnimation()
        {
            if (_progressTweener != null && _progressTweener.IsActive())
            {
                _progressTweener.Complete();
                _progressTweener = null;
            }
        }

        /// <summary>
        /// 진행 중인 애니메이션을 취소합니다 (현재 위치에서 멈춤)
        /// </summary>
        public void CancelAnimation()
        {
            _progressTweener?.Kill();
            _progressTweener = null;
        }

        /// <summary>
        /// 애니메이션이 진행 중인지 확인합니다
        /// </summary>
        public bool IsAnimating()
        {
            return _progressTweener != null && _progressTweener.IsActive();
        }
        #endregion
    }
}