using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace PX
{
    /// <summary>파트 하나가 어떤 본에 얼마나 걸쳐 있는지. 사망 포즈에서 파트 중심을 구하는 데 쓴다.</summary>
    public sealed class ShatterSlotBone
    {
        /// <summary>SkinnedMeshRenderer.bones 인덱스.</summary>
        public int BoneIndex;

        /// <summary>
        /// 이 본에 붙은 정점들의 무게중심을, 그 본의 로컬 공간으로 옮겨 둔 값.
        /// 런타임에는 bone.localToWorldMatrix 만 곱하면 사망 포즈의 월드 좌표가 나온다
        /// (bindpose 를 미리 곱해 뒀으므로 런타임에 메시를 다시 읽을 필요가 없다).
        /// </summary>
        public Vector3 BoneLocalCentroid;

        public int VertexCount;
    }

    /// <summary>분해된 파트 하나.</summary>
    public sealed class ShatterSlot
    {
        public MonsterShatterPart Part;
        public ShatterSlotBone[] Bones;

        /// <summary>메시 로컬 기준 반지름. 지면에 놓였을 때 파묻히지 않을 정도의 두께다.</summary>
        public float Radius;

        public int VertexCount;

        /// <summary>이름이 아니라 공간 분할로 만들어진 조각인지 (슬롯 이름에 의미가 없다).</summary>
        public bool IsChunk;

        /// <summary>가장 긴 축. 착지 후 조각을 눕히는 데 쓴다 (대표 본의 로컬 공간).</summary>
        public Vector3 LongAxisBoneLocal;

        /// <summary>위 축을 월드로 옮길 때 쓸 본.</summary>
        public int LongAxisBone;

        /// <summary>
        /// 눕힘 보정 세기(0~1). 길쭉한 조각일수록 1에 가깝다.
        /// 검이나 지팡이가 바닥에 곧추선 채로 멈추는 걸 막는 값이라, 둥근 조각에는 걸 필요가 없다.
        /// </summary>
        public float Flatten;
    }

    /// <summary>분해 메시 + 파트 정보.</summary>
    public sealed class ShatterMeshData
    {
        public Mesh Mesh;

        /// <summary>슬롯 인덱스로 바로 접근한다. 그 파트가 없으면 null.</summary>
        public ShatterSlot[] Slots;

        public int PartCount;
    }

    /// <summary>
    /// 몬스터 사망 파쇄용 "파트 분해 메시"를 만들고 캐시한다.
    ///
    /// 삼각형의 지배 본이 어느 파트에 속하는지(<see cref="MonsterShatterParts.Classify"/>)로 묶어
    /// 머리·몸통·팔·다리·무기 단위로 메시를 가른다. 같은 파트 안에서는 원본 정점을 그대로 공유하고
    /// 파트 경계에서만 정점을 뗀다. 그래서 파트 내부는 빈틈없이 붙어 있고, 정점 증가량도
    /// 전체의 2% 남짓으로 아주 작다.
    ///
    /// 갈라진 자리는 그대로 두면 속이 빈 껍데기가 되므로 경계 루프를 찾아 뚜껑을 덮는다
    /// (<see cref="Build"/> 안의 BuildCutCaps). 원본 메시가 처음부터 열려 있던 가장자리는
    /// 막을 단면 자체가 없어 셰이더의 Cull Off 가 맡는다.
    ///
    /// 정점에 굽는 추가 데이터 (PX/MonsterShatterURP 가 읽는다):
    ///   UV3(TexCoord2) : x = 파트 슬롯 인덱스, y = 소멸용 난수(0~1)
    ///
    /// 파트별 궤적에 필요한 값(중심·반지름)은 정점이 아니라 <see cref="ShatterSlot"/> 에 담아
    /// 런타임에 셰이더 상수 배열로 올린다. 정점에 구울 수 없는 이유는, 사망 포즈에서의 파트 중심은
    /// 그때의 본 행렬을 곱해야 나오는 값이라 바인드 포즈 시점에 확정할 수 없기 때문이다.
    ///
    /// 캐시 키는 (원본 메시 InstanceID, 병합 단계)다. 스테이지 내내 일반 몬스터 종류가 1종으로
    /// 고정되므로(BattleModeBase.stageFixedNormalMonsterIndex) 실제 캐시 크기는 등장 종류 수만큼이다.
    /// </summary>
    public static class MonsterShatterMeshCache
    {
        /// <summary>
        /// 이름만으로 이만큼도 못 가르면 공간 분할로 더 쪼갠다.
        /// 슬라임·비홀더처럼 팔다리가 없는 리그가 두 덩어리로만 갈라지는 것을 막는다.
        /// 개별 설정(Config 의 몬스터별 파트 수)이 걸린 몬스터는 그 값이 대신 기준이 된다.
        /// </summary>
        private const int MinParts = 5;

        //키 = (원본 메시 InstanceID, 병합 단계). 같은 메시를 다른 단계로도 구울 수 있어야 한다
        private static readonly Dictionary<long, ShatterMeshData> _cache = new Dictionary<long, ShatterMeshData>();

        //메시별로 확정된 병합 단계. PrepareRoot 가 정하고 GetOrCreate 가 그대로 따라간다
        private static readonly Dictionary<int, int> _meshLevel = new Dictionary<int, int>();

        //읽기 불가(Read/Write Enabled 꺼짐) 등으로 분해에 실패한 원본. 매번 재시도하지 않는다
        private static readonly HashSet<int> _unsupported = new HashSet<int>();

        //재사용 버퍼 (분해는 스테이지 진입 시점에만 일어나므로 정적 버퍼로 충분)
        private static readonly List<Vector2> _pieceBuffer = new List<Vector2>();
        private static readonly List<string> _nameBuffer = new List<string>(8);

        public static int CachedCount => _cache.Count;

        /// <summary>
        /// 잘린 단면을 뚜껑으로 막을지. 굽는 시점에 확정되므로 바꾸면 캐시를 비워야 한다.
        /// </summary>
        private static bool FillCuts
        {
            get
            {
                DeathSettings death = GameClientPlayConfig.Instance != null ? GameClientPlayConfig.Instance.death : null;
                return death == null || death.shatterFillCuts;
            }
        }

        /// <summary>
        /// 렌더러의 메시에 대응하는 파트 분해 메시를 돌려준다. 없으면 만들어 캐시한다.
        /// 본 이름이 필요하므로 메시가 아니라 렌더러를 받는다.
        /// 읽기 불가 메시면 null (호출자는 기존 사망 연출로 폴백해야 한다).
        /// </summary>
        public static ShatterMeshData GetOrCreate(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null)
                return null;

            //병합 단계는 몬스터 전체를 본 PrepareRoot 가 이미 정해 뒀다.
            //여기서 다시 판단하면 렌더러마다 단계가 엇갈려 같은 몬스터가 뒤섞인 굵기로 갈린다
            _meshLevel.TryGetValue(renderer.sharedMesh.GetInstanceID(), out int level);

            return GetOrCreate(renderer, level, MinParts, false, null);
        }

        private static ShatterMeshData GetOrCreate(SkinnedMeshRenderer renderer, int mergeLevel, int minParts,
                                                  bool allowChunkSplit, bool[] reservedSlots)
        {
            if (renderer == null || renderer.sharedMesh == null)
                return null;

            Mesh source = renderer.sharedMesh;
            int meshId = source.GetInstanceID();
            long key = CacheKey(meshId, mergeLevel);

            if (_cache.TryGetValue(key, out ShatterMeshData cached))
            {
                //씬 전환 등으로 파괴됐으면 다시 만든다
                if (cached != null && cached.Mesh != null)
                    return cached;

                _cache.Remove(key);
            }

            if (_unsupported.Contains(meshId))
                return null;

            ShatterMeshData built = Build(source, renderer.bones, mergeLevel, minParts, allowChunkSplit, reservedSlots);
            if (built == null)
            {
                _unsupported.Add(meshId);
                return null;
            }

            _cache[key] = built;
            return built;
        }

        private static long CacheKey(int meshId, int mergeLevel)
        {
            //하위 8비트를 단계로 쓴다. 메시 InstanceID 는 서로 1 이상 차이나므로 8비트를 밀어도 겹치지 않는다
            return ((long)meshId << 8) | (uint)(mergeLevel & 0xFF);
        }

        /// <summary>
        /// 프리팹에 들어 있는 모든 SkinnedMeshRenderer 메시를 미리 분해해 둔다.
        /// 스테이지 진입 시(PrepareStageSpawnAssets) 1회 호출해서 전투 중 hitch 를 없앤다.
        /// </summary>
        public static void PrewarmPrefab(GameObject prefab)
        {
            if (prefab != null)
                PrepareRoot(prefab.transform);
        }

        /// <summary>
        /// 몬스터 하나(루트 아래 스킨 메시 전부)를 한꺼번에 분해한다.
        ///
        /// 파트 굵기는 반드시 몬스터 전체를 보고 정해야 한다. 메시 하나만 놓고 보면
        /// orc_archer 처럼 부위별로 메시가 쪼개진 몬스터는 메시마다 "파트가 몇 개 없다"고 판단해
        /// 멀쩡한 다리 메시까지 덩어리로 갈라 버린다. 그래서 먼저 전부 기본값으로 구운 뒤,
        /// 합친 결과를 보고 병합 단계나 덩어리 분할을 정한다.
        ///
        /// 다시 굽는 건 개별 설정이 걸렸거나 슬라임·비홀더처럼 실제로 파트가 모자란 경우뿐이고,
        /// 그마저도 스테이지 진입 때 한 번이다. 이미 캐시된 뒤에는 파트 수 확인만 하고 지나간다.
        /// </summary>
        public static void PrepareRoot(Transform root)
        {
            if (root == null)
                return;

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
                return;

            //이미 구워 둔 몬스터면 곧바로 나간다.
            //굽는 단계를 정하려면 0단계로 한 번 구워 보고 나온 파트를 봐야 하는데, 그 탐색을 사망마다
            //반복하면 개별 설정이 걸린 몬스터는 시체 한 구마다 0단계 메시를 새로 굽고 다시 버리게 된다
            //(실측 1구당 1.09ms — 설정이 없는 몬스터의 13배였다)
            if (IsPrepared(renderers))
                return;

            int target = ResolvePartTarget(root, renderers);

            //1) 병합 없이 한 번 구워 이 몬스터가 어떤 파트로 갈리는지 본다
            int level = 0;
            BuildAll(renderers, level, MinParts, out bool[] present, out int partCount, out int biggest);

            //본에 매달린 무기 MeshRenderer 는 스킨 메시가 아니라 여기 안 잡히지만,
            //런타임에 무기 슬롯을 통째로 차지하고 따로 날아간다. 조각 수를 셀 때 같이 세지 않으면
            //Config 의 "파트 수" 가 실제로 흩어지는 개수와 하나씩 어긋난다.
            //병합은 무기 슬롯을 건드리지 않으므로 이 값은 단계와 무관하게 그대로다
            int weaponExtra = present[(int)MonsterShatterPart.Weapon] == false && HasWeaponRenderer(root) ? 1 : 0;

            //2) 개별 설정이 있으면 목표 개수 이하가 되는 가장 낮은 병합 단계로 다시 굽는다.
            //   병합은 분류 결과에만 걸리는 순수 함수라, 방금 나온 파트 집합만으로 결과를 미리 알 수 있다
            if (target > 0)
            {
                int wanted = ResolveMergeLevel(present, Mathf.Max(target - weaponExtra, 1));
                if (wanted > 0)
                {
                    //0단계 결과는 어디까지나 탐색용이었으므로 버린다
                    DropCached(renderers, level);

                    level = wanted;
                    BuildAll(renderers, level, target, out present, out partCount, out biggest);
                }
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Mesh mesh = renderers[i] != null ? renderers[i].sharedMesh : null;
                if (mesh != null)
                    _meshLevel[mesh.GetInstanceID()] = level;
            }

            //3) 그래도 파트가 모자라면 가장 큰 메시 하나만 공간 분할로 더 쪼갠다
            int minParts = (target > 0 ? target : MinParts) - weaponExtra;
            if (partCount >= minParts || minParts < 2 || biggest < 0)
                return;

            //이미 다른 메시가 쓰고 있는 슬롯은 비워 두게 해서 조각끼리 슬롯이 겹치지 않게 한다
            SkinnedMeshRenderer chunkTarget = renderers[biggest];
            DropCached(chunkTarget.sharedMesh, level);
            GetOrCreate(chunkTarget, level, minParts, true, present);
        }

        /// <summary>
        /// 이 몬스터의 분해 메시가 전부 준비돼 있는지. 단계가 정해져 있고 그 단계의 메시가 살아 있어야 한다.
        /// 굽지 못하는 메시(읽기 불가 등)는 다시 시도해도 소용없으므로 준비된 것으로 친다.
        /// </summary>
        private static bool IsPrepared(SkinnedMeshRenderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Mesh mesh = renderers[i] != null ? renderers[i].sharedMesh : null;
                if (mesh == null)
                    continue;

                int id = mesh.GetInstanceID();
                if (_unsupported.Contains(id))
                    continue;

                if (_meshLevel.TryGetValue(id, out int level) == false)
                    return false;

                if (_cache.TryGetValue(CacheKey(id, level), out ShatterMeshData data) == false
                    || data == null || data.Mesh == null)
                    return false;
            }

            return true;
        }

        /// <summary>본에 매달린 무기 MeshRenderer 가 있는지. 판정 기준은 MonsterShatterState.Setup 과 같다.</summary>
        private static bool HasWeaponRenderer(Transform root)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].enabled == false)
                    continue;

                var filter = renderers[i].GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    return true;
            }

            return false;
        }

        /// <summary>렌더러 전부를 같은 병합 단계로 굽고, 합쳐서 어떤 파트가 나오는지 센다.</summary>
        private static void BuildAll(SkinnedMeshRenderer[] renderers, int level, int minParts,
                                     out bool[] present, out int partCount, out int biggest)
        {
            present = new bool[MonsterShatterParts.Count];
            biggest = -1;
            int biggestTriangles = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer smr = renderers[i];
                ShatterMeshData data = GetOrCreate(smr, level, minParts, false, null);
                if (data == null)
                    continue;

                for (int s = 0; s < data.Slots.Length; s++)
                {
                    if (data.Slots[s] != null)
                        present[s] = true;
                }

                int triangles = 0;
                for (int s = 0; s < smr.sharedMesh.subMeshCount; s++)
                    triangles += (int)(smr.sharedMesh.GetIndexCount(s) / 3);

                if (triangles > biggestTriangles)
                {
                    biggestTriangles = triangles;
                    biggest = i;
                }
            }

            partCount = 0;
            for (int s = 0; s < present.Length; s++)
            {
                if (present[s])
                    partCount++;
            }
        }

        /// <summary>
        /// 목표 파트 수 이하로 떨어지는 가장 낮은 병합 단계.
        /// 끝까지 올려도 못 맞추면(무기가 따로 있어 2개 밑으로는 안 내려간다) 최대 단계를 쓴다.
        /// </summary>
        private static int ResolveMergeLevel(bool[] present, int target)
        {
            var merged = new bool[MonsterShatterParts.Count];

            for (int level = 0; level <= MonsterShatterParts.MaxMergeLevel; level++)
            {
                for (int i = 0; i < merged.Length; i++)
                    merged[i] = false;

                int count = 0;

                for (int s = 0; s < present.Length; s++)
                {
                    if (present[s] == false)
                        continue;

                    int slot = (int)MonsterShatterParts.Merge((MonsterShatterPart)s, level);
                    if (merged[slot])
                        continue;

                    merged[slot] = true;
                    count++;
                }

                if (count <= target)
                    return level;
            }

            return MonsterShatterParts.MaxMergeLevel;
        }

        /// <summary>
        /// 이 몬스터에 개별 파트 수 설정이 걸려 있는지 본다. 0이면 기본 동작.
        ///
        /// 이름을 정규화(소문자로 낮추고 구분자 제거)해 부분 일치로 찾으므로, 규칙 하나
        /// ("grimreaper")로 메시 이름 "Grim Reaper" 와 프리팹 이름 "monster#normal#magic#grimreaper" 를
        /// 한꺼번에 잡는다. 같은 메시를 여러 프리팹(일반/보스/층보스)이 공유하므로 메시 이름으로
        /// 거는 편이 빠짐없이 걸린다.
        /// </summary>
        private static int ResolvePartTarget(Transform root, SkinnedMeshRenderer[] renderers)
        {
            DeathSettings death = GameClientPlayConfig.Instance != null ? GameClientPlayConfig.Instance.death : null;
            List<DeathSettings.ShatterPartOverride> rules = death != null ? death.shatterPartOverrides : null;
            if (rules == null || rules.Count == 0)
                return 0;

            _nameBuffer.Clear();

            //보통 MeshArea 가 넘어오므로 위로 몇 단계까지 본다 (프리팹 이름이 그쯤에 있다)
            Transform node = root;
            for (int i = 0; i < 3 && node != null; i++, node = node.parent)
                _nameBuffer.Add(NormalizeName(node.name));

            for (int i = 0; i < renderers.Length; i++)
            {
                Mesh mesh = renderers[i] != null ? renderers[i].sharedMesh : null;
                if (mesh != null)
                    _nameBuffer.Add(NormalizeName(mesh.name));
            }

            for (int r = 0; r < rules.Count; r++)
            {
                DeathSettings.ShatterPartOverride rule = rules[r];
                if (rule == null || rule.targetParts <= 0 || string.IsNullOrEmpty(rule.nameKeyword))
                    continue;

                string keyword = NormalizeName(rule.nameKeyword);
                if (keyword.Length == 0)
                    continue;

                for (int i = 0; i < _nameBuffer.Count; i++)
                {
                    if (_nameBuffer[i].Contains(keyword))
                        return rule.targetParts;
                }
            }

            return 0;
        }

        /// <summary>소문자로 낮추고 글자·숫자만 남긴다. 공백·언더바·# 표기 차이를 무시하기 위한 것이다.</summary>
        public static string NormalizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var buffer = new System.Text.StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c) == false)
                    continue;

                buffer.Append(char.ToLowerInvariant(c));
            }

            return buffer.ToString();
        }

        /// <summary>캐시 전체 해제. 스테이지를 나갈 때 호출해 분해 메시 메모리를 반납한다.</summary>
        public static void ClearAll()
        {
            foreach (KeyValuePair<long, ShatterMeshData> entry in _cache)
                DestroyMesh(entry.Value);

            _cache.Clear();
            _meshLevel.Clear();
            _unsupported.Clear();
        }

        private static void DropCached(SkinnedMeshRenderer[] renderers, int level)
        {
            for (int i = 0; i < renderers.Length; i++)
                DropCached(renderers[i] != null ? renderers[i].sharedMesh : null, level);
        }

        private static void DropCached(Mesh source, int level)
        {
            if (source == null)
                return;

            long key = CacheKey(source.GetInstanceID(), level);
            if (_cache.TryGetValue(key, out ShatterMeshData old) == false)
                return;

            DestroyMesh(old);
            _cache.Remove(key);
        }

        private static void DestroyMesh(ShatterMeshData data)
        {
            if (data == null || data.Mesh == null)
                return;

            //에디터에서 프리워밍/검증 도구로 호출될 수 있다.
            //에디트 모드에서 Destroy 를 부르면 에셋이 정리되지 않고 오류만 남는다
            if (Application.isPlaying)
                Object.Destroy(data.Mesh);
            else
                Object.DestroyImmediate(data.Mesh);
        }

        private static ShatterMeshData Build(Mesh src, Transform[] bones, int mergeLevel, int minParts,
                                             bool allowChunkSplit, bool[] reservedSlots)
        {
            //Read/Write Enabled 가 꺼진 메시는 정점 배열에 접근할 수 없다
            if (src.isReadable == false)
            {
                Debug.LogError($"[MonsterShatter] 메시 '{src.name}' 가 읽기 불가라 파쇄 연출을 적용할 수 없다. " +
                                "FBX 임포트 설정에서 Read/Write Enabled 를 켜야 한다");
                return null;
            }

            Vector3[] srcVerts = src.vertices;
            if (srcVerts == null || srcVerts.Length == 0)
                return null;

            Vector2[] srcUV = src.uv;
            BoneWeight[] srcWeights = src.boneWeights;
            Matrix4x4[] bindposes = src.bindposes;

            bool hasUV = srcUV != null && srcUV.Length == srcVerts.Length;
            bool hasWeights = srcWeights != null && srcWeights.Length == srcVerts.Length
                              && bindposes != null && bindposes.Length > 0
                              && bones != null && bones.Length >= bindposes.Length;

            //본 정보가 없으면 파트를 나눌 수 없다. 통째로 한 덩어리가 되는 건 연출이 아니므로 포기한다
            if (hasWeights == false)
            {
                Debug.LogWarning($"[MonsterShatter] 메시 '{src.name}' 에 본 정보가 없어 파트 분해를 건너뛴다");
                return null;
            }

            //본 → 파트. 같은 파트로 분류된 본들이 하나의 조각이 된다.
            //병합 단계는 분류 직후에 한 번만 걸면 아래 로직 전체가 그대로 굵은 단위로 동작한다
            int boneCount = bindposes.Length;
            var bonePart = new int[boneCount];
            for (int b = 0; b < boneCount; b++)
            {
                Transform bone = b < bones.Length ? bones[b] : null;
                MonsterShatterPart part = MonsterShatterParts.Classify(bone != null ? bone.name : null);
                bonePart[b] = (int)MonsterShatterParts.Merge(part, mergeLevel);
            }

            int subMeshCount = src.subMeshCount;
            var subTriangles = new int[subMeshCount][];
            var subSlot = new int[subMeshCount][];
            var subBone = new int[subMeshCount][];
            var slotTriCount = new int[MonsterShatterParts.Count];

            //실제로 쓰인 본 종류. 소품 메시(무기 등)를 몸통 메시와 구분하는 기준이 된다
            var usedBones = new HashSet<int>();
            int totalTriangles = 0;

            // --- 1패스: 삼각형마다 어느 파트인지 정한다
            for (int s = 0; s < subMeshCount; s++)
            {
                int[] indices = src.GetTriangles(s);
                int triCount = indices.Length / 3;

                subTriangles[s] = indices;
                subSlot[s] = new int[triCount];
                subBone[s] = new int[triCount];
                totalTriangles += triCount;

                for (int t = 0; t < triCount; t++)
                {
                    int i = t * 3;

                    //이 삼각형을 대표할 본 하나 → 그 본이 속한 파트가 곧 이 삼각형의 파트
                    int dominantBone = FindDominantBone(srcWeights[indices[i]], srcWeights[indices[i + 1]], srcWeights[indices[i + 2]]);
                    if (dominantBone < 0 || dominantBone >= boneCount)
                        dominantBone = 0;

                    int slot = bonePart[dominantBone];

                    subSlot[s][t] = slot;
                    subBone[s][t] = dominantBone;
                    slotTriCount[slot]++;
                    usedBones.Add(dominantBone);
                }
            }

            var isChunk = new bool[MonsterShatterParts.Count];
            if (allowChunkSplit)
            {
                SplitWhenTooFewParts(srcVerts, subTriangles, subSlot, slotTriCount, isChunk,
                                     usedBones.Count, totalTriangles, minParts, reservedSlots);
            }

            // --- 2패스: 파트 경계에서 정점을 떼며 새 메시를 만든다

            //소멸용 난수를 정점마다 흩뿌리면 화면에서 노이즈처럼 보인다.
            //위치를 성긴 격자로 양자화해 해시하면 덩어리 단위로 부슬부슬 사라진다
            float blobSize = Mathf.Max(src.bounds.size.magnitude * 0.07f, 1e-4f);

            //(파트 슬롯, 원본 정점 인덱스) → 새 정점 인덱스.
            //같은 파트 안에서는 원본 정점을 공유하고, 파트가 다르면 정점을 새로 뗀다
            var vertexRemap = new Dictionary<(int, int), int>();

            var verts = new List<Vector3>(srcVerts.Length);
            var uvs = new List<Vector2>(hasUV ? srcVerts.Length : 0);
            var boneIndices = new List<int>(srcVerts.Length);

            _pieceBuffer.Clear();

            //파트별 누적: 본 인덱스 → (정점 위치 합, 개수). 사망 포즈의 파트 중심을 구하는 재료다
            var slotBones = new Dictionary<int, Dictionary<int, (Vector3 sum, int count)>>();
            var slotBounds = new Dictionary<int, Bounds>();
            var slotVertexCount = new Dictionary<int, int>();

            var newSubTriangles = new int[subMeshCount][];

            for (int s = 0; s < subMeshCount; s++)
            {
                int[] indices = subTriangles[s];
                var newIndices = new int[indices.Length];

                for (int t = 0; t < subSlot[s].Length; t++)
                {
                    int i = t * 3;
                    int slot = subSlot[s][t];
                    int boneIndex = subBone[s][t];

                    newIndices[i] = Emit(slot, indices[i], boneIndex);
                    newIndices[i + 1] = Emit(slot, indices[i + 1], boneIndex);
                    newIndices[i + 2] = Emit(slot, indices[i + 2], boneIndex);
                }

                newSubTriangles[s] = newIndices;
            }

            // --- 2.5패스: 잘린 단면 막기
            //
            // 파트를 가르면 잘린 자리가 뻥 뚫린 채 남는다. 조각이 굴러 단면이 카메라를 향하면
            // 속이 빈 껍데기가 그대로 보인다. 갈라진 경계 루프를 찾아 부채꼴 뚜껑을 덮어 둔다.
            List<int> capTriangles = FillCuts ? BuildCutCaps() : null;

            if (capTriangles != null && capTriangles.Count > 0)
            {
                //뚜껑은 전부 첫 서브메시에 몰아넣는다. 런타임에 모든 서브메시가 같은 파쇄 머티리얼로
                //교체되므로 어느 서브메시에 들어가든 결과가 같고, 배열을 하나만 늘리면 된다
                int[] baseIndices = newSubTriangles[0];
                var merged = new int[baseIndices.Length + capTriangles.Count];

                System.Array.Copy(baseIndices, merged, baseIndices.Length);
                for (int i = 0; i < capTriangles.Count; i++)
                    merged[baseIndices.Length + i] = capTriangles[i];

                newSubTriangles[0] = merged;
            }

            int vertCount = verts.Count;
            if (vertCount == 0)
                return null;

            var mesh = new Mesh();
            mesh.name = src.name + "_Shatter";
            mesh.indexFormat = vertCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(verts);
            if (hasUV) mesh.SetUVs(0, uvs);
            mesh.SetUVs(2, _pieceBuffer);

            //본 웨이트는 정점당 1본으로 고정한다. 정점당 32B(가중치4+인덱스4) → 8B 로 줄고,
            //어차피 사망 포즈에서 애니메이터를 세워 두므로 가중치 블렌딩이 필요 없다
            var bonesPerVertex = new NativeArray<byte>(vertCount, Allocator.Temp);
            var boneWeights1 = new NativeArray<BoneWeight1>(vertCount, Allocator.Temp);

            for (int i = 0; i < vertCount; i++)
            {
                bonesPerVertex[i] = 1;
                boneWeights1[i] = new BoneWeight1 { boneIndex = boneIndices[i], weight = 1f };
            }

            mesh.SetBoneWeights(bonesPerVertex, boneWeights1);
            mesh.bindposes = bindposes;

            bonesPerVertex.Dispose();
            boneWeights1.Dispose();

            mesh.subMeshCount = subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
                mesh.SetTriangles(newSubTriangles[s], s, false);

            //조각이 원위치를 크게 벗어나므로 원본 bounds 로는 곧바로 컬링돼 사라진다.
            //넉넉히 부풀린 bounds 를 직접 지정하고 재계산은 하지 않는다
            Bounds meshBounds = src.bounds;
            meshBounds.Expand(meshBounds.size.magnitude * 3f);
            mesh.bounds = meshBounds;

            //CPU 사본은 더 필요 없다. GPU 로만 올려 메모리를 절반으로 줄인다
            mesh.UploadMeshData(true);

            //파트 정보 확정
            var slots = new ShatterSlot[MonsterShatterParts.Count];
            int partCount = 0;

            foreach (KeyValuePair<int, Dictionary<int, (Vector3 sum, int count)>> pair in slotBones)
            {
                int slot = pair.Key;
                var boneList = new List<ShatterSlotBone>(pair.Value.Count);

                foreach (KeyValuePair<int, (Vector3 sum, int count)> boneEntry in pair.Value)
                {
                    if (boneEntry.Value.count <= 0)
                        continue;

                    Vector3 centroid = boneEntry.Value.sum / boneEntry.Value.count;

                    boneList.Add(new ShatterSlotBone
                    {
                        BoneIndex = boneEntry.Key,
                        //bindpose 를 미리 곱해 본 로컬 좌표로 바꿔 둔다
                        BoneLocalCentroid = bindposes[boneEntry.Key].MultiplyPoint3x4(centroid),
                        VertexCount = boneEntry.Value.count,
                    });
                }

                if (boneList.Count == 0)
                    continue;

                Vector3 size = slotBounds[slot].size;
                float thin = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
                float thick = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

                //가장 정점이 많은 본을 대표로 삼는다. 장축을 월드로 옮길 때 이 본의 행렬을 쓴다
                ShatterSlotBone heaviest = boneList[0];
                for (int i = 1; i < boneList.Count; i++)
                {
                    if (boneList[i].VertexCount > heaviest.VertexCount)
                        heaviest = boneList[i];
                }

                Vector3 longAxis = LongestAxis(size, out float elongation);

                slots[slot] = new ShatterSlot
                {
                    Part = (MonsterShatterPart)slot,
                    Bones = boneList.ToArray(),
                    //지면에 누웠을 때 파묻히지 않을 두께. 가장 얇은 축이 기준이지만
                    //납작한 조각이 지면에 박히지 않도록 최소치를 걸어 둔다
                    Radius = Mathf.Max(thin, thick * 0.25f) * 0.5f,
                    VertexCount = slotVertexCount[slot],
                    IsChunk = isChunk[slot],
                    LongAxisBoneLocal = bindposes[heaviest.BoneIndex].MultiplyVector(longAxis),
                    LongAxisBone = heaviest.BoneIndex,
                    //둥근 조각은 어떻게 눕든 티가 안 나므로 보정하지 않는다
                    Flatten = Mathf.Clamp01((elongation - 1.6f) / 1.4f),
                };

                partCount++;
            }

            return new ShatterMeshData
            {
                Mesh = mesh,
                Slots = slots,
                PartCount = partCount,
            };

            int Emit(int slot, int srcIndex, int boneIndex)
            {
                var key = (slot, srcIndex);
                if (vertexRemap.TryGetValue(key, out int existing))
                    return existing;

                int newIndex = verts.Count;
                Vector3 v = srcVerts[srcIndex];

                verts.Add(v);
                if (hasUV) uvs.Add(srcUV[srcIndex]);
                boneIndices.Add(boneIndex);
                _pieceBuffer.Add(new Vector2(slot, BlobHash(v, blobSize)));

                vertexRemap[key] = newIndex;

                //파트 중심/크기 재료 누적
                if (slotBones.TryGetValue(slot, out Dictionary<int, (Vector3 sum, int count)> perBone) == false)
                {
                    perBone = new Dictionary<int, (Vector3, int)>();
                    slotBones[slot] = perBone;
                    slotBounds[slot] = new Bounds(v, Vector3.zero);
                    slotVertexCount[slot] = 0;
                }
                else
                {
                    Bounds bounds = slotBounds[slot];
                    bounds.Encapsulate(v);
                    slotBounds[slot] = bounds;
                }

                perBone.TryGetValue(boneIndex, out (Vector3 sum, int count) acc);
                perBone[boneIndex] = (acc.sum + v, acc.count + 1);
                slotVertexCount[slot] = slotVertexCount[slot] + 1;

                return newIndex;
            }

            //원본에 없던 정점(뚜껑 중심)을 하나 만든다. 파트 안쪽이라 bounds 는 건드리지 않는다
            int EmitSynthetic(int slot, Vector3 position, Vector2 uv, int boneIndex)
            {
                int newIndex = verts.Count;

                verts.Add(position);
                if (hasUV) uvs.Add(uv);
                boneIndices.Add(boneIndex);
                _pieceBuffer.Add(new Vector2(slot, BlobHash(position, blobSize)));

                Dictionary<int, (Vector3 sum, int count)> perBone = slotBones[slot];
                perBone.TryGetValue(boneIndex, out (Vector3 sum, int count) acc);
                perBone[boneIndex] = (acc.sum + position, acc.count + 1);
                slotVertexCount[slot] = slotVertexCount[slot] + 1;

                return newIndex;
            }

            //갈라진 자리를 찾아 뚜껑 삼각형을 만든다.
            //
            //간선 하나를 공유하는 두 삼각형의 파트가 다르면 그 간선이 곧 잘린 자리다.
            //파트별로 그런 간선을 모으면 방향이 일관된 닫힌 루프가 되고 (표면을 면 단위로 나눈
            //결과이므로 경계는 반드시 닫힌다), 루프 중심으로 부채꼴을 깔면 단면이 막힌다.
            //
            //원본 메시가 처음부터 열려 있던 가장자리(망토·머리카락 같은 한 겹 판)는 간선을
            //삼각형 하나만 쓰므로 여기 걸리지 않는다. 그쪽은 막을 단면 자체가 없어
            //셰이더의 Cull Off 로 뒷면을 그려 해결한다
            List<int> BuildCutCaps()
            {
                //정점이 UV 이음매에서 갈라져 있어도 같은 자리는 한 점으로 본다.
                //FBX 임포터가 위치는 그대로 복사하므로 정확 비교로 충분하다
                var weld = new int[srcVerts.Length];
                var weldMap = new Dictionary<Vector3, int>(srcVerts.Length);

                for (int i = 0; i < srcVerts.Length; i++)
                {
                    if (weldMap.TryGetValue(srcVerts[i], out int id) == false)
                    {
                        id = weldMap.Count;
                        weldMap[srcVerts[i]] = id;
                    }

                    weld[i] = id;
                }

                //간선 → 아직 짝을 못 만난 삼각형. 짝이 오면 두 파트를 비교한다
                var pending = new Dictionary<long, (int slot, int a, int b)>();

                //파트별 방향 경계 간선: 시작 웰드 정점 → (끝 웰드 정점, 원본 정점 쌍)
                var cuts = new Dictionary<int, Dictionary<int, (int to, int srcFrom, int srcTo)>>();

                for (int s = 0; s < subSlot.Length; s++)
                {
                    int[] indices = subTriangles[s];

                    for (int t = 0; t < subSlot[s].Length; t++)
                    {
                        int slot = subSlot[s][t];
                        int i = t * 3;

                        for (int e = 0; e < 3; e++)
                        {
                            int ia = indices[i + e];
                            int ib = indices[i + (e + 1) % 3];
                            int wa = weld[ia];
                            int wb = weld[ib];

                            if (wa == wb)
                                continue;

                            long key = wa < wb
                                ? ((long)wa << 32) | (uint)wb
                                : ((long)wb << 32) | (uint)wa;

                            if (pending.TryGetValue(key, out (int slot, int a, int b) first) == false)
                            {
                                pending[key] = (slot, ia, ib);
                                continue;
                            }

                            //짝을 찾았으니 목록에서 뺀다 (간선 하나를 셋 이상이 공유해도 폭주하지 않는다)
                            pending.Remove(key);

                            if (first.slot == slot)
                                continue;

                            //양쪽 파트 모두 자기 winding 방향으로 경계 간선을 하나씩 얻는다
                            AddCut(slot, wa, wb, ia, ib);
                            AddCut(first.slot, weld[first.a], weld[first.b], first.a, first.b);
                        }
                    }
                }

                if (cuts.Count == 0)
                    return null;

                var triangles = new List<int>(256);
                var loopSrc = new List<int>(64);

                foreach (KeyValuePair<int, Dictionary<int, (int to, int srcFrom, int srcTo)>> pair in cuts)
                {
                    Dictionary<int, (int to, int srcFrom, int srcTo)> map = pair.Value;
                    var visited = new HashSet<int>();

                    foreach (int start in map.Keys)
                    {
                        if (visited.Contains(start))
                            continue;

                        loopSrc.Clear();

                        int cursor = start;
                        bool closed = false;

                        while (map.TryGetValue(cursor, out (int to, int srcFrom, int srcTo) edge))
                        {
                            //이미 지난 정점으로 되돌아왔는데 시작점이 아니면 8자로 얽힌 경계다. 포기한다
                            if (visited.Add(cursor) == false)
                                break;

                            loopSrc.Add(edge.srcFrom);
                            cursor = edge.to;

                            if (cursor == start)
                            {
                                closed = true;
                                break;
                            }
                        }

                        if (closed == false || loopSrc.Count < 3)
                            continue;

                        AppendCap(pair.Key, loopSrc, triangles);
                    }
                }

                return triangles.Count > 0 ? triangles : null;

                void AddCut(int slot, int from, int to, int srcFrom, int srcTo)
                {
                    if (cuts.TryGetValue(slot, out Dictionary<int, (int to, int srcFrom, int srcTo)> map) == false)
                    {
                        map = new Dictionary<int, (int, int, int)>();
                        cuts[slot] = map;
                    }

                    //한 정점에서 경계가 여러 갈래로 갈리는 비다양체는 먼저 만난 쪽만 따라간다
                    if (map.ContainsKey(from) == false)
                        map[from] = (to, srcFrom, srcTo);
                }
            }

            //경계 루프 하나를 중심 부채꼴로 덮는다
            void AppendCap(int slot, List<int> loopSrc, List<int> triangles)
            {
                int n = loopSrc.Count;

                Vector3 center = Vector3.zero;
                Vector2 uv = Vector2.zero;

                for (int i = 0; i < n; i++)
                {
                    center += srcVerts[loopSrc[i]];
                    if (hasUV) uv += srcUV[loopSrc[i]];
                }

                center /= n;
                if (hasUV) uv /= n;

                //경계가 크게 휘어 있으면 평균 중심이 표면 밖으로 삐져나온다.
                //파트 안쪽으로 살짝 당겨 두면 튀어나오지 않고 상처처럼 옴폭 들어가 보인다
                float loopRadius = 0f;
                for (int i = 0; i < n; i++)
                    loopRadius = Mathf.Max(loopRadius, Vector3.Distance(srcVerts[loopSrc[i]], center));

                Vector3 slotCenter = slotBounds[slot].center;
                Vector3 inward = slotCenter - center;
                float distance = inward.magnitude;

                if (distance > 1e-5f)
                    center += inward * (Mathf.Min(loopRadius * 0.15f, distance) / distance);

                //뉴웰 법선으로 부채꼴 방향을 정해 파트 바깥을 보게 한다.
                //셰이더가 Cull Off 라 뒤집혀도 보이긴 하지만, 메시 자체는 앞뒤가 맞아야 나중에 곤란하지 않다
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < n; i++)
                    normal += Vector3.Cross(srcVerts[loopSrc[i]], srcVerts[loopSrc[(i + 1) % n]]);

                bool flip = Vector3.Dot(normal, center - slotCenter) < 0f;

                int centerIndex = EmitSynthetic(slot, center, uv, DominantLoopBone(slot, loopSrc));

                for (int i = 0; i < n; i++)
                {
                    int a = vertexRemap[(slot, loopSrc[i])];
                    int b = vertexRemap[(slot, loopSrc[(i + 1) % n])];

                    triangles.Add(centerIndex);
                    triangles.Add(flip ? b : a);
                    triangles.Add(flip ? a : b);
                }
            }

            //뚜껑 중심 정점이 따라갈 본. 경계에서 가장 많이 쓰인 본을 고른다.
            //사망 포즈에서 애니메이터가 멈춰 있어 스키닝은 정적이지만,
            //엉뚱한 본을 잡으면 파쇄 시작 직전 한 프레임이 튄다
            int DominantLoopBone(int slot, List<int> loopSrc)
            {
                int best = boneIndices[vertexRemap[(slot, loopSrc[0])]];
                int bestCount = 0;

                //루프 길이가 짧아(보통 수십) 딕셔너리를 만드는 것보다 그냥 세는 편이 싸다
                for (int i = 0; i < loopSrc.Count; i++)
                {
                    int candidate = boneIndices[vertexRemap[(slot, loopSrc[i])]];
                    int count = 0;

                    for (int j = 0; j < loopSrc.Count; j++)
                    {
                        if (boneIndices[vertexRemap[(slot, loopSrc[j])]] == candidate)
                            count++;
                    }

                    if (count > bestCount)
                    {
                        bestCount = count;
                        best = candidate;
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// 이름만으로는 파트가 거의 안 갈리는 리그를 위한 폴백.
        ///
        /// 슬라임은 팔다리가 아예 없고, 비홀더는 촉수 6가닥이 전부 Tentacle* 라 몸통 한 덩어리가 된다.
        /// 이런 경우 가장 큰 파트를 절반씩 갈라 목표 개수를 채운다.
        /// 파트가 이미 충분히 갈린 몬스터는 이 함수를 그대로 통과한다.
        ///
        /// 한 번에 하나씩만 늘리는 것이 중요하다. 예전에는 옥탄트(8분면)로 한꺼번에 갈랐는데,
        /// 한 번 돌 때마다 조각이 최대 7개씩 늘어 목표가 5든 3이든 결과가 늘 8~11개로 넘쳐 버렸다.
        /// 지금은 가장 큰 조각을 그 조각의 장축 기준 절반으로만 자르고 개수를 다시 확인한다.
        /// 반평면으로 자르므로 갈라진 두 조각이 공간적으로 붙어 있는 것도 보장된다
        /// (옥탄트는 마주 보는 두 사분면이 한 슬롯에 들어가 조각 하나가 두 덩이로 갈라질 수 있었다).
        ///
        /// 공간으로 잘라도 문제가 없는 이유는, 조각이 여러 본에 걸쳐 있어도 사망 포즈에서
        /// 얼어붙은 채 강체로 움직이기 때문이다. 파트 중심은 본별 무게중심을 합산해 구하므로
        /// 본이 몇 개든 정확하다.
        ///
        /// 호출 여부와 목표 개수는 <see cref="PrepareRoot"/> 가 몬스터 전체를 보고 정한다.
        /// reservedSlots 는 같은 몬스터의 다른 메시가 이미 쓰고 있는 슬롯이라 덩어리가 빌려 쓰면 안 된다.
        /// </summary>
        private static void SplitWhenTooFewParts(Vector3[] srcVerts, int[][] subTriangles, int[][] subSlot,
                                                 int[] slotTriCount, bool[] isChunk,
                                                 int usedBoneCount, int totalTriangles, int minParts, bool[] reservedSlots)
        {
            //소품 메시는 그대로 둔다. 몸통 메시는 본을 수십 개 쓰지만 무기·장식은 한두 개뿐이다
            if (usedBoneCount < 6 || totalTriangles < 200)
                return;

            int partCount = 0;
            for (int i = 0; i < slotTriCount.Length; i++)
            {
                if (slotTriCount[i] > 0)
                    partCount++;
            }

            //무기 슬롯은 비워 둔다. 무기 MeshRenderer 가 런타임에 이 슬롯을 강제로 쓰기 때문에
            //덩어리를 넣으면 무기와 한 몸으로 날아간다
            int weaponSlot = (int)MonsterShatterPart.Weapon;

            while (partCount < minParts)
            {
                //가장 큰 파트를 고른다
                int big = 0;
                for (int i = 1; i < slotTriCount.Length; i++)
                {
                    if (slotTriCount[i] > slotTriCount[big])
                        big = i;
                }

                //너무 작으면 더 갈라 봐야 파편만 남는다
                if (slotTriCount[big] < 32)
                    break;

                //뒤쪽 슬롯(천·날개·꼬리)부터 빌려 쓴다. 이름은 어차피 표시용이라
                //의미가 덜한 쪽부터 쓰는 편이 데모 표기가 덜 헷갈린다
                int spare = -1;
                for (int i = slotTriCount.Length - 1; i >= 0; i--)
                {
                    if (i == weaponSlot || slotTriCount[i] > 0)
                        continue;

                    //같은 몬스터의 다른 메시가 쓰는 슬롯은 건드리지 않는다.
                    //겹치면 두 조각이 같은 궤적 상수를 읽어 한 몸처럼 붙어 날아간다
                    if (reservedSlots != null && i < reservedSlots.Length && reservedSlots[i])
                        continue;

                    spare = i;
                    break;
                }

                if (spare < 0)
                    break;

                //이 조각의 무게중심과 퍼진 범위 — 가장 길게 뻗은 축으로 잘라야 두 쪽이 고르게 나뉜다
                Vector3 center = Vector3.zero;
                var bounds = new Bounds();
                int sampled = 0;

                for (int s = 0; s < subSlot.Length; s++)
                {
                    for (int t = 0; t < subSlot[s].Length; t++)
                    {
                        if (subSlot[s][t] != big)
                            continue;

                        Vector3 c = TriangleCenter(srcVerts, subTriangles[s], t);
                        center += c;

                        if (sampled == 0)
                            bounds = new Bounds(c, Vector3.zero);
                        else
                            bounds.Encapsulate(c);

                        sampled++;
                    }
                }

                if (sampled == 0)
                    break;

                center /= sampled;
                Vector3 axis = LongestAxis(bounds.size, out float _);

                int moved = 0;
                for (int s = 0; s < subSlot.Length; s++)
                {
                    for (int t = 0; t < subSlot[s].Length; t++)
                    {
                        if (subSlot[s][t] != big)
                            continue;

                        if (Vector3.Dot(TriangleCenter(srcVerts, subTriangles[s], t) - center, axis) < 0f)
                            continue;

                        subSlot[s][t] = spare;
                        moved++;
                    }
                }

                //한쪽으로 다 쏠렸으면 더 자를 수 없는 모양이다
                if (moved == 0 || moved == slotTriCount[big])
                    break;

                slotTriCount[spare] = moved;
                slotTriCount[big] -= moved;
                isChunk[big] = true;
                isChunk[spare] = true;
                partCount++;
            }
        }

        /// <summary>
        /// 바운즈에서 가장 긴 축과 "길쭉한 정도"(최장변 / 중간변)를 뽑는다.
        ///
        /// 바운즈는 축 정렬이라 대각선으로 누운 조각은 제대로 못 잡지만, 그런 경우엔 세 변이 비슷해져
        /// 길쭉함이 1에 가까워지고 보정이 저절로 꺼진다. 틀린 축으로 억지 보정하는 일은 생기지 않는다.
        /// </summary>
        public static Vector3 LongestAxis(Vector3 size, out float elongation)
        {
            float x = Mathf.Abs(size.x);
            float y = Mathf.Abs(size.y);
            float z = Mathf.Abs(size.z);

            Vector3 axis;
            float longest, middle;

            if (x >= y && x >= z)
            {
                axis = Vector3.right;
                longest = x;
                middle = Mathf.Max(y, z);
            }
            else if (y >= z)
            {
                axis = Vector3.up;
                longest = y;
                middle = Mathf.Max(x, z);
            }
            else
            {
                axis = Vector3.forward;
                longest = z;
                middle = Mathf.Max(x, y);
            }

            elongation = longest / Mathf.Max(middle, 1e-5f);
            return axis;
        }

        private static Vector3 TriangleCenter(Vector3[] verts, int[] indices, int triangle)
        {
            int i = triangle * 3;
            return (verts[indices[i]] + verts[indices[i + 1]] + verts[indices[i + 2]]) * (1f / 3f);
        }

        /// <summary>위치를 성긴 격자로 양자화해 0~1 해시. 같은 덩어리는 같은 값을 받는다.</summary>
        private static float BlobHash(Vector3 v, float cellSize)
        {
            int x = Mathf.FloorToInt(v.x / cellSize);
            int y = Mathf.FloorToInt(v.y / cellSize);
            int z = Mathf.FloorToInt(v.z / cellSize);

            //전투 난수 시퀀스를 건드리면 스테이지 해시 동기화가 어긋나므로
            //UnityEngine.Random 을 쓰지 않고 순수 해시로 만든다
            int h = x * 73856093 ^ y * 19349663 ^ z * 83492791;
            h &= 0x7fffffff;

            return (h % 9973) / 9973f;
        }

        /// <summary>삼각형 세 정점 중 가장 무거운 본 인덱스를 고른다.</summary>
        private static int FindDominantBone(BoneWeight a, BoneWeight b, BoneWeight c)
        {
            int bestIndex = a.boneIndex0;
            float bestWeight = 0f;

            AccumulateBest(a);
            AccumulateBest(b);
            AccumulateBest(c);

            return bestIndex;

            //정점 하나가 가진 최대 4본 중 가장 무거운 것을 후보로 올린다.
            //본별 합산 딕셔너리를 쓰면 정확하지만 삼각형마다 할당이 생긴다.
            //조각이 어느 본을 따라갈지만 정하면 되므로 최대 가중치 비교로 충분하다
            void AccumulateBest(BoneWeight w)
            {
                if (w.weight0 > bestWeight) { bestWeight = w.weight0; bestIndex = w.boneIndex0; }
                if (w.weight1 > bestWeight) { bestWeight = w.weight1; bestIndex = w.boneIndex1; }
                if (w.weight2 > bestWeight) { bestWeight = w.weight2; bestIndex = w.boneIndex2; }
                if (w.weight3 > bestWeight) { bestWeight = w.weight3; bestIndex = w.boneIndex3; }
            }
        }
    }
}
