using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PX
{
    /// <summary>
    /// Reddot UI 컴포넌트
    /// GameReddotManager와 자동 연동하여 Reddot 타입의 상태 변경 시 자동으로 표시/숨김 처리
    /// - 다중 타입 지원: OR 조건 - 하나라도 true면 표시
    /// - 단일 타입: 리스트에 1개만 추가하면 됨
    /// </summary>
    public class PXReddot : PXMonoBehaviour
    {
        [SerializeField]
        private Image _image_Dot;

        [Header("Reddot 설정")]
        [SerializeField]
        [Tooltip("Reddot 타입 리스트 (OR 조건 - 하나라도 true면 표시)")]
        private List<EReddotType> _reddotTypes = new List<EReddotType>();

        [SerializeField]
        [Tooltip("GameReddotManager에 자동으로 구독할지 여부")]
        private bool _autoSubscribe = true;

        private bool _isSubscribed = false;

        /// <summary>
        /// 단일 Reddot 타입 프로퍼티 (편의 프로퍼티 - 리스트의 첫 번째 요소)
        /// </summary>
        public EReddotType ReddotType
        {
            get
            {
                return (_reddotTypes != null && _reddotTypes.Count > 0)
                    ? _reddotTypes[0]
                    : EReddotType.None;
            }
            set
            {
                if (_reddotTypes == null)
                    _reddotTypes = new List<EReddotType>();

                // 기존 구독 해제
                if (_isSubscribed)
                {
                    Unsubscribe();
                }

                // 리스트 초기화 후 단일 타입 설정
                _reddotTypes.Clear();
                if (value != EReddotType.None)
                {
                    _reddotTypes.Add(value);
                }

                // 새 타입 구독
                if (_autoSubscribe && enabled)
                {
                    Subscribe();
                }
            }
        }

        /// <summary>
        /// 다중 Reddot 타입 리스트 프로퍼티
        /// </summary>
        public List<EReddotType> ReddotTypes
        {
            get { return _reddotTypes; }
            set
            {
                // 기존 구독 해제
                if (_isSubscribed)
                {
                    Unsubscribe();
                }

                _reddotTypes = value ?? new List<EReddotType>();

                // 새 타입 구독
                if (_autoSubscribe && enabled)
                {
                    Subscribe();
                }
            }
        }

        /// <summary>
        /// 자동 구독 여부 프로퍼티
        /// </summary>
        public bool AutoSubscribe
        {
            get { return _autoSubscribe; }
            set
            {
                if (_autoSubscribe != value)
                {
                    _autoSubscribe = value;

                    if (_autoSubscribe && enabled && HasAnyReddotType())
                    {
                        Subscribe();
                    }
                    else if (!_autoSubscribe && _isSubscribed)
                    {
                        Unsubscribe();
                    }
                }
            }
        }

        protected override void Awake()
        {
            base.Awake();

            // Reddot 초기 상태는 비활성화 (깜빡임 방지)
            if (_image_Dot != null)
            {
                _image_Dot.gameObject.SetActive(false);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (_autoSubscribe && HasAnyReddotType())
            {
                Subscribe();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            if (_isSubscribed)
            {
                Unsubscribe();
            }
        }

        /// <summary>
        /// 등록된 Reddot 타입이 있는지 확인
        /// </summary>
        private bool HasAnyReddotType()
        {
            if (_reddotTypes == null || _reddotTypes.Count == 0)
                return false;

            // None이 아닌 타입이 하나라도 있는지 확인
            foreach (var type in _reddotTypes)
            {
                if (type != EReddotType.None)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// GameReddotManager에 구독
        /// </summary>
        private void Subscribe()
        {
            if (_isSubscribed || !HasAnyReddotType())
                return;

            var reddotManager = GameReddotManager.Instance;
            if (reddotManager != null)
            {
                reddotManager.OnReddotStateChanged += OnReddotStateChanged;
                _isSubscribed = true;

                // 현재 상태로 즉시 갱신
                Refresh();
            }
        }

        /// <summary>
        /// GameReddotManager 구독 해제
        /// </summary>
        private void Unsubscribe()
        {
            if (!_isSubscribed)
                return;

            var reddotManager = GameReddotManager.Instance;
            if (reddotManager != null)
            {
                reddotManager.OnReddotStateChanged -= OnReddotStateChanged;
            }

            _isSubscribed = false;
        }

        /// <summary>
        /// Reddot 상태 변경 이벤트 콜백
        /// </summary>
        private void OnReddotStateChanged(EReddotType reddotType, bool isVisible)
        {
            // 리스트에 포함된 타입이면 갱신
            if (_reddotTypes != null && _reddotTypes.Contains(reddotType))
            {
                Refresh();
            }
        }

        /// <summary>
        /// Reddot 표시/숨김 설정
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_image_Dot != null)
            {
                _image_Dot.gameObject.SetActive(visible);
            }
            else
            {
                // Image가 없으면 자기 자신의 GameObject 활성화 제어
                gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 단일 Reddot 타입 설정 (구독 상태 갱신 포함)
        /// </summary>
        public void SetReddotType(EReddotType reddotType)
        {
            ReddotType = reddotType;
        }

        /// <summary>
        /// 다중 Reddot 타입 설정 (구독 상태 갱신 포함)
        /// </summary>
        public void SetReddotTypes(List<EReddotType> reddotTypes)
        {
            ReddotTypes = reddotTypes;
        }

        /// <summary>
        /// 다중 Reddot 타입 설정 (params 배열 버전)
        /// </summary>
        public void SetReddotTypes(params EReddotType[] reddotTypes)
        {
            ReddotTypes = new List<EReddotType>(reddotTypes);
        }

        /// <summary>
        /// Reddot 타입 추가 (기존 타입 유지)
        /// </summary>
        public void AddReddotType(EReddotType reddotType)
        {
            if (reddotType == EReddotType.None)
                return;

            if (_reddotTypes == null)
                _reddotTypes = new List<EReddotType>();

            if (!_reddotTypes.Contains(reddotType))
            {
                _reddotTypes.Add(reddotType);

                // 이미 구독 중이면 갱신
                if (_isSubscribed)
                {
                    Refresh();
                }
            }
        }

        /// <summary>
        /// Reddot 타입 제거
        /// </summary>
        public void RemoveReddotType(EReddotType reddotType)
        {
            if (_reddotTypes != null && _reddotTypes.Contains(reddotType))
            {
                _reddotTypes.Remove(reddotType);

                // 이미 구독 중이면 갱신
                if (_isSubscribed)
                {
                    Refresh();
                }
            }
        }

        /// <summary>
        /// 모든 Reddot 타입 제거
        /// </summary>
        public void ClearReddotTypes()
        {
            if (_reddotTypes != null)
                _reddotTypes.Clear();

            if (_isSubscribed)
            {
                Unsubscribe();
            }

            SetVisible(false);
        }

        /// <summary>
        /// 현재 Reddot 상태로 UI 갱신
        /// OR 조건: 하나라도 true면 표시
        /// </summary>
        public void Refresh()
        {
            var reddotManager = GameReddotManager.Instance;
            if (reddotManager == null)
            {
                SetVisible(false);
                return;
            }

            // 리스트가 비어있으면 숨김
            if (_reddotTypes == null || _reddotTypes.Count == 0)
            {
                SetVisible(false);
                return;
            }

            // OR 조건: 하나라도 true면 표시
            bool shouldShow = false;
            foreach (var type in _reddotTypes)
            {
                if (type != EReddotType.None && reddotManager.GetReddotState(type))
                {
                    shouldShow = true;
                    break; // 하나라도 true면 즉시 종료
                }
            }

            SetVisible(shouldShow);
        }

        /// <summary>
        /// 수동으로 Reddot 상태 갱신 요청
        /// </summary>
        public void RefreshReddotState()
        {
            var reddotManager = GameReddotManager.Instance;
            if (reddotManager == null || _reddotTypes == null)
                return;

            // 모든 등록된 타입 갱신
            foreach (var type in _reddotTypes)
            {
                if (type != EReddotType.None)
                {
                    reddotManager.RefreshReddot(type);
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 에디터에서 값 변경 시 호출 (디버깅용)
        /// </summary>
        private void OnValidate()
        {
            // 에디터에서 타입 변경 시 즉시 반영
            if (Application.isPlaying && enabled)
            {
                if (_autoSubscribe && HasAnyReddotType())
                {
                    // 기존 구독 해제 후 재구독
                    Unsubscribe();
                    Subscribe();
                }
            }
        }
#endif
    }
}

