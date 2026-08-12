// 랭킹 보상 지급 시스템
// 일일 랭킹 보상 지급 (스테이지 랭킹, 헌터 랭킹)
//
// [보상 지급 정책]
// - 스테이지 랭킹: 매일 00:00 KST에 보상 지급
// - 헌터 랭킹: 매일 00:00 KST에 보상 지급
// - 보상은 24시간 후 자동 소멸 (다음 00:00 전까지)
// - 랭킹 데이터는 리셋하지 않음 (누적 데이터)
//
// [보상 등급 (9티어)]
// - Tier 1: 1위
// - Tier 2: 2위
// - Tier 3: 3위
// - Tier 4: 상위 10위
// - Tier 5: 상위 10%
// - Tier 6: 상위 30%
// - Tier 7: 상위 50%
// - Tier 8: 상위 80%
// - Tier 9: 상위 100% (참여 보상)

import { https } from "firebase-functions/v2";
import { getFirestore, WriteBatch, Timestamp } from "firebase-admin/firestore";
import * as uuid from "uuid";
import { CServerInternalBase } from "../Base/ServerInternalBase";
import { logCategory, logDebug } from "../../Utility/UtilityBasic";
import { CFloorRanks, CAwakenGradeRanks, RankDB_Floor, RankDB_AwakenGrade } from "../ServerOperation/Rank";
import { CServerInfo } from "../ServerOperation/ServerInfo";
import { ServerSelector } from "../Factory/ServerSelector";
import { EMailboxFrom, ERankingRewardType, ERankingReward, EAwaken, ECurrency, ETier, EGrade } from "../../Data/Generated/CommonEnum";
import { CoreData_MailboxItem } from "../../Data/Generated/CoreData";
import { GameDB_Server_RankingReward, GameDB_Server_RankingRewardMap } from "../../Data/Generated/GameDBData";
import { GameDBPath } from "../../Data/Constants/FirestorePaths";
import { plainToInstance } from "class-transformer";
import * as GameBalanceConfig from "../../Data/Shared/Config/GameBalanceConfig";

// -----------------------------------------------
// 유틸리티 함수
// -----------------------------------------------

// 순위 기반 보상 등급(Tier 1~9) 계산
export function getRankingRewardTier(rank: number, totalUsers: number): number {
	if (rank === 1) return 1;  // 1위
	if (rank === 2) return 2;  // 2위
	if (rank === 3) return 3;  // 3위
	if (rank <= 10) return 4;  // 상위 10위

	const percentile = (rank / totalUsers) * 100;
	if (percentile <= 10) return 5;   // 상위 10%
	if (percentile <= 30) return 6;   // 상위 30%
	if (percentile <= 50) return 7;   // 상위 50%
	if (percentile <= 80) return 8;   // 상위 80%
	return 9;                          // 상위 100% (참여 보상)
}

// ERankingReward enum 값 생성 (RankingType + Tier 조합)
function getRankingRewardEnumValue(rankingType: ERankingRewardType, tier: number): ERankingReward {
	// ERankingReward enum 값 규칙:
	// floor_daily: 1000 + (tier - 1) = 1000 ~ 1008 (9티어)
	// awaken_daily: 100000 + (tier - 1) = 100000 ~ 100008 (9티어)

	switch (rankingType) {
		case ERankingRewardType.floor_daily:
			return (1000 + (tier - 1)) as ERankingReward;
		case ERankingRewardType.floor_weekly:
			return (10000 + (tier - 1)) as ERankingReward;
		case ERankingRewardType.floor_monthly:
			return (20000 + (tier - 1)) as ERankingReward;
		case ERankingRewardType.awaken_daily:
			return (100000 + (tier - 1)) as ERankingReward;
		case ERankingRewardType.awaken_weekly:
			return (110000 + (tier - 1)) as ERankingReward;
		case ERankingRewardType.awaken_monthly:
			return (120000 + (tier - 1)) as ERankingReward;
		default:
			return ERankingReward.None;
	}
}

