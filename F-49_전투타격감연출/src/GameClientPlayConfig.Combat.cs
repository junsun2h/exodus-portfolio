using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

namespace PX
{
    /// <summary>
    /// 스킬 하나에 지정하는 카메라 흔들림 세기.
    /// </summary>
    [Serializable]
    public class FCameraShakeSkill
    {
        [HorizontalGroup(0.68f)]
        [HideLabel]
        [ValueDropdown("GetSkillCandidates")]
        public ESkill skill = ESkill.None;

        [HorizontalGroup(0.32f)]
        [LabelText("세기")]
        [LabelWidth(30)]
        [PropertyRange(0f, 1f)]
        public float trauma = 0.6f;

        /// <summary>
        /// ESkill 에는 몬스터 스킬까지 수천 개가 들어 있어 그대로 띄우면 고를 수가 없다.
        /// 플레이어가 쓰는 스펠·오라만 남긴다
        /// </summary>
        private static IEnumerable<ESkill> GetSkillCandidates()
        {
            foreach (ESkill skillType in Enum.GetValues(typeof(ESkill)))
            {
                string skillName = skillType.ToString();

                if (skillName.StartsWith("skillspell_") || skillName.StartsWith("skillaura_"))
                    yield return skillType;
            }
        }
    }

    /// <summary>
    /// 전투 규칙 설정 — 탐색 주기, 피해 배율, 상태이상, 쿨다운 같은 수치 규칙
    /// </summary>
    [Serializable]
    public class CombatSettings
    {
        [BoxGroup("적 탐색")]
        [LabelText("플레이어 탐색 주기")]
        [PropertyRange(0.01f, 1f)]
        [SuffixLabel("초", overlay: true)]
        [Tooltip("플레이어 캐릭터가 적을 탐색하는 주기 (초 단위)")]
        public float playerSearchInterval = 0.1f;

        [BoxGroup("적 탐색")]
        [LabelText("몬스터 탐색 주기")]
        [PropertyRange(0.1f, 5f)]
        [SuffixLabel("초", overlay: true)]
        [Tooltip("몬스터 캐릭터가 적을 탐색하는 주기 (초 단위)")]
        public float monsterSearchInterval = 1.0f;

        [BoxGroup("데미지 배율")]
        [LabelText("2배 피해 배수")]
        [PropertyRange(1.0f, 5.0f)]
        [Tooltip("멀티플 피해 - 2배 데미지 배수")]
        public float doubleDamageMultiplier = 2.0f;

        [BoxGroup("데미지 배율")]
        [LabelText("3배 피해 배수")]
        [PropertyRange(1.0f, 10.0f)]
        [Tooltip("멀티플 피해 - 3배 데미지 배수")]
        public float tripleDamageMultiplier = 3.0f;

        [BoxGroup("폭발")]
        [LabelText("폭발 피해 비율")]
        [PropertyRange(0.01f, 1.0f)]
        [SuffixLabel("%", overlay: true)]
        [Tooltip("적 처치 시 폭발 피해 = 최대 생명력 × 이 비율 (0.1 = 10%)")]
        public float explosionDamagePercent = 0.1f;

        [BoxGroup("폭발")]
        [LabelText("폭발 기본 반경")]
        [PropertyRange(0.5f, 5.0f)]
        [SuffixLabel("m", overlay: true)]
        [Tooltip("적 폭발 시 기본 반경")]
        public float explosionBaseRadius = 1.0f;

        [BoxGroup("상태이상")]
        [LabelText("마비 최소 지속시간")]
        [PropertyRange(0.1f, 1.0f)]
        [SuffixLabel("초", overlay: true)]
        [Tooltip("마비 효과가 적용되는 최소 지속시간 (이하는 무시)")]
        public float paralyzeMinDuration = 0.2f;

        [BoxGroup("상태이상")]
        [LabelText("기절 최소 지속시간")]
        [PropertyRange(0.1f, 1.0f)]
        [SuffixLabel("초", overlay: true)]
        [Tooltip("기절 효과가 적용되는 최소 지속시간 (이하는 무시)")]
        public float stunMinDuration = 0.2f;

        [BoxGroup("버프 · DOT")]
        [LabelText("DOT 틱 간격")]
        [PropertyRange(0.1f, 5.0f)]
        [SuffixLabel("초", overlay: true)]
        [Tooltip("DOT(Damage Over Time) 데미지 적용 주기")]
        public float dotTickInterval = 1.0f;

