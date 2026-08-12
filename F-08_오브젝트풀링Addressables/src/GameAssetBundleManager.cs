using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PX
{
    /// <summary>
    /// Addressables 기반 에셋 로딩 관리자
    /// 기존 AssetBundle API 호환성 유지 (Addressables 내부 구현)
    /// </summary>
    public class GameAssetBundleManager : SingletonDependency<GameAssetBundleManager>
    {
        // Addressables 에셋 캐시 (address → handle)
        private Dictionary<string, AsyncOperationHandle> loadedAssets = new Dictionary<string, AsyncOperationHandle>();

        // 로드가 진행 중인 핸들 (address → handle).
        // 이게 없으면 비동기 로드 중에 같은 주소로 동기 LoadFromFile 이 들어올 때 캐시 미스가 나서 두 번째 핸들이 생기고,
        // 나중에 도착한 비동기 콜백이 loadedAssets 를 덮어써 동기 쪽 핸들이 릴리스 없이 유실된다
        private Dictionary<string, AsyncOperationHandle> loadingAssets = new Dictionary<string, AsyncOperationHandle>();

        private GameAssetBundleManager()
        {
        }

        ~GameAssetBundleManager()
        {
        }

        public override void Awake()
        {
        }

        public override void Start()
        {
        }

        public override void Update()
        {
        }

        public void LoadDefaultBunble()
        {
            // 에셋 자체는 필요 시 자동 로드되지만, 카탈로그 로드(= Addressables 초기화)는 여기서 미리 시작해 둔다.
            // 첫 로드 요청이 초기화를 트리거하는 구조로 두면 초기화가 끝나기 전에 들어온 동기 로드가
            // 카탈로그 준비 여부를 판단할 수 없는 구간에 걸린다 (IsAddressableKeyMissing 주석 참고)
            // 필요하다면 GameAddressablesManager.DownloadRuntimeAssetsAsync() 사용
            Addressables.InitializeAsync(true);
        }

        /// <summary>
        /// Addressables 주소 생성 (번들명/에셋명 형식)
        /// 대소문자 통일을 위해 자동으로 소문자로 변환
        /// </summary>
        private string GetAddressableAddress(string InBundleName, string InLoadObject)
        {
            // Addressables 주소 형식: "bundlename/assetname" (모두 소문자)
            // 예: "widget_popup/PXPopup_LoginHud" → "widget_popup/pxpopup_loginhud"
            string bundleName = InBundleName.ToLowerInvariant();
            string assetName = InLoadObject.ToLowerInvariant();
            return $"{bundleName}/{assetName}";
        }

        // Addressables 초기화 중간 단계에서 잠깐 등록되는 껍데기 locator 의 id (InitializationOperation.cs)
        private const string kCatalogBootstrapLocatorId = "CatalogLocator";

        /// <summary>
        /// 주소가 Addressables 카탈로그에 "확실히 없는지" 판단한다.
        ///
        /// 등록되지 않은 주소로 LoadAssetAsync 를 호출하면 Addressables 가 InvalidKeyException 을 콘솔에 남긴다.
        /// 아직 아트가 없는 아이콘처럼 "없는 게 정상"인 주소는 이 로그가 노이즈이므로 로드 전에 걸러낸다.
        /// ResourceLocators 조회는 딕셔너리 탐색이라 비용이 거의 없다.
        ///
        /// 주의: 카탈로그 로드 전에는 에셋 주소가 하나도 등록되어 있지 않아 모든 주소가 "없음"으로 보인다.
        /// 이때 걸러내면 정상 에셋까지 못 읽으므로, 판단이 불가능한 상태에서는 false(= 일단 로드 시도)를 돌려준다.
        ///
        /// "locator 가 하나라도 있으면 판단 가능"으로 보면 안 된다.
        /// Addressables 초기화는 카탈로그 파일 위치만 담은 껍데기 locator("CatalogLocator")를 먼저 등록하고,
        /// 그 다음에 catalog.bin 을 읽어 실제 주소가 담긴 locator 를 추가한다 (InitializationOperation.cs).
        /// 실기기에서는 이 두 단계 사이가 수백 ms 벌어져, 그 구간에 들어온 로드가 전부 "주소 없음"으로 오판된다.
        /// </summary>
        public bool IsAddressableKeyMissing(string InAddress)
        {
            if (string.IsNullOrEmpty(InAddress))
                return true;

            bool hasCatalogLocator = false;
            foreach (var locator in Addressables.ResourceLocators)
            {
                // 초기화 중간 단계의 껍데기 locator — 여기엔 게임 에셋 주소가 들어있지 않으므로 판단 근거가 못 된다
                if (locator.LocatorId == kCatalogBootstrapLocatorId)
                    continue;

                hasCatalogLocator = true;
                if (locator.Locate(InAddress, typeof(UnityEngine.Object), out _))
                    return false;
            }

            // 카탈로그가 아직 준비되지 않았으면 판단 보류 — 기존 경로대로 로드를 시도한다
            return hasCatalogLocator;
        }

        /// <summary>
        /// 에셋 동기 로드 (호환성 유지용)
        /// 주의: WaitForCompletion()은 메인 스레드를 블록하므로 성능 영향 있음
        /// 새 코드는 LoadFromFileAsync() 사용 권장
        /// </summary>
        public UnityEngine.Object LoadFromFile(string InBundleName, string InLoadObject)
        {
            if (string.IsNullOrEmpty(InBundleName) || string.IsNullOrEmpty(InLoadObject))
            {
                //Debug.LogError($"[Addressables] Invalid parameters - Bundle: {InBundleName}, Object: {InLoadObject}");
                return null;
            }

            string address = GetAddressableAddress(InBundleName, InLoadObject);

            // 캐시 확인
            if (loadedAssets.ContainsKey(address))
            {
                //Debug.Log($"[Addressables] Cache hit: {address}");
                AsyncOperationHandle handle = loadedAssets[address];
                if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded)
                {
                    return handle.Result as UnityEngine.Object;
                }
                else
                {
                    // 캐시가 유효하지 않으면 제거
                    loadedAssets.Remove(address);
                }
            }

            // 같은 주소를 비동기로 이미 로드 중이면 그 핸들을 그대로 기다린다.
            // 새 핸들을 또 만들면 둘 중 하나가 릴리스되지 않고 새어나간다
            if (loadingAssets.TryGetValue(address, out AsyncOperationHandle inflightHandle))
            {
                if (inflightHandle.IsValid())
                {
                    UnityEngine.Object inflightResult = inflightHandle.WaitForCompletion() as UnityEngine.Object;

                    if (inflightHandle.Status == AsyncOperationStatus.Succeeded && inflightResult != null)
                        return inflightResult;
                }

                loadingAssets.Remove(address);
            }

            // 카탈로그에 없는 주소는 로드를 시도하지 않는다 (InvalidKeyException 로그 방지)
            if (IsAddressableKeyMissing(address))
                return null;

            // Addressables 로드 (동기)
            //Debug.Log($"[Addressables] Loading (sync): {address}");

            try
            {
                AsyncOperationHandle<UnityEngine.Object> handle = Addressables.LoadAssetAsync<UnityEngine.Object>(address);

                // WaitForCompletion()으로 동기 변환 (메인 스레드 블록)
                UnityEngine.Object result = handle.WaitForCompletion();

                if (handle.Status == AsyncOperationStatus.Succeeded && result != null)
                {
                    loadedAssets[address] = handle;
                    //Debug.Log($"[Addressables] Successfully loaded: {address}");
                    return result;
                }
                else
                {
                    //Debug.LogError($"[Addressables] Load FAILED: {address}");
                    //Debug.LogError($"[Addressables] Status: {handle.Status}");
                    if (handle.OperationException != null)
                    {
                        //Debug.LogError($"[Addressables] Exception: {handle.OperationException}");
                    }

                    Addressables.Release(handle);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Addressables] Exception loading {address}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 에셋 비동기 로드 (권장)
        /// 새로운 코드는 이 메서드 사용을 권장합니다
        /// </summary>
        public void LoadFromFileAsync<T>(string InBundleName, string InLoadObject, Action<T> onComplete) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(InBundleName) || string.IsNullOrEmpty(InLoadObject))
            {
                //Debug.LogError($"[Addressables] Invalid parameters - Bundle: {InBundleName}, Object: {InLoadObject}");
                onComplete?.Invoke(null);
                return;
            }

            string address = GetAddressableAddress(InBundleName, InLoadObject);

            // 캐시 확인
            if (loadedAssets.ContainsKey(address))
            {
                //Debug.Log($"[Addressables] Cache hit (async): {address}");
                AsyncOperationHandle handle = loadedAssets[address];
                if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(handle.Result as T);
                    return;
                }
                else
                {
                    loadedAssets.Remove(address);
                }
            }

            // 같은 주소가 이미 로드 중이면 핸들을 새로 만들지 않고 그 완료에 편승한다
            if (loadingAssets.TryGetValue(address, out AsyncOperationHandle inflightHandle) && inflightHandle.IsValid())
            {
                inflightHandle.Completed += (op) => onComplete?.Invoke(op.Result as T);
                return;
            }

            // 카탈로그에 없는 주소는 로드를 시도하지 않는다 (InvalidKeyException 로그 방지)
            if (IsAddressableKeyMissing(address))
            {
                onComplete?.Invoke(null);
                return;
            }

            // Addressables 비동기 로드
            //Debug.Log($"[Addressables] Loading (async): {address}");

            AsyncOperationHandle<T> asyncHandle = Addressables.LoadAssetAsync<T>(address);
            loadingAssets[address] = asyncHandle;

            asyncHandle.Completed += (op) =>
            {
                loadingAssets.Remove(address);

                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    //Debug.Log($"[Addressables] Successfully loaded (async): {address}");
                    loadedAssets[address] = op;
                    onComplete?.Invoke(op.Result);
                }
                else
                {
                    //Debug.LogError($"[Addressables] Load FAILED (async): {address}");
                    //Debug.LogError($"[Addressables] Status: {op.Status}");
                    if (op.OperationException != null)
                    {
                        //Debug.LogError($"[Addressables] Exception: {op.OperationException}");
                    }

                    Addressables.Release(op);
                    onComplete?.Invoke(null);
                }
            };
        }

        /// <summary>
        /// 특정 에셋 해제
        /// </summary>
        public void ReleaseAsset(string InBundleName, string InLoadObject)
        {
            string address = GetAddressableAddress(InBundleName, InLoadObject);

            if (loadedAssets.ContainsKey(address))
            {
                //Debug.Log($"[Addressables] Releasing: {address}");
                Addressables.Release(loadedAssets[address]);
                loadedAssets.Remove(address);
            }
        }

        /// <summary>
        /// 모든 로드된 에셋 해제
        /// </summary>
        public void ReleaseAllAssets()
        {
            //Debug.Log($"[Addressables] Releasing all assets ({loadedAssets.Count})");

            foreach (var handle in loadedAssets.Values)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            loadedAssets.Clear();

            //진행 중이던 로드는 완료 콜백이 loadedAssets 에 넣기 전에 여기서 끊긴다. 추적만 비우고 핸들은 콜백이 처리한다
            loadingAssets.Clear();
        }

        /// <summary>
        /// 호환성 유지: 번들 경로 반환 (Addressables에서는 사용 안 함)
        /// </summary>
        [Obsolete("Addressables에서는 경로가 필요 없습니다. Addressables 주소를 사용하세요.")]
        public string GetBundleFullPath(string InBundleName)
        {
            //Debug.LogWarning($"[Addressables] GetBundleFullPath()는 Addressables에서 사용되지 않습니다: {InBundleName}");
            return GetAddressableAddress(InBundleName, "");
        }

        public override void OnDestroy()
        {
            // 종료 시 모든 핸들 해제
            ReleaseAllAssets();
        }
    }
}
