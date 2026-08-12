import { https } from "firebase-functions/v2";
import * as utilBasic from "../../Utility/UtilityBasic";
import { ConstKey } from "../../Data/Constants/ServerConstants";
import { GameDBPath } from "../../Data/Constants/FirestorePaths";
import { FirebaseInfo, getBucketID } from "../../Data/Config/ServerConfig";
import { EResponseCode, resultSuccess } from "../../Data/Types/ResponseCodes";
import * as admin from "firebase-admin";
import { CustomThrow, getEnumKeyOrThrow, throwLogicError, throwSystemError, logCategory } from "../../Utility/UtilityBasic";
import { EAPIErrorCode } from "../../Data/Generated/CommonEnum";
import { tmpdir } from "os";
import path from "path";
import * as fs from "fs";
import { gzipSync } from "zlib";
import crypto from "crypto";
import { Bucket } from "@google-cloud/storage";
import { CallableRequest } from "firebase-functions/v2/https";
import { File as GCSFile } from "@google-cloud/storage";
import { CServerInfo } from "../ServerOperation/ServerInfo";
import { CSecurityConfig } from "../Security/AppIntegrity";
import { CRequestBase } from "../Base/RequestBase";

export const EDataBundleCategory = {
	None: 0,
	Mod: 10,
	Player: 20,
	Equipment: 30,
	Skill: 40,
	Pet: 50,
	Monster: 60,
	Stage: 70,
	Nebula: 80,
	Gacha: 90,
	Currency: 100,
	Achieve: 110,
	Product: 120,
	Preset: 200,
	Awaken: 300,
	Ranking: 400,
	//StringKey: 900,	// Stringkey.bin 으로 클라에서만 사용
	Etc: 1000,
} as const;
export type EDataBundleCategory = (typeof EDataBundleCategory)[keyof typeof EDataBundleCategory];

// 테스트 코드에서 사용
export function getGameDBPathsForType(type: EDataBundleCategory): string[] {
	switch (type) {
		case EDataBundleCategory.Mod:
			// GameDB/Mod 아래에 Mod, SkillMod, CombineMod 이렇게 구성된다.
			return [GameDBPath.Mod, GameDBPath.SkillMod, GameDBPath.CombineMod, GameDBPath.CombineModTargetState];
		case EDataBundleCategory.Player:
			return [GameDBPath.Player_DefaultMod, GameDBPath.Player_Growth, GameDBPath.Player_Reinforce];
		case EDataBundleCategory.Equipment:
			return [GameDBPath.Equipment_Mod, GameDBPath.Equipment_ModTotalWeight, GameDBPath.Equipment_Mythic, GameDBPath.Equipment_Normal, GameDBPath.Equipment_Possession, GameDBPath.Equipment_ReforgingChance, GameDBPath.Equipment_ReinforceChance, GameDBPath.Equipment_ReinforceChanceByLargeType];
		case EDataBundleCategory.Skill:
			return [GameDBPath.Skill_ReinforceChance, GameDBPath.Skill_Spell, GameDBPath.Skill_Aura, GameDBPath.Skill_Curse, GameDBPath.Skill_Rune, GameDBPath.Skill_Buff];
		case EDataBundleCategory.Pet:
			return [GameDBPath.Pet, GameDBPath.PetPossession];
		case EDataBundleCategory.Monster:
			return [GameDBPath.MonsterType, GameDBPath.MonsterBoss, GameDBPath.MonsterBossLevel, GameDBPath.MonsterBossMod, GameDBPath.MonsterFloorBoss, GameDBPath.MonsterFloorBossLevel, GameDBPath.MonsterFloorBossMod, GameDBPath.MonsterDefaultMod, GameDBPath.MonsterNormal, GameDBPath.MonsterNormalLevel, GameDBPath.MonsterNormalMod];
		case EDataBundleCategory.Stage:
			return [GameDBPath.Stage, GameDBPath.StageReward, GameDBPath.StageReward_BossRush, GameDBPath.StageReward_GreatRift, GameDBPath.StageReward_Main, GameDBPath.StageReward_MainEndless, GameDBPath.StageReward_Rift];
		case EDataBundleCategory.Nebula:
			return [GameDBPath.Nebula, GameDBPath.Constellation, GameDBPath.ConstellationSlot];
		case EDataBundleCategory.Gacha:
			return [GameDBPath.Gacha, GameDBPath.GachaChance, GameDBPath.GachaLevelReward, GameDBPath.RedSoulStoneGachaItem];
		case EDataBundleCategory.Currency:
			return [GameDBPath.Currency, GameDBPath.CurrencyTicket];
		case EDataBundleCategory.Achieve:
			return [GameDBPath.AchiveItem, GameDBPath.Achievement, GameDBPath.Collection, GameDBPath.Content, GameDBPath.QuestDaily, GameDBPath.QuestWeekly, GameDBPath.QuestMain, GameDBPath.Title];
		case EDataBundleCategory.Etc:
			return [GameDBPath.Formula, GameDBPath.GameConfig, GameDBPath.Promotion, GameDBPath.Disjoint, GameDBPath.Craft];
		case EDataBundleCategory.Product:
			return [GameDBPath.Product, GameDBPath.ProductPackage, GameDBPath.ProductPass, GameDBPath.ProductSubscription, GameDBPath.VipLevel];
		case EDataBundleCategory.Awaken:
			return [GameDBPath.Awaken, GameDBPath.AwakenElement];
		case EDataBundleCategory.Preset:
			return [GameDBPath.Preset, GameDBPath.SkillSlot];
		case EDataBundleCategory.Ranking:
			return [GameDBPath.RankingReward];
		// case EDataBundleCategory.StringKey:
		// 	return [GameDBPath.StringKey];
		default:
			return [];
	}
}

