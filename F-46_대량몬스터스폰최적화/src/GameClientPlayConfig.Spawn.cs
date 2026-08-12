using UnityEngine;
using System;
using Sirenix.OdinInspector;

namespace PX
{
    /// <summary>
    /// 몬스터 스폰 설정 — 어디에 뿌리고(배치), 얼마나 흩고(분산), 어떻게 접근시키고, 언제 깨우는지
    /// </summary>
    [Serializable]
    public class SpawnSettings
    {
        [BoxGroup("스폰 배치")]
        [LabelText("플레이어 시작 위치 오프셋")]
        [PropertyRange(0, 13)]
        [SuffixLabel("타일", overlay: true)]
        [Tooltip("플레이어 시작 지점을 필드 중앙에서 11시 방향(화면 좌측 위) 가장자리로 몇 타일 밀어낼지.\n" +
                 "0이면 정중앙 — 이 경우 네 스폰 지점까지 거리가 모두 약 22m로 같아 첫 웨이브가 코앞에서 시작됩니다.\n" +
                 "값을 키우면 반대편(5시 방향) 스폰 지점까지 거리가 최대 43m까지 벌어집니다")]
        public int playerSpawnTileOffset = 10;

        [BoxGroup("스폰 배치")]
        [LabelText("스폰 지점 최소 거리")]
        [PropertyRange(0f, 40f)]
        [SuffixLabel("m", overlay: true)]
        [Tooltip("플레이어로부터 이 거리보다 가까운 스폰 지점은 후보에서 빼고 가장 먼 지점으로 교체합니다.\n" +
                 "0이면 비활성 (기존처럼 네 지점을 그대로 순환)")]
        public float spawnMinDistanceFromPlayer = 25f;

        [BoxGroup("스폰 배치")]
        [LabelText("스폰 지점 랜덤 선택")]
        [Tooltip("웨이브마다 네 모서리 중 무작위로 스폰 지점을 고릅니다. 직전 웨이브가 쓴 지점은 후보에서 빠지므로 2연속 같은 자리는 나오지 않습니다.\n" +
                 "끄면 예전처럼 후보 리스트를 순서대로 도는 방식이 됩니다.\n\n" +
                 "위 '스폰 지점 최소 거리'로 걸러 후보가 한 곳만 남으면 매 웨이브 같은 자리에서만 나오게 되므로,\n" +
                 "그럴 때는 거리 조건을 풀고 네 지점 전체에서 뽑습니다.\n" +
                 "방향을 제한하고 싶으면 최소 거리를 낮춰 후보가 2곳 이상 남게 하세요")]
        public bool spawnPointRandomPick = true;

        [BoxGroup("스폰 배치")]
        [LabelText("첫 웨이브는 항상 최원거리")]
        [Tooltip("스테이지 첫 웨이브를 플레이어에게서 가장 먼 스폰 지점에서 소환합니다.\n" +
                 "플레이어를 좌측 가장자리에 두면 첫 웨이브가 우측에서 부채꼴로 밀려옵니다")]
        public bool firstWaveUseFarthestSpawn = true;

        [BoxGroup("스폰 분산")]
        [LabelText("스폰 분산 사용")]
        [Tooltip("끄면 기존 방식(스폰 타일 중심 ±3m 균등 랜덤)으로 동작합니다")]
        public bool enableSpawnScatter = true;