        [BoxGroup("버프 · DOT")]
        [LabelText("버프 최근 추가 임계값")]
        [PropertyRange(1.0f, 10.0f)]
        [SuffixLabel("초", overlay: true)]
        [Tooltip("버프가 최근에 추가되었다고 판단하는 시간 기준")]
        public float buffRecentThreshold = 4.0f;

        [BoxGroup("흡수")]
        [LabelText("최대 지속 시간")]
        [PropertyRange(1f, 10f)]
        [SuffixLabel("초", overlay: true)]
        [Tooltip("흡수 인스턴스의 최대 지속 시간")]
        public float maxDuration = 5.0f;

        [BoxGroup("스킬 쿨다운")]
        [LabelText("쿨다운 감소율")]
        [PropertyRange(0f, 1f)]
        [SuffixLabel("%", overlay: true)]
        [Tooltip("스킬 쿨다운 감소 비율 (0.2 = 20% 감소)")]
        public float cooldownReduction = 0.2f;

        [BoxGroup("스킬 쿨다운")]
        [LabelText("최소 쿨다운")]
        [PropertyRange(0.1f, 5f)]
        [SuffixLabel("초", overlay: true)]
        [Tooltip("스킬 쿨다운 최소값")]
        public float minCooldown = 0.5f;

        [BoxGroup("캐스팅")]
        [LabelText("캐스팅 속도 배수")]
        [PropertyRange(0.5f, 3f)]
        [Tooltip("스킬 캐스팅 속도 (1.0 = 기본, 2.0 = 2배)")]
        public float castSpeedMultiplier = 1.0f;

        // ===================================================================
        // 히트 플래시 — 피격당한 몬스터를 아주 짧게 단색으로 덮는다.
        //
        // 자동전투라 유저가 때린 손맛을 줄 방법이 없다. 대신 "맞았다"를 눈으로 보여줘야 하는데,
        // 카메라 흔들기나 시간 정지는 초당 수십 번 일어나는 일반 히트에 쓰면 곧바로 멀미가 된다.
        // 몸 색을 잠깐 바꾸는 건 화면 전체를 건드리지 않으면서 개체 단위로 반응을 주는 유일한 채널이다.
        // ===================================================================

        [BoxGroup("히트 플래시")]
        [LabelText("사용")]
        [Tooltip("피격 순간 몬스터 몸이 잠깐 단색으로 번쩍인다. 끄면 관련 연산이 통째로 생략된다")]
        public bool hitFlashEnabled = true;

        [BoxGroup("히트 플래시")]
        [LabelText("지속 시간")]
        [PropertyRange(0.02f, 0.3f)]
        [SuffixLabel("초", overlay: true)]
        [EnableIf("hitFlashEnabled")]
        [Tooltip("번쩍임이 완전히 사라지기까지 걸리는 시간.\n" +
                 "0.1초를 넘기면 여러 대 연속으로 맞는 몬스터가 계속 하얀 상태로 남아 실루엣이 사라진다.\n" +
                 "짧을수록 '때렸다'는 사건으로 읽히고, 길수록 '상태'로 읽힌다 — 여기서 원하는 건 사건 쪽이다")]
        public float hitFlashDuration = 0.06f;

        [BoxGroup("히트 플래시")]
        [LabelText("색")]
        [EnableIf("hitFlashEnabled")]
        [Tooltip("몸을 덮는 색. 일반 히트와 크리티컬이 같은 색을 쓰고 세기로만 갈린다 —\n" +
                 "색을 나눠 봐야 0.06초짜리 번쩍임에서 색 차이는 읽히지 않고 조절할 곳만 둘로 늘어난다.\n\n" +
                 "흰색이면 몬스터 고유색과 무관하게 같은 세기로 읽힌다.\n" +
                 "회색으로 낮추면 세기를 낮추는 것과 효과가 겹치는 데다\n" +
                 "밝은 몬스터는 오히려 어두워져 '맞았다' 가 아니라 '그늘졌다' 로 보인다.\n" +
                 "HDR 이라 1을 넘기면 블룸에 걸려 번짐이 생긴다")]
        public Color hitFlashColor = Color.white;

        [BoxGroup("히트 플래시")]
        [LabelText("일반 히트 세기")]
        [PropertyRange(0f, 1f)]
        [EnableIf("hitFlashEnabled")]
        [Tooltip("몸을 덮는 정도. 1이면 완전히 단색이 되어 실루엣만 남는다.\n" +
                 "0.7~0.8 이면 원래 색이 비쳐 '무엇이 맞았는지'를 유지한 채 번쩍인다")]
        public float hitFlashStrength = 0.75f;

