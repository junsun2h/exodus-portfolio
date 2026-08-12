using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace PX
{
    public class PXRadioGroup : PXMonoBehaviour, IPXUITemplate
    {
        #region Events
        [Serializable]
        public class PXRadioGroupClickedEvent : UnityEvent<int, string> { }
        [FormerlySerializedAs("OnPXRadioGroupClickedEvent")]
        [SerializeField]
        private PXRadioGroupClickedEvent m_OnRadioGroupClick = new PXRadioGroupClickedEvent();

        public PXRadioGroupClickedEvent onRadioGroupClickEvent
        {
            get { return m_OnRadioGroupClick; }
            set { m_OnRadioGroupClick = value; }
        }
        #endregion
        public string SourceRadioParam { get; set; }
        public int CurrentIndex { get; private set; }
        public int AllSlotCount { get { return _listCheckBox.Count; } }

        private List<PXCheckBox> _listCheckBox = new List<PXCheckBox>();

        // Start is called before the first frame update
        protected override void Awake()
        {
            base.Awake();

            PXCheckBox[] checkBoxArr = GetComponentsInChildren<PXCheckBox>();
            _listCheckBox.AddRange(checkBoxArr);

            foreach (var entryWidget in _listCheckBox)
            {
                entryWidget.onCheckEvent.AddListener((bool inCheck, string inParam) =>
                {
                    RadioGroupCheckEvent(entryWidget);
                });

                entryWidget.SetCheck(false, false);
            }
        }
        protected override void Start()
        {
            base.Start();

            foreach (var entryWidget in _listCheckBox)
            {
                entryWidget.PXText.gameObject.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            foreach (var entryWidget in _listCheckBox)
            {
                entryWidget.onCheckEvent.RemoveAllListeners();
            }
        }

        void RadioGroupCheckEvent(PXCheckBox inCheckBox)
        {
            int checkIndex = _listCheckBox.IndexOf(inCheckBox);

            SetRatioCheckIndex(checkIndex);
        }

        public PXCheckBox GetRadioCheckBox(int inIndex)
        {
            if (inIndex < 0 || inIndex >= _listCheckBox.Count)
            {
                Debug.LogError($"Error, Invalid Radio Index = {inIndex}, Total = {_listCheckBox.Count}");
                return null;
            }

            return _listCheckBox[inIndex];
        }

        public void SetRatioCheckIndex(int inIndex, bool InForce = false, bool InEvent = true)
        {
            if (inIndex < 0 || inIndex >= _listCheckBox.Count)
            {
                Debug.LogError($"Error, Invalid Radio Index = {inIndex}, Total = {_listCheckBox.Count}");
            }

            if (InForce == false && CurrentIndex == inIndex)
            {
                _listCheckBox[CurrentIndex].SetCheck(true, false);
            }
            else
            {
                for (int i = 0; i < _listCheckBox.Count; i++)
                {
                    if (i == inIndex)
                    {
                        _listCheckBox[i].SetCheck(true, InEvent);
                    }
                    else
                    {
                        _listCheckBox[i].SetCheck(false, false);
                    }
                }
            }

            CurrentIndex = inIndex;

            onRadioGroupClickEvent.Invoke(inIndex, SourceRadioParam);
        }
    }
}

