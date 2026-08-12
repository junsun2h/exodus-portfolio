using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// DOTween ↔ UI Toolkit 브릿지.
    /// VisualElement의 style 프로퍼티를 DOTween 제네릭 API로 트윈한다.
    /// </summary>
    public static class TKTweenExtensions
    {
        /// <summary>
        /// transform.scale 트윈 (uniform)
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DOScale(
            this VisualElement el, float endValue, float duration)
        {
            float v = el.resolvedStyle.scale.value.x;
            return DOTween.To(
                () => v,
                x =>
                {
                    v = x;
                    el.style.scale = new StyleScale(new Scale(new Vector3(x, x, 1f)));
                },
                endValue, duration);
        }

        /// <summary>
        /// style.opacity 트윈
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DOFade(
            this VisualElement el, float endValue, float duration)
        {
            float v = el.resolvedStyle.opacity;
            return DOTween.To(
                () => v,
                x =>
                {
                    v = x;
                    el.style.opacity = x;
                },
                endValue, duration);
        }

        /// <summary>
        /// translate Y 트윈
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DOTranslateY(
            this VisualElement el, float endValue, float duration)
        {
            float v = el.resolvedStyle.translate.y;
            return DOTween.To(
                () => v,
                x =>
                {
                    v = x;
                    el.style.translate = new StyleTranslate(new Translate(0, x));
                },
                endValue, duration);
        }

        /// <summary>
        /// style.backgroundColor 트윈
        /// </summary>
        public static TweenerCore<Color, Color, ColorOptions> DOBackgroundColor(
            this VisualElement el, Color endValue, float duration)
        {
            Color v = el.resolvedStyle.backgroundColor;
            return DOTween.To(
                () => v,
                x =>
                {
                    v = x;
                    el.style.backgroundColor = x;
                },
                endValue, duration);
        }

        /// <summary>
        /// 팝업 등장 프리셋: Scale 0.9→1 + Fade 0→1
        /// </summary>
        public static Sequence DOPopupEnter(this VisualElement el, float duration = 0.25f)
        {
            el.style.scale = new StyleScale(new Scale(new Vector3(0.9f, 0.9f, 1f)));
            el.style.opacity = 0f;

            var seq = DOTween.Sequence();
            seq.Join(el.DOScale(1f, duration).SetEase(Ease.OutBack));
            seq.Join(el.DOFade(1f, duration * 0.6f).SetEase(Ease.OutQuad));
            return seq;
        }
    }
}
