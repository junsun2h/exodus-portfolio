using DG.Tweening;
using System.Numerics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.Rendering.PostProcessing.PostProcessResources;

namespace PX
{

    public static class TMProUGUIDoText
    {
        // 확장 메서드 사용 버전 필요없으면 this만 빼고 사용
        public static Tween DoText(this TextMeshProUGUI a_text, float a_duration)
        {
            a_text.maxVisibleCharacters = 0;
            return DOTween.To(x => a_text.maxVisibleCharacters = (int)x, 0f, a_text.text.Length, a_duration);
        }
    }

    public class PXText : PXMonoBehaviour, IPXUITemplate
    {
        public const string ConstStageName = "Stage";

        //[OnPropertyChangedCall("OnPropertyChanged")]
        public string TextID = "";

        #region Bind Widgets
        [SerializeField]
        private TextMeshProUGUI _tmp_text;
        [SerializeField]
        private Image _image_BG;
        #endregion

        #region Widgets Getter
        public TextMeshProUGUI Tmp_text { get { return _tmp_text; } }
        public Image Image_BG { get { return _image_BG; } }
        #endregion

        Tween textDurationTween;

        public bool IsTextVisible
        {
            get
            {
                return _tmp_text.maxVisibleCharacters > 0;
            }
        }
        /*
        public void OnPropertyChanged()
        {
            Tmp_text.SetText(Text);
        }
        */

        protected override void Awake()
        {
            base.Awake();

            /*OnPropertyChanged();*/
        }

        protected override void Start()
        {
            base.Start();

#if UNITY_EDITOR
            UpdateShaderForce();
#endif
        }

#if UNITY_EDITOR
        /*
         * Unity 최신버전 Shader 미갱신 이슈로 Shader 강제교체 적용
         */
        void UpdateShaderForce()
        {
            Material[] materials = _tmp_text.fontSharedMaterials;
            string[] shaders = new string[materials.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                shaders[i] = materials[i].shader.name;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i].shader = Shader.Find(shaders[i]);
            }
        }
#endif

        public void SetCountText(BigInteger InValue, float InTextDuration = 0)
        {
            SetText(GameUtility.ConvertToAlphabetValue(InValue), InTextDuration);
        }

        // public void SetTextKey(ENebula InNebula, float InTextDuration = 0)
        // {
        //     //string strText = GameUtility.GetTextKey(InKey);
        //     string strText = string.Empty;
        //     switch (InNebula)
        //     {
        //         case ENebula.nebula_asgard: strText = "아스가르드"; break;
        //         case ENebula.nebula_east: strText = "동방"; break;
        //         case ENebula.nebula_eden: strText = "에덴"; break;
        //         case ENebula.nebula_hell: strText = "지옥"; break;
        //         case ENebula.nebula_olympos: strText = "올림푸스"; break;
        //     }

        //     SetText(strText, InTextDuration);
        // }
        public void SetTextKey(string InKey, float InTextDuration = 0)
        {
            string strText = GameUtility.GetTextKey(InKey);

            SetText(strText, InTextDuration);
        }

        public void SetText(string InText, string InColor, float InTextDuration = 0)
        {
            SetText(PXText.WrapString(InText, InColor), InTextDuration);
        }
        public void SetText(string InText, float InTextDuration = 0)
        {
            if (InText == null)
            {
                _tmp_text.SetText("null");
                Debug.LogError("SetText Null Text");
                return;
            }

            _tmp_text.SetText(InText);

            if (InTextDuration > 0)
            {
                textDurationTween = TMProUGUIDoText.DoText(_tmp_text, InTextDuration);
            }
            else
            {
                textDurationTween.Kill();
                _tmp_text.maxVisibleCharacters = _tmp_text.text.Length;
            }
        }

        public void SetTextVisible(bool InOn)
        {
            textDurationTween.Kill();
            _tmp_text.maxVisibleCharacters = InOn ? _tmp_text.text.Length : 0;
        }

        public void SetBGVisible(bool InOn)
        {
            _image_BG.enabled = InOn;
        }

        public void SetColor(Color InColor)
        {
            _tmp_text.color = InColor;
        }
        public void SetFontSize(float InSize)
        {
            _tmp_text.fontSize = InSize;
        }

        public static string WrapString(string input, string color)
        {
            return "<color=#" + color + ">" + input + "</color>";
        }
    }

}