// 다음 KST 자정 시간을 UTC ISO 문자열로 반환 (보상 만료용)
function getNextMidnightKST(): string {
	const formatter = new Intl.DateTimeFormat("en-CA", {
		timeZone: "Asia/Seoul",
		year: "numeric",
		month: "2-digit",
		day: "2-digit",
		hour12: false,
	});

	const now = new Date();
	const parts = formatter.formatToParts(now);
	const p: Record<string, number> = {};
	parts.forEach(({ type, value }) => {
		if (type !== "literal") p[type] = parseInt(value);
	});

	// 다음 날 00:00 KST를 UTC로 변환
	// KST 00:00 = UTC 15:00 (전날)
	const nextMidnightKST = new Date(Date.UTC(p.year, p.month - 1, p.day + 1, 0, 0, 0));
	const utcTime = new Date(nextMidnightKST.getTime() - 9 * 60 * 60 * 1000);
	return utcTime.toISOString();
}

// 오늘 날짜 문자열 (YYYYMMDD, KST 기준)
function getTodayDateString(): string {
	const formatter = new Intl.DateTimeFormat("en-CA", {
		timeZone: "Asia/Seoul",
		year: "numeric",
		month: "2-digit",
		day: "2-digit",
	});

	const now = new Date();
	const parts = formatter.formatToParts(now);
	const p: Record<string, string> = {};
	parts.forEach(({ type, value }) => {
		if (type !== "literal") p[type] = value;
	});

	return `${p.year}${p.month}${p.day}`;
}

// -----------------------------------------------
// 랭킹 보상 메일 생성 인터페이스
// -----------------------------------------------
interface RankingRewardMailData {
	uuid: string;
	rank: number;
	tier: number;
	rankingType: ERankingRewardType;
	floorNumber?: number;       // 스테이지 랭킹용
	awakenGrade?: EAwaken;      // 헌터 랭킹용
}

// -----------------------------------------------
// 일일 랭킹 보상 클래스
// -----------------------------------------------
export class CRankingReward_Daily extends CServerInternalBase {
	private firestore = getFirestore();
	private batchSize = 500;

	constructor() {
		super("RankingReward_Daily");
		// 점검 아닐 때만 실행 (유저 데이터 관련)
		this.setMaintenancePolicy(false, false);
	}

	protected async execute(): Promise<void> {
		const today = getTodayDateString();
		logDebug(`[RANKING_REWARD_DAILY] 일일 랭킹 보상 처리 시작 - ${today}`);

		// 서버 목록 조회
		const serverInfo = new CServerInfo();
		const serverCount = await serverInfo.getServerCount();

		// GameDB에서 보상 테이블 로드
		const rewardMap = await this.loadRankingRewardMap();
		if (!rewardMap || rewardMap.MapData.size === 0) {
			logCategory("RANKING_REWARD", "보상 테이블이 비어있음, 보상 처리 중단");
			return;
		}

		// 서버별 보상 처리
		for (let serverNum = 1; serverNum <= serverCount; serverNum++) {
			const serverID = `Server_${serverNum}`;

			// 1. 층수 랭킹 일일 보상
			await this.processFloorDailyRewards(serverID, rewardMap, today);

			// 2. 각성 등급 랭킹 일일 보상
			await this.processAwakenDailyRewards(serverID, rewardMap, today);

			logCategory("RANKING_REWARD", `Server ${serverID}: 일일 랭킹 보상 처리 완료`);
		}

		logCategory("RANKING_REWARD", "일일 랭킹 보상 처리 전체 완료");
	}

