using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PX
{
    /// <summary>
    /// 사망 파쇄 연출을 전투 없이 바로 보고 조절하는 데모 (ShatterDemoScene 전용).
    ///
    /// 실제 런타임 경로(MonsterShatterRunner)는 UCharacterActor / 전투 매니저에 묶여 있어
    /// 데모 씬에서 그대로 쓸 수 없다. 그래서 수명 관리만 여기서 따로 하고,
    /// 눈에 보이는 계산은 전투와 똑같이 <see cref="MonsterShatterState"/> 를 쓴다.
    /// 여기서 맞춘 값은 [Config에 저장]으로 그대로 전투에 반영된다.
    ///
    /// 사용법: 씬을 열고 Play → 화면 왼쪽 패널에서 몬스터 선택·재생·파라미터 조절.
    /// </summary>
    public class MonsterShatterDemo : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("비워 두면 에디터에서 몬스터 프리팹 폴더 전체를 자동으로 읽어 온다")]
        [SerializeField] private GameObject[] monsterPrefabs;

        [SerializeField] private Transform spawnPoint;
        [SerializeField] private Camera demoCamera;

        [Header("파쇄 파라미터 (Play 중 UI 로 조절)")]
        [SerializeField] private float duration = 1.6f;
        [SerializeField] private float upForce = 6f;
        [SerializeField] private float spread = 1.8f;
        [SerializeField] private float gravity = 14f;
        [SerializeField] private float sizeReference = 2f;
        [SerializeField] private float spin = 4.5f;
        [SerializeField] private float rollKeep = 0.35f;
        [SerializeField] private float rollDamp = 0.4f;
        [SerializeField] private float landBounce = 0.5f;
        [SerializeField] private float groundOffset = 0f;
        [SerializeField] private float fadeStart = 0.72f;

        [Header("조각 날리기 (전투에서는 공격 지점 반대편 — 데모에서는 몬스터 뒤쪽으로 날린다)")]
        [SerializeField] private bool knockbackEnabled = true;
        [SerializeField] private float knockbackForceMin = 1.8f;
        [SerializeField] private float knockbackForceMax = 3.2f;
        [SerializeField] private float knockbackUpwardMin = 0.7f;
        [SerializeField] private float knockbackUpwardMax = 1.3f;
        [SerializeField] private float knockbackAngleSpread = 15f;

        [Header("조각 수 랜덤 (맞닿은 조각끼리 묶어 사망마다 덩어리 수를 다르게 한다)")]
        [SerializeField] private bool pieceRandomEnabled = true;
        [SerializeField] private float pieceRatioMin = 0f;
        [SerializeField] private float pieceRatioMax = 1f;

        [Header("재생")]
        [SerializeField] private bool autoLoop = true;
        [SerializeField] private float loopInterval = 0.8f;

        [Header("카메라")]
        [SerializeField] private bool autoRotate = true;
        [SerializeField] private float rotateSpeed = 25f;
        [SerializeField] private float cameraDistance = 2.4f;
        [SerializeField] private float cameraHeight = 0.45f;

        [Header("바닥")]
        [Tooltip("조각이 어디에 떨어져 구르는지 보이도록 바닥 평면을 만들어 준다")]
        [SerializeField] private bool showGround = true;

        [Header("조작 UI")]
        [Tooltip("조작 패널 확대 배율. 0이면 화면 세로 720px 를 1배로 보고 자동으로 정한다")]
        [SerializeField] private float uiScale = 0f;

        private int _index;
        private GameObject _instance;
        private GameObject _ground;

        private readonly MonsterShatterState _state = new MonsterShatterState();

        private Vector2 _panelScroll;
        private float _scale = 1f;
        private float _panelContentHeight = 640f;

        private bool _shattering;
        private bool _manualScrub;
        private float _elapsed;
        private float _progress;
        private float _restTimer;
        private float _cameraYaw;
        private Bounds _bounds;

        private string _status = "";
        private string _partSummary = "";
        private int _lastTriangleCount;

        //현재 몬스터를 가리키는 규칙 키워드 (첫 스킨 메시 이름). 이 이름이 Config 의 개별 설정 키가 된다
        private string _overrideKey = "";

        //이 몬스터의 목표 파트 수 (0 = 자동)
        private int _partTarget;

        private void Start()
        {
            EnsureRefs();
            ResolvePrefabs();

            //씬에 저장된 값이 아니라 Config 값으로 시작한다. 데모에서 보는 그림이
            //항상 실제 전투와 같아야 하고, 여기서 조절하다 저장 안 하고 나간 값이
            //씬 파일에 남아 다음에 헷갈리는 것도 막는다. (_overrideKey 는 Spawn 이 다시 읽는다)
            LoadFromConfig();

            BuildGround();
            Spawn();
        }

        private void OnDestroy()
        {
            CleanupInstance();
            MonsterShatterState.ClearMaterialCache();
            MonsterShatterMeshCache.ClearAll();
        }

        private void EnsureRefs()
        {
            if (demoCamera == null)
                demoCamera = Camera.main;

            if (spawnPoint == null)
                spawnPoint = transform;
        }

        private void ResolvePrefabs()
        {
            if (monsterPrefabs != null && monsterPrefabs.Length > 0)
                return;

#if UNITY_EDITOR
            //인스펙터에 아무것도 안 넣어도 바로 쓸 수 있게 몬스터 프리팹을 전부 긁어 온다.
            //데모 씬 전용이라 에디터 API 를 써도 무방하다
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/GameAssets/Characters/Prefabs/Monster" });
            var list = new List<GameObject>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                    list.Add(go);
            }

            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            monsterPrefabs = list.ToArray();
