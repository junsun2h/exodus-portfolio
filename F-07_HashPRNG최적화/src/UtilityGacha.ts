// -----------------------------------------------
// Gacha 확률 유틸리티
// -----------------------------------------------

import { EAPIErrorCode, EGrade, ETier } from "../Data/Generated/CommonEnum";
import { GameDB_Server_GachaChanceList } from "../Data/Generated/GameDBData";
import { throwLogicError } from "./UtilityBasic";

// 누적 확률 캐싱용 인터페이스
interface CumulativeGachaChance {
	accumulatedChance: number;
	tier: ETier;
	grade: EGrade;
}

// 누적 확률 캐시 저장용 WeakMap
const cumulativeChancesCache = new WeakMap<GameDB_Server_GachaChanceList, CumulativeGachaChance[]>();

// 누적 확률 배열 생성 함수
function buildCumulativeChances(list: GameDB_Server_GachaChanceList): CumulativeGachaChance[] {
	const result: CumulativeGachaChance[] = [];
	let accumulatedChance = 0.0;

	// NOTE: 루프 내에서 Math.abs(1 - acc) < EPSILON 스냅을 하지 않는다.
	// 중간 누적값이 우연히 1.0 근처가 되면 이후 엔트리가 dead code가 되는 버그가 있었다.
	// 이진 탐색은 left==length-1에서 끝나 마지막 엔트리를 자연스럽게 fallback으로 반환한다.
	for (const item of list.GachaChances) {
		accumulatedChance += item.Chance;
		result.push({
			accumulatedChance,
			tier: item.Tier,
			grade: item.Grade,
		});
	}
	return result;
}

// 이진 탐색으로 티어/등급 결정 (O(log n))
export function findTierGradeByBinarySearch(gachaChanceList: GameDB_Server_GachaChanceList, randValue: number): [ETier, EGrade] {
	// 빈 리스트 안전장치: fallback으로 undefined 반환되어 런타임 에러가 발생하는 것을 방지
	if (gachaChanceList.GachaChances.length === 0) {
		throwLogicError("findTierGradeByBinarySearch(): empty chance list", EAPIErrorCode.DATABASE_ERROR);
	}

	// 캐시에서 누적 확률 가져오기 (없으면 생성)
	let cumulative = cumulativeChancesCache.get(gachaChanceList);
	if (!cumulative) {
		cumulative = buildCumulativeChances(gachaChanceList);
		cumulativeChancesCache.set(gachaChanceList, cumulative);
	}

	// 이진 탐색: randValue 이상인 첫 번째 요소 찾기
	let left = 0;
	let right = cumulative.length - 1;

	while (left < right) {
		const mid = Math.floor((left + right) / 2);
		if (cumulative[mid].accumulatedChance < randValue) {
			left = mid + 1;
		} else {
			right = mid;
		}
	}

	const found = cumulative[left];
	return [found.tier, found.grade];
}
