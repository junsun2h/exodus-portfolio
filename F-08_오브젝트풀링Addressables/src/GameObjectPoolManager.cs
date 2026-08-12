using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PX
{
    public class GameObjectPoolManager : SingletonDependency<GameObjectPoolManager>
    {
        //ModValue 오브젝트풀 사용 유무
        public static bool UseModPool = false;

        //GC강제 호출 최대 캐싱 메모리MB
        private const float _GCMBSize = 20.0f;
        //GC강제 호출 최소 주기
        private const float _GCTermSec = 60.0f;
        //GC강제 호출로 인해 해제할 최대 메모리 비율
        private const float _GCClearCacheRate = 0.2f;
        //기본 캐싱오브젝트 갯수
        private const int _InitModCount = 100000;
        //추가 캐싱 증가 갯수
        private const int _CreatePerCount = 100;

        //ModValue ByteSize
        private int _sizeModValue = 16;

        private const float _MegaByte = 1000000.0f;
        private object _lockObject = new object();
        private int _modValueUsingCount = 0;
        private LinkedList<PX.ModValue> _managedModValueCache;
        private int _cacheCeche = 0;
        private int _cacheUse = 0;
        private float _gcCollectTime = 0;

        private Dictionary<string, List<RuntimeAnimatorController>> _animatorControllerPoolDic;
        private Dictionary<string, UAttachmentBase> _attachObjectPoolDic;
        private Dictionary<string, List<UCharacterActor>> _characterPoolDic;
        //키를 문자열로 합치면 조회마다 string.Format 이 할당을 만든다. 튜플 키는 할당이 없다
        private Dictionary<(string bundle, string name), List<EffectData>> _effectDataDic;
        private Dictionary<string, List<UPropBase>> _propPoolDic;

        private List<ProjectileActor> _projectileActorPoolList;
        private Dictionary<ESkill, List<ProjectileSkill>> _projectileSkillPoolList;

        // DynamicSpell 풀링 (PoisonArrow_Dot 등)
        private Dictionary<string, List<DynamicSpell>> _dynamicSpellPoolDic;

        private GameObjectPoolManager()
        {
            _managedModValueCache = new LinkedList<ModValue>();
            _animatorControllerPoolDic = new Dictionary<string, List<RuntimeAnimatorController>>();
            _attachObjectPoolDic = new Dictionary<string, UAttachmentBase>();
            _characterPoolDic = new Dictionary<string, List<UCharacterActor>>();
            _effectDataDic = new Dictionary<(string bundle, string name), List<EffectData>>();
            _propPoolDic = new Dictionary<string, List<UPropBase>>();
            _projectileActorPoolList = new List<ProjectileActor>();
            _projectileSkillPoolList = new Dictionary<ESkill, List<ProjectileSkill>>();
            _dynamicSpellPoolDic = new Dictionary<string, List<DynamicSpell>>();
        }

        ~GameObjectPoolManager()
        {
        }

        public override void InitData()
        {
            //이펙트 프리워밍은 스테이지 진입 시 장착 스킬 기준으로 수행한다 (BattleModeBase 의 콜드 스폰 최적화 리전).
            //여기 있던 주석 처리 코드는 살려도 동작하지 않았다 —
            //갓 만든 개체는 IsPlaying == false 라 StopEffect 가 맨 앞에서 그대로 반환해 풀에 도달하지 못한다

            /*
            List<BattleStatusSO> battleStatusListSO = PrefebManager.Instance.TemplateBattleStatusComp.GetBattleStatusListSO();
            for (int i = 0; i < battleStatusListSO.Count; i++)
            {
                AddAnimatorController(CreateAnimController(battleStatusListSO[i].battleStatus.CTID));
            }
            */


#if UNITY_EDITOR
            _sizeModValue = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ModValue));
#endif

            CreateModValue(_InitModCount);

            //첫번째 애니 컨트롤러가 항상 날아가서 더미로 하나 호출함. 원인은 알수없음
            //CreateAnimController("animator#player#magicwand");
        }

        public override void Awake()
        {
        }

        public override void Start()
        {
        }

        private string CheckMemory(bool inCollectGC = false)
        {
            int tmpTotal = _managedModValueCache.Count + _modValueUsingCount;
            int byteSize = _sizeModValue * tmpTotal;
            float mbSize = byteSize / _MegaByte;

            if (inCollectGC)
            {
                if (mbSize >= _GCMBSize && Time.realtimeSinceStartup > _gcCollectTime + _GCTermSec)
                {
                    _gcCollectTime = Time.realtimeSinceStartup;

                    int removeCacheCount = (int)(_managedModValueCache.Count * _GCClearCacheRate);
                    for (int i = 0; i < removeCacheCount; i++)
                    {
                        _managedModValueCache.RemoveFirst();
                    }

                    System.GC.Collect();

                    Debug.Log($"!!!!!!!! Collect GC");
                }
            }

            return ($"RemainCount = {_managedModValueCache.Count}, UsingCount = {_modValueUsingCount}, Total = {tmpTotal}, [ModSize = {_sizeModValue}Byte, {byteSize}Byte, {mbSize}Mb]");
        }

        public override void LateUpdate()
        {
            if (UseModPool)
            {
                if (_cacheCeche != _managedModValueCache.Count || _cacheUse != _modValueUsingCount)
                {
                    _cacheCeche = _managedModValueCache.Count;
                    _cacheUse = _modValueUsingCount;

                    string memoryInfo = CheckMemory(true);
                    Debug.Log($"!!!!!!!! LateUpdate, {memoryInfo}");
                }
            }
        }

        private void CreateModValue(int InCount)
        {
            if (UseModPool == false)
                return;

            lock (_lockObject)
            {
                for (int i = 0; i < InCount; i++)
                {
                    try
                    {
                        _managedModValueCache.AddLast(new ModValue());
                        //_managedModValueCache.Push(new ModValue());
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"CreateModValue Error 1, _managedModValueCache.Count = {_managedModValueCache.Count}, _modValueUsingCount = {_modValueUsingCount}");
                        Debug.LogError($"CreateModValue Error 2, {e.ToString()}");
                    }
                }

                /*  Debug.Log($"!!!!!!!! CreateModValue, {CheckMemory()}");*/
            }

        }

        public ModValue GetPoolModValue()
        {
            if (UseModPool == false)
            {
                return new ModValue();
            }

            lock (_lockObject)
            {
                if (_managedModValueCache.Count <= 0)
                {
                    CreateModValue(_CreatePerCount);
                }

                _modValueUsingCount++;

                try
                {
                    ModValue tempNode = _managedModValueCache.First.Value;
                    tempNode.Update_None();
                    _managedModValueCache.RemoveFirst();
                    return tempNode;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error, GetPoolModValue, e = {e.ToString()}");
                }

                return null;
            }
        }

        public void AddPoolModValue(ModValue InValue)
        {
            if (UseModPool == false)
                return;

            lock (_lockObject)
            {
                _modValueUsingCount--;
                InValue.Update_None();
                _managedModValueCache.AddLast(InValue);

            }
        }

        public void AddPoolProp(UPropBase InPoolProp)
        {
            if (InPoolProp == null)
            {
                Debug.LogError("AddPoolProp, UPropBase Is Null");
                return;
            }

            InPoolProp.SetPoolData();
            InPoolProp.transform.SetParent(GamePrefabManager.Instance.TemplatePrefebComp.PropsArea.transform);

            if (_propPoolDic.ContainsKey(InPoolProp.PrefabName) == false)
                _propPoolDic.Add(InPoolProp.PrefabName, new List<UPropBase>());

            _propPoolDic[InPoolProp.PrefabName].Add(InPoolProp);
        }

        public void AddPoolCharacter(UCharacterActor InPoolCharacter)
        {
            if (InPoolCharacter == null)
            {
                Debug.LogError("AddPoolCharacter, UCharacterActor Is Null");
                return;
            }

            if (InPoolCharacter.CharacterStatus == null)
                return;

            InPoolCharacter.SetPoolData();

            if (_characterPoolDic.TryGetValue(InPoolCharacter.PrefebName, out List<UCharacterActor> poolList) == false)
            {
                poolList = new List<UCharacterActor>();
                _characterPoolDic.Add(InPoolCharacter.PrefebName, poolList);
            }

            poolList.Add(InPoolCharacter);


            if (InPoolCharacter.IsPlayerCharacter)
            {
                var targetAnimator = InPoolCharacter.GetAnimator;
                AddPlayerAnimatorController(targetAnimator.runtimeAnimatorController);
                targetAnimator.runtimeAnimatorController = null;
            }

        }

        /// <summary>
        /// 해당 프리팹의 현재 풀 보유 수.
        /// _characterPoolDic 은 Clear/Remove 가 코드 전체에 없어 세션 내내 비워지지 않으므로,
        /// 프리워밍 전에 이 값을 보고 이미 충분하면 건너뛰어야 무한 누적을 막을 수 있다
        /// </summary>
        public int GetPoolCharacterCount(string InPrefabName)
        {
            if (string.IsNullOrEmpty(InPrefabName))
                return 0;

            return _characterPoolDic.TryGetValue(InPrefabName, out List<UCharacterActor> poolList) ? poolList.Count : 0;
        }

        /// <summary>
        /// 프리워밍 전용 풀 삽입.
        ///
        /// AddPoolCharacter 를 쓰면 안 된다. CharacterStatus == null 조기 반환에 걸려 조용히 무시되고,
        /// 우회하더라도 SetPoolData() 가 CharacterRigidbody(=UpdateActor 에서만 생성된다)에서 NRE 를 낸다.
        /// 프리워밍 개체는 아직 상태를 가진 적이 없으므로 비활성화만 하고 그대로 담는다
        /// </summary>
        public void AddPoolCharacterPrewarm(UCharacterActor InPoolCharacter, string InPrefabName)
        {
            if (InPoolCharacter == null)
            {
                Debug.LogError("AddPoolCharacterPrewarm, UCharacterActor Is Null");
                return;
            }

            if (string.IsNullOrEmpty(InPrefabName))
            {
                Debug.LogError("AddPoolCharacterPrewarm, PrefabName Is Empty");
                return;
            }

            InPoolCharacter.gameObject.SetActive(false);

            if (_characterPoolDic.TryGetValue(InPrefabName, out List<UCharacterActor> poolList) == false)
            {
                poolList = new List<UCharacterActor>();
                _characterPoolDic.Add(InPrefabName, poolList);
            }

            poolList.Add(InPoolCharacter);
        }

        public UCharacterActor GetPoolCharacter(FCharacterSingleData InSingleData)
        {
            string prefebName = GameCharacterManager.Instance.GetPrefebName(InSingleData);

            if (_characterPoolDic.TryGetValue(prefebName, out List<UCharacterActor> poolList) == false)
                return null;

            //풀 엔트리가 외부에서 파괴되면 Unity fake-null 이 된다.
            //파괴된 것은 버리고 살아 있는 개체를 찾을 때까지 진행한다 (예전 코드는 null 을 그대로 역참조해 크래시)
            while (poolList.Count > 0)
            {
                int lastIndex = poolList.Count - 1;
                UCharacterActor resultPool = poolList[lastIndex];
                poolList.RemoveAt(lastIndex);

                if (resultPool == null)
                    continue;

                resultPool.ReactivePoolData();

                return resultPool;
            }

            return null;
        }

        public UPropBase GetPoolProp(string prefabName)
        {
            if (_propPoolDic.ContainsKey(prefabName))
            {
                int targetPoolCount = _propPoolDic[prefabName].Count;
                if (targetPoolCount > 0)
                {
                    UPropBase resultProp = _propPoolDic[prefabName][targetPoolCount - 1];
                    _propPoolDic[prefabName].RemoveAt(targetPoolCount - 1);

                    if (resultProp == null)
                    {
                        Debug.LogError("GetPoolProp, UPropBase Not Valid");
                        return null;
                    }

                    resultProp.ReactivePoolData();
                    return resultProp;
                }
            }

            return null;
        }

        public void AddPoolProjectile(ProjectileBase InPoolProjectile)
        {
            if (InPoolProjectile == null)
                return;

            if (InPoolProjectile.ActorType == EActorType.ProjectileActor)
            {
                if (_projectileActorPoolList.Contains(InPoolProjectile))
                    return;

                InPoolProjectile.SetPoolData();
                InPoolProjectile.GetGameObject.SetActive(false);

                _projectileActorPoolList.Add((ProjectileActor)InPoolProjectile);
            }
            else if (InPoolProjectile.ActorType == EActorType.ProjectileSkill)
            {
                ESkill skillType = InPoolProjectile.ProjectileDataInfo.BattleData.Skill;

                // 성능 최적화: TryGetValue 패턴으로 이중 해싱 제거
                if (!_projectileSkillPoolList.TryGetValue(skillType, out var poolList))
                {
                    poolList = new List<ProjectileSkill>();
                    _projectileSkillPoolList.Add(skillType, poolList);
                }
                else if (poolList.Contains(InPoolProjectile))
                {
                    return;  // 이미 풀에 있음
                }

                InPoolProjectile.SetPoolData();
                InPoolProjectile.GetGameObject.SetActive(false);
                poolList.Add((ProjectileSkill)InPoolProjectile);
            }
            else
            {
                Debug.LogError($"정의 되지않은 투사체 타입 ActorType = {InPoolProjectile.ActorType}");
            }
        }

        public ProjectileActor GetPoolProjectileActor()
        {
            if (_projectileActorPoolList.Count > 0)
            {
                int getIndex = _projectileActorPoolList.Count - 1;
                ProjectileActor resultProjectile = _projectileActorPoolList[getIndex];
                if (resultProjectile)
                {
                    _projectileActorPoolList.RemoveAt(getIndex);
                    return GetPoolProjectileCommon(resultProjectile);
                }
            }

            return null;
        }
        // 성능 최적화: TryGetValue 패턴으로 이중 해싱 제거 + 로컬 변수 캐싱
        public ProjectileSkill GetPoolProjectileSkill(ESkill InSkillType)
        {
            if (_projectileSkillPoolList.TryGetValue(InSkillType, out var poolList) && poolList.Count > 0)
            {
                int getIndex = poolList.Count - 1;
                ProjectileSkill resultProjectile = poolList[getIndex];
                if (resultProjectile)
                {
                    poolList.RemoveAt(getIndex);
                    return GetPoolProjectileCommon(resultProjectile);
                }
            }

            return null;
        }
        T GetPoolProjectileCommon<T>(T InProjectile) where T : ProjectileBase
        {
            InProjectile?.GetGameObject.SetActive(true);
            InProjectile?.GetPoolData();
            return InProjectile;
        }



        public void AddPlayerAnimatorController(RuntimeAnimatorController InController)
        {
            //스파인 컨트롤러 분리 안함
            //return;

            if (InController == null)
                return;

            string strName = InController.name;
            if (_animatorControllerPoolDic.ContainsKey(InController.name) == false)
            {
                List<RuntimeAnimatorController> addList = new List<RuntimeAnimatorController>();
                _animatorControllerPoolDic.Add(strName, addList);
            }

            _animatorControllerPoolDic[strName].Add(InController);
        }

        public void AddPoolWeaponObject(UWeaponAttachment InWeapon)
        {
            if (InWeapon == null)
                return;

            _attachObjectPoolDic.Add(InWeapon.AttachmentKey, InWeapon);

            InWeapon.transform.SetParent(GamePrefabManager.Instance.TemplatePrefebComp.WeaponsArea.transform);
            InWeapon.transform.localRotation = Quaternion.Euler(Vector3.zero);
            InWeapon.transform.localPosition = Vector3.zero;
            InWeapon.gameObject.SetActive(false);
        }

        public UWeaponAttachment GetWeaponObject(EquipmentMythicData equipmentData)
        {
            string weaponKey = equipmentData.AttachmentKey;
            UWeaponAttachment targetObject = null;
            if (_attachObjectPoolDic.ContainsKey(weaponKey))
            {
                targetObject = (UWeaponAttachment)_attachObjectPoolDic[weaponKey];
                _attachObjectPoolDic.Remove(weaponKey);
            }
            else
            {
                GameObject origin = GameAssetBundleManager.Instance.LoadFromFile("character_weapon", equipmentData.AssetName) as GameObject;
                if (origin != null)
                {
                    GameObject instanceObject = UnityEngine.Object.Instantiate<GameObject>(origin);
                    targetObject = instanceObject.AddComponent<UWeaponAttachment>();
                    targetObject.SetWeaponData(equipmentData);
                }
                else
                {
                    Debug.LogError(string.Format("Create New Not Exist Myth GetWeaponObject {0}", equipmentData.AssetName));
                }
            }

            if (targetObject != null)
            {
                targetObject.gameObject.SetActive(true);
            }

            return targetObject;
        }
        public UWeaponAttachment GetWeaponObject(EquipmentNormalData equipmentData)
        {
            string weaponKey = equipmentData.AttachmentKey;
            UWeaponAttachment targetObject = null;
            if (_attachObjectPoolDic.ContainsKey(weaponKey))
            {
                targetObject = (UWeaponAttachment)_attachObjectPoolDic[weaponKey];
                _attachObjectPoolDic.Remove(weaponKey);
            }
            else
            {
                GameObject origin = GameAssetBundleManager.Instance.LoadFromFile("character_weapon", equipmentData.AssetName) as GameObject;
                if (origin != null)
                {
                    GameObject instanceObject = UnityEngine.Object.Instantiate<GameObject>(origin);
                    targetObject = instanceObject.AddComponent<UWeaponAttachment>();
                    targetObject.SetWeaponData(equipmentData);
                }
                else
                {
                    Debug.LogError(string.Format("Create New Not Exist Normal GetWeaponObject {0}", equipmentData.AssetName));
                }
            }

            if (targetObject != null)
            {
                targetObject.gameObject.SetActive(true);
            }

            return targetObject;
        }


        public RuntimeAnimatorController GetPlayerAnimatorController(string InAnimatorName)
        {
            //스파인 컨트롤러 분리 안함
            //return null;

            if (_animatorControllerPoolDic.Count > 0)
            {
                if (_animatorControllerPoolDic.ContainsKey(InAnimatorName))
                {
                    if (_animatorControllerPoolDic[InAnimatorName].Count > 0)
                    {
                        int getIndex = _animatorControllerPoolDic[InAnimatorName].Count - 1;
                        RuntimeAnimatorController targetController = _animatorControllerPoolDic[InAnimatorName][getIndex];
                        _animatorControllerPoolDic[InAnimatorName].RemoveAt(getIndex);

                        Debug.Log(string.Format("Use Pool RuntimeAnimatorController {0}", InAnimatorName));
                        return targetController;
                    }
                }
            }

            return CreateAnimController(InAnimatorName);
        }

        public RuntimeAnimatorController CreateAnimController(string InAnimatorName)
        {
            //현재 SingleSword 애니메이션만 사용중 필요하면 PXAnimationConfig 를 추가하여 애니메이터별로 분리
            //PXAnimationConfig animation_SingleSword = GamePrefabManager.Instance.TemplatePrefebComp.animationConfig_SingleSword;

            AnimatorOverrideController origin = GameAssetBundleManager.Instance.LoadFromFile("character_animator", InAnimatorName) as AnimatorOverrideController;
            if (origin != null)
            {
                AnimatorOverrideController animController = UnityEngine.Object.Instantiate<AnimatorOverrideController>(origin);

                /*
                var overridesOrigins = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                origin.GetOverrides(overridesOrigins);

                // 기존 오버라이드 컨트롤러의 모든 클립을 순회
                foreach (var clipPair in overridesOrigins)
                {
                    string stateName = clipPair.Key.name;
                    // List에서 matching되는 클립 찾기
                    var findIndex = animation_SingleSword.stateNames.FindIndex(x => x == stateName);
                    if (findIndex >= 0)
                    {
                        animController[clipPair.Key] = animController[animation_SingleSword.animationClips[findIndex]];
                    }
                    else
                    {
                        Debug.LogError($"CreateAnimController Not Exist Clip {stateName}");
                    }
                }
                */

                return animController;
            }
            else
            {
                Debug.LogError($"Create New Not Exist RuntimeAnimatorController {InAnimatorName}");
                return null;
            }
        }


        public void AddEffectData(EffectData InData)
        {
            if (InData == null)
                return;

            var effectKey = InData.EffectKey;

            if (_effectDataDic.TryGetValue(effectKey, out List<EffectData> poolList) == false)
            {
                poolList = new List<EffectData>();
                _effectDataDic.Add(effectKey, poolList);
            }

            //풀에 상한이 없으면 스파이크가 몰린 판의 최대치가 세션 내내 메모리에 남는다
            int maxPerKey = GameClientPlayConfig.Instance.performance.effectPoolMaxPerKey;

            if (maxPerKey > 0 && poolList.Count >= maxPerKey)
            {
                GameObject.Destroy(InData.GetGameObject);
                return;
            }

            poolList.Add(InData);
        }

        public EffectData GetEffectData<T>(string InBundle, string InName) where T : EffectData
        {
            EffectData targetEffect = null;

            if (_effectDataDic.TryGetValue((InBundle, InName), out List<EffectData> poolList))
            {
                //풀 개체가 외부에서 파괴되면 Unity fake-null 이 된다.
                //파괴된 것은 버리고 살아 있는 개체를 찾을 때까지 진행한다
                while (poolList.Count > 0)
                {
                    int getIndex = poolList.Count - 1;
                    EffectData resultEffect = poolList[getIndex];
                    poolList.RemoveAt(getIndex);

                    if (resultEffect == null)
                        continue;

                    targetEffect = resultEffect;
                    break;
                }
            }

            if (targetEffect == null)
                targetEffect = CreateEffectData<T>(InBundle, InName, true);

            return targetEffect;
        }

        /// <summary>
        /// 해당 이펙트의 현재 풀 보유 수. 프리워밍 전에 이미 충분한지 보는 용도
        /// </summary>
        public int GetPoolEffectCount(string InBundle, string InName)
        {
            if (string.IsNullOrEmpty(InBundle) || string.IsNullOrEmpty(InName))
                return 0;

            return _effectDataDic.TryGetValue((InBundle, InName), out List<EffectData> poolList) ? poolList.Count : 0;
        }

        /// <summary>
        /// 프리워밍 전용 풀 삽입.
        ///
        /// 일반 반환 경로(EffectData.StopEffect)를 쓰면 안 된다. 갓 만든 개체는 IsPlaying == false 라
        /// StopEffect 가 맨 앞에서 그대로 반환해 버려 풀에 도달하지 못한다.
        /// AddPoolCharacterPrewarm 과 같은 이유로 반환 경로를 우회한다.
        ///
        /// 풀은 (번들, 이름)으로만 구분하고 컴포넌트 타입은 보지 않는다.
        /// 그래서 타입을 잘못 붙여 넣으면 GetEffectData&lt;EffectSpriteAni&gt; 가 EffectParticle 을 꺼내 가게 된다.
        /// 프리팹 구성에서 타입을 직접 판별한다 (LightningOrbHit 처럼 파티클이 없는 스프라이트 이펙트가 있다)
        /// </summary>
        public bool PrewarmEffectData(string InBundle, string InName)
        {
            GameObject originObject = GameAssetBundleManager.Instance.LoadFromFile(InBundle, InName) as GameObject;

            if (originObject == null)
                return false;

            //EffectSpriteAni.InitData 가 루트의 SpriteRenderer / Animator 를 요구한다
            bool isSpriteAni = originObject.GetComponent<SpriteRenderer>() != null
                            && originObject.GetComponent<Animator>() != null;

            EffectData addEffectData = isSpriteAni
                ? CreateEffectData<EffectSpriteAni>(InBundle, InName, true)
                : CreateEffectData<EffectParticle>(InBundle, InName, true);

            if (addEffectData == null)
                return false;

            AddEffectData(addEffectData);

            return true;
        }

        /// <summary>
        /// 이펙트 풀을 비웁니다.
        /// 캐릭터 풀과 달리 이펙트는 스킬 구성에 따라 종류가 크게 달라지므로
        /// 전투 모드가 끝날 때 정리하지 않으면 안 쓰는 이펙트가 세션 내내 남습니다
        /// </summary>
        public void ClearEffectPool()
        {
            foreach (KeyValuePair<(string bundle, string name), List<EffectData>> entryData in _effectDataDic)
            {
                List<EffectData> poolList = entryData.Value;

                for (int i = 0; i < poolList.Count; i++)
                {
                    if (poolList[i] != null)
                        GameObject.Destroy(poolList[i].GetGameObject);
                }

                poolList.Clear();
            }

            _effectDataDic.Clear();
        }

        public EffectData CreateEffectData<T>(string InBundle, string InName, bool IsObjectPool) where T : EffectData
        {
            if (InBundle.Length == 0 || InName.Length == 0)
            {
                Debug.LogError(string.Format("CreateEffectData In Data Not Valid, InBundle = {0}, InName = {1}", InBundle, InName));
                return null;
            }

            //Debug.Log(string.Format("Create New CreateEffectData {0}, {1}", InBundle, InName));
            GameObject originObject = GameAssetBundleManager.Instance.LoadFromFile(InBundle, InName) as GameObject;
            if (originObject == null)
            {
                //예전에는 로그만 찍고 그대로 Instantiate(null) 로 넘어가 예외가 났다
                Debug.LogError(string.Format("CreateEffectData Not Exist, {0}, {1}", InBundle, InName));
                return null;
            }

            GameObject targetObject = GameObject.Instantiate(originObject, Vector3.zero, Quaternion.Euler(0, 90, 0));

            EffectData addEffectData = targetObject.AddComponent<T>();
            addEffectData.SetContext();
            addEffectData.SetEffectData(InBundle, InName, IsObjectPool);

            return addEffectData;

        }

        // 제네릭 메서드로 특정 타입의 Prop 생성/가져오기
        public T GetOrCreateProp<T>(string bundleName, string propName) where T : UPropBase
        {
            // 풀에서 먼저 찾아보기
            UPropBase pooledProp = GetPoolProp(propName);
            if (pooledProp != null && pooledProp is T)
            {
                return pooledProp as T;
            }

            // 풀에 없거나 타입이 다르면 새로 생성
            return CreateProp<T>(bundleName, propName);
        }

        public T CreateProp<T>(string bundleName, string propName) where T : UPropBase
        {
            if (bundleName.Length == 0 || propName.Length == 0)
            {
                Debug.LogError($"CreateProp<T> Invalid Data, bundleName = {bundleName}, propName = {propName}");
                return null;
            }

            GameObject originObject = GameAssetBundleManager.Instance.LoadFromFile(bundleName, propName) as GameObject;
            if (originObject == null)
            {
                Debug.LogError($"CreateProp<T> Not Exist, {bundleName}, {propName}");
                return null;
            }

            GameObject targetObject = GameObject.Instantiate(originObject, Vector3.zero, Quaternion.identity);
            GameBattleUtilityManager.Instance.ApplyToonOutlineShader(targetObject, 0);

            T propComponent = targetObject.GetComponent<T>();
            if (propComponent == null)
            {
                propComponent = targetObject.AddComponent<T>();
            }

            targetObject.transform.SetParent(GamePrefabManager.Instance.TemplatePrefebComp.PropsArea.transform);
            propComponent.SetPropData(propName);
            return propComponent;
        }

        #region DynamicSpell Pool

        /// <summary>
        /// DynamicSpell을 풀에 반환
        /// </summary>
        public void AddPoolDynamicSpell(DynamicSpell InSpell)
        {
            if (InSpell == null)
                return;

            string poolKey = InSpell.PoolKey;
            if (string.IsNullOrEmpty(poolKey))
            {
                // PoolKey가 없으면 즉시 삭제
                Object.Destroy(InSpell.gameObject);
                return;
            }

            InSpell.SetPoolData();
            InSpell.transform.SetParent(GamePrefabManager.Instance.TemplatePrefebComp.DynamicObjectArea.transform);

            if (!_dynamicSpellPoolDic.TryGetValue(poolKey, out var poolList))
            {
                poolList = new List<DynamicSpell>();
                _dynamicSpellPoolDic.Add(poolKey, poolList);
            }

            if (!poolList.Contains(InSpell))
            {
                poolList.Add(InSpell);
            }
        }

        /// <summary>
        /// 풀에서 DynamicSpell 가져오기 (없으면 null 반환)
        /// </summary>
        public T GetPoolDynamicSpell<T>(string poolKey) where T : DynamicSpell
        {
            if (string.IsNullOrEmpty(poolKey))
                return null;

            if (_dynamicSpellPoolDic.TryGetValue(poolKey, out var poolList) && poolList.Count > 0)
            {
                int lastIndex = poolList.Count - 1;
                DynamicSpell spell = poolList[lastIndex];
                poolList.RemoveAt(lastIndex);

                if (spell != null && spell is T typedSpell)
                {
                    typedSpell.GetPoolData();
                    return typedSpell;
                }
            }

            return null;
        }

        /// <summary>
        /// 풀에서 가져오거나 새로 생성
        /// </summary>
        public T GetOrCreateDynamicSpell<T>(string poolKey, Transform parent = null) where T : DynamicSpell
        {
            // 풀에서 먼저 시도
            T pooledSpell = GetPoolDynamicSpell<T>(poolKey);
            if (pooledSpell != null)
            {
                if (parent != null)
                    pooledSpell.transform.SetParent(parent);
                return pooledSpell;
            }

            // 새로 생성
            GameObject newObj = new GameObject(poolKey);
            if (parent != null)
                newObj.transform.SetParent(parent);
            else
                newObj.transform.SetParent(GamePrefabManager.Instance.TemplatePrefebComp.DynamicObjectArea.transform);

            T newSpell = newObj.AddComponent<T>();
            return newSpell;
        }

        #endregion
    }
}