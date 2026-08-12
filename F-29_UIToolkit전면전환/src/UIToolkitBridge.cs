using UnityEngine;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// UIPopup을 상속하여 GameUIManager 수정 없이 UI Toolkit을 통합하는 브릿지 클래스.
    /// UIPopup의 RequireComponent(Canvas, RectTransform, GraphicRaycaster)를 유지하면서
    /// UIDocument를 통해 UI Toolkit 렌더링을 수행한다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public abstract class UIToolkitBridge : UIPopup
    {
        UIDocument _uiDocument;
        bool _isBound;

        // 바인딩을 마친 VisualTree 루트. GameObject 재활성화로 UIDocument 가 트리를 새로 만들면
        // 이 참조와 달라지므로, "트리가 교체됐는가 = 재바인딩이 필요한가" 판별 기준으로 쓴다.
        VisualElement _boundRoot;

        /// <summary>
        /// UIDocument 컴포넌트
        /// </summary>
        public UIDocument UIDocument
        {
            get
            {
                if (_uiDocument == null)
                    _uiDocument = GetComponent<UIDocument>();
                return _uiDocument;
            }
        }

        /// <summary>
        /// UXML 루트 VisualElement (캐시하지 않음 — GameObject 재활성화 시 재생성되므로)
        /// </summary>
        public VisualElement Root => UIDocument != null ? UIDocument.rootVisualElement : null;

        protected override void Awake()
        {
            base.Awake();

            _uiDocument = GetComponent<UIDocument>();
        }

        /// <summary>
        /// 이름으로 VisualElement 조회 헬퍼
        /// </summary>
        protected T Q<T>(string name) where T : VisualElement
        {
            return Root?.Q<T>(name);
        }

        /// <summary>
        /// 이름으로 VisualElement 조회 헬퍼
        /// </summary>
        protected VisualElement Q(string name)
        {
            return Root?.Q(name);
        }

        /// <summary>
        /// 클래스명으로 VisualElement 목록 조회 헬퍼
        /// </summary>
        protected UQueryBuilder<T> QAll<T>(string className = null) where T : VisualElement
        {
            return Root.Query<T>(className: className);
        }

        #region 생명주기 매핑

        public override void DefineWidget()
        {
            base.DefineWidget();
            OnDefineUI();
        }

        public override void OnPreOpenedWidget()
        {
            base.OnPreOpenedWidget();
        }

        public override void OnOpenedWidget()
        {
            // base 내부의 Show() → OnShowEvent() 에서 이미 바인딩됐을 수 있다.
            // (EnsureBound 가 같은 트리에 대한 중복 바인딩을 걸러내므로 두 번 호출해도 안전)
            base.OnOpenedWidget();

            EnsureBound();

            OnOpenUI();
        }

        public override void OnAfterOpenedWidget()
        {
            base.OnAfterOpenedWidget();
            OnAfterOpenUI();
        }

        public override void OnClosedWidget(bool InWithHide = true)
        {
            Unbind();
            base.OnClosedWidget(InWithHide);
        }

        /// <summary>
        /// GameObject 재활성화 시점 (UIFull 화면 진입 후 복귀 등, GameUIManager.AllAbleShow → Show()).
        ///
        /// UIDocument 는 비활성화될 때 VisualTree 를 파기하고, 재활성화될 때 UXML 에서 새로 CloneTree 한다.
        /// 즉 이전에 등록한 이벤트 핸들러와 요소 참조가 전부 무효가 되고 라벨은 UXML 원본 텍스트로 돌아간다.
        /// 이 경로는 OnOpenedWidget 을 거치지 않으므로, 여기서 새 트리에 다시 바인딩해야 한다.
        /// </summary>
        protected override void OnShowEvent()
        {
            base.OnShowEvent();

            bool rebound = EnsureBound();

            // 이미 열려 있던(InitOpen) 위젯이 다시 활성화된 경우는 OnOpenedWidget 이 뒤따르지 않으므로
            // 데이터 갱신(OnOpenUI)도 여기서 직접 수행한다. 최초 오픈 경로는 Show() 시점에 InitOpen 이
            // 아직 false 라 스킵되고, 곧이어 OnOpenedWidget 이 OnOpenUI 를 호출한다(중복 방지).
            if (rebound && InitOpen)
                OnOpenUI();

            if (Root != null)
                Root.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// GameObject 비활성화 시점 (UIFull 화면 진입으로 GameUIManager.AllHide → Hide()).
        /// 파기될 트리에 대한 바인딩을 정리한다. 여기서 풀지 않으면 재활성화 때 OnBindUI 가 다시 구독하면서
        /// 델리게이트/스케줄이 중복 누적된다.
        /// </summary>
        protected override void OnHideEvent()
        {
            base.OnHideEvent();

            Unbind();
        }

        /// <summary>
        /// VisualTree 바인딩이 안 되어 있으면 실행. 실제로 바인딩을 수행했으면 true.
        /// </summary>
        bool EnsureBound()
        {
            VisualElement root = Root;
            if (root == null)
                return false;

            // 같은 VisualTree 에 이미 바인딩돼 있으면 재실행하지 않는다 (이벤트 중복 등록 방지)
            if (_isBound && ReferenceEquals(_boundRoot, root))
                return false;

            // 루트 VisualElement가 UIDocument 패널 전체를 채우도록 설정
            root.style.flexGrow = 1;

            // 디자인 프리뷰 요소 제거 (UI Builder용 미리보기, 런타임에서 불필요)
            StripDesignPreview();

            // 공용 currency-bar 자동 바인딩 (클릭 시 전체 재화 팝업 오픈)
            BindCurrencyBar();

            _boundRoot = root;
            _isBound = true;
            OnBindUI();

            return true;
        }

        /// <summary>
        /// 바인딩 해제. OnCloseUI 로 구독/스케줄을 풀고 바인딩 상태를 초기화한다.
        /// (Close 와 Hide 양쪽에서 호출되므로 _isBound 로 중복 실행을 막는다)
        /// </summary>
        void Unbind()
        {
            if (_isBound == false)
                return;

            _isBound = false;
            _boundRoot = null;
            OnCloseUI();
        }

        /// <summary>
        /// name="currency-bar" 요소 클릭 시 TKPopup_AllCurrency를 오픈한다.
        /// 모든 화면/팝업의 보유 재화 표시 영역에 공통 적용되는 규칙.
        /// </summary>
        void BindCurrencyBar()
        {
            var currencyBar = Root.Q("currency-bar");
            if (currencyBar == null)
                return;

            currencyBar.RegisterCallback<ClickEvent>(_ =>
            {
                GameUIManager.Instance.OpenWidget("TKPopup_AllCurrency");
            });

            // 자식이 없으면 배경/테두리까지 빈 채로 보이므로 .is-empty 토글로 숨긴다.
            // GeometryChangedEvent는 자식 추가/제거 시에도 발화하므로 동적 변화를 따라간다.
            currencyBar.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                currencyBar.EnableInClassList("is-empty", currencyBar.childCount == 0);
            });
            currencyBar.EnableInClassList("is-empty", currencyBar.childCount == 0);
        }

        /// <summary>
        /// UXML에 배치된 디자인 프리뷰 요소(.design-preview)를 일괄 제거한다.
        /// OnBindUI 전에 호출되므로, 각 화면의 바인딩 로직에는 영향을 주지 않는다.
        /// </summary>
        void StripDesignPreview()
        {
            var previews = Root.Query(className: "design-preview").ToList();
            for (int i = previews.Count - 1; i >= 0; i--)
            {
                previews[i].RemoveFromHierarchy();
            }
        }

        #endregion

        #region UI Toolkit 전용 생명주기 (서브클래스에서 구현)

        /// <summary>
        /// DefineWidget 단계에서 1회만 호출. PopupKey, eFullUIScreenType 설정.
        /// </summary>
        protected virtual void OnDefineUI() { }

        /// <summary>
        /// 위젯이 열릴 때 + 다시 활성화될 때마다 호출. UI 요소 조회 및 이벤트 바인딩.
        /// GameObject 재활성화 시 VisualTree가 재생성되므로 매번 바인딩 필요.
        /// (UIFull 화면 진입 → 복귀처럼 Open 을 거치지 않는 재활성화도 포함된다 — OnShowEvent 참조)
        /// </summary>
        protected virtual void OnBindUI() { }

        /// <summary>
        /// OnOpenedWidget 단계에서 호출. UI 데이터 갱신.
        /// </summary>
        protected virtual void OnOpenUI() { }

        /// <summary>
        /// OnAfterOpenedWidget 단계에서 호출. 추가 초기화 수행.
        /// </summary>
        protected virtual void OnAfterOpenUI() { }

        /// <summary>
        /// OnClosedWidget 단계 + GameObject 비활성화 시 호출. 이벤트 해제 등 정리.
        /// OnBindUI 와 정확히 짝을 이루므로, 여기서 구독을 풀지 않으면 재활성화 때 중복 구독이 쌓인다.
        /// </summary>
        protected virtual void OnCloseUI() { }

        #endregion
    }
}
