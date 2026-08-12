import { https } from "firebase-functions/v2";
import { getFirestore } from "firebase-admin/firestore";
import { ServerMailbox_DeleteExpiredDocuments } from "../ServerOperation/ServerMailbox";
import { CAwakenGradeRanks, CConstellationContractRanks, CFloorRanks } from "../ServerOperation/Rank";
import { logCategory } from "../../Utility/UtilityBasic";
import { EAPIErrorCode } from "../../Data/Generated/CommonEnum";
import { ServerSelector } from "../Factory/ServerSelector";
import { CServerInternalBase } from "../Base/ServerInternalBase";
import { CServerInfo } from "../ServerOperation/ServerInfo";

/* 크론 작업 형식 및 시간대
https://cloud.google.com/scheduler/docs/configuring/cron-job-schedules?hl=ko
|------------------------------- Minute (0-59)
|     |------------------------- Hour (0-23)
|     |     |------------------- Day of the month (1-31)
|     |     |     |------------- Month (1-12; or JAN to DEC)
|     |     |     |     |------- Day of the week (0-6; or SUN to SAT; or 7 for Sunday)
|     |     |     |     |
|     |     |     |     |
*     *     *     *     *
*/

// -----------------------------------------------
// 기존 함수들 (호환성을 위해 유지, 새로운 클래스 사용)
// -----------------------------------------------
export async function serverMailbox_DeleteExpired() {
	await new CServerMailboxDeleteExpired().executeFunction();
}

export async function refreshAllRanks() {
	await new CRefreshAllRanks().executeFunction();
}

// 테스트용 HTTP 함수 (새로운 클래스 기반 구조 사용)
export const Test_ScheduledFunction_ServerMailbox_DeleteExpired = https.onRequest({ timeoutSeconds: 360 }, async (_req, res) => {
	await new CServerMailboxDeleteExpired().executeFunction();
	res.send("Test_ScheduledFunction_ServerMailbox_DeleteExpired: Documents deleted");
});

// -----------------------------------------------
// 서버 메일박스 만료 문서 삭제 클래스
// -----------------------------------------------
export class CServerMailboxDeleteExpired extends CServerInternalBase {
	constructor() {
		super("ServerMailbox_DeleteExpired");
		// 점검 중에도 실행 허용 (메일박스 정리는 중요한 시스템 유지보수)
		this.setMaintenancePolicy(true, false);
	}

	protected async execute(): Promise<void> {
		await ServerMailbox_DeleteExpiredDocuments(new Date());
	}
}

// -----------------------------------------------
// 모든 랭크 갱신 클래스
// -----------------------------------------------
export class CRefreshAllRanks extends CServerInternalBase {
	constructor() {
		super("RefreshAllRanks");
		// 점검 중에는 실행하지 않음 (사용자 데이터 관련)
		this.setMaintenancePolicy(false, false);
	}

	protected async execute(): Promise<void> {
		const serverSelector = ServerSelector.getInstance();

		// CServerInfo에서 serverCount를 가져와서 활성 서버 목록 생성 (0번은 사용 안함)
		const serverInfo = new CServerInfo();
		const serverCount = await serverInfo.getServerCount();
		const activeServers: string[] = [];

		// Server_1부터 serverCount까지 생성 (0번은 사용 안함)
		for (let i = 1; i <= serverCount; i++) {
			activeServers.push(`Server_${i}`);
		}

		// 모든 활성 서버에 대해 랭크 갱신 실행
		const refreshPromises = activeServers.map(async (serverID) => {
			await new CFloorRanks(serverID).refreshRanksDB();
			await new CAwakenGradeRanks(serverID).refreshRanksDB();
			await new CConstellationContractRanks(serverID).refreshRanksDB();
		});

		await Promise.all(refreshPromises);
	}
}

