using UnityEngine;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// UI Toolkit 커스텀 프로그레스 바.
    /// USS transition을 활용한 애니메이션을 지원한다.
    /// </summary>
    [UxmlElement]
    public partial class TKProgress : VisualElement
    {
        const string USS_CLASS = "tk-progress";
        const string USS_FILL_CLASS = "tk-progress__fill";
        const string USS_LABEL_CLASS = "tk-progress__label";

        VisualElement _fill;
        Label _label;

        float _value;
        float _minValue;
        float _maxValue = 1f;

        [UxmlAttribute]
        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp(value, _minValue, _maxValue);
                UpdateFill();
            }
        }

        [UxmlAttribute]
        public float MinValue
        {
            get => _minValue;
            set
            {
                _minValue = value;
                _value = Mathf.Clamp(_value, _minValue, _maxValue);
                UpdateFill();
            }
        }

        [UxmlAttribute]
        public float MaxValue
        {
            get => _maxValue;
            set
            {
                _maxValue = value;
                _value = Mathf.Clamp(_value, _minValue, _maxValue);
                UpdateFill();
            }
        }

        [UxmlAttribute]
        public string LabelText
        {
            get => _label?.text ?? string.Empty;
            set
            {
                if (_label != null)
                    _label.text = value;
            }
        }

        /// <summary>
        /// 0~1 범위의 정규화된 값
        /// </summary>
        public float NormalizedValue
        {
            get
            {
                float range = _maxValue - _minValue;
                return range > 0f ? (_value - _minValue) / range : 0f;
            }
            set
            {
                Value = Mathf.Lerp(_minValue, _maxValue, Mathf.Clamp01(value));
            }
        }

        public TKProgress()
        {
            AddToClassList(USS_CLASS);

            _fill = new VisualElement();
            _fill.AddToClassList(USS_FILL_CLASS);
            Add(_fill);

            _label = new Label();
            _label.AddToClassList(USS_LABEL_CLASS);
            Add(_label);

            UpdateFill();
        }

        void UpdateFill()
        {
            if (_fill == null)
                return;

            float percent = NormalizedValue * 100f;
            _fill.style.width = new Length(percent, LengthUnit.Percent);
        }
    }
}
