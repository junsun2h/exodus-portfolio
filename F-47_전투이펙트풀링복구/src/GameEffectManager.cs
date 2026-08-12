using System.Collections.Generic;
using UnityEngine;

namespace PX
{
    public class GameEffectManager : SingletonDependency<GameEffectManager>
    {
        /// <summary>
        /// 재생 중인 이펙트 목록. EffectData 는 개별 MonoBehaviour Update() 를 갖지 않고
        /// 여기서 일괄 tick 된다.
        ///
        /// 등록/해제는 EffectData.PlayEffect / StopEffect 가 직접 호출한다.
        /// stopEffectEvent 콜백에 기대면 안 된다 — 단일 Action 필드라
        /// 호출자가 덮어쓰는 순간(UCharacterActor.AddDependencyParticle 등) 해제가 유실된다
        /// </summary>
        private readonly List<EffectData> _activeEffects = new List<EffectData>();

        /// <summary>
        /// 장착 스킬과 무관하게 거의 모든 판에서 쓰이는 이펙트.
        /// 몬스터 원거리·마법 공격 투사체(BaseActionController.FireProjectileEvent)와
        /// 공용 히트 이펙트(BaseSpellController)가 여기 해당한다.
        /// Projectile_default 계열은 스킬 이펙트 위에 겹쳐 붙는 문제로 사용을 중단해 제외했다
        /// </summary>
        private static readonly (string bundle, string name)[] CommonPrewarmEffects =
        {
            (FCommonDefine.ConstBundleEffectProjectile, "Projectile 7"),
            (FCommonDefine.ConstBundleEffectProjectile, "Hit 7"),
            (FCommonDefine.ConstBundleEffectProjectile, "Projectile 8"),
            (FCommonDefine.ConstBundleEffectProjectile, "Hit 8"),
            (FCommonDefine.ConstBundleEffectSkill, "Skill_Physic_FanOfKnives_Hit"),
        };

        private GameEffectManager()
        {
        }

        public override void InitData()
        {
            base.InitData();
        }

        public override void ClearAllData()
        {
            base.ClearAllData();

            ClearAllEffect();
        }

        public EffectData GetEffectSpriteAni(string InBundle, string InName, Transform InAttachTransform = null)
        {
            if (InAttachTransform != null)
            {
                if (EffectData.AbleAttachEffect(InAttachTransform, InBundle, InName))
                {
                    return GetEffectData<EffectSpriteAni>(InBundle, InName);
                }
            }
            else
            {
                return GetEffectData<EffectSpriteAni>(InBundle, InName);
            }

            return null;
        }

        public EffectData GetEffectParticle(string InBundle, string InName)
        {
            return GetEffectData<EffectParticle>(InBundle, InName);
        }

        private EffectData GetEffectData<T>(string InBundle, string InName) where T : EffectData
        {
            //활성 목록 등록은 PlayEffect 시점에 일어난다. 여기서 미리 넣으면
            //꺼내 놓고 재생하지 않은 개체까지 tick 대상이 된다
            return GameObjectPoolManager.Instance.GetEffectData<T>(InBundle, InName);
        }

        #region 프리워밍 키 수집

        /// <summary>
        /// 이번 판에 미리 만들어 둘 이펙트 키를 모읍니다.
        /// 공용 이펙트 + 메인 캐릭터가 장착한 스킬·오라가 선언한 이펙트를 합칩니다.
        ///
        /// 키가 데이터 테이블이 아니라 코드 하드코딩이라, 선언 지점(각 스킬 컨트롤러의
        /// CollectPrewarmEffects)에서 직접 받아옵니다
        /// </summary>
        public void CollectStagePrewarmEffects(List<(string bundle, string name)> OutList)
        {
            if (OutList == null)
                return;

            OutList.Clear();

            for (int i = 0; i < CommonPrewarmEffects.Length; i++)
            {
                OutList.Add(CommonPrewarmEffects[i]);
            }

            //사망 이펙트는 Config 로 이름이 바뀌므로 배열에 넣지 못한다.
            //판마다 거의 확실히 쓰이는 데다 첫 사망은 몰려서 오기 때문에 프리워밍 대상에 넣는다
            DeathSettings death = GameClientPlayConfig.Instance != null ? GameClientPlayConfig.Instance.death : null;

            if (death != null && death.deadEffectEnabled && string.IsNullOrEmpty(death.deadEffectName) == false)
            {
                OutList.Add((FCommonDefine.ConstBundleEffectSkill, death.deadEffectName));
            }

            UCharacterActor mainCharacter = GameBattleControlManager.Instance.CurrentMainCharacter;

            if (mainCharacter != null && mainCharacter.ActionControl != null)
            {
                mainCharacter.ActionControl.CollectPrewarmEffects(OutList);
            }

            //같은 키가 여러 컨트롤러에서 겹칠 수 있다 (ChainLightHitParticle, Skill_Physic_FanOfKnives_Hit 등)
            RemoveDuplicatedKey(OutList);
        }

