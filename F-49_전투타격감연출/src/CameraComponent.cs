using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    public class CameraComponent : PXMonoBehaviour
    {
        //ī�޶� ��ġ
        public Vector3 cameraCheckPosition = new Vector3(0, 0, 0);
        //ī�޶� �������� �ӵ�
        public float cameraSmoothSpeed = 0;
        //ī�޶� ĳ���� ���� �Ÿ�
        public float cameraFrontViewDistance = 0;

        private Transform currentViewCamera;

        public UCharacterActor followCameraCharacter { get; private set; }
        public UCharacterActor followBossCameraCharacter { get; private set; }

        public UCharacterActor currentCameraCharacter { get; private set; }

        // ===================================================================
        // 카메라 셰이크 (트라우마 모델)
        //
        // trauma(0~1)에 사건이 가산되고 매 프레임 지수적으로 줄어든다.
        // 코루틴 슬롯 하나를 StopCoroutine 으로 갈아 끼우던 옛 방식은 마지막 사건만 남아
        // "여럿이 동시에 터졌다" 를 표현할 수 없었다.
        //
        // ⚠️ 오프셋을 부모 Transform 에 주면 안 된다.
        // 아래 Update 가 currentViewCamera.position 을 월드 좌표로 직접 덮어쓰므로
        // 부모를 흔들어도 자식의 월드 위치가 고정되어 그대로 상쇄된다.
        // 구 GameCameraManager.SetCameraShake 가 shakeTrans 를 흔들고도 아무 일이 없었던 이유가 이것이고,
        // 호출부가 전부 주석 처리되어 있던 것도 같은 이유로 보인다
        // ===================================================================

        private float _shakeTrauma = 0f;
        private Vector3 _shakeOffset = Vector3.zero;

        //Perlin 샘플 시작점. 카메라가 여럿일 때 같은 파형이 겹치지 않도록 개체마다 다르게 잡는다
        private float _shakeSeed = 0f;

        //마지막으로 흔들림을 쌓은 시각과 프레임.
        //오라 중에는 적을 처치할 때마다 발동하는 것들이 있어 요청이 초당 여러 번 들어오는데,
        //그대로 받으면 누적값이 최대치에 붙어 화면이 상시 떨게 된다
        private float _lastShakeTime = -999f;
        private int _lastShakeFrame = -1;

        protected override void Awake()
        {
            base.Awake();

            currentViewCamera = transform;
            cameraCheckPosition = currentViewCamera.position;

            _shakeSeed = Random.Range(0f, 100f);
        }

        /// <summary>
        /// 카메라 흔들림을 누적한다 (0~1). 여러 사건이 겹치면 합쳐져 더 크게 흔들린다.
        ///
        /// ⚠️ 일반 히트처럼 초당 수십 번 일어나는 사건에는 절대 붙이지 않는다.
        /// 화면 전체가 움직이는 연출이라 빈도가 곧 멀미가 된다
        /// </summary>
        public void AddShakeTrauma(float InTrauma)
        {
            if (InTrauma <= 0f)
                return;

            CombatSettings combat = GameClientPlayConfig.Instance.combat;

            if (combat.cameraShakeEnabled == false)
                return;

            bool isSameFrame = _lastShakeFrame == Time.frameCount;

            //프레임이 다르면 실시간 최소 간격을 본다.
            //여기가 없으면 처치할 때마다 발동하는 오라 때문에 누적값이 최대치에 붙어 화면이 상시 떨게 된다
            if (isSameFrame == false && Time.unscaledTime - _lastShakeTime < combat.cameraShakeMinInterval)
                return;

            //유저 설정(옵션 > 카메라 흔들림). PlayerPrefs 조회라 매 프레임 읽지 않고
            //간격 제한을 통과한 요청에서만 본다
            if (PXPopup_Setting.GetSettingValue(PXPopup_Setting.ESettingType.CameraShake) == false)
                return;

            float requested = Mathf.Clamp01(InTrauma);

            if (isSameFrame)
            {
                //같은 프레임의 요청은 더하지 않고 가장 센 것 하나만 남긴다.
                //멀티샷처럼 한 번의 시전이 발사 지점을 여러 번 지나는 경우가 있어서,
                //더해 버리면 세기를 낮춰 놔도 두세 발 만에 최대치에 붙는다
                _shakeTrauma = Mathf.Max(_shakeTrauma, requested);
                return;
            }

            _lastShakeTime = Time.unscaledTime;
            _lastShakeFrame = Time.frameCount;

            //프레임이 다른(= 최소 간격을 넘긴) 요청은 남아 있던 여운 위에 쌓는다.
            //연달아 터지는 사건이 점점 세지는 것으로 읽힌다
            _shakeTrauma = Mathf.Clamp01(_shakeTrauma + requested);
        }

        void UpdateShake()
        {
            if (_shakeTrauma <= 0f)
            {
                _shakeOffset = Vector3.zero;
                return;
            }

            CombatSettings combat = GameClientPlayConfig.Instance.combat;

            //감쇠도 진동도 실시간 기준으로 돈다.
            //화면이 실제로 얼마나 자주 흔들리는지가 멀미를 좌우하므로,
            //2배속이라고 흔들림이 두 배로 잦아지면 안 된다
            float unscaledDelta = Time.unscaledDeltaTime;

            _shakeTrauma = Mathf.Max(_shakeTrauma - combat.cameraShakeDecay * unscaledDelta, 0f);

            //제곱으로 눌러 약한 사건은 거의 안 보이고 강한 사건만 확실히 튀게 한다.
            //선형이면 작은 흔들림이 상시 깔려 화면이 늘 미세하게 떠는 것처럼 보인다
            float amplitude = _shakeTrauma * _shakeTrauma * combat.cameraShakeDistance;

            //Perlin 은 연속이라 프레임마다 위치가 튀지 않는다.
            //Random 을 쓰면 매 프레임 무관한 값이 나와 흔들림이 아니라 지글거림이 된다
            float samplePos = Time.unscaledTime * combat.cameraShakeFrequency;
            float noiseX = Mathf.PerlinNoise(_shakeSeed, samplePos) * 2f - 1f;
            float noiseZ = Mathf.PerlinNoise(_shakeSeed + 31.7f, samplePos) * 2f - 1f;

            //이 오브젝트는 지면 높이(Y=0)의 카메라 리그 루트라 XZ 이동이 곧 화면상 이동이 된다.
            //높이(Y)는 흔들지 않는다 — 화면에서 XZ 흔들림과 잘 구분되지 않으면서
            //카메라가 지형을 뚫고 내려갈 위험만 는다
            _shakeOffset = new Vector3(noiseX * amplitude, 0f, noiseZ * amplitude);
        }

        public void SetFollowCameraCharacter(UCharacterActor InCharacter)
        {
            followCameraCharacter = InCharacter;
            currentCameraCharacter = InCharacter;
        }
        public void SetFollowBossCameraCharacter(UCharacterActor InBossActor)
        {
            followBossCameraCharacter = InBossActor;
            currentCameraCharacter = InBossActor;
        }

        protected override void Update()
        {
            base.Update();

            if (GameBattleControlManager.IsInit == false)
                return;

            if (currentViewCamera != null)
            {
                //UCharacterActor currentCharacter = GameBattleControlManager.Instance.CurrentMainCharacter;

                if (currentCameraCharacter != null)
                {
                    // ī�޶� ��ġ�� ��� ��ġ�� �̵�
                    Vector3 characterPosition = currentCameraCharacter.CenterWorldPosition;
                    Vector3 desiredPosition = new Vector3(characterPosition.x, 0, characterPosition.z);
                    desiredPosition += cameraCheckPosition;

                    //ĳ���� �������� ���� �� �̵�
                    if (cameraFrontViewDistance > 0)
                    {
                        desiredPosition += currentCameraCharacter.GetTransform.forward * cameraFrontViewDistance;
                    }

                    Vector3 smoothedPosition = desiredPosition;
                    //���� ����
                    if (cameraSmoothSpeed > 0)
                    {
                        //보간 기준은 흔들림을 뺀 위치여야 한다.
                        //흔들린 위치를 기준으로 삼으면 오프셋이 다음 프레임 목표에 섞여 들어가
                        //추종 위치 자체가 흔들림을 따라 조금씩 밀려난다
                        Vector3 basePosition = currentViewCamera.position - _shakeOffset;
                        smoothedPosition = Vector3.Lerp(basePosition, smoothedPosition, Time.unscaledDeltaTime * cameraSmoothSpeed);
                    }

                    UpdateShake();

                    currentViewCamera.position = smoothedPosition + _shakeOffset;


                }


            }
        }
    }

}
