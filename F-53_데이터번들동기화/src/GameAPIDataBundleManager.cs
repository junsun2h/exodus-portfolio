using Firebase.Firestore;
using Firebase.Storage;
using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace PX
{
    // EDataBundleCategory는 GameDBClientManager.cs에 정의됨

    public delegate void GameAPIDataBundleCompleteDelegate();
    public delegate void GameAPIDataBundleDownloadProgressDelegate(int inNow, int inTotal);
    public delegate void GameAPIDataBundleLoadProgressDelegate(int inNow, int inTotal);

    public class GameAPIDataBundleManager : SingletonDependency<GameAPIDataBundleManager>
    {
        #region Constants and Properties
        private const string CLIENT_BUNDLE_VERSION = "ClientBundleVersion";
        private const string PREF_BUNDLE_VERSION = "DataBundleVersion";
        private const string LOCAL_BUNDLE_FOLDER = "DataBundles";
        private const int MAX_BUNDLE_SIZE_BYTES = 50 * 1024 * 1024; // 50MB

        public GameAPIDataBundleCompleteDelegate gameAPIDataBundleCompleteDelegate;
        public GameAPIDataBundleDownloadProgressDelegate gameAPIDataBundleDownloadProgressDelegate;
        public GameAPIDataBundleLoadProgressDelegate gameAPIDataBundleLoadProgressDelegate;

        private string LocalBundleFolderPath => Path.Combine(Application.persistentDataPath, LOCAL_BUNDLE_FOLDER);
        //public FirebaseFirestore firebaseFirestore => FirebaseFirestore.DefaultInstance;
        public FirebaseFirestore firebaseFirestore => FBMainManager.Instance.firebaseFirestore;
        public FirebaseStorage firebaseStorage => FBMainManager.Instance.firebaseStorage;
        #endregion

        #region Firebase Storage Setup
        // private FirebaseStorage _firebaseStorage_database;
        // public FirebaseStorage firebaseStorage_database
        // {
        //     get
        //     {
        //         if (_firebaseStorage_database == null)
        //         {
        //             _firebaseStorage_database = CreateFirebaseStorageInstance();
        //         }
        //         return _firebaseStorage_database;
        //     }
        // }

        // private FirebaseStorage CreateFirebaseStorageInstance()
        // {
        //     try
        //     {
        //         if (TemplateLocalData.UseLocalEmulator)
        //         {
        //             return FirebaseStorage.GetInstance(Firebase.FirebaseApp.DefaultInstance, FBMainManager.LOCAL_EMULATOR_STORAGE_BUCKET);
        //         }
        //         else
        //         {
        //             Debug.Log("[DataBundle] Using Firebase Storage Cloud: " + FBMainManager.Instance.SERVER_STORAGE_BUCKET);
        //             return FirebaseStorage.GetInstance(FBMainManager.Instance.SERVER_STORAGE_BUCKET);
        //         }
        //     }
        //     catch (System.Exception ex)
        //     {
        //         GameLogSystem.LogError($"[DataBundle] Error setting up Firebase storage: {ex}");
        //         throw;
        //     }
        // }
        #endregion

        #region Initialization
        private GameAPIDataBundleManager() { }
        ~GameAPIDataBundleManager() { }

        public override void Awake()
        {
            EnsureLocalBundleFolderExists();
        }

        private void EnsureLocalBundleFolderExists()
        {
            try
            {
                if (!Directory.Exists(LocalBundleFolderPath))
                {
                    Directory.CreateDirectory(LocalBundleFolderPath);
                    Debug.Log($"[DataBundle] Created local bundle directory: {LocalBundleFolderPath}");
                }
            }
            catch (Exception ex)
            {
                GameLogSystem.LogError($"[DataBundle] Failed to create local bundle directory: {ex.Message}");
                // 안드로이드에서 폴더 생성 실패 시에도 앱이 계속 실행되도록 함
            }
        }
        #endregion

        #region Local File Management
        private string GetLocalBundleFilePath(EDataBundleCategory category, int version)
        {
            return Path.Combine(LocalBundleFolderPath, $"{category}.v{version}.bin.gz");
        }

        private void SaveBundleToLocalFile(EDataBundleCategory category, int version, byte[] bundleData)
        {
            try
            {
                // 안드로이드 저장 공간 체크
                if (!CheckAvailableStorage(bundleData.Length))
                {
                    GameLogSystem.LogError($"[DataBundle] Insufficient storage space for {category}.v{version}");
                    return;
                }

                string filePath = GetLocalBundleFilePath(category, version);
                CleanupOldVersionFiles(category, version);

                // 안드로이드에서 안전한 파일 쓰기
                WriteFilesSafely(filePath, bundleData);
            }
            catch (Exception ex)
            {
                GameLogSystem.LogError($"[DataBundle] Failed to save bundle to local file: {ex.Message}");
                // 안드로이드에서 크래시 방지를 위해 throw 제거
            }
        }

        private bool CheckAvailableStorage(int requiredBytes)
        {
            try
            {
                var driveInfo = new DriveInfo(LocalBundleFolderPath);
                long availableBytes = driveInfo.AvailableFreeSpace;
                long requiredWithBuffer = requiredBytes * 2; // 50% 버퍼 추가

                return availableBytes > requiredWithBuffer;
            }
            catch (Exception ex)
            {
                GameLogSystem.LogError($"[DataBundle] Could not check storage space: {ex.Message}");
                return true; // 체크 실패 시 진행 허용
            }
        }

        private void WriteFilesSafely(string filePath, byte[] data)
        {
            string tempFilePath = filePath + ".tmp";

            try
            {
                // 임시 파일에 먼저 쓰기
                File.WriteAllBytes(tempFilePath, data);

                // 기존 파일 삭제 후 임시 파일을 최종 파일로 이동
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempFilePath, filePath);
            }
            catch (Exception)
            {
                // 실패 시 임시 파일 정리
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
                throw;
            }
        }

        private void CleanupOldVersionFiles(EDataBundleCategory category, int newVersion)
        {
            try
            {
                if (!Directory.Exists(LocalBundleFolderPath)) return;

                string pattern = $"{category}.v*.bin.gz";
                string[] existingFiles = Directory.GetFiles(LocalBundleFolderPath, pattern);

                foreach (string filePath in existingFiles)
                {
                    string fileName = Path.GetFileName(filePath);
                    if (TryExtractVersionFromFileName(fileName, category, out int existingVersion) &&
                        existingVersion != newVersion)
                    {
                        File.Delete(filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                GameLogSystem.LogError($"[DataBundle] Error cleaning up old version files: {ex.Message}");
            }
        }

        private bool TryExtractVersionFromFileName(string fileName, EDataBundleCategory category, out int version)
        {
            version = 0;
            string expectedPrefix = $"{category}.v";
            string expectedSuffix = ".bin.gz";

            if (fileName.StartsWith(expectedPrefix) && fileName.EndsWith(expectedSuffix))
            {
                int startIndex = expectedPrefix.Length;
                int endIndex = fileName.Length - expectedSuffix.Length;
                string versionStr = fileName.Substring(startIndex, endIndex - startIndex);
                return int.TryParse(versionStr, out version);
            }

            return false;
        }
        #endregion

        #region Version Management
        public void RequestCheckVersionDataBundle()
        {
            //Review 프로젝트 이동동후 본프로젝트로 다시 설치시 리뷰버전에서 생성한 번들데이터로 인해 번들초기화에 실패함.
            //Dev 프로젝트사용시 번들 초기화 하도록 지정
            if (GameAPIServerVersionCheckManager.Instance.IsReleaseProject() == false)
            {
                DeleteClientDataBundleVersion();
            }

            var requestData = CreateVersionCheckRequest();
            var requestCall = new FRequestHttpsCall(ERequestHttpsCallType.DataBundle_CheckVersion, requestData, (resp) =>
            {
            });
            GameNetworkManager.Instance.SendRequestHttpCall(requestCall);
        }

        private Dictionary<string, object> CreateVersionCheckRequest()
        {
            var requestData = new Dictionary<string, object>();
            var typeDatas = new JSONObject();
            var localBundleVersions = GetClientDataBundleVersion();

            foreach (var kv in localBundleVersions)
            {
                typeDatas.Add(kv.Key.ToString(), kv.Value);
            }

            requestData.Add(CLIENT_BUNDLE_VERSION, typeDatas.ToString());
            requestData.Add("IsLocalEmurator", TemplateLocalData.UseLocalEmulator);
            return requestData;
        }

        private Dictionary<EDataBundleCategory, int> GetClientDataBundleVersion()
        {
            if (!PlayerPrefs.HasKey(PREF_BUNDLE_VERSION))
            {
                return InitializeDefaultVersions();
            }
            return ParseStoredVersions();
        }

        private Dictionary<EDataBundleCategory, int> InitializeDefaultVersions()
        {
            var versions = new Dictionary<EDataBundleCategory, int>();
            foreach (EDataBundleCategory category in Enum.GetValues(typeof(EDataBundleCategory)))
            {
                if (category != EDataBundleCategory.None)
                {
                    versions[category] = 0;
                }
            }
            return versions;
        }

        private Dictionary<EDataBundleCategory, int> ParseStoredVersions()
        {
            var versions = new Dictionary<EDataBundleCategory, int>();
            string rawData = PlayerPrefs.GetString(PREF_BUNDLE_VERSION);

            if (string.IsNullOrEmpty(rawData))
            {
                GameLogSystem.LogError("[DataBundle] Stored bundle version data is empty");
                return versions;
            }

            JSONNode root = JSON.Parse(rawData);
            if (root == null)
            {
                GameLogSystem.LogError("[DataBundle] Failed to parse stored bundle version data");
                return versions;
            }

            foreach (var kv in root)
            {
                if (Enum.TryParse<EDataBundleCategory>(kv.Key, out var category))
                {
                    versions[category] = kv.Value.AsInt;
                }
                else
                {
                    GameLogSystem.LogError($"[DataBundle] Unknown category in stored data: {kv.Key}");
                }
            }

            return versions;
        }

        private void SaveClientDataBundleVersion(JSONNode deltaNode)
        {
            var currentVersions = GetLocalDataBundleVersions();

            foreach (var kv in deltaNode)
            {
                if (Enum.TryParse<EDataBundleCategory>(kv.Key, out var category))
                {
                    currentVersions[category] = kv.Value.AsInt;
                }
                else
                {
                    GameLogSystem.LogError($"[DataBundle] Unknown category while saving: {kv.Key}");
                }
            }

            var jsonObject = new JSONObject();
            foreach (var kv in currentVersions)
            {
                jsonObject.Add(kv.Key.ToString(), kv.Value);
            }

            PlayerPrefs.SetString(PREF_BUNDLE_VERSION, jsonObject.ToString());
        }
        public void DeleteClientDataBundleVersion()
        {
            PlayerPrefs.DeleteKey(PREF_BUNDLE_VERSION);
        }

        private Dictionary<EDataBundleCategory, int> GetLocalDataBundleVersions()
        {
            var versions = new Dictionary<EDataBundleCategory, int>();
            string rawData = PlayerPrefs.GetString(PREF_BUNDLE_VERSION);

            if (string.IsNullOrEmpty(rawData)) return versions;

            foreach (var kv in JSON.Parse(rawData))
            {
                if (Enum.TryParse<EDataBundleCategory>(kv.Key, out var category))
                {
                    versions[category] = kv.Value.AsInt;
                }
                else
                {
                    GameLogSystem.LogError($"[DataBundle] Unknown category in local data: {kv.Key}");
                }
            }

            return versions;
        }
        #endregion

        #region Bundle Download and Sync
        IEnumerator SyncBundlesAndUpdate(JSONNode deltaNode, Action<bool> onComplete)
        {
            if (deltaNode.IsString)
            {
                deltaNode = JSON.Parse(deltaNode.Value);
            }

            if (TemplateLocalData.UseLocalEmulator)
            {
                yield return DirectHttpBundleDownload(deltaNode, onComplete);
            }
            else
            {
                yield return FirebaseStreamBundleDownload(deltaNode, onComplete);
            }
        }

        private IEnumerator FirebaseStreamBundleDownload(JSONNode deltaNode, Action<bool> onComplete)
        {
            var downloadResults = new Dictionary<EDataBundleCategory, bool>();
            //var storage = firebaseStorage_database;
            var storage = firebaseStorage;

            int downloadCount = 0;
            gameAPIDataBundleDownloadProgressDelegate?.Invoke(downloadCount, deltaNode.Count);

            foreach (var kv in deltaNode)
            {
                if (!Enum.TryParse<EDataBundleCategory>(kv.Key, out var category))
                {
                    GameLogSystem.LogError($"[DataBundle] Invalid category: {kv.Key}");
                    downloadResults[category] = false;
                    continue;
                }

                int version = kv.Value.AsInt;
                string storagePath = $"databundle/{category}.v{version}.bin.gz";
                string localFilePath = GetLocalBundleFilePath(category, version);

                yield return DownloadFileWithURL(storage, storagePath, localFilePath, category, version, downloadResults);

                downloadCount++;
                gameAPIDataBundleDownloadProgressDelegate?.Invoke(downloadCount, deltaNode.Count);
            }

            bool success = ValidateStreamDownloadResults(downloadResults);
            if (success)
            {
                SaveClientDataBundleVersion(deltaNode);
                Debug.Log($"[DataBundle] URL downloads completed successfully. Version updated.");
            }
            else
            {
                GameLogSystem.LogError($"[DataBundle] URL downloads failed. Version NOT updated.");
            }

            onComplete?.Invoke(success);
        }

        private IEnumerator DownloadFileWithURL(FirebaseStorage storage, string storagePath, string localFilePath,
    EDataBundleCategory category, int version, Dictionary<EDataBundleCategory, bool> results)
        {
            bool downloadSuccess = false;
            string tempFilePath = localFilePath + ".tmp";
            string downloadUrl = null;

            // 기존 임시 파일 정리
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }

            // Step 1: Firebase Storage에서 다운로드 URL 가져오기
            StorageReference storageRef = storage.GetReference(storagePath);
            var urlTask = storageRef.GetDownloadUrlAsync();

            yield return new WaitUntil(() => urlTask.IsCompleted || urlTask.IsFaulted);

            if (urlTask.IsFaulted)
            {
                GameLogSystem.LogError($"[DataBundle] Failed to get download URL for {category}: {urlTask.Exception?.GetBaseException()?.Message}");
                results[category] = false;
                yield break;
            }

            downloadUrl = urlTask.Result.ToString();
            Debug.Log($"[DataBundle] Got download URL for {category}: {downloadUrl}");

            // Step 2: UnityWebRequest로 파일 직접 다운로드
            using (var webRequest = new UnityEngine.Networking.UnityWebRequest(downloadUrl))
            {
                var downloadHandler = new UnityEngine.Networking.DownloadHandlerFile(tempFilePath);
                downloadHandler.removeFileOnAbort = true; // 중단 시 파일 자동 삭제
                webRequest.downloadHandler = downloadHandler;

                // 요청 헤더 설정
                webRequest.SetRequestHeader("User-Agent", "Unity-WebRequest/1.0");

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    try
                    {
                        // 다운로드 성공 시 기존 버전 파일들 정리 후 최종 파일로 이동
                        CleanupOldVersionFiles(category, version);

                        if (File.Exists(localFilePath))
                        {
                            File.Delete(localFilePath);
                        }
                        File.Move(tempFilePath, localFilePath);

                        downloadSuccess = true;
                        Debug.Log($"[DataBundle] Successfully downloaded {category} v{version} via URL ({webRequest.downloadedBytes} bytes)");
                    }
                    catch (Exception ex)
                    {
                        GameLogSystem.LogError($"[DataBundle] Error finalizing download for {category}: {ex.Message}");
                        downloadSuccess = false;
                    }
                }
                else
                {
                    GameLogSystem.LogError($"[DataBundle] URL download failed for {category}: {webRequest.error}");
                    downloadSuccess = false;
                }
            }

            // 실패 시 임시 파일 정리
            if (!downloadSuccess && File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }

            results[category] = downloadSuccess;
        }

        private bool ValidateStreamDownloadResults(Dictionary<EDataBundleCategory, bool> results)
        {
            int successCount = results.Values.Count(success => success);
            int totalCount = results.Count;

            bool allSuccessful = successCount == totalCount && totalCount > 0;

            if (!allSuccessful)
            {
                GameLogSystem.LogError($"[DataBundle] Stream download validation failed. Successful: {successCount}/{totalCount}");
            }

            return allSuccessful;
        }

        private IEnumerator DirectHttpBundleDownload(JSONNode deltaNode, Action<bool> onComplete)
        {
            var downloadResults = new Dictionary<EDataBundleCategory, bool>();

            int downloadCount = 0;
            int totalCount = deltaNode.Count;
            gameAPIDataBundleDownloadProgressDelegate?.Invoke(downloadCount, totalCount);

            // 모든 다운로드를 병렬로 시작
            var downloadTasks = new List<Task>();

            foreach (var kv in deltaNode)
            {
                if (!Enum.TryParse<EDataBundleCategory>(kv.Key, out var category))
                {
                    GameLogSystem.LogError($"[DataBundle] Invalid category: {kv.Key}");
                    downloadResults[EDataBundleCategory.None] = false;
                    continue;
                }

                int version = kv.Value.AsInt;
                string fileName = $"databundle/{category}.v{version}.bin.gz";

                var task = DownloadFileDirectlyAsync(fileName, category, version, downloadResults);
                downloadTasks.Add(task);
            }

            // 병렬 다운로드 진행 상황 모니터링
            while (downloadTasks.Count > 0)
            {
                // 완료된 태스크 찾기
                Task completedTask = null;
                foreach (var task in downloadTasks)
                {
                    if (task.IsCompleted || task.IsFaulted || task.IsCanceled)
                    {
                        completedTask = task;
                        break;
                    }
                }

                if (completedTask != null)
                {
                    downloadTasks.Remove(completedTask);
                    downloadCount++;
                    gameAPIDataBundleDownloadProgressDelegate?.Invoke(downloadCount, totalCount);
                }

                yield return null;
            }

            bool success = ValidateDirectDownloadResults(downloadResults);
            if (success)
            {
                SaveClientDataBundleVersion(deltaNode);
                Debug.Log($"[DataBundle] Direct HTTP downloads completed successfully. Version updated.");
            }
            else
            {
                GameLogSystem.LogError($"[DataBundle] Direct HTTP downloads failed. Version NOT updated.");
            }

            onComplete?.Invoke(success);
        }

        private async Task DownloadFileDirectlyAsync(string fileName, EDataBundleCategory category, int version, Dictionary<EDataBundleCategory, bool> results)
        {
            string[] headerCombinations = { "identity", "gzip, deflate", "*", "" };
            bool downloadSuccess = false;

            foreach (string acceptEncoding in headerCombinations)
            {
                if (downloadSuccess) break;

                string url = $"http://localhost:9199/v0/b/default-bucket/o/{UnityEngine.Networking.UnityWebRequest.EscapeURL(fileName)}?alt=media";

                using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
                {
                    if (!string.IsNullOrEmpty(acceptEncoding))
                    {
                        request.SetRequestHeader("Accept-Encoding", acceptEncoding);
                    }
                    request.SetRequestHeader("User-Agent", "Unity-WebRequest/1.0");

                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        SaveBundleToLocalFile(category, version, request.downloadHandler.data);
                        downloadSuccess = true;
                    }
                }
            }

            results[category] = downloadSuccess;
            if (!downloadSuccess)
            {
                GameLogSystem.LogError($"[DataBundle] All download methods failed for {category}");
            }
        }

        private IEnumerator DownloadFileDirectly(string fileName, EDataBundleCategory category, int version, Dictionary<EDataBundleCategory, bool> results)
        {
            string[] headerCombinations = { "identity", "gzip, deflate", "*", "" };
            bool downloadSuccess = false;

            foreach (string acceptEncoding in headerCombinations)
            {
                if (downloadSuccess) break;

                string url = $"http://localhost:9199/v0/b/default-bucket/o/{UnityEngine.Networking.UnityWebRequest.EscapeURL(fileName)}?alt=media";

                using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
                {
                    if (!string.IsNullOrEmpty(acceptEncoding))
                    {
                        request.SetRequestHeader("Accept-Encoding", acceptEncoding);
                    }
                    request.SetRequestHeader("User-Agent", "Unity-WebRequest/1.0");

                    yield return request.SendWebRequest();

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        SaveBundleToLocalFile(category, version, request.downloadHandler.data);
                        downloadSuccess = true;
                    }
                }
            }

            results[category] = downloadSuccess;
            if (!downloadSuccess)
            {
                GameLogSystem.LogError($"[DataBundle] All download methods failed for {category}");
            }
        }

        private bool ValidateDirectDownloadResults(Dictionary<EDataBundleCategory, bool> results)
        {
            int successCount = results.Values.Count(success => success);
            int validCategoryCount = results.Keys.Count(cat => cat != EDataBundleCategory.None);
            return validCategoryCount > 0 && successCount == validCategoryCount;
        }
        #endregion

        #region Bundle Loading
        private IEnumerator LoadAllLocalBundles(Action onComplete)
        {
            var localBundleVersions = GetClientDataBundleVersion();
            var db = firebaseFirestore;
            int loadedCount = 0;
            int totalCount = localBundleVersions.Count(kv => kv.Value > 0);
            gameAPIDataBundleLoadProgressDelegate?.Invoke(loadedCount, totalCount);

            // 모든 로드를 병렬로 시작
            var loadTasks = new List<Task>();

            foreach (var kv in localBundleVersions)
            {
                if (kv.Value > 0)
                {
                    string localFilePath = GetLocalBundleFilePath(kv.Key, kv.Value);
                    if (File.Exists(localFilePath))
                    {
                        var task = LoadLocalBundleFileAsync(localFilePath, kv.Key, db);
                        loadTasks.Add(task);
                    }
                    else
                    {
                        GameLogSystem.LogError($"[DataBundle] Local bundle file missing: {localFilePath}");
                    }
                }
            }

            // 병렬 로드 진행 상황 모니터링
            while (loadTasks.Count > 0)
            {
                // 완료된 태스크 찾기
                Task completedTask = null;
                foreach (var task in loadTasks)
                {
                    if (task.IsCompleted || task.IsFaulted || task.IsCanceled)
                    {
                        completedTask = task;
                        break;
                    }
                }

                if (completedTask != null)
                {
                    loadTasks.Remove(completedTask);
                    loadedCount++;
                    gameAPIDataBundleLoadProgressDelegate?.Invoke(loadedCount, totalCount);
                }

                yield return null;
            }

            Debug.Log($"[DataBundle] Loaded {loadedCount} local bundles");
            onComplete?.Invoke();
        }

        private async Task LoadLocalBundleFileAsync(string filePath, EDataBundleCategory category, FirebaseFirestore db)
        {
            byte[] bundleData = null;

            try
            {
                // 파일 존재 및 크기 확인
                if (!File.Exists(filePath))
                {
                    GameLogSystem.LogError($"[DataBundle] Bundle file not found: {filePath}");
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MAX_BUNDLE_SIZE_BYTES)
                {
                    GameLogSystem.LogError($"[DataBundle] Bundle file too large: {fileInfo.Length} bytes");
                    return;
                }

                bundleData = File.ReadAllBytes(filePath);

                if (bundleData == null || bundleData.Length == 0)
                {
                    GameLogSystem.LogError($"[DataBundle] Bundle file is empty or corrupted: {filePath}");
                    return;
                }

                // Firebase에 번들 로드
                await db.LoadBundleAsync(bundleData);
            }
            catch (Exception ex)
            {
                GameLogSystem.LogError($"[DataBundle] Failed to load local bundle {category}: {ex.Message}");
            }
            finally
            {
                // 메모리 정리
                bundleData = null;
                System.GC.Collect();
            }
        }

        private IEnumerator LoadLocalBundleFile(string filePath, EDataBundleCategory category, FirebaseFirestore db)
        {
            byte[] bundleData = null;

            try
            {
                // 안드로이드에서 메모리 효율적인 파일 읽기
                if (!File.Exists(filePath))
                {
                    GameLogSystem.LogError($"[DataBundle] Bundle file not found: {filePath}");
                    yield break;
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MAX_BUNDLE_SIZE_BYTES)
                {
                    GameLogSystem.LogError($"[DataBundle] Bundle file too large: {fileInfo.Length} bytes");
                    yield break;
                }

                bundleData = File.ReadAllBytes(filePath);

                // 안드로이드에서 메모리 확인
                if (bundleData == null || bundleData.Length == 0)
                {
                    GameLogSystem.LogError($"[DataBundle] Bundle file is empty or corrupted: {filePath}");
                    yield break;
                }
            }
            catch (Exception ex)
            {
                GameLogSystem.LogError($"[DataBundle] Error reading local bundle file {filePath}: {ex.Message}");
                yield break;
            }

            var loadTask = db.LoadBundleAsync(bundleData);
            yield return new WaitUntil(() => loadTask.IsCompleted || loadTask.IsFaulted);

            if (loadTask.IsFaulted)
            {
                GameLogSystem.LogError($"[DataBundle] Failed to load local bundle {category}: {loadTask.Exception?.GetBaseException()?.Message}");
            }
            else
            {
            }

            // 안드로이드에서 메모리 정리
            bundleData = null;
            System.GC.Collect();
        }
        #endregion

        #region API Response Handling
        public void ResponseAPI(FRequestHttpBase InResponseCall, JSONObject InResponseData)
        {
            switch (InResponseCall.requestHttpsCallType)
            {
                case ERequestHttpsCallType.DataBundle_CheckVersion:
                    HandleVersionCheckResponse(InResponseData);
                    break;
                default:
                    GameLogSystem.LogError($"Unhandled ResponseAPI type: {InResponseCall.requestHttpsCallType}");
                    break;
            }
        }

        private void HandleVersionCheckResponse(JSONObject responseData)
        {
            //GameUIManager.Instance.SetLoadingOn(true);

            if (!IsVersionUpdateRequired(responseData))
            {
                LoadExistingBundlesAndComplete(responseData);
                return;
            }

            if (responseData.HasKey(FCommonDefine.ConstResponse))
            {
                ProcessVersionUpdate(responseData);
            }
            else
            {
                LoadExistingBundlesAndComplete(responseData);
            }
        }

        private bool IsVersionUpdateRequired(JSONObject responseData)
        {
            return responseData.HasKey(FCommonDefine.ConstSuccess) && responseData[FCommonDefine.ConstSuccess].AsBool;
        }

        private void ProcessVersionUpdate(JSONObject responseData)
        {
            JSONNode deltaNode = JSON.Parse(responseData[FCommonDefine.ConstResponse]);

            if (deltaNode.Count > 0)
            {
                StartBundleUpdateProcess(responseData, deltaNode);
            }
            else
            {
                LoadExistingBundlesAndComplete(responseData);
            }
        }

        private void StartBundleUpdateProcess(JSONObject responseData, JSONNode deltaNode)
        {
            GameMonoCoroutineManager.Instance.StartCoroutine_IEnumerator(
                "SyncBundlesAndUpdate",
                SyncBundlesAndUpdate(responseData[FCommonDefine.ConstResponse], (success) =>
                {
                    if (success)
                    {
                        LoadAllBundlesAndComplete(responseData);
                    }
                    else
                    {
                        GameLogSystem.LogError("[DataBundle] Bundle update failed, skipping further processing");
                        // 번들 업데이트 실패 시에도 로딩 화면 해제
                        //GameUIManager.Instance.SetLoadingOn(false);
                        gameAPIDataBundleCompleteDelegate?.Invoke();
                    }
                }));
        }

        private void LoadExistingBundlesAndComplete(JSONObject responseData)
        {
            GameMonoCoroutineManager.Instance.StartCoroutine_IEnumerator(
                "LoadExistingLocalBundles",
                LoadAllLocalBundles(() => CompleteInitialization(responseData)));
        }

        private void LoadAllBundlesAndComplete(JSONObject responseData)
        {
            GameMonoCoroutineManager.Instance.StartCoroutine_IEnumerator(
                "LoadAllLocalBundlesAfterDownload",
                LoadAllLocalBundlesWithDelay(responseData));
        }

        private IEnumerator LoadAllLocalBundlesWithDelay(JSONObject responseData)
        {
            // 다운로드 Task들이 완전히 정리될 시간을 줌
            yield return new WaitForSeconds(0.5f);
            System.GC.Collect();
            yield return LoadAllLocalBundles(() => CompleteInitialization(responseData));
        }

        private void CompleteInitialization(JSONObject responseData)
        {
            GameDBClientManager.Instance.StartUpdateBundleGameDB(async () =>
            {
                SetupCrypto(responseData);
                GameDBClientManager.Instance.GameDB_StringKey = await GameDBStringKeyManager.LoadGameDBStringKey();
                GameUtility.RunAllTests();

                // 데이터 번들 처리 완료 - 로딩 화면 해제
                //GameUIManager.Instance.SetLoadingOn(false);
                gameAPIDataBundleCompleteDelegate?.Invoke();
            });
        }

        private void SetupCrypto(JSONObject responseData)
        {
            string aesKey = responseData[FCommonDefine.CONST_AESKEY];
            string aesIV = responseData[FCommonDefine.CONST_AESIV];
            if (string.IsNullOrEmpty(aesKey) || string.IsNullOrEmpty(aesIV))
            {
                GameLogSystem.LogError("[DataBundle] AES key or IV is null or empty");
                return;
            }
            GameCryptoManager.Instance.SetKeyAndIV(aesKey, aesIV);
        }
        #endregion
    }
}