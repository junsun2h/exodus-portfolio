import { https } from "firebase-functions/v2";
import { CFunctionBase } from "../Base/FunctionBase";
import { ValidationSchema } from "../../Utility/InputValidation";
import { EResponseCode, resultSuccess } from "../../Data/Types/ResponseCodes";
import { EAPIErrorCode } from "../../Data/Generated/CommonEnum";
import { throwLogicError, logCategory } from "../../Utility/UtilityBasic";
import * as admin from "firebase-admin";
import { Timestamp } from "firebase-admin/firestore";
import * as tls from "tls";
import * as crypto from "crypto";

// -----------------------------------------------
// Firebase Functions Export (파일 최상단)
// -----------------------------------------------

export async function request_Security_VerifyAppSignature(request: https.CallableRequest<any>) {
	return await new CSecurityVerifyAppSignature().handleRequest(request);
}

export async function request_Security_ReportThreat(request: https.CallableRequest<any>) {
	return await new CSecurityReportThreat().handleRequest(request);
}

export async function request_Security_InitConfig(request: https.CallableRequest<any>) {
	return await new CSecurityInitConfig().handleRequest(request);
}

export async function request_Security_UpdateConfig(request: https.CallableRequest<any>) {
	return await new CSecurityUpdateConfig().handleRequest(request);
}

export async function request_Security_GetConfig(request: https.CallableRequest<any>) {
	return await new CSecurityGetConfig().handleRequest(request);
}

// -----------------------------------------------
// Constants
// -----------------------------------------------

// Firestore 경로 (ServerInfo 하위로 통합)
const SERVER_INFO_COLLECTION = "ServerInfo";

// 테스트 환경에서는 테스트용 문서 사용
function getSecurityConfigDocId(): string {
	return process.env.FUNCTIONAL_TEST === "unittest" ? "SecurityConfig_Test" : "SecurityConfig";
}

// 테스트 환경에서는 테스트용 ServerInfo 문서 사용 (프로덕션 문서 오염 방지)
function getServerInfoDocId(): string {
	return process.env.FUNCTIONAL_TEST === "unittest" ? "ServerInfo_Test" : "ServerInfo";
}

// ThreatLogs 서브컬렉션 (ServerInfo/SecurityConfig/ThreatLogs)
const THREAT_LOGS_SUBCOLLECTION = "ThreatLogs";

// -----------------------------------------------
// Types
// -----------------------------------------------

// 보안 설정 인터페이스 (Phase 4: 단일 해시)
interface SecurityConfig {
	// 현재 유효한 APK 서명 해시 (단일 값)
	currentSignatureHash: string;
	// 서명 검증 활성화 여부
	signatureVerificationEnabled: boolean;
	// 마지막 업데이트 시간
	lastUpdated: Timestamp;
	// 동적 해킹 도구 패키지 목록
	hackingToolPackages: string[];
	// 동적 인증서 핀닝 해시 목록
	certificatePinningHashes: string[];
}

// ServerInfo 버전 정보 인터페이스
interface ServerVersionInfo {
	currentVersion: string;
	minimumVersion: string;
	reviewVersion: string;
	developVersion: string;
}

// 보안 위협 유형
const ESecurityThreatType = {
	None: 0,
	SpeedHack: 100,
	MemoryManipulation: 200,
	InjectionDetected: 300,
	TimeCheating: 400,
	InvalidSignature: 500,
	RootDetected: 600,
	EmulatorDetected: 700,
	HackingToolDetected: 800,
} as const;

type ESecurityThreatTypeValue = (typeof ESecurityThreatType)[keyof typeof ESecurityThreatType];

// 보안 위협 로그 인터페이스
interface SecurityThreatLog {
	uuid: string;
	threatType: ESecurityThreatTypeValue;
	threatDetails: string;
	deviceInfo: string;
	appVersion: string;
	timestamp: Timestamp;
	signatureHash: string;
}

// -----------------------------------------------
// Utility Functions
// -----------------------------------------------

