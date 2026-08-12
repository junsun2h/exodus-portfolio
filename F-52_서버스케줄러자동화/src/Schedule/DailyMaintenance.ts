// 정기점검 자동화 시스템
// 일일/주간/긴급 점검 스케줄 및 Admin API

import * as admin from "firebase-admin";
import { https } from "firebase-functions/v2";
import { CServerInternalBase } from "../Base/ServerInternalBase";
import { CAdminFunctionBase } from "../Base/AdminFunctionBase";
import { ServerInfo, EServerStatusInfo, EMaintenanceType, MaintenanceInfo } from "../../Data/Shared/Types/ServerStatusTypes";
import { EResponseCode } from "../../Data/Types/ResponseCodes";
import { EAPIErrorCode } from "../../Data/Generated/CommonEnum";
import { throwLogicError, logCategory, logDebug } from "../../Utility/UtilityBasic";
import { ValidationSchema } from "../../Utility/InputValidation";
import { broadcastServerNotification } from "../ServerOperation/Notification/ServerEventNotification";
import { EServerEventNotificationType } from "../ServerOperation/Notification/ServerEventNotificationDefinitions";

// -----------------------------------------------
// Firebase Functions Export (파일 최상단)
// -----------------------------------------------

// 긴급점검 시작 (AdminTool에서 수동 호출)
export async function request_Maintenance_EmergencyStart(request: https.CallableRequest<any>) {
	return await new CMaintenance_EmergencyStart().handleRequest(request);
}

// 긴급점검 종료 (AdminTool에서 수동 호출)
export async function request_Maintenance_EmergencyEnd(request: https.CallableRequest<any>) {
	return await new CMaintenance_EmergencyEnd().handleRequest(request);
}

// 점검 연장 (모든 점검 유형에서 사용 가능)
export async function request_Maintenance_Extend(request: https.CallableRequest<any>) {
	return await new CMaintenance_Extend().handleRequest(request);
}

// -----------------------------------------------
// 유틸리티 함수 (표시용 KST 변환)
// -----------------------------------------------

// 현재 한국 시간 문자열 반환 (HH:MM 형식) - 운영툴/유저 표시용
function getKoreanTimeString(date: Date = new Date()): string {
	return date.toLocaleString("en-GB", {
		timeZone: "Asia/Seoul",
		hour: "2-digit",
		minute: "2-digit",
		hour12: false,
	});
}

// 현재 한국 날짜/시간 문자열 반환 (YYYY-MM-DD HH:MM 형식) - 운영툴/유저 표시용
function getKoreanDateTimeString(date: Date = new Date()): string {
	return date.toLocaleString("sv-SE", {
		timeZone: "Asia/Seoul",
		year: "numeric",
		month: "2-digit",
		day: "2-digit",
		hour: "2-digit",
		minute: "2-digit",
		hour12: false,
	}).replace(" ", " ");
}

// 현재 요일이 수요일인지 확인 (KST 기준)
function isWednesday(date: Date = new Date()): boolean {
	const formatter = new Intl.DateTimeFormat("en-US", {
		timeZone: "Asia/Seoul",
		weekday: "short",
	});
	return formatter.format(date) === "Wed";
}

// 현재 달의 첫 번째 수요일인지 확인
function isFirstWednesdayOfMonth(date: Date = new Date()): boolean {
	if (!isWednesday(date)) return false;
	const formatter = new Intl.DateTimeFormat("en-CA", {
		timeZone: "Asia/Seoul",
		day: "2-digit",
	});
	const day = parseInt(formatter.format(date));
	return day <= 7; // 1~7일 사이면 첫 번째 수요일
}

// 분 단위로 종료 시간 계산
function calculateEndTime(minutesFromNow: number): string {
	const endDate = new Date(Date.now() + minutesFromNow * 60 * 1000);
	return getKoreanTimeString(endDate);
}

// ServerInfo 문서 ID 가져오기 (테스트 환경 지원)
function getServerInfoDocId(): string {
	return process.env.FUNCTIONAL_TEST === "unittest" ? "ServerInfo_Test" : "ServerInfo";
}

// ServerInfo 조회
async function getServerInfo(): Promise<ServerInfo> {
	const db = admin.firestore();
	const docId = getServerInfoDocId();
	const doc = await db.collection("ServerInfo").doc(docId).get();

	if (!doc.exists) {
		throwLogicError("getServerInfo(): ServerInfo 문서가 없습니다.", EAPIErrorCode.DATABASE_ERROR);
	}

	return doc.data() as ServerInfo;
}

