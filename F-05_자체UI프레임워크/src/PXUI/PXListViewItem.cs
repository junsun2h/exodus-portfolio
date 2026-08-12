using UnityEngine;
using UnityEngine.UI;

namespace PX
{
    public class PXListViewItem : PXListViewItemCore, IPXUITemplate
    {
        #region Bind Widgets
        //[SerializeField]
        //private Image _image_BG;
        [SerializeField]
        RectTransform _panel_Content;
        #endregion
        public virtual int GetSlotCount() { return 1; }

        //public Image GetImageBG { get { return _image_BG; } }
        public RectTransform GetPanelContent { get { return _panel_Content; } }

        public virtual void UpdateItem()
        {
        }

        public override void Reload(PXListViewCore infinity, int _index)
        {
            base.Reload(infinity, _index);

            UpdateItem();
        }
        public override void SelfReload()
        {
            base.SelfReload();

            if (Index != int.MinValue)
            {
                UpdateItem();
            }
        }
    }
}