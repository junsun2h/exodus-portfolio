import "reflect-metadata";
import * as admin from "firebase-admin";
import { https, setGlobalOptions, GlobalOptions } from "firebase-functions/v2";
import { onSchedule } from "firebase-functions/v2/scheduler";

import { FirebaseInfo } from "./Data/Config/ServerConfig";
import { logDebug } from "./Utility/UtilityBasic";
import * as GameBalanceConfig from "./Data/Shared/Config/GameBalanceConfig";
import * as ScheduleConfig from "./Data/Config/ScheduleConfig";
import { request_Account_Delete, request_Account_Login, request_Account_Login_FullTest, request_Account_NickName, request_Account_UpdateFCMToken } from "./API/CoreData/Account";
import { CoreData_Account } from "./Data/Types/CoreDataExtensions";
import { request_Currency_Set } from "./API/CoreData/Currency";
import { request_Equipment_Gacha, request_Equipment_Reforging, request_Equipment_AutoReforging, request_Equipment_NormalMod_Lock, request_Equipment_Reinforce, request_Equipment_Promotion, request_Equipment_Promotion_All, request_Equipment_Equip, request_Equipment_UnEquip, request_Equipment_Gacha_Reinforce, request_Equipment_Gacha_Reforging, request_Equipment_Equip_Recommend } from "./API/CoreData/Equipment";
import { request_Player_Growth_LevelUp, request_Player_Growth_Unlock, request_Player_Reinforce_LevelUp, request_Player_Reinforce_Unlock, request_Player_Reinforce_Reset } from "./API/CoreData/Player";
import { request_Stage_Enter, request_Stage_EndLoop, request_Stage_Clear, request_Stage_Fail, request_Stage_QuickClear } from "./API/CoreData/Stage";
import { request_Nebula_AdEnable, request_Nebula_LevelUp } from "./API/CoreData/Nebula";
import { request_Constellation_Gacha, request_Constellation_LevelUp, request_Constellation_Equip, request_Constellation_Unequip, request_Constellation_Craft } from "./API/CoreData/Constellation";
import { request_Skill_Gacha, request_Skill_Reinforce, request_Skill_Promotion, request_Skill_Promotion_All, request_Skill_Equip, request_Skill_Rune_Equip, request_Skill_Rune_UnEquip, request_Skill_UnEquip, request_Skill_Gacha_Reinforce, request_Skill_Rune_Equip_Recommend, request_Skill_Equip_Recommend } from "./API/CoreData/Skill";
import { request_Pet_Create, request_Pet_Equip, request_Pet_Equip_Recommend, request_Pet_Gacha, request_Pet_Promotion, request_Pet_Promotion_All } from "./API/CoreData/Pet";
import { request_Preset_ChangeName, request_Preset_Load, request_Preset_Save, request_Preset_SetChallengeMap, request_Preset_SetQuickBar, request_Preset_Unlock } from "./API/CoreData/Preset";
import { request_Awaken_Element_LevelUp, request_Awaken_Promotion } from "./API/CoreData/Awaken";
import { request_Gacha_GetLevelReward, request_Gacha_RedSoulstone } from "./API/CoreData/Gacha";
import { request_ServerMailbox_Create } from "./API/ServerOperation/ServerMailbox";
import { request_ServerNotice_Create, request_ServerNotice_CreateCustom, request_ServerNotice_Update, request_ServerNotice_Delete, request_ServerNotice_DeleteAll, request_ServerNotice_CleanupExpired } from "./API/ServerOperation/Notification/ServerNotice";
import { request_ServerPush_SendToTopic, request_ServerPush_SendToAll } from "./API/ServerOperation/Notification/ServerPush";
import { request_ChatSystemMessage_Create, request_ChatSystemMessage_Update, request_ChatSystemMessage_Delete, request_ChatSystemMessage_DeleteAll, request_ChatSystemMessage_CleanupExpired } from "./API/ServerOperation/Notification/ChatSystemMessage";
import { request_ServerNotification_MaintenancePreview, request_ServerNotification_MaintenanceStart, request_ServerNotification_MaintenanceEnd, request_ServerNotification_EventStart, request_ServerNotification_UpdatePatch } from "./API/ServerOperation/Notification/ServerEventNotification";

