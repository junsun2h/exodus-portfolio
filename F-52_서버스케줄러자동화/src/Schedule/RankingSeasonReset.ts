// 랭킹 시즌 보상 시스템
// 주간/월간 랭킹 보상 지급 (추후 구현)
//
// [중요] 랭킹 리셋 정책:
// - 층수 랭킹, 각성 등급 랭킹은 누적 데이터이므로 리셋하지 않음
//   (예: 5층 1위를 리셋해도 동일 유저가 다시 1위가 되므로 무의미)
// - 성좌 계약 랭킹도 실시간 변동 데이터이므로 리셋 불필요
// - 보상 지급만 특정 시점 기준으로 스냅샷 찍어서 처리
//
// [보상 지급 시점 참고] (한국 모바일 게임 기준)
// - 대부분 월요일 새벽 4~5시 또는 화요일에 주간 보상 지급
// - 점검일(수요일)과 보상 지급일은 분리하는 것이 일반적

import { https } from "firebase-functions/v2";
import { CServerInternalBase } from "../Base/ServerInternalBase";
import { logCategory, logDebug } from "../../Utility/UtilityBasic";
// 리셋 기능은 비활성화되었으나, 추후 필요 시 사용할 수 있도록 import 유지
import { CFloorRanks, CAwakenGradeRanks } from "../ServerOperation/Rank";
import { CServerInfo } from "../ServerOperation/ServerInfo";

// -----------------------------------------------
// 유틸리티 함수
// -----------------------------------------------

// 현재 요일이 수요일인지 확인 (KST 기준)
export function isWednesday(date: Date = new Date()): boolean {
	const kstOffset = 9 * 60;
	const kstDate = new Date(date.getTime() + kstOffset * 60 * 1000);
	return kstDate.getUTCDay() === 3; // 0=일, 1=월, ..., 3=수
}

// 현재 달의 첫 번째 수요일인지 확인
export function isFirstWednesdayOfMonth(date: Date = new Date()): boolean {
	if (!isWednesday(date)) return false;
	const kstOffset = 9 * 60;
	const kstDate = new Date(date.getTime() + kstOffset * 60 * 1000);
	return kstDate.getUTCDate() <= 7; // 1~7일 사이면 첫 번째 수요일
}

// -----------------------------------------------
// 랭킹 시즌 리셋 클래스
// -----------------------------------------------

/**
 * 랭킹 시즌 보상 처리
 * - 스케줄: 매주 수요일 04:40 KST (주간 점검 시간 중)
 * - 주간 보상: 층수 랭킹 보상 (매주 수요일) - 추후 구현
 * - 월간 보상: 각성 등급 랭킹 보상 (매월 첫 번째 수요일) - 추후 구현
 *
 * [비활성화] 랭킹 리셋 기능
 * - 층수/각성/성좌 랭킹은 누적 데이터이므로 리셋하지 않음
 * - resetAllRanks() 메서드는 유지하되 호출하지 않음 (추후 필요 시 사용 가능)
 */
export class CRankingSeasonReset extends CServerInternalBase {
	constructor() {
		super("RankingSeasonReset");
		// 점검 중에만 실행
		this.setMaintenancePolicy(true, false);
	}

	protected async execute(): Promise<void> {
		const today = new Date();

		logDebug(`[RANKING_SEASON_REWARD] 랭킹 시즌 보상 처리 시작 - 오늘: ${today.toISOString()}`);

		// 서버 목록 조회
		const serverInfo = new CServerInfo();
		const serverCount = await serverInfo.getServerCount();

		// 1. 주간 보상 (매주 수요일)
		if (isWednesday(today)) {
			logDebug("[RANKING_SEASON_REWARD] 주간 랭킹 보상 처리 시작");
			await this.processWeeklyRewards(serverCount);
		}

		// 2. 월간 보상 (매월 첫 번째 수요일)
		if (isFirstWednesdayOfMonth(today)) {
			logDebug("[RANKING_SEASON_REWARD] 월간 랭킹 보상 처리 시작");
			await this.processMonthlyRewards(serverCount);
		}

		logCategory("RANKING_REWARD", "랭킹 시즌 보상 처리 완료");
	}

