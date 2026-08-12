using System;
using System.Numerics;
using UnityEngine;
using UnityEngine.UIElements;

namespace PX
{
    /// <summary>
    /// UI Toolkit VisualElement 확장 메서드.
    /// 기존 PXUI 컴포넌트의 기능을 UI Toolkit에서 사용할 수 있도록 래핑한다.
    /// </summary>
    public static class UIToolkitExtensions
    {
        /// <summary>
        /// 클릭 시 SFX를 자동 재생하는 버튼 이벤트 등록.
        /// 기본적으로 짧은 재진입 가드(guardSeconds, 기본 0.3초)를 두어 같은 버튼 연타로 인한
        /// 팝업 중첩/콜백 중복 호출을 자동 차단한다. 시각 변화는 없음.
        /// 응답이 0.3초보다 길 수 있는 서버 API 호출 버튼은 RegisterApiClick 사용 (응답까지 비활성화).
        /// 가드를 끄려면 guardSeconds: 0 명시.
        /// </summary>
        public static void RegisterClickWithSFX(this Button button, Action onClick, string sfxName = "sfx_button_click_1", float guardSeconds = 0.3f)
        {
            if (button == null) return;

            bool guarded = false;

            // 클릭 직후 화면이 전환되면(패널 detach) 해제 스케줄이 돌지 않아 가드가 영구히 걸린 채 남는다.
            // 트리가 유지된 상태로 다시 붙는 경우 버튼이 영영 무반응이 되므로, 떨어질 때 강제 해제한다.
            button.RegisterCallback<DetachFromPanelEvent>(_ => guarded = false);

            button.clicked += () =>
            {
                if (guarded) return;

                if (guardSeconds > 0f && button.panel != null)
                {
                    guarded = true;
                    button.schedule.Execute(() => guarded = false).StartingIn((long)(guardSeconds * 1000f));
                }

                GameSoundManager.Instance.PlaySFX_UI_Global(sfxName);
                onClick?.Invoke();
            };
        }

        /// <summary>
        /// API 호출 버튼용 가드 클릭 등록.
        /// 클릭 시 즉시 버튼을 비활성화하고, 콜백으로 전달된 release() 가 호출될 때까지 비활성 상태를 유지한다.
        /// 응답 누락 / 예외 시에도 영구 잠김을 막기 위해 safetyTimeoutSeconds 후 자동 해제.
        ///
        /// 사용:
        ///   button.RegisterApiClick(release =>
        ///   {
        ///       if (target == null) { release(); return; }
        ///       SomeApiManager.Request_X(args, () =>
        ///       {
        ///           // 성공 후처리...
        ///           release();
        ///       });
        ///   });
        ///
        /// 주의: 단순 팝업 열기처럼 API 를 호출하지 않는 버튼은 RegisterClickWithSFX 사용.
        /// 누름→떼는 홀드/연타 버튼(예: TKScreen_Player 강화 버튼)은 TKHoldButton 사용 — 본 헬퍼 적용 금지.
        /// </summary>
        public static void RegisterApiClick(this Button button, Action<Action> onClickWithRelease, string sfxName = "sfx_button_click_1", float safetyTimeoutSeconds = 8f)
        {
            if (button == null) return;

            button.clicked += () =>
            {
                if (button.enabledSelf == false) return; // 이미 응답 대기 중

                button.SetEnabled(false);
                GameSoundManager.Instance.PlaySFX_UI_Global(sfxName);

                bool released = false;
                IVisualElementScheduledItem timeoutItem = null;
                Action release = null;
                release = () =>
                {
                    if (released) return;
                    released = true;
                    if (timeoutItem != null) { timeoutItem.Pause(); timeoutItem = null; }
                    if (button.panel != null) button.SetEnabled(true);
                };

                // 안전 타임아웃: API 콜백 누락(에러/네트워크 실패 등) 시 영구 비활성화 방지.
                if (safetyTimeoutSeconds > 0f && button.panel != null)
                {
                    timeoutItem = button.schedule.Execute(() => release()).StartingIn((long)(safetyTimeoutSeconds * 1000f));
                }

                try
                {
                    onClickWithRelease?.Invoke(release);
                }
                catch
                {
                    release();
                    throw;
                }
            };
        }

        /// <summary>
        /// 로컬라이제이션 키를 적용하여 텍스트 설정
        /// </summary>
        public static void SetTextKey(this Label label, EStringKey key)
        {
            label.text = GameUtility.GetLocalizedText(key);
        }

        /// <summary>
        /// 로컬라이제이션 키를 적용하여 텍스트 설정 (string 키)
        /// </summary>
        public static void SetTextKey(this Label label, string key)
        {
            label.text = GameUtility.GetLocalizedText(key);
        }

        /// <summary>
        /// SpriteAtlas에서 스프라이트를 로드하여 배경 이미지로 설정
        /// </summary>
        public static void SetSprite(this VisualElement element, string bundleName, string atlasName, string spriteName)
        {
            var atlas = GameAssetBundleManager.Instance.LoadFromFile(bundleName, atlasName) as UnityEngine.U2D.SpriteAtlas;
            if (atlas != null)
            {
                var sprite = atlas.GetSprite(spriteName);
                if (sprite != null)
                {
                    element.style.backgroundImage = new StyleBackground(sprite);
                }
            }
        }

        /// <summary>
        /// Sprite를 직접 배경 이미지로 설정
        /// </summary>
        public static void SetSprite(this VisualElement element, Sprite sprite)
        {
            if (sprite != null)
            {
                element.style.backgroundImage = new StyleBackground(sprite);
            }
        }

        /// <summary>
        /// BigInteger 값을 알파벳 축약 형식으로 표시
        /// </summary>
        public static void SetCountText(this Label label, BigInteger value)
        {
            label.text = GameUtility.ConvertToAlphabetValue(value);
        }

        /// <summary>
        /// VisualElement 표시/숨김
        /// </summary>
        public static void SetVisible(this VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// USS 애니메이션 클래스 토글.
        /// animName: "anim-pulse", "anim-slide-up", "anim-fade", "anim-highlight", "anim-bounce"
        /// active: true → "--active" 클래스 추가 (또는 "--hidden" 제거), false → 반대
        /// </summary>
        public static void PlayAnimation(this VisualElement element, string animName, bool active = true)
        {
            // base 클래스 보장
            if (!element.ClassListContains(animName))
                element.AddToClassList(animName);

            // hidden 계열 (slide-up, fade) vs active 계열 (pulse, highlight, bounce) 구분
            var hiddenClass = animName + "--hidden";
            var activeClass = animName + "--active";

            bool isHiddenType = animName == "anim-slide-up" || animName == "anim-fade";

            if (isHiddenType)
            {
                if (active)
                    element.RemoveFromClassList(hiddenClass);
                else
                    element.AddToClassList(hiddenClass);
            }
            else
            {
                if (active)
                    element.AddToClassList(activeClass);
                else
                    element.RemoveFromClassList(activeClass);
            }
        }
    }
}