// [비활성화] 스케줄 함수 전면 중단 - 아래 ScheduleFunction 섹션 참조
// import { CServerMailboxDeleteExpired, CChatSystemMessageCleanupExpired, CServerNoticeCleanupExpired, Test_ScheduledFunction_ServerMailbox_DeleteExpired } from "./API/Schedule/ScheduleFunction";
// [비활성화] Firestore 일일 백업 제거 - 아래 백업 섹션 참조
// import { CDataBackup_DailyExport, Test_DataBackup_DailyExport } from "./API/Schedule/DataBackup";
// [비활성화] 정기점검 자동 스케줄은 시연 버전에서 제거 (아래 정기점검 스케줄 함수 섹션 참조).
// 수동 점검은 request_Maintenance_Emergency* 로 계속 가능하므로 그 셋만 남긴다.
import { request_Maintenance_EmergencyStart, request_Maintenance_EmergencyEnd, request_Maintenance_Extend } from "./API/Schedule/DailyMaintenance";
// [비활성화] 만료 데이터 정리 스케줄 제거 - 아래 섹션 참조
// import { CDataCleanup_ExpiredData } from "./API/Schedule/DataCleanup";
// [비활성화] 랭킹 시즌 리셋은 누적 데이터이므로 리셋하지 않음
// import { CRankingSeasonReset } from "./API/Schedule/RankingSeasonReset";
// [비활성화] 시연 버전에서 랭킹 미사용 - 아래 scheduledFunction_RankingReward_Daily 참조
// import { CRankingReward_Daily, Test_RankingReward_Daily } from "./API/Schedule/RankingReward";
import { request_Cache_Invalidate, request_Cache_GetVersion } from "./API/ServerOperation/CacheInvalidation";
import { CServerInternalBase } from "./API/Base/ServerInternalBase";
import { request_Mailbox_CheckAndDeliverSubscriptionProduct, request_Mailbox_Receive, request_Mailbox_ReceiveAll, request_Mailbox_SyncAndCleanServerMailbox } from "./API/CoreData/Mailbox";
import { request_Product_Buy, request_ProductPackage_Buy, request_ProductSubscription_Buy, request_ProductPass_Buy, request_ProductPass_ReceiveAll, request_ProductPass_Receive_PaidItem, request_ProductPass_ReceiveAll_FreeItem, request_ProductPass_Reset } from "./API/CoreData/Product";
import { request_InitRankDB, request_InsertFakeData_All, request_ClearRankData_All } from "./API/ServerOperation/Rank";
import { request_UpdateDailyUserData } from "./API/ServerOperation/UpdateDailyUserData";
import { request_QuestDaily_Complete, request_QuestWeekly_Complete } from "./API/CoreData/Quest";
import { request_QuestMain_Complete } from "./API/CoreData/Quest";
import { request_DataBundle_CheckVersion, request_DataBundle_Create } from "./API/DataBundle/DataBundle";
import { request_ServerInfo_Update, request_ServerVersionCheck, request_ServerInfo_AddServer } from "./API/ServerOperation/ServerInfo";
import { request_CoreDataMigration } from "./API/Migration/Migration";
import { request_OfflineReward_Claim } from "./API/OfflineReward/OfflineReward";
import { request_Craft_Merge, request_Craft_Disassemble } from "./API/CoreData/Craft";
import { request_Title_Obtain, request_Title_Set } from "./API/CoreData/title";
import { request_Ticket_UseGacha, request_Ticket_UsePick, request_Ticket_UseFix } from "./API/CoreData/Ticket";
import { request_DevTool_GenerateDummy } from "./API/ServerOperation/DevTools/GenerateDummy";
import { request_Security_VerifyAppSignature, request_Security_ReportThreat, request_Security_InitConfig, request_Security_UpdateConfig, request_Security_GetConfig, request_Security_RefreshCertificateHashes, CSecurityConfig } from "./API/Security/AppIntegrity";
// [비활성화] Schedule_Security_CertificatePinningAutoRefresh 제거 - 클라이언트가 해시를 받아가지 않음
// (CertificatePinning.CreateCertificateHandler / SetDynamicCertificateHashes 호출처가 없음)
// import { Test_CertificatePinning_AutoRefresh } from "./API/Schedule/SecuritySchedule";

