using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// UI Toolkit Button에 PXButton과 동일한 '홀드 → 연속 입력 누적 → 릴리즈 시 1회 후처리' 동작을 부여한다.
    /// GameClientPlayConfig.ui의 pressStartDelay / pressRepeatDelay / pressStage3StartDelay / pressStage3RepeatDelay를
    /// 공유하여 UGUI PXButton.PressUpdate와 동일한 타이밍으로 틱을 발사한다.
    ///
    /// 사용 예:
    ///   var hold = new TKHoldButton(btnBuy, OnTick, OnFinish);
    ///   // onTick: 매 틱마다 호출 (즉시 1회 + 홀드 유지 시 startDelay 뒤 반복)
    ///   // onFinish: 포인터가 떨어지거나 버튼 밖으로 나갈 때 1회 호출
    ///   hold.Dispose(); // 리스트 재구성 시 반드시 해제
    /// </summary>
    public class TKHoldButton
    {
        readonly Button _btn;
        readonly Action _onPressTick;
        readonly Action _onPressFinish;

        readonly EventCallback<PointerDownEvent> _downCb;
        readonly EventCallback<PointerUpEvent> _upCb;
        readonly EventCallback<PointerCaptureOutEvent> _captureOutCb;

        bool _pressing;
        bool _firstRepeatFired;
        float _startRealTime;
        float _lastTickRealTime;
        int _pressCount;
        int _capturedPointerId = -1;
        IVisualElementScheduledItem _ticker;

        /// <summary>
        /// 현재 홀드 세션에서 누적된 press 횟수 (tick 1회 = +1 기본, 외부에서 AddPressCount로 보정 가능).
        /// onPressFinish 호출 시점에는 리셋 전이므로 여기서 읽으면 된다.
        /// </summary>
        public int PressCount => _pressCount;

        public bool IsPressing => _pressing;

        public TKHoldButton(Button btn, Action onPressTick, Action onPressFinish)
        {
            _btn = btn;
            _onPressTick = onPressTick;
            _onPressFinish = onPressFinish;

            _downCb = OnPointerDown;
            _upCb = OnPointerUp;
            _captureOutCb = OnPointerCaptureOut;

            _btn.RegisterCallback(_downCb, TrickleDown.TrickleDown);
            _btn.RegisterCallback(_upCb, TrickleDown.TrickleDown);
            _btn.RegisterCallback(_captureOutCb);
        }

        public void Dispose()
        {
            CancelTicker();
            if (_btn == null) return;

            if (_capturedPointerId >= 0 && _btn.HasPointerCapture(_capturedPointerId))
                _btn.ReleasePointer(_capturedPointerId);
            _capturedPointerId = -1;

            _btn.UnregisterCallback(_downCb, TrickleDown.TrickleDown);
            _btn.UnregisterCallback(_upCb, TrickleDown.TrickleDown);
            _btn.UnregisterCallback(_captureOutCb);
        }

        /// <summary>
        /// onPressTick 내부에서 '한 틱이 N 레벨업을 수행함' 같은 보정이 필요할 때 호출.
        /// 기본 틱은 자동으로 +1을 누적하므로, 멀티플라이어 등의 이유로 다른 값이 필요하면
        /// 자동 누적(+1)과 합쳐지지 않도록 tick 내부에서 ResetTickCount 후 AddPressCount를 호출한다.
        /// </summary>
        public void AddPressCount(int delta)
        {
            _pressCount += delta;
        }

        public void ResetTickCount()
        {
            _pressCount = 0;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (_btn.enabledSelf == false)
                return;

            _pressing = true;
            _firstRepeatFired = false;
            _startRealTime = Time.realtimeSinceStartup;
            _lastTickRealTime = _startRealTime;
            _pressCount = 0;

            // UGUI PXButton은 OnPointerDown 시 SFX를 울리지 않고, OnPointerClick(=release)에서 울린다.
            // 홀드 케이스에서도 Finish() 시 1회만 울리도록 Finish에서 SFX 재생.

            FireTick();

            _capturedPointerId = evt.pointerId;
            _btn.CapturePointer(evt.pointerId);

            CancelTicker();
            _ticker = _btn.schedule.Execute(OnTick).Every(16);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (_capturedPointerId >= 0 && _btn.HasPointerCapture(_capturedPointerId))
                _btn.ReleasePointer(_capturedPointerId);
            _capturedPointerId = -1;
            Finish();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _capturedPointerId = -1;
            Finish();
        }

        void OnTick()
        {
            if (_pressing == false)
            {
                CancelTicker();
                return;
            }

            if (_btn.enabledSelf == false)
            {
                Finish();
                return;
            }

            var uiCfg = GameClientPlayConfig.Instance.ui;
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _startRealTime;
            float startDelay = uiCfg.pressStartDelay;
            float s2Interval = uiCfg.pressRepeatDelay;
            float s3StartDelay = uiCfg.pressStage3StartDelay;
            float s3Interval = uiCfg.pressStage3RepeatDelay;

            if (elapsed < startDelay)
                return;

            // startDelay 경과 후 첫 반복 틱
            if (_firstRepeatFired == false)
            {
                _firstRepeatFired = true;
                _lastTickRealTime = now;
                FireTick();
                return;
            }

            // 경과 시간에 따라 2단계/3단계 간격 결정
            float interval = (elapsed >= s3StartDelay) ? s3Interval : s2Interval;
            if (interval <= 0f) interval = 0.1f;

            float sinceLast = now - _lastTickRealTime;
            int ticks = Mathf.FloorToInt(sinceLast / interval);
            if (ticks > 0)
            {
                for (int i = 0; i < ticks; i++)
                    FireTick();
                _lastTickRealTime += ticks * interval;
            }
        }

        void FireTick()
        {
            _pressCount++;
            try { _onPressTick?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Finish()
        {
            if (_pressing == false)
                return;

            _pressing = false;
            CancelTicker();

            GameSoundManager.Instance.PlaySFX_UI_Global("sfx_button_click_1");

            try { _onPressFinish?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }

            _pressCount = 0;
        }

        void CancelTicker()
        {
            _ticker?.Pause();
            _ticker = null;
        }
    }
}
