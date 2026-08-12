import { CoreDataFactory } from "../Factory/CoreDataFactory";
import { GameDBFactory } from "../Factory/GameDBFactory";
import { ConstKey } from "../../Data/Constants/ServerConstants";
import { EResponseCode, resultSuccess } from "../../Data/Types/ResponseCodes";
import { getSessionSecret, getFunctionCallSecret } from "../../Utility/SecretManager";
import * as utilBasic from "../../Utility/UtilityBasic";
import { ECurrency, EAPIErrorCode } from "../../Data/Generated/CommonEnum";
import { CLogEvent } from "../LogEvent/LogEvent";
import { CustomThrow, getLastPuidData, setLastPuidData, throwLogicError } from "../../Utility/UtilityBasic";
import { logger } from "firebase-functions/v2";
import * as crypto from "crypto";
import { ValidationSchema, validateGenericData } from "../../Utility/InputValidation";
import { ServerInfo, EServerStatusInfo } from "../../Data/Shared/Types/ServerStatusTypes";
import * as admin from "firebase-admin";

// ServerInfo 메모리 캐시 (인스턴스 수명 동안 유지, TTL로 갱신)
let cachedServerStatus: typeof EServerStatusInfo[keyof typeof EServerStatusInfo] | null = null;
let cachedServerStatusAt: number = 0;
const SERVER_STATUS_CACHE_TTL_MS = 60000; // 60초

// 서버 상태 캐시 초기화 (테스트 환경에서 사용)
export function resetServerStatusCache(): void {
	cachedServerStatus = null;
	cachedServerStatusAt = 0;
}

// -----------------------------------------------
// Request Class
// -----------------------------------------------
// #region CRequestBase
export abstract class CRequestBase {
	UUID: string | undefined;
	gameDB: GameDBFactory;
	coreData: CoreDataFactory;
	resultData: Map<string, any>;
	logEvent: CLogEvent;
	result: [EResponseCode, string];
	isEditorDebug: boolean;
	serverID: string;
	protected AdminAuth = false; // 관리자 모드. 인증 필요 없음
	protected RequireSessionKey = true; // 세션키 검증 필요 여부 (기본값: true)
	protected RequireFunctionCallKey = true; // 함수 호출키 검증 필요 여부 (기본값: true)
	protected isTestEnvironment = false; // 테스트 환경 여부

	constructor() {
		// 테스트 환경 자동 감지 및 인증 요구사항 비활성화
		this.isTestEnvironment = process.env.FUNCTIONAL_TEST === "unittest" || process.env.NODE_ENV === "test";
		if (this.isTestEnvironment) {
			this.RequireSessionKey = false;
			this.RequireFunctionCallKey = false;
		}

		this.gameDB = new GameDBFactory();
		this.coreData = new CoreDataFactory();
		this.resultData = new Map<string, any>();
		this.logEvent = new CLogEvent();
		this.isEditorDebug = false;
	}

	// 서버 ID
	public getServerID(): string {
		return this.serverID;
	}

	// 공통 인자로 새 인스턴스를 생성하는 헬퍼 메서드
	protected createInternalRequest<T extends CRequestBase>(TargetClass: new (init?: any) => T): T {
		return new TargetClass({
			UUID: this.UUID,
			serverID: this.serverID,
			gameDB: this.gameDB,
			coreData: this.coreData,
		});
	}

	// -----------------------------------------------
	// 범용 Validation 시스템
	// -----------------------------------------------

	/**
	 * 각 클래스에서 오버라이드하여 validation 스키마를 정의
	 * 스키마가 정의되지 않으면 validation을 수행하지 않음
	 */
	protected getValidationSchema(): ValidationSchema | null {
		return null;
	}

	/**
	 * validation 최대 크기 설정 (기본 10KB)
	 * 각 클래스에서 오버라이드 가능
	 */
	protected getMaxRequestSize(): number {
		return 10000;
	}

	/**
	 * 입력 데이터 validation 수행
	 * @param data 검증할 데이터
	 * @returns 검증된 데이터
	 */
	protected validateInputData(data: any): any {
		const schema = this.getValidationSchema();
		if (!schema) {
			// 스키마가 정의되지 않으면 validation을 수행하지 않음
			return data;
		}

		const maxSize = this.getMaxRequestSize();
		return validateGenericData(data, schema, maxSize);
	}