        [BoxGroup("히트 플래시")]
        [LabelText("크리티컬 세기")]
        [PropertyRange(0f, 1f)]
        [EnableIf("hitFlashEnabled")]
        [Tooltip("크리티컬은 일반 히트보다 강하게 덮어야 난전에서 구분된다.\n" +
                 "일반 히트 세기와 차이가 0.15 미만이면 사실상 구분되지 않는다")]
        public float hitFlashCriticalStrength = 1.0f;

        [BoxGroup("히트 플래시")]
        [LabelText("프레임당 상한")]
        [PropertyRange(1, 64)]
        [SuffixLabel("마리", overlay: true)]
        [EnableIf("hitFlashEnabled")]
        [Tooltip("한 프레임에 새로 번쩍일 수 있는 최대 개체 수. 초과분은 그냥 번쩍이지 않는다.\n" +
                 "광역기 한 발에 스무 마리가 한꺼번에 하얘지면 개별 피격이 아니라 화면 백화로 보인다.\n" +
                 "상한에 걸린 개체는 어차피 다른 개체의 번쩍임에 묻히므로 잃는 정보가 없다.\n\n" +
                 "광역기는 한 프레임에 몰려 들어오므로 이 값이 사실상 동시 상한으로 작동한다.\n" +
                 "이미 번쩍이는 중인 개체가 또 맞는 건 이 수를 쓰지 않는다")]
        public int hitFlashMaxPerFrame = 12;

        [BoxGroup("히트 플래시")]
        [LabelText("군중 감쇠")]
        [PropertyRange(0.1f, 1f)]
        [EnableIf("hitFlashEnabled")]
        [Tooltip("같은 프레임에 맞은 수가 상한에 도달했을 때 각 번쩍임에 곱하는 세기 배율.\n" +
                 "1이면 감쇠 없음 — 여럿이 동시에 맞을수록 화면이 밝아진다.\n" +
                 "0.5 면 상한 근처에서 절반 세기로 줄어 총량이 대체로 일정하게 유지된다.\n" +
                 "한 마리만 맞을 때는 항상 100% 세기다")]
        public float hitFlashCrowdAttenuation = 0.5f;

        // ===================================================================
        // 히트 셰이크 — 피격당한 몬스터의 몸이 짧게 진동한다.
        //
        // 플래시가 "맞았다" 를 알린다면 이쪽은 "얼마나 세게" 를 알린다.
        // 색과 달리 셰이더를 타지 않아 어떤 셰이더를 쓰는 몬스터에도 똑같이 적용된다
        // (일부 몬스터는 Shader Graphs/URPmaskTint 를 써서 플래시가 걸리지 않는다).
        //
        // ⚠️ 애니메이션 설정의 "피격 효과" 항목들과는 별개다.
        // 그쪽은 훈련장 더미 전용 DOTween 흔들림이고, 여기는 실전 몬스터용 자체 감쇠 진동이다
        // ===================================================================

        [BoxGroup("히트 셰이크")]
        [LabelText("사용")]
        [Tooltip("피격 순간 몬스터 몸이 짧게 진동한다. 끄면 관련 연산이 통째로 생략된다")]
        public bool hitShakeEnabled = true;

        [BoxGroup("히트 셰이크")]
        [LabelText("지속 시간")]
        [PropertyRange(0.03f, 0.4f)]
        [SuffixLabel("초", overlay: true)]
        [EnableIf("hitShakeEnabled")]
        [Tooltip("진동이 잦아들기까지 걸리는 시간.\n" +
                 "0.2초를 넘기면 연타를 맞는 몬스터가 계속 떨고 있어 '맞았다' 가 아니라 '떨고 있다' 로 보인다")]
        public float hitShakeDuration = 0.12f;

        [BoxGroup("히트 셰이크")]
        [LabelText("진폭")]
        [PropertyRange(0.01f, 0.6f)]
        [EnableIf("hitShakeEnabled")]
        [Tooltip("몸이 밀려나는 최대 거리 (월드 유닛). 시작 순간이 가장 크고 곧게 줄어든다.\n" +
                 "직교 카메라 세로가 16 유닛이므로 0.12 는 화면 높이의 약 0.75% 다.\n" +
                 "0.3 을 넘기면 몸이 발밑에서 떨어져 나온 것처럼 보인다")]
        public float hitShakeDistance = 0.12f;