// 버전 비교 함수 (x.x.x 형식)
// 반환: 0(같음), 1(v1 > v2), -1(v1 < v2)
function compareVersion(v1: string, v2: string): number {
	const parts1 = v1.split(".").map((p) => parseInt(p, 10) || 0);
	const parts2 = v2.split(".").map((p) => parseInt(p, 10) || 0);

	const maxLen = Math.max(parts1.length, parts2.length);
	for (let i = 0; i < maxLen; i++) {
		const p1 = parts1[i] || 0;
		const p2 = parts2[i] || 0;
		if (p1 > p2) return 1;
		if (p1 < p2) return -1;
	}
	return 0;
}

// -----------------------------------------------
// Class Implementations
// -----------------------------------------------

// APK 서명 검증 클래스 (Phase 4: 버전 기반 자동 등록)
class CSecurityVerifyAppSignature extends CFunctionBase {
	private verificationResult: boolean = false;
	private message: string = "";

	constructor() {
		super("CSecurityVerifyAppSignature");
	}

	// Security API는 PacketUID 검증 및 서버 상태 체크 스킵
	protected isAdminAuth(): boolean {
		return true;
	}

	protected getValidationSchema(): ValidationSchema | null {
		return {
			signatureHash: { type: "string", required: true },
			appVersion: { type: "string", required: true },
			deviceInfo: { type: "string", required: false },
		};
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const signatureHash = data["signatureHash"] as string;
		const appVersion = data["appVersion"] as string;
		const deviceInfo = (data["deviceInfo"] as string) || "";

		// 입력 검증
		if (!signatureHash || signatureHash.length === 0) {
			throwLogicError("execute(): signatureHash is required", EAPIErrorCode.INVALID_PARAMETERS);
		}
		if (!appVersion || appVersion.length === 0) {
			throwLogicError("execute(): appVersion is required", EAPIErrorCode.INVALID_PARAMETERS);
		}

		const db = admin.firestore();

		// 1. 보안 설정 로드
		const securityConfig = await this.loadSecurityConfig(db);

		// 서명 검증 비활성화 상태면 통과
		if (!securityConfig.signatureVerificationEnabled) {
			logCategory("SECURITY", `[SECURITY_VERIFY] 서명 검증 비활성화 상태 - 통과 처리`);
			this.verificationResult = true;
			this.message = "Verification disabled";
			return resultSuccess;
		}

		// 2. ServerInfo에서 currentVersion 조회
		const serverVersion = await this.loadServerVersion(db);
		const currentVersion = serverVersion.currentVersion;

		// 3. 버전 검증: appVersion <= currentVersion
		if (compareVersion(appVersion, currentVersion) > 0) {
			// 미래 버전 - 거부
			logCategory("SECURITY", `[SECURITY_VERIFY] 미래 버전 감지: ${appVersion} > ${currentVersion}`);

			// 필드에 없는 정보만 기록: 서버 현재 버전
			const futureVersionDetails = `[미래 버전 감지] 서버 현재 버전: ${currentVersion}`;

			await this.logSecurityThreat(db, {
				uuid: request.auth?.uid || "anonymous",
				threatType: ESecurityThreatType.InvalidSignature,
				threatDetails: futureVersionDetails,
				deviceInfo: deviceInfo,
				appVersion: appVersion,
				timestamp: Timestamp.now(),
				signatureHash: signatureHash,
			});

			this.verificationResult = false;
			this.message = "Invalid version";
			return resultSuccess;
		}

		// 4. 해시 검증
		if (!securityConfig.currentSignatureHash || securityConfig.currentSignatureHash.length === 0) {
			// 첫 등록: 해시가 비어있으면 자동 등록
			logCategory("SECURITY", `[SECURITY_VERIFY] 첫 접속 - 해시 자동 등록: ${signatureHash.substring(0, 16)}...`);

			await this.registerSignatureHash(db, signatureHash);

			this.verificationResult = true;
			this.message = "Hash registered";
			return resultSuccess;
		}

		// 해시 비교
		if (securityConfig.currentSignatureHash === signatureHash) {
			logCategory("SECURITY", `[SECURITY_VERIFY] 서명 검증 성공`);
			this.verificationResult = true;
			this.message = "Valid signature";
		} else {
			// 해시 불일치 - 변조 의심
			const expectedHash = securityConfig.currentSignatureHash;
			logCategory("SECURITY", `[SECURITY_VERIFY] 서명 불일치: expected=${expectedHash.substring(0, 16)}..., got=${signatureHash.substring(0, 16)}...`);

			// 필드에 없는 정보만 기록: 등록된 해시, 서버 현재 버전
			const mismatchDetails = `[APK 서명 불일치] 등록된 해시: ${expectedHash}, 서버 버전: ${currentVersion}`;

			await this.logSecurityThreat(db, {
				uuid: request.auth?.uid || "anonymous",
				threatType: ESecurityThreatType.InvalidSignature,
				threatDetails: mismatchDetails,
				deviceInfo: deviceInfo,
				appVersion: appVersion,
				timestamp: Timestamp.now(),
				signatureHash: signatureHash,
			});

			this.verificationResult = false;
			this.message = "Invalid signature";
		}

		return resultSuccess;
	}