	// GameDB에서 보상 테이블 로드
	private async loadRankingRewardMap(): Promise<GameDB_Server_RankingRewardMap | null> {
		const docPath = GameDBPath.RankingReward;
		const doc = await this.firestore.doc(docPath).get();

		if (!doc.exists) {
			logCategory("RANKING_REWARD", `보상 테이블 문서를 찾을 수 없음: ${docPath}`);
			return null;
		}

		const data = doc.data();
		if (!data || !data.RankingReward) {
			logCategory("RANKING_REWARD", `보상 테이블 데이터가 없음`);
			return null;
		}

		return plainToInstance(GameDB_Server_RankingRewardMap, data.RankingReward);
	}

	// 스테이지 랭킹 일일 보상 처리
	private async processFloorDailyRewards(
		serverID: string,
		rewardMap: GameDB_Server_RankingRewardMap,
		today: string
	): Promise<void> {
		logDebug(`[RANKING_REWARD_DAILY] ${serverID} 스테이지 랭킹 보상 처리 시작`);

		const floorRanks = new CFloorRanks(serverID);

		// 모든 층의 유저를 가져옴
		// 각 층별로 별도 처리 (층수가 곧 랭킹 그룹)
		const FLOOR_NUMBERS = Array.from({ length: GameBalanceConfig.num_floor }, (_, i) => i + 1);

		for (const floorNumber of FLOOR_NUMBERS) {
			const { users, totalCount } = await floorRanks.getUsersWithRankByFloor(floorNumber);

			if (totalCount === 0) continue;

			const rewardMails: RankingRewardMailData[] = [];

			for (const user of users) {
				const tier = getRankingRewardTier(user.rank, totalCount);

				rewardMails.push({
					uuid: user.uuid,
					rank: user.rank,
					tier,
					rankingType: ERankingRewardType.floor_daily,
					floorNumber,
				});
			}

			// 배치로 메일 발송
			await this.sendRewardMails(serverID, rewardMails, rewardMap, today, "floor");

			logDebug(`[RANKING_REWARD_DAILY] ${serverID} Floor_${floorNumber}: ${rewardMails.length}명 보상 발송`);
		}
	}

	// 헌터 등급 랭킹 일일 보상 처리
	private async processAwakenDailyRewards(
		serverID: string,
		rewardMap: GameDB_Server_RankingRewardMap,
		today: string
	): Promise<void> {
		logDebug(`[RANKING_REWARD_DAILY] ${serverID} 헌터 랭킹 보상 처리 시작`);

		const awakenRanks = new CAwakenGradeRanks(serverID);

		// None과 unwakened를 제외한 모든 각성 등급
		const AWAKEN_GRADES = Object.values(EAwaken).filter(
			(grade) => grade !== EAwaken.None && grade !== EAwaken.awaken_grade_unwakened && typeof grade === "number"
		) as EAwaken[];

		for (const awakenGrade of AWAKEN_GRADES) {
			const { users, totalCount } = await awakenRanks.getUsersWithRankByGrade(awakenGrade);

			if (totalCount === 0) continue;

			const rewardMails: RankingRewardMailData[] = [];

			for (const user of users) {
				const tier = getRankingRewardTier(user.rank, totalCount);

				rewardMails.push({
					uuid: user.uuid,
					rank: user.rank,
					tier,
					rankingType: ERankingRewardType.awaken_daily,
					awakenGrade,
				});
			}

			// 배치로 메일 발송
			await this.sendRewardMails(serverID, rewardMails, rewardMap, today, "awaken");

			logDebug(`[RANKING_REWARD_DAILY] ${serverID} Awaken_${awakenGrade}: ${rewardMails.length}명 보상 발송`);
		}
	}

