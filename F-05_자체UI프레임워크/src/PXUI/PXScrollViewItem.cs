using UnityEngine;
using UnityEngine.UI;

namespace PX
{
    public class PXScrollViewItem : PXMonoBehaviour, IPXUITemplate
    {
        #region Bind Widgets
        [SerializeField]
        private Image _image_BG;
        [SerializeField]
        RectTransform _panel_Content;
        #endregion
    }

}