// Start writing Firebase Functions
// https://firebase.google.com/docs/functions/typescript

// Firebase Admin SDK 초기화 - Auth 에뮬레이터는 사용하지 않음
const app = admin.initializeApp();

// Auth 에뮬레이터 연결 방지 - 실제 Firebase Auth 사용
if (process.env.FUNCTIONS_EMULATOR === "true") {
	// Functions 에뮬레이터 환경에서도 실제 Firebase Auth 사용
	delete process.env.FIREBASE_AUTH_EMULATOR_HOST;
}

// -----------------------------------------------
// Environment Debug (Emulator 개발용)
// -----------------------------------------------
const debugEnvironment = () => {
	logDebug("=== Environment Debug ===");
	logDebug("FUNCTIONS_EMULATOR:", process.env.FUNCTIONS_EMULATOR);
	logDebug("GCLOUD_PROJECT:", process.env.GCLOUD_PROJECT);
	logDebug("FUNCTION_REGION:", process.env.FUNCTION_REGION);
	logDebug("FIREBASE_STORAGE_BUCKET:", process.env.FIREBASE_STORAGE_BUCKET);
	logDebug("FIREBASE_STORAGE_EMULATOR_HOST:", process.env.FIREBASE_STORAGE_EMULATOR_HOST);
	logDebug("FIRESTORE_EMULATOR_HOST:", process.env.FIRESTORE_EMULATOR_HOST);
	logDebug("NODE_ENV:", process.env.NODE_ENV);
	logDebug("FUNCTIONAL_TEST:", process.env.FUNCTIONAL_TEST);
	logDebug("========================");
};
//debugEnvironment();	// 환경 정보 출력

// -----------------------------------------------
// V2 onCall, onRequest 옵션
// -----------------------------------------------
export enum V2_Functions_Preset {
	LITE = 0, // 메뉴 클릭 등 가벼운 처리
	STANDARD = 1, // DB 읽기 쓰기 1~2회, 비교적 간단한 계산, 루프 없음
	HEAVY = 2, // 루프가 들어가는 로직
	SCHEDULE = 3, // 랭킹 정렬 등 서버 스케쥴러
}

export const FUNCTION_PRESET: Record<V2_Functions_Preset, GlobalOptions> = {
	[V2_Functions_Preset.LITE]: {
		concurrency: 80,
		// 256MiB 로는 OOM 발생 (Cloud Run 이 컨테이너를 강제 종료 → 클라이언트에 INTERNAL 반환).
		// index.ts 가 전체 API 모듈을 로드하고 GameDBFactory.buildGameDBCache() 가
		// StaticGameDB 전체를 메모리에 올리는 시점에 이미 RSS 약 190MB 를 사용한다.
		// 요청 처리분까지 감안해 512MiB 로 상향.
		memory: "512MiB",
		// 콜드스타트 시 GameDBFactory.buildGameDBCache()가 첫 요청 안에서 실행되므로 10s로는 부족
		timeoutSeconds: 30,
		minInstances: 0,
	},
	[V2_Functions_Preset.STANDARD]: {
		concurrency: 40,
		memory: "512MiB",
		timeoutSeconds: 30,
	},
	[V2_Functions_Preset.HEAVY]: {
		concurrency: 20,
		memory: "1GiB",
		timeoutSeconds: 60,
	},
	[V2_Functions_Preset.SCHEDULE]: {
		concurrency: 1,
		memory: "1GiB",
		timeoutSeconds: 360,
		minInstances: 0,
		maxInstances: 1,
	},
};

// https://realspace3.atlassian.net/wiki/spaces/Pixel/pages/2803171329/Cloud+Functions+V2
// onCall, onRequest 전역 설정. 모든 v2 함수가 이 설정을 기본으로 물려받는다.
// 예시) 아래 코드는 전역 설정을 덮어 씌운다. 덮어 씌운 값은 우선한다.
// export const Request_Stage_EndLoop    = https.onCall(
//  setV2FunctionPreset(V2_Functions_Preset.HEAVY, { concurrency: 30 }),
//  request_Stage_EndLoop
// );
setGlobalOptions({
	region: FirebaseInfo.FBRegion,
	concurrency: 80,
	// LITE 와 동일한 이유로 256MiB → 512MiB (프리셋을 지정하지 않는 Test_* onRequest 함수들이 상속)
	memory: "512MiB",
	timeoutSeconds: 60,
	maxInstances: 200, // 최소한의 방어코드
});