	/**
	 * 테스트 모드 설정
	 * @param enableTestMode true: 테스트에서 모드 활성화(키 검증 활성화)
	 */
	public setTestKeyMode(enableTestMode: boolean = true): void {
		this.RequireSessionKey = enableTestMode;
		this.RequireFunctionCallKey = enableTestMode;
	}

	addToCreateCoreData(prop: any, constKey: string) {
		this.coreData.addToCreateList(prop, constKey);
	}

	excludeJson(propName: string) {
		this.coreData.excludeJson(propName);
	}

	// this.excludeJsonKeys(ExcludeCoreDataKey.ACCOUNT, "RateLimitData");
	excludeJsonKeys(propName: string, keys: ECurrency | ECurrency[] | string) {
		const stringKeys: string[] = [];

		if (Array.isArray(keys)) {
			keys.forEach((key) => {
				stringKeys.push(key.toString());
			});
		} else {
			stringKeys.push(keys.toString());
		}

		this.coreData.excludeJsonKeys(propName, stringKeys);
	}

	// #region handleRequest
	/**
	 *
	 * 요청을 받아 최종 JSON 응답까지 반환하는 메인 함수
	 * @param data - 요청 또는 내부 API 호출 시 전달되는 데이터
	 * @param context - Firebase Context 등 인증 정보
	 * @param existResultData  Request가 아닌 api 내부에서 호출된 케이스로
	 * 						  이 때는 resultData를 새로 만들지 않고 SUCCESSKEY도 만들지 않는다.
	 */
	async handleRequest(data: any, context: any, existResultData?: Map<string, any>): Promise<string> {
		try {
			// 에디터, 디버그 모드
			this.isEditorDebug = data?.IsEditorDebug === true;

			// 내부 호출용 processRequest()를 호출한 후, 결과 Map을 JSON 문자열로 변환하여 반환
			const resultMap = await this.processRequest(data, context, existResultData);
			return utilBasic.strMapToJsonWithFuncTimeMS(resultMap, !existResultData);
		} catch (error) {
			const errorResultMap = this.handleError(error);
			return utilBasic.strMapToJsonWithFuncTimeMS(errorResultMap, !existResultData);
		}
	}