	// 주간 랭킹 보상 처리 (층수 랭킹)
	private async processWeeklyRewards(serverCount: number): Promise<void> {
		logDebug("[RANKING_SEASON_REWARD] 주간 층수 랭킹 보상 처리 중...");

		for (let serverNum = 1; serverNum <= serverCount; serverNum++) {
			const serverID = `Server_${serverNum}`;

			// 1. 현재 랭킹 스냅샷 저장 (보상 지급용 - 추후 구현)
			// const snapshot = await this.saveRankingSnapshot(serverID, "floor", "weekly");

			// 2. 랭킹 보상 메일 발송 (추후 구현)
			// await this.sendRankingRewards(serverID, snapshot, ERankingRewardType.WEEKLY_FLOOR);

			// [비활성화] 랭킹 리셋 - 누적 데이터이므로 리셋하지 않음
			// 추후 필요 시 아래 코드 활성화 가능
			// const floorRanks = new CFloorRanks(serverID);
			// await floorRanks.resetAllRanks();

			logCategory("RANKING_REWARD", `Server ${serverID}: 주간 층수 랭킹 보상 처리 완료 (보상 기능 추후 구현)`);
		}

		logCategory("RANKING_REWARD", "주간 층수 랭킹 보상 처리 전체 완료");
	}

	// 월간 랭킹 보상 처리 (각성 등급 랭킹)
	private async processMonthlyRewards(serverCount: number): Promise<void> {
		logDebug("[RANKING_SEASON_REWARD] 월간 각성 등급 랭킹 보상 처리 중...");

		for (let serverNum = 1; serverNum <= serverCount; serverNum++) {
			const serverID = `Server_${serverNum}`;

			// 1. 현재 랭킹 스냅샷 저장 (보상 지급용 - 추후 구현)
			// const snapshot = await this.saveRankingSnapshot(serverID, "awaken", "monthly");

			// 2. 랭킹 보상 메일 발송 (추후 구현)
			// await this.sendRankingRewards(serverID, snapshot, ERankingRewardType.MONTHLY_AWAKEN);

			// [비활성화] 랭킹 리셋 - 누적 데이터이므로 리셋하지 않음
			// 추후 필요 시 아래 코드 활성화 가능
			// const awakenRanks = new CAwakenGradeRanks(serverID);
			// await awakenRanks.resetAllRanks();

			logCategory("RANKING_REWARD", `Server ${serverID}: 월간 각성 등급 랭킹 보상 처리 완료 (보상 기능 추후 구현)`);
		}

		logCategory("RANKING_REWARD", "월간 각성 등급 랭킹 보상 처리 전체 완료");
	}

	// TODO: 추후 구현 - 랭킹 스냅샷 저장
	// private async saveRankingSnapshot(serverID: string, rankType: string, period: string): Promise<RankingSnapshot> {
	//     // GCS 또는 Firestore Archive에 현재 랭킹 스냅샷 저장
	//     // 보상 지급 시 참조용
	// }

	// TODO: 추후 구현 - 랭킹 보상 메일 발송
	// private async sendRankingRewards(serverID: string, snapshot: RankingSnapshot, rewardType: ERankingRewardType): Promise<void> {
	//     // Top N 유저에게 보상 메일 발송
	//     // ServerMailbox 사용
	// }
}

// -----------------------------------------------
// 테스트용 HTTP 함수 export
// -----------------------------------------------

export const Test_RankingSeasonReward = https.onRequest({ timeoutSeconds: 360 }, async (_req, res) => {
	await new CRankingSeasonReset().executeFunction();
	res.send("Test_RankingSeasonReward: 랭킹 시즌 보상 처리 완료 (보상 기능 추후 구현)");
});

// [비활성화] 리셋 테스트 함수들 - 누적 데이터이므로 리셋하지 않음
// 추후 필요 시 아래 코드 활성화 가능

// 주간 리셋만 강제 실행 (테스트용) - 비활성화됨
// export const Test_RankingSeasonReset_Weekly = https.onRequest({ timeoutSeconds: 360 }, async (_req, res) => {
// 	const serverInfo = new CServerInfo();
// 	const serverCount = await serverInfo.getServerCount();
//
// 	for (let serverNum = 1; serverNum <= serverCount; serverNum++) {
// 		const serverID = `Server_${serverNum}`;
// 		const floorRanks = new CFloorRanks(serverID);
// 		await floorRanks.resetAllRanks();
// 	}
//
// 	res.send("Test_RankingSeasonReset_Weekly: 주간 층수 랭킹 리셋 완료");
// });

// 월간 리셋만 강제 실행 (테스트용) - 비활성화됨
// export const Test_RankingSeasonReset_Monthly = https.onRequest({ timeoutSeconds: 360 }, async (_req, res) => {
// 	const serverInfo = new CServerInfo();
// 	const serverCount = await serverInfo.getServerCount();
//
// 	for (let serverNum = 1; serverNum <= serverCount; serverNum++) {
// 		const serverID = `Server_${serverNum}`;
// 		const awakenRanks = new CAwakenGradeRanks(serverID);
// 		await awakenRanks.resetAllRanks();
// 	}
//
// 	res.send("Test_RankingSeasonReset_Monthly: 월간 각성 등급 랭킹 리셋 완료");
// });
