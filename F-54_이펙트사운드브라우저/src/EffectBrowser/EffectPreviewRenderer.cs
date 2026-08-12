// 이펙트 브라우저 — 오프스크린 파티클 렌더러
// PreviewRenderUtility의 격리된 프리뷰 씬에서 파티클을 시뮬레이션하고 렌더한다.
// 현재 열려 있는 씬을 전혀 건드리지 않으므로 씬이 dirty 상태가 되지 않는다.

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PX.EffectBrowser
{
    /// <summary>
    /// 이펙트 프리팹 하나를 프리뷰 씬에 올려두고 시뮬레이션/렌더링한다.
    /// 썸네일 배치 캡처와 실시간 프리뷰가 각각 독립 인스턴스로 사용한다.
    /// </summary>
    public sealed class EffectPreviewRenderer : IDisposable
    {
        #region 상수

        /// <summary>시뮬레이션 1스텝의 최대 크기(초). 너무 크면 파티클 궤적이 끊겨 보인다.</summary>
        private const float MaxSimulationStep = 1f / 30f;

        /// <summary>썸네일 최적 시점을 찾을 때 실제로 렌더해보는 횟수</summary>
        private const int ThumbnailProbeSteps = 8;

        /// <summary>시점 탐색용 저해상도 렌더 크기. 밝기 비교만 하면 되므로 작아도 된다.</summary>
        private const int ProbeRenderSize = 64;

        /// <summary>
        /// 프로브 시점 분포의 지수. 1보다 크면 앞쪽이 조밀해진다.
        /// 타격·폭발처럼 초반에 정점을 찍고 사라지는 이펙트가 많아 앞쪽을 촘촘히 본다.
        /// </summary>
        private const float ProbeBias = 1.5f;

        /// <summary>
        /// 이 정도도 화면에 안 보이면 사실상 빈 그림으로 본다.
        /// <see cref="MeasureVisibleAmount"/>가 돌려주는 값의 기준이며, 전진 시뮬레이션 재시도를 촉발한다.
        /// </summary>
        private const float MinVisibleAmount = 3000f;

        /// <summary>카메라 프레이밍 시 허용하는 최대 반경. 파티클이 멀리 흩어져도 피사체가 점이 되지 않게 제한한다.</summary>
        private const float MaxFramingRadius = 40f;

        /// <summary>
        /// 화각에 담을 "시각적 질량"(파티클 크기 합)의 비율.
        /// 나머지는 화면 밖으로 나가도 무방한 잔여 스파크로 취급한다.
        /// </summary>
        private const float VisualMassRatio = 0.85f;

        #endregion

        #region 필드

        private PreviewRenderUtility _preview;
        private GameObject _instance;

        /// <summary>루트 파티클 시스템 (부모에 다른 ParticleSystem이 없는 것). Simulate는 루트에서 자식 포함으로 호출한다.</summary>
        private ParticleSystem[] _rootSystems = Array.Empty<ParticleSystem>();

        /// <summary>인스턴스에 포함된 전체 파티클 시스템</summary>
        private ParticleSystem[] _allSystems = Array.Empty<ParticleSystem>();

        private Animator[] _animators = Array.Empty<Animator>();

        private Bounds _framingBounds = new Bounds(Vector3.zero, Vector3.one);

        /// <summary>파티클 좌표 읽기용 재사용 버퍼 (매번 할당하면 GC가 크게 튄다)</summary>
        private ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[256];

        private readonly System.Collections.Generic.List<Vector3> _positionBuffer =
            new System.Collections.Generic.List<Vector3>(512);

        private readonly System.Collections.Generic.List<float> _sizeBuffer =
            new System.Collections.Generic.List<float>(512);

        /// <summary>(중심까지 거리, 파티클 크기) 쌍. 거리순 정렬해 시각적 반경을 구한다.</summary>
        private readonly System.Collections.Generic.List<Vector2> _weightBuffer =
            new System.Collections.Generic.List<Vector2>(512);

        private static readonly System.Comparison<Vector2> CompareByDistance =
            (a, b) => a.x.CompareTo(b.x);

        /// <summary>썸네일 시점 탐색용 — 프로브별로 실제 화면에 보인 양</summary>
        private readonly float[] _probeMass = new float[ThumbnailProbeSteps];

        /// <summary>프로브별 시각(초)</summary>
        private readonly float[] _probeTimes = new float[ThumbnailProbeSteps];

        #endregion

        #region 프로퍼티

        /// <summary>카메라 수평 회전각</summary>
        public float Yaw { get; set; } = 0f;

        /// <summary>카메라 수직 회전각</summary>
        public float Pitch { get; set; } = 8f;

        /// <summary>1.0이 자동 프레이밍 기준. 값이 클수록 멀어진다.</summary>
        public float ZoomFactor { get; set; } = 1f;

        /// <summary>배경색. 이펙트는 대부분 가산 합성이라 어두운 배경에서 잘 보인다.</summary>
        public Color Background { get; set; } = new Color(0.11f, 0.11f, 0.13f, 1f);

        /// <summary>
        /// 시뮬레이션 중 인스턴스를 전진시키는 속도(초당 유닛). 0이면 제자리에 둔다.
        /// 투사체 이펙트는 날아가야 트레일이 생기는데, 실제 이동은 런타임 스크립트가 담당하므로
        /// 에디터에서는 여기서 대신 밀어줘야 형태가 드러난다.
        /// </summary>
        public float MotionSpeed { get; set; }

        /// <summary>현재 대상이 설정되어 있는지</summary>
        public bool HasTarget => _instance != null;

        /// <summary>추정 재생 길이(초)</summary>
        public float EstimatedDuration { get; private set; } = 1f;

        /// <summary>포함된 파티클 시스템 개수</summary>
        public int ParticleSystemCount => _allSystems.Length;

        /// <summary>Animator 포함 여부</summary>
        public bool HasAnimator => _animators.Length > 0;

        /// <summary>현재 카메라가 잡고 있는 바운드 (프레이밍 진단용)</summary>
        public Bounds FramingBounds => _framingBounds;

        #endregion

        #region 초기화

        private void EnsurePreviewUtility()
        {
            if (_preview != null) return;

            _preview = new PreviewRenderUtility();

            var camera = _preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            camera.useOcclusionCulling = false;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            // URP는 카메라별 추가 데이터가 필요하다. 프리뷰에서는 그림자/후처리를 끄는 편이 빠르고 안정적이다.
            var urpData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (urpData == null)
            {
                urpData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            urpData.renderShadows = false;
            urpData.renderPostProcessing = false;
            urpData.antialiasing = AntialiasingMode.None;
            urpData.requiresColorOption = CameraOverrideOption.Off;
            urpData.requiresDepthOption = CameraOverrideOption.Off;

            _preview.lights[0].intensity = 1.1f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            if (_preview.lights.Length > 1)
            {
                _preview.lights[1].intensity = 0.6f;
                _preview.lights[1].transform.rotation = Quaternion.Euler(-20f, -120f, 0f);
            }
            _preview.ambientColor = new Color(0.35f, 0.35f, 0.38f, 0f);
        }

        #endregion

        #region 대상 설정

        /// <summary>
        /// 프리뷰 대상 프리팹을 설정한다. 기존 대상은 정리된다.
        /// </summary>
        /// <returns>인스턴스 생성에 성공했는지 여부</returns>
        public bool SetTarget(GameObject prefab)
        {
            ClearTarget();
            if (prefab == null) return false;

            EnsurePreviewUtility();

            try
            {
                _instance = UnityEngine.Object.Instantiate(prefab);
            }
            catch (Exception e)
            {
                // 프리팹의 [ExecuteAlways] 스크립트가 에디터에서 예외를 던지는 경우가 있다
                Debug.LogError($"[EffectBrowser] 프리팹 인스턴스 생성 실패: {prefab.name} — {e.Message}");
                _instance = null;
                return false;
            }

            _instance.hideFlags = HideFlags.HideAndDontSave;
            _instance.transform.position = Vector3.zero;
            _instance.transform.rotation = Quaternion.identity;
            _instance.SetActive(true);

            _preview.AddSingleGO(_instance);

            CollectComponents();
            EstimatedDuration = EstimateDuration();

            ResetSimulation();
            FrameCamera();
            return true;
        }

        /// <summary>현재 대상을 제거한다.</summary>
        public void ClearTarget()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
                _instance = null;
            }
            _rootSystems = Array.Empty<ParticleSystem>();
            _allSystems = Array.Empty<ParticleSystem>();
            _animators = Array.Empty<Animator>();
        }

        /// <summary>인스턴스에서 시뮬레이션 대상 컴포넌트를 수집한다.</summary>
        private void CollectComponents()
        {
            _allSystems = _instance.GetComponentsInChildren<ParticleSystem>(true);

            // 루트 시스템만 추려낸다 — Simulate(withChildren:true)가 하위를 함께 처리하므로 중복 호출을 피한다
            var roots = new System.Collections.Generic.List<ParticleSystem>();
            foreach (var system in _allSystems)
            {
                var parent = system.transform.parent;
                if (parent == null || parent.GetComponentInParent<ParticleSystem>() == null)
                {
                    roots.Add(system);
                }
            }
            _rootSystems = roots.ToArray();

            _animators = _instance.GetComponentsInChildren<Animator>(true);
            foreach (var animator in _animators)
            {
                // 프리뷰 씬에는 카메라 절두체 밖 판정이 있을 수 있어 항상 갱신하도록 강제한다
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        #endregion

        #region 시뮬레이션

        /// <summary>시뮬레이션을 0초 상태로 되돌린다.</summary>
        public void ResetSimulation()
        {
            // 전진 시뮬레이션으로 옮겨진 위치도 함께 원복해야 한다
            if (_instance != null)
            {
                _instance.transform.position = Vector3.zero;
            }

            foreach (var system in _rootSystems)
            {
                if (system == null || !system.gameObject.activeInHierarchy) continue;
                try
                {
                    system.Simulate(0f, true, true, false);
                }
                catch (Exception)
                {
                    // 일부 서브에미터 구성에서 Simulate가 실패할 수 있다 — 해당 시스템만 건너뛴다
                }
            }

            foreach (var animator in _animators)
            {
                if (animator == null || animator.runtimeAnimatorController == null) continue;
                try
                {
                    animator.Rebind();
                    animator.Update(0f);
                }
                catch (Exception)
                {
                    // Animator가 프리뷰 씬에서 초기화되지 않은 경우 무시한다
                }
            }
        }

        /// <summary>시뮬레이션을 지정 시간만큼 진행한다.</summary>
        public void StepSimulation(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            float remaining = deltaTime;
            while (remaining > 0f)
            {
                float step = Mathf.Min(MaxSimulationStep, remaining);
                remaining -= step;

                if (MotionSpeed != 0f && _instance != null)
                {
                    var transform = _instance.transform;
                    transform.position += transform.forward * (MotionSpeed * step);
                }

                foreach (var system in _rootSystems)
                {
                    if (system == null || !system.gameObject.activeInHierarchy) continue;
                    try
                    {
                        system.Simulate(step, true, false, false);
                    }
                    catch (Exception)
                    {
                        // 개별 시스템 실패는 무시한다
                    }
                }

                foreach (var animator in _animators)
                {
                    if (animator == null || animator.runtimeAnimatorController == null) continue;
                    try
                    {
                        animator.Update(step);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        /// <summary>0초부터 다시 시작해 지정 시점까지 시뮬레이션한다.</summary>
        public void SimulateTo(float time)
        {
            ResetSimulation();
            StepSimulation(time);
        }

        /// <summary>현재 살아 있는 파티클 총수</summary>
        public int GetTotalParticleCount()
        {
            int total = 0;
            foreach (var system in _allSystems)
            {
                if (system == null) continue;
                total += system.particleCount;
            }
            return total;
        }

        /// <summary>
        /// 지금 화면에 실제로 보이는 양을 잰다 — 저해상도로 한 장 렌더해 배경 대비 픽셀 차이를 합산한다.
        /// 파티클 개수나 알파만으로는 판단할 수 없다. 스프라이트 시트 애니메이션으로
        /// 사라지는 이펙트는 파티클도 살아 있고 알파도 1이지만 화면에는 아무것도 남지 않는다.
        /// </summary>
        private float MeasureVisibleAmount()
        {
            var texture = RenderStatic(ProbeRenderSize);
            if (texture == null) return 0f;

            var pixels = texture.GetPixels32();
            if (pixels.Length == 0)
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return 0f;
            }

            // 모서리 픽셀을 배경으로 간주한다 (이펙트는 중앙에 프레이밍되어 있다)
            var background = pixels[0];
            float sum = 0f;

            foreach (var pixel in pixels)
            {
                sum += Mathf.Abs(pixel.r - background.r)
                       + Mathf.Abs(pixel.g - background.g)
                       + Mathf.Abs(pixel.b - background.b);
            }

            UnityEngine.Object.DestroyImmediate(texture);
            return sum;
        }

        /// <summary>
        /// 썸네일로 쓰기 좋은 시점(파티클이 가장 많은 순간)을 찾는다.
        /// 한 번만 순회하며 최댓값 시점을 기록한 뒤 그 지점을 반환한다.
        /// </summary>
        public float FindBestThumbnailTime()
        {
            MotionSpeed = 0f;
            float staticBest = ProbeForBestTime(out float staticPeak);

            // 제자리에서 충분히 보이면 그대로 쓴다
            if (staticPeak >= MinVisibleAmount)
            {
                return staticBest;
            }

            // 거의 아무것도 안 보이면 투사체·트레일 계열로 보고 전진시키며 다시 훑는다
            MotionSpeed = EstimateMotionSpeed();
            float movingBest = ProbeForBestTime(out float movingPeak);

            if (movingPeak > staticPeak * 1.5f)
            {
                return movingBest;
            }

            // 나아지지 않았으면 정지 상태로 되돌린다
            MotionSpeed = 0f;
            return staticBest;
        }

        /// <summary>전진 시뮬레이션 속도 — 재생 시간 동안 이펙트 반경의 다섯 배쯤 이동하도록 잡는다.</summary>
        private float EstimateMotionSpeed()
        {
            float radius = Mathf.Max(_framingBounds.extents.magnitude / Mathf.Sqrt(3f), 0.3f);
            return radius * 5f / Mathf.Max(EstimatedDuration, 0.1f);
        }

        /// <summary>현재 설정(정지/전진)으로 시점들을 훑어 가장 잘 보이는 시각을 찾는다.</summary>
        private float ProbeForBestTime(out float peak)
        {
            float duration = EstimatedDuration;

            ResetSimulation();

            peak = 0f;
            float elapsed = 0f;

            // 시뮬레이션을 증분으로 진행하며 각 시점을 실제로 렌더해 본다
            for (int i = 0; i < ThumbnailProbeSteps; i++)
            {
                float time = duration * Mathf.Pow((i + 1f) / ThumbnailProbeSteps, ProbeBias);
                StepSimulation(time - elapsed);
                elapsed = time;

                _probeTimes[i] = time;

                // 각 시점의 "가장 잘 잡았을 때" 모습을 비교해야 하므로 프레이밍도 매번 맞춘다
                FrameCamera(false);
                _probeMass[i] = MeasureVisibleAmount();
                peak = Mathf.Max(peak, _probeMass[i]);
            }

            // 어느 시점에도 아무것도 보이지 않으면 중반 지점을 쓴다
            if (peak <= 0f)
            {
                return duration * 0.35f;
            }

            // 정점 부근에서는 파티클이 아직 한 점에 뭉쳐 있는 경우가 많다.
            // 보이는 양이 정점의 85% 이상을 유지하는 구간 중 가장 늦은 시점이 형태가 잘 드러난다.
            float threshold = peak * 0.85f;
            int chosen = 0;
            for (int i = 0; i < ThumbnailProbeSteps; i++)
            {
                if (_probeMass[i] >= threshold)
                {
                    chosen = i;
                }
            }

            return _probeTimes[chosen];
        }

        /// <summary>파티클 시스템 설정에서 전체 재생 길이를 추정한다.</summary>
        private float EstimateDuration()
        {
            float max = 0f;
            foreach (var system in _allSystems)
            {
                if (system == null) continue;

                var main = system.main;
                float lifetime = GetMaxCurveValue(main.startLifetime);
                float delay = GetMaxCurveValue(main.startDelay);
                float total = main.loop ? main.duration : main.duration + lifetime;
                max = Mathf.Max(max, total + delay);
            }

            if (max <= 0f)
            {
                max = 1f;
            }
            return Mathf.Clamp(max, 0.25f, 8f);
        }

        /// <summary>MinMaxCurve가 가질 수 있는 최댓값을 구한다.</summary>
        private static float GetMaxCurveValue(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return curve.constantMax;
                case ParticleSystemCurveMode.Curve:
                    return curve.curveMultiplier * GetCurvePeak(curve.curve);
                case ParticleSystemCurveMode.TwoCurves:
                    return curve.curveMultiplier * GetCurvePeak(curve.curveMax);
                default:
                    return 0f;
            }
        }

        private static float GetCurvePeak(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return 0f;

            float peak = 0f;
            foreach (var key in curve.keys)
            {
                peak = Mathf.Max(peak, key.value);
            }
            return peak;
        }

        #endregion

        #region 카메라

        /// <summary>
        /// 현재 파티클 분포를 기준으로 카메라 거리와 각도를 다시 계산한다.
        /// 파티클이 퍼진 시점에 호출해야 적절한 화각이 나온다.
        /// </summary>
        /// <param name="autoOrient">납작한 이펙트를 정면에서 보도록 각도를 자동으로 맞출지 여부</param>
        public void FrameCamera(bool autoOrient = true)
        {
            if (_instance == null) return;

            _framingBounds = CalculateBounds();

            if (autoOrient)
            {
                AutoOrientCamera();
            }
            ApplyCameraTransform();
        }

        /// <summary>
        /// 파티클 분포가 한 축으로 뚜렷하게 납작하면 그 축을 정면으로 바라본다.
        /// 참격이나 장판처럼 평면형인 이펙트를 옆에서 보면 선 하나로 보여 알아볼 수 없다.
        /// </summary>
        private void AutoOrientCamera()
        {
            if (_positionBuffer.Count < 4) return;

            var center = _framingBounds.center;
            var variance = Vector3.zero;
            float totalWeight = 0f;

            for (int i = 0; i < _positionBuffer.Count; i++)
            {
                var offset = _positionBuffer[i] - center;
                float weight = _sizeBuffer[i];

                variance += new Vector3(offset.x * offset.x, offset.y * offset.y, offset.z * offset.z) * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f) return;
            variance /= totalWeight;

            float maxVariance = Mathf.Max(variance.x, Mathf.Max(variance.y, variance.z));
            float minVariance = Mathf.Min(variance.x, Mathf.Min(variance.y, variance.z));
            if (maxVariance <= 1e-6f) return;

            // 뚜렷하게 납작할 때만 개입한다. 구형에 가까우면 기본 각도가 더 자연스럽다.
            if (minVariance > maxVariance * 0.2f) return;

            if (Mathf.Approximately(minVariance, variance.y))
            {
                // 바닥에 깔린 평면 — 비스듬히 내려다본다
                Yaw = 0f;
                Pitch = 65f;
            }
            else if (Mathf.Approximately(minVariance, variance.x))
            {
                // YZ 평면 — 옆에서 본다
                Yaw = 90f;
                Pitch = 8f;
            }
            else
            {
                // XY 평면 — 정면에서 본다
                Yaw = 0f;
                Pitch = 8f;
            }
        }

        /// <summary>사용자 조작(회전/줌)만 반영한다. 바운드는 유지한다.</summary>
        public void ApplyCameraTransform()
        {
            if (_preview == null) return;

            var camera = _preview.camera;
            camera.backgroundColor = Background;

            // extents.magnitude는 대각선이라 실제 반경보다 크다. 정사각 뷰포트 기준으로 보정한다.
            float radius = Mathf.Clamp(_framingBounds.extents.magnitude / Mathf.Sqrt(3f), 0.15f, MaxFramingRadius);
            float halfFov = Mathf.Deg2Rad * camera.fieldOfView * 0.5f;
            float distance = radius / Mathf.Max(Mathf.Tan(halfFov), 0.01f) * 1.15f * Mathf.Max(ZoomFactor, 0.05f);

            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            camera.transform.rotation = rotation;
            camera.transform.position = _framingBounds.center - rotation * Vector3.forward * distance;
            camera.nearClipPlane = Mathf.Max(0.01f, distance * 0.01f);
            camera.farClipPlane = distance * 20f;
        }

        /// <summary>
        /// 카메라가 잡을 바운드를 구한다.
        /// ParticleSystemRenderer.bounds는 실제 파티클보다 훨씬 크게 보고되는 경우가 많아
        /// (방출될 수 있는 최대 영역을 포함한다) 그대로 쓰면 이펙트가 화면에서 점처럼 작아진다.
        /// 그래서 살아 있는 파티클의 실제 좌표로 직접 계산한다.
        /// </summary>
        private Bounds CalculateBounds()
        {
            _positionBuffer.Clear();
            _sizeBuffer.Clear();
            float largestParticle = 0f;

            foreach (var system in _allSystems)
            {
                if (system == null) continue;

                int count = system.particleCount;
                if (count == 0) continue;

                EnsureParticleBuffer(count);
                int read = system.GetParticles(_particleBuffer, count);

                // Local 시뮬레이션 공간의 파티클 좌표는 시스템 로컬 기준이라 월드로 변환해야 한다
                bool isLocalSpace = system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                var systemTransform = system.transform;

                for (int i = 0; i < read; i++)
                {
                    var particle = _particleBuffer[i];
                    var position = isLocalSpace
                        ? systemTransform.TransformPoint(particle.position)
                        : particle.position;

                    float size = particle.GetCurrentSize(system);
                    float alpha = particle.GetCurrentColor(system).a / 255f;

                    _positionBuffer.Add(position);

                    // 거의 투명한 파티클이 화각을 넓히지 않도록 알파를 가중치에 반영한다
                    _sizeBuffer.Add(Mathf.Max(size * alpha, 0.0001f));
                    largestParticle = Mathf.Max(largestParticle, size);
                }
            }

            if (_positionBuffer.Count == 0)
            {
                // 파티클이 없는 이펙트(메시/트레일/라인 전용)는 렌더러 바운드로 대체한다
                return TryGetNonParticleBounds(out var fallback)
                    ? ClampRadius(fallback)
                    : new Bounds(Vector3.zero, Vector3.one * 2f);
            }

            // 시각적 무게중심 — 크고 진한 파티클(밝은 코어)이 중심을 정하도록 가중한다
            var center = Vector3.zero;
            float totalWeight = 0f;

            for (int i = 0; i < _positionBuffer.Count; i++)
            {
                float weight = _sizeBuffer[i];
                center += _positionBuffer[i] * weight;
                totalWeight += weight;
            }
            center /= totalWeight;

            // 거리-크기 쌍을 거리순으로 정렬해 시각적 질량의 대부분이 담기는 반경을 찾는다.
            // 단순 백분위를 쓰면 멀리 흩어진 작은 스파크 몇 개가 화각을 넓혀
            // 정작 봐야 할 본체가 화면에서 작아진다.
            _weightBuffer.Clear();
            for (int i = 0; i < _positionBuffer.Count; i++)
            {
                _weightBuffer.Add(new Vector2((_positionBuffer[i] - center).magnitude, _sizeBuffer[i]));
            }
            _weightBuffer.Sort(CompareByDistance);

            float target = totalWeight * VisualMassRatio;
            float accumulated = 0f;
            float radius = 0f;
            float includedMaxSize = 0f;

            foreach (var entry in _weightBuffer)
            {
                accumulated += entry.y;
                radius = entry.x;
                includedMaxSize = Mathf.Max(includedMaxSize, entry.y);

                if (accumulated >= target) break;
            }

            // 파티클 한 장이 통째로 본체인 경우(단일 대형 스프라이트)도 담기도록 하한을 둔다
            radius = Mathf.Max(radius + includedMaxSize * 0.5f, largestParticle * 0.6f);
            radius = Mathf.Max(radius, 0.15f);

            var bounds = new Bounds(center, Vector3.one * radius * 2f);

            // 트레일이나 메시 파트가 함께 있으면 그것도 화면에 들어와야 한다
            if (TryGetNonParticleBounds(out var extra))
            {
                bounds.Encapsulate(extra);
            }

            return ClampRadius(bounds);
        }

        /// <summary>파티클을 제외한 렌더러(메시/트레일/라인)들의 합집합 바운드</summary>
        private bool TryGetNonParticleBounds(out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool found = false;

            foreach (var renderer in _instance.GetComponentsInChildren<Renderer>(false))
            {
                if (renderer == null || !renderer.enabled) continue;
                if (renderer is ParticleSystemRenderer) continue;

                var rendererBounds = renderer.bounds;
                if (rendererBounds.size.sqrMagnitude <= 1e-6f) continue;

                if (!found)
                {
                    bounds = rendererBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }

            return found;
        }

        /// <summary>바운드가 폭주하면 피사체가 점처럼 보이므로 상한을 둔다.</summary>
        private static Bounds ClampRadius(Bounds bounds)
        {
            var extents = bounds.extents;
            return extents.magnitude > MaxFramingRadius
                ? new Bounds(bounds.center, extents.normalized * MaxFramingRadius * 2f)
                : bounds;
        }

        /// <summary>파티클 읽기 버퍼가 충분한 크기를 갖도록 보장한다.</summary>
        private void EnsureParticleBuffer(int required)
        {
            if (_particleBuffer.Length >= required) return;

            int size = Mathf.NextPowerOfTwo(required);
            _particleBuffer = new ParticleSystem.Particle[size];
        }

        #endregion

        #region 렌더링

        /// <summary>
        /// 실시간 프리뷰용 렌더. 반환 텍스처는 다음 렌더까지만 유효하다.
        /// </summary>
        public Texture RenderLive(Rect rect)
        {
            if (_instance == null || _preview == null) return null;
            if (rect.width < 1f || rect.height < 1f) return null;

            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.camera.Render();
            return _preview.EndPreview();
        }

        /// <summary>
        /// 썸네일용 정지 렌더. 반환된 Texture2D는 호출자가 파괴해야 한다.
        /// </summary>
        public Texture2D RenderStatic(int size)
        {
            if (_instance == null || _preview == null) return null;

            _preview.BeginStaticPreview(new Rect(0f, 0f, size, size));
            _preview.camera.Render();
            return _preview.EndStaticPreview();
        }

        #endregion

        #region 정리

        public void Dispose()
        {
            ClearTarget();

            if (_preview != null)
            {
                _preview.Cleanup();
                _preview = null;
            }
        }

        #endregion
    }
}