	// 메인 실행 함수. (내부 호출용)
	async processRequest(data: any, context: any, existResultData?: Map<string, any>): Promise<Map<string, any>> {
		// existResultData가 있으면 내부 API 호출로 간주.
		const callFromRequest = !existResultData;

		// 실제 Request를 통해 호출된 경우만 초기화와 인증 검증 등을 진행
		if (!callFromRequest) {
			// 내부 API 호출: 호출자의 resultData를 그대로 사용
			this.resultData = existResultData!;
		}

		if (callFromRequest) {
			this.resultData = utilBasic.createResultDataMap();
			this.serverID = data.serverID;

			// PacketUID 검증
			utilBasic.verifyData_PacketUID(data, this.resultData);
			const packetUID = this.resultData.get(ConstKey.PACKETUIDKEY);

			// User 인증 검증
			if (this.AdminAuth) {
				this.UUID = "Admin";
			} else {
				utilBasic.verifyData_UserAuth(context, this.resultData);
				this.UUID = context.auth?.uid;

				// PUI 중복 체크 및 Rate Limit 체크
				const clientIP = this.getClientIP(context);
				const rateLimitChecks = [
					{
						identifier: `ip:${clientIP}`,
						maxRequests: 500,
						windowMs: 60000, // 1분
					},
					{
						identifier: `user:${this.UUID}`,
						maxRequests: 100,
						windowMs: 60000, // 1분
					},
				];

				const validationResult = await utilBasic.validateRequestWithRateLimit(this.UUID, packetUID, rateLimitChecks, this.getServerID());
				if (validationResult.isDuplicate && validationResult.lastPuidResponseJson) {
					return new Map<string, any>(Object.entries(validationResult.lastPuidResponseJson));
				}
			}
		}

		// 코어 데이터 및 GameDB 초기화
		await this.setUp();
		await this.gameDB.createInstance();
		await this.coreData.createInstance(this.UUID, this.getServerID());

		// 외부 요청(Request)일 때만 _datasOriginal 스냅샷.
		// datas가 이미 설정되어 있지만 loadRequestObject를 거치지 않은 경우(테스트 등)를 위해 필요.
		// 내부 API 호출 시에는 스냅샷하지 않는다 — 신규 생성 데이터가 "변경 없음"으로 판정되는 것을 방지.
		if (callFromRequest) {
			this.coreData.snapshotOriginal();
		}

		// 세션키 및 함수 호출키 검증. FunctionCallKey 갱신
		if (callFromRequest && !this.AdminAuth) {
			await this.verifySessionAndFunctionCallKey(data);
		}

		// 입력 데이터 validation 수행
		const validatedData = this.validateInputData(data);

		// 서버 상태 체크
		if (callFromRequest && !this.AdminAuth) {
			const shouldProceed = await this.checkServerStatus();
			if (!shouldProceed) {
				// 서버 상태로 인한 정상 실패 응답
				this.resultData.set(ConstKey.SUCCESSKEY, false);
				this.resultData.set(ConstKey.RESPONSEKEY, "서버 점검 중입니다. 잠시 후 다시 시도해주세요.");
				return this.resultData;
			}
		}

		// 실제 비즈니스 로직(자식 클래스에서 오버라이드하여 사용)
		const [responseCode, responseMessage] = await this.execute(validatedData);

		if (responseCode === EResponseCode.Success) {
			// API 호출 성공 시 LastActivityTime 갱신 (일관성 있는 규칙)
			if (callFromRequest && !this.AdminAuth && this.coreData.datas.account) {
				this.coreData.datas.account.LoginInfo.LastActivityTime = utilBasic.currentServerTime().toMillis();
			}
			this.resultData.set(ConstKey.SUCCESSKEY, true);
			// Request일 때만 호출한다. 내부 api 호출일 때는 안쓰게 한다.
			if (callFromRequest) {
				// updateToDB(Firestore WRITE)와 objectDiffToResultData(CPU)는 독립적 → 병렬
				await Promise.all([
					this.updateToDB(),
					this.coreData.objectDiffToResultData(this.resultData),
				]);

				// recordEvents와 setLastPuidData도 병렬 (둘 다 objectDiff 완료 후 실행)
				await Promise.all([
					this.logEvent.recordEvents(this.getServerID()),
					(!this.AdminAuth) ? setLastPuidData(this.UUID, this.resultData, this.getServerID()) : Promise.resolve(),
				]);
			}
		} else {
			this.resultData.set(ConstKey.SUCCESSKEY, false);
			this.resultData.set(ConstKey.RESPONSEKEY, responseMessage);
		}

		return this.resultData;
	}

	async setUp() {}
	async execute(data: any): Promise<[EResponseCode, string]> {
		return resultSuccess;
	}

	async updateToDB() {
		const updateStrMap = await this.coreData.updateToUserDBOnlyDiff();

		await utilBasic.updateUserDocumentData(this.UUID, updateStrMap, this.getServerID());
	}

	// #region handleError
	handleError(error: unknown): Map<string, any> {
		this.resultData.set(ConstKey.SUCCESSKEY, false);

		if (error instanceof CustomThrow) {
			// 논리적 오류("logic")는 RESPONSEKEY에, 시스템 오류("system")는 ERRORKEY에 메시지를 저장합니다.
			if (error.errorType === "logic") {
				// 개발환경에서만 console 출력
				utilBasic.logError("[PX_ERROR_LOGIC]", error.stack);

				// Logic 에러도 Firebase Error Reporting으로 전송
				logger.error(error.message, {
					stack: error.stack,
					errorCode: error.errorCode,
					errorType: "logic",
					functionName: this.constructor.name,
					serverID: this.serverID,
					userId: this.UUID,
					timestamp: new Date().toISOString(),
				});

				this.resultData.set(ConstKey.RESPONSEKEY, error.message);
			} else {
				// System 에러는 Error Reporting으로 전송
				// 개발환경에서만 console 출력
				utilBasic.logError("[PX_ERROR_SYSTEM]", error.stack);

				// Firebase Error Reporting으로 전송
				logger.error(error.message, {
					stack: error.stack,
					errorCode: error.errorCode,
					errorType: "system",
					functionName: this.constructor.name,
					serverID: this.serverID,
					userId: this.UUID,
					timestamp: new Date().toISOString(),
				});

				this.resultData.set(ConstKey.ERRORKEY, error.message);
			}
		} else if (error instanceof Error) {
			// 예기치 않은 JS Error도 시스템 오류로 처리
			// 개발환경에서만 console 출력
			utilBasic.logError("[UNEXPECTED_ERROR]", error.stack);

			// Firebase Error Reporting으로 전송
			logger.error(error.message, {
				stack: error.stack,
				errorType: "unexpected",
				functionName: this.constructor.name,
				serverID: this.serverID,
				userId: this.UUID,
				timestamp: new Date().toISOString(),
			});

			this.resultData.set(ConstKey.ERRORKEY, error.message);
		} else {
			// 문자열, 숫자, 또는 단순 객체 등 Error 객체가 아닌 값이 throw된 경우
			// 개발환경에서만 console 출력
			utilBasic.logError("[UNEXPECTED_ERROR]", error);

			// Firebase Error Reporting으로 전송
			logger.error(String(error), {
				errorType: "unexpected",
				thrownValue: String(error),
				userId: this.UUID,
				functionName: this.constructor.name,
				serverID: this.serverID,
				isAuthenticated: !this.AdminAuth,
				timestamp: new Date().toISOString(),
			});

			this.resultData.set(ConstKey.ERRORKEY, String(error));
		}

		return this.resultData;
	}

