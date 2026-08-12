// 만료 데이터 정리 시스템
// LogEvent, TransactionID 등 만료된 데이터 삭제

import * as admin from "firebase-admin";
import { https } from "firebase-functions/v2";
import { CServerInternalBase } from "../Base/ServerInternalBase";
import { logCategory, logDebug } from "../../Utility/UtilityBasic";
import * as ScheduleConfig from "../../Data/Config/ScheduleConfig";
import { CServerInfo } from "../ServerOperation/ServerInfo";

// -----------------------------------------------
// 만료 데이터 정리 클래스
// -----------------------------------------------

/**
 * 만료된 데이터 정리 (LogEvent, TransactionID)
 * - 스케줄: 매일 04:30 KST
 * - 대상: expireAt 필드가 현재 시간보다 작은 문서
 * - 방식: collectionGroup 쿼리로 모든 서버의 데이터 조회 후 배치 삭제
 */
export class CDataCleanup_ExpiredData extends CServerInternalBase {
	private batchSize: number;

	constructor() {
		super("DataCleanup_ExpiredData");
		// 점검 중에만 실행 (데이터 일관성 보장)
		this.setMaintenancePolicy(true, false);
		this.batchSize = ScheduleConfig.cleanup_BatchSize;
	}

	protected async execute(): Promise<void> {
		const startTime = Date.now();
		const currentTime = new Date();

		logDebug(`[DATA_CLEANUP_START] 만료 데이터 정리 시작 - 기준 시간: ${currentTime.toISOString()}`);

		// 1. LogEvent 삭제 (모든 서버)
		const logDeletedCount = await this.cleanupExpiredLogEvents(currentTime);

		// 2. TransactionID 삭제 (모든 서버)
		const txDeletedCount = await this.cleanupExpiredTransactionIDs(currentTime);

		const executionTime = Date.now() - startTime;

		logCategory(
			"DATA_CLEANUP",
			`만료 데이터 정리 완료 - LogEvents: ${logDeletedCount}건, TransactionIDs: ${txDeletedCount}건, 소요시간: ${executionTime}ms`
		);
	}

	// LogEvent 만료 데이터 삭제
	private async cleanupExpiredLogEvents(currentTime: Date): Promise<number> {
		const db = admin.firestore();
		let totalDeleted = 0;

		// 서버 목록 조회
		const serverInfo = new CServerInfo();
		const serverCount = await serverInfo.getServerCount();

		for (let serverNum = 1; serverNum <= serverCount; serverNum++) {
			const serverID = `Server_${serverNum}`;
			const collectionPath = `${serverID}/LogEvent/Events`;

			const deleted = await this.deleteExpiredDocuments(db, collectionPath, currentTime, "LogEvent");
			totalDeleted += deleted;
		}

		logDebug(`[DATA_CLEANUP_LOGEVENT] LogEvent 삭제 완료: ${totalDeleted}건`);
		return totalDeleted;
	}

	// TransactionID 만료 데이터 삭제
	private async cleanupExpiredTransactionIDs(currentTime: Date): Promise<number> {
		const db = admin.firestore();
		let totalDeleted = 0;

		// 서버 목록 조회
		const serverInfo = new CServerInfo();
		const serverCount = await serverInfo.getServerCount();

		for (let serverNum = 1; serverNum <= serverCount; serverNum++) {
			const serverID = `Server_${serverNum}`;
			const collectionPath = `${serverID}/TransactionID/IDs`;

			const deleted = await this.deleteExpiredDocuments(db, collectionPath, currentTime, "TransactionID");
			totalDeleted += deleted;
		}

		logDebug(`[DATA_CLEANUP_TRANSACTIONID] TransactionID 삭제 완료: ${totalDeleted}건`);
		return totalDeleted;
	}

	// 만료된 문서 배치 삭제
	private async deleteExpiredDocuments(
		db: admin.firestore.Firestore,
		collectionPath: string,
		currentTime: Date,
		dataType: string
	): Promise<number> {
		let deletedCount = 0;

		// expireAt 필드가 현재 시간보다 작은 문서 조회
		const query = db.collection(collectionPath).where("expireAt", "<", currentTime).limit(this.batchSize);

		let snapshot = await query.get();

		while (!snapshot.empty) {
			const batch = db.batch();
			snapshot.docs.forEach((doc) => batch.delete(doc.ref));
			await batch.commit();

			deletedCount += snapshot.size;
			logDebug(`[DATA_CLEANUP_BATCH] ${dataType} 배치 삭제: ${snapshot.size}건 (누적: ${deletedCount}건)`);

			// 다음 배치 조회
			snapshot = await query.get();
		}

		return deletedCount;
	}
}

// -----------------------------------------------
// 테스트용 HTTP 함수 export
// -----------------------------------------------

export const Test_DataCleanup_ExpiredData = https.onRequest({ timeoutSeconds: 360 }, async (_req, res) => {
	await new CDataCleanup_ExpiredData().executeFunction();
	res.send("Test_DataCleanup_ExpiredData: 만료 데이터 정리 완료");
});
