// ===========================================================================
// [발췌] UCharacterActor.cs 의 타격감 연출 구간만 잘라낸 파일이다.
// 원본: Assets/Source/Logic/Character/Component/CharacterActors/Base/UCharacterActor.cs
//       (전체 3,182줄) 중 1279~1651행.
//
// 원본에서 이 구간을 호출하는 지점은 두 곳이다.
//   - 매 프레임: UpdateHitFlash(deltaTime) / UpdateHitShake(deltaTime)   (원본 423~424행)
//   - 피격 시각: PlayHitFlash(isCriticalHit) / PlayHitShake(isCriticalHit) (원본 1248~1249행)
//
// 클래스 선언과 using 은 발췌 범위 밖이라 이 파일만으로는 컴파일되지 않는다.
// ===========================================================================

        // ===================================================================
        // 히트 플래시 — 피격 순간 몸을 아주 짧게 단색으로 덮는다.
        //
        // 자동전투라 타격감을 실어 줄 입력이 없다. 카메라 흔들기나 시간 정지는 초당 수십 번 일어나는
        // 일반 히트에 쓰면 곧바로 멀미가 되므로, 화면을 건드리지 않고 개체 단위로만 반응하는
        // 이 채널이 사실상 유일한 선택지다.
        //
        // 셰이더 쪽은 PX_MonsterShaderURP.shader 의 _HitFlashColor / _HitFlashBlend.
        // ===================================================================

        //매 프레임 문자열을 해싱하지 않도록 프로퍼티 ID 를 캐시한다
        private static readonly int HitFlashColorPropID = Shader.PropertyToID("_HitFlashColor");
        private static readonly int HitFlashBlendPropID = Shader.PropertyToID("_HitFlashBlend");

        //blend 를 이 단계 수로 끊는다. 계단이 눈에 띄지 않으면서 렌더러 쓰기는 줄어드는 지점
        private const int HitFlashBlendSteps = 16;

        //모든 개체가 돌려쓰는 MPB 하나. SetPropertyBlock 이 값을 렌더러 쪽으로 복사해 가므로
        //개체마다 따로 들고 있을 이유가 없다.
        //⚠️ new Material() 로 개체별 머티리얼을 만드는 방식은 쓰지 않는다 —
        //풀에서 몬스터가 회전할 때마다 머티리얼이 새로 생겨 그대로 누수된다
        private static MaterialPropertyBlock _hitFlashBlock;

        //같은 프레임에 새로 시작한 번쩍임 수. 프레임이 바뀌면 0 으로 돌아간다.
        //
        //"지금 번쩍이는 개체 수" 를 누적으로 세지 않는 이유는, 씬 전환이나 강제 파괴로 반납이
        //단 한 번만 누락돼도 카운터가 영영 높은 값에 묶여 그 뒤로 아무도 번쩍이지 않게 되기 때문이다.
        //프레임 단위면 다음 프레임에 스스로 회복한다.
        //광역기는 어차피 한 프레임에 몰려 들어오므로 백화가 생기는 구간도 정확히 여기다
        private static int _hitFlashFrameStamp = -1;
        private static int _hitFlashFrameCount = 0;

        //이 개체의 몸 렌더러 목록. 몸이 head/chest/arms/legs 로 나뉜 몬스터가 많아
        //하나만 잡으면 머리만 번쩍이고 몸통은 그대로인 그림이 나온다
        private readonly List<Renderer> _hitFlashRenderers = new List<Renderer>();
        private bool _isHitFlashRendererCached = false;

        private float _hitFlashTimer = 0f;
        private float _hitFlashDuration = 0f;
        private float _hitFlashPeak = 0f;
        private Color _hitFlashColor = Color.white;

        //마지막으로 렌더러에 써 넣은 blend 단계. 값이 바뀔 때만 SetPropertyBlock 을 호출한다
        private int _hitFlashWrittenStep = -1;

        /// <summary>
        /// 피격 번쩍임을 시작한다. 이미 번쩍이는 중이면 타이머를 되살리고 더 센 쪽만 남긴다.
        /// </summary>
        void PlayHitFlash(bool InIsCritical)
        {
            CombatSettings combat = GameClientPlayConfig.Instance.combat;

            if (combat.hitFlashEnabled == false)
                return;

            //플레이어는 셰이더가 다르고 서브메시가 최대 7개라 비용 대비 효과가 낮다.
            //"내가 맞고 있다" 는 이미 붉은 데미지 텍스트가 전달하고 있다
            if (IsPlayerCharacter)
                return;

            //죽은 개체는 곧 파쇄가 머티리얼을 통째로 갈아 끼우므로 여기서 넣은 값이 의미가 없다
            if (IsDead || IsShattering)
                return;

            if (CacheHitFlashRenderer() == false)
                return;

            //프레임이 바뀌었으면 이번 프레임 몫을 새로 연다
            if (_hitFlashFrameStamp != Time.frameCount)
            {
                _hitFlashFrameStamp = Time.frameCount;
                _hitFlashFrameCount = 0;
            }

            //이미 번쩍이는 개체가 또 맞은 건 새 자리를 쓰지 않는다.
            //연타가 잦은 근접 몬스터가 상한을 혼자 다 먹는 걸 막는다
            bool isRefresh = _hitFlashTimer > 0f;

            if (isRefresh == false && _hitFlashFrameCount >= combat.hitFlashMaxPerFrame)
                return;

            //자리를 잡기 직전의 인원수로 감쇠를 정한다. 혼자 맞고 있으면 감쇠가 없다
            float newPeak = ResultHitFlashPeak(combat, InIsCritical, _hitFlashFrameCount);

            if (isRefresh == false)
                _hitFlashFrameCount++;

            //연타 중이면 더 센 쪽만 남긴다.
            //일반 히트가 직전 크리티컬의 번쩍임을 덮어 약하게 만드는 게 제일 어색하다
            if (isRefresh == false || newPeak >= _hitFlashPeak)
                _hitFlashPeak = newPeak;

            //색은 일반·크리티컬이 같다. 0.06초짜리 번쩍임에서 색 차이는 읽히지 않아
            //구분은 세기가 전담하고, 조절할 곳을 하나로 줄였다
            _hitFlashColor = combat.hitFlashColor;

            _hitFlashDuration = Mathf.Max(combat.hitFlashDuration, 0.01f);
            _hitFlashTimer = _hitFlashDuration;

            ApplyHitFlashBlend(_hitFlashPeak);
        }

        /// <summary>
        /// 같은 프레임에 함께 맞은 수를 반영한 번쩍임 세기.
        ///
        /// 스무 마리가 한꺼번에 같은 세기로 하얘지면 개별 피격이 아니라 화면 백화로 보인다.
        /// 인원이 늘수록 각자를 낮춰 화면에 올라가는 총량을 대체로 일정하게 유지한다
        /// </summary>
        static float ResultHitFlashPeak(CombatSettings InCombat, bool InIsCritical, int InActiveCountBefore)
        {
            float peak = InIsCritical ? InCombat.hitFlashCriticalStrength : InCombat.hitFlashStrength;

            int limit = Mathf.Max(InCombat.hitFlashMaxPerFrame, 1);

            if (limit > 1)
            {
                float crowd = Mathf.Clamp01((float)Mathf.Max(InActiveCountBefore, 0) / (limit - 1));
                peak *= Mathf.Lerp(1f, InCombat.hitFlashCrowdAttenuation, crowd);
            }

            return Mathf.Clamp01(peak);
        }

        void UpdateHitFlash(float InDeltaTime)
        {
            if (_hitFlashTimer <= 0f)
                return;

            //파쇄가 머티리얼을 갈아 끼운 뒤라 렌더러를 건드리면 파쇄가 넣어 둔 값을 지운다.
            //자리만 반납하고 빠진다
            if (IsShattering)
            {
                ClearHitFlash(false);
                return;
            }

            _hitFlashTimer -= InDeltaTime;

            if (_hitFlashTimer <= 0f)
            {
                ClearHitFlash(true);
                return;
            }

            //선형 감쇠. 맞은 순간이 제일 밝고 곧게 사그라든다
            ApplyHitFlashBlend(_hitFlashPeak * (_hitFlashTimer / _hitFlashDuration));
        }

        void ApplyHitFlashBlend(float InBlend)
        {
            //값이 실제로 달라질 때만 써 넣는다. 지속시간이 짧아 프레임 수가 몇 안 되지만
            //지속시간을 늘리거나 배속이 낮아지면 눈에 보이지도 않는 차이로 매 프레임 쓰기가 돈다
            int step = Mathf.RoundToInt(Mathf.Clamp01(InBlend) * HitFlashBlendSteps);

            if (step == _hitFlashWrittenStep)
                return;

            _hitFlashWrittenStep = step;

            if (step <= 0)
            {
                //MPB 를 떼면 머티리얼 기본값(0)으로 돌아간다. 0 을 써 넣는 것보다 렌더링 경로가 짧다
                for (int i = 0; i < _hitFlashRenderers.Count; i++)
                {
                    Renderer bodyRenderer = _hitFlashRenderers[i];

                    if (bodyRenderer != null)
                        bodyRenderer.SetPropertyBlock(null);
                }

                return;
            }

            if (_hitFlashBlock == null)
                _hitFlashBlock = new MaterialPropertyBlock();

            _hitFlashBlock.SetColor(HitFlashColorPropID, _hitFlashColor);
            _hitFlashBlock.SetFloat(HitFlashBlendPropID, (float)step / HitFlashBlendSteps);

            for (int i = 0; i < _hitFlashRenderers.Count; i++)
            {
                Renderer bodyRenderer = _hitFlashRenderers[i];

                if (bodyRenderer != null)
                    bodyRenderer.SetPropertyBlock(_hitFlashBlock);
            }
        }

        /// <summary>
        /// 번쩍임을 끝낸다.
        /// </summary>
        /// <param name="InRestoreRenderer">
        /// 렌더러의 MPB 를 실제로 되돌릴지.
        /// 파쇄가 이미 머티리얼을 갈아 끼운 뒤라면 여기서 건드리면 파쇄 쪽 값이 지워지므로 false 로 부른다
        /// </param>
        void ClearHitFlash(bool InRestoreRenderer)
        {
            if (InRestoreRenderer && _hitFlashWrittenStep > 0)
                ApplyHitFlashBlend(0f);

            _hitFlashWrittenStep = -1;
            _hitFlashTimer = 0f;
            _hitFlashPeak = 0f;
        }

        /// <summary>
        /// 몸 렌더러를 한 번만 모아 둔다. 첫 피격 때 모으므로 스폰 비용에는 얹히지 않는다.
        ///
        /// 파티클·트레일 렌더러까지 덮으면 몸에 붙은 이펙트가 통째로 하얘지므로 메시 렌더러만 고른다
        /// </summary>
        bool CacheHitFlashRenderer()
        {
            if (_isHitFlashRendererCached)
                return _hitFlashRenderers.Count > 0;

            //메시가 아직 안 붙었으면 캐시로 확정하지 않는다. 다음 피격에서 다시 시도한다
            Transform meshRoot = MeshArea;

            if (meshRoot == null)
                return false;

            _hitFlashRenderers.Clear();
            meshRoot.GetComponentsInChildren(true, _hitFlashRenderers);

            for (int i = _hitFlashRenderers.Count - 1; i >= 0; i--)
            {
                Renderer bodyRenderer = _hitFlashRenderers[i];

                if (bodyRenderer is SkinnedMeshRenderer || bodyRenderer is MeshRenderer)
                    continue;

                _hitFlashRenderers.RemoveAt(i);
            }

            _isHitFlashRendererCached = true;

            return _hitFlashRenderers.Count > 0;
        }

        // ===================================================================
        // 히트 셰이크 — 피격당한 몬스터의 몸이 짧게 진동한다.
        //
        // 플래시가 "맞았다" 를 알린다면 이쪽은 "얼마나 세게" 를 알린다.
        // 셰이더를 타지 않아 어떤 셰이더를 쓰는 몬스터에도 똑같이 걸린다 —
        // 일부 몬스터(beholder / slime 계열)는 Shader Graphs/URPmaskTint 를 써서 플래시가 무시되는데,
        // 그 개체들에게는 이쪽이 유일한 피격 반응이다.
        //
        // ⚠️ AnimationSettings 의 "피격 효과" 와는 별개다. 그쪽은 훈련장 더미 전용 DOTween 흔들림으로,
        // 매번 DOKill + 새 트윈을 만드는 방식이라 실전의 초당 수십 회 피격에는 쓰지 않는다
        // ===================================================================

        private static int _hitShakeFrameStamp = -1;
        private static int _hitShakeFrameCount = 0;

        private float _hitShakeTimer = 0f;
        private float _hitShakeDuration = 0f;
        private float _hitShakeDistance = 0f;
        private float _hitShakeFrequency = 30f;

        //흔들기 시작 전의 X. UpdateMeshArea 가 스폰마다 0 으로 두지만,
        //원위치를 실제로 읽어 두면 프리팹에 오프셋이 생겨도 제자리로 돌아온다
        private float _hitShakeBaseX = 0f;

        //MeshArea 를 우리가 밀어 놓은 상태인지. 밀지 않았는데 되돌리면 남의 위치를 덮어쓴다
        private bool _isHitShakeActive = false;

        /// <summary>
        /// 피격 진동을 시작한다. 이미 떨고 있으면 더 큰 쪽만 남기고 타이머를 되살린다.
        /// </summary>
        void PlayHitShake(bool InIsCritical)
        {
            CombatSettings combat = GameClientPlayConfig.Instance.combat;

            if (combat.hitShakeEnabled == false)
                return;

            if (IsPlayerCharacter)
                return;

            //파쇄가 시작되면 조각이 자체 물리로 흩어진다. 여기서 MeshArea 를 밀면 조각 전체가 같이 끌려간다
            if (IsDead || IsShattering)
                return;

            if (MeshArea == null)
                return;

            if (_hitShakeFrameStamp != Time.frameCount)
            {
                _hitShakeFrameStamp = Time.frameCount;
                _hitShakeFrameCount = 0;
            }

            //이미 떨고 있는 개체가 또 맞은 건 새 자리를 쓰지 않는다
            bool isRefresh = _hitShakeTimer > 0f;

            if (isRefresh == false)
            {
                if (_hitShakeFrameCount >= combat.hitShakeMaxPerFrame)
                    return;

                _hitShakeFrameCount++;

                //떨기 전의 위치를 기준으로 삼는다. 연타 때 다시 읽으면 이미 밀린 값이 기준이 되어 몸이 흘러간다
                _hitShakeBaseX = MeshArea.localPosition.x;
                _isHitShakeActive = true;
            }

            float distance = combat.hitShakeDistance * (InIsCritical ? combat.hitShakeCriticalScale : 1f);

            //연타 중이면 더 큰 쪽만 남긴다. 약한 히트가 직전 크리티컬의 진동을 줄이는 게 어색하다
            if (isRefresh == false || distance >= _hitShakeDistance)
                _hitShakeDistance = distance;

            _hitShakeDuration = Mathf.Max(combat.hitShakeDuration, 0.01f);
            _hitShakeFrequency = Mathf.Max(combat.hitShakeFrequency, 1f);
            _hitShakeTimer = _hitShakeDuration;
        }

        void UpdateHitShake(float InDeltaTime)
        {
            if (_hitShakeTimer <= 0f)
                return;

            //파쇄로 넘어갔으면 조각 쪽이 MeshArea 를 쓴다. 되돌리지도 말고 손을 뗀다
            if (IsShattering)
            {
                _hitShakeTimer = 0f;
                _hitShakeDistance = 0f;
                _isHitShakeActive = false;
                return;
            }

            _hitShakeTimer -= InDeltaTime;

            if (_hitShakeTimer <= 0f)
            {
                ClearHitShake();
                return;
            }

            if (MeshArea == null)
                return;

            //진폭이 남은 시간에 비례해 줄어드는 감쇠 진동.
            //위상은 경과 시간으로 만들어 시작(0)에서 곧바로 튀어 나가고,
            //진폭이 0 으로 수렴하므로 끝날 때 제자리로 돌아온다
            float elapsed = _hitShakeDuration - _hitShakeTimer;
            float decay = _hitShakeTimer / _hitShakeDuration;
            float wave = Mathf.Sin(elapsed * _hitShakeFrequency * Mathf.PI * 2f);

            Vector3 localPos = MeshArea.localPosition;
            localPos.x = _hitShakeBaseX + wave * _hitShakeDistance * decay;
            MeshArea.localPosition = localPos;
        }

        void ClearHitShake()
        {
            _hitShakeTimer = 0f;
            _hitShakeDistance = 0f;

            if (_isHitShakeActive == false)
                return;

            _isHitShakeActive = false;

            //파쇄 중이면 조각 쪽이 MeshArea 를 쓰고 있으므로 되돌리지 않는다
            if (MeshArea != null && IsShattering == false)
            {
                Vector3 localPos = MeshArea.localPosition;
                localPos.x = _hitShakeBaseX;
                MeshArea.localPosition = localPos;
            }
        }
