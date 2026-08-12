using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    public enum EWidgetType
    {
        Parent = 0,
        Child,
    }

    public struct UIWidgetPool
    {
        Dictionary<string, List<UIPopup>> widgetPoolDic;

        // 성능 최적화: TryGetValue 패턴으로 이중 해싱 제거
        public void AddPool(string InKey, UIPopup InWidget)
        {
            if (InWidget == null)
                return;

            if (widgetPoolDic == null)
                widgetPoolDic = new Dictionary<string, List<UIPopup>>();

            if (!widgetPoolDic.TryGetValue(InKey, out var poolList))
            {
                poolList = new List<UIPopup>();
                widgetPoolDic.Add(InKey, poolList);
            }

            poolList.Add(InWidget);
            InWidget.Hide();
        }

        // 성능 최적화: TryGetValue 패턴 + 로컬 변수 캐싱으로 Dictionary 접근 횟수 감소
        public UIPopup GetPool(string InKey)
        {
            if (widgetPoolDic != null && widgetPoolDic.TryGetValue(InKey, out var poolList) && poolList.Count > 0)
            {
                int lastIndex = poolList.Count - 1;
                UIPopup resultWidget = poolList[lastIndex];
                poolList.RemoveAt(lastIndex);
                return resultWidget;
            }

            return null;
        }

        public void PrintData()
        {
            if (widgetPoolDic != null)
            {
                Debug.Log("!!!!!!!!!!!!!!!!!!!!! Print Widget Pool");
                foreach (KeyValuePair<string, List<UIPopup>> entryData in widgetPoolDic)
                {
                    Debug.Log("Pool Widget Key = " + entryData.Key + ", Count = " + entryData.Value.Count);
                }
            }
        }
    }

    public struct UIWidgetData
    {
        static int UIUID = 0;

        public int uiUID { get; private set; }
        public string key { get; private set; }
        public UIPopup widget { get; private set; }
        public EWidgetType eWidgetType { get; private set; }

        List<UIWidgetData> childWidgets;

        public int parentUID { get; private set; }

        public UIWidgetData(string InKey, UIPopup InPopup, EWidgetType InWidgetType)
        {
            UIUID++;
            uiUID = UIUID;

            parentUID = -1;
            key = InKey;
            widget = InPopup;
            eWidgetType = InWidgetType;

            childWidgets = new List<UIWidgetData>();
        }
        public void SetParent(int InUID)
        {
            parentUID = InUID;
        }

        public void AddChild(UIWidgetData InChildWidget)
        {
            childWidgets.Add(InChildWidget);
        }
        public List<UIWidgetData> GetChildList()
        {
            return childWidgets;
        }

        /*
        public UIWidgetData RemoveChild(string InKey)
        {
            int findIndex = childWidgets.FindIndex(x => x.key == InKey);
            if(findIndex >= 0)
            {
                UIWidgetData removeWidet = childWidgets[findIndex];
                childWidgets.RemoveAt(findIndex);

                return removeWidet;
            }

            return new UIWidgetData();
        }
        */

        public UIPopup RemoveChild(int InUID)
        {
            UIPopup removeWidet = null;

            if (childWidgets.Count > 0)
            {
                int targetIndex = childWidgets.FindIndex(t => t.uiUID == InUID);

                if (targetIndex >= 0)
                {
                    removeWidet = childWidgets[targetIndex].widget;
                    childWidgets.RemoveAt(targetIndex);
                }
            }

            return removeWidet;
        }

        public void ClearChild()
        {
            childWidgets.Clear();
        }

        public bool IsVisibleWidget(string InKey)
        {
            if (InKey == key)
            {
                return widget.IsVisible;
            }
            else
            {
                for (int i = childWidgets.Count - 1; i >= 0; i--)
                {
                    if (childWidgets[i].key == InKey)
                    {
                        return childWidgets[i].widget.IsVisible;
                    }
                }
            }

            return false;
        }

        public BaseUserWidget GetWidget(string InKey)
        {
            if (InKey == key)
            {
                return widget.gameObject.GetComponent<BaseUserWidget>();
            }
            else
            {
                for (int i = childWidgets.Count - 1; i >= 0; i--)
                {
                    if (childWidgets[i].key == InKey)
                    {
                        return childWidgets[i].widget.gameObject.GetComponent<BaseUserWidget>();
                    }
                }
            }

            return null;
        }

        public void AllHide(int InExcludeUID)
        {
            for (int i = childWidgets.Count - 1; i >= 0; i--)
            {
                childWidgets[i].AllHide(InExcludeUID);
            }

            if (uiUID != InExcludeUID)
            {
                widget.Hide();
            }
        }

        public bool AllAbleShow(int InExcludeUID)
        {
            if (uiUID != InExcludeUID)
            {
                widget.Show();

                for (int i = childWidgets.Count - 1; i >= 0; i--)
                {
                    if (childWidgets[i].AllAbleShow(InExcludeUID))
                    {
                        return true;
                    }
                }

                if (widget.eFullUIScreenType == EUIFullScreenType.UIFull)
                {
                    return true;
                }
            }





            return false;
        }

        public void PrintData()
        {
            // #if UNITY_EDITOR
            //             Debug.Log("!!!!!!!!!!!!!!! UIWidgetData");
            //             Debug.Log("PrintData, Parent key = " + key);

            //             for (int i = childWidgets.Count - 1; i >= 0; i--)
            //                 Debug.Log("PrintData, child key = " + childWidgets[i].key);
            // #endif
        }
    }

    /*public delegate void LobbyMainMenuClickDelegate(EMainMenuType InMenuType);*/
    //public delegate void OpenWidgetDelegate(string InKey, UIPopup InWidget, EWidgetType InWidgetType);
    public delegate void OpenWidgetDelegate(UIWidgetData InWidgetData);
    public delegate void CloseWidgetDelegate(UIWidgetData InWidgetData);

    public class GameUIManager : SingletonDependency<GameUIManager>
    {
        UIWidgetPool widgetPool = new UIWidgetPool();
        public List<UIWidgetData> widgetDataList { get; private set; }

        public OpenWidgetDelegate openWidgetDelegateHandle { get; set; }
        public CloseWidgetDelegate closeWidgetDelegateHandle { get; set; }

        RectTransform popupArea;
        UnityEngine.UI.Image popupBG;
        LoadingWidget loadingWidget;
        ToastMessageWidget toastMessageWidget;

        GameUIManager()
        {
            widgetDataList = new List<UIWidgetData>();
        }
        ~GameUIManager()
        {

        }

        public GameObject GetOriginWidget(string InWidget)
        {
            switch (InWidget)
            {
                default:
                    {
                        return (GameObject)GameAssetBundleManager.Instance.LoadFromFile("widget_popup", InWidget);
                    }
            }
        }

        public override void Awake()
        {
            popupArea = GamePrefabManager.Instance.TemplatePrefebComp.popupArea;
            if (popupArea == null)
                Debug.LogError("GameUIManager::Awake, popupArea Not Exist");

            popupBG = GamePrefabManager.Instance.TemplatePrefebComp.popupBG;
            if (popupBG != null)
                popupBG.gameObject.SetActive(false);
            else
                Debug.LogError("GameUIManager::Awake, popupBG Not Exist");

            toastMessageWidget = GamePrefabManager.Instance.TemplatePrefebComp.toastMessageWidget;
            if (toastMessageWidget != null)
                toastMessageWidget.gameObject.SetActive(false);
            else
                Debug.LogError("GameUIManager::Awake, toastMessageWidget Not Exist");

            loadingWidget = GamePrefabManager.Instance.TemplatePrefebComp.loadingWidget;
            if (loadingWidget != null)
                loadingWidget.gameObject.SetActive(false);
            else
                Debug.LogError("GameUIManager::Awake, loadingWidget Not Exist");

            openWidgetDelegateHandle = OnOpenWidgetDelegate;
            closeWidgetDelegateHandle = OnCloseWidgetDelegate;
        }

        public override void Start()
        {
        }

        public bool IsVisibleWidget(string InKey)
        {
            for (int i = widgetDataList.Count - 1; i >= 0; i--)
            {
                if (widgetDataList[i].IsVisibleWidget(InKey))
                {
                    return true;
                }
            }

            return false;
        }

        public BaseUserWidget GetWidget(string InKey)
        {
            for (int i = widgetDataList.Count - 1; i >= 0; i--)
            {
                BaseUserWidget targetBase = widgetDataList[i].GetWidget(InKey);
                if (targetBase != null)
                {
                    return targetBase;
                }
            }

            return null;
        }


        //   public void SetBackSibling(UIPopup InFrontPopup)
        // {

        //     var frontPopup = GameUIManager.Instance.GetWidget(InFrontPopup.PopupKey);
        //     if (frontPopup == null)
        //         return;

        //     if (InFrontPopup.transform.GetSiblingIndex() == transform.GetSiblingIndex() + 1)
        //         return;

        //     transform.SetSiblingIndex(InFrontPopup.transform.GetSiblingIndex() - 1);
        // }

        public BaseUserWidget OpenWidget(string InKey, UIPopup InFrontPopup)
        {
            var openedPopup = OpenWidget(InKey, EWidgetType.Parent);

            if (InFrontPopup != null)
            {
                openedPopup.transform.SetSiblingIndex(InFrontPopup.transform.GetSiblingIndex() + 1);
            }

            return openedPopup;

        }

        public BaseUserWidget OpenWidget(string InKey, EWidgetType InType = EWidgetType.Parent)
        {
            UIPopup targetWidget = widgetPool.GetPool(InKey);
            /*
            if (targetWidget == null)
                targetWidget = UIPopup.GetPopup(InKey);
            */
            if (targetWidget == null)
            {
                GameObject loadWidget = GetOriginWidget(InKey);
                if (loadWidget != null)
                {
                    targetWidget = GameObject.Instantiate(loadWidget, popupArea.transform).GetComponent<UIPopup>(); ;
                    if (targetWidget == null)
                    {
                        Debug.LogError("OpenWidget GetWidget GameObject.Instantiate Failed, InKey = " + InKey);
                        return null;
                    }
                    targetWidget.DefineWidget();
                }
                else
                {
                    Debug.LogError("OpenWidget GetWidget Not Exist, InKey = " + InKey);
                    return null;
                }
            }

            if (targetWidget == null)
            {
                Debug.LogError("OpenWidget Not Exist, InKey = " + InKey);
                return null;
            }

            UIWidgetData addWidgetData = new UIWidgetData(InKey, targetWidget, InType);

            if (InType == EWidgetType.Parent)
            {
                widgetDataList.Add(addWidgetData);
            }
            else
            {
                if (widgetDataList.Count > 0)
                {
                    for (int i = widgetDataList.Count - 1; i >= 0; i--)
                    {
                        if (widgetDataList[i].eWidgetType == EWidgetType.Parent)
                        {
                            widgetDataList[i].AddChild(addWidgetData);
                            break;
                        }
                    }

                    //widgetDataList[widgetDataList.Count - 1].AddChild(addWidgetData);
                }
                else
                {
                    Debug.LogError("OpenWidget Not Exist Parent, InKey = " + InKey);
                    return null;
                }
            }

            BaseUserWidget targetBase = targetWidget.gameObject.GetComponent<BaseUserWidget>();
            targetBase.SetContext();

            targetBase.OnPreOpenedWidget();
            targetBase.OnOpenedWidget();
            targetBase.OnAfterOpenedWidget();
            //targetBase.Show();

            targetWidget.transform.SetAsLastSibling();

            openWidgetDelegateHandle(addWidgetData);

            return targetBase;
        }

        bool CloseWidgetData(UIWidgetData InData, string InKey)
        {
            bool IsClose = false;

            UIPopup closeWidet = InData.widget;
            string strLowerLeft = InData.key.ToLower();
            string strLowerRight = InKey.ToLower();

            if (closeWidet != null && strLowerLeft.CompareTo(strLowerRight) == 0)
            {
                //자식 위젯 Close
                {
                    List<UIWidgetData> childList = InData.GetChildList();
                    for (int k = childList.Count - 1; k >= 0; k--)
                    {
                        CloseWidgetData(childList[k], childList[k].key);
                    }

                    InData.ClearChild();
                }

                //타겟 위젯 Close
                {
                    BaseUserWidget targetBase = closeWidet.gameObject.GetComponent<BaseUserWidget>();
                    if (targetBase != null)
                        targetBase.OnClosedWidget();

                    widgetPool.AddPool(InData.key, closeWidet);

                    PrintData();
                }

                //타겟위젯 데이터 제거
                {
                    int removeIndex = widgetDataList.FindIndex(t => t.uiUID == InData.uiUID);
                    if (removeIndex >= 0)
                    {
                        widgetDataList.RemoveAt(removeIndex);
                    }
                }

                IsClose = true;
                closeWidgetDelegateHandle(InData);
            }
            else
            {
                List<UIWidgetData> childList = InData.GetChildList();

                for (int k = childList.Count - 1; k >= 0; k--)
                {
                    IsClose = CloseWidgetData(childList[k], InKey);

                    if (IsClose)
                    {
                        InData.RemoveChild(childList[k].uiUID);
                        break;
                    }
                }
            }

            return IsClose;
        }

        public void CloseAllWidget(List<string> InExcludeWidgetList = null)
        {
            for (int i = widgetDataList.Count - 1; i >= 0; i--)
            {
                UIWidgetData targetData = widgetDataList[i];

                if (InExcludeWidgetList != null)
                {
                    if (InExcludeWidgetList.Contains(targetData.key))
                        continue;
                }

                CloseWidgetData(targetData, targetData.key);
            }
        }

        public void CloseWidget(string InKey)
        {
            for (int i = widgetDataList.Count - 1; i >= 0; i--)
            {
                if (CloseWidgetData(widgetDataList[i], InKey))
                {
                    break;
                }
            }
        }

        void OnOpenWidgetDelegate(UIWidgetData InWidgetData)
        {
            if (InWidgetData.widget.eFullUIScreenType == EUIFullScreenType.UIFull)
            {
                for (int i = widgetDataList.Count - 1; i >= 0; i--)
                {
                    widgetDataList[i].AllHide(InWidgetData.uiUID);
                }
            }

            UpdatePopupBG();
            UpdateUIFullScreenStatus();
            CheckBattleSound(true, InWidgetData);
        }
        void OnCloseWidgetDelegate(UIWidgetData InWidgetData)
        {
            // AllAbleShow 는 "이 위젯(또는 그 자식)이 UIFull 이라 더 아래는 되살리면 안 된다"는 뜻으로 true 를 돌려준다.
            // 반환값을 무시하고 끝까지 돌면 UIFull 화면(예: TKScreen_Player) 진입 시 AllHide 로 숨겨둔 로비 HUD 까지
            // 전부 SetActive(true) 가 되어, 그 위에서 UINotFull 팝업(예: TKPopup_Title)을 닫는 순간
            // 로비가 화면에 겹쳐 나타난다. 최상위 UIFull 을 만나면 거기서 멈춘다.
            for (int i = widgetDataList.Count - 1; i >= 0; i--)
            {
                if (widgetDataList[i].AllAbleShow(InWidgetData.uiUID))
                    break;
            }

            UpdatePopupBG();
            UpdateUIFullScreenStatus();
            CheckBattleSound(false, InWidgetData);
        }

        void CheckBattleSound(bool InIsOpen, UIWidgetData InWidgetData)
        {
            bool isBattleSound = true;
            foreach (UIWidgetData entry in widgetDataList)
            {
                if (entry.eWidgetType == EWidgetType.Parent && entry.widget.eFullUIScreenType == EUIFullScreenType.UIFull)
                {
                    isBattleSound = false;
                    break;
                }
            }

            GameSoundManager.Instance.SetBattleSoundAudible(isBattleSound);
        }

        void UpdatePopupBG()
        {
            if (popupBG == null)
                return;

            bool hasFullScreenPopup = false;
            for (int i = widgetDataList.Count - 1; i >= 0; i--)
            {
                if (widgetDataList[i].widget != null && widgetDataList[i].widget.eFullUIScreenType == EUIFullScreenType.UIFull)
                {
                    hasFullScreenPopup = true;
                    break;
                }
            }

            popupBG.gameObject.SetActive(hasFullScreenPopup);
        }

        void UpdateUIFullScreenStatus()
        {
            EUIFullScreenType resultType = EUIFullScreenType.UINotFull;

            // 스택에서 UIFull이 하나라도 있으면 UI만 렌더링
            foreach (UIWidgetData entry in widgetDataList)
            {
                if (entry.widget != null && entry.widget.eFullUIScreenType == EUIFullScreenType.UIFull)
                {
                    resultType = EUIFullScreenType.UIFull;
                    break;
                }
            }

            GameCameraManager.Instance.SetUIFullScreenStatus(resultType);
        }

        public void PrintData()
        {
            for (int i = widgetDataList.Count - 1; i >= 0; i--)
            {
                widgetDataList[i].PrintData();
            }
        }

        public void SetLoadingOn(bool InOn)
        {
            if (loadingWidget != null)
            {
                if (InOn)
                {
                    //loadingWidget.SetVisibleLoading(true);
                    //loadingWidget.Show();
                    loadingWidget.gameObject.SetActive(true);
                    loadingWidget.transform.SetAsLastSibling();
                }
                else
                {
                    //loadingWidget.Hide();
                    loadingWidget.gameObject.SetActive(false);
                }
            }
        }

        public void ShowToastMessage(EToastMessageType InType, string InMessage, int InShowTime = 3)
        {
            toastMessageWidget.ShowToastMessage(InType, InMessage, InShowTime);
        }
    }
}