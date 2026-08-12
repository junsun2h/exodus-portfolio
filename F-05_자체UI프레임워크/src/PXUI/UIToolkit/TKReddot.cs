using System.Collections.Generic;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// UI Toolkit 커스텀 레드닷. PXReddot(UGUI)과 동일한 API 패턴을 제공한다.
    ///
    /// 사용 패턴:
    /// 1) 전역 EReddotType 기반 구독:
    ///    - SetReddotType / SetReddotTypes / AddReddotType 로 타입 지정
    ///    - GameReddotManager.OnReddotStateChanged 를 자동 구독하여 상태 갱신
    ///    - 다중 타입은 OR 조건 (하나라도 true면 표시)
    /// 2) Per-item 수동 제어(예: 개별 장비 슬롯 승급 조건):
    ///    - 타입을 지정하지 않고 SetVisible(bool) 만 호출
    /// </summary>
    [UxmlElement]
    public partial class TKReddot : VisualElement
    {
        const string USS_CLASS = "tk-reddot";

        readonly List<EReddotType> _reddotTypes = new List<EReddotType>();
        bool _isSubscribed;

        public TKReddot()
        {
            AddToClassList(USS_CLASS);
            // 깜빡임 방지: 기본은 숨김
            style.display = DisplayStyle.None;

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        // ----------------------------------------
        // Reddot 타입 설정 API (전역 EReddotType 기반)
        // ----------------------------------------

        /// <summary>단일 Reddot 타입 설정 (기존 타입 대체)</summary>
        public void SetReddotType(EReddotType reddotType)
        {
            Unsubscribe();
            _reddotTypes.Clear();

            if (reddotType != EReddotType.None)
                _reddotTypes.Add(reddotType);

            if (panel != null)
                Subscribe();
            else
                Refresh();
        }

        /// <summary>다중 Reddot 타입 설정 (params)</summary>
        public void SetReddotTypes(params EReddotType[] reddotTypes)
        {
            Unsubscribe();
            _reddotTypes.Clear();
            if (reddotTypes != null)
                _reddotTypes.AddRange(reddotTypes);

            if (panel != null)
                Subscribe();
            else
                Refresh();
        }

        /// <summary>다중 Reddot 타입 설정 (List)</summary>
        public void SetReddotTypes(List<EReddotType> reddotTypes)
        {
            Unsubscribe();
            _reddotTypes.Clear();
            if (reddotTypes != null)
                _reddotTypes.AddRange(reddotTypes);

            if (panel != null)
                Subscribe();
            else
                Refresh();
        }

        /// <summary>Reddot 타입 추가 (기존 타입 유지, OR 조건)</summary>
        public void AddReddotType(EReddotType reddotType)
        {
            if (reddotType == EReddotType.None)
                return;

            if (_reddotTypes.Contains(reddotType))
                return;

            _reddotTypes.Add(reddotType);

            if (panel != null && !_isSubscribed)
                Subscribe();
            else if (_isSubscribed)
                Refresh();
        }

        /// <summary>Reddot 타입 제거</summary>
        public void RemoveReddotType(EReddotType reddotType)
        {
            if (!_reddotTypes.Remove(reddotType))
                return;

            if (_isSubscribed)
                Refresh();
        }

        /// <summary>모든 타입 제거 및 숨김</summary>
        public void ClearReddotTypes()
        {
            Unsubscribe();
            _reddotTypes.Clear();
            SetVisible(false);
        }

        // ----------------------------------------
        // 수동 제어 API (per-item 등 전역 enum에 매핑되지 않는 케이스)
        // ----------------------------------------

        /// <summary>
        /// 수동 표시/숨김.
        /// 주의: 구독 중인 EReddotType 이 있으면 다음 Refresh 호출 시 덮어쓰여진다.
        /// Per-item 조건(예: 개별 장비 슬롯)에는 SetReddotType 없이 SetVisible 만 사용할 것.
        /// </summary>
        public void SetVisible(bool visible)
        {
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>GameReddotManager 에 현재 등록된 타입들의 재평가 요청</summary>
        public void RefreshReddotState()
        {
            var reddotManager = GameReddotManager.Instance;
            if (reddotManager == null)
                return;

            foreach (var type in _reddotTypes)
            {
                if (type != EReddotType.None)
                    reddotManager.RefreshReddot(type);
            }
        }

        // ----------------------------------------
        // 구독 라이프사이클
        // ----------------------------------------

        void OnAttachToPanel(AttachToPanelEvent evt) => Subscribe();
        void OnDetachFromPanel(DetachFromPanelEvent evt) => Unsubscribe();

        void Subscribe()
        {
            if (_isSubscribed || _reddotTypes.Count == 0)
                return;

            var reddotManager = GameReddotManager.Instance;
            if (reddotManager == null)
                return;

            reddotManager.OnReddotStateChanged += OnReddotStateChanged;
            _isSubscribed = true;

            Refresh();
        }

        void Unsubscribe()
        {
            if (!_isSubscribed)
                return;

            var reddotManager = GameReddotManager.Instance;
            if (reddotManager != null)
                reddotManager.OnReddotStateChanged -= OnReddotStateChanged;

            _isSubscribed = false;
        }

        void OnReddotStateChanged(EReddotType reddotType, bool isVisible)
        {
            if (_reddotTypes.Contains(reddotType))
                Refresh();
        }

        /// <summary>
        /// 구독 타입 상태로 표시 갱신 (OR 조건: 하나라도 true면 표시).
        /// 타입이 없으면 호출해도 상태를 변경하지 않음 — 수동 제어(SetVisible) 값을 유지.
        /// </summary>
        public void Refresh()
        {
            if (_reddotTypes.Count == 0)
                return; // 수동 제어 모드: SetVisible 가 마지막 값을 관리

            var reddotManager = GameReddotManager.Instance;
            if (reddotManager == null)
            {
                SetVisible(false);
                return;
            }

            bool shouldShow = false;
            foreach (var type in _reddotTypes)
            {
                if (type != EReddotType.None && reddotManager.GetReddotState(type))
                {
                    shouldShow = true;
                    break;
                }
            }

            SetVisible(shouldShow);
        }
    }
}
