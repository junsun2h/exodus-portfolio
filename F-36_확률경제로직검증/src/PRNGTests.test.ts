// Phase 3: HashPRNG 알고리즘 단위 테스트, validateHash 엣지케이스, 분포 품질 검증

import "reflect-metadata";
import * as chai from "chai";
import { HashPRNG } from "../../src/Data/Types/PRNGServer";

const assert = chai.assert;

describe("HealthCheck: HashPRNG 검증", () => {

	// 3-0. PRNG 알고리즘 단위 테스트
	describe("PRNG 알고리즘 단위 테스트", () => {

		it("동일 시드 → 동일 시퀀스 (결정론 검증)", () => {
			const hash = HashPRNG.generateHash();

			const prng1 = new HashPRNG(hash);
			const newHash1 = prng1.createPRNG(hash);
			const seq1 = Array.from({ length: 10 }, () => prng1.nextRandom());

			const prng2 = new HashPRNG(hash);
			prng2.createPRNG(hash);
			const seq2 = Array.from({ length: 10 }, () => prng2.nextRandom());

			assert.deepEqual(seq1, seq2, "동일 시드에서 동일 시퀀스가 나와야 함");
		}).timeout(0);

		it("다른 시드 → 다른 시퀀스", () => {
			const hash1 = HashPRNG.generateHash();
			const hash2 = HashPRNG.generateHash();

			const prng1 = new HashPRNG(hash1);
			prng1.createPRNG(hash1);
			const seq1 = Array.from({ length: 10 }, () => prng1.nextRandom());

			const prng2 = new HashPRNG(hash2);
			prng2.createPRNG(hash2);
			const seq2 = Array.from({ length: 10 }, () => prng2.nextRandom());

			// 10개 중 적어도 하나는 달라야 한다 (확률적으로 동일할 확률은 극히 낮음)
			const allSame = seq1.every((val, i) => val === seq2[i]);
			assert.isFalse(allSame, "다른 시드에서 다른 시퀀스가 나와야 함");
		}).timeout(0);

		it("시드 경계값: 0x7fffffff 근처 해시에서도 정상 동작", () => {
			// 앞 8글자가 "7fffffff"인 해시 시뮬레이션
			const hash = "7fffffff" + "a".repeat(56); // 64자 hex
			const prng = new HashPRNG(hash);

			// validateHash를 우회하기 위해 동일 해시로 createPRNG
			prng.createPRNG(hash);
			const val = prng.nextRandom();

			assert.isAtLeast(val, 0.0, "결과가 0.0 이상이어야 함");
			assert.isBelow(val, 1.0, "결과가 1.0 미만이어야 함");
		}).timeout(0);

		it("시퀀스 1000회 생성 → 모든 값이 [0, 1) 범위", () => {
			const hash = HashPRNG.generateHash();
			const prng = new HashPRNG(hash);
			prng.createPRNG(hash);

			for (let i = 0; i < 1000; i++) {
				const val = prng.nextRandom();
				assert.isAtLeast(val, 0.0, `${i}번째 값이 0.0 이상이어야 함`);
				assert.isBelow(val, 1.0, `${i}번째 값이 1.0 미만이어야 함`);
			}
		}).timeout(0);
	});

	// 3-1. validateHash 엣지케이스
	describe("validateHash 엣지케이스", () => {

		it("실패: 빈 문자열 → 에러", () => {
			const hash = HashPRNG.generateHash();
			const prng = new HashPRNG(hash);

			assert.throws(() => {
				prng.validateHash("");
			}, /Invalid or mismatched Hash/);
		}).timeout(0);

		it("실패: null/undefined → 에러", () => {
			const hash = HashPRNG.generateHash();
			const prng = new HashPRNG(hash);

			assert.throws(() => {
				prng.validateHash(null as any);
			}, /Invalid or mismatched Hash/);
		}).timeout(0);

		it("실패: 잘못된 해시(불일치) → 에러", () => {
			const hash = HashPRNG.generateHash();
			const prng = new HashPRNG(hash);

			assert.throws(() => {
				prng.validateHash("wrong_hash_value");
			}, /Invalid or mismatched Hash/);
		}).timeout(0);
	});

	// 3-2. 분포 품질
	describe("분포 품질", () => {

		it("10,000회 nextRandom → 평균 ≈ 0.5 (오차 ±0.05)", () => {
			const hash = HashPRNG.generateHash();
			const prng = new HashPRNG(hash);
			prng.createPRNG(hash);

			const N = 10000;
			let sum = 0;
			for (let i = 0; i < N; i++) {
				sum += prng.nextRandom();
			}
			const mean = sum / N;

			assert.isAbove(mean, 0.45, `평균 ${mean}이 0.45보다 커야 함`);
			assert.isBelow(mean, 0.55, `평균 ${mean}이 0.55보다 작아야 함`);
		}).timeout(0);

		it("10,000회 → 표준편차 ≈ 0.289 (균등분포 이론값, 오차 ±0.02)", () => {
			const hash = HashPRNG.generateHash();
			const prng = new HashPRNG(hash);
			prng.createPRNG(hash);

			const N = 10000;
			const values: number[] = [];
			for (let i = 0; i < N; i++) {
				values.push(prng.nextRandom());
			}

			const mean = values.reduce((a, b) => a + b, 0) / N;
			const variance = values.reduce((a, b) => a + (b - mean) ** 2, 0) / N;
			const stdDev = Math.sqrt(variance);

			// 균등분포 U(0,1)의 이론 표준편차: 1/sqrt(12) ≈ 0.2887
			assert.isAbove(stdDev, 0.269, `표준편차 ${stdDev}가 0.269보다 커야 함`);
			assert.isBelow(stdDev, 0.309, `표준편차 ${stdDev}가 0.309보다 작아야 함`);
		}).timeout(0);

		it("10구간 히스토그램 → 각 구간 빈도가 기대값의 ±20% 이내", () => {
			const hash = HashPRNG.generateHash();
			const prng = new HashPRNG(hash);
			prng.createPRNG(hash);

			const N = 10000;
			const bins = new Array(10).fill(0);
			for (let i = 0; i < N; i++) {
				const val = prng.nextRandom();
				const binIdx = Math.min(Math.floor(val * 10), 9);
				bins[binIdx]++;
			}

			const expected = N / 10; // 1000
			for (let i = 0; i < 10; i++) {
				const lowerBound = expected * 0.8;
				const upperBound = expected * 1.2;
				assert.isAbove(bins[i], lowerBound, `구간 ${i}: 빈도 ${bins[i]}이 ${lowerBound}보다 커야 함`);
				assert.isBelow(bins[i], upperBound, `구간 ${i}: 빈도 ${bins[i]}이 ${upperBound}보다 작아야 함`);
			}
		}).timeout(0);
	});
});
