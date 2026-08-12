using System.Collections;
using DG.Tweening;
using PX;
using UnityEngine;
using UnityEngine.UI;

public class BaseUserWidget : UContextBase
{
    public EUIFullScreenType eFullUIScreenType { get; protected set; }

    public bool InitDefine { get; private set; }
    public bool InitShow { get; private set; }
    //public bool InitHide { get; private set; }
    public bool InitOpen { get; private set; }
    public bool InitClose { get; private set; }

    public bool InitPreOpen { get; private set; }
    public bool InitAfterOpen { get; private set; }


    Canvas widgetCanvas;
    GraphicRaycaster graphicRaycaster;
    CanvasGroup getCanvasGroup;

    RectTransform rectTransform;
    Coroutine aniEffectCoroutine = null;

    BaseUserWidget[] _cachedChildWidgets;
    BaseUserWidget[] CachedChildWidgets => _cachedChildWidgets ??= GetComponentsInChildren<BaseUserWidget>();

    // GetGameObject와 GetTransform은 부모 클래스 UContextBase에 정의되어 있음
    // SetContext()를 통해 초기화됨

    public CanvasGroup GetCanvasGroup
    {
        get
        {
            if (getCanvasGroup == null)
                getCanvasGroup = GetGameObject.GetComponent<CanvasGroup>();
            return getCanvasGroup;
        }

    }

    public RectTransform GetRectTransform
    {
        get
        {
            if (rectTransform == null)
                rectTransform = (RectTransform)transform;
            return rectTransform;
        }
    }

    public virtual void OnPreOpenedWidget()
    {
        if (InitPreOpen)
            return;

        InitPreOpen = true;

        BaseUserWidget[] CompArr = CachedChildWidgets;

        for (int i = 0; i < CompArr.Length; i++)
        {
            if (CompArr[i] == null || CompArr[i] == this)
            {
                continue;
            }

            try
            {
                CompArr[i].OnPreOpenedWidget();
            }
            catch (System.Exception e)
            {
                Debug.LogError("ERROR, OnPreOpenedWidget gameObject name = " + CompArr[i].gameObject.name + " , Comp name = " + CompArr[i].name + ", error = " + e.ToString());
                throw;
            }
        }
    }
    public virtual void OnAfterOpenedWidget()
    {
        if (InitAfterOpen)
            return;

        InitAfterOpen = true;

        BaseUserWidget[] CompArr = CachedChildWidgets;

        for (int i = 0; i < CompArr.Length; i++)
        {
            if (CompArr[i] == null || CompArr[i] == this)
            {
                continue;
            }

            try
            {
                CompArr[i].OnAfterOpenedWidget();
            }
            catch (System.Exception e)
            {
                Debug.LogError("ERROR, OnAfterOpenedWidget gameObject name = " + CompArr[i].gameObject.name + " , Comp name = " + CompArr[i].name + ", error = " + e.ToString());
                throw;
            }
        }
    }

    public virtual void OnOpenedWidget()
    {
        if (InitOpen)
            return;

        Show();

        InitOpen = true;
        InitClose = false;

        BaseUserWidget[] CompArr = CachedChildWidgets;

        for (int i = 0; i < CompArr.Length; i++)
        {
            if (CompArr[i] == null || CompArr[i] == this)
            {
                continue;
            }

            try
            {
                CompArr[i].OnOpenedWidget();
            }
            catch (System.Exception e)
            {
                Debug.LogError("ERROR, OnOpenedWidget gameObject name = " + CompArr[i].gameObject.name + " , Comp name = " + CompArr[i].name + ", error = " + e.ToString());
                throw;
            }
        }
    }

    public virtual void OnClosedWidget(bool InWithHide = true)
    {
        if (InitClose)
            return;

        InitClose = true;
        InitOpen = false;
        InitPreOpen = false;
        InitAfterOpen = false;

        if (InWithHide)
            Hide();

        BaseUserWidget[] CompArr = CachedChildWidgets;

        for (int i = 0; i < CompArr.Length; i++)
        {
            if (CompArr[i] != null && CompArr[i] != this && CompArr[i].gameObject.activeSelf)
                CompArr[i].OnClosedWidget(false);
        }

        _cachedChildWidgets = null;


    }

    public virtual void DefineWidget()
    {
        if (InitDefine)
            return;

        eFullUIScreenType = EUIFullScreenType.None;

        InitDefine = true;

        BaseUserWidget[] CompArr = GetComponentsInChildren<BaseUserWidget>(true);
        for (int i = 0; i < CompArr.Length; i++)
        {
            if (CompArr[i] != null && CompArr[i] != this /*&& CompArr[i].gameObject.activeSelf*/)
                CompArr[i].DefineWidget();
        }
    }

    protected override void Awake()
    {
        base.Awake();
        SetContext();  // 부모 클래스의 GetTransform, GetGameObject 초기화
    }
    protected override void Start()
    {
        base.Start();

        widgetCanvas = GetComponent<Canvas>();
        graphicRaycaster = GetComponent<GraphicRaycaster>();
    }
    protected override void OnEnable()
    {
        base.OnEnable();
    }
    protected override void OnDisable()
    {
        base.OnDisable();
    }
    protected override void Update()
    {
        base.Update();
    }

    public void Show()
    {
        //         if (InitShow)
        //             return;
        //
        //         InitShow = true;
        //         InitHide = false;

        SetVisibility(true);

        SetActiveOn(true);

        if (InitShow == false)
        {
            InitShow = true;
            OnShowEvent();
        }
    }

    public virtual void Hide()
    {
        //         if (InitHide)
        //             return;
        // 
        //         InitHide = true;
        //         InitShow = false;

        SetActiveOn(false);

        if (InitShow)
        {
            InitShow = false;
            OnHideEvent();
        }
    }
    protected virtual void OnShowEvent()
    {

    }
    protected virtual void OnHideEvent()
    {

    }

    void SetActiveOn(bool InActiveOn)
    {
        gameObject.SetActive(InActiveOn);
    }

    public virtual void SetVisibility(bool InIOn)
    {
        if (widgetCanvas != null)
            widgetCanvas.enabled = InIOn;

        if (graphicRaycaster != null)
            graphicRaycaster.enabled = InIOn;
    }

    public virtual void UpdateItem(int Index)
    {
    }

    public void SetAsLastSibling()
    {
        transform.SetAsLastSibling();
    }

    public void SetAniEffect(CanvasGroup InCanvasGroup, int InIndex)
    {
        if (gameObject.activeSelf == false || gameObject.activeInHierarchy == false)
            return;


        //지연이펙트 없이 즉시노출
        if (true)
        {
            InCanvasGroup.alpha = 1;
        }
        else
        {
            if (aniEffectCoroutine != null)
            {
                StopCoroutine(aniEffectCoroutine);
            }
            aniEffectCoroutine = StartCoroutine(AniEffectCoroutine(InCanvasGroup, InIndex));
        }

    }

    IEnumerator AniEffectCoroutine(CanvasGroup InCanvasGroup, int InIndex)
    {
        InCanvasGroup.alpha = 0;
        InCanvasGroup.DOKill();

        float showDelay = 0.05f;
        float showTime = 0.1f;

        yield return new WaitForSeconds(showDelay * InIndex);

        yield return InCanvasGroup.DOFade(1, showTime).SetEase(Ease.InOutQuart).WaitForCompletion();

    }
}