        [BoxGroup("스폰 분산")]
        [LabelText("최대 분산 반경")]
        [PropertyRange(1f, 15f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("스폰 타일 중심으로부터 몬스터가 퍼지는 최대 거리. 전장 타일 1칸 = 1.1m, 전체 필드는 30x30칸(약 33m)")]
        public float spawnScatterRadius = 6.0f;

        [BoxGroup("스폰 분산")]
        [LabelText("최소 분산 반경")]
        [PropertyRange(0f, 5f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("첫 번째 몬스터가 스폰 타일 중심에서 떨어지는 거리")]
        public float spawnScatterMinRadius = 0.8f;

        [BoxGroup("스폰 분산")]
        [LabelText("분산 슬롯 주기")]
        [PropertyRange(4, 200)]
        [SuffixLabel("마리", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("이 마릿수까지 최대 반경에 걸쳐 균등 분산됩니다. 그룹당 몬스터 수(시트데이터 기본 20마리)와 맞추면 가장 고르게 퍼집니다. " +
                 "[1] 스테이지 시스템 > 테스트 & 디버그 에서 스폰 수를 바꿨다면 그 값에 맞춰주세요. " +
                 "초과분은 최대 반경 링에 각도만 달리해 늘어섭니다")]
        public int spawnScatterSlotCycle = 20;

        [BoxGroup("스폰 분산")]
        [LabelText("분산 각도")]
        [PropertyRange(30f, 360f)]
        [SuffixLabel("도", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("스폰 지점을 중심으로 몬스터가 퍼지는 각도 범위. 필드 안쪽 방향이 부채꼴의 중심축입니다.\n" +
                 "360이면 사방 원형 분산 — 절반이 필드 밖/플레이어 등 뒤로 가고 한가운데 파고들면 둘러싸입니다.\n" +
                 "값을 줄이면 필드 안쪽을 향한 부채꼴이 되어, 다가오는 플레이어가 앞줄부터 차례로 맞물립니다")]
        public float spawnScatterArcAngle = 140f;

        [BoxGroup("스폰 분산")]
        [LabelText("위치 지터")]
        [PropertyRange(0f, 2f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("계산된 배치 지점에 더해지는 무작위 흔들림. 0이면 매번 완전히 동일한 대형으로 스폰됩니다")]
        public float spawnScatterJitter = 0.3f;

        [BoxGroup("스폰 분산")]
        [LabelText("필드 안쪽 보정")]
        [PropertyRange(0f, 10f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("스폰 지점이 필드 모서리라 분산 원의 절반이 필드 밖으로 나가는 것을 막기 위해, 배치 전체를 필드 중앙 쪽으로 밀어주는 거리")]
        public float spawnScatterInwardBias = 3.5f;

        [BoxGroup("스폰 분산")]
        [LabelText("플레이어 최소 이격 거리")]
        [PropertyRange(0f, 20f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("분산 배치가 플레이어 쪽으로 이 거리보다 가까이 파고들지 못하게 막습니다.\n" +
                 "플레이어를 향하는 방향의 반경만 잘라내므로 좌우로 퍼지는 폭은 그대로 유지되고, 플레이어 쪽만 눌린 초승달 모양이 됩니다.\n" +
                 "전장이 30x30타일(약 33m)이라 플레이어가 한가운데 있으면 스폰 지점까지 최대 23m뿐입니다.\n" +
                 "따라서 이 값 + 필드 안쪽 보정이 23m에 가까워질수록 무리가 스폰 지점 근처로 몰립니다. 0이면 비활성")]
        public float spawnMinGapFromPlayer = 10f;

        [BoxGroup("스폰 분산")]
        [LabelText("NavMesh 스냅 거리")]
        [PropertyRange(0f, 10f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("분산된 위치가 이동 가능 영역 밖이면 이 거리 안에서 가장 가까운 NavMesh 지점으로 보정합니다. 0이면 보정하지 않습니다.\n" +
                 "배치 계산이 이미 필드 안쪽 보정과 플레이어 이격 축소를 거치므로 대부분 NavMesh 위에 떨어집니다.\n" +
                 "반경이 크면 탐색 범위가 넓어져 웨이브당 마릿수만큼(예: 60회) 비용을 냅니다. 실패한 소수는 아래 폴백이 받아냅니다")]
        public float spawnNavSampleDistance = 2.0f;

        [BoxGroup("스폰 분산")]
        [LabelText("NavMesh 스냅 폴백 거리")]
        [PropertyRange(0f, 20f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enableSpawnScatter")]
        [Tooltip("좁은 반경으로 못 찾았을 때만 이 거리로 한 번 더 시도합니다. 0이면 폴백하지 않습니다.\n" +
                 "NavMesh 위에 못 올라간 개체는 isOnNavMesh 가 false 가 되어 영구히 움직이지 않으므로 폴백을 끄지 마세요")]
        public float spawnNavSampleFallbackDistance = 10.0f;

        [BoxGroup("접근 분산")]
        [LabelText("접근 분산 사용")]
        [Tooltip("끄면 기존 방식(전원이 타겟 좌표를 그대로 목적지로 사용)으로 동작합니다. 몬스터에만 적용되며 플레이어/펫은 항상 기존 동작입니다")]
        public bool enableApproachSpread = true;

        [BoxGroup("접근 분산")]
        [LabelText("접근 각도 분산")]
        [PropertyRange(0f, 90f)]
        [SuffixLabel("도", overlay: true)]
        [EnableIf("enableApproachSpread")]
        [Tooltip("몬스터마다 고유하게 부여되는 접근 방향 오프셋(±). 클수록 넓게 포위하지만 너무 크면 타겟 주위를 크게 돌아 들어옵니다")]
        public float approachAngleSpread = 40f;

        [BoxGroup("접근 분산")]
        [LabelText("정지 링 비율")]
        [PropertyRange(0.3f, 0.95f)]
        [SuffixLabel("x", overlay: true)]
        [EnableIf("enableApproachSpread")]
        [Tooltip("공격 사거리 대비 목적지 링 반경. 반드시 1 미만이어야 사거리 안에서 멈춰 공격합니다")]
        public float approachRingRatio = 0.85f;

        [BoxGroup("접근 분산")]
        [LabelText("정지 링 최소 반경")]
        [PropertyRange(0f, 3f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enableApproachSpread")]
        [Tooltip("사거리가 매우 짧은 근접 몬스터도 최소한 이 반경의 링에 늘어서게 합니다")]
        public float approachRingMin = 0.8f;

        [BoxGroup("접근 분산")]
        [LabelText("정지 링 편차")]
        [PropertyRange(0f, 0.6f)]
        [EnableIf("enableApproachSpread")]
        [Tooltip("몬스터마다 링 반경을 이 비율만큼 줄여 앞뒤 층을 만듭니다. 0이면 전원이 정확히 같은 반경의 원에 섭니다")]
        public float approachRingVariance = 0.3f;

        [BoxGroup("상호 회피")]
        [LabelText("회피 우선순위 지터")]
        [PropertyRange(0, 40)]
        [Tooltip("일반 몬스터의 NavMesh 회피 우선순위를 개체마다 ± 이 범위로 흩뜨립니다. 우선순위가 전부 같으면 서로 비켜주지 못하고 교착되어 뭉칩니다")]
        public int avoidancePriorityJitter = 20;

        [BoxGroup("상호 회피")]
        [LabelText("회피 품질")]
        [Tooltip("NavMeshAgent의 obstacleAvoidanceType. 높일수록 서로 잘 비켜가지만 CPU 비용이 늘어납니다")]
        public UnityEngine.AI.ObstacleAvoidanceType obstacleAvoidanceType =
            UnityEngine.AI.ObstacleAvoidanceType.LowQualityObstacleAvoidance;

        [BoxGroup("무리 각성")]
        [LabelText("무리 각성 사용")]
        [Tooltip("끄면 예전처럼 일반 몬스터가 소환 즉시 플레이어를 추적합니다.\n" +
                 "런타임에 꺼도 이미 잠들어 있던 개체는 다음 프레임에 즉시 깨어납니다")]
        public bool enablePackSleep = true;

        [BoxGroup("무리 각성")]
        [LabelText("각성 반경")]
        [PropertyRange(3f, 25f)]
        [SuffixLabel("m", overlay: true)]
        [EnableIf("enablePackSleep")]
        [Tooltip("잠든 몬스터가 플레이어를 인식하는 거리. 무리 중 한 마리라도 인식하면 웨이브 전원이 깨어납니다.\n\n" +
                 "이 값이 곧 몬스터가 달리는 거리이고, 달리는 거리가 대형을 흐트러뜨립니다.\n" +
                 "전장은 30x30타일(약 33m)이고 새 무리의 앞줄은 플레이어에게서 약 15.5m 지점에 섭니다\n" +
                 "(스폰 타일 25m - 필드 안쪽 보정 3.5m - 분산 반경 6m).\n" +
                 "따라서 15 이상으로 두면 소환과 거의 동시에 깨어나 예전과 같아집니다. 6~8이 적정선입니다")]
        public float packWakeRadius = 8f;

        [BoxGroup("무리 각성")]
        [LabelText("각성 전파 지연")]
        [PropertyRange(0f, 1f)]
        [SuffixLabel("초", overlay: true)]
        [EnableIf("enablePackSleep")]
        [Tooltip("무리가 깨어날 때 개체마다 0~이 값 사이의 무작위 지연을 줍니다.\n" +
                 "0이면 전원이 정확히 같은 프레임에 돌아서 기계적으로 보입니다.\n" +
                 "플레이어를 처음 발견한 개체는 지연 없이 즉시 반응합니다")]
        public float packWakeSpreadDelay = 0.3f;

        [BoxGroup("무리 각성")]
        [LabelText("잠든 개체 에이전트 끄기")]
        [EnableIf("enablePackSleep")]
        [Tooltip("잠든 동안 NavMeshAgent를 꺼서 회피 시뮬레이션 비용을 없앱니다.\n" +
                 "각성 시 다시 켜며, 그 사이 사망 넉백 등으로 이동 불가 영역에 밀려났으면 가장 가까운 NavMesh 지점으로 스냅합니다.\n" +
                 "켜는 비용이 문제가 되면 끄세요 — 그래도 각성 동작 자체는 동일합니다")]
        public bool packSleepDisableAgent = true;

        [BoxGroup("스폰 연출 랜덤")]
        [LabelText("스폰 시 애니메이션 위상 랜덤")]
        [Tooltip("소환된 개체의 대기 모션 시작 지점을 개체마다 다르게 흩뜨립니다.\n" +
                 "끄면 한 웨이브 전원이 같은 프레임에 맞춰 움직이는 로봇 군무가 됩니다.\n" +
                 "켜고 끄는 것과 무관하게 대기 상태는 명시적으로 지정되므로, 풀에서 돌아온 개체가 시체 포즈로 나오지 않습니다")]
        public bool enableSpawnAnimatorPhaseRandom = true;

        [BoxGroup("스폰 연출 랜덤")]
        [LabelText("이동 모션 위상 랜덤")]
        [Tooltip("소환된 개체가 처음 달리기 시작할 때 모션 시작 지점을 개체마다 다르게 잡습니다.\n" +
                 "대기 위상만 흩뜨리면 IsRun 전이가 걸리는 순간 전원이 달리기 0프레임으로 다시 정렬되어,\n" +
                 "웨이브 전체가 발을 맞춰 행진하는 것처럼 보입니다.\n" +
                 "소환 후 첫 이동 1회에만 적용되므로 전투 중 멈췄다 다시 뛸 때 모션이 튀지 않습니다")]
        public bool enableRunAnimatorPhaseRandom = true;

        [BoxGroup("스폰 연출 랜덤")]
        [LabelText("개체별 재생 속도 편차")]
        [PropertyRange(0f, 0.3f)]
        [SuffixLabel("± 비율", overlay: true)]
        [Tooltip("개체마다 대기/이동 모션 재생 속도를 이 비율 범위에서 다르게 줍니다 (0.1 = 90%~110%).\n" +
                 "위상만 흩뜨리면 속도가 같아 상대 간격이 영원히 유지되지만, 속도가 조금씩 다르면 시간이 갈수록 더 흩어집니다.\n" +
                 "스킬/공격 모션에는 적용되지 않습니다 — 시전 속도는 전투 타이밍과 직결되기 때문입니다.\n" +
                 "너무 키우면 발이 미끄러지는 느낌이 납니다. 0이면 비활성")]
        public float animatorSpeedJitterRange = 0.1f;

        /// <summary>황금비 역수. 각도 오프셋 산출용</summary>
        public const float SlotSeedAngle = 0.6180339887f;

        /// <summary>플라스틱수 기반 무리수. 링 반경 편차 산출용(각도와 상관관계가 생기지 않도록 다른 값 사용)</summary>
        public const float SlotSeedRing = 0.7548776662f;

        /// <summary>황금각(도). 스폰 나선 배치용</summary>
        public const float GoldenAngleDeg = 137.508f;

        /// <summary>
        /// 슬롯 인덱스로부터 0~1 사이의 저불일치(low-discrepancy) 값을 만듭니다.
        /// 무리수 배수의 소수부를 쓰므로 인접한 슬롯끼리 값이 최대한 벌어집니다.
        /// </summary>
        public static float GetSlotRatio(int InSlotIndex, float InIrrational)
        {
            float value = (InSlotIndex + 0.5f) * InIrrational;
            return value - Mathf.Floor(value);
        }
    }
}