// 기본값: "비인증 액세스 허용" (AppCheck 사용하지 않음)
export function setV2FunctionPreset(p: V2_Functions_Preset, extra?: Partial<GlobalOptions>): GlobalOptions {
	return { ...FUNCTION_PRESET[p], ...extra };
}

// -----------------------------------------------
// ScheduleFunction
// -----------------------------------------------
// [비활성화] 스케줄 함수를 전면 중단했다.
// 이 프로젝트는 1~2분짜리 데모 시연 용도로만 쓰고 출시하지 않으므로,
// 만료 데이터가 쌓일 일이 없어 주기적 정리가 필요 없다.
// Cloud Scheduler는 작업이 존재하기만 해도 과금(무료 3개 초과분 $0.10/작업/월)되므로 전부 제거한다.
// 실서비스로 전환할 때 이 파일의 [비활성화] 주석들을 되살릴 것.

// export const scheduledFunction_ServerMailbox_DeleteExpired = onSchedule(
// 	{
// 		schedule: `*/${ScheduleConfig.schedule_ServerMailbox_DeleteExpired_Minutes} * * * *`,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CServerMailboxDeleteExpired)
// );

// [비활성화] 시연 버전에서 랭킹 미사용.
// Cloud Scheduler는 작업 존재만으로 과금($0.10/작업/월)되므로 배포에서도 제거했다.
// 랭킹 재도입 시 주석 해제 + Floor_N 컬렉션 그룹 복합 인덱스(bestStageMain DESC + timestamp) 생성 필요.
// export const scheduledFunction_RefreshAllRanks = onSchedule(
// 	{
// 		schedule: `every ${ScheduleConfig.schedule_RefreshAllRanks_Minutes} minutes`,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CRefreshAllRanks)
// );

// 백업 시 수동 호출 예정 - 스케줄 비활성화 (Request_ChatSystemMessage_CleanupExpired, Request_ServerNotice_CleanupExpired 사용)
// export const scheduledFunction_ChatSystemMessage_CleanupExpired = onSchedule(
// 	{
// 		schedule: `*/${GameBalanceConfig.schedule_ChatSystemMessage_CleanupExpired_Minutes} * * * *`,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CChatSystemMessageCleanupExpired)
// );

// export const scheduledFunction_ServerNotice_CleanupExpired = onSchedule(
// 	{
// 		schedule: `*/${GameBalanceConfig.schedule_ChatSystemMessage_CleanupExpired_Minutes} * * * *`,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CServerNoticeCleanupExpired)
// );

// [비활성화] Firestore 일일 백업 (매일 새벽 4:05 KST = UTC 19:05)
// 시연 버전에서는 백업이 불필요하다. 게다가 이 함수는 실제로 동작한 적이 없다 -
// 함수 서비스 계정에 datastore.databases.export 권한이 없어 "The caller does not have
// permission"으로 매일 실패했다. 되살릴 때는 서비스 계정에 roles/datastore.importExportAdmin을
// 부여하고 백업 버킷(PROJECTID-backups)을 먼저 만들어야 한다.
// export const scheduledFunction_DataBackup_DailyExport = onSchedule(
// 	{
// 		schedule: ScheduleConfig.schedule_DataBackup_DailyExport_Cron,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CDataBackup_DailyExport)
// );

// 백업 테스트용 HTTP 함수 (수동 테스트용)
// export { Test_DataBackup_DailyExport };

// -----------------------------------------------
// 정기점검 스케줄 함수
// -----------------------------------------------

// [비활성화] 시연 버전에는 실유저가 없어 정기점검이 불필요하다.
// 오히려 매일 04:00에 서버가 MAINTENANCE로 잠기고, 종료 스케줄이 실패하면 그대로 막히는 리스크만 남는다.
// 수동 점검이 필요하면 Request_Maintenance_EmergencyStart / EmergencyEnd 로 처리한다.
// 재도입 시 아래 5개(PreAlert10Min/PreAlert5Min/Start/End_Daily/End_Weekly)를 함께 주석 해제할 것.