	protected getResponseData(): Record<string, any> | null {
		return {
			isValid: this.verificationResult,
			message: this.message,
		};
	}

	// 보안 설정 로드
	private async loadSecurityConfig(db: admin.firestore.Firestore): Promise<SecurityConfig> {
		const docRef = db.collection(SERVER_INFO_COLLECTION).doc(getSecurityConfigDocId());
		const doc = await docRef.get();

		if (!doc.exists) {
			logCategory("SECURITY", `[SECURITY_CONFIG] SecurityConfig 문서 없음 - 기본값 사용`);
			return {
				currentSignatureHash: "",
				signatureVerificationEnabled: false,
				lastUpdated: Timestamp.now(),
				hackingToolPackages: [],
				certificatePinningHashes: [],
			};
		}

		return doc.data() as SecurityConfig;
	}

	// ServerInfo에서 버전 정보 로드
	private async loadServerVersion(db: admin.firestore.Firestore): Promise<ServerVersionInfo> {
		const docRef = db.collection(SERVER_INFO_COLLECTION).doc(getServerInfoDocId());
		const doc = await docRef.get();

		if (!doc.exists) {
			throwLogicError("loadServerVersion(): ServerInfo document not found", EAPIErrorCode.DATABASE_ERROR);
		}

		const data = doc.data();
		if (!data || !data.version) {
			throwLogicError("loadServerVersion(): version field not found", EAPIErrorCode.DATABASE_ERROR);
		}

		return data.version as ServerVersionInfo;
	}

	// 서명 해시 자동 등록
	private async registerSignatureHash(db: admin.firestore.Firestore, signatureHash: string): Promise<void> {
		const docRef = db.collection(SERVER_INFO_COLLECTION).doc(getSecurityConfigDocId());
		await docRef.update({
			currentSignatureHash: signatureHash,
			lastUpdated: Timestamp.now(),
		});
	}

	// 보안 위협 로그 저장 (서브컬렉션)
	private async logSecurityThreat(db: admin.firestore.Firestore, log: SecurityThreatLog): Promise<void> {
		const threatLogsRef = db.collection(SERVER_INFO_COLLECTION).doc(getSecurityConfigDocId()).collection(THREAT_LOGS_SUBCOLLECTION);
		await threatLogsRef.add(log);
	}
}

// 보안 위협 리포트 클래스 (클라이언트에서 감지한 위협 보고)
class CSecurityReportThreat extends CFunctionBase {
	constructor() {
		super("CSecurityReportThreat");
	}

	// Security API는 PacketUID 검증 및 서버 상태 체크 스킵
	protected isAdminAuth(): boolean {
		return true;
	}

