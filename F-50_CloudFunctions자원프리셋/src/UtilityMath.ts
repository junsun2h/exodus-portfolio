// -----------------------------------------------
// 수학 함수
// -----------------------------------------------

import { cloneDeep } from "lodash";
import { throwLogicError } from "./UtilityError";
import { EAPIErrorCode } from "../Data/Generated/CommonEnum";

export const maxInt: number = 2147483647; // int의 최대값 (32-bit)
export const maxDouble: number = Number.MAX_VALUE; // double의 최대값

export function randomRange(min, max) {
	return Math.floor(Math.random() * (max - min + 1)) + min;
}

export function clamp(number, min, max) {
	return Math.max(min, Math.min(number, max));
}

// https://lodash.com/docs/4.17.15#cloneDeep
export function deepClone(obj) {
	return cloneDeep(obj);
}

export function verifyIntinity(value: number) {
	if (isFinite(value) == false) {
		throwLogicError("verifyIntinity(): verifyIntinity() value is infinity!", EAPIErrorCode.INVALID_PARAMETERS);
	}
}

export function IsInRange(value: number, minValue: number, maxValue: number) {
	if (value < minValue || value > maxValue) {
		return false;
	}

	return true;
}

export function limitUnsignedInt(value: number): number {
	if (value > maxInt) {
		return maxInt;
	} else if (value < 0) {
		return 0;
	}

	return value;
}

export function limitUnsignedDouble(value: number): number {
	if (value > maxDouble) {
		return maxDouble;
	} else if (value < 0) {
		return 0;
	}

	return value;
}

export function limitToMinBigInt(value: bigint): bigint {
	if (value < 0n) {
		return 0n;
	}

	return value;
}

// 이항 분포 샘플링 (n회 독립 베르누이 시행에서 성공 횟수 반환)
// 최적화: n회 PRNG 호출 대신 2회 PRNG 호출로 동일한 확률 분포 결과 생성
// 주의: PRNG 시퀀스가 변경되므로 클라이언트와 동기화 검증 필요
export function sampleBinomial(n: number, p: number, rand1: number, rand2: number): number {
	if (n <= 0 || p <= 0) return 0;
	if (p >= 1) return n;

	// 기대값과 표준편차
	const mean = n * p;
	const stdDev = Math.sqrt(n * p * (1 - p));

	// n이 충분히 크고 np, n(1-p)가 5 이상이면 정규 근사 사용
	if (n >= 20 && mean >= 5 && n * (1 - p) >= 5) {
		// Box-Muller 변환으로 정규 분포 샘플 생성
		const u1 = Math.max(rand1, 1e-10); // log(0) 방지
		const u2 = rand2;
		const z = Math.sqrt(-2 * Math.log(u1)) * Math.cos(2 * Math.PI * u2);

		// 정규 분포 값을 이항 분포로 변환
		const result = Math.floor(mean + z * stdDev);

		// 범위 제한 [0, n]
		return Math.max(0, Math.min(n, result));
	}

	// n이 작으면 직접 시뮬레이션 (기존 방식보다 효율적인 역변환 방법)
	// Inverse Transform Sampling with CDF
	let successes = 0;
	let cumulativeProb = 0;
	const q = 1 - p;

	// 이항 분포 PMF: C(n,k) * p^k * (1-p)^(n-k)
	// 점화식: P(k+1) = P(k) * (n-k)/(k+1) * p/q
	let prob = Math.pow(q, n); // P(X=0)

	for (let k = 0; k <= n; k++) {
		cumulativeProb += prob;
		if (rand1 <= cumulativeProb) {
			successes = k;
			break;
		}
		// 다음 확률 계산 (점화식)
		prob *= (n - k) / (k + 1) * (p / q);
	}

	return successes;
}