// 점검 10분 전 알림 (03:50 KST)
// export const scheduledFunction_Maintenance_PreAlert10Min = onSchedule(
// 	{
// 		schedule: ScheduleConfig.schedule_Maintenance_PreAlert10Min_Cron,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CMaintenanceSchedule_PreAlert10Min)
// );

// 점검 5분 전 알림 (03:55 KST)
// export const scheduledFunction_Maintenance_PreAlert5Min = onSchedule(
// 	{
// 		schedule: ScheduleConfig.schedule_Maintenance_PreAlert5Min_Cron,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CMaintenanceSchedule_PreAlert5Min)
// );

// 점검 시작 (04:00 KST)
// export const scheduledFunction_Maintenance_Start = onSchedule(
// 	{
// 		schedule: ScheduleConfig.schedule_Maintenance_Start_Cron,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CMaintenanceSchedule_Start)
// );

// [비활성화] 만료 데이터 삭제 (04:30 KST) - 위 스케줄 전면 중단 주석 참조
// export const scheduledFunction_DataCleanup_ExpiredData = onSchedule(
// 	{
// 		schedule: ScheduleConfig.schedule_DataCleanup_Cron,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CDataCleanup_ExpiredData)
// );

// [비활성화] 랭킹 시즌 리셋은 누적 데이터이므로 리셋하지 않음
// export const scheduledFunction_RankingSeasonReset = onSchedule(
// 	{
// 		schedule: GameBalanceConfig.schedule_RankingSeasonReset_Cron,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CRankingSeasonReset)
// );

// [비활성화] 시연 버전에서 랭킹 미사용 - scheduledFunction_RefreshAllRanks와 함께 배포에서 제거.
// 일일 랭킹 보상 지급 (매일 00:00 KST = UTC 15:00)
// export const scheduledFunction_RankingReward_Daily = onSchedule(
// 	{
// 		schedule: "0 15 * * *", // 매일 00:00 KST
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CRankingReward_Daily)
// );

// [비활성화] 점검 시작 스케줄과 세트 - 위 정기점검 비활성화 주석 참조

// 일일 점검 종료 (05:00 KST)
// export const scheduledFunction_Maintenance_End_Daily = onSchedule(
// 	{
// 		schedule: ScheduleConfig.schedule_Maintenance_End_Daily_Cron,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CMaintenanceSchedule_End_Daily)
// );

// 주간 점검 종료 (07:00 KST, 수요일)
// export const scheduledFunction_Maintenance_End_Weekly = onSchedule(
// 	{
// 		schedule: ScheduleConfig.schedule_Maintenance_End_Weekly_Cron,
// 		...setV2FunctionPreset(V2_Functions_Preset.SCHEDULE),
// 	},
// 	CServerInternalBase.createInternalFunction(CMaintenanceSchedule_End_Weekly)
// );

// 스케줄러 함수는 에뮬레이터에서 테스트 할 수 없다. 아래 TEST_ 시리즈 함수를 통해 테스트
// http://localhost:5001/<project-id>/asia-northeast3/Test_ScheduledFunction_ServerMailbox_DeleteExpired 크롬 주소에서 호출
//export { Test_ScheduledFunction_ServerMailbox_DeleteExpired };

// -----------------------------------------------
// DataBundle
// -----------------------------------------------
export const Request_DataBundle_Create = https.onCall(setV2FunctionPreset(V2_Functions_Preset.SCHEDULE), request_DataBundle_Create);
export const Request_DataBundle_CheckVersion = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_DataBundle_CheckVersion);

// -----------------------------------------------
// ServerInfo
// -----------------------------------------------
export const Request_ServerInfo_Update = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerInfo_Update);
export const Request_ServerVersionCheck = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerVersionCheck);
export const Request_ServerInfo_AddServer = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerInfo_AddServer);

// -----------------------------------------------
// Security (보안 - APK 서명 검증, 위협 리포트, 인증서 핀닝)
// -----------------------------------------------
export const Request_Security_VerifyAppSignature = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Security_VerifyAppSignature);
export const Request_Security_ReportThreat = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Security_ReportThreat);
export const Request_Security_InitConfig = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Security_InitConfig);
export const Request_Security_UpdateConfig = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Security_UpdateConfig);
export const Request_Security_GetConfig = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Security_GetConfig);
export const Request_Security_RefreshCertificateHashes = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Security_RefreshCertificateHashes);

