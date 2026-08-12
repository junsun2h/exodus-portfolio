using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PX
{
    public class PXTabGroup : PXMonoBehaviour, IPXUITemplate
    {
        #region Events
        [Serializable]
        public class PXTabGroupEvent : UnityEvent<int, string> { }
        [SerializeField]
        private PXTabGroupEvent m_OnTabGroupClick = new PXTabGroupEvent();

        public PXTabGroupEvent onTabGroupClickEvent
        {
            get { return m_OnTabGroupClick; }
            set { m_OnTabGroupClick = value; }
        }
        #endregion

        public string SourceTabParam { get; set; }
        public int CurrentIndex { get; private set; }
        public int TabCount { get { return _tabButtons.Count; } }

        private List<PXTabButton> _tabButtons = new List<PXTabButton>();

        protected override void Awake()
        {
            base.Awake();

            PXTabButton[] buttons = GetComponentsInChildren<PXTabButton>(true);
            _tabButtons.AddRange(buttons);

            foreach (var tab in _tabButtons)
            {
                var captured = tab;
                tab.onTabClickEvent.AddListener((bool inOn, string inParam) =>
                {
                    OnTabClicked(captured);
                });

                tab.SetSelected(false, false);
            }
        }

        protected override void Start()
        {
            base.Start();

            foreach (var tab in _tabButtons)
            {
                if (tab.PXText != null)
                    tab.PXText.gameObject.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            foreach (var tab in _tabButtons)
            {
                tab.onTabClickEvent.RemoveAllListeners();
            }
        }

        void OnTabClicked(PXTabButton inTab)
        {
            int index = _tabButtons.IndexOf(inTab);
            SetTabIndex(index);
        }

        public PXTabButton GetTabButton(int inIndex)
        {
            if (inIndex < 0 || inIndex >= _tabButtons.Count)
            {
                Debug.LogError($"PXTabGroup: Invalid tab index = {inIndex}, Total = {_tabButtons.Count}");
                return null;
            }

            return _tabButtons[inIndex];
        }

        public void SetTabIndex(int inIndex, bool inForce = false, bool inEvent = true)
        {
            if (inIndex < 0 || inIndex >= _tabButtons.Count)
            {
                Debug.LogError($"PXTabGroup: Invalid tab index = {inIndex}, Total = {_tabButtons.Count}");
                return;
            }

            if (!inForce && CurrentIndex == inIndex)
            {
                _tabButtons[CurrentIndex].SetSelected(true, false);
                return;
            }

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                _tabButtons[i].SetSelected(i == inIndex, false);
            }

            CurrentIndex = inIndex;

            if (inEvent)
            {
                onTabGroupClickEvent.Invoke(inIndex, SourceTabParam);
            }
        }
    }
}
