using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    /// <summary>
    /// 몬스터 사망 파쇄 연출의 실행부.
    ///
    /// 사망 순간 메시를 파트 분해 메시로 갈아 끼우고, 경과 시간을 셰이더에 밀어 준다.
    /// 눈에 보이는 계산은 전부 <see cref="MonsterShatterState"/> 에 있고 (데모 씬과 공유),
    /// 여기서는 전투 쪽 사정 — 진영 판정, 넉백, 애니메이터 정지, 풀 반환 — 만 다룬다.
    ///
    /// EffectData 와 마찬가지로 개별 MonoBehaviour Update() 를 두지 않고
    /// GameBattleControlManager.Update() 에서 일괄 tick 된다. 사망 개체가 60마리까지 겹쳐도
    /// 업데이트 호출은 1회다.
    ///
    /// 되돌리기가 핵심이다. 캐릭터는 풀로 재사용되므로 파쇄 상태로 반환되면
    /// 다음 소환 때 조각난 몬스터가 튀어나온다. 정상 종료(Tick 완료)든 강제 종료(SetPoolData)든
    /// 반드시 Restore 를 거치게 되어 있다.
    /// </summary>
    public static class MonsterShatterRunner
    {
        private class Entry
        {
            public UCharacterActor Actor;
            public MonsterShatterState State = new MonsterShatterState();
            public float Elapsed;
            public float Duration = 1f;
            public float OriginalAnimatorSpeed = 1f;
            public bool KinematicOverridden;
            public bool OriginalKinematic;
            public MonsterShatterTuning Tuning;
        }

        private static readonly List<Entry> _active = new List<Entry>();
        private static readonly Stack<Entry> _pool = new Stack<Entry>();

        public static int ActiveCount => _active.Count;

        /// <summary>
        /// 파쇄를 시작한다. 시작했으면 true — 호출자는 기존 사망 연출(사망 애니메이션 대기)을 건너뛰어야 한다.
        /// 설정이 꺼져 있거나 메시가 읽기 불가면 false 를 돌려주고 아무것도 건드리지 않는다.
        ///
        /// damageData 는 치명타 한 방으로 죽은 경우에만 들어온다. 여기서는 공격 지점만 꺼내
        /// 조각이 날아갈 방향을 정하는 데 쓰고, 없으면(웨이브 정리 등으로 일괄 사망) 제자리에서 터진다.
        /// </summary>
        public static bool TryStart(UCharacterActor actor, FCalcResultModDamage damageData = null)
        {
            if (actor == null || actor.MeshArea == null)
                return false;

            DeathSettings death = GameClientPlayConfig.Instance != null ? GameClientPlayConfig.Instance.death : null;
            if (death == null || death.shatterEnabled == false)
                return false;

            //아군까지 터뜨리면 곤란하다. 적 진영만 대상으로 한다
            if (actor.IsAlianceTeam)
                return false;

            MonsterShatterTuning tuning = MonsterShatterTuning.FromConfig(death);

            //캐릭터는 발밑이 원점이므로 루트 y 가 곧 지면 높이다
            float groundY = actor.GetTransform.position.y + tuning.GroundOffset;

            Vector3 knockbackDir = tuning.HasKnockback ? GetKnockbackDirection(actor, damageData) : Vector3.zero;

            Entry entry = _pool.Count > 0 ? _pool.Pop() : new Entry();

            if (entry.State.Setup(actor.MeshArea, groundY, tuning, knockbackDir) == false)
            {
                Release(entry);
                return false;
            }

            entry.Actor = actor;
            entry.Elapsed = 0f;
            entry.Duration = Mathf.Max(0.1f, tuning.Duration);
            entry.Tuning = tuning;

            //넉백은 이 시점보다 한 프레임 먼저(SetDamaged) 이미 걸려 있다.
            //Config 로 넉백 호출 자체를 막지 않고 여기서 되돌리는 이유는,
            //읽기 불가 메시 등으로 파쇄가 실패했을 때 넉백이 멀쩡히 남아야 하기 때문이다.
            //파쇄 궤적은 시작 시점의 월드 좌표를 기준으로 잡히므로, 도중에 시체가 밀려나면
            //조각이 본체를 따라가지 않고 늘어져 보인다 — 넉백을 죽이는 건 그래서도 필요하다.
            //
            //속도 대입만으로는 부족하다. 넉백은 AddForce(ForceMode.Impulse) 이고 PhysX 는
            //임펄스를 별도 누산기에 쌓아 두었다가 다음 FixedUpdate 에 적용한다.
            //linearVelocity 를 0 으로 써도 누산기는 그대로 남아 결국 날아간다.
            //kinematic 전환은 속도와 누산기를 함께 비우므로 이쪽을 쓴다.
            //(kinematic 상태에서는 속도 대입이 무시되니 순서도 속도 → kinematic 이어야 한다)
            if (death.shatterDisablesKnockback && actor.CharacterRigidbody != null)
            {
                entry.OriginalKinematic = actor.CharacterRigidbody.isKinematic;
                entry.KinematicOverridden = true;

                actor.CharacterRigidbody.linearVelocity = Vector3.zero;
                actor.CharacterRigidbody.angularVelocity = Vector3.zero;
                actor.CharacterRigidbody.isKinematic = true;
            }
            else
            {
                entry.KinematicOverridden = false;
            }

            //현재 포즈에서 터뜨린다. 애니메이터를 그대로 두면 쓰러지는 동작과 폭발이 겹쳐 산만하고,
            //본이 계속 움직이면 시작 시점에 잡아 둔 파트 중심과 실제 메시가 어긋난다.
            //사망 애니메이션 끝의 AnimDeadFinish 가 파쇄 도중 풀 반환을 일으키는 것도 막는다.
            //
            //speed 를 0 으로 쓰는 것만으로는 부족하다. UCharacterActor.UpdateAnimator 가
            //매 프레임 speed 를 다시 써 넣기 때문에(사망 여부를 보지 않는다) 플래그로 그쪽을 재워야 한다
            actor.IsShattering = true;

            Animator animator = actor.GetAnimator;
            if (animator != null)
            {
                entry.OriginalAnimatorSpeed = animator.speed;
                animator.speed = 0f;
            }
            else
            {
                entry.OriginalAnimatorSpeed = 1f;
            }

            entry.State.Apply(0f, 0f, tuning);
            _active.Add(entry);

            return true;
        }

        /// <summary>
        /// 조각 뭉치가 밀려날 수평 방향. Zero 면 날리지 않고 제자리에서 터진다.
        ///
        /// 기본은 "맞은 쪽 반대" — 물리 넉백(GameBattleUtilityManager.ApplyDeadKnockback)과 같은 기준으로
        /// 공격 지점에서 몬스터로 향하는 방향을 쓴다. 두 연출의 방향이 어긋나면
        /// 파쇄에 실패해 물리 넉백으로 폴백한 몬스터만 반대로 날아가 보인다.
        ///
        /// 공격 지점이 없거나(장판·도트처럼 공격자 위치가 실리지 않는 피해) 몬스터와 겹칠 만큼 가까우면
        /// 몬스터가 바라보는 반대쪽으로 보낸다. 몬스터는 대개 플레이어를 향하고 있으므로
        /// 이쪽이 '뒤로 날아간다'에 가장 가깝다.
        /// </summary>
        private static Vector3 GetKnockbackDirection(UCharacterActor actor, FCalcResultModDamage damageData)
        {
            //피해 정보 자체가 없는 사망(웨이브 정리 등)은 맞아서 죽은 게 아니라 밀어낼 근거가 없다
            if (damageData == null)
                return Vector3.zero;

            //SetAttackPos 가 불리지 않으면 0 이 그대로 남는다. 스테이지 원점을 공격 지점으로 삼으면
            //엉뚱한 쪽으로 날아가므로 미설정으로 보고 아래 폴백에 맡긴다
            if (damageData.attackPos != Vector3.zero)
            {
                Vector3 direction = actor.GetTransform.position - damageData.attackPos;
                direction.y = 0f;

                if (direction.sqrMagnitude > 0.0001f)
                    return direction.normalized;
            }

            Vector3 backward = -actor.GetTransform.forward;
            backward.y = 0f;

            return backward.sqrMagnitude > 0.0001f ? backward.normalized : Vector3.zero;
        }

        /// <summary>진행 중인 파쇄를 갱신한다. GameBattleControlManager.Update 에서 매 프레임 호출된다.</summary>
        public static void Tick(float deltaTime)
        {
            if (_active.Count == 0)
                return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Entry entry = _active[i];

                //파쇄 도중 다른 경로로 캐릭터가 정리됐으면 조용히 접는다
                if (entry.Actor == null || entry.Actor.CharacterStatus == null)
                {
                    Finish(entry, i);
                    continue;
                }

                entry.Elapsed += deltaTime;
                float progress = Mathf.Clamp01(entry.Elapsed / entry.Duration);

                entry.State.Apply(entry.Elapsed, progress, entry.Tuning);

                if (progress < 1f)
                    continue;

                UCharacterActor actor = entry.Actor;
                Finish(entry, i);

                //사망 애니메이션을 멈춰 뒀으므로 AnimDeadFinish 가 오지 않는다. 여기서 직접 반환한다
                GameBattleControlManager.Instance?.SetCharacterDeadFinish(actor);
            }
        }

        /// <summary>
        /// 해당 캐릭터의 파쇄를 즉시 취소하고 원상 복구한다.
        /// 풀 반환(SetPoolData) 경로에서 불려 파쇄 상태가 풀에 섞여 들어가는 것을 막는다.
        /// </summary>
        public static void Cancel(UCharacterActor actor)
        {
            if (actor == null || _active.Count == 0)
                return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Actor != actor)
                    continue;

                Finish(_active[i], i);
                return;
            }
        }

        /// <summary>전투 종료 등으로 전부 정리. 캐시된 분해 메시와 머티리얼도 반납한다.</summary>
        public static void ClearAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                Finish(_active[i], i);

            MonsterShatterState.ClearMaterialCache();
            MonsterShatterMeshCache.ClearAll();
        }

        /// <summary>원복 → 목록에서 제거 → 풀 반환. 종료 경로가 셋이라 한 곳으로 모았다.</summary>
        private static void Finish(Entry entry, int index)
        {
            entry.State.Restore();

            if (entry.Actor != null)
            {
                Animator animator = entry.Actor.GetAnimator;
                if (animator != null)
                    animator.speed = entry.OriginalAnimatorSpeed <= 0f ? 1f : entry.OriginalAnimatorSpeed;

                //UpdateAnimator 에 다시 애니메이터를 넘긴다. speed 를 되돌린 뒤에 풀어야
                //한 프레임이라도 0 인 채로 갱신되는 일이 없다
                entry.Actor.IsShattering = false;

                //넉백을 막으려고 kinematic 으로 돌려놨던 것을 되돌린다.
                //풀 반환 경로(SetPoolData)가 어차피 false 로 되돌리지만,
                //파쇄만 취소되고 캐릭터는 살아 있는 경로도 있어 여기서 짝을 맞춘다
                if (entry.KinematicOverridden && entry.Actor.CharacterRigidbody != null)
                    entry.Actor.CharacterRigidbody.isKinematic = entry.OriginalKinematic;
            }

            _active.RemoveAt(index);
            Release(entry);
        }

        private static void Release(Entry entry)
        {
            entry.Actor = null;
            entry.Elapsed = 0f;
            entry.KinematicOverridden = false;
            _pool.Push(entry);
        }
    }
}