// ServerInfo 상태 업데이트
async function updateServerStatus(status: EServerStatusInfo, maintenanceInfo: MaintenanceInfo | null): Promise<void> {
	const db = admin.firestore();
	const docId = getServerInfoDocId();

	const updateData: Partial<ServerInfo> = {
		status: status,
	};

	if (maintenanceInfo) {
		updateData.maintenanceInfo = maintenanceInfo;
	}

	await db.collection("ServerInfo").doc(docId).update(updateData);
	logCategory("MAINTENANCE", `ServerInfo 상태 업데이트: status=${status}`);
}

// 캐시 버전 증가
async function incrementCacheVersion(): Promise<void> {
	const db = admin.firestore();
	const docId = getServerInfoDocId();

	await db
		.collection("ServerInfo")
		.doc(docId)
		.update({
			cacheVersion: admin.firestore.FieldValue.increment(1),
		});

	logCategory("CACHE_INVALIDATE", "캐시 버전 증가 완료");
}

// -----------------------------------------------
// 스케줄 기본 클래스
// -----------------------------------------------

abstract class CMaintenanceScheduleBase extends CServerInternalBase {
	constructor(functionName: string) {
		super(functionName);
		// 점검 관련 스케줄은 점검 중에도 실행 허용
		this.setMaintenancePolicy(true, true);
	}
}

// -----------------------------------------------
// 스케줄 클래스들 (자동 실행)
// -----------------------------------------------

// 점검 예고 (10분 전)
export class CMaintenanceSchedule_PreAlert10Min extends CMaintenanceScheduleBase {
	constructor() {
		super("MaintenanceSchedule_PreAlert10Min");
	}

	protected async execute(): Promise<void> {
		logDebug("[MAINTENANCE_PREALERT] 점검 10분 전 알림 발송");

		await broadcastServerNotification(EServerEventNotificationType.SERVER_MAINTENANCE_PREVIEW, {
			minutes: 10,
		});

		logCategory("MAINTENANCE", "점검 10분 전 알림 발송 완료");
	}
}

// 점검 예고 (5분 전)
export class CMaintenanceSchedule_PreAlert5Min extends CMaintenanceScheduleBase {
	constructor() {
		super("MaintenanceSchedule_PreAlert5Min");
	}

	protected async execute(): Promise<void> {
		logDebug("[MAINTENANCE_PREALERT] 점검 5분 전 알림 발송");

		await broadcastServerNotification(EServerEventNotificationType.SERVER_MAINTENANCE_PREVIEW, {
			minutes: 5,
		});

		logCategory("MAINTENANCE", "점검 5분 전 알림 발송 완료");
	}
}

// 점검 시작
export class CMaintenanceSchedule_Start extends CMaintenanceScheduleBase {
	constructor() {
		super("MaintenanceSchedule_Start");
	}

	protected async execute(): Promise<void> {
		// 점검 유형 결정 (수요일이면 WEEKLY, 아니면 DAILY)
		const maintenanceType = isWednesday() ? EMaintenanceType.WEEKLY : EMaintenanceType.DAILY;
		const endTime = maintenanceType === EMaintenanceType.WEEKLY ? "07:00" : "05:00";
		const message = maintenanceType === EMaintenanceType.WEEKLY ? "주간 정기점검 중입니다." : "일일 점검 중입니다.";

		logDebug(`[MAINTENANCE_START] 점검 시작 - 유형: ${maintenanceType === EMaintenanceType.WEEKLY ? "WEEKLY" : "DAILY"}`);

		// ServerInfo 상태 변경
		await updateServerStatus(EServerStatusInfo.MAINTENANCE, {
			startTime: getKoreanTimeString(),
			endTime: endTime,
			message: message,
			maintenanceType: maintenanceType,
		});

		// 점검 시작 알림 브로드캐스트
		await broadcastServerNotification(EServerEventNotificationType.SERVER_MAINTENANCE_START, {
			endTime: endTime,
		});

		logCategory("MAINTENANCE", `점검 시작 - 유형: ${maintenanceType}, 종료 예정: ${endTime}`);
	}
}

// 일일 점검 종료 (05:00)
export class CMaintenanceSchedule_End_Daily extends CMaintenanceScheduleBase {
	constructor() {
		super("MaintenanceSchedule_End_Daily");
	}

