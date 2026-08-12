using PX;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;



public class EffectData : UContextBase
{
    static Dictionary<Transform, Dictionary<(string, string), int>> attachEffectDic = new Dictionary<Transform, Dictionary<(string, string), int>>();

    public EEffectType GetEEffectType { get; protected set; }
    public bool IsLoop { get; protected set; }
    [ReadOnly] protected bool IsPlaying;

    Action<EffectData> stopEffectEvent;

    public float nowDeltaTime { get; protected set; }
    public float nowDuration { get; protected set; }
    public bool IsObjectPool { get; private set; }
    //조회마다 string.Format 으로 키를 만들면 그만큼 GC 할당이 쌓인다. 튜플은 할당이 없다
    public (string bundle, string name) EffectKey { get; private set; }
    public string EffectBundle { get; private set; }
    public string EffectName { get; private set; }

    //풀 반환 시 되돌릴 원본 로컬 트랜스폼.
    //프리팹 스케일이 1 이 아닐 수 있어 Vector3.one 하드 리셋은 쓸 수 없다
    Vector3 _originLocalScale = Vector3.one;
    Quaternion _originLocalRotation = Quaternion.identity;

    /// <summary>
    /// GameEffectManager._activeEffects 안에서의 자기 위치. 비활성이면 -1.
    /// swap-remove 를 O(1) 로 하기 위해 개체가 직접 들고 있다
    /// </summary>
    public int ActiveEffectIndex { get; private set; } = -1;

    public void SetActiveEffectIndex(int InIndex)
    {
        ActiveEffectIndex = InIndex;
    }

    /// <summary>
    /// GameEffectManager 가 매 프레임 일괄 호출한다.
    /// 이펙트마다 MonoBehaviour Update() 를 두지 않으려고 Tick 으로 뺐다
    /// </summary>
    public virtual void Tick(float InDeltaTime)
    {
        if (IsPlaying == false)
            return;

        nowDeltaTime = InDeltaTime;
        nowDuration += InDeltaTime;
    }

    protected override void InitData()
    {
        base.InitData();

        nowDeltaTime = 0;
        nowDuration = 0;
        IsPlaying = false;

        GetTransform.SetParent(GamePrefabManager.Instance.TemplatePrefebComp.EffectsArea.transform);
        GetTransform.position = Vector3.zero;
        GetTransform.rotation = Quaternion.Euler(0, 0, 0);

        //여기까지가 최초 재생이 보게 되는 상태다. 그대로 캐싱해 두면
        //풀에서 꺼낸 재사용 개체도 최초 재생과 완전히 같은 트랜스폼에서 시작한다
        _originLocalScale = GetTransform.localScale;
        _originLocalRotation = GetTransform.localRotation;

#if UNITY_EDITOR
        GetGameObject.AddComponent<FixShadersForEditor>();
#endif

        GetGameObject.SetActive(false);
    }

    virtual public void PlayEffect(bool InLoop = false)
    {
        IsLoop = InLoop;
        nowDuration = 0;

        if (IsPlaying == false)
        {
            IsPlaying = true;
            GameEffectManager.Instance.AddActiveEffect(this);
        }

        GetGameObject.SetActive(true);
    }

    virtual public void StopEffectInner()
    {
    }

    public void SetEffectData(string InBundle, string InName, bool InObjectPool)
    {
        EffectBundle = InBundle;
        EffectName = InName;
        EffectKey = (InBundle, InName);

        IsObjectPool = InObjectPool;
    }

    public void StopEffect(bool InCloseEvent = true)
    {
        if (IsPlaying == false)
            return;

        IsPlaying = false;
        nowDuration = 0;

        GameEffectManager.Instance.RemoveActiveEffect(this);

        StopEffectInner();

        GetGameObject.SetActive(false);

        RemovAttachEffect(GetTransform.parent, EffectBundle, EffectName);

        if (IsObjectPool)
        {
            if(GamePrefabManager.Instance.TemplatePrefebComp != null)
            {
                Transform effectsArea = GamePrefabManager.Instance.TemplatePrefebComp.EffectsArea.transform;

                if (GetTransform.parent != effectsArea)
                {
                    //worldPositionStays 기본값(true)은 부모 스케일 비율에 맞춰 localScale 을 재계산한다.
                    //부착 이펙트를 뗄 때마다 왜곡이 누적되므로 false 로 붙이고 아래에서 원본값으로 되돌린다
                    GetTransform.SetParent(effectsArea, false);
                }
            }

            //position 만 되돌리면 직전 사용처가 바꿔 둔 회전·크기가 그대로 남아
            //다음 재생이 엉뚱한 크기/회전으로 나온다
            GetTransform.position = Vector3.zero;
            GetTransform.localRotation = _originLocalRotation;
            GetTransform.localScale = _originLocalScale;

            GameObjectPoolManager.Instance.AddEffectData(this);
        }

        if (stopEffectEvent != null && InCloseEvent)
        {
            Action<EffectData> tempAction = stopEffectEvent;
            stopEffectEvent = null;
            tempAction(this);
        }
        else
        {
            stopEffectEvent = null;
        }
    }

