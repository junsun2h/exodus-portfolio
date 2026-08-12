// 서버 전용 PRNG 구현 (HashPRNG)
// 동기화 불필요

import * as crypto from "crypto";
import { getAccountHashSecret } from "../../Utility/SecretManager";
import { EAPIErrorCode } from "../Generated/CommonEnum";
import { Exclude } from "class-transformer";
import path from "path";
import * as fs from "fs";
import * as utilBasic from "../../Utility/UtilityBasic";
import { IPRNG } from "../Shared/Types/PRNGTypes";

// Main 스테이지가 아닐 때 일반 Math.random() 사용하게 한다.
export function resolvePRNG(prng: HashPRNG): IPRNG {
	if (prng) return prng;

	return {
		nextRandom: Math.random,
		logRandAndEnum: () => void 0,
		logRandAndTwoEnums: () => void 0,
		writeRandomLogsToFile: () => void 0,
	};
}

export class HashPRNG implements IPRNG {
	@Exclude() private hash: string; // 해시 문자열
	@Exclude() private prng: number | null; // 32비트 LCG state

	@Exclude() private isLogging: boolean; // 로깅 플래그
	@Exclude() private debugLogs: string[]; // "0.43244,2" 등

	constructor(hash: string, isLogging: boolean = false) {
		this.hash = hash;
		this.prng = null;

		this.isLogging = isLogging;
		this.debugLogs = [];
	}

	// 해시 검증
	public validateHash(clientHash: string) {
		if (!clientHash || clientHash !== this.hash) {
			utilBasic.throwLogicError("validateHash(): Invalid or mismatched Hash", EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	// Hash를 생성/갱신
	public static generateHash() {
		// 1) 랜덤 솔트 생성 (16바이트 → 32글자 hex)
		const randomSalt: string = crypto.randomBytes(16).toString("hex");
		// 2) 입력값 조합 (현재 시간|랜덤 솔트)
		const currentTime = Date.now();
		const inputStr: string = [currentTime.toString(), randomSalt].join("|");
		// 3) HMAC-SHA256 생성
		const hmac = crypto.createHmac("sha256", getAccountHashSecret());
		hmac.update(inputStr, "utf8");
		// 4) 최종 해시를 hex 문자열로 반환
		return hmac.digest("hex");
	}

	// 1. 해시 검증
	// 2. PRNG 생성
	// 3. 해시 갱신
	public createPRNG(clientHash: string): string {
		// 1) 클라이언트 해시가 현재 AccountHash와 일치하는지 확인
		this.validateHash(clientHash);

		// 디버그 로그 배열 초기화 (새로 뽑기 시작)
		this.debugLogs = [];

		// prng 초기화
		// 예: "ABCDEF1234567890..." 중 앞 8글자 = "ABCDEF12" (약 2^32 범위)
		const sliceHex = this.hash.substring(0, 8);
		let seed = parseInt(sliceHex, 16); // 0 ~ 4294967295 or 음수(2의 보수)
		if (seed > 0x7fffffff) {
			// C#과 같이 맞추는 부분
			seed = seed - 0x100000000; // 4294967296
		}
		this.prng = seed;

		// 3) Hash 갱신
		return HashPRNG.generateHash();
	}

	// PRNG: Pseudo-Random Number Generator(의사난수 생성기)
	// PRNG 로 난수 뽑기: 0.0 <= x < 1.0(0.99999...)
	public nextRandom(): number {
		if (this.prng == null) {
			utilBasic.throwLogicError("nextRandom(): LCG state is null.", EAPIErrorCode.SERVER_ERROR);
		}

		// LCG 상수
		const MODULUS = 0x7fffffff; // 2147483647
		const MULTIPLIER = 1103515245;
		const INCREMENT = 12345;

		// 1) 32비트 LCG 계산
		const calcPrng = this.prng | 0;
		// 곱셈, 덧셈도 32비트 범위
		let calc = Math.imul(calcPrng, MULTIPLIER);
		calc = (calc + INCREMENT) | 0; // 다시 32비트
		// 모듈로 2,147,483,647 => 음수면 +MODULUS
		calc = calc % MODULUS; // 자바스크립트 음수 가능
		if (calc < 0) {
			calc += MODULUS; // 0..(MODULUS-1)
		}
		this.prng = calc;

		// 2) 정수 연산으로 fractionInt = floor((calc * 1e6) / 2147483647)
		//    BigInt로 정확 계산
		const calcBig = BigInt(calc);
		const fractionInt = (calcBig * BigInt(1000000)) / BigInt(2147483647);

		// => 0.0 ~ 0.999999 (소수점 6자리)
		const finalVal = Number(fractionInt) / 1e6;

		return finalVal;
	}

	// 난수 + 단일 enum 출력을 기록
	public logRandAndEnum(randVal: number, enumValue: number | string) {
		if (this.isLogging) {
			// 문자열 형태로 "0.43244,2" 라는 식으로 저장
			const line = `${randVal.toFixed(6)},${enumValue}`;
			this.debugLogs.push(line);
		}
	}

	// 난수 + 2개 enum (예: tier, grade)
	public logRandAndTwoEnums(randVal: number, enumA: number | string, enumB: number | string) {
		if (this.isLogging) {
			// "0.43244,2,1"
			const line = `${randVal.toFixed(6)},${enumA},${enumB}`;
			this.debugLogs.push(line);
		}
	}

	// RandomValues를 기록한 Json 파일을 생성
	public async writeRandomLogsToFile(filePrefix: string) {
		if (!this.isLogging) {
			return;
		}

		try {
			// 결과 : <project-root>\Temp\NextRandom
			const folder = path.resolve(process.cwd(), "..", "..", "Temp", "NextRandom");

			if (!fs.existsSync(folder)) {
				await fs.promises.mkdir(folder, { recursive: true });
			}

			// 파일명 예: "3f5e8a4d_server_gacha_equipment.json"
			const shortHash = this.hash.slice(0, 8); // 앞 8글자
			const fileName = `${shortHash}_server_${filePrefix}.json`;
			const fullPath = path.join(folder, fileName);

			const logsData = {
				randomValues: this.debugLogs, // string[]
			};

			await fs.promises.writeFile(fullPath, JSON.stringify(logsData, null, 2), "utf8");
		} catch (err) {
			utilBasic.throwLogicError(`writeRandomLogsToFile(): Failed to write random logs:`, EAPIErrorCode.SERVER_ERROR);
		}
	}
}
