import { CallableRequest } from "firebase-functions/v2/https";
import * as utilBasic from "../../Utility/UtilityBasic";
import { CoreDataKey, GameDBPath } from "../../Data/Constants/FirestorePaths";
import { EResponseCode, resultSuccess } from "../../Data/Types/ResponseCodes";
import { plainToInstance } from "class-transformer";
import { GameDB_Server_GachaChanceMap } from "../../Data/Generated/GameDBData";
import { CRequestBase } from "../Base/RequestBase";
import { EAPIErrorCode, EGachaLevel, EGachaProduct, ERedsoulstoneGachaItem, ECurrency, EGrade, EStage, EStageRewardPayment } from "../../Data/Generated/CommonEnum";
import { CoreData_Gacha, CoreData_Gachas } from "../../Data/Generated/CoreData";
import { ModValueInt } from "../../Data/Shared/Types/ModValueTypes";
import { PXBigInt } from "../../Data/Shared/Types/BigIntTypes";
import * as XPTables from "../../Data/Shared/Tables/XPTables";
import { CStageBase } from "./Stage";
import * as GameBalanceConfig from "../../Data/Shared/Config/GameBalanceConfig";
import { CoreDataLatestVersion } from "../Migration/CoreDataMigration";
import { throwLogicError } from "../../Utility/UtilityBasic";
import { ValidationSchema } from "../../Utility/InputValidation";
import { resolvePRNG } from "../../Data/Types/PRNGServer";
import { GameDBDatas } from "../Factory/GameDBFactory";
import { GachaRewardParameters } from "../../Data/Types/GachaServerTypes";
import { currency_Payment, currency_Add, isBigIntCurrency } from "./Currency";

// -----------------------------------------------
// Request
// -----------------------------------------------
export async function request_Gacha_GetLevelReward(request: CallableRequest<any>) {
	return await new CGacha_GetLevelReward().handleRequest(request.data, request);
}

export async function request_Gacha_RedSoulstone(request: CallableRequest<any>) {
	return await new CGacha_RedSoulstone().handleRequest(request.data, request);
}

// -----------------------------------------------
// Class
// -----------------------------------------------
export class CGachaBase extends CRequestBase {
	constructor(init?: Partial<CGachaBase>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.account, CoreDataKey.COREDATAKEY_ACCOUNT);
	}
}

export class CGacha_LevelUp extends CGachaBase {
	constructor(init?: Partial<CGacha_LevelUp>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			gachaProduct: {
				type: "enum",
				enumObject: EGachaProduct,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const gachaProduct: EGachaProduct = data.gachaProduct;

		return await this.executeGachaLevelUp(gachaProduct);
	}

	public async executeGachaLevelUp(gachaProduct: EGachaProduct): Promise<[EResponseCode, string]> {
		const gachaInfo = this.coreData.datas.gachas.Gachas.get(gachaProduct);
		if (!gachaInfo) {
			throwLogicError(`gacha_LevelUp(): This Product does not have gachaProduct(${gachaProduct}).`, EAPIErrorCode.DATABASE_ERROR);
		}

		const targetLevel = gachaInfo.Level + 1;
		const requiredXP = XPTables.GachaLevel_TotalXP[targetLevel];
		if (gachaInfo.Count.Value >= requiredXP) {
			gachaInfo.Count.Subtract(requiredXP);
			gachaInfo.Level = targetLevel as EGachaLevel;
		}

		return resultSuccess;
	}
}

export class CGacha_GetLevelReward extends CGachaBase {
	constructor(init?: Partial<CGacha_GetLevelReward>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.account, CoreDataKey.COREDATAKEY_ACCOUNT);
		this.addToCreateCoreData(this.coreData.datas.gachas, CoreDataKey.COREDATAKEY_GACHA);
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			gachaProduct: {
				type: "enum",
				enumObject: EGachaProduct,
			},
			gachaLevel: {
				type: "enum",
				enumObject: EGachaLevel,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const gachaProduct: EGachaProduct = data.gachaProduct;
		const gachaLevel: EGachaLevel = data.gachaLevel;

		return await this.executeGachaGetLevelReward(gachaProduct, gachaLevel);
	}