        private static void RemoveDuplicatedKey(List<(string bundle, string name)> InOutList)
        {
            for (int i = InOutList.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    if (InOutList[i] == InOutList[j])
                    {
                        InOutList.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        #endregion

        #region 활성 이펙트 tick

        /// <summary>
        /// 매 프레임 GameBattleControlManager.Update 가 BattleDeltaTime 계산 직후 호출한다
        /// </summary>
        public void UpdateEffects(float InDeltaTime)
        {
            //Tick 안에서 StopEffect 가 불려 목록이 줄어들 수 있어 역순으로 순회한다.
            //swap-remove 는 마지막 원소를 빈 자리로 옮기는데, 역순이면 그 원소는 이미 처리된 뒤다
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                if (i >= _activeEffects.Count)
                    continue;

                EffectData targetEffect = _activeEffects[i];

                if (targetEffect == null)
                {
                    //외부에서 파괴된 개체(Unity fake-null)는 여기서 걷어낸다
                    RemoveAtSwap(i);
                    continue;
                }

                targetEffect.Tick(InDeltaTime);
            }
        }

        /// <summary>
        /// 전투 배속이 바뀌면 재생 중인 파티클의 simulationSpeed 도 같이 따라가야 한다
        /// </summary>
        public void ApplyBattleSpeed(float InBattleSpeed)
        {
            for (int i = 0; i < _activeEffects.Count; i++)
            {
                if (_activeEffects[i] is EffectParticle particleEffect)
                {
                    particleEffect.ApplyBattleSpeed(InBattleSpeed);
                }
            }
        }

        public void AddActiveEffect(EffectData InEffect)
        {
            if (InEffect == null)
                return;

            //이미 목록에 있으면 무시한다. 두 번 들어가면 tick 이 두 번 돌아 재생이 2배 빨라진다
            if (InEffect.ActiveEffectIndex >= 0)
                return;

            InEffect.SetActiveEffectIndex(_activeEffects.Count);
            _activeEffects.Add(InEffect);
        }

        public void RemoveActiveEffect(EffectData InEffect)
        {
            if (InEffect == null)
                return;

            int targetIndex = InEffect.ActiveEffectIndex;

            if (targetIndex < 0 || targetIndex >= _activeEffects.Count)
                return;

            //Unity 의 == 오버로드를 피하려고 참조 동일성으로 확인한다
            if (ReferenceEquals(_activeEffects[targetIndex], InEffect) == false)
                return;

            RemoveAtSwap(targetIndex);
        }

        /// <summary>
        /// 재생 중인 이펙트를 전부 정지시킨다. 풀 대상은 이 과정에서 풀로 돌아간다
        /// </summary>
        public void ClearAllEffect()
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                if (i >= _activeEffects.Count)
                    continue;

                EffectData targetEffect = _activeEffects[i];

                if (targetEffect != null)
                    targetEffect.StopEffect(false);
            }

            //StopEffect 가 걷어내지 못한 잔여물(파괴된 개체 등) 정리.
            //인덱스를 되돌려 두지 않으면 다음 재생 때 AddActiveEffect 가 거부한다
            for (int i = 0; i < _activeEffects.Count; i++)
            {
                if (_activeEffects[i] != null)
                    _activeEffects[i].SetActiveEffectIndex(-1);
            }

            _activeEffects.Clear();
        }

        public int GetActiveEffectCount()
        {
            return _activeEffects.Count;
        }

        private void RemoveAtSwap(int InIndex)
        {
            int lastIndex = _activeEffects.Count - 1;

            EffectData removeEffect = _activeEffects[InIndex];

            if (InIndex != lastIndex)
            {
                EffectData lastEffect = _activeEffects[lastIndex];
                _activeEffects[InIndex] = lastEffect;

                if (lastEffect != null)
                    lastEffect.SetActiveEffectIndex(InIndex);
            }

            _activeEffects.RemoveAt(lastIndex);

            if (removeEffect != null)
                removeEffect.SetActiveEffectIndex(-1);
        }

        #endregion
    }
}