	protected getValidationSchema(): ValidationSchema | null {
		return {
			threatType: { type: "number", required: true },
			threatDetails: { type: "string", required: true },
			deviceInfo: { type: "string", required: false },
			appVersion: { type: "string", required: false },
			signatureHash: { type: "string", required: false },
		};
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const threatType = data["threatType"] as number;
		const threatDetails = data["threatDetails"] as string;
		const deviceInfo = (data["deviceInfo"] as string) || "";
		const appVersion = (data["appVersion"] as string) || "";
		const signatureHash = (data["signatureHash"] as string) || "";

		// 입력 검증
		if (!threatDetails || threatDetails.length === 0) {
			throwLogicError("execute(): threatDetails is required", EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 유효한 위협 유형인지 확인
		const validThreatTypes = Object.values(ESecurityThreatType);
		if (!validThreatTypes.includes(threatType as ESecurityThreatTypeValue)) {
			throwLogicError("execute(): invalid threatType", EAPIErrorCode.INVALID_PARAMETERS);
		}

		const uuid = request.auth?.uid || "anonymous";

		logCategory("SECURITY", `[SECURITY_THREAT] 위협 리포트 수신 - UUID: ${uuid}, Type: ${threatType}, Details: ${threatDetails}`);

		// 위협 로그 저장 (서브컬렉션)
		const db = admin.firestore();
		const threatLogsRef = db.collection(SERVER_INFO_COLLECTION).doc(getSecurityConfigDocId()).collection(THREAT_LOGS_SUBCOLLECTION);

		await threatLogsRef.add({
			uuid: uuid,
			threatType: threatType,
			threatDetails: threatDetails,
			deviceInfo: deviceInfo,
			appVersion: appVersion,
			timestamp: Timestamp.now(),
			signatureHash: signatureHash,
		});

		return resultSuccess;
	}

	protected getResponseData(): Record<string, any> | null {
		return {
			reported: true,
		};
	}
}

// SecurityConfig 헬퍼 클래스 (DataBundle 생성 시 호출)
export class CSecurityConfig {
	private collectionName: string = SERVER_INFO_COLLECTION;
	private docId: string;

	constructor(docId?: string) {
		// 우선순위: 1. 생성자 파라미터, 2. getSecurityConfigDocId() (환경변수 기반)
		this.docId = docId || getSecurityConfigDocId();
	}

	// SecurityConfig 초기화 (DataBundle 생성 시 호출)
	public async executeSecurityConfigInit(skipIfExists: boolean = true): Promise<void> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);
		const doc = await docRef.get();

		if (doc.exists && skipIfExists) {
			logCategory("SECURITY", `[SECURITY_CONFIG_INIT] SecurityConfig 이미 존재 - 스킵`);
			return;
		}

		const initialConfig: SecurityConfig = {
			currentSignatureHash: "",
			signatureVerificationEnabled: false,
			lastUpdated: Timestamp.now(),
			hackingToolPackages: [],
			certificatePinningHashes: [],
		};

		await docRef.set(initialConfig);
		logCategory("SECURITY", `[SECURITY_CONFIG_INIT] SecurityConfig 초기화 완료`);
	}

