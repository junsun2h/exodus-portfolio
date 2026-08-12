using UnityEngine;
using UnityEngine.UI;

namespace PX
{
    public enum EGradientDirection
    {
        Vertical,
        Horizontal,
    }

    [AddComponentMenu("PX/PXGradient")]
    public class PXGradient : BaseMeshEffect
    {
        [SerializeField]
        private Color _colorTop = Color.white;
        [SerializeField]
        private Color _colorBottom = Color.black;
        [SerializeField]
        private EGradientDirection _direction = EGradientDirection.Vertical;

        public Color ColorTop
        {
            get => _colorTop;
            set { _colorTop = value; graphic.SetVerticesDirty(); }
        }

        public Color ColorBottom
        {
            get => _colorBottom;
            set { _colorBottom = value; graphic.SetVerticesDirty(); }
        }

        public EGradientDirection Direction
        {
            get => _direction;
            set { _direction = value; graphic.SetVerticesDirty(); }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive())
                return;

            UIVertex vertex = default;
            int vertCount = vh.currentVertCount;

            if (vertCount == 0)
                return;

            float minPos = float.MaxValue;
            float maxPos = float.MinValue;

            for (int i = 0; i < vertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                float pos = _direction == EGradientDirection.Vertical ? vertex.position.y : vertex.position.x;
                minPos = Mathf.Min(minPos, pos);
                maxPos = Mathf.Max(maxPos, pos);
            }

            float range = maxPos - minPos;
            if (range <= 0f)
                return;

            for (int i = 0; i < vertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                float pos = _direction == EGradientDirection.Vertical ? vertex.position.y : vertex.position.x;
                float t = (pos - minPos) / range;
                vertex.color *= Color.Lerp(_colorBottom, _colorTop, t);
                vh.SetUIVertex(vertex, i);
            }
        }
    }
}
