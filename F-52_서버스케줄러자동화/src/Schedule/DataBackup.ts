import { google } from "googleapis";
import { CServerInternalBase } from "../Base/ServerInternalBase";
import { logCategory, logDebug, logError } from "../../Utility/UtilityBasic";
import { FirebaseInfo } from "../../Data/Config/ServerConfig";

// -----------------------------------------------
// Firestore Export 백업 설정
// -----------------------------------------------

// 백업 버킷 이름 (GCS)
// 형식: [PROJECT_ID]-backups
export const getBackupBucketName = (): string => {
	return `${FirebaseInfo.PROJECTID}-backups`;
};

// 백업 경로 생성
// 형식: firestore/daily/YYYY/MM/YYYY-MM-DD
export const getBackupOutputPath = (date: Date): string => {
	const year = date.getFullYear();
	const month = String(date.getMonth() + 1).padStart(2, "0");
	const day = String(date.getDate()).padStart(2, "0");
	const dateString = `${year}-${month}-${day}`;

	return `gs://${getBackupBucketName()}/firestore/daily/${year}/${month}/${dateString}`;
};

// -----------------------------------------------
// Firestore Daily Export 백업 클래스
// -----------------------------------------------

/**
 * 매일 Firestore 전체 백업을 GCS로 Export
 * - 스케줄: 매일 새벽 4:05 KST (UTC 19:05)
 * - 저장 위치: gs://[PROJECT_ID]-backups/firestore/daily/YYYY/MM/YYYY-MM-DD/
 * - GCS Lifecycle으로 자동 스토리지 클래스 전환 및 삭제
 */
export class CDataBackup_DailyExport extends CServerInternalBase {
	constructor() {
		super("DataBackup_DailyExport");
		// 점검 중에도 실행 허용 (백업은 시스템 유지보수 작업)
		this.setMaintenancePolicy(true, true);
	}

	protected async execute(): Promise<void> {
		const startTime = Date.now();
		const backupDate = new Date();
		const outputUri = getBackupOutputPath(backupDate);

		logDebug(`[BACKUP_START] Firestore Export 시작 - 대상 경로: ${outputUri}`);

		// Google Auth 클라이언트 생성 (기본 서비스 계정 사용)
		const auth = new google.auth.GoogleAuth({
			scopes: [
				"https://www.googleapis.com/auth/datastore",
				"https://www.googleapis.com/auth/cloud-platform",
			],
		});

		const firestore = google.firestore({
			version: "v1",
			auth: auth,
		});

		// Firestore Export API 호출
		// https://cloud.google.com/firestore/docs/manage-data/export-import
		const projectId = FirebaseInfo.PROJECTID;
		const databaseId = "(default)";

		const response = await firestore.projects.databases.exportDocuments({
			name: `projects/${projectId}/databases/${databaseId}`,
			requestBody: {
				outputUriPrefix: outputUri,
				// collectionIds를 지정하지 않으면 전체 컬렉션 백업
			},
		});

		const operationName = response.data.name;
		const executionTime = Date.now() - startTime;

		logCategory(
			"BACKUP_EXPORT",
			`Firestore Export 요청 완료 - Operation: ${operationName}, 소요시간: ${executionTime}ms`
		);

		// Export는 비동기 작업이므로 완료를 기다리지 않음
		// Cloud Console 또는 gcloud 명령으로 작업 상태 확인 가능:
		// gcloud firestore operations describe [OPERATION_NAME]
		logDebug(`[BACKUP_INFO] Export 작업이 백그라운드에서 실행 중입니다.`);
		logDebug(`[BACKUP_INFO] 작업 상태 확인: gcloud firestore operations describe ${operationName}`);
	}
}

// -----------------------------------------------
// 호출 함수 (호환성을 위해 유지)
// -----------------------------------------------

export async function dataBackup_DailyExport() {
	await new CDataBackup_DailyExport().executeFunction();
}

// -----------------------------------------------
// 테스트용 HTTP 함수 export
// -----------------------------------------------

import { https } from "firebase-functions/v2";

export const Test_DataBackup_DailyExport = https.onRequest({ timeoutSeconds: 360 }, async (_req, res) => {
	await new CDataBackup_DailyExport().executeFunction();
	res.send("Test_DataBackup_DailyExport: Firestore Export 요청 완료");
});
