import { https } from "firebase-functions/v2";
import { onSchedule } from "firebase-functions/v2/scheduler";
import { CServerInternalBase } from "../Base/ServerInternalBase";
import { CCertificatePinningAutoRefresh } from "../Security/AppIntegrity";

// -----------------------------------------------
// 인증서 핀닝 해시 자동 갱신 스케줄 클래스
// -----------------------------------------------

// 매일 새벽 6시(KST)에 Firebase/Google 서버의 인증서 해시를 자동 갱신
// SecurityConfig.certificatePinningHashes에 저장하여 클라이언트가 사용
export class CCertificatePinningRefresh extends CServerInternalBase {
	constructor() {
		super("CertificatePinning_AutoRefresh");
		// 점검 중에도 실행 허용 (보안 설정 갱신은 시스템 유지보수 작업)
		this.setMaintenancePolicy(true, true);
	}

	protected async execute(): Promise<void> {
		const autoRefresh = new CCertificatePinningAutoRefresh();
		await autoRefresh.execute();
	}
}

// 스케줄 함수에서 호출하는 래퍼 함수
export async function certificatePinning_AutoRefresh() {
	await new CCertificatePinningRefresh().executeFunction();
}

// 테스트용 HTTP 함수
export const Test_CertificatePinning_AutoRefresh = https.onRequest({ timeoutSeconds: 60 }, async (_req, res) => {
	await new CCertificatePinningRefresh().executeFunction();
	res.send("Test_CertificatePinning_AutoRefresh: Certificate hashes refreshed");
});

// -----------------------------------------------
// 스케줄 함수 (Firebase Functions Export)
// -----------------------------------------------

// 인증서 핀닝 해시 자동 갱신 (매일 04:10 KST = UTC 19:10 실행 - 정기점검 중)
export const Schedule_Security_CertificatePinningAutoRefresh = onSchedule(
	{
		schedule: "10 19 * * *", // UTC 19:10 = KST 04:10
		// STANDARD preset: concurrency 40, memory 512MiB, timeout 30s
		concurrency: 40,
		memory: "512MiB",
		timeoutSeconds: 30,
	},
	certificatePinning_AutoRefresh
);
