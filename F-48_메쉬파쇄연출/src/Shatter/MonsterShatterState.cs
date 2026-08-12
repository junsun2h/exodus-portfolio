using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace PX
{
    /// <summary>파쇄 연출 파라미터. Config 값을 그대로 담아 셰이더로 넘긴다.</summary>
    public struct MonsterShatterTuning
    {
        public float Duration;
        public float UpForce;
        public float Spread;
        public float Gravity;
        public float Spin;
        public float RollKeep;
        public float RollDamp;
        public float LandBounce;
        public float GroundOffset;
        public float FadeStart;
        public float SizeReference;

        //조각 날리기 — 파쇄 조각 전체에 공통으로 실리는 넉백 (물리 넉백과 달리 셰이더 궤적에 합쳐진다).
        //방향은 한 마리에 하나, 세기는 조각마다 min~max 에서 따로 뽑는다
        public bool KnockbackEnabled;
        public float KnockbackForceMin;
        public float KnockbackForceMax;
        public float KnockbackUpwardMin;
        public float KnockbackUpwardMax;
        public float KnockbackAngleSpread;

        //조각 수 랜덤 — 죽는 몬스터마다 맞닿은 조각을 몇 개씩 묶어 덩어리 수를 다르게 한다.
        //메시는 그대로 두고 궤적 상수만 공유시키는 방식이라 개수를 줄이는 쪽으로만 움직인다.
        //개수가 아니라 비율인 이유는 몬스터마다 갈리는 조각이 4~13개로 제각각이기 때문이다
        public bool PieceRandomEnabled;
        public float PieceRatioMin;
        public float PieceRatioMax;

        public static MonsterShatterTuning Default => new MonsterShatterTuning
        {
            Duration = 1.6f,
            UpForce = 6f,
            Spread = 1.8f,
            Gravity = 14f,
            Spin = 4.5f,
            RollKeep = 0.35f,
            RollDamp = 0.4f,
            LandBounce = 0.5f,
            GroundOffset = 0f,
            FadeStart = 0.72f,
            SizeReference = 2f,
            KnockbackEnabled = true,
            KnockbackForceMin = 1.8f,
            KnockbackForceMax = 3.2f,
            KnockbackUpwardMin = 0.7f,
            KnockbackUpwardMax = 1.3f,
            KnockbackAngleSpread = 15f,
            PieceRandomEnabled = true,
            PieceRatioMin = 0f,
            PieceRatioMax = 1f,
        };

        public static MonsterShatterTuning FromConfig(DeathSettings death)
        {
            if (death == null)
                return Default;

            return new MonsterShatterTuning
            {
                Duration = death.shatterDuration,
                UpForce = death.shatterUpForce,
                Spread = death.shatterSpread,
                Gravity = death.shatterGravity,
                Spin = death.shatterSpin,
                RollKeep = death.shatterRollKeep,
                RollDamp = death.shatterRollDamp,
                LandBounce = death.shatterLandBounce,
                GroundOffset = death.shatterGroundOffset,
                FadeStart = death.shatterFadeStart,
                SizeReference = death.shatterSizeReference,
                KnockbackEnabled = death.shatterKnockbackEnabled,
                KnockbackForceMin = death.shatterKnockbackForceMin,
                KnockbackForceMax = death.shatterKnockbackForceMax,
                KnockbackUpwardMin = death.shatterKnockbackUpwardMin,
                KnockbackUpwardMax = death.shatterKnockbackUpwardMax,
                KnockbackAngleSpread = death.shatterKnockbackAngleSpread,
                PieceRandomEnabled = death.shatterPieceRandomEnabled,
                PieceRatioMin = death.shatterPieceRatioMin,
                PieceRatioMax = death.shatterPieceRatioMax,
            };
        }

        /// <summary>수평 속도로 뽑힐 수 있는 최댓값. 최소·최대를 뒤집어 넣어도 같은 답이 나온다.</summary>
        public readonly float KnockbackForceUpperBound => Mathf.Max(KnockbackForceMin, KnockbackForceMax);

        /// <summary>
        /// 조각 날리기가 실제로 걸리는 설정인지. 힘이 전부 0이면 켜 봐야 아무 일도 없다.
        /// readonly 로 두는 건 이 구조체가 대부분 in 파라미터로 넘어가기 때문이다 (방어적 복사 방지).
        /// </summary>
        public readonly bool HasKnockback => KnockbackEnabled
                                             && (KnockbackForceUpperBound > 0.001f
                                                 || Mathf.Max(KnockbackUpwardMin, KnockbackUpwardMax) > 0.001f);
    }

    /// <summary>
    /// 파쇄 한 건의 실행 상태. 렌더러 교체 · 파트별 궤적 상수 계산 · 원복을 담당한다.
    ///
    /// 전투(<see cref="MonsterShatterRunner"/>)와 데모 씬(<see cref="MonsterShatterDemo"/>)이
    /// 이 클래스를 공유한다. 데모에서 맞춘 값이 전투에서 그대로 나와야 하므로,
    /// 눈에 보이는 계산은 전부 여기 한 곳에만 둔다. 양쪽의 차이는 수명 관리뿐이다.
    ///
    /// 핵심은 "사망 포즈에서의 파트 중심"을 CPU 에서 구해 셰이더 상수로 올리는 것이다.
    /// 파트는 최대 15개뿐이라 정점 수와 무관하게 본 행렬 몇 번만 곱하면 된다.
    /// </summary>
    public sealed class MonsterShatterState
    {
        private const string ShaderName = "PX/MonsterShatterURP";
        private const int PieceMax = 16;   //셰이더 배열 길이와 같아야 한다

        private static readonly int ShatterTimeID = Shader.PropertyToID("_ShatterTime");
        private static readonly int ShatterProgressID = Shader.PropertyToID("_ShatterProgress");
        private static readonly int GravityID = Shader.PropertyToID("_Gravity");
        private static readonly int GroundYID = Shader.PropertyToID("_GroundY");
        private static readonly int RollKeepID = Shader.PropertyToID("_RollKeep");
        private static readonly int RollDampID = Shader.PropertyToID("_RollDamp");
        private static readonly int LandBounceID = Shader.PropertyToID("_LandBounce");
        private static readonly int ForcePieceID = Shader.PropertyToID("_ForcePiece");
        private static readonly int PiecePivotID = Shader.PropertyToID("_PiecePivot");
        private static readonly int PieceVelID = Shader.PropertyToID("_PieceVel");
        private static readonly int PieceAxisID = Shader.PropertyToID("_PieceAxis");
        private static readonly int PieceLongID = Shader.PropertyToID("_PieceLong");
        private static readonly int BaseMapID = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        private static readonly int UseTintID = Shader.PropertyToID("_UseTint");
        private static readonly int TintMaskID = Shader.PropertyToID("_TintMask");
        private static readonly int TintAID = Shader.PropertyToID("_TintA");
        private static readonly int TintBID = Shader.PropertyToID("_TintB");
        private static readonly int TintCID = Shader.PropertyToID("_TintC");

        //원본 텍스처별 파쇄 머티리얼. 같은 텍스처를 쓰는 몬스터끼리 머티리얼을 공유한다
        private static readonly Dictionary<int, Material> _materialCache = new Dictionary<int, Material>();
        private static Shader _shader;

        //연출용 난수. UnityEngine.Random 을 쓰면 전투 난수 시퀀스를 건드려
        //스테이지 해시 동기화가 어긋날 수 있으므로 반드시 별도 인스턴스를 쓴다
        private static readonly System.Random _rand = new System.Random(20260802);

        private struct RendererEntry
        {
            public Renderer Renderer;
            public Mesh OriginalMesh;              //스킨 렌더러만
            public Material[] OriginalMaterials;
            public ShadowCastingMode OriginalShadow;
            public Bounds OriginalLocalBounds;     //스킨 렌더러만
            public bool Skinned;
        }

        private readonly List<RendererEntry> _entries = new List<RendererEntry>(8);

        //파트별 누적 (사망 포즈 기준 월드 좌표)
        private readonly Vector3[] _pivotSum = new Vector3[MonsterShatterParts.Count];
        private readonly float[] _pivotWeight = new float[MonsterShatterParts.Count];
        private readonly float[] _radius = new float[MonsterShatterParts.Count];
        private readonly bool[] _present = new bool[MonsterShatterParts.Count];

        //이름이 아니라 공간 분할로 만들어진 슬롯 (표시용 — 슬롯 이름에 의미가 없다)
        private readonly bool[] _chunk = new bool[MonsterShatterParts.Count];

        //조각 수 랜덤 — 이 슬롯이 어느 덩어리에 속하는지(자기 자신이면 그 덩어리의 대표).
        //대표만 궤적을 계산하고 나머지는 그 값을 그대로 복사해 한 몸으로 움직인다
        private readonly int[] _groupLeader = new int[MonsterShatterParts.Count];

        //덩어리 후보 목록 (병합 루프용 재사용 버퍼)
        private readonly List<int> _groupBuffer = new List<int>(MonsterShatterParts.Count);

        //착지 후 눕히기 — 장축(월드) + 보정 세기, 그리고 어느 렌더러 값을 채택할지 정하는 가중치
        private readonly Vector4[] _longAxis = new Vector4[MonsterShatterParts.Count];
        private readonly int[] _longWeight = new int[MonsterShatterParts.Count];

        //셰이더로 올릴 상수 배열
        private readonly Vector4[] _piecePivot = new Vector4[PieceMax];
        private readonly Vector4[] _pieceVel = new Vector4[PieceMax];
        private readonly Vector4[] _pieceAxis = new Vector4[PieceMax];
        private readonly Vector4[] _pieceLong = new Vector4[PieceMax];

        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _weaponBlock;
        private bool _hasWeaponRenderer;

        //몬스터 덩치 (파쇄 직전의 월드 바운즈)
        private Bounds _bodyBounds;
        private bool _bodyBoundsInit;

        /// <summary>
        /// 덩치 보정 배율. 속도와 중력에 함께 곱해진다.
        ///
        /// 둘 다 같은 배율로 곱하는 게 핵심이다. 속도만 키우면 큰 몬스터의 조각이 한참 떠 있게 되고,
        /// 중력만 키우면 툭 떨어진다. v 와 g 를 같이 k 배 하면 도달 높이는 k 배가 되면서
        /// 체공 시간(v/g)은 그대로라, 덩치와 무관하게 같은 박자로 같은 비율의 연출이 나온다.
        /// </summary>
        private float _sizeScale = 1f;

        /// <summary>
        /// 조각 뭉치가 날아갈 수평 방향 (정규화된 월드 벡터, Zero 면 날리지 않는다).
        /// 공격 지점 → 몬스터 방향이라 "맞은 쪽 반대로" 밀려난다.
        /// </summary>
        private Vector3 _knockbackDir = Vector3.zero;

        /// <summary>
        /// 조각 날리기로 늘어나는 컬링 볼륨 여유분. 렌더러를 갈아 끼우는 시점에 필요한데
        /// 그때는 아직 파트별 속도가 정해지기 전이라, 설정값만으로 넉넉하게 어림해 둔다
        /// (수평 속도 × 체공 시간 정도면 충분하고, 남는 만큼은 컬링이 조금 덜 걸릴 뿐이다)
        /// </summary>
        private float _knockbackPadding;

        /// <summary>이번 파쇄에서 실제로 흩어진 덩어리 수 (뭉치기가 끝난 뒤의 값 — 데모 표시용).</summary>
        public int PartCount { get; private set; }

        /// <summary>뭉치기 전에 메시가 갈린 조각 수. 조각 수 랜덤의 상한이다 (데모 표시용).</summary>
        public int MaxPartCount { get; private set; }

        /// <summary>분해 메시 정점 수 합계 (데모 표시용).</summary>
        public int VertexCount { get; private set; }

        /// <summary>분해 메시 메모리 합계 (데모 표시용).</summary>
        public long MeshBytes { get; private set; }

        public bool IsActive => _entries.Count > 0;

        // ------------------------------------------------------------------
        // 시작 / 원복

        /// <summary>
        /// 파쇄를 시작한다. meshRoot 아래의 스킨 메시를 분해 메시로 갈아 끼우고,
        /// 본에 매달린 무기 MeshRenderer 도 함께 날아가게 잡아 둔다.
        /// 하나도 교체하지 못하면 아무것도 건드리지 않은 채 false 를 돌려준다.
        /// </summary>
        public bool Setup(Transform meshRoot, float groundY, in MonsterShatterTuning tuning)
        {
            return Setup(meshRoot, groundY, tuning, Vector3.zero);
        }

        /// <summary>
        /// 조각 뭉치를 한쪽으로 날려 보내는 파쇄.
        ///
        /// knockbackDir 은 조각이 밀려날 수평 방향(정규화 여부는 상관없다 — 여기서 다시 재운다).
        /// Zero 를 주면 날리지 않고 제자리에서 터지는 기존 연출이 그대로 나온다.
        ///
        /// 물리 넉백처럼 시체를 움직이는 게 아니라 파트별 초기 속도에 공통 성분으로 더하는 방식이다.
        /// 궤적 계산이 전부 셰이더 안에서 pivot 기준으로 돌아가므로 조각이 늘어지지 않고,
        /// 착지·구르기·눕힘 보정도 날아간 자리에서 그대로 이어진다.
        /// </summary>
        public bool Setup(Transform meshRoot, float groundY, in MonsterShatterTuning tuning, Vector3 knockbackDir)
        {
            Restore();

            if (meshRoot == null)
                return false;

            //수평 성분만 쓴다. 위로 뜨는 몫은 KnockbackUpward 가 따로 담당한다
            knockbackDir.y = 0f;
            _knockbackDir = knockbackDir.sqrMagnitude > 1e-6f ? knockbackDir.normalized : Vector3.zero;

            bool willKnockback = tuning.HasKnockback && _knockbackDir != Vector3.zero;
            _knockbackPadding = willKnockback ? tuning.KnockbackForceUpperBound * 2f : 0f;

            Shader shader = GetShader();
            if (shader == null)
                return false;

            for (int i = 0; i < MonsterShatterParts.Count; i++)
            {
                _pivotSum[i] = Vector3.zero;
                _pivotWeight[i] = 0f;
                _radius[i] = 0f;
                _present[i] = false;
                _chunk[i] = false;
                _longAxis[i] = Vector4.zero;
                _longWeight[i] = 0;
                _groupLeader[i] = i;
            }

            VertexCount = 0;
            MeshBytes = 0;
            _hasWeaponRenderer = false;
            _bodyBoundsInit = false;
            _sizeScale = 1f;

            //덩어리 분할 여부는 몬스터 전체를 봐야 정할 수 있다. 스테이지 진입 때 이미 구워 뒀다면
            //파트 수 확인만 하고 지나간다 (프리워밍을 못 탄 경로로 처음 죽는 경우를 위한 안전망)
            MonsterShatterMeshCache.PrepareRoot(meshRoot);

            Renderer[] renderers = meshRoot.GetComponentsInChildren<Renderer>(true);

            //1) 스킨 메시 — 파트 단위로 분해된 메시로 교체하고 파트 중심을 누적한다
            for (int i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i] as SkinnedMeshRenderer;
                if (smr == null || smr.sharedMesh == null || smr.enabled == false)
                    continue;

                ShatterMeshData data = MonsterShatterMeshCache.GetOrCreate(smr);
                if (data == null || data.Mesh == null)
                    continue;

                Material shatterMat = GetShatterMaterial(smr.sharedMaterials, shader);
                if (shatterMat == null)
                    continue;

                AccumulateSkinned(smr, data);
                SwapRenderer(smr, data.Mesh, shatterMat, true);

                VertexCount += data.Mesh.vertexCount;
                MeshBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(data.Mesh);
            }

            //스킨 메시를 하나도 못 바꿨으면 연출 자체가 성립하지 않는다.
            //무기만 날아가는 꼴이 되므로 통째로 접고 기존 연출로 폴백시킨다
            if (_entries.Count == 0)
            {
                Restore();
                return false;
            }

            //2) 무기 — 본에 매달린 MeshRenderer(DemonKingBlade, orc_bow 등).
            //   분해할 필요 없이 통째로 무기 파트에 태운다
            for (int i = 0; i < renderers.Length; i++)
            {
                var mr = renderers[i] as MeshRenderer;
                if (mr == null || mr.enabled == false)
                    continue;

                var filter = mr.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                Material shatterMat = GetShatterMaterial(mr.sharedMaterials, shader);
                if (shatterMat == null)
                    continue;

                AccumulateWeapon(mr, filter.sharedMesh);
                SwapRenderer(mr, null, shatterMat, false);
                _hasWeaponRenderer = true;
            }

            BuildPieceConstants(groundY, tuning);
            ApplyConstants(groundY, tuning);

            return true;
        }

        /// <summary>렌더러를 원래 메시·머티리얼로 되돌린다. 여러 번 불러도 안전하다.</summary>
        public void Restore()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                RendererEntry entry = _entries[i];
                Renderer renderer = entry.Renderer;
                if (renderer == null)
                    continue;

                renderer.SetPropertyBlock(null);
                renderer.sharedMaterials = entry.OriginalMaterials;
                renderer.shadowCastingMode = entry.OriginalShadow;

                if (entry.Skinned && renderer is SkinnedMeshRenderer smr)
                {
                    smr.sharedMesh = entry.OriginalMesh;
                    smr.localBounds = entry.OriginalLocalBounds;
                }
            }

            _entries.Clear();
            PartCount = 0;
            _knockbackDir = Vector3.zero;
            _knockbackPadding = 0f;
        }

        // ------------------------------------------------------------------
        // 갱신

        /// <summary>진행도를 갱신한다. seconds 는 파쇄 시작 후 경과 시간(초).</summary>
        public void Apply(float seconds, float progress, in MonsterShatterTuning tuning)
        {
            if (_entries.Count == 0)
                return;

            //상수 배열은 Setup 에서 한 번만 넣었고 블록이 그대로 들고 있다.
            //매 프레임 바뀌는 건 시간·진행도뿐이라 그 둘만 덮어쓴다 (Clear 를 부르면 배열이 날아간다)
            _block.SetFloat(ShatterTimeID, seconds);
            _block.SetFloat(ShatterProgressID, progress);

            if (_hasWeaponRenderer && _weaponBlock != null)
            {
                _weaponBlock.SetFloat(ShatterTimeID, seconds);
                _weaponBlock.SetFloat(ShatterProgressID, progress);
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                RendererEntry entry = _entries[i];
                if (entry.Renderer == null)
                    continue;

                entry.Renderer.SetPropertyBlock(entry.Skinned ? _block : _weaponBlock);
            }
        }

        // ------------------------------------------------------------------
        // 파트 중심 누적

        /// <summary>
        /// 스킨 메시의 파트 중심을 사망 포즈 기준 월드 좌표로 누적한다.
        ///
        /// 파트가 여러 본에 걸쳐 있어도, 본별 무게중심을 각 본의 행렬로 옮겨 정점 수로 가중평균하면
        /// 전체 무게중심이 정확히 나온다 (합집합의 무게중심 = 부분집합 무게중심의 가중평균).
        /// 정점 배열을 다시 읽을 필요가 없어 본 개수만큼의 행렬 곱으로 끝난다.
        ///
        /// 렌더러가 여러 개인 몬스터(orc_archer 는 6개)는 같은 파트가 여러 렌더러에 흩어져 있다.
        /// 렌더러마다 따로 중심을 구하면 몸통이 렌더러 경계에서 갈라지므로, 여기서 전부 합산한 뒤
        /// 마지막에 한 번만 나눈다.
        /// </summary>
        private void AccumulateSkinned(SkinnedMeshRenderer smr, ShatterMeshData data)
        {
            Transform[] bones = smr.bones;
            if (bones == null)
                return;

            Vector3 scale = smr.transform.lossyScale;
            float scaleAvg = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;

            //덩치 보정용 바운즈. 분해 메시로 갈아 끼우기 전에 재야 한다
            //(교체 후에는 컬링용으로 크게 부풀린 값이 나온다)
            if (_bodyBoundsInit == false)
            {
                _bodyBounds = smr.bounds;
                _bodyBoundsInit = true;
            }
            else
            {
                _bodyBounds.Encapsulate(smr.bounds);
            }

            for (int slot = 0; slot < data.Slots.Length && slot < MonsterShatterParts.Count; slot++)
            {
                ShatterSlot part = data.Slots[slot];
                if (part == null)
                    continue;

                for (int b = 0; b < part.Bones.Length; b++)
                {
                    ShatterSlotBone entry = part.Bones[b];
                    if (entry.BoneIndex < 0 || entry.BoneIndex >= bones.Length)
                        continue;

                    Transform bone = bones[entry.BoneIndex];
                    if (bone == null)
                        continue;

                    Vector3 world = bone.localToWorldMatrix.MultiplyPoint3x4(entry.BoneLocalCentroid);

                    _pivotSum[slot] += world * entry.VertexCount;
                    _pivotWeight[slot] += entry.VertexCount;
                }

                _radius[slot] = Mathf.Max(_radius[slot], part.Radius * scaleAvg);
                _present[slot] = true;
                _chunk[slot] |= part.IsChunk;

                //장축은 한 렌더러 값만 쓴다. 같은 파트가 여러 렌더러에 걸쳐 있으면 정점이 가장 많은 쪽
                if (part.VertexCount > _longWeight[slot]
                    && part.LongAxisBone >= 0 && part.LongAxisBone < bones.Length
                    && bones[part.LongAxisBone] != null)
                {
                    Vector3 world = bones[part.LongAxisBone].localToWorldMatrix.MultiplyVector(part.LongAxisBoneLocal);
                    if (world.sqrMagnitude > 1e-8f)
                    {
                        world.Normalize();
                        _longAxis[slot] = new Vector4(world.x, world.y, world.z, part.Flatten);
                        _longWeight[slot] = part.VertexCount;
                    }
                }
            }
        }

        /// <summary>무기 MeshRenderer 를 통째로 무기 파트에 얹는다.</summary>
        private void AccumulateWeapon(MeshRenderer mr, Mesh mesh)
        {
            const int slot = (int)MonsterShatterPart.Weapon;

            Bounds bounds = mr.bounds;   //월드 AABB
            float weight = Mathf.Max(mesh.vertexCount, 1);

            _pivotSum[slot] += bounds.center * weight;
            _pivotWeight[slot] += weight;

            Vector3 size = bounds.size;
            float thin = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float thick = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            _radius[slot] = Mathf.Max(_radius[slot], Mathf.Max(thin, thick * 0.25f) * 0.5f);
            _present[slot] = true;

            //무기는 대개 길쭉해서 눕힘 보정이 특히 중요하다.
            //월드 AABB 라 이미 월드 축이고, 본을 거칠 필요가 없다
            if (mesh.vertexCount > _longWeight[slot])
            {
                Vector3 axis = MonsterShatterMeshCache.LongestAxis(size, out float elongation);
                _longAxis[slot] = new Vector4(axis.x, axis.y, axis.z, Mathf.Clamp01((elongation - 1.6f) / 1.4f));
                _longWeight[slot] = mesh.vertexCount;
            }
        }

        // ------------------------------------------------------------------
        // 파트별 궤적 상수

        /// <summary>
        /// 파트마다 초기 속도·회전축·소멸 시점을 정한다.
        /// 분수처럼 위로 크게 솟구치고, 몸 중심에서 바깥으로 조금씩 벌어지게 한다.
        /// </summary>
        private void BuildPieceConstants(float groundY, in MonsterShatterTuning tuning)
        {
            //몸 중심 — 발산 방향의 기준점
            Vector3 center = Vector3.zero;
            float totalWeight = 0f;

            for (int slot = 0; slot < MonsterShatterParts.Count; slot++)
            {
                if (_present[slot] == false || _pivotWeight[slot] <= 0f)
                    continue;

                center += _pivotSum[slot];
                totalWeight += _pivotWeight[slot];
            }

            if (totalWeight > 0f)
                center /= totalWeight;

            //조각 수 랜덤 — 맞닿은 파트끼리 묶어 이번 사망의 덩어리 수를 먼저 확정한다.
            //묶인 파트는 아래 루프를 건너뛰고 대표의 궤적 상수를 그대로 복사받는다.
            //몸 중심은 병합해도 그대로이므로(가중합의 합은 순서를 타지 않는다) 위에서 미리 구해 둔 값을 쓴다
            PartCount = GroupPieces(tuning);

            //덩치 보정 — 2m 짜리 일반 몬스터와 7m 짜리 보스가 같은 속도로 터지면
            //보스는 조각이 제자리에서 꿈틀대는 것처럼 보인다
            _sizeScale = 1f;
            if (tuning.SizeReference > 0.01f && _bodyBoundsInit)
                _sizeScale = Mathf.Clamp(_bodyBounds.size.y / tuning.SizeReference, 0.5f, 4f);

            //조각 날리기 — 방향만 파쇄 한 건에 한 번 정하고 모든 파트가 같은 값을 공유한다.
            //파트마다 방향을 흔들면 뭉치가 한쪽으로 날아가는 것으로 읽히지 않고 그냥 더 넓게 퍼진 것처럼 보인다.
            //세기는 반대로 파트마다 따로 뽑는다 (아래 루프)
            bool hasKnockback = tuning.HasKnockback && _knockbackDir.sqrMagnitude > 0.5f;
            Vector3 knockbackDirection = _knockbackDir;

            if (hasKnockback && tuning.KnockbackAngleSpread > 0.01f)
                knockbackDirection = Quaternion.AngleAxis(NextSigned() * tuning.KnockbackAngleSpread, Vector3.up) * knockbackDirection;

            for (int slot = 0; slot < PieceMax; slot++)
            {
                if (slot >= MonsterShatterParts.Count || _present[slot] == false || _pivotWeight[slot] <= 0f)
                {
                    //쓰이지 않는 슬롯. 혹시라도 참조되면 제자리에 멈춰 있도록 안전한 값을 넣는다
                    _piecePivot[slot] = new Vector4(0f, 0f, 0f, 1f);
                    _pieceVel[slot] = Vector4.zero;
                    _pieceAxis[slot] = new Vector4(0f, 1f, 0f, 1f);
                    _pieceLong[slot] = new Vector4(0f, 1f, 0f, 0f);
                    continue;
                }

                //다른 덩어리에 흡수된 슬롯. 대표 값을 그대로 받아야 하므로 아래 복사 패스에 맡긴다
                //(여기서 계산하면 난수만 헛돌고 값은 어차피 덮어써진다)
                if (_groupLeader[slot] != slot)
                    continue;

                Vector3 pivot = _pivotSum[slot] / _pivotWeight[slot];
                float radius = Mathf.Max(_radius[slot], 0.02f);

                //몸 중심에서 파트로 향하는 수평 방향 = 바깥으로 벌어지는 방향
                Vector3 outward = pivot - center;
                outward.y = 0f;

                if (outward.sqrMagnitude < 1e-6f)
                {
                    //몸 중심축에 그대로 걸린 파트(몸통·머리)는 방향이 없으므로 아무 쪽으로나 흩는다
                    float angle = NextFloat() * Mathf.PI * 2f;
                    outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }
                else
                {
                    outward.Normalize();
                }

                float up = tuning.UpForce * _sizeScale * (0.75f + NextFloat() * 0.5f);
                float spread = tuning.Spread * _sizeScale * (0.55f + NextFloat() * 0.9f);
                Vector3 velocity = outward * spread + Vector3.up * up;

                //뭉치 전체를 밀어내는 몫. 수평과 수직을 따로 뽑아야 낮게 멀리 가는 조각과 높이 뜨는 조각이 섞인다
                //(한 배율로 벡터 전체를 키우면 세기만 다르고 궤적 모양은 전부 같아진다).
                //덩치 보정을 곱하는 이유는 위 _sizeScale 주석과 같다 — 중력이 같은 배율로 커져 있으므로
                //속도에도 곱해야 큰 보스가 제 덩치에 비례한 거리만큼 날아간다.
                //꺼져 있으면 난수를 뽑지 않으므로 기존 연출의 그림이 한 톨도 달라지지 않는다
                if (hasKnockback)
                {
                    float knockForce = Mathf.Lerp(tuning.KnockbackForceMin, tuning.KnockbackForceMax, NextFloat()) * _sizeScale;
                    float knockUp = Mathf.Lerp(tuning.KnockbackUpwardMin, tuning.KnockbackUpwardMax, NextFloat()) * _sizeScale;

                    velocity += knockbackDirection * knockForce + Vector3.up * knockUp;
                }

                //회전축은 진행 방향에 수직인 수평축. 이래야 공중에서 앞으로 구르다가
                //착지 후 굴러가는 동작으로 자연스럽게 이어진다.
                //날아가는 중이면 진행 방향이 넉백 쪽으로 기울므로, 발산 방향이 아니라 실제 수평 속도를 기준으로 잡는다
                Vector3 travelDirection = new Vector3(velocity.x, 0f, velocity.z);
                if (travelDirection.sqrMagnitude > 1e-6f)
                    travelDirection.Normalize();
                else
                    travelDirection = outward;

                Vector3 axis = Vector3.Cross(Vector3.up, travelDirection);
                axis += new Vector3(NextSigned(), NextSigned(), NextSigned()) * 0.3f;

                if (axis.sqrMagnitude < 1e-6f)
                    axis = Vector3.right;
                else
                    axis.Normalize();

                float omega = tuning.Spin * (0.6f + NextFloat() * 0.8f);
                float vanishAt = Mathf.Clamp(tuning.FadeStart + NextFloat() * 0.12f, 0f, 0.98f);

                _piecePivot[slot] = new Vector4(pivot.x, pivot.y, pivot.z, radius);
                _pieceVel[slot] = new Vector4(velocity.x, velocity.y, velocity.z, omega);
                _pieceAxis[slot] = new Vector4(axis.x, axis.y, axis.z, vanishAt);

                Vector4 lng = _longAxis[slot];
                _pieceLong[slot] = lng.sqrMagnitude > 1e-8f ? lng : new Vector4(0f, 1f, 0f, 0f);
            }

            //묶인 슬롯에 대표의 궤적 상수를 그대로 복사한다.
            //pivot·속도·회전축이 같으면 셰이더가 두 파트를 한 덩어리의 강체로 굴린다 —
            //상대 좌표가 전부 같은 pivot 기준으로 잡히므로 서로의 위치 관계가 유지된 채 함께 돈다
            for (int slot = 0; slot < MonsterShatterParts.Count; slot++)
            {
                int leader = _groupLeader[slot];
                if (leader == slot)
                    continue;

                _piecePivot[slot] = _piecePivot[leader];
                _pieceVel[slot] = _pieceVel[leader];
                _pieceAxis[slot] = _pieceAxis[leader];
                _pieceLong[slot] = _pieceLong[leader];
            }
        }

        /// <summary>
        /// 맞닿은 파트끼리 묶어 이번 사망의 덩어리 수를 정한다. 반환값은 실제로 남은 덩어리 수.
        ///
        /// 메시를 다시 가르는 게 아니라 궤적 상수를 공유시키는 방식이라 개수는 줄어드는 쪽으로만 움직인다.
        /// 더 잘게 갈리게 하려면 분해 자체를 바꿔야 하는데(Config 의 '몬스터별 파트 수'), 그건 스테이지
        /// 진입 때 굽고 종류별로 캐시하는 값이라 몬스터 한 마리 단위로는 흔들 수 없다.
        ///
        /// 합칠 상대는 늘 '가장 가까운 두 덩어리'다. 아무거나 묶으면 머리와 반대쪽 발이 한 몸이 되어
        /// 빈 공간을 사이에 두고 같이 도는 그림이 나온다. 거리로 붙이면 해부학적으로도 말이 되는
        /// 단위(팔+손, 몸통+머리)가 저절로 나온다.
        /// </summary>
        private int GroupPieces(in MonsterShatterTuning tuning)
        {
            for (int i = 0; i < MonsterShatterParts.Count; i++)
                _groupLeader[i] = i;

            _groupBuffer.Clear();

            for (int slot = 0; slot < MonsterShatterParts.Count; slot++)
            {
                if (_present[slot] && _pivotWeight[slot] > 0f)
                    _groupBuffer.Add(slot);
            }

            MaxPartCount = _groupBuffer.Count;

            //꺼져 있으면 난수를 아예 뽑지 않는다. 기존 연출의 그림이 한 톨도 달라지지 않아야 한다
            if (tuning.PieceRandomEnabled == false || _groupBuffer.Count <= 1)
                return _groupBuffer.Count;

            //최소·최대를 뒤집어 넣어도 같은 답이 나오게 한다.
            //비율 0 은 "전부 한 덩어리" 를 뜻하므로 0조각이 아니라 1조각으로 받는다
            float low = Mathf.Clamp01(Mathf.Min(tuning.PieceRatioMin, tuning.PieceRatioMax));
            float high = Mathf.Clamp01(Mathf.Max(tuning.PieceRatioMin, tuning.PieceRatioMax));

            float ratio = Mathf.Lerp(low, high, NextFloat());
            int target = Mathf.Clamp(Mathf.RoundToInt(_groupBuffer.Count * ratio), 1, _groupBuffer.Count);

            while (_groupBuffer.Count > target)
            {
                float bestDistance = float.MaxValue;
                int bestA = -1;
                int bestB = -1;

                //덩어리가 15개를 넘지 않으므로 전 쌍을 훑어도 100회 남짓이다
                for (int i = 0; i < _groupBuffer.Count; i++)
                {
                    Vector3 a = _pivotSum[_groupBuffer[i]] / _pivotWeight[_groupBuffer[i]];

                    for (int j = i + 1; j < _groupBuffer.Count; j++)
                    {
                        Vector3 b = _pivotSum[_groupBuffer[j]] / _pivotWeight[_groupBuffer[j]];
                        float distance = (a - b).sqrMagnitude;

                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;
                        bestA = i;
                        bestB = j;
                    }
                }

                if (bestA < 0)
                    break;

                //정점이 많은 쪽을 대표로 삼는다. 장축·반지름 같은 대표값이 큰 덩어리 것을 따라가야
                //눕힘 보정이 팔이 아니라 몸통 기준으로 걸린다
                int leader = _groupBuffer[bestA];
                int merged = _groupBuffer[bestB];

                if (_pivotWeight[merged] > _pivotWeight[leader])
                    (leader, merged) = (merged, leader);

                MergeGroup(leader, merged, Mathf.Sqrt(bestDistance));
                _groupBuffer.Remove(merged);
            }

            return _groupBuffer.Count;
        }

        /// <summary>덩어리 하나를 다른 덩어리에 흡수시킨다. gap 은 두 중심 사이의 거리.</summary>
        private void MergeGroup(int leader, int merged, float gap)
        {
            //무게중심은 정점 수로 가중한 합이라 그대로 더하면 합쳐진 덩어리의 중심이 나온다
            _pivotSum[leader] += _pivotSum[merged];
            _pivotWeight[leader] += _pivotWeight[merged];

            //착지 높이 기준. 붙어서 길어진 만큼은 키워야 큰 덩어리가 지면에 파묻히지 않는다.
            //다만 거리의 절반을 그대로 쓰면(=최장축 반경) 착지 후 눕고 나서도 그 높이에 떠 있게 되므로,
            //눕힘 보정이 장축을 수평으로 눕힌다는 걸 감안해 절반만 반영한다
            _radius[leader] = Mathf.Max(Mathf.Max(_radius[leader], _radius[merged]), gap * 0.25f);

            //장축은 정점이 많은 쪽 것을 쓴다 (AccumulateSkinned 와 같은 규칙)
            if (_longWeight[merged] > _longWeight[leader])
            {
                _longAxis[leader] = _longAxis[merged];
                _longWeight[leader] = _longWeight[merged];
            }

            //흡수된 덩어리에 이미 딸려 있던 슬롯까지 대표를 갈아 끼운다
            for (int i = 0; i < MonsterShatterParts.Count; i++)
            {
                if (_groupLeader[i] == merged)
                    _groupLeader[i] = leader;
            }
        }

        private void ApplyConstants(float groundY, in MonsterShatterTuning tuning)
        {
            if (_block == null)
                _block = new MaterialPropertyBlock();

            _block.Clear();
            FillCommon(_block, groundY, tuning);
            _block.SetFloat(ForcePieceID, -1f);

            if (_hasWeaponRenderer)
            {
                if (_weaponBlock == null)
                    _weaponBlock = new MaterialPropertyBlock();

                _weaponBlock.Clear();
                FillCommon(_weaponBlock, groundY, tuning);

                //무기 MeshRenderer 는 분해 메시가 아니라서 파트 인덱스를 담은 정점 데이터가 없다.
                //슬롯을 상수로 못 박아 준다
                _weaponBlock.SetFloat(ForcePieceID, (int)MonsterShatterPart.Weapon);
            }
        }

        private void FillCommon(MaterialPropertyBlock block, float groundY, in MonsterShatterTuning tuning)
        {
            block.SetVectorArray(PiecePivotID, _piecePivot);
            block.SetVectorArray(PieceVelID, _pieceVel);
            block.SetVectorArray(PieceAxisID, _pieceAxis);
            block.SetVectorArray(PieceLongID, _pieceLong);

            //중력도 속도와 같은 배율로 키운다 (위 _sizeScale 주석 참고)
            block.SetFloat(GravityID, Mathf.Max(tuning.Gravity * _sizeScale, 0.01f));
            block.SetFloat(GroundYID, groundY);
            block.SetFloat(RollKeepID, tuning.RollKeep);
            block.SetFloat(RollDampID, Mathf.Max(tuning.RollDamp, 0.02f));
            block.SetFloat(LandBounceID, tuning.LandBounce);
            block.SetFloat(ShatterTimeID, 0f);
            block.SetFloat(ShatterProgressID, 0f);
        }

        // ------------------------------------------------------------------
        // 렌더러 교체

        private void SwapRenderer(Renderer renderer, Mesh shattered, Material shatterMat, bool skinned)
        {
            var entry = new RendererEntry
            {
                Renderer = renderer,
                OriginalMaterials = renderer.sharedMaterials,
                OriginalShadow = renderer.shadowCastingMode,
                Skinned = skinned,
            };

            if (skinned && renderer is SkinnedMeshRenderer smr)
            {
                entry.OriginalMesh = smr.sharedMesh;
                entry.OriginalLocalBounds = smr.localBounds;

                smr.sharedMesh = shattered;

                //조각이 원위치에서 크게 벗어나므로 원래 컬링 볼륨으로는 화면 밖 판정을 받아 사라진다.
                //덩치 비례분(솟구침·발산) 위에 조각 날리기가 밀어내는 거리를 절대값으로 더한다 —
                //이쪽은 몬스터 크기와 무관하게 정해지는 값이라 비율만으로는 못 덮는다
                Bounds expanded = smr.localBounds;
                expanded.Expand(expanded.size.magnitude * 3f + _knockbackPadding);
                smr.localBounds = expanded;
            }

            //현재 몬스터 메시는 대부분 서브메시 1개지만, 여러 개인 메시가 들어오면
            //첫 슬롯만 바꿀 경우 나머지 서브메시가 원본 셰이더로 남아 파쇄되지 않은 채 떠 있게 된다
            Material[] originals = entry.OriginalMaterials;
            if (originals != null && originals.Length > 1)
            {
                var replaced = new Material[originals.Length];
                for (int m = 0; m < replaced.Length; m++)
                    replaced[m] = shatterMat;

                renderer.sharedMaterials = replaced;
            }
            else
            {
                renderer.sharedMaterial = shatterMat;
            }

            //조각이 흩어지는 동안의 그림자는 알아볼 수 없으면서 비용만 든다
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            _entries.Add(entry);
        }

        // ------------------------------------------------------------------
        // 공용 리소스

        public static Shader GetShader()
        {
            if (_shader == null)
                _shader = Shader.Find(ShaderName);

            if (_shader == null)
                Debug.LogError($"[MonsterShatter] 셰이더 '{ShaderName}' 를 찾을 수 없다. Resources/Shaders 에 있는지, 빌드에 포함됐는지 확인해야 한다");

            return _shader;
        }

        /// <summary>
        /// 원본 머티리얼의 베이스 텍스처를 물려받은 파쇄 머티리얼을 돌려준다.
        /// 몬스터가 쓰는 셰이더가 3종(PX/MonsterToonUnlitURP, URP/Lit, Shader Graphs/URPmaskTint)이라
        /// 프로퍼티 이름을 순서대로 훑어 텍스처를 찾는다.
        /// </summary>
        private static Material GetShatterMaterial(Material[] sources, Shader shader)
        {
            Material source = sources != null && sources.Length > 0 ? sources[0] : null;
            if (source == null)
                return null;

            //Material.mainTexture 는 쓰지 않는다. _MainTex 가 없는 셰이더에서 접근하면
            //Unity 가 매번 경고를 남기는데, 사망 때마다 콘솔이 도배된다
            Texture texture = null;
            if (source.HasProperty(BaseMapID))
                texture = source.GetTexture(BaseMapID);
            if (texture == null && source.HasProperty(MainTexID))
                texture = source.GetTexture(MainTexID);

            //여기까지 못 찾았으면 Shader Graph 머티리얼이다. 알베도·틴트 마스크·틴트 색을 같이 가져온다
            Texture tintMask = null;
            bool useTint = false;

            if (texture == null)
            {
                texture = FindShaderGraphTextures(source, out tintMask);
                useTint = texture != null && tintMask != null;
            }

            //텍스처를 못 찾으면 흰 덩어리가 터지는 꼴이 되므로 파쇄를 포기하고 기존 연출로 넘긴다
            if (texture == null)
                return null;

            //틴트를 쓰면 같은 텍스처에 색만 다른 머티리얼이 있을 수 있어 머티리얼 단위로 캐시한다.
            //그 외에는 텍스처 단위로 묶어야 같은 텍스처를 쓰는 몬스터끼리 배칭이 유지된다
            int key = useTint ? source.GetInstanceID() : texture.GetInstanceID();
            if (_materialCache.TryGetValue(key, out Material cached) && cached != null)
                return cached;

            var mat = new Material(shader);
            mat.name = "MonsterShatter_" + (useTint ? source.name : texture.name);
            mat.SetTexture(BaseMapID, texture);
            mat.SetColor(BaseColorID, Color.white);

            if (useTint)
            {
                CollectTints(source, out Color a, out Color b, out Color c);
                mat.SetFloat(UseTintID, 1f);
                mat.SetTexture(TintMaskID, tintMask);
                mat.SetColor(TintAID, a);
                mat.SetColor(TintBID, b);
                mat.SetColor(TintCID, c);
            }

            _materialCache[key] = mat;
            return mat;
        }

        /// <summary>셰이더에 선언된 순서대로 앞의 색 프로퍼티 3개를 가져온다.</summary>
        private static void CollectTints(Material source, out Color a, out Color b, out Color c)
        {
            a = Color.white;
            b = Color.white;
            c = Color.white;

            Shader shader = source.shader;
            if (shader == null)
                return;

            int count = shader.GetPropertyCount();
            int found = 0;

            for (int i = 0; i < count && found < 3; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Color)
                    continue;

                Color value = source.GetColor(shader.GetPropertyNameId(i));

                if (found == 0) a = value;
                else if (found == 1) b = value;
                else c = value;

                found++;
            }

            //색이 3개보다 적으면 남은 채널은 첫 색으로 채운다
            if (found == 1) { b = a; c = a; }
            else if (found == 2) { c = a; }
        }

        /// <summary>
        /// 관례 이름으로 못 찾을 때 쓰는 마지막 수단.
        ///
        /// Shader Graph 로 만든 머티리얼(Shader Graphs/URPmaskTint — 비홀더·슬라임이 쓴다)은
        /// 프로퍼티 이름이 Texture2D_ebff28f1… 같은 GUID 라 _BaseMap / _MainTex 로는 절대 안 잡히고,
        /// [MainTexture] 태그도 없어 mainTexture 도 null 이다.
        /// Shader Graph 는 블랙보드 선언 순서를 그대로 유지하므로 첫 텍스처를 알베도로 본다
        /// (URPmaskTint 의 경우 AlbedoMaskTint — 회색 몸통과 주황 촉수가 그대로 들어 있는 진짜 알베도다.
        ///  뒤따르는 Mask01 은 색을 덧씌우는 마스크, PBRDefault_MaskMap 은 URP 마스크맵이라 쓰지 않는다).
        /// </summary>
        private static Texture FindShaderGraphTextures(Material source, out Texture tintMask)
        {
            tintMask = null;

            Shader shader = source.shader;
            if (shader == null)
                return null;

            int count = shader.GetPropertyCount();
            Texture albedo = null;
            int found = 0;

            for (int i = 0; i < count && found < 2; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                    continue;

                //라이트맵 같은 엔진 내장 슬롯은 건너뛴다
                if (shader.GetPropertyName(i).StartsWith("unity_", System.StringComparison.Ordinal))
                    continue;

                Texture texture = source.GetTexture(shader.GetPropertyNameId(i));
                if (texture == null)
                    continue;

                if (found == 0) albedo = texture;
                else tintMask = texture;

                found++;
            }

            return albedo;
        }

        public static void ClearMaterialCache()
        {
            foreach (KeyValuePair<int, Material> entry in _materialCache)
            {
                if (entry.Value == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(entry.Value);
                else
                    Object.DestroyImmediate(entry.Value);
            }

            _materialCache.Clear();
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// 어떤 파트로 갈렸는지 사람이 읽을 수 있게 뽑는다 (데모 표시용).
        /// 한 덩어리로 묶인 파트는 '머리+몸통' 처럼 붙여 쓴다 — 띄어쓰기 하나가 조각 하나다.
        /// </summary>
        public string DescribeParts()
        {
            var sb = new StringBuilder();
            int chunkGroups = 0;

            for (int leader = 0; leader < MonsterShatterParts.Count; leader++)
            {
                if (_present[leader] == false || _pivotWeight[leader] <= 0f || _groupLeader[leader] != leader)
                    continue;

                int start = sb.Length;
                bool named = false;
                bool hasChunk = false;

                for (int slot = 0; slot < MonsterShatterParts.Count; slot++)
                {
                    if (_present[slot] == false || _groupLeader[slot] != leader)
                        continue;

                    //공간 분할로 만들어진 슬롯은 이름이 무의미하므로 개수만 센다
                    if (_chunk[slot])
                    {
                        hasChunk = true;
                        continue;
                    }

                    if (named)
                        sb.Append('+');
                    else if (start > 0)
                        sb.Append(' ');

                    sb.Append(MonsterShatterParts.KoreanName((MonsterShatterPart)slot));
                    named = true;
                }

                if (named == false && hasChunk)
                    chunkGroups++;
                else if (hasChunk)
                    sb.Append("+덩어리");
            }

            if (chunkGroups > 0)
            {
                if (sb.Length > 0)
                    sb.Append(' ');

                sb.Append("덩어리×").Append(chunkGroups);
            }

            return sb.ToString();
        }

        private static float NextFloat()
        {
            return (float)_rand.NextDouble();
        }

        private static float NextSigned()
        {
            return (float)(_rand.NextDouble() * 2.0 - 1.0);
        }
    }
}
