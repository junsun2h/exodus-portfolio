import { getFirestore, Firestore, DocumentReference, Timestamp, WriteBatch, FieldValue, FieldPath } from "firebase-admin/firestore";
import * as utilBasic from "../../Utility/UtilityBasic";
import { ConstKey } from "../../Data/Constants/ServerConstants";
import { CoreDataKey, GameDBPath } from "../../Data/Constants/FirestorePaths";
import { FirebaseInfo } from "../../Data/Config/ServerConfig";
import { EResponseCode, resultSuccess } from "../../Data/Types/ResponseCodes";
import * as GameBalanceConfig from "../../Data/Shared/Config/GameBalanceConfig";
import { EAPIErrorCode, EAwaken, ECollection, EConstellation, ENebula } from "../../Data/Generated/CommonEnum";
import { ModValueInt } from "../../Data/Shared/Types/ModValueTypes";
import { throwLogicError, logCategory } from "../../Utility/UtilityBasic";
import { ValidationSchema } from "../../Utility/InputValidation";
import { https } from "firebase-functions/v2";
import { CRequestBase } from "../Base/RequestBase";
import { CFunctionBase, createSimpleFunction } from "../Base/FunctionBase";
import { ServerSelector } from "../Factory/ServerSelector";

// -----------------------------------------------
// Firebase Functions Export (파일 최상단)
// -----------------------------------------------

export async function request_InitRankDB(request: https.CallableRequest<any>) {
	return await new CInitRankDB().handleRequest(request);
}

export async function request_InsertFakeData_All(request: https.CallableRequest<any>) {
	return await new CInsertFakeData_All().handleRequest(request.data, request);
}

export async function request_ClearRankData_All(request: https.CallableRequest<any>) {
	return await new CClearRankData_All().handleRequest(request.data, request);
}

// -----------------------------------------------
// Unified Ranking API Interfaces
// -----------------------------------------------

// -----------------------------------------------
// Class Implementations
// -----------------------------------------------

export class CInitRankDB extends CFunctionBase {
	constructor() {
		super("InitRankDB");
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			serverID: {
				type: "string",
				required: true,
				maxLength: 50,
			},
		};
	}

	protected async execute(data: any, request: https.CallableRequest<any>): Promise<[EResponseCode, string]> {
		// 본API는 개발자만 접근 가능한 API임. UserAuth검증 불필요

		// DB에 Rank/ConstellationContract/ConstellationContractRanking 데이터 생성/초기화
		await initializeRankDB_ConstellationContracts(this.resultData, data.serverID);

		return resultSuccess;
	}
}

// Helper 함수를 사용한 간단한 패턴 (선택적)
export const request_InitRankDB_Simple = createSimpleFunction(
	async (data: any, request: https.CallableRequest<any>) => {
		// 본API는 개발자만 접근 가능한 API임. UserAuth검증 불필요
		const resultData = utilBasic.createResultDataMap();

		// DB에 Rank/ConstellationContract/ConstellationContractRanking 데이터 생성/초기화
		await initializeRankDB_ConstellationContracts(resultData, data.serverID);

		return resultSuccess;
	},
	{
		serverID: {
			type: "string",
			required: true,
			maxLength: 50,
		},
	},
	"InitRankDB_Simple"
);

// -----------------------------------------------
// Class
// -----------------------------------------------
// 함수 호출 시 필요한 파라미터 구조
export interface UpdateRankData_Floor {
	floorNumber: number;
	strNickName: string;
	bestStageMain: number;
	awakenGrade: EAwaken;
}
// DB에 저장되는 필드 구조
export interface RankDB_Floor {
	rank: number;
	strNickName: string;
	bestStageMain: number;
	awakenGrade: EAwaken;
	timestamp: FirebaseFirestore.Timestamp;
}
// Firestore 업데이트를 위한 필드 구조 인터페이스 (rank 제외)
export type RankDB_FloorUpdatable = Omit<RankDB_Floor, "rank">;

// 모니터링 통계 인터페이스
export interface RankOperationStats {
	readCount: number;
	writeCount: number;
	deleteCount: number;
	executionTimeMs: number;
}

export abstract class CRankBase {
	protected firestore: Firestore;
	protected batchSize: number = 500;
	protected initialRank: number = 10000; // 초기값
	protected querylimitCount: number = 10000; // 최대 문서개수
	protected batchFetchSize: number = 1000; // 페이징 사이즈
	protected serverID: string;

	// 모니터링용 카운터
	protected stats: RankOperationStats = {
		readCount: 0,
		writeCount: 0,
		deleteCount: 0,
		executionTimeMs: 0
	};

	constructor(serverID: string) {
		this.firestore = getFirestore();
		const serverManager = ServerSelector.getInstance();
		this.serverID = serverManager.validateAndGetServerIDSync(serverID);
		this.resetStats();
	}

	// 모니터링 통계 초기화
	protected resetStats(): void {
		this.stats = {
			readCount: 0,
			writeCount: 0,
			deleteCount: 0,
			executionTimeMs: 0
		};
	}

	// 읽기 카운터 증가
	protected incrementReadCount(count: number = 1): void {
		this.stats.readCount += count;
	}

	// 쓰기 카운터 증가
	protected incrementWriteCount(count: number = 1): void {
		this.stats.writeCount += count;
	}

	// 삭제 카운터 증가
	protected incrementDeleteCount(count: number = 1): void {
		this.stats.deleteCount += count;
	}

	// 실행 시간 측정 시작
	protected startTimer(): number {
		return Date.now();
	}

	// 실행 시간 기록 및 로깅
	protected logExecutionStats(operationName: string, startTime: number): void {
		this.stats.executionTimeMs = Date.now() - startTime;
		logCategory("RANK_MONITOR", `[${operationName}] Server: ${this.serverID}, ExecutionTime: ${this.stats.executionTimeMs}ms, Reads: ${this.stats.readCount}, Writes: ${this.stats.writeCount}, Deletes: ${this.stats.deleteCount}`);
	}

	// 서버별 Rank 루트 경로 생성
	protected getRankRootPath(): string {
		const serverManager = ServerSelector.getInstance();
		return serverManager.getRankPathSync(this.serverID);
	}

	protected async commitBatchesParallel(batches: WriteBatch[], concurrency: number = 5): Promise<void> {
		const batchChunks = this.chunkArray(batches, concurrency);
		for (const batchChunk of batchChunks) {
			await Promise.all(batchChunk.map((batch) => batch.commit()));
		}
	}

	protected chunkArray<T>(array: T[], size: number): T[][] {
		const results: T[][] = [];
		for (let i = 0; i < array.length; i += size) {
			results.push(array.slice(i, i + size));
		}
		return results;
	}

	abstract refreshRanksDB(): Promise<void>;
}

export class CFloorRanks extends CRankBase {
	constructor(serverID: string) {
		super(serverID);
	}
	/*
	Rank
	└─ Floors
		└─ Floor_2
			└─ (UUID)
				├─ strNickName: "유저명"
				├─ bestStageMain: 최고 스테이지
				├─ timestamp: 도달 시간
			└─ (UUID)
			...
		└─ Floor_3
		...
	*/
	// 유저의 진행 데이터를 Rank에 저장
	async updateRankData(data: UpdateRankData_Floor, UUID: string) {
		const { floorNumber, strNickName, bestStageMain, awakenGrade } = data;

		this.validateInput(data);

		const timestamp = Timestamp.now();

		await this.firestore.runTransaction(async (transaction) => {
			const currentFloorRef = this.getUserDocRef(this.firestore, floorNumber, UUID);
			const previousFloor = floorNumber - 1;
			const previousFloorRef = previousFloor >= 1 ? this.getUserDocRef(this.firestore, previousFloor, UUID) : null;

			const currentFloorSnap = await transaction.get(currentFloorRef);

			if (!currentFloorSnap.exists) {
				// 새로 기록되는 경우 rank를 10000으로 설정
				const newRankData: RankDB_Floor = {
					rank: this.initialRank, // 초기 순위 값
					strNickName,
					bestStageMain,
					awakenGrade,
					timestamp,
				};
				transaction.set(currentFloorRef, newRankData);
			} else {
				// 기존 기록이 있는 경우 rank는 유지하고 다른 필드만 업데이트
				// rank는 스케줄링에서만 갱신된다.
				const updatedData: RankDB_FloorUpdatable = {
					strNickName,
					bestStageMain,
					awakenGrade,
					timestamp,
				};
				transaction.update(currentFloorRef, updatedData);
			}

			if (previousFloorRef) {
				// 이전 층의 문서를 삭제
				transaction.delete(previousFloorRef);
			}
		});

		return resultSuccess;
	}