// [비활성화] 인증서 핀닝 해시 자동 갱신 (매일 04:10 KST)
// 클라이언트에 CertificatePinning 구현은 있으나 CreateCertificateHandler / SetDynamicCertificateHashes를
// 호출하는 곳이 없어, 갱신한 해시를 받아가는 쪽이 없다. 핀닝을 실제로 연결할 때 함께 되살릴 것.
// 수동 갱신은 Request_Security_RefreshCertificateHashes 로 가능하다.
// export { Schedule_Security_CertificatePinningAutoRefresh };
// [비활성화] 인증서 핀닝 자동 갱신 테스트용 HTTP 함수
// 인증 없이 공개되는 엔드포인트라 핀닝을 쓰지 않는 동안은 노출할 이유가 없다.
// export { Test_CertificatePinning_AutoRefresh };

// -----------------------------------------------
// Maintenance (점검 관리 - 관리자 전용)
// -----------------------------------------------
export const Request_Maintenance_EmergencyStart = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Maintenance_EmergencyStart);
export const Request_Maintenance_EmergencyEnd = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Maintenance_EmergencyEnd);
export const Request_Maintenance_Extend = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Maintenance_Extend);

// -----------------------------------------------
// Cache (캐시 관리 - 관리자 전용)
// -----------------------------------------------
export const Request_Cache_Invalidate = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Cache_Invalidate);
export const Request_Cache_GetVersion = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Cache_GetVersion);

// -----------------------------------------------
// Server
// -----------------------------------------------
export const Request_UpdateDailyUserData = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_UpdateDailyUserData);

// -----------------------------------------------
// ServerMailbox (비인증 허용)
// -----------------------------------------------
export const Request_ServerMailbox_Create = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerMailbox_Create);

// -----------------------------------------------
// ServerNotice (관리자 전용)
// -----------------------------------------------
export const Request_ServerNotice_Create = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerNotice_Create);
export const Request_ServerNotice_CreateCustom = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerNotice_CreateCustom);
export const Request_ServerNotice_Update = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerNotice_Update);
export const Request_ServerNotice_Delete = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerNotice_Delete);
export const Request_ServerNotice_DeleteAll = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ServerNotice_DeleteAll);
export const Request_ServerNotice_CleanupExpired = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ServerNotice_CleanupExpired);

// -----------------------------------------------
// ServerPush (FCM 푸시 알림 - 관리자 전용)
// -----------------------------------------------
export const Request_ServerPush_SendToTopic = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerPush_SendToTopic);
export const Request_ServerPush_SendToAll = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ServerPush_SendToAll);

// -----------------------------------------------
// ChatSystemMessage (채팅 시스템 메시지 - 관리자 전용)
// -----------------------------------------------
export const Request_ChatSystemMessage_Create = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ChatSystemMessage_Create);
export const Request_ChatSystemMessage_Update = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ChatSystemMessage_Update);
export const Request_ChatSystemMessage_Delete = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ChatSystemMessage_Delete);
export const Request_ChatSystemMessage_DeleteAll = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ChatSystemMessage_DeleteAll);
export const Request_ChatSystemMessage_CleanupExpired = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ChatSystemMessage_CleanupExpired);

// -----------------------------------------------
// ServerEventNotification (서버 이벤트 알림 - 관리자 전용)
// -----------------------------------------------
export const Request_ServerNotification_MaintenancePreview = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ServerNotification_MaintenancePreview);
export const Request_ServerNotification_MaintenanceStart = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ServerNotification_MaintenanceStart);
export const Request_ServerNotification_MaintenanceEnd = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ServerNotification_MaintenanceEnd);
export const Request_ServerNotification_EventStart = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ServerNotification_EventStart);
export const Request_ServerNotification_UpdatePatch = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ServerNotification_UpdatePatch);

// -----------------------------------------------
// Rank
// -----------------------------------------------
export const Request_InitRankDB = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_InitRankDB);
export const Request_InsertFakeData_All = https.onCall(setV2FunctionPreset(V2_Functions_Preset.SCHEDULE), request_InsertFakeData_All);
export const Request_ClearRankData_All = https.onCall(setV2FunctionPreset(V2_Functions_Preset.SCHEDULE), request_ClearRankData_All);