        [BoxGroup("히트 셰이크")]
        [LabelText("진동수")]
        [PropertyRange(5f, 60f)]
        [SuffixLabel("Hz", overlay: true)]
        [EnableIf("hitShakeEnabled")]
        [Tooltip("초당 진동 횟수. 높을수록 '떨림', 낮을수록 '휘청임' 으로 읽힌다.\n" +
                 "지속 시간 × 진동수가 1 미만이면 왕복을 한 번도 못 끝내고 끝나 한쪽으로 밀리기만 한다")]
        public float hitShakeFrequency = 30f;

        [BoxGroup("히트 셰이크")]
        [LabelText("크리티컬 배수")]
        [PropertyRange(1f, 3f)]
        [EnableIf("hitShakeEnabled")]
        [Tooltip("크리티컬 피격 시 진폭에 곱하는 배수. 플래시의 세기 차이와 같은 역할을 몸짓으로 한다")]
        public float hitShakeCriticalScale = 1.6f;

        [BoxGroup("히트 셰이크")]
        [LabelText("프레임당 상한")]
        [PropertyRange(1, 64)]
        [SuffixLabel("마리", overlay: true)]
        [EnableIf("hitShakeEnabled")]
        [Tooltip("한 프레임에 새로 떨기 시작할 수 있는 최대 개체 수.\n" +
                 "플래시와 달리 화면이 밝아지는 문제는 없지만, 스무 마리가 동시에 떨면\n" +
                 "개별 피격이 아니라 화면 전체가 지글거리는 것으로 보인다")]
        public int hitShakeMaxPerFrame = 12;

        // ===================================================================
        // 카메라 셰이크 — 화면 전체가 흔들린다.
        //
        // 앞의 두 연출과 성격이 완전히 다르다. 플래시·몸 진동은 맞은 개체 하나에만 영향을 주지만
        // 이건 화면 전체를 움직이므로 유저가 보고 있던 다른 모든 것에도 영향을 준다.
        // 그래서 절대 일반 히트에 걸지 않는다 — 초당 수십 번 화면이 흔들리면 그냥 멀미다.
        // 오라 발동처럼 몇 초에 한 번 있는 사건에만 쓴다.
        //
        // 누적(trauma) 모델이라 여러 사건이 겹치면 합쳐진다.
        // 유저 설정(옵션 > 카메라 흔들림)이 꺼져 있으면 어떤 값을 넣어도 흔들리지 않는다
        // ===================================================================

        [BoxGroup("카메라 셰이크")]
        [LabelText("사용")]
        [Tooltip("개발용 마스터 토글. 유저 설정(옵션 > 카메라 흔들림)과는 별개이며, 둘 다 켜져야 흔들린다")]
        public bool cameraShakeEnabled = true;

        [BoxGroup("카메라 셰이크")]
        [LabelText("최대 진폭")]
        [PropertyRange(0.02f, 0.6f)]
        [EnableIf("cameraShakeEnabled")]
        [Tooltip("흔들림이 최대일 때 카메라가 움직이는 거리 (월드 유닛).\n\n" +
                 "직교 카메라 크기가 8 이라 화면 세로가 16 유닛이다. 0.25 는 화면 높이의 약 1.5% 지만,\n" +
                 "실제 진폭은 누적값의 제곱에 비례하므로 기본 설정에서는 그 3분의 1 정도만 쓴다.\n" +
                 "0.4 를 넘기면 흔들리는 게 아니라 화면이 튀는 것으로 보인다")]
        public float cameraShakeDistance = 0.25f;

        [BoxGroup("카메라 셰이크")]
        [LabelText("감쇠 속도")]
        [PropertyRange(0.5f, 8f)]
        [SuffixLabel("/초", overlay: true)]
        [EnableIf("cameraShakeEnabled")]
        [Tooltip("초당 줄어드는 누적값. 2.5 면 0.6 만큼 쌓인 흔들림이 약 0.24초 만에 사라진다.\n" +
                 "낮추면 여운이 길어지지만 사건이 연달아 터질 때 흔들림이 계속 겹쳐 쌓인다.\n\n" +
                 "배속과 무관한 실시간 기준으로 줄어든다 — 화면이 실제로 얼마나 자주 흔들리는지가\n" +
                 "멀미를 좌우하므로, 2배속이라고 흔들림이 두 배로 잦아지면 안 된다")]
        public float cameraShakeDecay = 2.5f;