// https://realspace3.atlassian.net/wiki/spaces/Pixel/pages/2802614316/DataBundle
export const DATABUNDLE_DIR = "databundle"; // Storage 폴더
export const VERSION_JSON = "version.json";
const TTL_BIN_YEAR = "public,max-age=31536000,immutable";
const TTL_JSON_60SEC = "public,max-age=60";

// version.json 형식
// {
// 	"timestamp":"2025-06-18T01:16:53.454+09:00"
// 	"Skill":    { "ver": 8, "hash": "5a17..." },
// 	"Equipment":{ "ver":15, "hash": "2b9c..." },
// 	"...":      { "ver": N, "hash": "..." },
// }
export interface DataBundleMeta {
	ver: number;
	hash: string;
}
export type DataBundleManifest = Record<string, DataBundleMeta> & { timestamp: string };

// -----------------------------------------------
// Request
// -----------------------------------------------
export async function request_DataBundle_Create(request: CallableRequest<any>) {
	return await new CDataBundle_Create().handleRequest(request.data, request);
}

export async function request_DataBundle_CheckVersion(request: CallableRequest<any>) {
	return await new CDataBundle_CheckVersion().handleRequest(request.data, request);
}

// -----------------------------------------------
// Class
// -----------------------------------------------
export class CDataBundle_Base extends CRequestBase {
	TEST_SUFFIX: string = "";