	// #region SessionKey, FunctionCallKey 관리
	// -----------------------------------------------
	// SessionKey, FunctionCallKey 관리
	// -----------------------------------------------
	async verifySessionAndFunctionCallKey(data: any) {
		// 세션키 검증
		if (this.RequireSessionKey) {
			const sessionKey = data[ConstKey.SESSION_KEY];
			if (!sessionKey) {
				utilBasic.throwLogicError("verifySessionAndFunctionCallKey(): Session key is required", EAPIErrorCode.AUTHENTICATION_FAILED);
			}

			// 세션키 검증 (만료시간 체크 없이 해시만 검증)
			if (this.coreData.datas.account.HashInfo.SessionKey !== sessionKey || !CRequestBase.verifySessionKey(this.UUID, sessionKey)) {
				utilBasic.throwLogicError("verifySessionAndFunctionCallKey(): Invalid session", EAPIErrorCode.AUTHENTICATION_FAILED);
			}
		}

		// 함수 호출키 검증 및 원자적 갱신
		if (this.RequireFunctionCallKey) {
			const functionCallKey = data[ConstKey.FUNCTION_CALL_KEY];
			if (!functionCallKey) {
				utilBasic.throwLogicError("verifySessionAndFunctionCallKey(): Function call key is required", EAPIErrorCode.AUTHENTICATION_FAILED);
			}

			// 키 검증
			if (this.coreData.datas.account.HashInfo.FunctionCallKey !== functionCallKey) {
				utilBasic.throwLogicError(`verifySessionAndFunctionCallKey(): [FUNCTION_CALL_KEY] Invalid key. Expected: ${this.coreData.datas.account.HashInfo.FunctionCallKey}, Received: ${functionCallKey}`, EAPIErrorCode.AUTHENTICATION_FAILED);
			}

			// 검증 성공 시 즉시 새 키 생성 및 설정 (원자성 보장)
			const newFunctionCallKey = CRequestBase.generateFunctionCallKey(this.UUID);
			this.coreData.datas.account.HashInfo.FunctionCallKey = newFunctionCallKey;
		}
	}

	// Rate Limiting 체크 (최적화된 단일 읽기/쓰기)
	protected async checkRateLimit(context: any): Promise<void> {
		// 개발 환경에서는 Rate Limit 비활성화
		if (utilBasic.isEmulatorEnvironment()) {
			return;
		}

		if (this.UUID) {
			const clientIP = this.getClientIP(context);
			const userUUID = this.UUID;

			// 여러 Rate Limit 체크를 한 번에 처리 (최적화)
			await utilBasic.checkMultipleRateLimits(
				userUUID,
				[
					{
						identifier: `ip:${clientIP}`,
						maxRequests: 500,
						windowMs: 60000, // 1분
					},
					{
						identifier: `user:${userUUID}`,
						maxRequests: 100,
						windowMs: 60000, // 1분
					},
				],
				this.getServerID()
			);
		}
	}

	/**
	 * 클라이언트 IP 주소 추출
	 * @param context Firebase Functions context
	 * @returns IP 주소 문자열
	 */
	private getClientIP(context: any): string {
		// Firebase Functions의 경우 rawRequest에서 IP를 가져올 수 있음
		const request = context?.rawRequest;

		if (request) {
			return request.headers?.["x-forwarded-for"] || request.headers?.["x-real-ip"] || request.connection?.remoteAddress || request.socket?.remoteAddress || "unknown";
		}

		return "unknown";
	}