// -----------------------------------------------
// 채팅 시스템 메시지 만료 정리 클래스
// -----------------------------------------------
export class CChatSystemMessageCleanupExpired extends CServerInternalBase {
	constructor() {
		super("ChatSystemMessage_CleanupExpired");
		// 점검 중에도 실행 허용 (메시지 정리는 시스템 유지보수)
		this.setMaintenancePolicy(true, false);
	}

	protected async execute(): Promise<void> {
		const db = getFirestore();
		const currentTime = Date.now();

		// 모든 서버 정리
		const serversRef = db
			.collection("ServerEvent")
			.doc("SystemMessage")
			.collection("Servers");
		const serversSnapshot = await serversRef.get();

		let totalCleaned = 0;
		let serversCleaned = 0;

		for (const serverDoc of serversSnapshot.docs) {
			const cleaned = await this.cleanupServerExpiredMessages(db, serverDoc.id, currentTime);
			totalCleaned += cleaned;
			if (cleaned > 0) serversCleaned++;
		}

		logCategory(
			"SCHEDULE_CHAT_CLEANUP",
			`Cleaned ${totalCleaned} expired messages from ${serversCleaned} servers`
		);
	}

	private async cleanupServerExpiredMessages(
		db: FirebaseFirestore.Firestore,
		serverId: string,
		currentTime: number
	): Promise<number> {
		const messagesRef = db
			.collection("ServerEvent")
			.doc("SystemMessage")
			.collection("Servers")
			.doc(serverId)
			.collection("Messages");

		// 만료된 메시지 조회 (expireAt > 0 AND expireAt < currentTime)
		const expiredQuery = messagesRef
			.where("expireAt", ">", 0)
			.where("expireAt", "<", currentTime);

		const snapshot = await expiredQuery.get();

		if (snapshot.empty) return 0;

		let cleanedCount = 0;
		for (const doc of snapshot.docs) {
			await doc.ref.delete();
			cleanedCount++;
		}

		return cleanedCount;
	}
}

// -----------------------------------------------
// 공지사항 만료 정리 클래스
// -----------------------------------------------
export class CServerNoticeCleanupExpired extends CServerInternalBase {
	constructor() {
		super("ServerNotice_CleanupExpired");
		// 점검 중에도 실행 허용 (공지 정리는 시스템 유지보수)
		this.setMaintenancePolicy(true, false);
	}

	protected async execute(): Promise<void> {
		const db = getFirestore();
		const currentTime = Date.now();

		// 모든 서버 정리
		const serversRef = db
			.collection("ServerEvent")
			.doc("Notice")
			.collection("Servers");
		const serversSnapshot = await serversRef.get();

		let totalCleaned = 0;
		let serversCleaned = 0;

		for (const serverDoc of serversSnapshot.docs) {
			const cleaned = await this.cleanupServerExpiredNotices(db, serverDoc.id, currentTime);
			totalCleaned += cleaned;
			if (cleaned > 0) serversCleaned++;
		}

		logCategory(
			"SCHEDULE_NOTICE_CLEANUP",
			`Cleaned ${totalCleaned} expired notices from ${serversCleaned} servers`
		);
	}

	private async cleanupServerExpiredNotices(
		db: FirebaseFirestore.Firestore,
		serverId: string,
		currentTime: number
	): Promise<number> {
		const noticesRef = db
			.collection("ServerEvent")
			.doc("Notice")
			.collection("Servers")
			.doc(serverId)
			.collection("Message");

		// 만료된 공지 조회 (expiryDate > 0 AND expiryDate < currentTime)
		const expiredQuery = noticesRef
			.where("expiryDate", ">", 0)
			.where("expiryDate", "<", currentTime);

		const snapshot = await expiredQuery.get();

		if (snapshot.empty) return 0;

		let cleanedCount = 0;
		for (const doc of snapshot.docs) {
			await doc.ref.delete();
			cleanedCount++;
		}

		return cleanedCount;
	}
}

/* ─── 1분마다 로그 찍기(테스트) ─── */
export const scheduledTest1 = () => {
	//utilBasic.logDebug(new Date());
};