	// 서명 해시 설정 (수동)
	public async setSignatureHash(signatureHash: string): Promise<void> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);

		await docRef.update({
			currentSignatureHash: signatureHash,
			lastUpdated: Timestamp.now(),
		});
		logCategory("SECURITY", `[SECURITY_CONFIG] 서명 해시 설정됨: ${signatureHash.substring(0, 16)}...`);
	}

	// 서명 해시 리셋 (키스토어 변경 시)
	public async resetSignatureHash(): Promise<void> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);

		await docRef.update({
			currentSignatureHash: "",
			lastUpdated: Timestamp.now(),
		});
		logCategory("SECURITY", `[SECURITY_CONFIG] 서명 해시 리셋됨`);
	}

	// 서명 검증 활성화/비활성화
	public async setSignatureVerificationEnabled(enabled: boolean): Promise<void> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);

		await docRef.update({
			signatureVerificationEnabled: enabled,
			lastUpdated: Timestamp.now(),
		});
		logCategory("SECURITY", `[SECURITY_CONFIG] 서명 검증 ${enabled ? "활성화" : "비활성화"}`);
	}

	// 해킹 도구 패키지 목록 설정
	public async setHackingToolPackages(packages: string[]): Promise<void> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);

		await docRef.update({
			hackingToolPackages: packages,
			lastUpdated: Timestamp.now(),
		});
		logCategory("SECURITY", `[SECURITY_CONFIG] 해킹 도구 패키지 목록 설정됨: ${packages.length}개`);
	}

	// 해킹 도구 패키지 추가
	public async addHackingToolPackage(packageName: string): Promise<void> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);
		const doc = await docRef.get();

		const data = doc.data() as SecurityConfig | undefined;
		const existingPackages = data?.hackingToolPackages || [];

		if (!existingPackages.includes(packageName)) {
			existingPackages.push(packageName);
			await docRef.update({
				hackingToolPackages: existingPackages,
				lastUpdated: Timestamp.now(),
			});
			logCategory("SECURITY", `[SECURITY_CONFIG] 해킹 도구 패키지 추가됨: ${packageName}`);
		}
	}

	// 인증서 핀닝 해시 목록 설정
	public async setCertificatePinningHashes(hashes: string[]): Promise<void> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);

		await docRef.update({
			certificatePinningHashes: hashes,
			lastUpdated: Timestamp.now(),
		});
		logCategory("SECURITY", `[SECURITY_CONFIG] 인증서 핀닝 해시 목록 설정됨: ${hashes.length}개`);
	}

	// 인증서 핀닝 해시 추가
	public async addCertificatePinningHash(hash: string): Promise<void> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);
		const doc = await docRef.get();

		const data = doc.data() as SecurityConfig | undefined;
		const existingHashes = data?.certificatePinningHashes || [];

		if (!existingHashes.includes(hash)) {
			existingHashes.push(hash);
			await docRef.update({
				certificatePinningHashes: existingHashes,
				lastUpdated: Timestamp.now(),
			});
			logCategory("SECURITY", `[SECURITY_CONFIG] 인증서 핀닝 해시 추가됨: ${hash.substring(0, 16)}...`);
		}
	}

	// 보안 설정 조회 (클라이언트용)
	public async getSecurityConfig(): Promise<SecurityConfig | null> {
		const db = admin.firestore();
		const docRef = db.collection(this.collectionName).doc(this.docId);
		const doc = await docRef.get();

		if (!doc.exists) {
			return null;
		}

		return doc.data() as SecurityConfig;
	}
}

// SecurityConfig 초기화 API (관리자 전용)
class CSecurityInitConfig extends CFunctionBase {
	private initialized: boolean = false;

	constructor() {
		super("CSecurityInitConfig");
	}

	// Admin API는 PacketUID 검증 및 서버 상태 체크 스킵
	protected isAdminAuth(): boolean {
		return true;
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const skipIfExists = data["skipIfExists"] !== false; // 기본값 true

		const securityConfig = new CSecurityConfig();
		await securityConfig.executeSecurityConfigInit(skipIfExists);

		this.initialized = true;
		return resultSuccess;
	}

	protected getResponseData(): Record<string, any> | null {
		return {
			initialized: this.initialized,
		};
	}
}

// SecurityConfig 업데이트 API (관리자 전용)
class CSecurityUpdateConfig extends CFunctionBase {
	private updated: boolean = false;

	constructor() {
		super("CSecurityUpdateConfig");
	}

	// Admin API는 PacketUID 검증 및 서버 상태 체크 스킵
	protected isAdminAuth(): boolean {
		return true;
	}

