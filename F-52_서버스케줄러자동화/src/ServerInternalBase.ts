import { ServerInfo, EServerStatusInfo } from "../../Data/Shared/Types/ServerStatusTypes";
import { throwLogicError, logCategory, logError, logDebug } from "../../Utility/UtilityBasic";
import * as admin from "firebase-admin";
import { EAPIErrorCode } from "../../Data/Generated/CommonEnum";
import { logger } from "firebase-functions/v2";

// -----------------------------------------------
// 서버 자체 호출 함수들을 위한 Base 클래스
// -----------------------------------------------

/**
 * Firebase onSchedule(크론), onRequest(테스트) 등 서버에서 자체적으로 호출되는 함수들을 위한 기본 클래스
 * 서버 상태 체크, 로깅, 예외 처리를 일관성 있게 제공
 */
export abstract class CServerInternalBase {
	protected functionName: string;
	protected allowInMaintenance: boolean = false; // 점검 중에도 실행할지 여부
	protected allowInEmergency: boolean = false; // 긴급 점검 중에도 실행할지 여부

	constructor(functionName?: string) {
		this.functionName = functionName || this.constructor.name;
	}

	/**
	 * 서버 내부 함수 실행의 메인 진입점
	 * 서버 상태 체크 후 실제 로직 실행
	 */
	public async executeFunction(): Promise<void> {
		const startTime = Date.now();

		try {
			// 서버 상태 체크
			const shouldProceed = await this.checkServerStatus();
			if (!shouldProceed) {
				logDebug(`[SERVER_INTERNAL_SKIP] ${this.functionName} - 서버 상태로 인해 실행 건너뛰기`);
				return;
			}

			// 실제 로직 실행
			await this.execute();
		} catch (error) {
			const executionTime = Date.now() - startTime;
			await this.handleError(error, executionTime);
		}
	}

	/**
	 * 서버 상태를 체크하여 스케줄 실행 여부를 결정
	 * @returns 실행해도 되는지 여부
	 */
	protected async checkServerStatus(): Promise<boolean> {
		const db = admin.firestore();
		// 테스트 환경에서는 테스트 전용 문서 ID 사용
		const docId = process.env.FUNCTIONAL_TEST === "unittest" ? "ServerInfo_Test" : "ServerInfo";
		const serverInfoDoc = await db.collection("ServerInfo").doc(docId).get();

		if (!serverInfoDoc.exists) {
			throwLogicError(`checkServerStatus(): [SERVER_INTERNAL_STATUS] ${this.functionName} - ServerInfo 문서가 없음, 실행 진행`, EAPIErrorCode.DATABASE_ERROR);
		}

		const serverInfo = serverInfoDoc.data() as ServerInfo;
		const currentStatus = serverInfo.status;

		switch (currentStatus) {
			case EServerStatusInfo.ONLINE:
				return true;

			case EServerStatusInfo.MAINTENANCE:
				if (this.allowInMaintenance) {
					logDebug(`[SERVER_INTERNAL_STATUS] ${this.functionName} - 정기점검 중이지만 실행 허용됨`);
					return true;
				}
				logDebug(`[SERVER_INTERNAL_STATUS] ${this.functionName} - 정기점검 중으로 실행 중단`);
				return false;

			case EServerStatusInfo.EMERGENCY:
				if (this.allowInEmergency) {
					logDebug(`[SERVER_INTERNAL_STATUS] ${this.functionName} - 긴급점검 중이지만 실행 허용됨`);
					return true;
				}
				logDebug(`[SERVER_INTERNAL_STATUS] ${this.functionName} - 긴급점검 중으로 실행 중단`);
				return false;

			case EServerStatusInfo.OFFLINE:
				logDebug(`[SERVER_INTERNAL_STATUS] ${this.functionName} - 서버 오프라인으로 실행 중단`);
				return false;

			default:
				throwLogicError(`logDebug(): [SERVER_INTERNAL_STATUS] ${this.functionName} - 알 수 없는 서버 상태(${currentStatus}), 실행 진행`, EAPIErrorCode.DATABASE_ERROR);
		}
	}

	/**
	 * 에러 처리 및 로깅
	 * @param error 발생한 에러
	 * @param executionTime 실행 시간
	 */
	protected async handleError(error: any, executionTime: number): Promise<void> {
		const errorMessage = error instanceof Error ? error.message : String(error);
		const errorStack = error instanceof Error ? error.stack : undefined;
		const errorCode = error.errorCode || EAPIErrorCode.SERVER_ERROR;
		const errorType = error.constructor?.name || "UnknownError";

		// 콘솔 로깅
		logError(`[SERVER_INTERNAL_ERROR] ${this.functionName} 실행 실패 (${executionTime}ms):`, errorMessage);
		if (errorStack) {
			logError(`[SERVER_INTERNAL_ERROR_STACK] ${this.functionName}:`, errorStack);
		}

		// Firebase Error Reporting으로 구조화된 에러 정보 전송
		logger.error("ServerInternalBase Function Error", {
			errorCode: errorCode,
			errorType: errorType,
			message: errorMessage,
			stack: errorStack,
			functionName: this.functionName,
			executionTime: executionTime,
			timestamp: new Date().toISOString(),
		});
	}

	/**
	 * 각 서버 내부 함수 클래스에서 구현해야 하는 실제 로직
	 */
	protected abstract execute(): Promise<void>;

	/**
	 * 점검 중에도 실행 허용 설정
	 * @param allowMaintenance 정기점검 중 실행 허용 여부
	 * @param allowEmergency 긴급점검 중 실행 허용 여부
	 */
	protected setMaintenancePolicy(allowMaintenance: boolean = false, allowEmergency: boolean = false): void {
		this.allowInMaintenance = allowMaintenance;
		this.allowInEmergency = allowEmergency;
	}

	/**
	 * 서버 내부 함수 생성 헬퍼 - index.ts에서 사용
	 * @param internalClass 서버 내부 함수 클래스
	 * @returns 실행 가능한 함수
	 */
	public static createInternalFunction<T extends CServerInternalBase>(internalClass: new () => T): () => Promise<void> {
		return async () => {
			const instance = new internalClass();
			await instance.executeFunction();
		};
	}
}
