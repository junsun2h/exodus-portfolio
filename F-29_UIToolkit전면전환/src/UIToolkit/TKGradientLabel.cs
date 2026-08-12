using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// 텍스트 그라데이션 레이블 (타이틀 전용).
    /// Label N개를 스택하고 각 레이어를 위에서부터 점점 얇게 클립하여
    /// 세로 방향 N-스텝 색상 블렌딩을 만든다. N이 커질수록 스텝이 부드럽게 보인다.
    /// </summary>
    [UxmlElement]
    public partial class TKGradientLabel : VisualElement
    {
        const string USS_CLASS = "tk-gradient-label";
        const string USS_LABEL = "tk-gradient-label__text";

        [UxmlAttribute]
        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                foreach (var l in _layers) l.text = value;
            }
        }

        [UxmlAttribute]
        public Color ColorTop
        {
            get => _colorTop;
            set { _colorTop = value; ApplyColors(); }
        }

        [UxmlAttribute]
        public Color ColorBottom
        {
            get => _colorBottom;
            set { _colorBottom = value; ApplyColors(); }
        }

        [UxmlAttribute]
        public int Steps
        {
            get => _steps;
            set
            {
                var clamped = Mathf.Clamp(value, 2, 32);
                if (_steps == clamped) return;
                _steps = clamped;
                RebuildLayers();
            }
        }

        string _text = "";
        Color _colorTop = new Color(1f, 0.92f, 0.6f, 1f);
        Color _colorBottom = new Color(0.92f, 0.65f, 0.2f, 1f);
        int _steps = 8;

        readonly List<Label> _layers = new();

        public TKGradientLabel()
        {
            AddToClassList(USS_CLASS);
            RebuildLayers();
        }

        void RebuildLayers()
        {
            foreach (var l in _layers) Remove(l);
            _layers.Clear();

            int n = Mathf.Max(2, _steps);
            for (int i = 0; i < n; i++)
            {
                var label = new Label();
                label.AddToClassList(USS_LABEL);
                label.text = _text;
                label.pickingMode = PickingMode.Ignore;
                // 덮는 레이어가 top 기준으로 클립되므로 텍스트 수직 정렬도 Upper 여야
                // 외부 USS의 '-unity-text-align: middle-*' 상속으로 텍스트가 박스 세로
                // 중앙에 정렬되면, 얇게 클립된 박스에서 텍스트 중앙 일부만 보여 그라데이션이 깨진다.
                // 모든 레이어에 UpperCenter 를 강제하여 외부 상속을 차단.
                label.style.unityTextAlign = TextAnchor.UpperCenter;

                float t = (float)i / (n - 1);
                label.style.color = Color.Lerp(_colorBottom, _colorTop, t);

                if (i == 0)
                {
                    // 기준 레이어: 레이아웃 높이 결정
                    label.style.position = Position.Relative;
                }
                else
                {
                    // 덮는 레이어: 위에서부터 점점 얇게 클립
                    label.style.position = Position.Absolute;
                    label.style.left = 0;
                    label.style.top = 0;
                    label.style.right = 0;
                    float heightPct = (float)(n - i) / n * 100f;
                    label.style.height = new StyleLength(new Length(heightPct, LengthUnit.Percent));
                    label.style.overflow = Overflow.Hidden;
                }

                Add(label);
                _layers.Add(label);
            }
        }

        void ApplyColors()
        {
            int n = _layers.Count;
            if (n < 2) return;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1);
                _layers[i].style.color = Color.Lerp(_colorBottom, _colorTop, t);
            }
        }
    }
}