	constructor(init?: Partial<CDataBundle_Base>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	/** required = true  ➔ 없으면 throw
	 *  required = false ➔ 없으면 빈 스텁 반환
	 */
	async loadVersionManifest(bkt: Bucket, required: boolean = false): Promise<DataBundleManifest> {
		const dir = path.posix.join(DATABUNDLE_DIR, this.TEST_SUFFIX, VERSION_JSON);
		const file = bkt.file(dir);
		const [exists] = await file.exists();

		if (!exists) {
			if (required) {
				throwSystemError(`loadVersionManifest(): [DataBundle] ${dir} not found`, EAPIErrorCode.DATABASE_ERROR);
			}
			// 최초 배포/테스트용 스텁
			return { timestamp: "0" } as DataBundleManifest;
		}

		const [buf] = await file.download();
		return JSON.parse(buf.toString()) as DataBundleManifest;
	}

	// ─────────────────────────────────────────────────────────────────────────
	// Race Condition 방지를 위한 헬퍼 함수들
	// ─────────────────────────────────────────────────────────────────────────
	// 파일이 존재할 때까지 재시도하는 함수
	async waitForFileExists(file: GCSFile, maxRetries = 10, delayMs = 100): Promise<boolean> {
		for (let i = 0; i < maxRetries; i++) {
			const [exists] = await file.exists();
			if (exists) {
				return true;
			}
			if (i < maxRetries - 1) {
				await new Promise((resolve) => setTimeout(resolve, delayMs));
			}
		}
		return false;
	}

	// GCS 재시도 로직: try/catch가 재시도 흐름 제어에 필수이므로 유지
	async getFilesWithRetry(bucket: Bucket, options: any, maxRetries = 5, delayMs = 200): Promise<[GCSFile[], {}, {}]> {
		let lastError: Error;

		for (let i = 0; i < maxRetries; i++) {
			try {
				const result = await bucket.getFiles(options);
				return result;
			} catch (error) {
				lastError = error as Error;
				// 재시도 중이민로 console.warn 대신 마지막에 throwSystemError
				// logDebug(`getFiles attempt ${i + 1} failed:`, (error as Error).message);
				if (i < maxRetries - 1) {
					await new Promise((resolve) => setTimeout(resolve, delayMs));
				}
			}
		}

		throwSystemError(`getFilesWithRetry(): getFiles failed after ${maxRetries} attempts: ${lastError.message}`, EAPIErrorCode.SERVER_ERROR);
	}
}

// #region CDataBundle_Create
export class CDataBundle_Create extends CDataBundle_Base {
	constructor(init?: Partial<CDataBundle_Create>) {
		super();
		Object.assign(this, init);
		this.AdminAuth = true;
	}

	public async setUp() {
		await super.setUp();
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		return await this.executeDataBundleCreate();
	}

	public async executeDataBundleCreate(): Promise<[EResponseCode, string]> {
		const db = admin.firestore();
		const bucket = admin.storage().bucket(getBucketID());
		const manifest = await this.loadVersionManifest(bucket);

		// /tmp 작업 디렉터리
		const buildRoot = path.join(tmpdir(), `databundle_${Date.now()}`);
		fs.mkdirSync(buildRoot, { recursive: true });

		// 카테고리별 번들 생성 & 업로드
		for (const categoryEnum of Object.values(EDataBundleCategory).filter((v) => v !== 0)) {
			const categoryName = getEnumKeyOrThrow(EDataBundleCategory, categoryEnum);

			// /GameDB/Nebula 상위 문서 아래 모든 /컬렉션/문서 수집
			const snaps = await collectSnapshots(db, this.getGameDBRoots(categoryEnum as EDataBundleCategory));
			const bundleBuilder = db.bundle();
			snaps.forEach((s) => bundleBuilder.add(s));

			const rawBuf = bundleBuilder.build();
			// 번들 내용을 SHA-1 해시 → 내용이 바뀌었는지 확인
			const hash = this.calcDataHash(snaps);
			// 변경이 없으면 Skip
			if (manifest[categoryName]?.hash === hash) {
				continue;
			}

			// 버전 관리
			const ver = (manifest[categoryName]?.ver ?? 0) + 1;
			const fname = `${categoryName}.v${ver}.bin.gz`;
			const storagePath = path.join(buildRoot, fname);
			// gzip 압축, 저장
			const gz = gzipSync(rawBuf);
			fs.writeFileSync(storagePath, gz);

			// 번들데이터 업로드
			const dir = path.posix.join(DATABUNDLE_DIR, this.TEST_SUFFIX);
			await bucket.upload(storagePath, {
				destination: `${dir}/${fname}`,
				public: true,
				metadata: {
					contentType: "application/octet-stream",
					contentEncoding: "gzip",
					cacheControl: TTL_BIN_YEAR,
				},
			});

			// 업로드된 번들 파일이 존재하는지 확인 (Race Condition 방지)
			const bundleFile = bucket.file(`${dir}/${fname}`);
			const bundleExists = await this.waitForFileExists(bundleFile);
			if (!bundleExists) {
				throwSystemError(`executeDataBundleCreate(): Bundle file ${fname} not found after upload`, EAPIErrorCode.SERVER_ERROR);
			}

			manifest[categoryName] = { ver, hash };

			logCategory("DATABUNDLE_CREATE", `${fname} uploaded`);
		}

		// version.json 갱신
		// 날짜는 UTC 기준 ISO-8601, 예: "2025-06-18T01:16:53.454Z"
		manifest.timestamp = new Date().toISOString();
		const jsonTmp = path.join(buildRoot, VERSION_JSON);
		fs.writeFileSync(jsonTmp, JSON.stringify(manifest, null, 2));

		const dir = path.posix.join(DATABUNDLE_DIR, this.TEST_SUFFIX, VERSION_JSON);
		await bucket.upload(jsonTmp, {
			destination: dir,
			public: true,
			metadata: { contentType: "application/json", cacheControl: TTL_JSON_60SEC },
		});

		// 업로드된 version.json 파일이 존재하는지 확인 (Race Condition 방지)
		const versionFile = bucket.file(dir);
		const versionExists = await this.waitForFileExists(versionFile);
		if (!versionExists) {
			throwSystemError("logCategory(): version.json file not found after upload", EAPIErrorCode.SERVER_ERROR);
		}

		// 오래된 파일 정리(-1버전까지만 남김)
		await this.gcOldBundles(bucket, manifest);

		logCategory("DATABUNDLE_CREATE", "version.json updated");

		// 서버 초기화 실행 (GameDB 생성과 함께 시스템 데이터 초기화)
		await this.initializeServerSystem();

		return resultSuccess;
	}