	protected getValidationSchema(): ValidationSchema | null {
		return {
			action: { type: "string", required: true },
			signatureHash: { type: "string", required: false },
			enabled: { type: "boolean", required: false },
			packages: { type: "object", required: false },
			packageName: { type: "string", required: false },
			hashes: { type: "object", required: false },
			hash: { type: "string", required: false },
		};
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const action = data["action"] as string;
		const securityConfig = new CSecurityConfig();

		switch (action) {
			case "setSignatureHash": {
				const signatureHash = data["signatureHash"] as string;
				if (!signatureHash) {
					throwLogicError("execute(): signatureHash is required for setSignatureHash action", EAPIErrorCode.INVALID_PARAMETERS);
				}
				await securityConfig.setSignatureHash(signatureHash);
				break;
			}

			case "resetSignatureHash":
				await securityConfig.resetSignatureHash();
				break;

			case "setVerificationEnabled": {
				const enabled = data["enabled"] as boolean;
				if (enabled === undefined) {
					throwLogicError("execute(): enabled is required for setVerificationEnabled action", EAPIErrorCode.INVALID_PARAMETERS);
				}
				await securityConfig.setSignatureVerificationEnabled(enabled);
				break;
			}

			case "setHackingToolPackages": {
				const packages = data["packages"] as string[];
				if (!packages || !Array.isArray(packages)) {
					throwLogicError("execute(): packages is required for setHackingToolPackages action", EAPIErrorCode.INVALID_PARAMETERS);
				}
				await securityConfig.setHackingToolPackages(packages);
				break;
			}

			case "addHackingToolPackage": {
				const packageName = data["packageName"] as string;
				if (!packageName) {
					throwLogicError("execute(): packageName is required for addHackingToolPackage action", EAPIErrorCode.INVALID_PARAMETERS);
				}
				await securityConfig.addHackingToolPackage(packageName);
				break;
			}

			case "setCertificatePinningHashes": {
				const hashes = data["hashes"] as string[];
				if (!hashes || !Array.isArray(hashes)) {
					throwLogicError("execute(): hashes is required for setCertificatePinningHashes action", EAPIErrorCode.INVALID_PARAMETERS);
				}
				await securityConfig.setCertificatePinningHashes(hashes);
				break;
			}

			case "addCertificatePinningHash": {
				const hash = data["hash"] as string;
				if (!hash) {
					throwLogicError("execute(): hash is required for addCertificatePinningHash action", EAPIErrorCode.INVALID_PARAMETERS);
				}
				await securityConfig.addCertificatePinningHash(hash);
				break;
			}

			default:
				throwLogicError(`execute(): unknown action: ${action}`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		this.updated = true;
		return resultSuccess;
	}

	protected getResponseData(): Record<string, any> | null {
		return {
			updated: this.updated,
		};
	}
}

// SecurityConfig 조회 API (클라이언트용 - 해킹 도구 목록, 인증서 해시만 반환)
class CSecurityGetConfig extends CFunctionBase {
	private hackingToolPackages: string[] = [];
	private certificatePinningHashes: string[] = [];

	constructor() {
		super("CSecurityGetConfig");
	}

	// Security API는 PacketUID 검증 및 서버 상태 체크 스킵
	protected isAdminAuth(): boolean {
		return true;
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const securityConfig = new CSecurityConfig();
		const config = await securityConfig.getSecurityConfig();

		if (config) {
			this.hackingToolPackages = config.hackingToolPackages || [];
			this.certificatePinningHashes = config.certificatePinningHashes || [];
		}

		logCategory("SECURITY", `[SECURITY_GET_CONFIG] 보안 설정 조회 - 해킹도구: ${this.hackingToolPackages.length}개, 인증서해시: ${this.certificatePinningHashes.length}개`);

		return resultSuccess;
	}

	protected getResponseData(): Record<string, any> | null {
		return {
			hackingToolPackages: this.hackingToolPackages,
			certificatePinningHashes: this.certificatePinningHashes,
		};
	}
}

// -----------------------------------------------
// 인증서 핀닝 해시 자동 갱신 (스케줄 함수)
// -----------------------------------------------

// 인증서 핀닝 대상 도메인 목록 (동적 생성)
function getCertificatePinningDomains(): string[] {
	const projectId = process.env.GCLOUD_PROJECT || "exodusp1";
	const region = process.env.FUNCTION_REGION || "asia-northeast3";

	return [
		`${region}-${projectId}.cloudfunctions.net`, // Firebase Functions
		"firestore.googleapis.com", // Firestore
		"identitytoolkit.googleapis.com", // Firebase Auth
		"securetoken.googleapis.com", // Firebase Auth Token
	];
}

// 도메인에서 인증서 해시 추출
async function getCertificateHash(hostname: string, port: number = 443): Promise<string | null> {
	return new Promise((resolve) => {
		const socket = tls.connect(
			{
				host: hostname,
				port: port,
				servername: hostname,
				rejectUnauthorized: true,
			},
			() => {
				const cert = socket.getPeerCertificate(true);
				if (cert && cert.raw) {
					// SHA-256 해시 계산
					const hash = crypto.createHash("sha256").update(cert.raw).digest("hex").toUpperCase();
					socket.end();
					resolve(hash);
				} else {
					socket.end();
					resolve(null);
				}
			}
		);

		socket.on("error", (err) => {
			logCategory("SECURITY", `[CERT_HASH] ${hostname} 연결 실패: ${err.message}`);
			resolve(null);
		});

		// 타임아웃 설정 (10초)
		socket.setTimeout(10000, () => {
			socket.destroy();
			resolve(null);
		});
	});
}

// 인증서 체인의 모든 해시 추출 (루트, 중간, 리프 인증서)
async function getCertificateChainHashes(hostname: string, port: number = 443): Promise<string[]> {
	return new Promise((resolve) => {
		const hashes: string[] = [];

		const socket = tls.connect(
			{
				host: hostname,
				port: port,
				servername: hostname,
				rejectUnauthorized: true,
			},
			() => {
				const cert = socket.getPeerCertificate(true) as tls.DetailedPeerCertificate;
				if (cert) {
					// 인증서 체인 순회
					let currentCert: tls.DetailedPeerCertificate | undefined = cert;
					const visited = new Set<string>();

					while (currentCert && currentCert.raw) {
						const hash = crypto.createHash("sha256").update(currentCert.raw).digest("hex").toUpperCase();

						// 중복 방지
						if (!visited.has(hash)) {
							visited.add(hash);
							hashes.push(hash);
						}

						// 상위 인증서로 이동 (자기 자신을 가리키면 루트 인증서)
						const issuer = currentCert.issuerCertificate;
						if (issuer && issuer !== currentCert && issuer.raw) {
							currentCert = issuer;
						} else {
							break;
						}
					}
				}
				socket.end();
				resolve(hashes);
			}
		);

		socket.on("error", (err) => {
			logCategory("SECURITY", `[CERT_CHAIN] ${hostname} 연결 실패: ${err.message}`);
			resolve([]);
		});

		socket.setTimeout(10000, () => {
			socket.destroy();
			resolve([]);
		});
	});
}

// 인증서 핀닝 해시 자동 갱신 스케줄 클래스
export class CCertificatePinningAutoRefresh {
	// 스케줄 함수에서 호출
	public async execute(): Promise<void> {
		logCategory("SECURITY", "[CERT_AUTO_REFRESH] 인증서 핀닝 해시 자동 갱신 시작");

		const allHashes = new Set<string>();

		// 모든 대상 도메인에서 인증서 체인 해시 수집
		const domains = getCertificatePinningDomains();
		for (const domain of domains) {
			const chainHashes = await getCertificateChainHashes(domain);
			for (const hash of chainHashes) {
				allHashes.add(hash);
			}
			logCategory("SECURITY", `[CERT_AUTO_REFRESH] ${domain}: ${chainHashes.length}개 해시 수집`);
		}

		if (allHashes.size === 0) {
			logCategory("SECURITY", "[CERT_AUTO_REFRESH] 수집된 해시 없음 - 갱신 스킵");
			return;
		}

		// SecurityConfig에 저장
		const securityConfig = new CSecurityConfig();
		const hashArray = Array.from(allHashes);

		await securityConfig.setCertificatePinningHashes(hashArray);

		logCategory("SECURITY", `[CERT_AUTO_REFRESH] 인증서 핀닝 해시 갱신 완료: ${hashArray.length}개`);
	}
}

// 테스트용 수동 갱신 API
export async function request_Security_RefreshCertificateHashes(request: https.CallableRequest<any>) {
	return await new CSecurityRefreshCertificateHashes().handleRequest(request);
}

class CSecurityRefreshCertificateHashes extends CFunctionBase {
	private refreshedCount: number = 0;

	constructor() {
		super("CSecurityRefreshCertificateHashes");
	}

	protected isAdminAuth(): boolean {
		return true;
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		const autoRefresh = new CCertificatePinningAutoRefresh();
		await autoRefresh.execute();

		// 갱신된 해시 개수 확인
		const securityConfig = new CSecurityConfig();
		const config = await securityConfig.getSecurityConfig();
		this.refreshedCount = config?.certificatePinningHashes?.length || 0;

		return resultSuccess;
	}

	protected getResponseData(): Record<string, any> | null {
		return {
			refreshed: true,
			hashCount: this.refreshedCount,
		};
	}
}
