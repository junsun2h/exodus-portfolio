using Firebase;
using Firebase.Extensions;
using Firebase.Functions;
using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PX
{
    #region Delegates
    public delegate void UpdateGoodsDelegate();
    public delegate void UpdateCharacterDelegate(FCharacterSingleData targetData);
    public delegate void UpdatePlayerDelegate(int resultLevel, long resultExp, int addLevel, long addExp, bool IsLevelUp);
    public delegate void UpdateEquipDelegate();
    #endregion

    #region Enums
    public enum EChatChannel
    {
        None = 0,
        Channel1,
        Channel2,
        Channel3,
        Channel4,
        Channel5,
    }

    public enum ERequestHttpsCallType
    {
        None = 0,

        // ============================================================
        // 시스템/서버 관련
        // ============================================================
        DataBundle_Download,        // 데이터 번들 다운로드 & Firestore 캐싱 갱신
        DataBundle_CheckVersion,    // 데이터 번들 버전 체크
        ServerVersionCheck,         // 서버 버전 체크

        // ============================================================
        // 계정 관련
        // ============================================================
        Account_Login,              // 로그인
        Account_UpdateFCMToken,     // FCM 토큰 서버 등록
        Account_Login_FullTest,     // 모든 컨텐츠가 있는 테스트 계정(FullTest) 생성/로그인
        Account_Delete,             // 계정 삭제
        Account_NickName,           // 닉네임 변경

        // ============================================================
        // 플레이어 관련
        // ============================================================
        Player_Growth_LevelUp,      // 성장 레벨업
        Player_Growth_Unlock,       // 성장 잠금 해제
        Player_Reinforce_LevelUp,   // 강화 레벨업
        Player_Reinforce_Unlock,    // 강화 잠금 해제
        Player_Reinforce_Reset,     // 강화 리셋 (API 아직 미적용, UI작업과 연동될 예정)

        // ============================================================
        // 각성 관련
        // ============================================================
        Awaken_Promotion,           // 각성 승급
        Awaken_Element_LevelUp,     // 각성 원소 레벨업

        // ============================================================
        // 스테이지 관련
        // ============================================================
        Stage_Enter,                // 스테이지 입장
        Stage_EndLoop,              // 스테이지 루프 종료
        Stage_Clear,                // 스테이지 클리어
        Stage_Fail,                 // 스테이지 실패
        Stage_QuickClear,           // 스테이지 빠른 클리어 (API 아직 미적용, UI작업과 연동될 예정)
        VerifyStageRewardSync,      // 스테이지 보상 무결성 검증 API

        // ============================================================
        // 장비 관련
        // ============================================================
        Equipment_Gacha,            // 장비 가챠
        Equipment_Reforging,        // 장비 재련
        Equipment_AutoReforging,    // 장비 옵션 자동 변경
        Equipment_NormalMod_Lock,   // 장비 옵션 잠금/해제
        Equipment_Reinforce,        // 장비 강화
        Equipment_Promotion,        // 장비 승급
        Equipment_Promotion_All,    // 장비 전체 승급
        Equipment_Equip,            // 장비 장착
        Equipment_UnEquip,          // 장비 해제

        // ============================================================
        // 스킬 관련
        // ============================================================
        Skill_Gacha,                // 스킬 가챠
        Skill_Create,               // 스킬 생성 (개발용)
        Skill_Reinforce,            // 스킬 강화
        Skill_Promotion,            // 스킬 승급
        Skill_Promotion_All,        // 스킬 전체 승급
        Skill_Equip,                // 스킬 장착
        Skill_UnEquip,              // 스킬 해제
        Skill_Rune_Equip,           // 스킬 룬 장착
        Skill_Rune_UnEquip,         // 스킬 룬 해제

        // ============================================================
        // 펫 관련
        // ============================================================
        Pet_Gacha,                  // 펫 가챠
        Pet_Create,                 // 펫 생성
        Pet_Promotion,              // 펫 승급
        Pet_Promotion_All,          // 펫 전체 승급
        Pet_Equip,                  // 펫 장착

        // ============================================================
        // 성좌 관련
        // ============================================================
        Constellation_Gacha,        // 성좌 가챠
        Constellation_LevelUp,      // 성좌 강화 (레벨업)
        Constellation_Equip,        // 성좌 슬롯 장착
        Constellation_Unequip,      // 성좌 슬롯 해제
        Constellation_Craft,        // 성좌 파편으로 카드 교환

        // ============================================================
        // 성운 관련
        // ============================================================
        Nebula_LevelUp,             // 성운 레벨업
        Nebula_AdEnable,            // 성운 광고 활성화

        // ============================================================
        // 상품 구매 관련 (일반, 패키지, 구독, 패스)
        // ============================================================
        Product_Buy,                        // 상품 구매
        ProductPackage_Buy,                 // 패키지 상품 구매
        ProductSubscription_Buy,            // 구독 상품 구매
        ProductPass_Buy,                    // 패스 상품 구매
        ProductPass_Receive_PaidItem,       // 패스 유료 아이템 수령
        ProductPass_ReceiveAll_FreeItem,    // 패스 무료 아이템 전체 수령
        ProductPass_ReceiveAll,             // 패스 전체 수령
        ProductPass_Reset,                  // 패스 리셋 (출석 패스 리셋)

        // ============================================================
        // 가챠 관련
        // ============================================================
        Equipment_Gacha_Reinforce,  // 장비 강화권 뽑기
        Equipment_Gacha_Reforging,  // 장비 재련권 뽑기
        Skill_Gacha_Reinforce,      // 스킬 강화석 뽑기
        Gacha_GetLevelReward,       // 뽑기 레벨 달성 보상 획득
        Gacha_RedSoulstone,         // 붉은 영혼석 가챠

        // ============================================================
        // 제작 관련
        // ============================================================
        Craft_Merge,                // 재화 합성
        Craft_Disassemble,          // 재화 분해

        // ============================================================
        // 프리셋 관련
        // ============================================================
        Preset_Unlock,              // 프리셋 잠금 해제
        Preset_ChangeName,          // 프리셋 이름 변경
        Preset_Load,                // 프리셋 로드
        Preset_Save,                // 프리셋 저장
        Preset_SetQuickBar,         // 프리셋 퀵바 설정
        Preset_SetChallengeMap,     // 도전 스테이지별 프리셋 매핑 저장

        // ============================================================
        // 퀘스트 관련
        // ============================================================
        QuestMain_Complete,         // 메인 퀘스트 완료
        QuestDaily_Complete,        // 일일 퀘스트 완료
        QuestWeekly_Complete,       // 주간 퀘스트 완료

        // ============================================================
        // 우편함 관련
        // ============================================================
        Mailbox_SyncAndCleanServerMailbox,          // 서버 우편함을 유저 우편함과 동기화
        Mailbox_CheckAndDeliverSubscriptionProduct, // 구독 상품을 체크하고 조건에 부합하면 우편함으로 재화를 전송한다.
        Mailbox_Receive,                            // 우편 아이템 선택한 하나 받기
        Mailbox_ReceiveAll,                         // 우편 아이템 전체 받기

        // ============================================================
        // 랭크 관련
        // ============================================================
        InsertFakeData_All,         // 모든 랭크 리더보드에 테스트 데이터 추가
        ClearRankData_All,          // 모든 랭크 리더보드 테스트 데이터 삭제

        // ============================================================
        // 추천 장착 관련
        // ============================================================
        Equipment_Equip_Recommend,  // 장비 추천 장착
        Skill_Equip_Recommend,      // 스킬(주문, 오라) 추천 장착
        Skill_Rune_Equip_Recommend, // 스킬 룬 추천 장착
        Pet_Equip_Recommend,        // 펫 추천 장착

        // ============================================================
        // 보상 관련
        // ============================================================
        OfflineReward_Claim,        // 오프라인 보상 획득

        // ============================================================
        // 칭호 관련
        // ============================================================
        Title_Obtain,               // 칭호 획득 (업적 달성 → Title_Completes 기록)
        Title_Set,                  // 칭호 장착 (CurrentTitle 변경, 사전 획득 필수)

        // ============================================================
        // 티켓 관련
        // ============================================================
        Ticket_UseGacha,            // 가챠 티켓 사용
        Ticket_UsePick,             // 선택권 사용
        Ticket_UseFix,              // 확정권 사용

        // ============================================================
        // 개발/테스트/관리 관련
        // ============================================================
        //#if UNITY_EDITOR //번들빌드시 아래 타입사용중인 로직상 관련코드 UNITY_EDITOR로 감싸지 않으면 에러발생함.
        Currency_Set,               // 개별 재화 수량 설정 (개발용)
        UpdateDailyUserData,        // 매일 서버 업데이트 해야 하는 함수들 호출(출석, 구독, 기타)
        Test,                       // 테스트용
        //#endif
    }

    public enum EResponseCode
    {
        None = 0,
        Succeess = 1,
        Fail = 2,
    }

    public enum ERequestHttpType
    {
        None = 0,
        HttpCall,
        HttpData,
    }

    public enum ERequestHttpDataType
    {
        None = 0,
        Const = 1,
        User = 2,
        Instance = 3,
        GameControl = 4,
        SystemMessage = 5,
    }
    #endregion

    public class FParseData : IPXDisposable
    {
        public string MainKey { get; protected set; }
        public FDisposableJson parseData { get; protected set; }
        public JSONNode nodeData { get; protected set; }

        public FParseData(FDisposableJson InData, JSONNode InNode, string InMainKey)
        {
            parseData = InData;
            nodeData = InNode;
            MainKey = InMainKey;
        }

        protected override void ManagedDispose()
        {
            base.ManagedDispose();
            parseData = null;
            nodeData = null;
        }
    }

    #region FRequestHttpBase
    public class FRequestHttpBase : IDisposable
    {
        private const string REQUEST_PREFIX = "Request_";

        public ERequestHttpType eRequestHttpType { get; protected set; }
        public string requestUID { get; protected set; }
        public ERequestHttpsCallType requestHttpsCallType { get; protected set; }
        public string RequestFuncName { get; protected set; }
        public Dictionary<string, object> requestData { get; protected set; }
        public Action<JSONObject> completeAction { get; protected set; }
        public string responseError { get; protected set; }
        public bool isVisible { get; protected set; }
        public int retryCount { get; set; } = 0;
        public float requestSentTime { get; set; } = 0f; // 라운드트립 측정용

        private bool disposedValue;

        public FRequestHttpBase(ERequestHttpsCallType InType, Dictionary<string, object> InRequestData, Action<JSONObject> InCompleteAction, bool InVisible = true)
        {
            requestUID = CreateFormatUID(InType);
            requestHttpsCallType = InType;
            requestData = InRequestData ?? new Dictionary<string, object>();
            completeAction = InCompleteAction;
            responseError = "";
            isVisible = InVisible;
            retryCount = 0;

            SetupRequestData();
            SetupRequestFuncName(InType);
        }

        // 재시도용 생성자 - UID 유지
        protected FRequestHttpBase(FRequestHttpBase original)
        {
            requestUID = original.requestUID; // UID 유지!
            requestHttpsCallType = original.requestHttpsCallType;
            RequestFuncName = original.RequestFuncName;
            requestData = new Dictionary<string, object>(original.requestData);
            completeAction = original.completeAction;
            responseError = original.responseError;
            isVisible = original.isVisible;
            retryCount = original.retryCount;
            eRequestHttpType = original.eRequestHttpType;

            // requestData의 PUID도 동일하게 유지
            requestData[FCommonDefine.ConstPUID] = requestUID;
        }

        #region SetupRequestData
        private void SetupRequestData()
        {
            requestData[FCommonDefine.ConstPUID] = requestUID;

#if UNITY_EDITOR
            // 로컬 에뮬레이터 접속일 때만 서버 디버그 모드를 요청한다.
            // 실서버는 난수 로그 파일을 쓸 수 없어 API 자체가 실패한다.
            if (TemplateLocalData.IsEditorDebugMode)
            {
                requestData["IsEditorDebug"] = true;
            }
#endif
            // SessionKey는 큐에 넣을 때 설정
            // FunctionCallKey는 실제 요청 직전에 설정 (매 요청마다 새로운 키가 필요)
            var userData = GameAPIUserManager.Instance?.userData;
            if (userData != null && userData.accountData.CoreData != null)
            {
                var accountCoreData = userData.accountData.CoreData;
                if (accountCoreData != null)
                {
                    if (!string.IsNullOrEmpty(accountCoreData.HashInfo.SessionKey))
                    {
                        requestData[FCommonDefine.ConstSessionKey] = accountCoreData.HashInfo.SessionKey;
                    }

                    if (!string.IsNullOrEmpty(accountCoreData.LoginInfo.ServerID))
                    {
                        requestData[FCommonDefine.ConstServerID] = accountCoreData.LoginInfo.ServerID;
                    }
                }
            }
        }
        #endregion

        private void SetupRequestFuncName(ERequestHttpsCallType InType)
        {
            if (InType != ERequestHttpsCallType.None)
            {
                RequestFuncName = $"{REQUEST_PREFIX}{requestHttpsCallType}";
            }
        }

        private string CreateFormatUID(ERequestHttpsCallType InType)
        {
            // Environment.TickCount (시스템 시작 후 경과 밀리초) + ThreadId 조합으로 유일성 보장
            int tickCount = Environment.TickCount;
            int threadId = Thread.CurrentThread.ManagedThreadId;

            return $"{InType.ToString()}_{tickCount}_{threadId}";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;

                if (disposing)
                {
                    completeAction = null;
                    requestData?.Clear();
                }
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class FRequestHttpsCall : FRequestHttpBase
    {
        public FRequestHttpsCall(ERequestHttpsCallType InType, Dictionary<string, object> InRequestData, Action<JSONObject> InCompleteAction, bool InVisible = true)
            : base(InType, InRequestData, InCompleteAction, InVisible)
        {
            eRequestHttpType = ERequestHttpType.HttpCall;
        }

        // 재시도용 생성자 - UID 유지
        public FRequestHttpsCall(FRequestHttpsCall InRequest, string InError)
            : base(InRequest) // 부모의 재시도용 생성자 호출
        {
            responseError = InError;
            eRequestHttpType = ERequestHttpType.HttpCall;
        }
    }



    // 재시도 관리를 위한 클래스
    public class FRequestTimeoutInfo
    {
        public string requestUID;
        public FRequestHttpBase request;
        public Coroutine timeoutCoroutine;
        public float startTime;

        public FRequestTimeoutInfo(string InUID, FRequestHttpBase InRequest, Coroutine InCoroutine)
        {
            requestUID = InUID;
            request = InRequest;
            timeoutCoroutine = InCoroutine;
            startTime = Time.time;
        }
    }
    #endregion

    #region GameNetworkManager
    public class GameNetworkManager : SingletonMono<GameNetworkManager>
    {
        private const float REQUEST_TIMEOUT_SECONDS = 60.0f;
        private const int MAX_RETRY_COUNT = 2;

        private List<FRequestHttpBase> requestHttpList = new List<FRequestHttpBase>();
        private Queue<FRequestHttpBase> requestHttpQueue = new Queue<FRequestHttpBase>();
        private Dictionary<string, FRequestTimeoutInfo> requestTimeoutMap = new Dictionary<string, FRequestTimeoutInfo>();
        private bool isProcessingRequest = false;
        private bool isDestroyed = false; // 파괴 상태 추적

        private FirebaseFunctions firebaseFunctions => FBMainManager.Instance.firebaseFunctions;
        public bool IsRequestRunning => requestHttpList.Count > 0 || requestHttpQueue.Count > 0;
        public bool IsProcessingRequest => isProcessingRequest;
        public int PendingRequestCount => requestHttpQueue.Count;

        private GameNetworkManager()
        {
        }

        ~GameNetworkManager()
        {
        }

        #region Override Methods
        protected override void Awake()
        {
            base.Awake();

            FBMainManager.Instance.onFirebaseAbleCheckEvent += OnFirebaseAbleCheckEvent;
        }

        protected override void OnDestroy()
        {
            isDestroyed = true;
            ClearAllData();
            base.OnDestroy();
        }

        public void ClearAllData()
        {
            // 모든 타임아웃 코루틴 정리
            foreach (var timeoutInfo in requestTimeoutMap.Values)
            {
                if (timeoutInfo.timeoutCoroutine != null)
                {
                    StopCoroutine(timeoutInfo.timeoutCoroutine);
                }
            }
            requestTimeoutMap.Clear();

            // 모든 대기 중인 요청 정리
            while (requestHttpQueue.Count > 0)
            {
                var request = requestHttpQueue.Dequeue();
                request?.Dispose();
            }

            // 진행 중인 요청들 정리
            foreach (var request in requestHttpList)
            {
                request?.Dispose();
            }
            requestHttpList.Clear();

            isProcessingRequest = false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 에디터 전용: 플레이모드 종료/도메인 리로드 직전에 Firebase 응답 대기를 즉시 포기한다.
        /// 이후 돌아오는 ContinueWithOnMainThread 콜백은 isDestroyed 가드로 무시된다.
        /// </summary>
        public void ForceAbortAllRequests()
        {
            // 콜백이 돌아와도 무시되도록 먼저 플래그 세팅
            isDestroyed = true;

            try
            {
                foreach (var timeoutInfo in requestTimeoutMap.Values)
                {
                    if (timeoutInfo.timeoutCoroutine != null)
                        StopCoroutine(timeoutInfo.timeoutCoroutine);
                }
                requestTimeoutMap.Clear();

                while (requestHttpQueue.Count > 0)
                {
                    var request = requestHttpQueue.Dequeue();
                    request?.Dispose();
                }

                foreach (var request in requestHttpList)
                    request?.Dispose();
                requestHttpList.Clear();

                isProcessingRequest = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"ForceAbortAllRequests failed: {ex}");
            }
        }
#endif
        #endregion

        public bool IsNetworkRunning(ERequestHttpsCallType InType)
        {
            if (InType == ERequestHttpsCallType.None)
                return false;

            // 진행 중인 요청과 대기 중인 요청 모두 확인
            bool isInProgress = requestHttpList.Find(t => t.requestHttpsCallType == InType) != null;
            bool isInQueue = false;

            foreach (var request in requestHttpQueue)
            {
                if (request.requestHttpsCallType == InType)
                {
                    isInQueue = true;
                    break;
                }
            }

            return isInProgress || isInQueue;
        }

        public bool SendRequestHttpCall(FRequestHttpBase InRequest)
        {
            if (isDestroyed || !ValidateRequest(InRequest))
                return false;

            // 요청을 큐에 추가
            requestHttpQueue.Enqueue(InRequest);

            if (InRequest.isVisible)
            {
                GameUIManager.Instance.SetLoadingOn(true);
            }

            // 현재 처리 중인 요청이 없으면 즉시 처리 시작
            if (!isProcessingRequest)
            {
                ProcessNextRequest();
            }

            return true;
        }

        #region Private Methods

        private void OnFirebaseAbleCheckEvent(DependencyStatus InEvent)
        {
            //Debug.Log($"GameNetworkManager::OnFirebaseAbleCheckEvent, InEvent = {InEvent}");

            switch (InEvent)
            {
                case DependencyStatus.Available:
                    // Firebase 사용 가능
                    break;
                default:
                    // Firebase 사용 불가능
                    break;
            }
        }

        private bool ValidateRequest(FRequestHttpBase InRequest)
        {
            if (InRequest == null)
            {
                //Debug.LogError("RequestHttp, InRequest is null");
                return false;
            }

            if (InRequest.requestHttpsCallType == ERequestHttpsCallType.None)
            {
                //Debug.LogError("RequestHttp, requestHttpsCallType Not Valid");
                return false;
            }

            if (string.IsNullOrEmpty(InRequest.RequestFuncName))
            {
                //Debug.LogError($"RequestHttp, RequestFuncName Empty, requestHttpsCallType = {InRequest.requestHttpsCallType}");
                return false;
            }

            return true;
        }

        private void ProcessNextRequest()
        {
            if (requestHttpQueue.Count == 0 || isDestroyed)
            {
                isProcessingRequest = false;
                return;
            }

            if (isProcessingRequest)
                return;

            isProcessingRequest = true;
            FRequestHttpBase nextRequest = requestHttpQueue.Dequeue();

            PrepareRequest(nextRequest);
            ExecuteRequest(nextRequest);
        }

        private void OnRequestCompleted()
        {
            if (isDestroyed) return;

            isProcessingRequest = false;

            // 다음 요청이 있으면 처리
            if (requestHttpQueue.Count > 0)
            {
#if UNITY_EDITOR
                //Debug.Log($"Processing next request, Queue count: {requestHttpQueue.Count}");
#endif
                ProcessNextRequest();
            }
        }

        private void PrepareRequest(FRequestHttpBase InRequest)
        {
            requestHttpList.Add(InRequest);

#if UNITY_EDITOR
            //Debug.Log($"PrepareRequest, InRequest.RequestFuncName = {InRequest.RequestFuncName}");
#endif

            GameAPIManager.Instance.requestHttpsDelegate?.Invoke(InRequest);
        }

        private void ExecuteRequest(FRequestHttpBase InRequest)
        {
            // 실제 요청 직전에 최신 FunctionCallKey 설정
            UpdateFunctionCallKey(InRequest);

            // 타임아웃 코루틴 시작
            StartTimeoutCoroutine(InRequest);

            // 라운드트립 측정용 시간 기록
            InRequest.requestSentTime = Time.realtimeSinceStartup;

            var requestTask = firebaseFunctions.GetHttpsCallable(InRequest.RequestFuncName).CallAsync(InRequest.requestData);
            requestTask.ContinueWithOnMainThread(HandleRequestResponse);
        }

        private void UpdateFunctionCallKey(FRequestHttpBase InRequest)
        {
            var userData = GameAPIUserManager.Instance?.userData;
            if (userData?.accountData?.CoreData != null)
            {
                var accountCoreData = userData.accountData.CoreData;
                if (!string.IsNullOrEmpty(accountCoreData.HashInfo.FunctionCallKey))
                {
                    InRequest.requestData[FCommonDefine.ConstFunctionCallKey] = accountCoreData.HashInfo.FunctionCallKey;
                }
            }
        }

        private void StartTimeoutCoroutine(FRequestHttpBase InRequest)
        {
            Coroutine timeoutCoroutine = StartCoroutine(RequestTimeoutCoroutine(InRequest.requestUID));
            FRequestTimeoutInfo timeoutInfo = new FRequestTimeoutInfo(InRequest.requestUID, InRequest, timeoutCoroutine);
            requestTimeoutMap[InRequest.requestUID] = timeoutInfo;
        }

        private IEnumerator RequestTimeoutCoroutine(string requestUID)
        {
            yield return new WaitForSeconds(REQUEST_TIMEOUT_SECONDS);

            // 타임아웃 발생 - 파괴되지 않은 경우에만 처리
            if (!isDestroyed && requestTimeoutMap.TryGetValue(requestUID, out FRequestTimeoutInfo timeoutInfo))
            {
                //Debug.LogWarning($"Request timeout occurred: {timeoutInfo.request.RequestFuncName}, UID: {requestUID}");

                // 타임아웃 정보 제거
                requestTimeoutMap.Remove(requestUID);

                // 재시도 처리
                HandleRequestTimeout(timeoutInfo.request);
            }
        }

        private void HandleRequestTimeout(FRequestHttpBase timedOutRequest)
        {
            // 이미 파괴된 경우 처리하지 않음
            if (isDestroyed) return;

#if UNITY_EDITOR
            //Debug.LogWarning($"HandleRequestTimeout: {timedOutRequest.RequestFuncName}, UID: {timedOutRequest.requestUID}");
#endif

            // 진행 중인 요청 목록에서 제거 (타임아웃 정리는 이미 RequestTimeoutCoroutine에서 처리됨)
            int targetIndex = requestHttpList.FindIndex(t => t.requestUID.CompareTo(timedOutRequest.requestUID) == 0);
            if (targetIndex >= 0)
            {
                requestHttpList.RemoveAt(targetIndex);
#if UNITY_EDITOR
                //Debug.Log($"HandleRequestTimeout: Removed timed out request from list. Remaining requests: {requestHttpList.Count}");
#endif
            }

            // 재시도 가능한지 확인
            if (timedOutRequest.retryCount < MAX_RETRY_COUNT)
            {
                // 재시도 횟수 증가
                timedOutRequest.retryCount++;

#if UNITY_EDITOR
                //Debug.Log($"HandleRequestTimeout: Retrying request: {timedOutRequest.RequestFuncName}, Retry count: {timedOutRequest.retryCount}/{MAX_RETRY_COUNT}");
#endif

                // 재시도 요청을 큐에 추가 (UID 유지됨)
                SendRequestHttpCall(timedOutRequest);
            }
            else
            {
                // 최대 재시도 횟수 초과
                //Debug.LogError($"HandleRequestTimeout: Request failed after {MAX_RETRY_COUNT} retries: {timedOutRequest.RequestFuncName}, UID: {timedOutRequest.requestUID}");

                // 로딩 상태 해제
                if (timedOutRequest.isVisible)
                {
                    GameUIManager.Instance.SetLoadingOn(false);
                }

                // 에러 콜백 실행 (null 응답으로)
                timedOutRequest.completeAction?.Invoke(null);

                // 리소스 정리
                timedOutRequest.Dispose();

                // 다음 요청 처리
                //OnRequestCompleted();

                //통신 종료 후 팝업 출력 및 로그인 모드로 이동
                ClearAllData();

                var errorPopup = GameUIManager.Instance.OpenWidget("PXPopup_NetworkError") as PXPopup_NetworkError;
                errorPopup.SetErrorInfo("Request timeout occurred: " + timedOutRequest.RequestFuncName);
            }
        }

        private void HandleRequestResponse(System.Threading.Tasks.Task<HttpsCallableResult> InResponse)
        {
            // 이미 파괴된 경우 처리하지 않음
            if (isDestroyed) return;

            string errorInfo = "";
            string responseUID = "";
            JSONObject resultJson = null;
            bool isClearError = false;
            bool isOverlapResponse = false;

            if (InResponse.IsFaulted)
            {
                GameUIManager.Instance.SetLoadingOn(false);

                errorInfo = HandleRequestError(InResponse);
                // 에러 발생 시 진행 중인 모든 요청에서 타임아웃 정리 시도
                CleanupTimeoutForErrorResponse();

                //네트워크 에러 발생 시 모든 네트워크 동작 정리 및 초기화
                isClearError = true;
            }
            else
            {
                (resultJson, errorInfo, responseUID) = ProcessSuccessResponse(InResponse);
                // 응답이 성공적으로 처리된 경우 타임아웃 코루틴 정리
                CleanupTimeoutByUID(responseUID);

                isOverlapResponse = string.IsNullOrEmpty(responseUID);

                if (errorInfo.Length > 0)
                {
                    isClearError = true;
                }
            }

            // 중복 응답 처리 개선
            if (responseUID == "IGNORE_RESPONSE")
            {
                // 중복 응답은 무시하고 다음 요청 처리
#if UNITY_EDITOR
                //Debug.LogWarning("HandleRequestResponse: Ignoring duplicate/late response. Continuing with next request.");
#endif
                OnRequestCompleted();
                return;
            }

            if (isClearError)
            {
                //모든 네트워크 동작 정리 및 초기화
                ClearAllData();


                //임시 에러 처리
                if (true)
                {
                    GameUIManager.Instance.ShowToastMessage(EToastMessageType.Error, "임시 에러 알림 Request timeout occurred: " + errorInfo);
                    return;

                }
                else
                {
                    //이동 팝업 출력 및 로그인 모드로 이동
                    var errorPopup = GameUIManager.Instance.OpenWidget("PXPopup_NetworkError") as PXPopup_NetworkError;
                    errorPopup.SetErrorInfo(errorInfo);
                }


            }
            else if (isOverlapResponse)
            {
                // 기존 overlap 로직 - PUID가 없는 경우
#if UNITY_EDITOR
                //Debug.LogWarning("HandleRequestResponse: Response missing PUID, treating as error");
#endif
                ClearAllData();
                var errorPopup = GameUIManager.Instance.OpenWidget("PXPopup_NetworkError") as PXPopup_NetworkError;
                errorPopup.SetErrorInfo("Response missing unique identifier");
            }
            else
            {
                FinalizeRequest(resultJson, errorInfo, responseUID);
            }
        }

        private string HandleRequestError(System.Threading.Tasks.Task<HttpsCallableResult> InResponse)
        {
            string errorInfo = "";
            foreach (Exception inner in InResponse.Exception.InnerExceptions)
            {
                if (inner is FunctionsException e)
                {
                    errorInfo = $"error code = {e.ErrorCode}, message = {e.Message}";
                    //Debug.LogError(errorInfo);
                }
            }
            return errorInfo;
        }

        #region ProcessSuccessResponse
        private (JSONObject resultJson, string errorInfo, string responseUID) ProcessSuccessResponse(System.Threading.Tasks.Task<HttpsCallableResult> InResponse)
        {
            string errorInfo = "";
            string responseUID = "";
            JSONObject resultJson = null;

            if (InResponse.Result.Data != null)
            {
                try
                {
                    string strResult = InResponse.Result.Data.ToString();
                    resultJson = (JSONObject)JSONNode.Parse(strResult);

                    if (resultJson.HasKey(FCommonDefine.ConstError))
                    {
                        errorInfo = resultJson[FCommonDefine.ConstError];
                    }

                    if (resultJson.HasKey(FCommonDefine.ConstPUID))
                    {
                        responseUID = resultJson[FCommonDefine.ConstPUID];

                        FRequestHttpBase request = GetRequestHttpByUID(responseUID);
                        if (request == null)
                        {
                            // 이미 정리된 요청의 응답은 무시 (타임아웃 재시도 등으로 인한 중복 응답)
#if UNITY_EDITOR
                            //Debug.LogWarning($"ProcessSuccessResponse: Received response for already cleaned request: {responseUID}. Ignoring response.");
#endif
                            // null을 반환하여 응답을 무시하도록 함
                            return (null, "", "IGNORE_RESPONSE");
                        }
                    }
                    else
                    {
                        //Debug.LogError($"ProcessSuccessResponse, resultJson.HasKey(FCommonDefine.ConstPUID) = false");
                        errorInfo = "Response missing PUID";
                    }

                    // API 성공 여부 체크 (로깅용) - 유효한 요청인 경우에만
                    if (!string.IsNullOrEmpty(responseUID))
                    {
                        CheckAPISuccess(resultJson, responseUID);
                    }
                }
                catch (Exception e)
                {
#if UNITY_EDITOR
                    //Debug.LogError($"SendRequestHttpCall, Response parsing error = {e}");
#endif
                    errorInfo = $"Response parsing error: {e.Message}";
                }
            }
            else
            {
#if UNITY_EDITOR
                //Debug.LogError("SendRequestHttpCall, Response Data = null");
#endif
                errorInfo = "Response Data is null";
            }

            return (resultJson, errorInfo, responseUID);
        }
        #endregion

        private void FinalizeRequest(JSONObject resultJson, string errorInfo, string responseUID)
        {
            if (resultJson != null)
            {
#if UNITY_EDITOR
                //Debug.Log("SendRequestHttpCall, Response received");
#endif
                if (string.IsNullOrEmpty(errorInfo))
                {
                    try
                    {
                        ResponseHttps(GetRequestHttpByUID(responseUID), resultJson);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"FinalizeRequest, ResponseHttps error = {e}");
                    }
                }
                RemoveRequestHttp(responseUID);
            }
            else
            {
                // 응답이 null인 경우 모든 진행 중인 요청 중 타임아웃되지 않은 것들을 정리
                if (string.IsNullOrEmpty(responseUID))
                {
                    // responseUID가 없는 경우 가장 오래된 요청을 찾아서 정리
                    CleanupOldestRequest();
                }
                else
                {
                    RemoveRequestHttp(responseUID);
                }

                if (!isDestroyed)
                {
                    GameUIManager.Instance.SetLoadingOn(false);
                }
            }

            // 요청 완료 처리 및 다음 요청 시작
            OnRequestCompleted();
        }

        private void RemoveRequestHttp(string InRequestUID)
        {
            if (string.IsNullOrEmpty(InRequestUID))
            {
#if UNITY_EDITOR
                //Debug.LogWarning("RemoveRequestHttp: InRequestUID is null or empty");
#endif
                return;
            }

            // 타임아웃 정리
            CleanupTimeoutByUID(InRequestUID);

            // 요청 리스트에서 제거
            int targetIndex = requestHttpList.FindIndex(t => t.requestUID.CompareTo(InRequestUID) == 0);
            if (targetIndex >= 0)
            {
                var removedRequest = requestHttpList[targetIndex];
#if UNITY_EDITOR
                //Debug.Log($"RemoveRequestHttp: Removing completed request: {removedRequest.RequestFuncName}, UID: {InRequestUID}");
#endif
                removedRequest.Dispose();
                requestHttpList.RemoveAt(targetIndex);
            }
            else
            {
#if UNITY_EDITOR
                //Debug.LogWarning($"RemoveRequestHttp: Request not found in list, UID: {InRequestUID}");
#endif
            }

#if UNITY_EDITOR
            //Debug.Log($"RemoveRequestHttp: Current remaining requests: {requestHttpList.Count}");
            if (requestHttpList.Count > 0)
            {
                //Debug.Log($"RemoveRequestHttp: Remaining request types: {string.Join(", ", requestHttpList.ConvertAll(r => r.RequestFuncName))}");
            }
#endif
        }

        private FRequestHttpBase GetRequestHttpByUID(string InRequestUID)
        {
            return requestHttpList.Find(t => t.requestUID.CompareTo(InRequestUID) == 0);
        }

        private void ResponseHttps(FRequestHttpBase InResponseCall, JSONObject InResponseData)
        {
            if (InResponseCall == null)
            {
                //Debug.LogError("InResponseCall Not Define");
                return;
            }

            if (InResponseCall.isVisible && !isDestroyed)
            {
                GameUIManager.Instance.SetLoadingOn(false);
            }

            GameAPIManager.Instance.ResponseAPI(InResponseCall, InResponseData);

            // 완료 콜백 실행
            InResponseCall.completeAction?.Invoke(InResponseData);
        }

        private bool CheckAPISuccess(JSONObject resultJson, string responseUID)
        {
            if (resultJson == null)
                return false;

            bool isSuccess = false;

            if (resultJson.HasKey(FCommonDefine.ConstSuccess))
            {
                isSuccess = resultJson[FCommonDefine.ConstSuccess].AsBool;
            }

#if UNITY_EDITOR
            // responseUID로 요청 정보 가져와서 로깅
            FRequestHttpBase request = GetRequestHttpByUID(responseUID);
            LogAPIResponse(request, resultJson, isSuccess);
#endif

            return isSuccess;
        }

        // 타임아웃 정리 헬퍼 메서드들
        private void CleanupTimeoutByUID(string requestUID)
        {
            if (string.IsNullOrEmpty(requestUID) || isDestroyed)
                return;

            if (requestTimeoutMap.TryGetValue(requestUID, out FRequestTimeoutInfo timeoutInfo))
            {
                if (timeoutInfo.timeoutCoroutine != null)
                {
                    StopCoroutine(timeoutInfo.timeoutCoroutine);
                }
                requestTimeoutMap.Remove(requestUID);
            }
        }

        private void CleanupTimeoutForErrorResponse()
        {
            // 에러 응답의 경우 가장 오래된 요청의 타임아웃을 정리
            if (requestHttpList.Count > 0 && requestTimeoutMap.Count > 0)
            {
                var oldestRequest = requestHttpList[0];
                CleanupTimeoutByUID(oldestRequest.requestUID);
            }
        }

        private void CleanupOldestRequest()
        {
            if (requestHttpList.Count > 0)
            {
                var oldestRequest = requestHttpList[0];
                RemoveRequestHttp(oldestRequest.requestUID);
            }
        }

#if UNITY_EDITOR
        private void LogAPIResponse(FRequestHttpBase InRequest, JSONObject resultJson, bool isSuccess)
        {
            //Debug.Log($"API Response: {resultJson}");

            //재시도를 요청했는데 그사이에 이전요청이 정상처리된 경우 클라는  해당PUID 정보가 없을수 있음. 이런 경우 아무행위를 하지 않음
            if (InRequest == null)
            {
                //Debug.Log("LogAPIResponse, InRequest is null");
                return;
            }

            if (resultJson.HasKey(FCommonDefine.ConstFuncRuntime))
            {
                int funcRuntime = resultJson[FCommonDefine.ConstFuncRuntime].AsInt;
                int roundTripMs = InRequest.requestSentTime > 0 ? (int)((Time.realtimeSinceStartup - InRequest.requestSentTime) * 1000f) : -1;
                int overhead = roundTripMs > 0 ? roundTripMs - funcRuntime : -1;
                Debug.Log($"[API_PERF] {InRequest?.RequestFuncName} | server:{funcRuntime}ms | roundTrip:{roundTripMs}ms | overhead:{overhead}ms");
            }

            if (!isSuccess)
            {
                GameUIManager.Instance.ShowToastMessage(EToastMessageType.Error, $"Network Not Define success, RequestFuncName = {InRequest?.RequestFuncName}");
                Debug.LogError($"[Network Error] success 필드 없음 또는 false, RequestFuncName = {InRequest?.RequestFuncName}");

                if (resultJson.HasKey(FCommonDefine.ConstResponse))
                {
                    string response = resultJson[FCommonDefine.ConstResponse];
                    Debug.LogError($"[Network Error] Response: {response}");
                }
            }

            if (resultJson.HasKey(FCommonDefine.ConstError))
            {
                string errorMessage = resultJson[FCommonDefine.ConstError];
                Debug.LogError($"[Network Error] RequestFuncName = {InRequest?.RequestFuncName}, Error: {errorMessage}");
            }
        }
#endif
        #endregion
    }
    #endregion
}