	// 보상 메일 배치 발송
	private async sendRewardMails(
		serverID: string,
		mails: RankingRewardMailData[],
		rewardMap: GameDB_Server_RankingRewardMap,
		today: string,
		type: "floor" | "awaken"
	): Promise<void> {
		const serverManager = ServerSelector.getInstance();
		const gameDataPath = serverManager.getGameDataPathSync(serverID);

		const batches: WriteBatch[] = [];
		let currentBatch = this.firestore.batch();
		let batchCount = 0;

		for (const mail of mails) {
			// 보상 데이터 조회
			const rewardEnumValue = getRankingRewardEnumValue(mail.rankingType, mail.tier);
			const rewardData = rewardMap.MapData.get(rewardEnumValue);

			if (!rewardData) {
				logCategory("RANKING_REWARD", `보상 데이터를 찾을 수 없음: ${mail.rankingType}, tier=${mail.tier}`);
				continue;
			}

			// 메일박스 문서 경로
			const mailboxDocRef = this.firestore
				.collection(gameDataPath)
				.doc("Users")
				.collection("UUID")
				.doc(mail.uuid)
				.collection("Mailboxes")
				.doc("Mailboxes");

			// 메일 아이템 생성
			const mailItemKey = `ranking_${type}_${today}`;
			const mailItem = this.createMailboxItem(mail, rewardData, type);

			// 배치에 추가 (Map 필드 업데이트)
			currentBatch.set(
				mailboxDocRef,
				{
					Items: {
						[mailItemKey]: this.mailboxItemToPlainObject(mailItem),
					},
				},
				{ merge: true }
			);

			batchCount++;

			// 배치 크기 제한
			if (batchCount >= this.batchSize) {
				batches.push(currentBatch);
				currentBatch = this.firestore.batch();
				batchCount = 0;
			}
		}

		// 남은 배치 추가
		if (batchCount > 0) {
			batches.push(currentBatch);
		}

		// 배치 커밋
		for (const batch of batches) {
			await batch.commit();
		}
	}

	// 메일박스 아이템 생성
	private createMailboxItem(
		mail: RankingRewardMailData,
		rewardData: GameDB_Server_RankingReward,
		type: "floor" | "awaken"
	): CoreData_MailboxItem {
		const item = new CoreData_MailboxItem();
		item.UID = uuid.v4();
		item.From = type === "floor" ? EMailboxFrom.RankingReward_Floor : EMailboxFrom.RankingReward_Awaken;
		item.PurchaseLimitDate = getNextMidnightKST(); // 24시간 후 만료
		item.TimeLeftToReceiveDate = getNextMidnightKST();

		// 랭킹 보상 관련 정보 설정
		item.RankingRewardRank = mail.rank;
		item.RankingRewardTier = mail.tier;
		if (type === "floor") {
			item.RankingRewardFloor = mail.floorNumber || 0;
			item.RankingRewardAwakenGrade = 0;
		} else {
			item.RankingRewardFloor = 0;
			item.RankingRewardAwakenGrade = mail.awakenGrade || 0;
		}

		return item;
	}

	// CoreData_MailboxItem을 plain object로 변환
	private mailboxItemToPlainObject(item: CoreData_MailboxItem): Record<string, any> {
		return {
			UID: item.UID,
			PurchaseLimitDate: item.PurchaseLimitDate,
			TimeLeftToReceiveDate: item.TimeLeftToReceiveDate,
			From: item.From,
			ProductID: item.ProductID,
			ProductPassID: item.ProductPassID,
			ProductSubscriptionID: item.ProductSubscriptionID,
			ProductPackageID: item.ProductPackageID,
			ServerMailboxID: item.ServerMailboxID,
			RankingRewardRank: item.RankingRewardRank,
			RankingRewardTier: item.RankingRewardTier,
			RankingRewardFloor: item.RankingRewardFloor,
			RankingRewardAwakenGrade: item.RankingRewardAwakenGrade,
		};
	}
}

// -----------------------------------------------
// 테스트용 HTTP 함수 export
// -----------------------------------------------
export const Test_RankingReward_Daily = https.onRequest({ timeoutSeconds: 360 }, async (_req, res) => {
	await new CRankingReward_Daily().executeFunction();
	res.send("Test_RankingReward_Daily: 일일 랭킹 보상 처리 완료");
});
