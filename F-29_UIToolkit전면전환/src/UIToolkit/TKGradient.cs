using UnityEngine;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// 프로그래매틱 2색 그라데이션 VisualElement.
    /// generateVisualContent로 4-vertex 메시를 직접 생성하여 텍스처 없이 그라데이션을 렌더링한다.
    /// USS 커스텀 프로퍼티로도 색상 지정 가능: --gradient-color-top, --gradient-color-bottom
    /// </summary>
    [UxmlElement]
    public partial class TKGradient : VisualElement
    {
        const string USS_CLASS = "tk-gradient";

        [UxmlAttribute]
        public Color ColorTop
        {
            get => _colorTop;
            set { _colorTop = value; MarkDirtyRepaint(); }
        }

        [UxmlAttribute]
        public Color ColorBottom
        {
            get => _colorBottom;
            set { _colorBottom = value; MarkDirtyRepaint(); }
        }

        [UxmlAttribute]
        public bool Horizontal
        {
            get => _horizontal;
            set { _horizontal = value; MarkDirtyRepaint(); }
        }

        Color _colorTop = new Color(0.31f, 0.78f, 1f, 0.3f);    // 시안 반투명
        Color _colorBottom = new Color(0.04f, 0.07f, 0.15f, 0f); // 투명
        bool _horizontal;

        public TKGradient()
        {
            AddToClassList(USS_CLASS);
            generateVisualContent += OnGenerateVisualContent;
        }

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 1 || rect.height < 1)
                return;

            // USS 커스텀 프로퍼티 우선 적용
            var cssTop = customStyle.TryGetValue(new CustomStyleProperty<Color>("--gradient-color-top"), out var ct) ? ct : _colorTop;
            var cssBottom = customStyle.TryGetValue(new CustomStyleProperty<Color>("--gradient-color-bottom"), out var cb) ? cb : _colorBottom;

            var painter = mgc.painter2D;
            painter.fillColor = cssTop;

            // 4-vertex quad: top-left, top-right, bottom-right, bottom-left
            if (_horizontal)
            {
                // 좌→우 그라데이션: left=colorTop, right=colorBottom
                DrawGradientQuad(mgc, rect, cssTop, cssBottom, cssBottom, cssTop);
            }
            else
            {
                // 상→하 그라데이션: top=colorTop, bottom=colorBottom
                DrawGradientQuad(mgc, rect, cssTop, cssTop, cssBottom, cssBottom);
            }
        }

        /// <summary>
        /// 4개의 vertex 색상을 직접 지정하여 그라데이션 quad 생성
        /// </summary>
        static void DrawGradientQuad(MeshGenerationContext mgc, Rect rect,
            Color topLeft, Color topRight, Color bottomRight, Color bottomLeft)
        {
            var mesh = mgc.Allocate(4, 6);
            if (mesh.vertexCount == 0)
                return;

            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ),
                tint = topLeft
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ),
                tint = topRight
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ),
                tint = bottomRight
            });
            mesh.SetNextVertex(new Vertex
            {
                position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ),
                tint = bottomLeft
            });

            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
        }
    }
}
