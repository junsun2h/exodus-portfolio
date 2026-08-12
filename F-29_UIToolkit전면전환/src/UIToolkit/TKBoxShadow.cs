using UnityEngine;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// 프로그래매틱 박스 셰도우/글로우 VisualElement.
    /// 동적 크기/색상 변화가 필요할 때 사용 (등급별 글로우, 선택 상태 하이라이트 등).
    /// 정적 셰도우는 PXShadow.uss의 9-slice 방식이 더 효율적.
    ///
    /// USS 커스텀 프로퍼티: --shadow-size, --shadow-color, --shadow-blur, --shadow-radius
    /// </summary>
    [UxmlElement]
    public partial class TKBoxShadow : VisualElement
    {
        const string USS_CLASS = "tk-box-shadow";

        [UxmlAttribute]
        public float ShadowSize
        {
            get => _shadowSize;
            set { _shadowSize = value; MarkDirtyRepaint(); }
        }

        [UxmlAttribute]
        public Color ShadowColor
        {
            get => _shadowColor;
            set { _shadowColor = value; MarkDirtyRepaint(); }
        }

        [UxmlAttribute]
        public float ShadowBlur
        {
            get => _shadowBlur;
            set { _shadowBlur = Mathf.Max(1f, value); MarkDirtyRepaint(); }
        }

        [UxmlAttribute]
        public float ShadowRadius
        {
            get => _shadowRadius;
            set { _shadowRadius = Mathf.Max(0f, value); MarkDirtyRepaint(); }
        }

        float _shadowSize = 12f;
        Color _shadowColor = new Color(0f, 0f, 0f, 0.5f);
        float _shadowBlur = 8f;
        float _shadowRadius = 8f;

        public TKBoxShadow()
        {
            AddToClassList(USS_CLASS);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;

            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            bool dirty = false;
            if (customStyle.TryGetValue(new CustomStyleProperty<float>("--shadow-size"), out var s))  { _shadowSize = s; dirty = true; }
            if (customStyle.TryGetValue(new CustomStyleProperty<Color>("--shadow-color"), out var c)) { _shadowColor = c; dirty = true; }
            if (customStyle.TryGetValue(new CustomStyleProperty<float>("--shadow-blur"), out var b))  { _shadowBlur = Mathf.Max(1f, b); dirty = true; }
            if (customStyle.TryGetValue(new CustomStyleProperty<float>("--shadow-radius"), out var r)){ _shadowRadius = Mathf.Max(0f, r); dirty = true; }
            if (dirty)
                MarkDirtyRepaint();
        }

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 1 || rect.height < 1)
                return;

            // 셰도우 영역 = 콘텐트 + 셰도우 크기
            int rings = Mathf.Max(2, Mathf.CeilToInt(_shadowBlur));
            float step = _shadowSize / rings;

            for (int i = 0; i < rings; i++)
            {
                float t = (float)(i + 1) / rings;
                float expand = step * (i + 1);

                var ringColor = _shadowColor;
                // 가우시안 감쇠
                ringColor.a *= Mathf.Exp(-3f * t * t);

                var ringRect = new Rect(
                    rect.x - expand,
                    rect.y - expand,
                    rect.width + expand * 2,
                    rect.height + expand * 2);

                float radius = _shadowRadius + expand * 0.5f;
                DrawRoundedRect(mgc, ringRect, ringColor, radius);
            }
        }

        static void DrawRoundedRect(MeshGenerationContext mgc, Rect rect, Color color, float radius)
        {
            var painter = mgc.painter2D;
            painter.fillColor = color;
            painter.BeginPath();

            float r = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);

            painter.MoveTo(new Vector2(rect.xMin + r, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax - r, rect.yMin));
            painter.ArcTo(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMin + r), r);
            painter.LineTo(new Vector2(rect.xMax, rect.yMax - r));
            painter.ArcTo(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax - r, rect.yMax), r);
            painter.LineTo(new Vector2(rect.xMin + r, rect.yMax));
            painter.ArcTo(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMax - r), r);
            painter.LineTo(new Vector2(rect.xMin, rect.yMin + r));
            painter.ArcTo(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin + r, rect.yMin), r);

            painter.ClosePath();
            painter.Fill();
        }
    }
}