// -----------------------------------------------
// Account
// -----------------------------------------------
export const Request_Account_Login = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Account_Login);
export const Request_Account_Login_FullTest = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Account_Login_FullTest); // 테스트 로그인
export const Request_Account_Delete = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Account_Delete);
export const Request_Account_NickName = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Account_NickName);
export const Request_Account_UpdateFCMToken = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Account_UpdateFCMToken);

// -----------------------------------------------
// Player
// -----------------------------------------------
export const Request_Player_Growth_LevelUp = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Player_Growth_LevelUp);
export const Request_Player_Growth_Unlock = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Player_Growth_Unlock);
export const Request_Player_Reinforce_LevelUp = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Player_Reinforce_LevelUp);
export const Request_Player_Reinforce_Unlock = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Player_Reinforce_Unlock);
export const Request_Player_Reinforce_Reset = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Player_Reinforce_Reset);

// -----------------------------------------------
// Equipment
// -----------------------------------------------
export const Request_Equipment_Gacha = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Equipment_Gacha);
export const Request_Equipment_Gacha_Reinforce = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Equipment_Gacha_Reinforce);
export const Request_Equipment_Gacha_Reforging = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Equipment_Gacha_Reforging);
export const Request_Equipment_Reforging = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Equipment_Reforging);
export const Request_Equipment_AutoReforging = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Equipment_AutoReforging);
export const Request_Equipment_NormalMod_Lock = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Equipment_NormalMod_Lock);
export const Request_Equipment_Reinforce = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Equipment_Reinforce);
export const Request_Equipment_Promotion = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Equipment_Promotion);
export const Request_Equipment_Promotion_All = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Equipment_Promotion_All);
export const Request_Equipment_Equip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Equipment_Equip);
export const Request_Equipment_UnEquip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Equipment_UnEquip);
export const Request_Equipment_Equip_Recommend = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Equipment_Equip_Recommend);

// -----------------------------------------------
// Skill
// -----------------------------------------------
export const Request_Skill_Gacha = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Skill_Gacha);
export const Request_Skill_Gacha_Reinforce = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Skill_Gacha_Reinforce);
export const Request_Skill_Reinforce = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Skill_Reinforce);
export const Request_Skill_Promotion = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Skill_Promotion);
export const Request_Skill_Promotion_All = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Skill_Promotion_All);
export const Request_Skill_Equip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Skill_Equip);
export const Request_Skill_UnEquip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Skill_UnEquip);
export const Request_Skill_Equip_Recommend = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Skill_Equip_Recommend);
export const Request_Skill_Rune_Equip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Skill_Rune_Equip);
export const Request_Skill_Rune_UnEquip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Skill_Rune_UnEquip);
export const Request_Skill_Rune_Equip_Recommend = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Skill_Rune_Equip_Recommend);

// -----------------------------------------------
// Pet
// -----------------------------------------------
export const Request_Pet_Gacha = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Pet_Gacha);
export const Request_Pet_Create = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Pet_Create);
export const Request_Pet_Promotion = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Pet_Promotion);
export const Request_Pet_Promotion_All = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Pet_Promotion_All);
export const Request_Pet_Equip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Pet_Equip);
export const Request_Pet_Equip_Recommend = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Pet_Equip_Recommend);

// -----------------------------------------------
// Stage
// -----------------------------------------------
export const Request_Stage_Enter = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Stage_Enter);
export const Request_Stage_EndLoop = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Stage_EndLoop);
export const Request_Stage_Clear = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Stage_Clear);
export const Request_Stage_Fail = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Stage_Fail);
export const Request_Stage_QuickClear = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Stage_QuickClear);

// -----------------------------------------------
// Nebula
// -----------------------------------------------
export const Request_Nebula_LevelUp = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Nebula_LevelUp);
export const Request_Nebula_AdEnable = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Nebula_AdEnable);

// -----------------------------------------------
// Constellation (성좌 가챠/강화/슬롯)
// -----------------------------------------------
export const Request_Constellation_Gacha = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Constellation_Gacha);
export const Request_Constellation_LevelUp = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Constellation_LevelUp);
export const Request_Constellation_Equip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Constellation_Equip);
export const Request_Constellation_Unequip = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Constellation_Unequip);
export const Request_Constellation_Craft = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Constellation_Craft);

