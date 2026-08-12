using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace PX
{
    public class PXImage : PXMonoBehaviour, IPXUITemplate
    {
        #region Bind Widgets
        [SerializeField]
        private Image image_Texture;
        #endregion

        //public List<Sprite> spriteList;

        public Image GetImage
        {
            get
            {
                return image_Texture;
            }
        }

        public void SetSpriteIndex(int inIndex, List<Sprite> spriteList)
        {
            if (inIndex >= 0 && inIndex < spriteList.Count)
            {
                if (spriteList[inIndex] != null)
                {
                    SetSprite(spriteList[inIndex]);
                }
                else
                {
                    Debug.LogError($"PXImage, Texture Null, inIndex  = {inIndex}");
                }
            }
        }

        public void SetSprite(Sprite inSprite)
        {
            if (image_Texture == null)
                return;

            image_Texture.sprite = inSprite;
        }

        public void SetSprite(string inAtlasBundleName, string inAtlasSpriteName, string inSpriteName)
        {
            var testAtlas = (SpriteAtlas)GameAssetBundleManager.Instance.LoadFromFile(inAtlasBundleName, inAtlasSpriteName);
            if (testAtlas != null)
            {
                SetSprite(testAtlas.GetSprite(inSpriteName));
            }
        }

        public void SetColor(Color InColor)
        {
            image_Texture.color = InColor;
        }
    }
}