	public static verifySessionKey(uuid: string, sessionKey: string): boolean {
		if (!uuid || !sessionKey) {
			return false;
		}

		const parts = sessionKey.split(".");
		if (parts.length !== 2) {
			return false;
		}

		const timestamp = parseInt(parts[0]);
		const providedHash = parts[1];

		// 해시 검증
		const data = `${uuid}:${timestamp}:${getSessionSecret()}`;
		const expectedHash = crypto.createHash("sha256").update(data).digest("hex");

		return expectedHash === providedHash;
	}

	/**
	 * 세션키와 함수 호출키 모두 새로 생성
	 * @param uuid 사용자 UUID
	 */
	public static async generateNewAllKeys(uuid: string): Promise<{ sessionKey: string; functionCallKey: string }> {
		if (!uuid || uuid.length === 0) {
			utilBasic.throwLogicError("generateNewAllKeys(): UUID is required for session keys refresh", EAPIErrorCode.AUTHENTICATION_FAILED);
		}

		const sessionKey = CRequestBase.generateSessionKey(uuid);
		const functionCallKey = CRequestBase.generateFunctionCallKey(uuid);

		return { sessionKey, functionCallKey };
	}

	public static generateSessionKey(uuid: string): string {
		if (!uuid || uuid.length === 0) {
			utilBasic.throwLogicError("generateSessionKey(): UUID is required for session key generation", EAPIErrorCode.AUTHENTICATION_FAILED);
		}

		const timestamp = Date.now(); // Unix timestamp (milliseconds)
		const data = `${uuid}:${timestamp}:${getSessionSecret()}`;
		const hash = crypto.createHash("sha256").update(data).digest("hex");

		// 타임스탬프를 포함한 세션키 형태: timestamp.hash
		return `${timestamp}.${hash}`;
	}

	public static generateFunctionCallKey(uuid: string): string {
		if (!uuid || uuid.length === 0) {
			utilBasic.throwLogicError("generateFunctionCallKey(): UUID is required for function call key generation", EAPIErrorCode.AUTHENTICATION_FAILED);
		}

		const timestamp = Date.now();
		const randomValue = Math.random().toString(36).substring(2, 15);
		const data = `${uuid}:${timestamp}:${randomValue}:${getFunctionCallSecret()}`;
		const hash = crypto.createHash("sha256").update(data).digest("hex");

		return hash;
	}

	/**
	 * 서버 상태를 체크하여 요청 처리 여부를 결정
	 * @returns 처리해도 되는지 여부
	 */
	protected async checkServerStatus(): Promise<boolean> {
		const now = Date.now();
		// 캐시가 유효하면 Firestore 읽기 없이 반환
		if (cachedServerStatus !== null && (now - cachedServerStatusAt) < SERVER_STATUS_CACHE_TTL_MS) {
			return cachedServerStatus === EServerStatusInfo.ONLINE;
		}

		const db = admin.firestore();
		// 테스트 환경에서는 테스트 전용 문서 ID 사용
		const docId = process.env.FUNCTIONAL_TEST === "unittest" ? "ServerInfo_Test" : "ServerInfo";
		const serverInfoDoc = await db.collection("ServerInfo").doc(docId).get();

		if (!serverInfoDoc.exists) {
			throwLogicError(`checkServerStatus(): [SERVER_STATUS] ${this.constructor.name} - ServerInfo 문서가 없음, 실행 진행`, EAPIErrorCode.DATABASE_ERROR);
		}

		const serverInfo = serverInfoDoc.data() as ServerInfo;
		cachedServerStatus = serverInfo.status;
		cachedServerStatusAt = now;

		switch (cachedServerStatus) {
			case EServerStatusInfo.ONLINE:
				return true;

			case EServerStatusInfo.MAINTENANCE:
			case EServerStatusInfo.EMERGENCY:
			case EServerStatusInfo.OFFLINE:
				return false;

			default:
				throwLogicError(`throwLogicError(): [SERVER_STATUS] ${this.constructor.name} - 알 수 없는 서버 상태(${cachedServerStatus}), 실행 진행`, EAPIErrorCode.DATABASE_ERROR);
		}
	}

	// #endregion
}