// -----------------------------------------------
// Currency
// -----------------------------------------------
export const Request_Currency_Set = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Currency_Set);

// -----------------------------------------------
// Preset
// -----------------------------------------------
export const Request_Preset_Unlock = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Preset_Unlock);
export const Request_Preset_ChangeName = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Preset_ChangeName);
export const Request_Preset_Load = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Preset_Load);
export const Request_Preset_SetQuickBar = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Preset_SetQuickBar);
export const Request_Preset_Save = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Preset_Save);
export const Request_Preset_SetChallengeMap = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Preset_SetChallengeMap);

// -----------------------------------------------
// Awaken
// -----------------------------------------------
export const Request_Awaken_Promotion = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Awaken_Promotion);
export const Request_Awaken_Element_LevelUp = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Awaken_Element_LevelUp);

// -----------------------------------------------
// Contents
// -----------------------------------------------

// -----------------------------------------------
// Title
// -----------------------------------------------
export const Request_Title_Obtain = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Title_Obtain);
export const Request_Title_Set = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Title_Set);

// -----------------------------------------------
// Gacha
// -----------------------------------------------
export const Request_Gacha_GetLevelReward = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Gacha_GetLevelReward);
export const Request_Gacha_RedSoulstone = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Gacha_RedSoulstone);

// -----------------------------------------------
// Product
// -----------------------------------------------
export const Request_Product_Buy = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Product_Buy);
export const Request_ProductPackage_Buy = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_ProductPackage_Buy);
export const Request_ProductSubscription_Buy = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_ProductSubscription_Buy);
export const Request_ProductPass_Buy = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_ProductPass_Buy);
export const Request_ProductPass_Receive_PaidItem = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ProductPass_Receive_PaidItem);
export const Request_ProductPass_ReceiveAll_FreeItem = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ProductPass_ReceiveAll_FreeItem);
export const Request_ProductPass_ReceiveAll = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_ProductPass_ReceiveAll);
export const Request_ProductPass_Reset = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_ProductPass_Reset);

// -----------------------------------------------
// Mailbox
// -----------------------------------------------
export const Request_Mailbox_SyncAndCleanServerMailbox = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Mailbox_SyncAndCleanServerMailbox);
export const Request_Mailbox_CheckAndDeliverSubscriptionProduct = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Mailbox_CheckAndDeliverSubscriptionProduct);
export const Request_Mailbox_Receive = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Mailbox_Receive);
export const Request_Mailbox_ReceiveAll = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Mailbox_ReceiveAll);

// -----------------------------------------------
// Quest
// -----------------------------------------------
export const Request_QuestMain_Complete = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_QuestMain_Complete);
export const Request_QuestDaily_Complete = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_QuestDaily_Complete);
export const Request_QuestWeekly_Complete = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_QuestWeekly_Complete);

// -----------------------------------------------
// Migration
// -----------------------------------------------
export const Request_CoreDataMigration = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_CoreDataMigration);

// -----------------------------------------------
// OfflineReward
// -----------------------------------------------
export const Request_OfflineReward_Claim = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_OfflineReward_Claim);

// -----------------------------------------------
// Craft
// -----------------------------------------------
export const Request_Craft_Merge = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Craft_Merge);
export const Request_Craft_Disassemble = https.onCall(setV2FunctionPreset(V2_Functions_Preset.LITE), request_Craft_Disassemble);

// -----------------------------------------------
// Ticket (티켓 시스템)
// -----------------------------------------------
export const Request_Ticket_UseGacha = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Ticket_UseGacha);
export const Request_Ticket_UsePick = https.onCall(setV2FunctionPreset(V2_Functions_Preset.STANDARD), request_Ticket_UsePick);
export const Request_Ticket_UseFix = https.onCall(setV2FunctionPreset(V2_Functions_Preset.HEAVY), request_Ticket_UseFix);

// -----------------------------------------------
// DevTools (개발자 도구)
// -----------------------------------------------
export const Request_DevTool_GenerateDummy = https.onCall(setV2FunctionPreset(V2_Functions_Preset.SCHEDULE), request_DevTool_GenerateDummy);
