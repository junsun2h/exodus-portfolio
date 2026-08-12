// 서버 전용 뽑기 관련 타입
// 동기화 불필요

import { ModValueInt } from "../Shared/Types/ModValueTypes";
import { PXBigInt } from "../Shared/Types/BigIntTypes";
import { IPRNG } from "../Shared/Types/PRNGTypes";
import { ECurrency, EGrade, ETier } from "../Generated/CommonEnum";

// 뽑기를 요청한 출처
export const EGachaFrom = {
	None: 0,
	Product: 10,
	Mailbox: 20,
	StageMainReward: 30,
	StageChallengeReward: 40,
} as const;
export type EGachaFrom = (typeof EGachaFrom)[keyof typeof EGachaFrom];

// 서버에서만 사용하는 뽑기(gacha) 보상에 사용되는 파라미터
// Equipment, Skill, Pet 등에 사용한다.
// 스테이지 보상, Product 의 Currency 중 뽑기가 필요한 항목이 있을 때 전달 하는데 사용
export class GachaRewardParameters {
	public readonly PRNG: IPRNG; // null 이면 Math.random 사용
	public readonly Currency: ECurrency;
	public readonly Count: ModValueInt;
	public readonly BigIntCount: PXBigInt;
	public readonly Tier: ETier;
	public readonly Grade: EGrade;

	constructor(props: { PRNG: IPRNG; Currency: ECurrency; Count: ModValueInt; BigIntCount: PXBigInt; Tier: ETier; Grade: EGrade }) {
		Object.assign(this, props);
	}
}
