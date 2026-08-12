
using UnityEditor;
using UnityEngine;

public class PXUITemplateEditor
{
    const string pxTemplatePath = "Assets/GameAssets/UI/Prefab/PXUI/Template/";
    const string pxWidgetPath = "Assets/GameAssets/UI/Prefab/PXUI/Widget/";
    const string pxPopupPath = "Assets/GameAssets/UI/Prefab/PXUI/Popup/";

    #region PX Template
    [MenuItem("GameObject/UI PX/Template/PXPanel")]
    public static void CreateUICore_Template_PXPanel()
    {
        CreateUICoreTemplate("PXPanel", true);
    }
    [MenuItem("GameObject/UI PX/Template/PXButton")]
    public static void CreateUICore_Template_PXButton()
    {
        CreateUICoreTemplate("PXButton");
    }
    [MenuItem("GameObject/UI PX/Template/PXImage")]
    public static void CreateUICore_Template_PXImage()
    {
        CreateUICoreTemplate("PXImage");
    }
    [MenuItem("GameObject/UI PX/Template/PXText")]
    public static void CreateUICore_Template_PXText()
    {
        CreateUICoreTemplate("PXText");
    }
    [MenuItem("GameObject/UI PX/Template/PXCheckBox")]
    public static void CreateUICore_Template_PXCheckBox()
    {
        CreateUICoreTemplate("PXCheckBox");
    }
    [MenuItem("GameObject/UI PX/Template/PXRadioGroup")]
    public static void CreateUICore_Template_PXRadioGroup()
    {
        CreateUICoreTemplate("PXRadioGroup");
    }
    [MenuItem("GameObject/UI PX/Template/PXDropdown")]
    public static void CreateUICore_Template_PXDropdown()
    {
        CreateUICoreTemplate("PXDropdown");
    }
    [MenuItem("GameObject/UI PX/Template/PXProgress")]
    public static void CreateUICore_Template_PXProgress()
    {
        CreateUICoreTemplate("PXProgress");
    }
    [MenuItem("GameObject/UI PX/Template/PXReddot")]
    public static void CreateUICore_Template_PXReddot()
    {
        CreateUICoreTemplate("PXReddot");
    }
    [MenuItem("GameObject/UI PX/Template/PXListView_Horizon")]
    public static void CreateUICore_Template_PXListView_Horizon()
    {
        CreateUICoreTemplate("PXListView_Horizon");
    }
    [MenuItem("GameObject/UI PX/Template/PXListView_Vertical")]
    public static void CreateUICore_Template_PXListView_Vertical()
    {
        CreateUICoreTemplate("PXListView_Vertical");
    }
    [MenuItem("GameObject/UI PX/Template/PXListViewItem_MakeBase")]
    public static void CreateUICore_Template_PXListViewItem_MakeBase()
    {
        CreateUICoreTemplate("PXListViewItem_MakeBase");
    }
    [MenuItem("GameObject/UI PX/Template/PXScrollView_Horizon")]
    public static void CreateUICore_Template_PXScrollView_Horizon()
    {
        CreateUICoreTemplate("PXScrollView_Horizon");
    }
    [MenuItem("GameObject/UI PX/Template/PXScrollView_Vertical")]
    public static void CreateUICore_Template_PXScrollView_Vertical()
    {
        CreateUICoreTemplate("PXScrollView_Vertical");
    }
    [MenuItem("GameObject/UI PX/Template/PXScrollViewItem_MakeBase")]
    public static void CreateUICore_Template_PXScrollViewItem_MakeBase()
    {
        CreateUICoreTemplate("PXScrollViewItem_MakeBase");
    }
    #endregion

    #region PX Popup
    [MenuItem("GameObject/UI PX/Popup/PXPopup_MakeBase")]
    public static void CreateUICore_PXPopup_MakeBase()
    {
        CreateUICorePopup("PXPopup_MakeBase", true);
    }
    #endregion
    #region PX Widget
    [MenuItem("GameObject/UI PX/Widget/PXWidget_MakeBase")]
    public static void CreateUICore_PXWidget_MakeBase()
    {
        CreateUICoreWidget("PXWidget_MakeBase");
    }
    #endregion

    static void CreateUICoreTemplate(string inBpName, bool inInstance = false)
    {
        string path = pxTemplatePath + inBpName + ".prefab";
        CreateUIPrefab(path, inBpName, inInstance);
    }
    static void CreateUICorePopup(string inBpName, bool isFullScreen)
    {
        string path = pxPopupPath + inBpName + ".prefab";
        GameObject uiObject = CreateUIPrefab(path, inBpName, true);
        if (uiObject != null)
        {
            if (isFullScreen)
            {
                uiObject.GetComponent<RectTransform>().SetLeft(0);
                uiObject.GetComponent<RectTransform>().SetRight(0);
                uiObject.GetComponent<RectTransform>().SetTop(0);
                uiObject.GetComponent<RectTransform>().SetBottom(0);
            }
        }
    }
    static void CreateUICoreWidget(string inBpName)
    {
        string path = pxWidgetPath + inBpName + ".prefab";
        CreateUIPrefab(path, inBpName, true);
    }

    static GameObject CreateUIPrefab(string inPath, string inBpName, bool inInstance)
    {
        try
        {
            Object obj = AssetDatabase.LoadAssetAtPath(inPath, typeof(Object));
            if (obj == null)
            {
                EditorUtility.DisplayDialog("Error", "Can not Find Prefab in Path:" + inPath, "ok");
            }
            else
            {
                GameObject pxObject = null;
                //독립 인스턴스로 생성
                if (inInstance)
                {
                    pxObject = GameObject.Instantiate((GameObject)obj) as GameObject;
                }
                //프리팹 원본으로 사용
                else
                {
                    pxObject = PrefabUtility.InstantiatePrefab(obj) as GameObject;
                }

                Vector3 scale = pxObject.transform.localScale;
                pxObject.name = inBpName;
                if (Selection.activeGameObject != null)
                {
                    pxObject.transform.SetParent(Selection.activeGameObject.transform);
                    pxObject.transform.localScale = scale;
                    pxObject.transform.localPosition = Vector3.zero;
                }

                return pxObject;
            }
        }
        catch
        {
            EditorUtility.DisplayDialog("Error", $"Can not Find Prefab in inPath = {inPath}, inBpName = {inBpName}", "ok");
        }

        return null;
    }
}