	/** 카테고리별 GameDB 루트 문서 경로만 반환 */
	public getGameDBRoots(type: EDataBundleCategory): string[] {
		const key = getEnumKeyOrThrow(EDataBundleCategory, type);

		// None 타입은 무효한 카테고리이므로 빈 배열 반환
		if (type === EDataBundleCategory.None) {
			throwLogicError(`getGameDBRoots(): 무효한 데이터 번들 카테고리: ${key} (${type})`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		switch (type) {
			// 복수 루트가 존재하는 카테고리만 예외 처리 예시)
			// case EDataBundleCategory.Nebula:
			// 	return ["/GameDB/Nebula", "/GameDB/Constellation"];
			default:
				// 기본 패턴: /GameDB/<EnumKey>
				return [`/GameDB/${key}`];
		}
	}

	// hash 생성 로직. 정렬을 시켜서 항상 같은 순서가 되게 한다.
	calcDataHash(docs: FirebaseFirestore.DocumentSnapshot[]): string {
		// 경로 기준 정렬 → deterministic order
		const stable = docs
			.sort((a, b) => a.ref.path.localeCompare(b.ref.path))
			.map((snap) => ({
				path: snap.ref.path,
				data: this.sortObject(snap.data()), // 필드 키 정렬
			}));

		const json = JSON.stringify(stable);
		return crypto.createHash("sha1").update(json).digest("hex");
	}

	/* 중첩 객체를 키 이름 오름차순으로 재귀 정렬 */
	sortObject(obj: any): any {
		if (obj === null || typeof obj !== "object") return obj;

		if (Array.isArray(obj)) return obj.map((v) => this.sortObject(v));

		return Object.keys(obj)
			.sort()
			.reduce((acc, k) => {
				acc[k] = this.sortObject(obj[k]);
				return acc;
			}, {} as any);
	}

	// 가비지 컬렉션(오래된 파일 정리) - Race Condition 방지 적용
	async gcOldBundles(bkt: Bucket, manifest: DataBundleManifest) {
		const dir = path.posix.join(DATABUNDLE_DIR, this.TEST_SUFFIX);

		// getFiles() 호출 시 재시도 로직 적용
		const [files] = await this.getFilesWithRetry(bkt, { prefix: `${dir}/` });
		const keep = new Set<string>();

		const versionJsonDir = path.posix.join(dir, VERSION_JSON);
		keep.add(versionJsonDir);

		for (const [cat, meta] of Object.entries(manifest)) {
			if (cat === "timestamp") continue;

			const { ver } = meta as DataBundleMeta;
			keep.add(`${cat}.v${ver}.bin.gz`); // 현재 버전
			if (ver > 1) {
				keep.add(`${cat}.v${ver - 1}.bin.gz`); // 직전 버전을 남긴다.
			}
		}

		// .bin.gz 파일만 GC 대상으로 삼아 version.json 오작동 방지
		const deletions = files.filter((f) => f.name.endsWith(".bin.gz") && !keep.has(path.basename(f.name)));

		await Promise.all(deletions.map((f) => f.delete().catch(() => {})));
	}

	/**
	 * 서버 시스템 초기화
	 * GameDB 생성과 함께 필요한 시스템 데이터들을 초기화합니다.
	 */
	private async initializeServerSystem(): Promise<void> {
		logCategory("DATABUNDLE_CREATE", "Initializing server system...");

		// ServerInfo 인스턴스 생성 및 초기화
		const serverInfo = new CServerInfo();

		// 이미 존재하는 경우 스킵하도록 설정
		await serverInfo.executeServerInfoInit(true);

		// SecurityConfig 초기화 (보안 설정)
		const securityConfig = new CSecurityConfig();
		await securityConfig.executeSecurityConfigInit(true);

		logCategory("DATABUNDLE_CREATE", "Server system initialization completed");
	}
}

// #region CDataBundle_CheckVersion
export class CDataBundle_CheckVersion extends CDataBundle_Base {
	constructor(init?: Partial<CDataBundle_CheckVersion>) {
		super();
		Object.assign(this, init);
		this.AdminAuth = true; // ← 로그인 불필요
	}

	public async setUp() {
		await super.setUp();
	}

	parseClientBundleData(data: any): Record<string, number> {
		if (!data || typeof data !== "object") {
			throwLogicError("parseClientBundleData(): empty request", EAPIErrorCode.INVALID_PARAMETERS);
		}

		const obj = data["ClientBundleVersion"];
		if (!obj || typeof obj !== "string") {
			throwLogicError("throwLogicError(): missing clientBundleVersion data", EAPIErrorCode.INVALID_PARAMETERS);
		}

		const raw = JSON.parse(obj);
		const out: Record<string, number> = {};
		for (const [cat, ver] of Object.entries(raw)) {
			const v = Number(ver);
			if (!Number.isFinite(v) || v < 0) {
				throwLogicError(`throwLogicError(): invalid version: ${cat}`, EAPIErrorCode.INVALID_PARAMETERS);
			}
			out[cat] = v;
		}
		return out;
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const clientMap = this.parseClientBundleData(data);
		return await this.executeDataBundleCheckVersion(clientMap);
	}

	public async executeDataBundleCheckVersion(clientMap: Record<string, number>): Promise<[EResponseCode, string]> {
		const bucket = admin.storage().bucket(getBucketID());
		const manifest = await this.loadVersionManifest(bucket, true);

		const diff: Record<string, number> = {};
		for (const [category, meta] of Object.entries(manifest)) {
			if (category === "timestamp") continue;

			const { ver } = meta as DataBundleMeta;
			const clientVer = clientMap?.[category] ?? 0;
			if (ver > clientVer) {
				diff[category] = ver;
			}
		}

		this.resultData.set(ConstKey.RESPONSEKEY, JSON.stringify(diff));

		// 암호화 키, 클라에서 stringKey.bin 등에 사용한다.
		this.resultData.set(ConstKey.AESKEY, ConstKey.AESKEY_VALUE);
		this.resultData.set(ConstKey.AESIV, ConstKey.AESIV_VALUE);

		return resultSuccess;
	}
}

/** 주어진 루트(들) 아래의 (하위 컬렉션명 == 도큐먼트ID) 스냅들을 모두 반환 */
export async function collectSnapshots(db: FirebaseFirestore.Firestore, roots: string[]): Promise<FirebaseFirestore.DocumentSnapshot[]> {
	const snaps: FirebaseFirestore.DocumentSnapshot[] = [];

	for (const path of roots) {
		// GameDB 루트 문서 접근 방지 - /GameDB/GameDB 같은 잘못된 경로 차단
		if (path === "/GameDB/GameDB" || path.endsWith("/GameDB")) {
			throwLogicError(`collectSnapshots(): 잘못된 GameDB 경로 접근 차단: ${path}`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		const rootDoc = db.doc(path);
		const subCols = await rootDoc.listCollections();

		if (subCols.length === 0) {
			// Currency 같이 하위 컬렉션이 없는 단일 도큐먼트형
			snaps.push(await rootDoc.get());
			continue;
		}

		for (const col of subCols) {
			snaps.push(await col.doc(col.id).get());
		}
	}

	return snaps;
}