        [BoxGroup("카메라 셰이크")]
        [LabelText("진동수")]
        [PropertyRange(5f, 50f)]
        [SuffixLabel("Hz", overlay: true)]
        [EnableIf("cameraShakeEnabled")]
        [Tooltip("흔들림의 빠르기. 낮으면 '휘청', 높으면 '떨림' 으로 읽힌다.\n" +
                 "40 을 넘기면 프레임 사이 간격보다 빨라져 흔들림이 아니라 지글거림이 된다")]
        public float cameraShakeFrequency = 22f;

        [BoxGroup("카메라 셰이크")]
        [LabelText("최소 간격")]
        [PropertyRange(0f, 2f)]
        [SuffixLabel("초", overlay: true)]
        [EnableIf("cameraShakeEnabled")]
        [Tooltip("흔들림을 새로 쌓을 수 있는 최소 간격. 이 안에 들어온 요청은 버린다.\n\n" +
                 "⚠️ 이 값이 0 이면 화면이 상시 흔들릴 수 있다.\n" +
                 "오라 중에는 '적을 처치할 때마다' 발동하는 것들이 있어서(출혈 폭발·냉기 폭발)\n" +
                 "방치형처럼 처치가 끊이지 않는 전투에서는 요청이 초당 여러 번 들어온다.\n" +
                 "그대로 두면 누적값이 최대치에 붙어 화면이 계속 떨게 된다.\n\n" +
                 "같은 프레임에 들어온 요청은 이 간격과 무관하게 통과하되, 더해지지 않고 가장 센 것만 남는다 —\n" +
                 "멀티샷처럼 한 번의 시전이 발사 지점을 여러 번 지나는 경우가 있어서다.\n" +
                 "배속과 무관한 실시간 기준이다")]
        public float cameraShakeMinInterval = 0.4f;

        [BoxGroup("카메라 셰이크")]
        [LabelText("흔들 스킬")]
        [EnableIf("cameraShakeEnabled")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [Tooltip("여기 등록된 스킬만 카메라를 흔든다. 목록에 없는 스킬은 아무리 세도 화면이 반응하지 않는다.\n\n" +
                 "세기는 0~1 의 누적값이며, 실제 진폭은 그 값의 제곱에 최대 진폭을 곱한 것이다 —\n" +
                 "0.6 이면 최대의 36%, 0.3 이면 9% 다. 제곱을 쓰는 이유는 약한 사건이 상시 깔려\n" +
                 "화면이 늘 미세하게 떠는 걸 막기 위해서다. 그래서 세기를 절반으로 줄이면 흔들림은 4분의 1이 된다.\n\n" +
                 "스펠(fireball 등)은 쿨타임마다 시전되므로 등록하면 상당히 잦다.\n" +
                 "오라 중에도 bloodrupture · frostbreak 는 적을 처치할 때마다, thunderfall 은 충전이 있으면\n" +
                 "스킬 명중마다 터진다. 잦은 쪽을 넣을 거라면 최소 간격을 함께 올리는 편이 좋다")]
        public List<FCameraShakeSkill> cameraShakeSkills = new List<FCameraShakeSkill>
        {
            new FCameraShakeSkill { skill = ESkill.skillaura_meterodescent, trauma = 0.8f },
            new FCameraShakeSkill { skill = ESkill.skillaura_thunderfall,   trauma = 0.6f },
            new FCameraShakeSkill { skill = ESkill.skillaura_bloodrupture,  trauma = 0.5f },
            new FCameraShakeSkill { skill = ESkill.skillaura_frostbreak,    trauma = 0.5f },
        };

        /// <summary>
        /// 이 스킬에 지정된 흔들림 세기. 목록에 없으면 0 — 즉 흔들지 않는다.
        /// </summary>
        public float GetCameraShakeTrauma(ESkill InSkill)
        {
            if (cameraShakeEnabled == false || cameraShakeSkills == null)
                return 0f;

            //등록 가능한 스킬이 10종뿐이라 선형 탐색으로 충분하다.
            //사전을 만들면 인스펙터에서 값을 바꿀 때마다 다시 지어야 해서 오히려 손이 많이 간다
            for (int i = 0; i < cameraShakeSkills.Count; i++)
            {
                FCameraShakeSkill entry = cameraShakeSkills[i];

                if (entry != null && entry.skill == InSkill)
                    return entry.trauma;
            }

            return 0f;
        }
    }
}