	// 입력 데이터 검증
	validateInput(data: UpdateRankData_Floor) {
		const { floorNumber, strNickName, bestStageMain, awakenGrade } = data;
		if (floorNumber === undefined || floorNumber < 1 || !strNickName || bestStageMain === undefined || bestStageMain < 1 || awakenGrade === undefined) {
			throwLogicError("validateInput(): 필수 매개변수가 없습니다.", EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	// 유저 문서 레퍼런스 가져오기
	getUserDocRef(firestore: Firestore, floorNumber: number, UUID: string): DocumentReference {
		return firestore.collection(this.getRankRootPath()).doc("Floors").collection(`Floor_${floorNumber}`).doc(UUID);
	}

	// 주기적으로 각 층의 모든 유저에 대해서 순위를 정렬하고 rank 필드에 순위를 업데이트 하는 함수
	async refreshRanksDB(): Promise<void> {
		const startTime = this.startTimer();
		this.resetStats();

		const FLOOR_NUMBERS = Array.from({ length: GameBalanceConfig.num_floor }, (_, i) => i + 1);
		logCategory("RANK", "CFloorRanks.refreshRanksDB(): 시작...");

		const floorPromises = FLOOR_NUMBERS.map(async (floorNumber) => {
			const floorCollectionRef = this.firestore.collection(this.getRankRootPath()).doc("Floors").collection(`Floor_${floorNumber}`);

			let rank = 1;
			let lastDoc: FirebaseFirestore.QueryDocumentSnapshot | null = null;
			let processedCount = 0;

			const updates: { docRef: DocumentReference; newRank: number }[] = [];

			while (processedCount < this.querylimitCount) {
				let query = floorCollectionRef.orderBy("bestStageMain", "desc").orderBy("timestamp", "asc").limit(this.batchFetchSize);

				if (lastDoc) {
					query = query.startAfter(lastDoc);
				}

				const usersSnapshot = await query.get();
				this.incrementReadCount(usersSnapshot.size);

				if (usersSnapshot.empty) break;

				usersSnapshot.forEach((doc) => {
					const data = doc.data() as RankDB_Floor;
					if (data.rank !== rank) {
						updates.push({ docRef: doc.ref, newRank: rank });
					}
					rank++;
				});

				lastDoc = usersSnapshot.docs[usersSnapshot.docs.length - 1];
				processedCount += usersSnapshot.size;
			}

			if (updates.length === 0) return;

			logCategory("RANK", `CFloorRanks.refreshRanksDB(): floor_${floorNumber} - 업데이트된 랭크 수: ${updates.length}`);
			this.incrementWriteCount(updates.length);

			const batches: WriteBatch[] = [];
			for (let i = 0; i < updates.length; i += this.batchSize) {
				const batch = this.firestore.batch();
				const batchUpdates = updates.slice(i, i + this.batchSize);
				batchUpdates.forEach(({ docRef, newRank }) => {
					batch.update(docRef, { rank: newRank });
				});
				batches.push(batch);
			}

			await this.commitBatchesParallel(batches, 5);
		});

		await Promise.all(floorPromises);

		this.logExecutionStats("CFloorRanks.refreshRanksDB", startTime);
	}

	// 모든 층수 랭킹 데이터 초기화 (시즌 리셋용)
	async resetAllRanks(): Promise<void> {
		const startTime = this.startTimer();
		this.resetStats();

		logCategory("RANK", "CFloorRanks.resetAllRanks(): 층수 랭킹 리셋 시작...");

		const FLOOR_NUMBERS = Array.from({ length: GameBalanceConfig.num_floor }, (_, i) => i + 1);

		for (const floorNumber of FLOOR_NUMBERS) {
			const floorCollectionRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("Floors")
				.collection(`Floor_${floorNumber}`);

			let deletedCount = 0;
			let snapshot = await floorCollectionRef.limit(this.batchSize).get();

			while (!snapshot.empty) {
				const batch = this.firestore.batch();
				snapshot.docs.forEach((doc) => batch.delete(doc.ref));
				await batch.commit();

				deletedCount += snapshot.size;
				this.incrementDeleteCount(snapshot.size);

				snapshot = await floorCollectionRef.limit(this.batchSize).get();
			}

			if (deletedCount > 0) {
				logCategory("RANK", `CFloorRanks.resetAllRanks(): Floor_${floorNumber} - ${deletedCount}건 삭제`);
			}
		}

		this.logExecutionStats("CFloorRanks.resetAllRanks", startTime);
		logCategory("RANK", "CFloorRanks.resetAllRanks(): 층수 랭킹 리셋 완료");
	}

	// 랭킹 보상 지급용 - 모든 층의 모든 유저를 순위와 함께 가져오기
	// 반환: Map<UUID, { rank, floorNumber, strNickName, bestStageMain, awakenGrade }>
	async getAllUsersWithRank(): Promise<Map<string, { uuid: string; rank: number; floorNumber: number; strNickName: string; bestStageMain: number; awakenGrade: EAwaken }>> {
		const startTime = this.startTimer();
		this.resetStats();

		const FLOOR_NUMBERS = Array.from({ length: GameBalanceConfig.num_floor }, (_, i) => i + 1);
		const userRankMap = new Map<string, { uuid: string; rank: number; floorNumber: number; strNickName: string; bestStageMain: number; awakenGrade: EAwaken }>();

		logCategory("RANK", "CFloorRanks.getAllUsersWithRank(): 모든 유저 랭킹 조회 시작...");

		// 높은 층수부터 순회 (높은 층 우선)
		for (let i = FLOOR_NUMBERS.length - 1; i >= 0; i--) {
			const floorNumber = FLOOR_NUMBERS[i];
			const floorCollectionRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("Floors")
				.collection(`Floor_${floorNumber}`);

			let lastDoc: FirebaseFirestore.QueryDocumentSnapshot | null = null;
			let processedCount = 0;

			while (processedCount < this.querylimitCount) {
				let query = floorCollectionRef
					.orderBy("rank", "asc")
					.limit(this.batchFetchSize);

				if (lastDoc) {
					query = query.startAfter(lastDoc);
				}

				const snapshot = await query.get();
				this.incrementReadCount(snapshot.size);

				if (snapshot.empty) break;

				snapshot.forEach((doc) => {
					const data = doc.data() as RankDB_Floor;
					const uuid = doc.id;

					// 유저는 하나의 층에만 존재 (이전 층 삭제됨)
					// 이미 등록된 유저는 건너뜀 (중복 방지)
					if (!userRankMap.has(uuid)) {
						userRankMap.set(uuid, {
							uuid,
							rank: data.rank,
							floorNumber,
							strNickName: data.strNickName,
							bestStageMain: data.bestStageMain,
							awakenGrade: data.awakenGrade,
						});
					}
				});

				lastDoc = snapshot.docs[snapshot.docs.length - 1];
				processedCount += snapshot.size;
			}
		}

		this.logExecutionStats("CFloorRanks.getAllUsersWithRank", startTime);
		logCategory("RANK", `CFloorRanks.getAllUsersWithRank(): 총 ${userRankMap.size}명 조회 완료`);

		return userRankMap;
	}

	// 랭킹 보상 지급용 - 특정 층의 모든 유저를 순위와 함께 가져오기 (총 유저 수 포함)
	async getUsersWithRankByFloor(floorNumber: number): Promise<{ users: Array<{ uuid: string; rank: number; data: RankDB_Floor }>; totalCount: number }> {
		const floorCollectionRef = this.firestore
			.collection(this.getRankRootPath())
			.doc("Floors")
			.collection(`Floor_${floorNumber}`);

		const users: Array<{ uuid: string; rank: number; data: RankDB_Floor }> = [];
		let lastDoc: FirebaseFirestore.QueryDocumentSnapshot | null = null;
		let processedCount = 0;

		while (processedCount < this.querylimitCount) {
			let query = floorCollectionRef
				.orderBy("rank", "asc")
				.limit(this.batchFetchSize);

			if (lastDoc) {
				query = query.startAfter(lastDoc);
			}

			const snapshot = await query.get();
			this.incrementReadCount(snapshot.size);

			if (snapshot.empty) break;

			snapshot.forEach((doc) => {
				const data = doc.data() as RankDB_Floor;
				users.push({
					uuid: doc.id,
					rank: data.rank,
					data,
				});
			});

			lastDoc = snapshot.docs[snapshot.docs.length - 1];
			processedCount += snapshot.size;
		}

		return { users, totalCount: users.length };
	}
}

export class CInsertFakeData_All extends CRequestBase {
	constructor(init?: Partial<CInsertFakeData_All>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.account, CoreDataKey.COREDATAKEY_ACCOUNT);
		this.addToCreateCoreData(this.coreData.datas.player, CoreDataKey.COREDATAKEY_PLAYER);
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			userCount: {
				type: "number",
				min: 0,
				max: 1000,
				required: false,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const userCount: number = data.userCount || 100; // 기본값 100명
		return await this.insertAllFakeData(userCount);
	}

	async insertAllFakeData(userCount: number): Promise<[EResponseCode, string]> {
		if (userCount < 0 || userCount > 1000) {
			throwLogicError("insertAllFakeData(): userCount must be 0 or more and 1000 or less", EAPIErrorCode.INVALID_PARAMETERS);
		}

		const promises: Promise<[EResponseCode, string]>[] = [];
		const results: string[] = [];

		// FloorRank 가짜 데이터 생성 (2층~최대층까지 분배)
		const floorPromises = await this.generateFloorRankData(userCount);
		promises.push(...floorPromises.promises);
		results.push(...floorPromises.results);

		// AwakenGradeRank 가짜 데이터 생성 (모든 등급에 분배)
		const awakenPromises = await this.generateAwakenGradeRankData(userCount);
		promises.push(...awakenPromises.promises);
		results.push(...awakenPromises.results);

		// ConstellationContract 가짜 데이터 생성
		const constellationContractGenerator = this.createInternalRequest(CInsertFakeData_ConstellationContract);
		await constellationContractGenerator.setUp();
		promises.push(constellationContractGenerator.insertFakeConstellationContractData(userCount));
		results.push(`ConstellationContract: ${userCount} contracts`);

		// HallOfFame 가짜 데이터 생성
		const hallOfFamePromises = await this.generateHallOfFameData(userCount);
		promises.push(...hallOfFamePromises.promises);
		results.push(...hallOfFamePromises.results);

		// 모든 가짜 데이터 생성을 병렬로 실행
		const allResults = await Promise.all(promises);

		// 결과 확인
		for (const result of allResults) {
			if (result[0] !== EResponseCode.Success) {
				throwLogicError(`insertAllFakeData(): One or more operations failed: ${result[1]}`, EAPIErrorCode.SERVER_ERROR);
			}
		}

		const successMessage = `Successfully generated fake data for all ranking systems:\n${results.join("\n")}`;
		logCategory("RANK", `insertAllFakeData(): ${successMessage}`);

		return [EResponseCode.Success, successMessage];
	}

	private async generateFloorRankData(totalUserCount: number): Promise<{ promises: Promise<[EResponseCode, string]>[]; results: string[] }> {
		const promises: Promise<[EResponseCode, string]>[] = [];
		const results: string[] = [];

		const availableFloors = Array.from({ length: GameBalanceConfig.num_floor }, (_, i) => i + 1);
		const minUsersPerFloor = 10;
		const totalMinUsers = availableFloors.length * minUsersPerFloor;

		if (totalUserCount < totalMinUsers) {
			throwLogicError(`generateFloorRankData(): Need at least ${totalMinUsers} users for ${availableFloors.length} floors (${minUsersPerFloor} per floor)`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 각 층에 최소 인원 배치 후 나머지 랜덤 분배
		const baseUsers = minUsersPerFloor;
		const remainingUsers = totalUserCount - totalMinUsers;

		const floorRankGenerator = this.createInternalRequest(CInsertFakeData_FloorRank);
		await floorRankGenerator.setUp();

		for (let i = 0; i < availableFloors.length; i++) {
			const floorNumber = availableFloors[i];
			// 남은 유저를 랜덤하게 분배 (마지막 층에서 나머지 모두 배치)
			const extraUsers = i === availableFloors.length - 1 ? remainingUsers - Math.floor(remainingUsers / availableFloors.length) * i : Math.floor(remainingUsers / availableFloors.length);

			const usersForThisFloor = baseUsers + extraUsers;

			if (usersForThisFloor > 0) {
				promises.push(floorRankGenerator.insertFakeFloorRankData(usersForThisFloor, floorNumber));
				results.push(`FloorRank: ${usersForThisFloor} users for floor ${floorNumber}`);
			}
		}

		return { promises, results };
	}

	private async generateAwakenGradeRankData(totalUserCount: number): Promise<{ promises: Promise<[EResponseCode, string]>[]; results: string[] }> {
		const promises: Promise<[EResponseCode, string]>[] = [];
		const results: string[] = [];

		// None과 unwakened를 제외한 모든 각성 등급
		const availableGrades = Object.values(EAwaken).filter((grade) => grade !== EAwaken.None && grade !== EAwaken.awaken_grade_unwakened && typeof grade === "number") as EAwaken[];

		const minUsersPerGrade = 10;
		const totalMinUsers = availableGrades.length * minUsersPerGrade;

		if (totalUserCount < totalMinUsers) {
			throwLogicError(`generateAwakenGradeRankData(): Need at least ${totalMinUsers} users for ${availableGrades.length} awaken grades (${minUsersPerGrade} per grade)`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 각 등급에 최소 인원 배치 후 나머지 랜덤 분배
		const baseUsers = minUsersPerGrade;
		const remainingUsers = totalUserCount - totalMinUsers;

		const awakenGradeRankGenerator = this.createInternalRequest(CInsertFakeData_AwakenGradeRank);
		await awakenGradeRankGenerator.setUp();

		for (let i = 0; i < availableGrades.length; i++) {
			const awakenGrade = availableGrades[i];
			// 남은 유저를 랜덤하게 분배 (마지막 등급에서 나머지 모두 배치)
			const extraUsers = i === availableGrades.length - 1 ? remainingUsers - Math.floor(remainingUsers / availableGrades.length) * i : Math.floor(remainingUsers / availableGrades.length);

			const usersForThisGrade = baseUsers + extraUsers;

			if (usersForThisGrade > 0) {
				promises.push(awakenGradeRankGenerator.insertFakeAwakenGradeRankData(usersForThisGrade, awakenGrade));

				// 각성 등급 이름 가져오기
				const gradeName = utilBasic.getKeyName(EAwaken, awakenGrade) || `Grade_${awakenGrade}`;
				results.push(`AwakenGradeRank: ${usersForThisGrade} users for ${gradeName}`);
			}
		}

		return { promises, results };
	}

	private async generateHallOfFameData(totalUserCount: number): Promise<{ promises: Promise<[EResponseCode, string]>[]; results: string[] }> {
		const promises: Promise<[EResponseCode, string]>[] = [];
		const results: string[] = [];

		// 1. ConstellationFirst 가짜 데이터 생성 (최대 25명)
		const maxConstellationAchievers = Math.min(25, Math.floor(totalUserCount * 0.1)); // 전체 유저의 10%, 최대 25명
		if (maxConstellationAchievers > 0) {
			promises.push(this.generateConstellationFirstAchieversData(maxConstellationAchievers));
			results.push(`HallOfFame - ConstellationFirst: ${maxConstellationAchievers} achievers`);
		}

		// 2. FloorFirstClear 가짜 데이터 생성 (각 층당 최대 5명)
		const floorsToGenerate = Math.min(10, Math.floor(totalUserCount / 20)); // 최대 10층, 유저수에 따라 조정
		if (floorsToGenerate > 0) {
			promises.push(this.generateFloorFirstClearsData(floorsToGenerate));
			results.push(`HallOfFame - FloorFirstClear: ${floorsToGenerate} floors, up to 5 clears each`);
		}

		// 3. TotalPlayTime 가짜 데이터 생성
		const playTimeUsers = Math.min(100, Math.floor(totalUserCount * 0.3)); // 전체 유저의 30%, 최대 100명
		if (playTimeUsers > 0) {
			promises.push(this.generateTotalPlayTimeData(playTimeUsers));
			results.push(`HallOfFame - TotalPlayTime: ${playTimeUsers} users`);
		}

		// 4. QuestAchievements 가짜 데이터 생성
		const questUsers = Math.min(100, Math.floor(totalUserCount * 0.25)); // 전체 유저의 25%, 최대 100명
		if (questUsers > 0) {
			promises.push(this.generateQuestAchievementsData(questUsers));
			results.push(`HallOfFame - QuestAchievements: ${questUsers} users`);
		}

		return { promises, results };
	}

	private async generateConstellationFirstAchieversData(userCount: number): Promise<[EResponseCode, string]> {
		const manager = new CHallOfFame_ConstellationFirstAchievers(this.getServerID());

		// 사용 가능한 성좌 목록 (None 제외)
		const availableConstellations = Object.values(EConstellation).filter(
			(constellation) => constellation !== EConstellation.None && typeof constellation === "number"
		) as EConstellation[];

		let successCount = 0;
		const maxAchievers = Math.min(userCount, 25, availableConstellations.length);

		// 에뮬레이터 환경에서는 동시성 제한 없이 실행
		const isEmulator = utilBasic.isEmulatorEnvironment();
		const CONCURRENT_LIMIT = 20;
		const promises: Promise<[EResponseCode, string]>[] = [];

		for (let i = 0; i < maxAchievers; i++) {
			const constellation = availableConstellations[i % availableConstellations.length];
			const testUUID = `fake_constellation_${constellation}_${i}`;
			const testData: UpdateRankData_ConstellationFirstAchiever = {
				strNickName: `FakeConstellationUser${i + 1}`,
				constellation: constellation,
				timestamp: Timestamp.fromMillis(Date.now() - (maxAchievers - i) * 86400000) // 역순으로 날짜 설정
			};

			promises.push(
				manager.recordFirstAchiever(testData, testUUID).catch(() => [EResponseCode.Fail, ""] as [EResponseCode, string])
			);

			// 프로덕션 환경에서만 배치 처리
			if (!isEmulator && promises.length >= CONCURRENT_LIMIT) {
				const results = await Promise.all(promises);
				successCount += results.filter(r => r[0] === EResponseCode.Success).length;
				promises.length = 0;
			}
		}

		// 남은 작업 처리
		if (promises.length > 0) {
			const results = await Promise.all(promises);
			successCount += results.filter(r => r[0] === EResponseCode.Success).length;
		}

		await manager.refreshRanksDB();
		return [EResponseCode.Success, `Generated ${successCount} constellation first achievers`];
	}

	private async generateFloorFirstClearsData(maxFloor: number): Promise<[EResponseCode, string]> {
		const manager = new CHallOfFame_FloorFirstClears(this.getServerID());
		let totalClears = 0;

		// 에뮬레이터 환경에서는 동시성 제한 없이 실행
		const isEmulator = utilBasic.isEmulatorEnvironment();
		const CONCURRENT_LIMIT = 20;
		const promises: Promise<[EResponseCode, string, number]>[] = [];

		for (let floor = 1; floor <= maxFloor; floor++) {
			const clearsForThisFloor = Math.min(5, Math.floor(Math.random() * 5) + 1); // 1~5명 랜덤

			for (let clearIndex = 0; clearIndex < clearsForThisFloor; clearIndex++) {
				const testUUID = `fake_floor_${floor}_clear_${clearIndex}`;
				const testData: UpdateRankData_FloorFirstClear = {
					floorNumber: floor,
					strNickName: `FakeFloorUser${floor}_${clearIndex + 1}`,
					timestamp: Timestamp.fromMillis(Date.now() - (maxFloor - floor + 1) * 86400000 - clearIndex * 3600000), // 층별, 클리어 순서별 시간차
					bestStage: floor * 10 + Math.floor(Math.random() * 10) // 층수 * 10 + 랜덤 보너스
				};

				promises.push(
					manager.recordFirstClear(testData, testUUID).catch(() => [EResponseCode.Fail, "", 0] as [EResponseCode, string, number])
				);

				// 프로덕션 환경에서만 배치 처리
				if (!isEmulator && promises.length >= CONCURRENT_LIMIT) {
					const results = await Promise.all(promises);
					totalClears += results.filter(r => r[0] === EResponseCode.Success).length;
					promises.length = 0;
				}
			}
		}

		// 남은 작업 처리
		if (promises.length > 0) {
			const results = await Promise.all(promises);
			totalClears += results.filter(r => r[0] === EResponseCode.Success).length;
		}

		return [EResponseCode.Success, `Generated ${totalClears} floor first clears for ${maxFloor} floors`];
	}

	private async generateTotalPlayTimeData(userCount: number): Promise<[EResponseCode, string]> {
		const manager = new CHallOfFame_TotalPlayTime(this.getServerID());
		let successCount = 0;

		// 에뮬레이터 환경에서는 동시성 제한 없이 실행
		const isEmulator = utilBasic.isEmulatorEnvironment();
		const CONCURRENT_LIMIT = 20;
		const promises: Promise<[EResponseCode, string]>[] = [];

		for (let i = 0; i < userCount; i++) {
			const testUUID = `fake_playtime_user_${i}`;
			// 1시간~100시간 랜덤 플레이타임 (초 단위)
			const minSeconds = 3600; // 1시간
			const maxSeconds = 360000; // 100시간
			const playTimeSeconds = minSeconds + Math.floor(Math.random() * (maxSeconds - minSeconds));

			const testData: UpdateRankData_TotalPlayTime = {
				totalPlayTimeSeconds: playTimeSeconds
			};

			promises.push(
				manager.updateRankData(testData, testUUID, `FakePlayTimeUser${i + 1}`).catch(() => [EResponseCode.Fail, ""] as [EResponseCode, string])
			);

			// 프로덕션 환경에서만 배치 처리
			if (!isEmulator && promises.length >= CONCURRENT_LIMIT) {
				const results = await Promise.all(promises);
				successCount += results.filter(r => r[0] === EResponseCode.Success).length;
				promises.length = 0;
			}
		}

		// 남은 작업 처리
		if (promises.length > 0) {
			const results = await Promise.all(promises);
			successCount += results.filter(r => r[0] === EResponseCode.Success).length;
		}

		await manager.refreshRanksDB();
		return [EResponseCode.Success, `Generated ${successCount} total play time records`];
	}

	private async generateQuestAchievementsData(userCount: number): Promise<[EResponseCode, string]> {
		const manager = new CHallOfFame_QuestAchievements(this.getServerID());
		let successCount = 0;

		// 에뮬레이터 환경에서는 동시성 제한 없이 실행
		const isEmulator = utilBasic.isEmulatorEnvironment();
		const CONCURRENT_LIMIT = 20;
		const promises: Promise<[EResponseCode, string]>[] = [];

		for (let i = 0; i < userCount; i++) {
			const testUUID = `fake_quest_user_${i}`;
			// 10~1000개 퀘스트 완료 랜덤
			const questCompletionCount = 10 + Math.floor(Math.random() * 990);

			const testData: UpdateRankData_QuestAchievement = {
				questCompletionCount: questCompletionCount
			};

			promises.push(
				manager.updateRankData(testData, testUUID, `FakeQuestUser${i + 1}`).catch(() => [EResponseCode.Fail, ""] as [EResponseCode, string])
			);

			// 프로덕션 환경에서만 배치 처리
			if (!isEmulator && promises.length >= CONCURRENT_LIMIT) {
				const results = await Promise.all(promises);
				successCount += results.filter(r => r[0] === EResponseCode.Success).length;
				promises.length = 0;
			}
		}

		// 남은 작업 처리
		if (promises.length > 0) {
			const results = await Promise.all(promises);
			successCount += results.filter(r => r[0] === EResponseCode.Success).length;
		}

		await manager.refreshRanksDB();
		return [EResponseCode.Success, `Generated ${successCount} quest achievement records`];
	}
}

export class CClearRankData_All extends CRequestBase {
	constructor(init?: Partial<CClearRankData_All>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.account, CoreDataKey.COREDATAKEY_ACCOUNT);
	}

	// 서버별 Rank 루트 경로 생성
	private getRankRootPath(): string {
		const serverManager = ServerSelector.getInstance();
		return serverManager.getRankPathSync(this.getServerID());
	}

	protected getValidationSchema(): ValidationSchema {
		return {};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		return await this.clearTestRankData();
	}

	async clearTestRankData(): Promise<[EResponseCode, string]> {
		const promises: Promise<void>[] = [];
		const results: string[] = [];

		// FloorRank 테스트 데이터 삭제
		const floorPromise = this.clearFloorRankTestData();
		promises.push(floorPromise);
		results.push("FloorRank test data cleared");

		// AwakenGradeRank 테스트 데이터 삭제
		const awakenPromise = this.clearAwakenGradeRankTestData();
		promises.push(awakenPromise);
		results.push("AwakenGradeRank test data cleared");

		// ConstellationContract 테스트 데이터 초기화
		const constellationPromise = this.clearConstellationContractTestData();
		promises.push(constellationPromise);
		results.push("ConstellationContract test data cleared");

		// 모든 클리어 작업을 병렬로 실행
		await Promise.all(promises);

		const successMessage = `Successfully cleared all test ranking data:\n${results.join("\n")}`;
		logCategory("RANK", `clearTestRankData(): ${successMessage}`);

		return [EResponseCode.Success, successMessage];
	}

	private async clearFloorRankTestData(): Promise<void> {
		const firestore = getFirestore();
		const batchSize = 500;

		// 1층부터 최대층까지 처리
		const availableFloors = Array.from({ length: GameBalanceConfig.num_floor }, (_, i) => i + 1);

		for (const floorNumber of availableFloors) {
			const floorCollectionRef = firestore.collection(this.getRankRootPath()).doc("Floors").collection(`Floor_${floorNumber}`);

			let hasMore = true;
			while (hasMore) {
				// 쿼리 필터로 fake_ 접두사 문서만 조회 (전수 조회 대신 필터링으로 성능 개선)
				const snapshot = await floorCollectionRef
					.where(FieldPath.documentId(), ">=", "fake_")
					.where(FieldPath.documentId(), "<", "fake_\uffff")
					.limit(batchSize)
					.get();

				if (snapshot.empty) {
					hasMore = false;
					continue;
				}

				const batch = firestore.batch();
				snapshot.forEach((doc) => {
					batch.delete(doc.ref);
				});

				await batch.commit();
				hasMore = snapshot.size === batchSize;
			}
		}
	}

	private async clearAwakenGradeRankTestData(): Promise<void> {
		const firestore = getFirestore();
		const batchSize = 500;

		// None과 unwakened를 제외한 모든 각성 등급
		const availableGrades = Object.values(EAwaken).filter((grade) => grade !== EAwaken.None && grade !== EAwaken.awaken_grade_unwakened && typeof grade === "number") as EAwaken[];

		const rankManager = new CAwakenGradeRanks(this.getServerID());

		for (const grade of availableGrades) {
			const collectionName = rankManager.getAwakenGradeCollectionName(grade);
			const gradeCollectionRef = firestore.collection(this.getRankRootPath()).doc("AwakenGrades").collection(collectionName);

			let hasMore = true;
			const userIdsToDelete: string[] = [];

			while (hasMore) {
				// 쿼리 필터로 fake_ 접두사 문서만 조회 (전수 조회 대신 필터링으로 성능 개선)
				const snapshot = await gradeCollectionRef
					.where(FieldPath.documentId(), ">=", "fake_")
					.where(FieldPath.documentId(), "<", "fake_\uffff")
					.limit(batchSize)
					.get();

				if (snapshot.empty) {
					hasMore = false;
					continue;
				}

				const batch = firestore.batch();
				snapshot.forEach((doc) => {
					batch.delete(doc.ref);
					userIdsToDelete.push(doc.id);
				});

				await batch.commit();
				hasMore = snapshot.size === batchSize;
			}

			// 대응하는 GameData/Users 경로의 테스트 Player 데이터도 삭제
			if (userIdsToDelete.length > 0) {
				await this.clearPlayerTestData(userIdsToDelete);
			}
		}
	}

	private async clearPlayerTestData(userIds: string[]): Promise<void> {
		const firestore = getFirestore();
		const batchSize = 500;
		const serverManager = ServerSelector.getInstance();
		const gameDataPath = serverManager.getGameDataPathSync(this.getServerID());

		for (let i = 0; i < userIds.length; i += batchSize) {
			const batchIds = userIds.slice(i, i + batchSize);
			const batch = firestore.batch();

			for (const userId of batchIds) {
				// fake_ 로 시작하는 테스트 데이터만 삭제
				if (userId.startsWith("fake_")) {
					const playerDocRef = firestore.collection(gameDataPath).doc("Users").collection("UUID").doc(userId).collection("Player").doc("Player");

					batch.delete(playerDocRef);

					// UUID 문서도 삭제 (빈 문서가 남지 않도록)
					const uuidDocRef = firestore.collection(gameDataPath).doc("Users").collection("UUID").doc(userId);
					batch.delete(uuidDocRef);
				}
			}

			await batch.commit();
		}
	}

	private async clearConstellationContractTestData(): Promise<void> {
		// ConstellationContract는 테스트용으로 초기화만 수행 (데이터 구조 유지)
		await initializeRankDB_ConstellationContracts(new Map<string, any>(), this.getServerID());
	}
}

export interface UpdateRankData_AwakenGrade {
	combatPower: number;
	awakenGrade: EAwaken;
}
export interface RankDB_AwakenGrade {
	rank: number;
	combatPower: number;
	awakenGrade: EAwaken;
	timestamp: FirebaseFirestore.Timestamp;
}
export type RankDB_AwakenGradeUpdatable = Omit<RankDB_AwakenGrade, "rank">;

// 유저의 현재 각성 등급 컬렉션을 추적하는 메타데이터 (불필요한 삭제 최적화용)
export interface AwakenGradeMetadata {
	currentCollection: string;  // 현재 유저가 속한 컬렉션명
	lastUpdated: FirebaseFirestore.Timestamp;
}

export class CAwakenGradeRanks extends CRankBase {
	constructor(serverID: string) {
		super(serverID);
	}

	// 메타데이터 문서 참조 헬퍼
	private getMetadataDocRef(UUID: string): DocumentReference {
		return this.firestore
			.collection(this.getRankRootPath())
			.doc("AwakenGrades")
			.collection("_metadata")
			.doc(UUID);
	}

	// AwakenGrade 기반으로 유저의 Rank 데이터를 저장 (메타데이터를 사용한 삭제 최적화)
	async UpdateRankData(data: UpdateRankData_AwakenGrade, UUID: string): Promise<[EResponseCode, string]> {
		const { combatPower, awakenGrade } = data;

		// 입력 데이터 검증
		this.validateInput_AwakenGrade(data);

		// awakenGrade가 None(0) 또는 awaken_grade_unwakened(1)일 경우 스킵
		if (awakenGrade === EAwaken.None || awakenGrade === EAwaken.awaken_grade_unwakened) {
			return resultSuccess;
		}

		const timestamp = Timestamp.now();

		await this.firestore.runTransaction(async (transaction) => {
			// 목표 Awaken Grade 컬렉션 이름
			const targetCollectionName = this.getAwakenGradeCollectionName(awakenGrade);

			// 메타데이터 문서 참조 및 조회
			const metadataRef = this.getMetadataDocRef(UUID);
			const metadataSnap = await transaction.get(metadataRef);

			// 목표 컬렉션의 문서 참조
			const targetDocRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("AwakenGrades")
				.collection(targetCollectionName)
				.doc(UUID);

			// 현재 Awaken Grade에서 유저 데이터 조회
			const targetDocSnap = await transaction.get(targetDocRef);

			if (!targetDocSnap.exists) {
				// 새로 기록되는 경우 초기 rank 설정 (예: 10000)
				const newRankData: RankDB_AwakenGrade = {
					rank: this.initialRank,
					combatPower: combatPower,
					awakenGrade,
					timestamp,
				};
				transaction.set(targetDocRef, newRankData);
			} else {
				// 기존 기록이 있는 경우 rank는 유지하고 다른 필드만 업데이트
				const updatedData: RankDB_AwakenGradeUpdatable = {
					combatPower: combatPower,
					awakenGrade,
					timestamp,
				};
				transaction.update(targetDocRef, updatedData);
			}

			// 이전 컬렉션에서 유저 문서 삭제 (메타데이터 기반 최적화)
			if (metadataSnap.exists) {
				const metadata = metadataSnap.data() as AwakenGradeMetadata;
				const previousCollection = metadata.currentCollection;

				// 이전 컬렉션과 현재 컬렉션이 다른 경우에만 삭제
				if (previousCollection && previousCollection !== targetCollectionName) {
					const previousDocRef = this.firestore
						.collection(this.getRankRootPath())
						.doc("AwakenGrades")
						.collection(previousCollection)
						.doc(UUID);
					transaction.delete(previousDocRef);
				}
			}

			// 메타데이터 업데이트
			const newMetadata: AwakenGradeMetadata = {
				currentCollection: targetCollectionName,
				lastUpdated: timestamp,
			};
			transaction.set(metadataRef, newMetadata);
		});

		return resultSuccess;
	}

	validateInput_AwakenGrade(data: UpdateRankData_AwakenGrade) {
		const { combatPower, awakenGrade } = data;
		if (combatPower === undefined || combatPower < 0 || awakenGrade === undefined || !Object.values(EAwaken).includes(awakenGrade)) {
			throwLogicError(`validateInput_AwakenGrade(): 필수 매개변수가 없거나 유효하지 않습니다. combatPower(${combatPower}), awakenGrade(${awakenGrade})`, EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	async refreshRanksDB(): Promise<void> {
		const startTime = this.startTimer();
		this.resetStats();

		const AWAKEN_GRADE_NUMBERS = Object.values(EAwaken).filter((grade) => grade !== EAwaken.None && grade !== EAwaken.awaken_grade_unwakened) as EAwaken[];
		logCategory("RANK", "CAwakenGradeRanks.refreshRanksDB(): Starting...");

		const awakenGradePromises = AWAKEN_GRADE_NUMBERS.map((awakenGrade) => this.refreshAwakenGradeRank(awakenGrade));

		await Promise.all(awakenGradePromises);

		this.logExecutionStats("CAwakenGradeRanks.refreshRanksDB", startTime);
	}

	// 유저의 combatPower를 업데이트하고, 랭크를 재계산
	private async refreshAwakenGradeRank(awakenGrade: EAwaken): Promise<void> {
		const collectionName = this.getAwakenGradeCollectionName(awakenGrade);
		const awakenCollectionRef = this.firestore
			.collection(this.getRankRootPath())
			.doc("AwakenGrades")
			.collection(collectionName);

		let lastDoc: FirebaseFirestore.QueryDocumentSnapshot | null = null;
		let processedCount = 0;
		const maxDocuments = this.querylimitCount;

		const combatPowerUpdates: { docRef: DocumentReference; combatPower: number }[] = [];

		// 페이징을 통해 문서 가져오기
		while (processedCount < maxDocuments) {
			let query = awakenCollectionRef.limit(this.batchFetchSize);

			if (lastDoc) {
				query = query.startAfter(lastDoc);
			}

			const usersSnapshot = await query.get();
			this.incrementReadCount(usersSnapshot.size);

			if (usersSnapshot.empty) break;

			// 각 유저의 combatPower 업데이트 (배치 읽기 사용)
			const batchUpdates = await this.fetchCombatPowerUpdates(usersSnapshot.docs);
			this.incrementReadCount(usersSnapshot.docs.length); // getAll() 배치 읽기 카운트

			combatPowerUpdates.push(...batchUpdates);

			lastDoc = usersSnapshot.docs[usersSnapshot.docs.length - 1];
			processedCount += usersSnapshot.size;
		}

		if (combatPowerUpdates.length > 0) {
			this.incrementWriteCount(combatPowerUpdates.length);
			await this.batchUpdateDocuments(combatPowerUpdates, (batch, { docRef, combatPower }) => {
				batch.update(docRef, { combatPower });
			});
		}

		// 업데이트된 데이터를 기반으로 랭크 재계산
		await this.recalculateRanks(awakenCollectionRef, collectionName);
	}

	// 각 유저의 CoreData_Player 문서에서 combatPower 값을 가져와 업데이트 목록을 생성
	// getAll() 배치 읽기를 사용하여 N+1 쿼리 문제 해결
	private async fetchCombatPowerUpdates(docs: FirebaseFirestore.QueryDocumentSnapshot[]): Promise<{ docRef: DocumentReference; combatPower: number }[]> {
		const serverManager = ServerSelector.getInstance();
		const gameDataPath = serverManager.getGameDataPathSync(this.serverID);

		// 모든 유저의 Player 문서 참조 배열 생성
		const playerDocRefs = docs.map((doc) => {
			return this.firestore
				.collection(gameDataPath)
				.doc("Users")
				.collection("UUID")
				.doc(doc.id)
				.collection("Player")
				.doc("Player");
		});

		// getAll()을 사용한 배치 읽기 (N+1 → 1 쿼리로 최적화)
		const playerDocs = await this.firestore.getAll(...playerDocRefs);

		const results: { docRef: DocumentReference; combatPower: number }[] = [];

		for (let i = 0; i < docs.length; i++) {
			const rankDoc = docs[i];
			const playerDoc = playerDocs[i];

			if (!playerDoc.exists) {
				logCategory("RANK", `fetchCombatPowerUpdates(): Player 문서를 찾을 수 없습니다. UUID: ${rankDoc.id}`);
				continue;
			}

			const playerData = playerDoc.data();
			if (!playerData || !playerData.CombatPower) {
				logCategory("RANK", `fetchCombatPowerUpdates(): CombatPower 필드가 없습니다. UUID: ${rankDoc.id}`);
				continue;
			}

			// combatPower 필드를 ModValueInt 형태로 처리
			const combatPowerObj = playerData.CombatPower;
			const modValueInt = ModValueInt.create(combatPowerObj.Value);
			modValueInt.ValueType = combatPowerObj.ValueType;
			const combatPower = modValueInt.GetValue();

			results.push({ docRef: rankDoc.ref, combatPower });
		}

		return results;
	}

	private async batchUpdateDocuments<T>(updates: T[], updateFunc: (batch: FirebaseFirestore.WriteBatch, update: T) => void): Promise<void> {
		const batches: FirebaseFirestore.WriteBatch[] = [];
		for (let i = 0; i < updates.length; i += this.batchSize) {
			const batch = this.firestore.batch();
			const batchUpdates = updates.slice(i, i + this.batchSize);
			batchUpdates.forEach((update) => {
				updateFunc(batch, update);
			});
			batches.push(batch);
		}

		await this.commitBatchesParallel(batches, 5);
	}

	// 업데이트된 combatPower를 기반으로 랭크를 정렬하고 업데이트
	private async recalculateRanks(awakenCollectionRef: FirebaseFirestore.CollectionReference, collectionName: string): Promise<void> {
		let lastDoc: FirebaseFirestore.QueryDocumentSnapshot | null = null;
		let processedCount = 0;
		const maxDocuments = this.querylimitCount;

		let rank = 1;
		const rankUpdates: { docRef: DocumentReference; newRank: number }[] = [];

		while (processedCount < maxDocuments) {
			let query = awakenCollectionRef.orderBy("combatPower", "desc").orderBy("timestamp", "asc").limit(this.batchFetchSize);

			if (lastDoc) {
				query = query.startAfter(lastDoc);
			}

			const updatedUsersSnapshot = await query.get();
			this.incrementReadCount(updatedUsersSnapshot.size);

			if (updatedUsersSnapshot.empty) break;

			updatedUsersSnapshot.forEach((doc) => {
				const data = doc.data() as RankDB_AwakenGrade;
				if (data.rank !== rank) {
					rankUpdates.push({ docRef: doc.ref, newRank: rank });
				}
				rank++;
			});

			lastDoc = updatedUsersSnapshot.docs[updatedUsersSnapshot.docs.length - 1];
			processedCount += updatedUsersSnapshot.size;
		}

		if (rankUpdates.length === 0) return;

		logCategory("RANK", `CAwakenGradeRanks.recalculateRanks(): ${collectionName} - 업데이트된 랭크 수: ${rankUpdates.length}`);
		this.incrementWriteCount(rankUpdates.length);

		await this.batchUpdateDocuments(rankUpdates, (batch, { docRef, newRank }) => {
			batch.update(docRef, { rank: newRank });
		});
	}

	// 모든 Awaken Grade 컬렉션 이름을 반환하는 함수
	getAllAwakenGradeCollectionNames(): string[] {
		return Object.keys(EAwaken)
			.filter((key) => typeof EAwaken[key as keyof typeof EAwaken] === "number")
			.map((key) => this.getAwakenGradeCollectionName(EAwaken[key as keyof typeof EAwaken]));
	}

	// Awaken Grade에 따른 Firestore 컬렉션 이름을 반환하는 함수
	getAwakenGradeCollectionName(awakenGrade: EAwaken): string {
		const key = utilBasic.getKeyName(EAwaken, awakenGrade);
		if (!key) {
			throwLogicError(`getAwakenGradeCollectionName(): Invalid awaken grade value: ${awakenGrade}`);
		}

		let name: string;

		if (key.startsWith("awaken_grade_")) {
			name = key.replace("awaken_grade_", "");
		}

		// 문자열의 첫 글자를 대문자로 변환
		name = utilBasic.capitalizeFirstLetter(name);

		return `Awaken_Grade_${name}`;
	}

	// 모든 각성 등급 랭킹 데이터 초기화 (시즌 리셋용)
	async resetAllRanks(): Promise<void> {
		const startTime = this.startTimer();
		this.resetStats();

		logCategory("RANK", "CAwakenGradeRanks.resetAllRanks(): 각성 랭킹 리셋 시작...");

		// None과 unwakened를 제외한 모든 각성 등급
		const AWAKEN_GRADES = Object.values(EAwaken).filter(
			(grade) => grade !== EAwaken.None && grade !== EAwaken.awaken_grade_unwakened && typeof grade === "number"
		) as EAwaken[];

		for (const awakenGrade of AWAKEN_GRADES) {
			const collectionName = this.getAwakenGradeCollectionName(awakenGrade);
			const awakenCollectionRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("AwakenGrades")
				.collection(collectionName);

			let deletedCount = 0;
			let snapshot = await awakenCollectionRef.limit(this.batchSize).get();

			while (!snapshot.empty) {
				const batch = this.firestore.batch();
				snapshot.docs.forEach((doc) => batch.delete(doc.ref));
				await batch.commit();

				deletedCount += snapshot.size;
				this.incrementDeleteCount(snapshot.size);

				snapshot = await awakenCollectionRef.limit(this.batchSize).get();
			}

			if (deletedCount > 0) {
				logCategory("RANK", `CAwakenGradeRanks.resetAllRanks(): ${collectionName} - ${deletedCount}건 삭제`);
			}
		}

		// 메타데이터도 삭제
		const metadataCollectionRef = this.firestore
			.collection(this.getRankRootPath())
			.doc("AwakenGrades")
			.collection("_metadata");

		let metadataDeleted = 0;
		let metadataSnapshot = await metadataCollectionRef.limit(this.batchSize).get();

		while (!metadataSnapshot.empty) {
			const batch = this.firestore.batch();
			metadataSnapshot.docs.forEach((doc) => batch.delete(doc.ref));
			await batch.commit();

			metadataDeleted += metadataSnapshot.size;
			this.incrementDeleteCount(metadataSnapshot.size);

			metadataSnapshot = await metadataCollectionRef.limit(this.batchSize).get();
		}

		if (metadataDeleted > 0) {
			logCategory("RANK", `CAwakenGradeRanks.resetAllRanks(): _metadata - ${metadataDeleted}건 삭제`);
		}

		this.logExecutionStats("CAwakenGradeRanks.resetAllRanks", startTime);
		logCategory("RANK", "CAwakenGradeRanks.resetAllRanks(): 각성 랭킹 리셋 완료");
	}

	// 랭킹 보상 지급용 - 특정 각성 등급의 모든 유저를 순위와 함께 가져오기 (총 유저 수 포함)
	async getUsersWithRankByGrade(awakenGrade: EAwaken): Promise<{ users: Array<{ uuid: string; rank: number; data: RankDB_AwakenGrade }>; totalCount: number }> {
		const collectionName = this.getAwakenGradeCollectionName(awakenGrade);
		const awakenCollectionRef = this.firestore
			.collection(this.getRankRootPath())
			.doc("AwakenGrades")
			.collection(collectionName);

		const users: Array<{ uuid: string; rank: number; data: RankDB_AwakenGrade }> = [];
		let lastDoc: FirebaseFirestore.QueryDocumentSnapshot | null = null;
		let processedCount = 0;

		while (processedCount < this.querylimitCount) {
			let query = awakenCollectionRef
				.orderBy("rank", "asc")
				.limit(this.batchFetchSize);

			if (lastDoc) {
				query = query.startAfter(lastDoc);
			}

			const snapshot = await query.get();
			this.incrementReadCount(snapshot.size);

			if (snapshot.empty) break;

			snapshot.forEach((doc) => {
				const data = doc.data() as RankDB_AwakenGrade;
				users.push({
					uuid: doc.id,
					rank: data.rank,
					data,
				});
			});

			lastDoc = snapshot.docs[snapshot.docs.length - 1];
			processedCount += snapshot.size;
		}

		return { users, totalCount: users.length };
	}

	// 랭킹 보상 지급용 - 모든 각성 등급의 모든 유저를 순위와 함께 가져오기
	// 각성 등급별 최고 순위 유저만 반환 (유저는 하나의 등급에만 존재)
	async getAllUsersWithRank(): Promise<Map<string, { uuid: string; rank: number; awakenGrade: EAwaken; combatPower: number }>> {
		const startTime = this.startTimer();
		this.resetStats();

		const AWAKEN_GRADES = Object.values(EAwaken).filter(
			(grade) => grade !== EAwaken.None && grade !== EAwaken.awaken_grade_unwakened && typeof grade === "number"
		) as EAwaken[];

		const userRankMap = new Map<string, { uuid: string; rank: number; awakenGrade: EAwaken; combatPower: number }>();

		logCategory("RANK", "CAwakenGradeRanks.getAllUsersWithRank(): 모든 유저 랭킹 조회 시작...");

		for (const awakenGrade of AWAKEN_GRADES) {
			const { users } = await this.getUsersWithRankByGrade(awakenGrade);

			for (const user of users) {
				// 유저는 하나의 각성 등급에만 존재
				if (!userRankMap.has(user.uuid)) {
					userRankMap.set(user.uuid, {
						uuid: user.uuid,
						rank: user.rank,
						awakenGrade,
						combatPower: user.data.combatPower,
					});
				}
			}
		}

		this.logExecutionStats("CAwakenGradeRanks.getAllUsersWithRank", startTime);
		logCategory("RANK", `CAwakenGradeRanks.getAllUsersWithRank(): 총 ${userRankMap.size}명 조회 완료`);

		return userRankMap;
	}
}

export interface UpdateRankData_ConstellationContract {
	prevConstellation: EConstellation; // 이전 계약
	newConstellation: EConstellation; // 새 계약
}
export interface RankDB_ConstellationContract {
	rank: number;
	strNickName: string; // 성좌를 이어 받은 유저 닉네임
	constellation: EConstellation;
}
export type RankDB_ConstellationContractUpdatable = Omit<RankDB_ConstellationContract, "rank">;

const NUM_SHARDS = 10; // 샤드 수

export class CConstellationContractRanks extends CRankBase {
	constructor(serverID: string) {
		super(serverID);
	}

	async UpdateRankData(data: UpdateRankData_ConstellationContract): Promise<[EResponseCode, string]> {
		const { prevConstellation, newConstellation } = data;

		this.validateInput_Constellation(data);

		const batch = this.firestore.batch();

		// 새로운 Constellation의 contractCount 증가
		const newCollectionName = this.getConstellationCollectionName(newConstellation);
		const newShardId = Math.floor(Math.random() * NUM_SHARDS).toString();
		// prettier-ignore
		const newShardRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("ConstellationContract")
				.collection("ConstellationContract")
				.doc(newCollectionName)
				.collection("shards")
				.doc(newShardId);
		batch.set(newShardRef, { contractCount: FieldValue.increment(1) }, { merge: true });

		// 이전 Constellation이 None이 아닌 경우에만 처리
		if (prevConstellation !== EConstellation.None) {
			const prevCollectionName = this.getConstellationCollectionName(prevConstellation);
			const prevShardId = Math.floor(Math.random() * NUM_SHARDS).toString();
			// prettier-ignore
			const prevShardRef = this.firestore
					.collection(this.getRankRootPath())
					.doc("ConstellationContract")
					.collection("ConstellationContract")
					.doc(prevCollectionName)
					.collection("shards")
					.doc(prevShardId);
			batch.set(prevShardRef, { contractCount: FieldValue.increment(-1) }, { merge: true });
		}

		await batch.commit();

		return resultSuccess;
	}

	validateInput_Constellation(data: UpdateRankData_ConstellationContract): [EResponseCode, string] {
		const { prevConstellation, newConstellation } = data;

		if (prevConstellation == null || newConstellation == null || !Object.values(EConstellation).includes(prevConstellation) || !Object.values(EConstellation).includes(newConstellation)) {
			throwLogicError(`validateInput_Constellation(): 필수 매개변수가 없거나 유효하지 않습니다. prevConstellation(${prevConstellation}), newConstellation(${newConstellation})`, EAPIErrorCode.INVALID_PARAMETERS);
		}
		return resultSuccess;
	}

	getConstellationCollectionName(constellation: EConstellation): string {
		const key = utilBasic.getKeyName(EConstellation, constellation);
		if (!key) {
			throwLogicError(`getConstellationCollectionName(): Invalid constellation value: ${constellation}`);
		}

		let name: string;

		if (key.startsWith("constellation_")) {
			name = key.replace("constellation_", "");
		}

		// 문자열의 첫 글자를 대문자로 변환
		name = utilBasic.capitalizeFirstLetter(name);

		return `Constellation_${name}`;
	}

	async refreshRanksDB(): Promise<void> {
		logCategory("RANK", "CConstellationContractRanks.refreshRanks(): Starting...");

		// prettier-ignore
		const collectionRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("ConstellationContract")
				.collection("ConstellationContract");
		const constellationDocs = await collectionRef.get();

		if (constellationDocs.empty) {
			logCategory("RANK", "CConstellationContractRanks.refreshRanks(): No constellations found.");
			return;
		}

		// 각 성좌의 총 contractCount 계산 (Promise.all 결과로 배열 수집)
		const constellationCounts = await Promise.all(
			constellationDocs.docs.map(async (doc) => {
				const constellationName = doc.id;
				const docRef = doc.ref;

				const shardsRef = docRef.collection("shards");
				const shardDocs = await shardsRef.get();

				let totalCount = 0;
				shardDocs.forEach((shardDoc) => {
					const data = shardDoc.data();
					totalCount += data.contractCount || 0;
				});

				return { constellationName, totalCount, docRef };
			})
		);

		// 총계를 기준으로 내림차순 정렬하여 순위 결정
		constellationCounts.sort((a, b) => b.totalCount - a.totalCount);

		let rank = 1;
		const updates: { docRef: FirebaseFirestore.DocumentReference; newRank: number }[] = [];

		for (const item of constellationCounts) {
			const data = (await item.docRef.get()).data() as RankDB_ConstellationContract;
			if (data.rank !== rank) {
				updates.push({ docRef: item.docRef, newRank: rank });
			}
			rank++;
		}

		if (updates.length === 0) {
			return;
		}

		logCategory("RANK", `refreshConstellationContractRanks(): Rank updates to perform: ${updates.length}`);

		const batches: FirebaseFirestore.WriteBatch[] = [];
		const batchSize = this.batchSize || 500; // batchSize가 정의되어 있지 않으면 기본값 500 사용

		for (let i = 0; i < updates.length; i += batchSize) {
			const batch = this.firestore.batch();
			const batchUpdates = updates.slice(i, i + batchSize);
			batchUpdates.forEach(({ docRef, newRank }) => {
				batch.update(docRef, { rank: newRank });
			});
			batches.push(batch);
		}

		// 병렬로 배치 커밋 실행
		await Promise.all(batches.map((batch) => batch.commit()));

		logCategory("RANK", "CConstellationContractRanks.refreshRanks(): Completed");
	}

	// 모든 성좌 계약 랭킹 데이터 초기화 (시즌 리셋용)
	async resetAllRanks(): Promise<void> {
		logCategory("RANK", "CConstellationContractRanks.resetAllRanks(): 성좌 계약 랭킹 리셋 시작...");

		const collectionRef = this.firestore
			.collection(this.getRankRootPath())
			.doc("ConstellationContract")
			.collection("ConstellationContract");

		const constellationDocs = await collectionRef.get();

		if (constellationDocs.empty) {
			logCategory("RANK", "CConstellationContractRanks.resetAllRanks(): No constellations found.");
			return;
		}

		// 각 성좌 문서의 rank를 초기값으로 리셋하고 샤드 카운터 초기화
		for (const doc of constellationDocs.docs) {
			const batch = this.firestore.batch();

			// rank를 초기값으로 리셋
			batch.update(doc.ref, { rank: this.initialRank });

			// 샤드 카운터 초기화
			const shardsRef = doc.ref.collection("shards");
			const shardDocs = await shardsRef.get();

			shardDocs.forEach((shardDoc) => {
				batch.update(shardDoc.ref, { contractCount: 0 });
			});

			await batch.commit();

			logCategory("RANK", `CConstellationContractRanks.resetAllRanks(): ${doc.id} - rank 및 샤드 카운터 초기화 완료`);
		}

		logCategory("RANK", "CConstellationContractRanks.resetAllRanks(): 성좌 계약 랭킹 리셋 완료");
	}
}

// -----------------------------------------------
// Function
// -----------------------------------------------
// Rank/ConstellationContract/ConstellationContractRanking 데이터 생성/초기화
export async function initializeRankDB_ConstellationContracts(InResultData: Map<string, any>, serverID: string) {
	const firestore = getFirestore();
	const batch = firestore.batch();

	const serverManager = ServerSelector.getInstance();
	const validatedServerID = serverManager.validateAndGetServerIDSync(serverID);
	const rankRootPath = serverManager.getRankPathSync(validatedServerID);

	const constellations = Object.entries(EConstellation).filter(([key, _]) => key !== "None") as Array<[keyof typeof EConstellation, EConstellation]>;

	for (const [key, value] of constellations) {
		const collectionName = getConstellationCollectionName(value);
		// prettier-ignore
		const docRef = firestore
				.collection(rankRootPath)
				.doc("ConstellationContract")
				.collection("ConstellationContract")
				.doc(collectionName);

		// 메인 도큐먼트 초기화
		const initialData: RankDB_ConstellationContract = {
			rank: 10000,
			strNickName: "",
			constellation: value,
			// 샤드 카운터를 사용하므로 메인 도큐먼트에서는 contractCount를 제거합니다.
		};

		batch.set(docRef, initialData);

		// 각 성좌에 대한 샤드 도큐먼트 초기화
		for (let i = 0; i < NUM_SHARDS; i++) {
			const shardId = i.toString();
			const shardRef = docRef.collection("shards").doc(shardId);
			batch.set(shardRef, { contractCount: 0 });
		}
	}

	await batch.commit();

	logCategory("RANK", "[initializeRankDB] 성공: 초기 RankDB_ConstellationContract 필드가 성공적으로 설정되었습니다.");
	return InResultData.set(ConstKey.SUCCESSKEY, true);
}

function getConstellationCollectionName(constellation: EConstellation): string {
	const key = utilBasic.getKeyName(EConstellation, constellation);
	if (!key) {
		throwLogicError(`getConstellationCollectionName(): Invalid constellation value: ${constellation}`);
	}

	let name: string = key;

	if (key.startsWith("constellation_")) {
		name = key.replace("constellation_", "");
	}

	// 문자열의 첫 글자를 대문자로 변환
	name = utilBasic.capitalizeFirstLetter(name);

	return `Constellation_${name}`;
}

// -----------------------------------------------
// Fake Data Generation Classes
// -----------------------------------------------
export class CInsertFakeData_FloorRank extends CRequestBase {
	constructor(init?: Partial<CInsertFakeData_FloorRank>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const userCount: number = data["userCount"] || 50; // 기본값 50명
		const floorNumber: number = data["floorNumber"] || 2; // 기본값 2층
		return await this.insertFakeFloorRankData(userCount, floorNumber);
	}

	async insertFakeFloorRankData(userCount: number, floorNumber: number): Promise<[EResponseCode, string]> {
		if (userCount <= 0 || userCount > 1000) {
			throwLogicError("insertFakeFloorRankData(): userCount must be between 1 and 1000", EAPIErrorCode.INVALID_PARAMETERS);
		}

		if (floorNumber < 1 || floorNumber > GameBalanceConfig.num_floor) {
			throwLogicError(`insertFakeFloorRankData(): floorNumber must be between 1 and ${GameBalanceConfig.num_floor}`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		const awakenGrades = [EAwaken.awaken_grade_a, EAwaken.awaken_grade_b, EAwaken.awaken_grade_c, EAwaken.awaken_grade_d, EAwaken.awaken_grade_s];

		// 에뮬레이터 환경에서는 동시성 제한 없이 실행
		const isEmulator = utilBasic.isEmulatorEnvironment();
		const CONCURRENT_LIMIT = 20;
		const promises: Promise<[EResponseCode, string]>[] = [];

		for (let i = 1; i <= userCount; i++) {
			const fakeUUID = `fake_floor_user_${floorNumber}_${i}_${Date.now()}`;
			const fakeData: UpdateRankData_Floor = {
				floorNumber: floorNumber,
				strNickName: `FakeUser${i}`,
				bestStageMain: Math.floor(Math.random() * 1000) + 100, // 100~1099 랜덤 스테이지
				awakenGrade: awakenGrades[Math.floor(Math.random() * awakenGrades.length)],
			};

			// 각 호출마다 새 인스턴스 생성하여 병렬 처리
			const floorRanks = new CFloorRanks(this.serverID);
			promises.push(floorRanks.updateRankData(fakeData, fakeUUID));

			// 프로덕션 환경에서만 배치 처리
			if (!isEmulator && promises.length >= CONCURRENT_LIMIT) {
				await Promise.all(promises);
				promises.length = 0;
			}
		}

		// 남은 작업 처리
		if (promises.length > 0) {
			await Promise.all(promises);
		}

		logCategory("RANK", `insertFakeFloorRankData(): Successfully inserted ${userCount} users for floor ${floorNumber}`);
		return resultSuccess;
	}
}

export class CInsertFakeData_AwakenGradeRank extends CRequestBase {
	constructor(init?: Partial<CInsertFakeData_AwakenGradeRank>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.player, CoreDataKey.COREDATAKEY_PLAYER);
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const userCount: number = data["userCount"] || 50; // 기본값 50명
		const awakenGrade: EAwaken = data["awakenGrade"] || EAwaken.awaken_grade_a; // 기본값 A등급
		return await this.insertFakeAwakenGradeRankData(userCount, awakenGrade);
	}

	// 가짜 각성 등급 랭킹 데이터 삽입 (각 호출마다 새 인스턴스 생성하여 상태 충돌 방지)
	async insertFakeAwakenGradeRankData(userCount: number, awakenGrade: EAwaken): Promise<[EResponseCode, string]> {
		if (userCount <= 0 || userCount > 1000) {
			throwLogicError("insertFakeAwakenGradeRankData(): userCount must be between 1 and 1000", EAPIErrorCode.INVALID_PARAMETERS);
		}

		if (awakenGrade === EAwaken.None || awakenGrade === EAwaken.awaken_grade_unwakened) {
			throwLogicError("insertFakeAwakenGradeRankData(): Invalid awaken grade", EAPIErrorCode.INVALID_PARAMETERS);
		}

		const firestore = getFirestore();
		const serverManager = ServerSelector.getInstance();
		const gameDataPath = serverManager.getGameDataPathSync(this.serverID);

		// 에뮬레이터 환경에서는 동시성 제한 없이 실행
		const isEmulator = utilBasic.isEmulatorEnvironment();
		const CONCURRENT_LIMIT = 20;
		const promises: Promise<any>[] = [];

		for (let i = 1; i <= userCount; i++) {
			const fakeUUID = `fake_awaken_user_${awakenGrade}_${i}_${Date.now()}`;
			const fakeCombatPower = Math.floor(Math.random() * 50000) + 1000; // 1000~51000 랜덤 전투력

			const fakeData: UpdateRankData_AwakenGrade = {
				combatPower: fakeCombatPower,
				awakenGrade: awakenGrade,
			};

			// CoreData_Player에도 가짜 데이터 생성 (combatPower 조회용)
			const playerDocRef = firestore.collection(gameDataPath).doc("Users").collection("UUID").doc(fakeUUID).collection("Player").doc("Player");

			const playerData = {
				CombatPower: ModValueInt.create(fakeCombatPower).toObject(),
			};

			// 각 호출마다 새 인스턴스 생성
			const awakenGradeRanks = new CAwakenGradeRanks(this.serverID);
			promises.push(Promise.all([awakenGradeRanks.UpdateRankData(fakeData, fakeUUID), playerDocRef.set(playerData)]));

			// 프로덕션 환경에서만 배치 처리
			if (!isEmulator && promises.length >= CONCURRENT_LIMIT) {
				await Promise.all(promises);
				promises.length = 0;
			}
		}

		// 남은 작업 처리
		if (promises.length > 0) {
			await Promise.all(promises);
		}

		logCategory("RANK", `insertFakeAwakenGradeRankData(): Successfully inserted ${userCount} users for awaken grade ${awakenGrade}`);
		return resultSuccess;
	}
}

export class CInsertFakeData_ConstellationContract extends CRequestBase {
	constructor(init?: Partial<CInsertFakeData_ConstellationContract>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const contractCount: number = data["contractCount"] || 100; // 기본값 100개 계약
		return await this.insertFakeConstellationContractData(contractCount);
	}

	// 가짜 성좌 계약 데이터 삽입 (각 호출마다 새 인스턴스 생성하여 상태 충돌 방지)
	async insertFakeConstellationContractData(contractCount: number): Promise<[EResponseCode, string]> {
		if (contractCount <= 0 || contractCount > 10000) {
			throwLogicError("insertFakeConstellationContractData(): contractCount must be between 1 and 10000", EAPIErrorCode.INVALID_PARAMETERS);
		}

		// None을 제외한 모든 성좌 목록
		const constellations = Object.values(EConstellation).filter((constellation) => constellation !== EConstellation.None && typeof constellation === "number") as EConstellation[];

		if (constellations.length === 0) {
			throwLogicError("insertFakeConstellationContractData(): No valid constellations found", EAPIErrorCode.SERVER_ERROR);
		}

		// 에뮬레이터 환경에서는 동시성 제한 없이 실행
		const isEmulator = utilBasic.isEmulatorEnvironment();
		const CONCURRENT_LIMIT = 20;
		const promises: Promise<any>[] = [];

		for (let i = 1; i <= contractCount; i++) {
			// 랜덤한 성좌 선택
			const randomConstellation = constellations[Math.floor(Math.random() * constellations.length)];

			const fakeData: UpdateRankData_ConstellationContract = {
				prevConstellation: EConstellation.None, // 새로운 계약으로 가정
				newConstellation: randomConstellation,
			};

			// 각 호출마다 새 인스턴스 생성
			const constellationContractRanks = new CConstellationContractRanks(this.serverID);
			promises.push(constellationContractRanks.UpdateRankData(fakeData));

			// 프로덕션 환경에서만 배치 처리
			if (!isEmulator && promises.length >= CONCURRENT_LIMIT) {
				await Promise.all(promises);
				promises.length = 0;
			}
		}

		// 남은 작업 처리
		if (promises.length > 0) {
			await Promise.all(promises);
		}

		logCategory("RANK", `insertFakeConstellationContractData(): Successfully created ${contractCount} fake contracts`);
		return resultSuccess;
	}
}

// -----------------------------------------------
// Hall of Fame Classes - 명예의 전당 시스템
// -----------------------------------------------

// 성좌 최초 달성자 데이터 인터페이스
export interface UpdateRankData_ConstellationFirstAchiever {
	strNickName: string;
	constellation: EConstellation;
	timestamp: FirebaseFirestore.Timestamp;
}

export interface RankDB_ConstellationFirstAchiever {
	rank: number; // 달성 순서 (1~25)
	UUID: string;
	strNickName: string;
	constellation: EConstellation;
	timestamp: FirebaseFirestore.Timestamp;
}

// 층별 최초 클리어 데이터 인터페이스
export interface UpdateRankData_FloorFirstClear {
	floorNumber: number;
	strNickName: string;
	timestamp: FirebaseFirestore.Timestamp;
	bestStage: number;
}

export interface RankDB_FloorFirstClear {
	rank: number; // 클리어 순서 (1~5)
	UUID: string;
	strNickName: string;
	timestamp: FirebaseFirestore.Timestamp;
	bestStage: number;
}

// 총 플레이 타임 데이터 인터페이스
export interface UpdateRankData_TotalPlayTime {
	totalPlayTimeSeconds: number;
}

export interface RankDB_TotalPlayTime {
	rank: number;
	strNickName: string;
	totalPlayTimeSeconds: number;
	timestamp: FirebaseFirestore.Timestamp;
}

// 퀘스트 달성 횟수 데이터 인터페이스
export interface UpdateRankData_QuestAchievement {
	questCompletionCount: number;
}

export interface RankDB_QuestAchievement {
	rank: number;
	strNickName: string;
	questCompletionCount: number;
	timestamp: FirebaseFirestore.Timestamp;
}

// 성좌 최초 달성자 클래스 (25명 제한)
export class CHallOfFame_ConstellationFirstAchievers extends CRankBase {
	private readonly maxAchievers = 25;

	constructor(serverID: string) {
		super(serverID);
	}

	async recordFirstAchiever(data: UpdateRankData_ConstellationFirstAchiever, UUID: string): Promise<[EResponseCode, string]> {
		const { strNickName, constellation, timestamp } = data;

		this.validateInput_ConstellationFirstAchiever(data);

		await this.firestore.runTransaction(async (transaction) => {
			const collectionRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("HallOfFame")
				.collection("ConstellationFirst");

			// 이미 기록된 성좌인지 확인
			const existingQuery = await transaction.get(collectionRef.where("constellation", "==", constellation));
			if (!existingQuery.empty) {
				throwLogicError(`recordFirstAchiever(): Constellation ${constellation} already has a first achiever`, EAPIErrorCode.INVALID_PARAMETERS);
			}

			// 현재 등록된 달성자 수 확인
			const countQuery = await transaction.get(collectionRef);
			if (countQuery.size >= this.maxAchievers) {
				throwLogicError(`throwLogicError(): Maximum ${this.maxAchievers} constellation first achievers already recorded`, EAPIErrorCode.INVALID_PARAMETERS);
			}

			const newRank = countQuery.size + 1;
			const newRecord: RankDB_ConstellationFirstAchiever = {
				rank: newRank,
				UUID,
				strNickName,
				constellation,
				timestamp
			};

			// 타임스탬프를 기반으로 한 문서 ID 생성 (순서 보장)
			const docId = `${timestamp.toMillis()}_${UUID}`;
			transaction.set(collectionRef.doc(docId), newRecord);
		});

		return resultSuccess;
	}

	private validateInput_ConstellationFirstAchiever(data: UpdateRankData_ConstellationFirstAchiever) {
		const { strNickName, constellation, timestamp } = data;
		if (!strNickName || !constellation || !timestamp) {
			throwLogicError("validateInput_ConstellationFirstAchiever(): Required parameters missing or invalid", EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	async refreshRanksDB(): Promise<void> {
		// 타임스탬프 순서로 이미 정렬되어 있으므로 rank 재계산만 수행
		logCategory("RANK", "CHallOfFame_ConstellationFirstAchievers.refreshRanks(): Starting...");

		const collectionRef = this.firestore
			.collection(this.getRankRootPath())
			.doc("HallOfFame")
			.collection("ConstellationFirst");

		const snapshot = await collectionRef.orderBy("timestamp", "asc").get();

		if (snapshot.empty) return;

		const batches: WriteBatch[] = [];
		let currentBatch = this.firestore.batch();
		let batchSize = 0;
		let rank = 1;

		snapshot.docs.forEach((doc) => {
			const data = doc.data() as RankDB_ConstellationFirstAchiever;
			if (data.rank !== rank) {
				currentBatch.update(doc.ref, { rank });
				batchSize++;

				if (batchSize >= this.batchSize) {
					batches.push(currentBatch);
					currentBatch = this.firestore.batch();
					batchSize = 0;
				}
			}
			rank++;
		});

		if (batchSize > 0) {
			batches.push(currentBatch);
		}

		await this.commitBatchesParallel(batches, 5);
		logCategory("RANK", "CHallOfFame_ConstellationFirstAchievers.refreshRanks(): Completed");
	}
}

// 층별 최초 클리어 클래스 (각 층당 5명 제한)
export class CHallOfFame_FloorFirstClears extends CRankBase {
	private readonly maxClearsPerFloor = 5;

	constructor(serverID: string) {
		super(serverID);
	}

	async recordFirstClear(data: UpdateRankData_FloorFirstClear, UUID: string): Promise<[EResponseCode, string, number]> {
		const { floorNumber, strNickName, timestamp, bestStage } = data;

		this.validateInput_FloorFirstClear(data);

		const achievedRank = await this.firestore.runTransaction(async (transaction) => {
			const collectionRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("HallOfFame")
				.collection("FloorFirstClear")
				.doc(`Floor_${floorNumber}`)
				.collection("Achievers");

			// 이미 등록된 유저면 기존 rank 반환 (멱등성 보장)
			const existingDoc = await transaction.get(collectionRef.doc(UUID));
			if (existingDoc.exists) {
				return existingDoc.data()!.rank as number;
			}

			// 현재 등록된 클리어 수 확인
			const countQuery = await transaction.get(collectionRef);
			if (countQuery.size >= this.maxClearsPerFloor) {
				throwLogicError(`recordFirstClear(): Maximum ${this.maxClearsPerFloor} first clears already recorded for floor ${floorNumber}`, EAPIErrorCode.INVALID_PARAMETERS);
			}

			const newRank = countQuery.size + 1;
			const newRecord: RankDB_FloorFirstClear = {
				rank: newRank,
				UUID,
				strNickName,
				timestamp,
				bestStage
			};

			transaction.set(collectionRef.doc(UUID), newRecord);
			return newRank;
		});

		return [EResponseCode.Success, "First clear recorded", achievedRank];
	}

	private validateInput_FloorFirstClear(data: UpdateRankData_FloorFirstClear) {
		const { floorNumber, strNickName, timestamp, bestStage } = data;
		if (!floorNumber || floorNumber < 1 || floorNumber > 10 || !strNickName || !timestamp || !bestStage || bestStage < 1) {
			throwLogicError("validateInput_FloorFirstClear(): Required parameters missing or invalid", EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	async refreshRanksDB(): Promise<void> {
		logCategory("RANK", "CHallOfFame_FloorFirstClears.refreshRanks(): Starting...");

		// 1~10층까지 각각 처리
		const floorPromises = Array.from({ length: 10 }, (_, i) => i + 1).map(async (floorNumber) => {
			const collectionRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("HallOfFame")
				.collection("FloorFirstClear")
				.doc(`Floor_${floorNumber}`)
				.collection("Achievers");

			const snapshot = await collectionRef.orderBy("timestamp", "asc").get();

			if (snapshot.empty) return;

			const batches: WriteBatch[] = [];
			let currentBatch = this.firestore.batch();
			let batchSize = 0;
			let rank = 1;

			snapshot.docs.forEach((doc) => {
				const data = doc.data() as RankDB_FloorFirstClear;
				if (data.rank !== rank) {
					currentBatch.update(doc.ref, { rank });
					batchSize++;

					if (batchSize >= this.batchSize) {
						batches.push(currentBatch);
						currentBatch = this.firestore.batch();
						batchSize = 0;
					}
				}
				rank++;
			});

			if (batchSize > 0) {
				batches.push(currentBatch);
			}

			await this.commitBatchesParallel(batches, 5);
		});

		await Promise.all(floorPromises);
		logCategory("RANK", "CHallOfFame_FloorFirstClears.refreshRanks(): Completed");
	}
}

// 총 플레이 타임 랭킹 클래스
export class CHallOfFame_TotalPlayTime extends CRankBase {
	constructor(serverID: string) {
		super(serverID);
	}

	async updateRankData(data: UpdateRankData_TotalPlayTime, UUID: string, strNickName: string): Promise<[EResponseCode, string]> {
		const { totalPlayTimeSeconds } = data;

		this.validateInput_TotalPlayTime(data);

		const timestamp = Timestamp.now();

		await this.firestore.runTransaction(async (transaction) => {
			const docRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("HallOfFame")
				.collection("LegendRecord")
				.doc("TotalPlayTime")
				.collection("TotalPlayTime")
				.doc(UUID);

			const docSnap = await transaction.get(docRef);

			if (!docSnap.exists) {
				const newRecord: RankDB_TotalPlayTime = {
					rank: this.initialRank,
					strNickName,
					totalPlayTimeSeconds,
					timestamp
				};
				transaction.set(docRef, newRecord);
			} else {
				const updatedRecord = {
					strNickName,
					totalPlayTimeSeconds,
					timestamp
				};
				transaction.update(docRef, updatedRecord);
			}
		});

		return resultSuccess;
	}

	private validateInput_TotalPlayTime(data: UpdateRankData_TotalPlayTime) {
		const { totalPlayTimeSeconds } = data;
		if (totalPlayTimeSeconds === undefined || totalPlayTimeSeconds < 0) {
			throwLogicError("validateInput_TotalPlayTime(): totalPlayTimeSeconds must be >= 0", EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	async refreshRanksDB(): Promise<void> {
		logCategory("RANK", "CHallOfFame_TotalPlayTime.refreshRanks(): Starting...");

		const collectionRef = this.firestore
			.collection(this.getRankRootPath())
			.doc("HallOfFame")
			.collection("LegendRecord")
			.doc("TotalPlayTime")
			.collection("TotalPlayTime");

		let lastDoc: FirebaseFirestore.QueryDocumentSnapshot | null = null;
		let processedCount = 0;
		let rank = 1;

		const updates: { docRef: DocumentReference; newRank: number }[] = [];

		while (processedCount < this.querylimitCount) {
			let query = collectionRef
				.orderBy("totalPlayTimeSeconds", "desc")
				.orderBy("timestamp", "asc")
				.limit(this.batchFetchSize);

			if (lastDoc) {
				query = query.startAfter(lastDoc);
			}

			const snapshot = await query.get();
			if (snapshot.empty) break;

			snapshot.forEach((doc) => {
				const data = doc.data() as RankDB_TotalPlayTime;
				if (data.rank !== rank) {
					updates.push({ docRef: doc.ref, newRank: rank });
				}
				rank++;
			});

			lastDoc = snapshot.docs[snapshot.docs.length - 1];
			processedCount += snapshot.size;
		}

		if (updates.length > 0) {
			const batches: WriteBatch[] = [];
			for (let i = 0; i < updates.length; i += this.batchSize) {
				const batch = this.firestore.batch();
				const batchUpdates = updates.slice(i, i + this.batchSize);
				batchUpdates.forEach(({ docRef, newRank }) => {
					batch.update(docRef, { rank: newRank });
				});
				batches.push(batch);
			}

			await this.commitBatchesParallel(batches, 5);
		}

		logCategory("RANK", "CHallOfFame_TotalPlayTime.refreshRanks(): Completed");
	}
}

// 퀘스트 달성 횟수 랭킹 클래스
export class CHallOfFame_QuestAchievements extends CRankBase {
	constructor(serverID: string) {
		super(serverID);
	}

	async updateRankData(data: UpdateRankData_QuestAchievement, UUID: string, strNickName: string): Promise<[EResponseCode, string]> {
		const { questCompletionCount } = data;

		this.validateInput_QuestAchievement(data);

		const timestamp = Timestamp.now();

		await this.firestore.runTransaction(async (transaction) => {
			const docRef = this.firestore
				.collection(this.getRankRootPath())
				.doc("HallOfFame")
				.collection("LegendRecord")
				.doc("QuestAchievements")
				.collection("QuestAchievements")
				.doc(UUID);

			const docSnap = await transaction.get(docRef);

			if (!docSnap.exists) {
				const newRecord: RankDB_QuestAchievement = {
					rank: this.initialRank,
					strNickName,
					questCompletionCount,
					timestamp
				};
				transaction.set(docRef, newRecord);
			} else {
				const updatedRecord = {
					strNickName,
					questCompletionCount,
					timestamp
				};
				transaction.update(docRef, updatedRecord);
			}
		});

		return resultSuccess;
	}

	private validateInput_QuestAchievement(data: UpdateRankData_QuestAchievement) {
		const { questCompletionCount } = data;
		if (questCompletionCount === undefined || questCompletionCount < 0) {
			throwLogicError("validateInput_QuestAchievement(): questCompletionCount must be >= 0", EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	async refreshRanksDB(): Promise<void> {
		logCategory("RANK", "CHallOfFame_QuestAchievements.refreshRanks(): Starting...");

		const collectionRef = this.firestore
			.collection(this.getRankRootPath())
			.doc("HallOfFame")
			.collection("LegendRecord")
			.doc("QuestAchievements")
			.collection("QuestAchievements");

		let lastDoc: FirebaseFirestore.QueryDocumentSnapshot | null = null;
		let processedCount = 0;
		let rank = 1;

		const updates: { docRef: DocumentReference; newRank: number }[] = [];

		while (processedCount < this.querylimitCount) {
			let query = collectionRef
				.orderBy("questCompletionCount", "desc")
				.orderBy("timestamp", "asc")
				.limit(this.batchFetchSize);

			if (lastDoc) {
				query = query.startAfter(lastDoc);
			}

			const snapshot = await query.get();
			if (snapshot.empty) break;

			snapshot.forEach((doc) => {
				const data = doc.data() as RankDB_QuestAchievement;
				if (data.rank !== rank) {
					updates.push({ docRef: doc.ref, newRank: rank });
				}
				rank++;
			});

			lastDoc = snapshot.docs[snapshot.docs.length - 1];
			processedCount += snapshot.size;
		}

		if (updates.length > 0) {
			const batches: WriteBatch[] = [];
			for (let i = 0; i < updates.length; i += this.batchSize) {
				const batch = this.firestore.batch();
				const batchUpdates = updates.slice(i, i + this.batchSize);
				batchUpdates.forEach(({ docRef, newRank }) => {
					batch.update(docRef, { rank: newRank });
				});
				batches.push(batch);
			}

			await this.commitBatchesParallel(batches, 5);
		}

		logCategory("RANK", "CHallOfFame_QuestAchievements.refreshRanks(): Completed");
	}
}