    public void SetStopEvent(Action<EffectData> InData)
    {
        stopEffectEvent = InData;
    }

    public void SetEffectWorldPosition(Vector3 InWorldLocate)
    {
        GetTransform.position = InWorldLocate;
    }

    public void SetEffectLocalPosition(Vector3 InLocalPosition)
    {
        GetTransform.localPosition = InLocalPosition;
    }

    public void SetEffectLocalScale(Vector3 InScale)
    {
        GetTransform.localScale = InScale;
    }

    public void SetEffectWorldRotate(Vector3 InRotate)
    {
        SetEffectWorldRotate(Quaternion.Euler(InRotate));
    }

    public void SetEffectWorldRotate(Quaternion InQuaternion)
    {
        GetTransform.rotation = InQuaternion;
    }

    public void SetEffectLocalRotate(Vector3 InLocalRotate)
    {
        SetEffectLocalRotate(Quaternion.Euler(InLocalRotate));
    }

    public void SetEffectLocalRotate(Quaternion InQuaternion)
    {
        GetTransform.localRotation = InQuaternion;
    }

    public void SetEffectAttachData(Transform InParent, Vector3 InLocalPos, Vector3 InScale, Vector3 InRotate)
    {
        AddAttachEffect(InParent, EffectBundle, EffectName);

        GetTransform.SetParent(InParent);
        SetEffectLocalPosition(InLocalPos);
        SetEffectLocalScale(InScale);
        SetEffectLocalRotate(InRotate);
    }

    public void SetEffectAttach(Transform InParent, Vector3 InLocalPos)
    {
        SetEffectAttachData(InParent, InLocalPos, Vector3.one, Vector3.zero);
    }


    /// <summary>
    /// 이펙트 컨트롤
    /// </summary>

    static (string bundle, string name) MakeAttachEffectKey(string InBundle, string InName)
    {
        return (InBundle, InName);
    }

    public static void AddAttachEffect(Transform InAttachTransform, string InBundle, string InName)
    {
        if (InAttachTransform == null)
            return;

        var resultKey = MakeAttachEffectKey(InBundle, InName);

        if (attachEffectDic.TryGetValue(InAttachTransform, out Dictionary<(string, string), int> effectCountDic) == false)
        {
            effectCountDic = new Dictionary<(string, string), int>();
            attachEffectDic.Add(InAttachTransform, effectCountDic);
        }

        effectCountDic.TryGetValue(resultKey, out int nowCount);
        effectCountDic[resultKey] = nowCount + 1;
    }
    public static void RemovAttachEffect(Transform InAttachTransform, string InBundle, string InName)
    {
        if (InAttachTransform == null)
            return;

        if (attachEffectDic.TryGetValue(InAttachTransform, out Dictionary<(string, string), int> effectCountDic) == false)
            return;

        var resultKey = MakeAttachEffectKey(InBundle, InName);

        if (effectCountDic.TryGetValue(resultKey, out int nowCount) == false)
            return;

        nowCount--;

        //카운트만 줄이고 엔트리를 남기면 부착이 끝난 뒤에도 딕셔너리가 계속 불어난다
        if (nowCount > 0)
        {
            effectCountDic[resultKey] = nowCount;
            return;
        }

        effectCountDic.Remove(resultKey);

        if (effectCountDic.Count == 0)
            attachEffectDic.Remove(InAttachTransform);
    }

    public static bool AbleAttachEffect(Transform InAttachTransform, string InBundle, string InName)
    {
        if (InAttachTransform == null)
            return false;

        var resultKey = MakeAttachEffectKey(InBundle, InName);

        int ableStackCount = 2;
        if (InBundle == FCommonDefine.ConstBundleEffectHit && InName == "LightningOrbHit")
        {
            ableStackCount = 1;
        }

        //조회만 하는 함수다. 예전에는 여기서 엔트리를 만들어 붙인 적 없는 조합까지 딕셔너리에 쌓였다
        if (attachEffectDic.TryGetValue(InAttachTransform, out Dictionary<(string, string), int> effectCountDic) == false)
            return true;

        if (effectCountDic.TryGetValue(resultKey, out int nowCount) == false)
            return true;

        return nowCount < ableStackCount;
    }

    /// <summary>
    /// 부착 이펙트 레지스트리를 비웁니다.
    /// 키가 Transform 이라 캐릭터가 파괴되면 죽은 엔트리(Unity fake-null)가 그대로 남으므로
    /// 캐릭터를 전부 정리하는 시점에 함께 비웁니다
    /// </summary>
    public static void ClearAllAttachEffect()
    {
        attachEffectDic.Clear();
    }
}