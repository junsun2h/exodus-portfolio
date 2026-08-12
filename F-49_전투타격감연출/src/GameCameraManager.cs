using System;
using System.Collections;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace PX
{
    public class GameCameraManager : SingletonDependency<GameCameraManager>
    {
        Transform audioListenerTrans;
        CameraController cameraController;
        public Camera currentViewCamera { get; private set; }
        public CameraComponent currentCameraComponent { get; private set; }

        public EBattleMode eBattleMode { get; private set; }

        Quaternion prevRotate;
        float prevSize;

        private GameCameraManager()
        {
        }

        ~GameCameraManager()
        {
        }

        public override void Awake()
        {
            base.Awake();

            cameraController = GameObject.FindObjectOfType<CameraController>();
            cameraController?.loginCamera?.gameObject.SetActive(false);
            cameraController?.lobbyCamera?.gameObject.SetActive(false);
            //cameraController?.StageCamera?.gameObject.SetActive(false);

            audioListenerTrans = cameraController?.AudioListenerTrans;
        }

        public void SetFollowCameraCharacter(UCharacterActor InCharacter)
        {
            currentCameraComponent?.SetFollowCameraCharacter(InCharacter);

            // GameMonoCoroutineManager.Instance.ClearCoroutine("ToCameraValue");
            // GameMonoCoroutineManager.Instance.StartCoroutine_IEnumerator("ToCameraValue", ToCameraValue(prevRotate, prevSize));
        }
        public void SetFollowBossCameraCharacter(UCharacterActor InBossActor, Action InOnComplete)
        {
            prevRotate = currentViewCamera.transform.rotation;
            prevSize = currentViewCamera.orthographicSize;

            GameMonoCoroutineManager.Instance.ClearCoroutine("ToBossCameraView"); GameMonoCoroutineManager.Instance.StartCoroutine_IEnumerator("ToBossCameraView", ToBossCameraView(InBossActor, InOnComplete));
        }

        IEnumerator ToBossCameraView(UCharacterActor InBossActor, Action InOnComplete)
        {
            //이 코루틴은 timeScale 을 0.1 까지 낮췄다가 되돌린다. 중간에 끊기면 저속이 그대로 남아
            //전투 전체가 1/10 속도로 고정되므로, 복원은 finally 에 둬서 정상 종료와 중단 양쪽에서 보장한다.
            //(try-finally 안의 yield return 은 합법. try-catch 안에서만 금지된다)
            try
            {
                currentCameraComponent?.SetFollowBossCameraCharacter(InBossActor); InBossActor.GetTransform.LookAt(GameCharacterManager.Instance.GetPlayerCharacter().GetTransform.position);

                yield return null;

                var bossFaceRotate = InBossActor.transform.rotation;
                Quaternion toCameraRotate = Quaternion.Euler(10, bossFaceRotate.eulerAngles.y + 180, 0);
                float toCameraSize = 5;
                Quaternion startRotate = currentViewCamera.transform.rotation;
                float startSize = currentViewCamera.orthographicSize;

                float time = 0;
                float duration = 1.0f;
                float minTimeScale = 0.1f;
                float ratorSpeed = 3.0f;

                // 첫 번째 while: EaseOut (빠르게 시작, 느리게 끝남)
                while (time < duration)
                {
                    time += Time.deltaTime * ratorSpeed;
                    if (time > duration)
                    {
                        time = duration;
                    }
                    float normalizedTime = time / duration;
                    float easedTime = 1f - Mathf.Pow(1f - normalizedTime, 4f); // EaseOut Cubic
                    GameBattleControlManager.Instance.SetGameTimeScale(Mathf.Max(easedTime, minTimeScale));
                    currentViewCamera.transform.rotation = Quaternion.Lerp(startRotate, toCameraRotate, easedTime);
                    currentViewCamera.orthographicSize = Mathf.Lerp(startSize, toCameraSize, easedTime);
                    yield return null;
                }

                yield return new WaitForSeconds(0.2f);

                time = 0;
                // 두 번째 while: EaseIn (느리게 시작, 빠르게 끝남)
                while (time < duration)
                {
                    time += Time.deltaTime * ratorSpeed;
                    if (time > duration)
                    {
                        time = duration;
                    }
                    float normalizedTime = time / duration;
                    float easedTime = Mathf.Pow(normalizedTime, 4f); // EaseIn Cubic
                    GameBattleControlManager.Instance.SetGameTimeScale(Mathf.Max(easedTime, minTimeScale));
                    currentViewCamera.transform.rotation = Quaternion.Lerp(toCameraRotate, prevRotate, easedTime);
                    currentViewCamera.orthographicSize = Mathf.Lerp(toCameraSize, prevSize, easedTime);
                    yield return null;
                }

                SetFollowCameraCharacter(GameCharacterManager.Instance.GetPlayerCharacter());


                InOnComplete?.Invoke();
            }
            finally
            {
                GameBattleControlManager.Instance.SetGameTimeScale(1.0f);
            }
        }

        public void SetModeViewTarget(EBattleMode InMode)
        {
            if (eBattleMode == InMode)
                return;

            eBattleMode = InMode;

            Camera targetModeCamera = null;
            switch (InMode)
            {
                case EBattleMode.login:
                    {
                        targetModeCamera = cameraController.loginCamera;
                    }
                    break;
                default:
                    {
                        targetModeCamera = cameraController.lobbyCamera;
                    }
                    break;
            }

            if (currentViewCamera != null && currentViewCamera != targetModeCamera)
            {
                currentViewCamera.tag = "Untagged";
                currentViewCamera.gameObject.SetActive(false);
                currentViewCamera = null;
            }

            if (targetModeCamera != null)
            {
                currentViewCamera = targetModeCamera;
                currentViewCamera.tag = "MainCamera";
                currentViewCamera.gameObject.SetActive(true);
                currentCameraComponent = currentViewCamera.GetComponent<CameraComponent>();


                AttachMainCameraAudioListener();
            }
        }

        public void AttachMainCameraAudioListener()
        {
            if (currentViewCamera == null)
                return;

            //AudioListener 갱신
            AttachAudioListener(currentViewCamera.transform);
        }

        public void AttachAudioListener(Transform InParent)
        {
            if (audioListenerTrans == null || InParent == null)
                return;

            audioListenerTrans.SetParent(InParent);
            audioListenerTrans.localPosition = Vector3.zero;
            audioListenerTrans.localScale = Vector3.one;
            audioListenerTrans.rotation = Quaternion.Euler(Vector3.zero);
        }

        /// <summary>
        /// 카메라 흔들림을 누적한다 (0~1). 여러 사건이 겹치면 합쳐져 더 크게 흔들린다.
        /// 실제 계산은 CameraComponent 가 자기 위치를 갱신할 때 함께 처리한다.
        ///
        /// ⚠️ 일반 히트처럼 초당 수십 번 일어나는 사건에는 절대 붙이지 않는다.
        /// 화면 전체가 움직이는 연출이라 빈도가 곧 멀미가 된다 —
        /// 개체 단위 반응이 필요하면 히트 플래시·히트 셰이크를 쓴다
        /// </summary>
        public void AddCameraShake(float InTrauma)
        {
            currentCameraComponent?.AddShakeTrauma(InTrauma);
        }

        /// <summary>
        /// 스킬에 지정된 세기로 카메라를 흔든다.
        /// 어떤 스킬이 흔들지는 GameClientPlayConfig 의 "흔들 스킬" 목록이 정하며,
        /// 목록에 없는 스킬은 여기까지 와도 아무 일도 일어나지 않는다.
        ///
        /// 발동 지점에서는 스킬 종류만 넘기면 되므로, 연출을 조정할 때 코드를 고칠 필요가 없다
        /// </summary>
        public void AddCameraShakeBySkill(ESkill InSkill)
        {
            if (InSkill == ESkill.None)
                return;

            float trauma = GameClientPlayConfig.Instance.combat.GetCameraShakeTrauma(InSkill);

            if (trauma <= 0f)
                return;

            currentCameraComponent?.AddShakeTrauma(trauma);
        }

        //구 SetCameraShake / SetCameraShakeRator / ShakeInner / ShakeInit 은 제거했다.
        //cameraController.shakeTrans 를 흔드는 방식이었는데, CameraComponent.Update 가
        //카메라의 월드 위치를 매 프레임 직접 덮어쓰기 때문에 부모를 흔들어도 그대로 상쇄됐다.
        //호출부 4곳이 전부 주석 처리되어 있던 것도 그 때문으로 보인다.
        //그 밖에 ShakeInner 가 원위치가 아닌 직전 위치에 오프셋을 누적해 카메라가 랜덤 워크로 밀려나고,
        //간격 누적값을 리셋하지 않아 shakeInterval 인자가 무시되며,
        //ShakeInit 이 localPosition 이 아니라 position 을 0 으로 만들어 리그를 월드 원점으로 보내는
        //문제가 함께 있었다. 새 경로(AddCameraShake)는 이 셋 모두 구조적으로 발생하지 않는다

        public void SetUIFullScreenStatus(EUIFullScreenType InFullScreen)
        {
            if (InFullScreen == EUIFullScreenType.None)
                return;

            switch (InFullScreen)
            {
                case EUIFullScreenType.UIFull:
                    {
                        OnlyLayerMask(LayerMask.NameToLayer("UI"));
                    }
                    break;
                case EUIFullScreenType.UINotFull:
                    {
                        //Everything();
                        currentViewCamera.cullingMask = ~-1;

                        currentViewCamera.cullingMask |= 1 << LayerMask.NameToLayer("UI");
                        currentViewCamera.cullingMask |= 1 << LayerMask.NameToLayer("Default");
                        currentViewCamera.cullingMask |= 1 << LayerMask.NameToLayer("Projectile");
                        currentViewCamera.cullingMask |= 1 << LayerMask.NameToLayer("Actor");
                        currentViewCamera.cullingMask |= 1 << LayerMask.NameToLayer("UIActor");
                        currentViewCamera.cullingMask |= 1 << LayerMask.NameToLayer("Environment");

                        /*
                        if (eBattleMode != EBattleMode.Main)
                        {
                            currentViewCamera.cullingMask |= 1 << LayerMask.NameToLayer("NaviGround");
                        }
                        */
                    }
                    break;
            }
        }

        void OnlyLayerMask(int layerIndex)
        {
            currentViewCamera.cullingMask = 1 << layerIndex;
        }

        void OffLayerMask(int layerIndex)
        {
            currentViewCamera.cullingMask = ~(1 << layerIndex);
        }

        void Everything()
        {
            currentViewCamera.cullingMask = -1;
        }

        void Nothing()
        {
            currentViewCamera.cullingMask = ~-1;
        }
    }


}