	protected async execute(): Promise<void> {
		const serverInfo = await getServerInfo();

		// 주간 점검 중이면 일일 종료 건너뜀
		if (serverInfo.maintenanceInfo?.maintenanceType === EMaintenanceType.WEEKLY) {
			logDebug("[MAINTENANCE_END_DAILY] 주간 점검 중이므로 일일 종료 건너뜀");
			return;
		}

		// 긴급 점검 중이면 종료하지 않음
		if (serverInfo.status === EServerStatusInfo.EMERGENCY) {
			logDebug("[MAINTENANCE_END_DAILY] 긴급 점검 중이므로 종료 건너뜀");
			return;
		}

		// 이미 온라인이면 건너뜀
		if (serverInfo.status === EServerStatusInfo.ONLINE) {
			logDebug("[MAINTENANCE_END_DAILY] 이미 온라인 상태이므로 종료 건너뜀");
			return;
		}

		logDebug("[MAINTENANCE_END_DAILY] 일일 점검 종료 처리");

		// ServerInfo 상태 변경
		await updateServerStatus(EServerStatusInfo.ONLINE, null);

		// 캐시 버전 증가 (점검 중 GameDB 변경 반영)
		await incrementCacheVersion();

		// 점검 종료 알림 브로드캐스트
		await broadcastServerNotification(EServerEventNotificationType.SERVER_MAINTENANCE_END, {});

		logCategory("MAINTENANCE", "일일 점검 종료 완료");
	}
}

// 주간 점검 종료 (07:00, 수요일)
export class CMaintenanceSchedule_End_Weekly extends CMaintenanceScheduleBase {
	constructor() {
		super("MaintenanceSchedule_End_Weekly");
	}

	protected async execute(): Promise<void> {
		const serverInfo = await getServerInfo();

		// 긴급 점검 중이면 종료하지 않음
		if (serverInfo.status === EServerStatusInfo.EMERGENCY) {
			logDebug("[MAINTENANCE_END_WEEKLY] 긴급 점검 중이므로 종료 건너뜀");
			return;
		}

		// 이미 온라인이면 건너뜀
		if (serverInfo.status === EServerStatusInfo.ONLINE) {
			logDebug("[MAINTENANCE_END_WEEKLY] 이미 온라인 상태이므로 종료 건너뜀");
			return;
		}

		logDebug("[MAINTENANCE_END_WEEKLY] 주간 점검 종료 처리");

		// ServerInfo 상태 변경
		await updateServerStatus(EServerStatusInfo.ONLINE, null);

		// 캐시 버전 증가 (점검 중 GameDB 변경 반영)
		await incrementCacheVersion();

		// 점검 종료 알림 브로드캐스트
		await broadcastServerNotification(EServerEventNotificationType.SERVER_MAINTENANCE_END, {});

		logCategory("MAINTENANCE", "주간 점검 종료 완료");
	}
}

// -----------------------------------------------
// Admin API 클래스들 (수동 호출)
// -----------------------------------------------

// 긴급점검 시작
export class CMaintenance_EmergencyStart extends CAdminFunctionBase {
	constructor() {
		super("Maintenance_EmergencyStart");
	}

	protected requiresAuthentication(): boolean {
		return process.env.FUNCTIONS_EMULATOR !== "true";
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			estimatedMinutes: {
				type: "number",
				required: false,
				min: 1,
				max: 1440, // 최대 24시간
			},
			message: {
				type: "string",
				required: false,
				maxLength: 500,
			},
		};
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const { estimatedMinutes = 60, message = "긴급 점검 중입니다." } = data;
		const endTime = calculateEndTime(estimatedMinutes);

		// ServerInfo 상태 변경
		await updateServerStatus(EServerStatusInfo.EMERGENCY, {
			startTime: getKoreanTimeString(),
			endTime: endTime,
			message: message,
			maintenanceType: EMaintenanceType.EMERGENCY,
		});

		// 긴급점검 알림 브로드캐스트
		await broadcastServerNotification(EServerEventNotificationType.SERVER_MAINTENANCE_START, {
			endTime: endTime,
			customMessage: `[긴급 점검] ${message} 예상 종료: ${endTime}`,
		});

		this.setResponseData("maintenanceType", "EMERGENCY");
		this.setResponseData("startTime", getKoreanTimeString());
		this.setResponseData("endTime", endTime);