#endif

            if (monsterPrefabs == null || monsterPrefabs.Length == 0)
                _status = "몬스터 프리팹이 없다. 인스펙터 monsterPrefabs 에 직접 넣어야 한다";
        }

        /// <summary>조각이 떨어지는 지면을 눈으로 확인할 수 있게 평면을 깐다.</summary>
        private void BuildGround()
        {
            if (showGround == false || _ground != null)
                return;

            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.name = "[Demo] Ground";
            _ground.transform.position = new Vector3(spawnPoint.position.x, spawnPoint.position.y, spawnPoint.position.z);
            _ground.transform.localScale = Vector3.one * 2f;

            var collider = _ground.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = _ground.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(0.22f, 0.23f, 0.27f);
        }

        // ------------------------------------------------------------------
        // 스폰 / 정리

        private void Spawn()
        {
            CleanupInstance();

            if (monsterPrefabs == null || monsterPrefabs.Length == 0)
                return;

            _index = Mathf.Clamp(_index, 0, monsterPrefabs.Length - 1);
            GameObject prefab = monsterPrefabs[_index];
            if (prefab == null)
                return;

            _instance = Instantiate(prefab, spawnPoint.position, Quaternion.identity);
            _instance.name = "[Demo] " + prefab.name;

            _lastTriangleCount = 0;
            bool boundsInit = false;

            var found = _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < found.Length; i++)
            {
                SkinnedMeshRenderer smr = found[i];
                if (smr.sharedMesh == null)
                    continue;

                for (int s = 0; s < smr.sharedMesh.subMeshCount; s++)
                    _lastTriangleCount += (int)(smr.sharedMesh.GetIndexCount(s) / 3);

                if (!boundsInit) { _bounds = smr.bounds; boundsInit = true; }
                else _bounds.Encapsulate(smr.bounds);
            }

            if (!boundsInit)
                _bounds = new Bounds(spawnPoint.position, Vector3.one);

            //개별 설정은 메시 이름으로 건다. 같은 메시를 일반/보스/층보스 프리팹이 공유하므로
            //메시 이름으로 걸어야 한 번 맞춰 놓은 값이 세 프리팹 전부에 걸린다
            _overrideKey = found.Length > 0 && found[0].sharedMesh != null ? found[0].sharedMesh.name : prefab.name;
            _partTarget = ReadPartOverride(_overrideKey);

            _shattering = false;
            _manualScrub = false;
            _progress = 0f;
            _elapsed = 0f;
            _restTimer = 0f;
            _partSummary = "";

            FrameCamera();
            _status = prefab.name + " / " + found.Length + "렌더러 " + _lastTriangleCount + "tri";
        }

        private void CleanupInstance()
        {
            RestoreInstance();

            if (_instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }
        }

        // ------------------------------------------------------------------
        // 파쇄 시작 / 원복

        private MonsterShatterTuning BuildTuning()
        {
            return new MonsterShatterTuning
            {
                Duration = duration,
                UpForce = upForce,
                Spread = spread,
                Gravity = gravity,
                Spin = spin,
                RollKeep = rollKeep,
                RollDamp = rollDamp,
                LandBounce = landBounce,
                GroundOffset = groundOffset,
                FadeStart = fadeStart,
                SizeReference = sizeReference,
                KnockbackEnabled = knockbackEnabled,
                KnockbackForceMin = knockbackForceMin,
                KnockbackForceMax = knockbackForceMax,
                KnockbackUpwardMin = knockbackUpwardMin,
                KnockbackUpwardMax = knockbackUpwardMax,
                KnockbackAngleSpread = knockbackAngleSpread,
                PieceRandomEnabled = pieceRandomEnabled,
                PieceRatioMin = pieceRatioMin,
                PieceRatioMax = pieceRatioMax,
            };
        }

        private void BeginShatter()
        {
            if (_instance == null)
                return;

            //프리팹 루트가 발밑 기준이므로 스폰 지점의 y 가 곧 지면이다
            MonsterShatterTuning tuning = BuildTuning();
            float groundY = spawnPoint.position.y + tuning.GroundOffset;

            Transform meshArea = _instance.transform.Find("MeshArea");
            if (meshArea == null)
                meshArea = _instance.transform;

            //전투에서는 공격 지점에서 방향을 잡지만 데모에는 공격자가 없다.
            //몬스터가 바라보는 반대쪽 = 실전에서 정면의 플레이어에게 맞고 밀려나는 방향과 같다
            Vector3 knockbackDir = -_instance.transform.forward;

            if (_state.Setup(meshArea, groundY, tuning, knockbackDir) == false)
            {
                _status = "분해 메시를 만들지 못했다 (메시 읽기 불가 / 본 없음?)";
                return;
            }

            //현재 포즈에서 터뜨린다
            var animator = _instance.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.speed = 0f;

            _shattering = true;
            _manualScrub = false;
            _elapsed = 0f;
            _progress = 0f;
            _partSummary = _state.DescribeParts();

            _status = monsterPrefabs[_index].name + "\n"
                      + _lastTriangleCount + "tri → " + _state.PartCount + "파트 / "
                      + _state.VertexCount + "v / " + (_state.MeshBytes / 1024) + "KB\n"
                      + _partSummary;
        }

        private void RestoreInstance()
        {
            _state.Restore();

            if (_instance != null)
            {
                var animator = _instance.GetComponentInChildren<Animator>();
                if (animator != null)
                    animator.speed = 1f;
            }

            _shattering = false;
        }

        // ------------------------------------------------------------------
        // 갱신

        private void Update()
        {
            UpdateCamera();

            if (_shattering == false)
            {
                //반복 재생 대기
                if (autoLoop && _instance != null)
                {
                    _restTimer += Time.deltaTime;
                    if (_restTimer >= loopInterval)
                    {
                        _restTimer = 0f;
                        BeginShatter();
                    }
                }
                return;
            }

            if (_manualScrub == false)
            {
                _elapsed += Time.deltaTime;
                _progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, duration));
            }

            _state.Apply(_elapsed, _progress, BuildTuning());

            if (_manualScrub == false && _progress >= 1f)
            {
                RestoreInstance();
                _restTimer = 0f;

                if (autoLoop == false)
                    _status = "재생 완료 — [재생]으로 다시";
            }
        }

        private void FrameCamera()
        {
            if (demoCamera == null)
                return;

            _cameraYaw = 180f;
            PlaceCamera(Mathf.Max(_bounds.size.magnitude, 1f));
        }

        private void UpdateCamera()
        {
            if (demoCamera == null)
                return;

            if (autoRotate)
                _cameraYaw += rotateSpeed * Time.deltaTime;

            PlaceCamera(Mathf.Max(_bounds.size.magnitude, 1f));
        }

        private void PlaceCamera(float radius)
        {
            Vector3 center = _bounds.center;
            Quaternion rot = Quaternion.Euler(0, _cameraYaw, 0);
            Vector3 offset = rot * new Vector3(0, radius * cameraHeight, -radius * cameraDistance);

            demoCamera.transform.position = center + offset;
            demoCamera.transform.LookAt(center);
        }

        // ------------------------------------------------------------------
        // 조작 UI

        private void OnGUI()
        {
            //GUI.matrix 로 통째로 확대하면 12px 로 구워진 폰트 아틀라스를 비트맵째 늘이는 셈이라
            //글자가 뭉개진다. 스타일의 fontSize 를 올려 그 크기로 새로 렌더링시켜야 선명하다
            float scale = uiScale > 0f ? uiScale : Mathf.Max(1f, Screen.height / 720f);
            _scale = scale;

            GUISkin prevSkin = GUI.skin;
            GUI.skin = ScaledSkin(scale);

            //패널을 화면 끝까지 늘이면 아래가 텅 빈다. 지난 프레임에 잰 실제 내용 높이에 맞춘다
            float maxHeight = Screen.height - 20f;
            float panelHeight = Mathf.Min(_panelContentHeight + 16f * scale, maxHeight);
            GUILayout.BeginArea(new Rect(10, 10, 330f * scale, panelHeight), GUI.skin.box);

            //배율을 키우면 내용이 화면을 넘칠 수 있어 스크롤을 둔다 (넘치지 않으면 막대가 안 보인다)
            _panelScroll = GUILayout.BeginScrollView(_panelScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            GUILayout.Label("<b>몬스터 사망 파쇄 데모</b>", RichLabel());

            //대상 선택
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(34f * _scale)))
            {
                _index = (_index - 1 + monsterPrefabs.Length) % monsterPrefabs.Length;
                Spawn();
            }

            string current = (monsterPrefabs != null && monsterPrefabs.Length > 0 && monsterPrefabs[_index] != null)
                ? monsterPrefabs[_index].name
                : "(없음)";
            GUILayout.Label(current, GUILayout.ExpandWidth(true));

            if (GUILayout.Button("▶", GUILayout.Width(34f * _scale)))
            {
                _index = (_index + 1) % monsterPrefabs.Length;
                Spawn();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(_index + 1 + " / " + (monsterPrefabs != null ? monsterPrefabs.Length : 0));

            //재생
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("재생", GUILayout.Height(28f * _scale)))
            {
                RestoreInstance();
                BeginShatter();
            }
            if (GUILayout.Button("리셋", GUILayout.Height(28f * _scale)))
            {
                Spawn();
            }
            GUILayout.EndHorizontal();

            autoLoop = GUILayout.Toggle(autoLoop, " 자동 반복");
            if (autoLoop)
                loopInterval = LabeledSlider("반복 간격", loopInterval, 0.1f, 3f, "초");

            //진행도 스크럽 — 궤적이 닫힌 수식이라 임의 시점을 바로 그릴 수 있다
            GUILayout.Space(6);
            GUILayout.Label("진행도 " + _progress.ToString("F2") + "  (" + _elapsed.ToString("F2") + "초)");
            float newProgress = GUILayout.HorizontalSlider(_progress, 0f, 1f);
            if (Mathf.Abs(newProgress - _progress) > 0.0001f)
            {
                if (_shattering == false)
                    BeginShatter();

                _manualScrub = true;
                _progress = newProgress;
                _elapsed = _progress * duration;
            }

            if (_manualScrub && GUILayout.Button("수동 정지 해제"))
                _manualScrub = false;

            //이 몬스터만의 조각 굵기 — 리그마다 갈리는 개수가 달라 개별 조정이 필요하다.
            //분해 메시를 굽는 시점에 확정되는 값이라 바꿀 때마다 캐시를 비우고 다시 굽는다
            GUILayout.Space(8);
            GUILayout.Label("<b>이 몬스터</b>", RichLabel());
            GUILayout.Label("규칙 키워드: " + _overrideKey);

            GUILayout.BeginHorizontal();
            GUILayout.Label("파트 수", GUILayout.Width(90f * _scale));
            int slider = Mathf.RoundToInt(GUILayout.HorizontalSlider(_partTarget, 0f, 15f, GUILayout.ExpandWidth(true)));
            GUILayout.Label(_partTarget <= 0 ? "자동" : _partTarget.ToString(), GUILayout.Width(56f * _scale));
            GUILayout.EndHorizontal();

            //2개 미만은 의미가 없으므로 자동으로 되돌린다
            int target = slider < 2 ? 0 : slider;
            if (target != _partTarget)
                ApplyPartTarget(target);

            DeathSettings death = GetDeath();
            if (death != null)
            {
                bool fill = GUILayout.Toggle(death.shatterFillCuts, " 잘린 단면 채우기");
                if (fill != death.shatterFillCuts)
                {
                    death.shatterFillCuts = fill;
                    RebakeMeshes();
                }
            }

            //파라미터
            GUILayout.Space(8);
            GUILayout.Label("<b>파라미터</b>", RichLabel());

            duration = LabeledSlider("지속시간", duration, 0.3f, 10f, "초");
            upForce = LabeledSlider("솟구치는 힘", upForce, 0f, 12f, "");
            spread = LabeledSlider("퍼지는 힘", spread, 0f, 6f, "");
            gravity = LabeledSlider("중력", gravity, 1f, 40f, "");
            sizeReference = LabeledSlider("기준 몬스터 키", sizeReference, 0f, 6f, "m");
            spin = LabeledSlider("회전 속도", spin, 0f, 20f, "");
            rollKeep = LabeledSlider("착지 미끄러짐", rollKeep, 0f, 1f, "");
            rollDamp = LabeledSlider("구르기 감쇠", rollDamp, 0.05f, 2f, "초");
            landBounce = LabeledSlider("착지 반동", landBounce, 0f, 2f, "");
            groundOffset = LabeledSlider("지면 보정", groundOffset, -0.5f, 0.5f, "m");
            fadeStart = LabeledSlider("소멸 시작", fadeStart, 0f, 1f, "");

            //조각 날리기
            GUILayout.Space(8);
            GUILayout.Label("<b>조각 날리기</b>", RichLabel());
            knockbackEnabled = GUILayout.Toggle(knockbackEnabled, " 사용 (몬스터 뒤쪽으로)");

            if (knockbackEnabled)
            {
                //수평·수직 모두 조각마다 min~max 에서 따로 뽑힌다. 최소=최대면 전부 같은 속도로 밀린다
                knockbackForceMin = LabeledSlider("날아가는 힘 최소", knockbackForceMin, 0f, 10f, "");
                knockbackForceMax = LabeledSlider("날아가는 힘 최대", knockbackForceMax, 0f, 10f, "");
                knockbackUpwardMin = LabeledSlider("뜨는 힘 최소", knockbackUpwardMin, 0f, 6f, "");
                knockbackUpwardMax = LabeledSlider("뜨는 힘 최대", knockbackUpwardMax, 0f, 6f, "");
                knockbackAngleSpread = LabeledSlider("방향 산포", knockbackAngleSpread, 0f, 90f, "도");
            }

            //조각 수 랜덤 — 갈린 조각을 몇 개씩 묶을지. 위 '파트 수' 가 상한이라 그보다 많아지지는 않는다
            GUILayout.Space(8);
            GUILayout.Label("<b>조각 수 랜덤</b>", RichLabel());
            pieceRandomEnabled = GUILayout.Toggle(pieceRandomEnabled, " 사용 (사망마다 다르게)");

            if (pieceRandomEnabled)
            {
                //갈린 조각 수에 곱해지는 비율이다. 0 이면 통째로 한 덩어리, 1 이면 갈린 그대로 전부 흩어진다
                pieceRatioMin = LabeledSlider("조각 비율 최소", pieceRatioMin, 0f, 1f, "");
                pieceRatioMax = LabeledSlider("조각 비율 최대", pieceRatioMax, 0f, 1f, "");

                //비율은 '갈린 조각 수'에 곱해지므로, 이 몬스터에서 실제로 몇 조각이 나오는지 같이 보여준다
                int parts = _state != null ? _state.MaxPartCount : 0;
                if (parts > 0)
                {
                    int lo = Mathf.Clamp(Mathf.RoundToInt(parts * Mathf.Min(pieceRatioMin, pieceRatioMax)), 1, parts);
                    int hi = Mathf.Clamp(Mathf.RoundToInt(parts * Mathf.Max(pieceRatioMin, pieceRatioMax)), 1, parts);
                    GUILayout.Label($"{parts}조각으로 갈리는 몬스터 → {lo}~{hi}덩어리 (이번 사망은 {_state.PartCount})");
                }
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Config 불러오기"))
                LoadFromConfig();
            if (GUILayout.Button("Config에 저장"))
                SaveToConfig();
            GUILayout.EndHorizontal();

            //카메라
            GUILayout.Space(8);
            GUILayout.Label("<b>카메라</b>", RichLabel());
            autoRotate = GUILayout.Toggle(autoRotate, " 자동 회전");
            cameraDistance = LabeledSlider("거리", cameraDistance, 0.6f, 6f, "");
            cameraHeight = LabeledSlider("높이", cameraHeight, -0.5f, 1.5f, "");

            GUILayout.Space(8);
            GUILayout.Label("<b>화면</b>", RichLabel());
            uiScale = LabeledSlider("UI 배율", scale, 1f, 4f, "배");

            GUILayout.Space(8);
            GUILayout.Label(_status, RichLabel());

            //마지막 컨트롤의 아래끝이 곧 내용 높이다. 레이아웃이 확정되는 Repaint 에서만 잰다
            if (Event.current.type == EventType.Repaint)
                _panelContentHeight = GUILayoutUtility.GetLastRect().yMax;

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUI.skin = prevSkin;
        }

        private static GUIStyle _richLabel;
        private static GUISkin _scaledSkin;
        private static float _scaledSkinScale = -1f;

        /// <summary>
        /// 기본 스킨을 복제해 글자·위젯 크기를 배율만큼 키운 스킨을 돌려준다.
        /// fontSize 를 직접 올리므로 폰트가 그 크기로 새로 렌더링되어 확대해도 선명하다.
        /// </summary>
        private static GUISkin ScaledSkin(float scale)
        {
            //드래그 중 매 프레임 새로 만들지 않도록 0.05 단위로 끊는다
            float quantized = Mathf.Round(scale * 20f) / 20f;
            if (_scaledSkin != null && Mathf.Approximately(_scaledSkinScale, quantized))
                return _scaledSkin;

            if (_scaledSkin != null)
                DestroyImmediate(_scaledSkin);

            GUISkin skin = Instantiate(GUI.skin);
            skin.hideFlags = HideFlags.HideAndDontSave;

            foreach (GUIStyle style in EnumerateStyles(skin))
            {
                if (style == null)
                    continue;

                //내장 스타일은 fontSize 가 0(폰트 기본값 사용)이라 12 를 기준으로 잡는다
                int baseSize = style.fontSize > 0 ? style.fontSize : 12;
                style.fontSize = Mathf.RoundToInt(baseSize * quantized);

                //슬라이더 트랙·손잡이·스크롤바는 고정 크기라 따로 키워야 한다.
                //border 는 건드리지 않는다 — 9-slice 원본이 늘어나 뭉개지기 때문
                if (style.fixedHeight > 0f)
                    style.fixedHeight *= quantized;
                if (style.fixedWidth > 0f)
                    style.fixedWidth *= quantized;
            }

            _scaledSkin = skin;
            _scaledSkinScale = quantized;
            _richLabel = null;   //스킨이 바뀌면 여기서 파생된 스타일도 다시 만든다
            return skin;
        }

        private static System.Collections.Generic.IEnumerable<GUIStyle> EnumerateStyles(GUISkin skin)
        {
            yield return skin.box;
            yield return skin.button;
            yield return skin.toggle;
            yield return skin.label;
            yield return skin.textField;
            yield return skin.textArea;
            yield return skin.window;
            yield return skin.horizontalSlider;
            yield return skin.horizontalSliderThumb;
            yield return skin.verticalSlider;
            yield return skin.verticalSliderThumb;
            yield return skin.horizontalScrollbar;
            yield return skin.horizontalScrollbarThumb;
            yield return skin.horizontalScrollbarLeftButton;
            yield return skin.horizontalScrollbarRightButton;
            yield return skin.verticalScrollbar;
            yield return skin.verticalScrollbarThumb;
            yield return skin.verticalScrollbarUpButton;
            yield return skin.verticalScrollbarDownButton;
            yield return skin.scrollView;

            if (skin.customStyles == null)
                yield break;

            for (int i = 0; i < skin.customStyles.Length; i++)
                yield return skin.customStyles[i];
        }

        private static GUIStyle RichLabel()
        {
            if (_richLabel == null)
                _richLabel = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

            return _richLabel;
        }

        private float LabeledSlider(string label, float value, float min, float max, string suffix)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90f * _scale));
            float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(result.ToString("F2") + suffix, GUILayout.Width(56f * _scale));
            GUILayout.EndHorizontal();
            return result;
        }

        private static DeathSettings GetDeath()
        {
            return GameClientPlayConfig.Instance != null ? GameClientPlayConfig.Instance.death : null;
        }

        // ------------------------------------------------------------------
        // 몬스터별 개별 설정
        //
        // 굽는 시점에 확정되는 값들이라, 바꾸면 캐시를 통째로 비우고 다시 구워야 화면에 반영된다.
        // 값은 Config 인스턴스에 바로 쓴다 (플레이 중이라도 같은 ScriptableObject 라서 즉시 먹는다).
        // 디스크에 남기려면 아래 [Config에 저장]을 눌러야 한다.

        private int ReadPartOverride(string key)
        {
            DeathSettings death = GetDeath();
            if (death == null || death.shatterPartOverrides == null || string.IsNullOrEmpty(key))
                return 0;

            string normalized = MonsterShatterMeshCache.NormalizeName(key);

            for (int i = 0; i < death.shatterPartOverrides.Count; i++)
            {
                DeathSettings.ShatterPartOverride rule = death.shatterPartOverrides[i];
                if (rule != null && MonsterShatterMeshCache.NormalizeName(rule.nameKeyword) == normalized)
                    return rule.targetParts;
            }

            return 0;
        }

        private void ApplyPartTarget(int target)
        {
            _partTarget = target;

            DeathSettings death = GetDeath();
            if (death == null || string.IsNullOrEmpty(_overrideKey))
                return;

            if (death.shatterPartOverrides == null)
                death.shatterPartOverrides = new List<DeathSettings.ShatterPartOverride>();

            string normalized = MonsterShatterMeshCache.NormalizeName(_overrideKey);
            int found = -1;

            for (int i = 0; i < death.shatterPartOverrides.Count; i++)
            {
                DeathSettings.ShatterPartOverride rule = death.shatterPartOverrides[i];
                if (rule != null && MonsterShatterMeshCache.NormalizeName(rule.nameKeyword) == normalized)
                {
                    found = i;
                    break;
                }
            }

            if (target <= 0)
            {
                //자동으로 되돌리면 규칙 자체를 지운다. 남겨 두면 Config 목록에 의미 없는 줄이 쌓인다
                if (found >= 0)
                    death.shatterPartOverrides.RemoveAt(found);
            }
            else if (found >= 0)
            {
                death.shatterPartOverrides[found].targetParts = target;
            }
            else
            {
                death.shatterPartOverrides.Add(new DeathSettings.ShatterPartOverride
                {
                    nameKeyword = _overrideKey,
                    targetParts = target,
                });
            }

            RebakeMeshes();
        }

        /// <summary>분해 메시를 버리고 다시 굽는다. 원복을 먼저 해야 파괴된 메시를 참조하지 않는다.</summary>
        private void RebakeMeshes()
        {
            RestoreInstance();
            MonsterShatterMeshCache.ClearAll();
            BeginShatter();
        }

        private void LoadFromConfig()
        {
            DeathSettings death = GetDeath();
            if (death == null)
            {
                _status = "GameClientPlayConfig 를 찾을 수 없다";
                return;
            }

            duration = death.shatterDuration;
            upForce = death.shatterUpForce;
            spread = death.shatterSpread;
            gravity = death.shatterGravity;
            sizeReference = death.shatterSizeReference;
            spin = death.shatterSpin;
            rollKeep = death.shatterRollKeep;
            rollDamp = death.shatterRollDamp;
            landBounce = death.shatterLandBounce;
            groundOffset = death.shatterGroundOffset;
            fadeStart = death.shatterFadeStart;

            knockbackEnabled = death.shatterKnockbackEnabled;
            knockbackForceMin = death.shatterKnockbackForceMin;
            knockbackForceMax = death.shatterKnockbackForceMax;
            knockbackUpwardMin = death.shatterKnockbackUpwardMin;
            knockbackUpwardMax = death.shatterKnockbackUpwardMax;
            knockbackAngleSpread = death.shatterKnockbackAngleSpread;
            pieceRandomEnabled = death.shatterPieceRandomEnabled;
            pieceRatioMin = death.shatterPieceRatioMin;
            pieceRatioMax = death.shatterPieceRatioMax;

            _partTarget = ReadPartOverride(_overrideKey);

            _status = "Config 값을 불러왔다";
        }

        private void SaveToConfig()
        {
            GameClientPlayConfig config = GameClientPlayConfig.Instance;
            DeathSettings death = config != null ? config.death : null;
            if (death == null)
            {
                _status = "GameClientPlayConfig 를 찾을 수 없다";
                return;
            }

            death.shatterDuration = duration;
            death.shatterUpForce = upForce;
            death.shatterSpread = spread;
            death.shatterGravity = gravity;
            death.shatterSizeReference = sizeReference;
            death.shatterSpin = spin;
            death.shatterRollKeep = rollKeep;
            death.shatterRollDamp = rollDamp;
            death.shatterLandBounce = landBounce;
            death.shatterGroundOffset = groundOffset;
            death.shatterFadeStart = fadeStart;

            death.shatterKnockbackEnabled = knockbackEnabled;
            death.shatterKnockbackForceMin = knockbackForceMin;
            death.shatterKnockbackForceMax = knockbackForceMax;
            death.shatterKnockbackUpwardMin = knockbackUpwardMin;
            death.shatterKnockbackUpwardMax = knockbackUpwardMax;
            death.shatterKnockbackAngleSpread = knockbackAngleSpread;
            death.shatterPieceRandomEnabled = pieceRandomEnabled;
            death.shatterPieceRatioMin = pieceRatioMin;
            death.shatterPieceRatioMax = pieceRatioMax;

#if UNITY_EDITOR
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            _status = "GameClientPlayConfig.asset 에 저장했다";
#else
            _status = "에디터에서만 저장할 수 있다";
#endif
        }
    }
}
