using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace PX
{
    public class PXDropdown : PXMonoBehaviour, IPXUITemplate
    {
        #region Bind Widgets
        [SerializeField]
        private Dropdown _dropdown;

        #region Events
        [Serializable]
        public class PXDropdownEvent : UnityEvent<int, string> { }
        [FormerlySerializedAs("OnPXDropdownEvent")]
        [SerializeField]
        private PXDropdownEvent m_OnDropdown = new PXDropdownEvent();

        public PXDropdownEvent onDowndownEvent
        {
            get { return m_OnDropdown; }
            set { m_OnDropdown = value; }
        }
        #endregion
        public string sourceDropdownParam { get; set; }
        public RectTransform template { get { return _dropdown.template; } set { _dropdown.template = value; } }
        public Text captionText { get { return _dropdown.captionText; } set { _dropdown.captionText = value; } }
        public int value { get { return _dropdown.value; } set { _dropdown.value = value; } }
        public float alphaFadeSpeed { get { return _dropdown.alphaFadeSpeed; } set { _dropdown.alphaFadeSpeed = value; } }
        public Image captionImage { get { return _dropdown.captionImage; } set { _dropdown.captionImage = value; } }
        public Text itemText { get { return _dropdown.itemText; } set { _dropdown.itemText = value; } }
        public Image itemImage { get { return _dropdown.itemImage; } set { _dropdown.itemImage = value; } }
        public List<UnityEngine.UI.Dropdown.OptionData> options { get { return _dropdown.options; } set { _dropdown.options = value; } }

        #endregion
        // Start is called before the first frame update
        void Awake()
        {
            _dropdown.onValueChanged.AddListener((int inIndex) =>
            {
                onDowndownEvent.Invoke(inIndex, sourceDropdownParam);
            });
        }
        void OnDestroy()
        {
            _dropdown.onValueChanged.RemoveAllListeners();
            onDowndownEvent.RemoveAllListeners();
        }
    }

}