using UnityEngine;

namespace PX
{
    public class PXListViewItem_Sample : PXListViewItem
    {
        public PXText textInfo;


        public override void UpdateItem()
        {
            textInfo.SetText($"Slot {Index}");
            Debug.Log($"Slot {Index}");
        }
    }
}