	public async executeGachaGetLevelReward(gachaProduct: EGachaProduct, gachaLevel: EGachaLevel): Promise<[EResponseCode, string]> {
		const gachaInfo = this.coreData.datas.gachas.Gachas.get(gachaProduct);
		if (!gachaInfo) {
			throwLogicError(`gacha_GetLevelReward(): This Product does not have gachaProduct(${gachaProduct}).`, EAPIErrorCode.DATABASE_ERROR);
		}

		if (gachaLevel > gachaInfo.Level) {
			throwLogicError(`gacha_GetLevelReward(): gachaLevel(${gachaLevel}) is higher than gachaInfo.Level(${gachaInfo.Level}).`, EAPIErrorCode.DATABASE_ERROR);
		}

		const rewardMap = this.gameDB.datas.gachaLevelRewardMap.MapData.get(gachaProduct);
		if (!rewardMap) {
			throwLogicError(`gacha_GetLevelReward(): This Product does not have gachaProduct(${gachaProduct}).`, EAPIErrorCode.DATABASE_ERROR);
		}

		const gachaReward = rewardMap.GachaRewardDatas.get(gachaLevel);
		if (!gachaReward) {
			throwLogicError(`gacha_GetLevelReward(): This Product does not have gachaLevel(${gachaLevel}).`, EAPIErrorCode.DATABASE_ERROR);
		}

		if (gachaInfo.RewardLevelReceived >= EGachaLevel.level10) {
			// 10레벨 보상까지 획득했으면 10레벨 보상을 반복해서 준다.
			const requiredCount = GameBalanceConfig.GachaLevelRewardRepeatCount.get(gachaProduct);
			if (gachaInfo.Count.Value >= requiredCount) {
				gachaInfo.Count.Subtract(requiredCount);
			} else {
				throwLogicError(`gacha_GetLevelReward(): gachaInfo.Count(${gachaInfo.Count.Value}) is lower than requiredCount(${requiredCount}).`, EAPIErrorCode.DATABASE_ERROR);
			}
		}

		// 보상 리스트 만들기
		const allRewardList = new Array<GachaRewardParameters>();
		const newReward = new GachaRewardParameters({
			PRNG: resolvePRNG(undefined),
			Currency: gachaReward.Currency,
			Tier: gachaReward.Tier,
			Grade: gachaReward.Grade,
			Count: ModValueInt.create(gachaReward.Count),
			BigIntCount: new PXBigInt(),
		});
		allRewardList.push(newReward);

		// 보상 지급 처리. StageAPI 를 사용
		await this.createInternalRequest(CStageBase).applyRewardDataToCoreData(allRewardList);

		// 레벨 보상 획득 처리
		gachaInfo.RewardLevelReceived = gachaLevel;

		return resultSuccess;
	}
}

export class CGacha_RedSoulstone extends CGachaBase {
	constructor(init?: Partial<CGacha_RedSoulstone>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		// 영혼석 차감을 위한 통화 데이터
		this.addToCreateCoreData(this.coreData.datas.currencies, CoreDataKey.COREDATAKEY_CURRENCY);

		// 보상 지급을 위한 데이터
		this.addToCreateCoreData(this.coreData.datas.equipments, CoreDataKey.COREDATAKEY_EQUIPMENT);
		this.addToCreateCoreData(this.coreData.datas.skills, CoreDataKey.COREDATAKEY_SKILL);
		this.addToCreateCoreData(this.coreData.datas.pets, CoreDataKey.COREDATAKEY_PET);

		// 골드 보상 계산을 위한 스테이지 데이터
		this.addToCreateCoreData(this.coreData.datas.stages, CoreDataKey.COREDATAKEY_STAGE);

		// GameDB 데이터
		this.gameDB.addToCreateList(this.gameDB.datas.redSoulStoneGachaItemMap, GameDBPath.RedSoulStoneGachaItem);
		this.gameDB.addToCreateList(this.gameDB.datas.stageRewardMap, GameDBPath.StageReward);
		this.gameDB.addToCreateList(this.gameDB.datas.stageMap, GameDBPath.Stage);
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			count: {
				type: "number",
				min: 1,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const count: number = data.count;
		return await this.executeRedSoulstoneGacha(count);
	}

	async executeRedSoulstoneGacha(count: number): Promise<[EResponseCode, string]> {
		// 필요한 영혼석 개수 계산
		const requiredRedSoulstones = count * GameBalanceConfig.num_redsoulstone_per_gacha;

		// 영혼석 보유량 검증
		const currentRedSoulstones = this.coreData.datas.currencies.Currencies.get(ECurrency.currency_redsoulstone);
		if (!currentRedSoulstones) {
			throwLogicError(`executeRedSoulstoneGacha(): currency_redsoulstone not found in currencies`, EAPIErrorCode.DATABASE_ERROR);
		}

		if (currentRedSoulstones.Count.GetValue() < requiredRedSoulstones) {
			throwLogicError(`executeRedSoulstoneGacha(): Insufficient redsoulstones. Required: ${requiredRedSoulstones}, Current: ${currentRedSoulstones.Count.GetValue()}`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 영혼석 차감
		currency_Payment(this.coreData.datas.currencies, ECurrency.currency_redsoulstone, requiredRedSoulstones);

		// 보상 리스트 만들기
		const allRewardList = new Array<GachaRewardParameters>();

		// count번 반복하면서 아이템 뽑기
		for (let i = 0; i < count; i++) {
			// 확률로 아이템 굴림
			const rolledItem = this.roll_RedSoulstoneGachaItem();

			// 아이템 데이터 가져오기
			const itemData = this.gameDB.datas.redSoulStoneGachaItemMap.MapData.get(rolledItem);
			if (!itemData) {
				throwLogicError(`executeRedSoulstoneGacha(): RedSoulStoneGachaItem(${rolledItem}) not found in redSoulStoneGachaItemMap`, EAPIErrorCode.DATABASE_ERROR);
			}

			// GachaRewardParameters 생성
			let newReward: GachaRewardParameters;

			// 골드인 경우 스테이지 기반 계산 적용
			if (itemData.Currency === ECurrency.currency_gold) {
				const goldAmount = this.calculateGoldReward(itemData.Count);
				const bigIntCount = new PXBigInt();
				bigIntCount.Set(goldAmount);
				newReward = new GachaRewardParameters({
					PRNG: resolvePRNG(undefined),
					Currency: itemData.Currency,
					Tier: itemData.Tier,
					Grade: EGrade.grade1,
					Count: ModValueInt.create(0),
					BigIntCount: bigIntCount,
				});
			} else if (isBigIntCurrency(itemData.Currency)) {
				// 골드 외 BigInt 재화(경험치 등)
				const bigIntCount = new PXBigInt();
				bigIntCount.Set(BigInt(itemData.Count));
				newReward = new GachaRewardParameters({
					PRNG: resolvePRNG(undefined),
					Currency: itemData.Currency,
					Tier: itemData.Tier,
					Grade: EGrade.grade1,
					Count: ModValueInt.create(0),
					BigIntCount: bigIntCount,
				});
			} else {
				newReward = new GachaRewardParameters({
					PRNG: resolvePRNG(undefined),
					Currency: itemData.Currency,
					Tier: itemData.Tier,
					Grade: EGrade.grade1,
					Count: ModValueInt.create(itemData.Count),
					BigIntCount: new PXBigInt(),
				});
			}
			allRewardList.push(newReward);
		}

		// 보상 지급 처리. StageAPI를 사용
		await this.createInternalRequest(CStageBase).applyRewardDataToCoreData(allRewardList);

		return resultSuccess;
	}

	// 스테이지 레벨 기반 골드 보상 계산
	// multiplier: 배수값 (GameDB에서 설정된 값)
	calculateGoldReward(multiplier: number): bigint {
		// 사용자의 최대 도달 스테이지 레벨 가져오기
		const bestStageLevel = this.coreData.datas.stages.BestStages.get(EStage.stage_main);
		if (!bestStageLevel) {
			throwLogicError(`calculateGoldReward(): BestStages for stage_main not found`, EAPIErrorCode.DATABASE_ERROR);
		}
		const stageLevel = bestStageLevel.GetValue();

		// stageRewardMap에서 stage_main 보상 데이터 가져오기
		const stageReward = this.gameDB.datas.stageRewardMap.MapData.get(EStage.stage_main);
		if (!stageReward) {
			throwLogicError(`calculateGoldReward(): stageRewardMap for stage_main not found`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 골드 보상 데이터 찾기 (reward_pay_perkill 타입)
		const goldRewardData = stageReward.StageRewardDatas.find(
			(data) => data.Currency === ECurrency.currency_gold && data.Payment === EStageRewardPayment.perkill
		);
		if (!goldRewardData) {
			throwLogicError(`calculateGoldReward(): currency_gold reward data not found in stageRewardMap`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 스테이지 레벨 기반 골드 수량 계산 (소수점 버림)
		const goldPerMonster = Math.floor(goldRewardData.Count.getValue(stageLevel).GetValue());

		// 스테이지의 몬스터 개체수 가져오기
		const stageData = this.gameDB.datas.stageMap.MapData.get(EStage.stage_main);
		if (!stageData) {
			throwLogicError(`calculateGoldReward(): stageMap for stage_main not found`, EAPIErrorCode.DATABASE_ERROR);
		}
		const monsterCount = stageData.AllMonsterCount;

		// 최종 골드 계산: (몬스터당 골드 × 몬스터 개체수) × 배수
		const totalGold = BigInt(goldPerMonster) * BigInt(monsterCount) * BigInt(multiplier);

		return totalGold;
	}

	// RedSoulStoneGachaItem을 확률로 굴림
	roll_RedSoulstoneGachaItem(): ERedsoulstoneGachaItem {
		const gachaItemMap = this.gameDB.datas.redSoulStoneGachaItemMap.MapData;

		// 전체 확률 합계 계산
		let totalChance = 0.0;
		for (const [key, value] of gachaItemMap.entries()) {
			totalChance += value.Chance;
		}

		// 0.0 ~ totalChance 범위의 난수 생성
		const randomValue = Math.random() * totalChance;

		// 누적 확률로 아이템 결정
		let accumulateChance = 0.0;
		for (const [key, value] of gachaItemMap.entries()) {
			accumulateChance += value.Chance;
			if (accumulateChance >= randomValue) {
				return key;
			}
		}

		// 여기까지 왔다는 것은 오류
		throwLogicError(`roll_RedSoulstoneGachaItem(): Failed to roll item. totalChance: ${totalChance}, randomValue: ${randomValue}`, EAPIErrorCode.DATABASE_ERROR);
	}
}

// -----------------------------------------------
// GameDB
// -----------------------------------------------
export async function GetGameDB_GachaChanceMap() {
	const docData = await utilBasic.getDocumentData(GameDBPath.GachaChance);
	const dbObject = plainToInstance(GameDB_Server_GachaChanceMap, docData);

	return Promise.resolve(dbObject);
}

// -----------------------------------------------
// Function
// -----------------------------------------------
export async function gacha_CreateCoreData(DBDatas: GameDBDatas) {
	const coreDataMap = new CoreData_Gachas();

	for (const key of Object.keys(EGachaProduct)) {
		const value = EGachaProduct[key as keyof typeof EGachaProduct];
		if (value == EGachaProduct.None) continue;

		const newGacha = new CoreData_Gacha();
		newGacha.Level = EGachaLevel.level1;
		newGacha.Count = ModValueInt.create(0);
		newGacha.RewardLevelReceived = EGachaLevel.level1;
		coreDataMap.Gachas.set(value, newGacha);
	}

	return coreDataMap;
}