		logCategory("MAINTENANCE", `긴급점검 시작 - 예상 종료: ${endTime}, 메시지: ${message}`);
		return [EResponseCode.Success, "Emergency maintenance started"];
	}
}

// 긴급점검 종료
export class CMaintenance_EmergencyEnd extends CAdminFunctionBase {
	constructor() {
		super("Maintenance_EmergencyEnd");
	}

	protected requiresAuthentication(): boolean {
		return process.env.FUNCTIONS_EMULATOR !== "true";
	}

	protected getValidationSchema(): ValidationSchema {
		return {};
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const serverInfo = await getServerInfo();

		// 긴급 점검 상태가 아니면 에러
		if (serverInfo.status !== EServerStatusInfo.EMERGENCY) {
			throwLogicError("execute(): 현재 긴급 점검 중이 아닙니다.", EAPIErrorCode.INVALID_PARAMETERS);
		}

		// ServerInfo 상태 변경
		await updateServerStatus(EServerStatusInfo.ONLINE, null);

		// 캐시 버전 증가
		await incrementCacheVersion();

		// 점검 종료 알림 브로드캐스트
		await broadcastServerNotification(EServerEventNotificationType.SERVER_MAINTENANCE_END, {
			customMessage: "[긴급 점검 종료] 서비스가 정상화되었습니다.",
		});

		this.setResponseData("endedAt", getKoreanDateTimeString());

		logCategory("MAINTENANCE", "긴급점검 종료 완료");
		return [EResponseCode.Success, "Emergency maintenance ended"];
	}
}

// 점검 연장 (모든 유형)
export class CMaintenance_Extend extends CAdminFunctionBase {
	constructor() {
		super("Maintenance_Extend");
	}

	protected requiresAuthentication(): boolean {
		return process.env.FUNCTIONS_EMULATOR !== "true";
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			extendMinutes: {
				type: "number",
				required: true,
				min: 1,
				max: 1440, // 최대 24시간
			},
			newMessage: {
				type: "string",
				required: false,
				maxLength: 500,
			},
		};
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const { extendMinutes, newMessage } = data;
		const serverInfo = await getServerInfo();

		// 현재 점검 상태 확인
		if (serverInfo.status !== EServerStatusInfo.MAINTENANCE && serverInfo.status !== EServerStatusInfo.EMERGENCY) {
			throwLogicError("execute(): 현재 점검 중이 아닙니다.", EAPIErrorCode.INVALID_PARAMETERS);
		}

		if (!serverInfo.maintenanceInfo) {
			throwLogicError("execute(): 점검 정보가 없습니다.", EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 새 종료 시간 계산 (현재 종료 시간에서 연장)
		const newEndTime = calculateEndTime(extendMinutes);

		// ServerInfo 업데이트
		const updatedMaintenanceInfo: MaintenanceInfo = {
			...serverInfo.maintenanceInfo,
			endTime: newEndTime,
			message: newMessage || serverInfo.maintenanceInfo.message,
		};

		await updateServerStatus(serverInfo.status, updatedMaintenanceInfo);

		// 연장 알림 브로드캐스트
		await broadcastServerNotification(EServerEventNotificationType.SERVER_MAINTENANCE_PREVIEW, {
			minutes: extendMinutes,
			customMessage: `[점검 연장] 점검이 ${extendMinutes}분 연장되었습니다. 새로운 종료 시간: ${newEndTime}`,
		});

		this.setResponseData("extendedMinutes", extendMinutes);
		this.setResponseData("newEndTime", newEndTime);

		logCategory("MAINTENANCE", `점검 연장 - ${extendMinutes}분 연장, 새 종료 시간: ${newEndTime}`);
		return [EResponseCode.Success, `Maintenance extended by ${extendMinutes} minutes`];
	}
}

// -----------------------------------------------
// 테스트용 HTTP 함수 export
// -----------------------------------------------

export const Test_MaintenanceSchedule_Start = https.onRequest({ timeoutSeconds: 60 }, async (_req, res) => {
	await new CMaintenanceSchedule_Start().executeFunction();
	res.send("Test_MaintenanceSchedule_Start: 점검 시작 처리 완료");
});

export const Test_MaintenanceSchedule_End_Daily = https.onRequest({ timeoutSeconds: 60 }, async (_req, res) => {
	await new CMaintenanceSchedule_End_Daily().executeFunction();
	res.send("Test_MaintenanceSchedule_End_Daily: 일일 점검 종료 처리 완료");
});
