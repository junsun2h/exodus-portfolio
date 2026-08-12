import { CallableRequest } from "firebase-functions/v2/https";
import { Timestamp } from "firebase-admin/firestore";
import * as utilBasic from "../../Utility/UtilityBasic";
import { logDebug } from "../../Utility/UtilityLogging";
import { ModValueDouble, ModValueInt } from "../../Data/Shared/Types/ModValueTypes";
import { PXBigInt } from "../../Data/Shared/Types/BigIntTypes";
import { CRequestBase } from "../Base/RequestBase";
import { instanceToPlain, plainToInstance } from "class-transformer";
import { CoreData_Stages } from "../../Data/Generated/CoreData";
import { GameDB_Server_StageMap, GameDB_Server_StageRewardData, GameDB_Server_StageRewardMap, GameDB_Server_StageReward_GreatRiftMap, GameDB_Server_StageReward_MainMap, GameDB_Server_StageReward_RiftMap } from "../../Data/Generated/GameDBData";
import { EAPIErrorCode, ECurrency, EEquipmentLargeType, EEquipmentType, EGrade, EMod, ENebula, EPreset, EProductSubscription, EStage, EStageBossRush, EStageGreatRift, EStageMain, EStageMainEndless, EStageMode, EStageRewardPayment, EStageRift, ETier } from "../../Data/Generated/CommonEnum";
import { ConstKey } from "../../Data/Constants/ServerConstants";
import { CoreDataKey, GameDBPath } from "../../Data/Constants/FirestorePaths";
import { EResponseCode, resultSuccess } from "../../Data/Types/ResponseCodes";
import { CCurrency_GachaWithParameters, currency_Add, currency_Subtract, isBigIntCurrency } from "./Currency";
import { CPlayer_LevelUp, CPlayer_Update_CombatPower } from "./Player";
import * as GameBalanceConfig from "../../Data/Shared/Config/GameBalanceConfig";
import { CEquipment_Create, CEquipment_GachaWithParameters } from "./Equipment";
import { CSkill_GachaWithParameters } from "./Skill";
import { CPet_GachaWithParameters } from "./Pet";
import { ExcludeCoreDataKey } from "../Factory/CoreDataFactory";
import { CoreDataLatestVersion } from "../Migration/CoreDataMigration";
import { CFloorRanks, UpdateRankData_Floor, CHallOfFame_FloorFirstClears } from "../ServerOperation/Rank";
import { throwLogicError, sampleBinomial } from "../../Utility/UtilityBasic";
import { HashPRNG, resolvePRNG } from "../../Data/Types/PRNGServer";
import { IPRNG } from "../../Data/Shared/Types/PRNGTypes";
import { DumpCoreDatas } from "../../Utility/CoreDataDump";
import { GameDBDatas, GameDBFactory } from "../Factory/GameDBFactory";
import { ValidationSchema } from "../../Utility/InputValidation";
import { GachaRewardParameters } from "../../Data/Types/GachaServerTypes";
import { ServerEventNotificationService } from "../ServerOperation/Notification/ServerEventNotification";
import { EServerEventNotificationType } from "../ServerOperation/Notification/ServerEventNotificationDefinitions";

// -----------------------------------------------
// Request
// -----------------------------------------------
export async function request_Stage_Enter(request: CallableRequest<any>) {
	return await new CStage_Enter().handleRequest(request.data, request);
}

export async function request_Stage_EndLoop(request: CallableRequest<any>) {
	return await new CStage_EndLoop().handleRequest(request.data, request);
}

export async function request_Stage_Clear(request: CallableRequest<any>) {
	return await new CStage_Clear().handleRequest(request.data, request);
}

export async function request_Stage_Fail(request: CallableRequest<any>) {
	return await new CStage_Fail().handleRequest(request.data, request);
}

export async function request_Stage_QuickClear(request: CallableRequest<any>) {
	return await new CStage_QuickClear().handleRequest(request.data, request);
}

// -----------------------------------------------
// Class
// -----------------------------------------------
// #region CStageBase
export class CStageBase extends CRequestBase {
	constructor(init?: Partial<CStageBase>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.account, CoreDataKey.COREDATAKEY_ACCOUNT);
		this.addToCreateCoreData(this.coreData.datas.player, CoreDataKey.COREDATAKEY_PLAYER);
		this.addToCreateCoreData(this.coreData.datas.stages, CoreDataKey.COREDATAKEY_STAGE);
		this.addToCreateCoreData(this.coreData.datas.currencies, CoreDataKey.COREDATAKEY_CURRENCY);
		this.addToCreateCoreData(this.coreData.datas.statistics, CoreDataKey.COREDATAKEY_STATISTICS);

		// mod 동적 합산을 위한 데이터 로드
		this.addToCreateCoreData(this.coreData.datas.nebulas, CoreDataKey.COREDATAKEY_NEBULA);
		this.addToCreateCoreData(this.coreData.datas.products, CoreDataKey.COREDATAKEY_PRODUCT);
		this.gameDB.addToCreateList(this.gameDB.datas.nebulaMap, GameDBPath.Nebula);
		this.gameDB.addToCreateList(this.gameDB.datas.awakenElementMap, GameDBPath.AwakenElement);
	}

	// 티어, 등급 보상 시트가 따로 있으면 반환
	getExtraRewardData(stage: EStage) {
		switch (stage) {
			case EStage.stage_main_tutorial:
			case EStage.stage_main_act1:
			case EStage.stage_main:
				return this.gameDB.datas.stageReward_MainMap;
			case EStage.stage_main_endless:
				return this.gameDB.datas.stageReward_MainEndlessMap;
			case EStage.stage_rift:
				return this.gameDB.datas.stageReward_RiftMap;
			case EStage.stage_greatrift:
				return this.gameDB.datas.stageReward_GreatRiftMap;
			default:
				return undefined;
		}
	}

	// 각 컨텐츠의 index로 변환해준다.
	convertStageLevelToStageIndex(stage: EStage, stage_level: number) {
		switch (stage) {
			case EStage.stage_main_tutorial:
			case EStage.stage_main_act1:
			case EStage.stage_main:
				return this.stageLevelToStageMainIndex(stage_level);
			case EStage.stage_main_endless:
				// endless는 하나의 레벨만 사용하므로 그대로 반환
				return EStageMainEndless.stage_main_endless;
			case EStage.stage_rift:
				return stage_level as EStageRift;
			case EStage.stage_greatrift:
				return stage_level as EStageGreatRift;
			case EStage.stage_bossrush:
				return stage_level as EStageBossRush;
			default:
				return stage_level;
		}
	}

	// 메인 스테이지 단계를 EStageMain 의 단계로 변환해준다.
	stageLevelToStageMainIndex(stage_level: number): EStageMain {
		const num_stage_per_area = GameBalanceConfig.num_stage_per_area;
		//const mapKey = Math.trunc(stage_level / (num_stage_per_area + 1)) + 1;
		const mapKey = Math.trunc((stage_level - 1) / num_stage_per_area) + 1;
		const retKey = mapKey as EStageMain;

		utilBasic.validateUnionRange<EStageMain>(retKey);

		return retKey;
	}

	updateEquipmentMap(map, largeType: EEquipmentLargeType, type: EEquipmentType, tier: ETier, grade: EGrade, count: number): Map<[EEquipmentLargeType, EEquipmentType, ETier, EGrade], number> {
		const key = [largeType, type, tier, grade];
		if (map.has(key)) {
			const orgCount = map.get(key);
			map.set(key, orgCount + count);
		} else {
			map.set(key, count);
		}
		return map;
	}

	async call_RequestCreateEquipment(equipmentArray: Array<[[EEquipmentLargeType, EEquipmentType, ETier, EGrade], number]>) {
		const request = this.createInternalRequest(CEquipment_Create);

		const arrEquipemtPromise = equipmentArray.map(async (item) => {
			const [[largeType, type, tier, grade], count] = item;

			await request.processRequest({ largeType, type, tier, grade, count }, {}, this.resultData);
		});

		await Promise.all(arrEquipemtPromise);
	}

	createStageRewards(PRNG: IPRNG, stage: EStage, stage_level: number, totalKill: number, totalDamage: number) {
		// 보상 결과를 담을 배열
		const RewardList: GachaRewardParameters[] = [];

		// 기본 보상(stage_reward )
		const rewardData = this.gameDB.datas.stageRewardMap.MapData.get(stage);
		const arrRewards1 = this.createRewards(PRNG, stage, stage_level, totalKill, totalDamage, rewardData.StageRewardDatas);
		// 결과 합침
		if (arrRewards1) RewardList.push(...arrRewards1);

		// 추가 보상(stage_reward_main, stage_reward_rift, ...)
		let arrRewards2: GachaRewardParameters[] | undefined;
		const extraRewardDB = this.getExtraRewardData(stage);
		if (extraRewardDB) {
			const stageIndex: any = this.convertStageLevelToStageIndex(stage, stage_level);
			const extraRewardData = extraRewardDB.MapData.get(stageIndex);

			arrRewards2 = this.createRewards(PRNG, stage, stage_level, totalKill, totalDamage, extraRewardData.StageRewardDatas);
			if (arrRewards2) RewardList.push(...arrRewards2);
		}

		// 클라이언트와 동일하게 그룹화 후 mod 한 번 적용
		return this.mergeAndApplyModifiers(RewardList);
	}

	// accountHash를 기반으로 확률을 굴려, StageRewardDatas를 GachaRewardParameters로 변환
	public createRewards(PRNG: IPRNG, stage: EStage, stage_level: number, totalKill: number, totalDamage: number, StageRewardDatas: GameDB_Server_StageRewardData[]): GachaRewardParameters[] {
		const rewards: GachaRewardParameters[] = [];

		const rollChanceHash = (PRNG: IPRNG, chance: number): boolean => {
			const randVal = PRNG.nextRandom();
			const success = randVal < chance;
			PRNG.logRandAndEnum(randVal, success ? 1 : 0); // TS용 포팅 함수
			return success;
		};

		// let rollChanceHash: (c: number) => boolean;
		// rollChanceHash = (c: number) => PRNG.nextRandom() < c;

		// 정렬된 리스트로 순회 (Currency -> Tier -> Grade 순) - 클라이언트와 동일한 순서 보장
		const sortedRewards = [...StageRewardDatas].sort((a, b) => {
			if (a.Currency !== b.Currency) return a.Currency - b.Currency;
			if (a.Tier !== b.Tier) return a.Tier - b.Tier;
			return a.Grade - b.Grade;
		});

		// 순차(for-of) 처리
		for (const item of sortedRewards) {
			let totalCount = 0;
			let bigIntTotalCount = 0n;

			// 몬스터 수(기본 or totalKill)
			let num_monster = this.gameDB.datas.stageMap.MapData.get(stage).AllMonsterCount;
			if (totalKill > 0) {
				num_monster = totalKill;
			}

			const chance = item.Chance.getValue(stage_level).GetValue();

			switch (item.Payment) {
				case EStageRewardPayment.complete: {
					// 한 번만 확률 판정
					if (rollChanceHash(PRNG, chance)) {
						if (isBigIntCurrency(item.Currency)) {
							bigIntTotalCount = PXBigInt.numberToBigInt(item.Count.getValue(stage_level).GetValue());
						} else {
							totalCount = item.Count.getValue(stage_level).GetValue();
						}
					}
					break;
				}

				case EStageRewardPayment.perkill: {
					if (chance >= 1.0) {
						// 100%일 경우 곱셈
						if (isBigIntCurrency(item.Currency)) {
							bigIntTotalCount = PXBigInt.numberToBigInt(item.Count.getValue(stage_level).GetValue() * num_monster);
						} else {
							totalCount = item.Count.getValue(stage_level).GetValue() * num_monster;
						}
					} else {
						// 최적화: 이항 분포 샘플링 (n회 독립 시행 → 2회 PRNG 호출)
						// 기존: num_monster회 독립 시행으로 O(n) PRNG 호출
						// 최적화: 이항 분포로 성공 횟수 직접 샘플링 O(1) 또는 O(log n)
						const rand1 = PRNG.nextRandom();
						const rand2 = PRNG.nextRandom();
						PRNG.logRandAndEnum(rand1, 0);
						PRNG.logRandAndEnum(rand2, 0);

						const successCount = sampleBinomial(num_monster, chance, rand1, rand2);

						if (isBigIntCurrency(item.Currency)) {
							bigIntTotalCount = BigInt(successCount);
						} else {
							totalCount = successCount;
						}
					}
					break;
				}

				case EStageRewardPayment.rank: {
					// 랭크 보상 로직 (예시, 생략)
					break;
				}

				case EStageRewardPayment.totalkill: {
					// totalKill값 기반으로 한 번 확률 판정
					if (rollChanceHash(PRNG, chance)) {
						if (isBigIntCurrency(item.Currency)) {
							bigIntTotalCount = PXBigInt.numberToBigInt(item.Count.getValue(totalKill).GetValue());
						} else {
							totalCount = item.Count.getValue(totalKill).GetValue();
						}
					}
					break;
				}

				case EStageRewardPayment.totaldamage: {
					// totalDamage를 그대로
					if (isBigIntCurrency(item.Currency)) {
						bigIntTotalCount = PXBigInt.numberToBigInt(totalDamage);
					} else {
						totalCount = totalDamage;
					}
					break;
				}

				default:
					throwLogicError("rollChanceHash(): createRewards: EStageRewardPayment is not Valid", EAPIErrorCode.DATABASE_ERROR);
			}

			// 둘 다 0이면 보상을 생성하지 않는다.
			if (totalCount === 0 && bigIntTotalCount === 0n) {
				continue;
			}

			// applyGainModifiers는 mergeAndApplyModifiers에서 합산 후 한 번만 적용

			// 최종 GachaRewardParameters 구성
			let count: ModValueInt = ModValueInt.create(0);
			const bigIntCount: PXBigInt = new PXBigInt();

			if (isBigIntCurrency(item.Currency)) {
				bigIntCount.Set(bigIntTotalCount);
				bigIntCount.BigIntToStirng();
			} else {
				utilBasic.verifyIntinity(totalCount);
				count = ModValueInt.create(totalCount);
			}

			const reward = new GachaRewardParameters({
				PRNG: PRNG,
				Currency: item.Currency,
				Count: count,
				BigIntCount: bigIntCount,
				Tier: item.Tier,
				Grade: item.Grade,
			});

			rewards.push(reward);
		}

		return rewards;
	}

	// 클라이언트와 동일하게 보상 그룹화 후 mod 한 번 적용
	private mergeAndApplyModifiers(rewards: GachaRewardParameters[]): GachaRewardParameters[] {
		// 0인 보상 필터링
		const filtered = rewards.filter((r) => r.Count.GetValue() !== 0 || r.BigIntCount.Value !== 0n);

		// Currency, Tier, Grade 기준 그룹화
		const groupMap = new Map<string, GachaRewardParameters[]>();
		for (const r of filtered) {
			const key = `${r.Currency}_${r.Tier}_${r.Grade}`;
			if (!groupMap.has(key)) {
				groupMap.set(key, []);
			}
			groupMap.get(key)!.push(r);
		}

		// 그룹화된 보상 정렬 (Currency -> Tier -> Grade)
		const sortedKeys = Array.from(groupMap.keys()).sort((a, b) => {
			const [aCur, aTier, aGrade] = a.split("_").map(Number);
			const [bCur, bTier, bGrade] = b.split("_").map(Number);
			if (aCur !== bCur) return aCur - bCur;
			if (aTier !== bTier) return aTier - bTier;
			return aGrade - bGrade;
		});

		const mergedRewards: GachaRewardParameters[] = [];

		for (const key of sortedKeys) {
			const group = groupMap.get(key)!;
			const first = group[0];

			// 합산
			let totalCount = group.reduce((sum, r) => sum + r.Count.GetValue(), 0);
			let totalBigInt = group.reduce((sum, r) => sum + r.BigIntCount.Value, 0n);

			// 합산 후 0이면 스킵
			if (totalCount === 0 && totalBigInt === 0n) {
				continue;
			}

			// 경험치/골드 획득 증가 mod 적용 (합산 후 한 번만)
			const modResult = this.applyGainModifiers(first.Currency, totalCount, totalBigInt);
			totalCount = modResult.count;
			totalBigInt = modResult.bigIntCount;

			// 새 GachaRewardParameters 생성
			const count = ModValueInt.create(totalCount);
			const bigIntCount = new PXBigInt();
			if (isBigIntCurrency(first.Currency)) {
				bigIntCount.Set(totalBigInt);
				bigIntCount.BigIntToStirng();
			}

			mergedRewards.push(
				new GachaRewardParameters({
					PRNG: first.PRNG,
					Currency: first.Currency,
					Count: count,
					BigIntCount: bigIntCount,
					Tier: first.Tier,
					Grade: first.Grade,
				})
			);
		}

		return mergedRewards;
	}

	/**
	 * Curreny.Comsulable(소비 가능한가)아 아닌 재화는 Currency에서 관리하는 것이 아닌 단순 재화의 구분으로만 쓰고
	 * 실제 개수는 각 CoreData에서 관리한다.
	 * Consumable이 아닌 재화들 예) PlayerCoreData.XP, Equipment, Skill
	 */
	public async applyRewardDataToCoreData(rewardList: GachaRewardParameters[]) {
		// 순차적으로 처리
		for (const reward of rewardList) {
			switch (reward.Currency) {
				// Equipment
				case ECurrency.currency_equipment_weapon:
				case ECurrency.currency_equipment_armour:
				case ECurrency.currency_equipment_accessory: {
					await this.createInternalRequest(CEquipment_GachaWithParameters).processRequest({ reward }, {}, this.resultData);
					break;
				}

				// Skill
				case ECurrency.currency_skill_spell:
				case ECurrency.currency_skill_aura:
				case ECurrency.currency_skill_rune: {
					await this.createInternalRequest(CSkill_GachaWithParameters).processRequest({ reward }, {}, this.resultData);
					break;
				}

				case ECurrency.currency_pet: {
					await this.createInternalRequest(CPet_GachaWithParameters).processRequest({ reward }, {}, this.resultData);
					break;
				}

				case ECurrency.currency_xp: {
					await this.createInternalRequest(CCurrency_GachaWithParameters).processRequest({ reward }, {}, this.resultData);

					// 레벨업
					await this.createInternalRequest(CPlayer_LevelUp).processRequest({}, {}, this.resultData);
					break;
				}

				default: {
					this.varifyStageRewardCurrency(reward.Currency);

					await this.createInternalRequest(CCurrency_GachaWithParameters).processRequest({ reward }, {}, this.resultData);
					break;
				}
			}
		}
	}

	varifyStageRewardCurrency(currency: ECurrency) {
		switch (currency) {
			case ECurrency.currency_equipment_reinforce:
			case ECurrency.currency_equipment_reforging:
			case ECurrency.currency_skill_reinforce:
			case ECurrency.currency_awakening_stone:
			case ECurrency.currency_neblua_offering_point:
			case ECurrency.currency_redsoulstone:
			case ECurrency.currency_diamond:
			case ECurrency.currency_gold:
			case ECurrency.currency_ticket_rift:
			case ECurrency.currency_ticket_greatrift:
			case ECurrency.currency_ticket_bossrush:
			case ECurrency.currency_ticket_corruption:
			case ECurrency.currency_ticket_incursion:
			case ECurrency.currency_ticket_invasion:
			case ECurrency.currency_mastery_crystal:
				return true;
			default:
				throwLogicError(`applyRewardDataToCoreData(): Currency(${currency}) is not valid`, EAPIErrorCode.DATABASE_ERROR);
		}
	}

	// 탑 클리어 보상 획득량 증가 mod 적용
	// Stage_Enter에서 저장한 MOD 스냅샷 사용 (Enter/Clear 사이 상태 변화 방지)
	// 스냅샷(StageMod*)은 퍼센트 원본이 아니라 이미 변환된 소수 배율이므로 DOUBLE로 담겨 있다.
	applyGainModifiers(currency: ECurrency, count: number, bigIntCount: bigint): { count: number; bigIntCount: bigint } {
		const hashInfo = this.coreData.datas.account.HashInfo;
		let modGainRate = 0;

		switch (currency) {
			case ECurrency.currency_xp:
				modGainRate = hashInfo.StageModXp.GetDouble;
				break;
			case ECurrency.currency_gold:
				modGainRate = hashInfo.StageModGold.GetDouble;
				break;
			default:
				modGainRate = hashInfo.StageModRewardQty.GetDouble;
				break;
		}

		if (modGainRate <= 0) {
			return { count, bigIntCount };
		}

		// 계산: 최종값 = 기본값 * (1 + modGainRate)
		// modGainRate는 이미 소수 배율(50% → 0.5)이므로 여기서 추가 변환하지 않는다.
		const multiplier = 1.0 + modGainRate;

		// BigInt 재화인 경우 (경험치, 골드) — Number 변환 없이 정수 비율로 계산
		if (bigIntCount !== 0n) {
			const scale = 10000n;
			const scaledMultiplier = BigInt(Math.round(multiplier * 10000));
			const newBigIntCount = (bigIntCount * scaledMultiplier) / scale;
			return { count, bigIntCount: newBigIntCount };
		}
		// 일반 재화인 경우
		else if (count > 0) {
			const newCount = Math.floor(count * multiplier);
			return { count: newCount, bigIntCount };
		}

		return { count, bigIntCount };
	}

	// 경험치/골드 획득 증가 mod 동적 합산
	// GameDB의 Nebula/AwakenElement Stat은 FLOAT_PER라 GetValue()가 퍼센트를 소수로 변환한다.
	// 따라서 반환값은 퍼센트(50)가 아니라 소수 배율(0.5)이다.
	getGainModifierTotal(modType: EMod): number {
		let total = 0;
		const nebulaMod = this.getNebulaModValue(modType);
		const awakenMod = this.getAwakenElementModValue(modType);
		total = nebulaMod + awakenMod;

		// 진단 로그: 클라/서버 MOD 불일치 간헐적 발생 원인 추적
		if (this.isEditorDebug && total > 0) {
			const nebulaData = this.coreData.datas.nebulas;
			const vipAdRemove = this.coreData.datas.products.ProductSubscription.get(EProductSubscription.product_subscription_vip_adremove);
			const isVip = vipAdRemove ? Boolean(vipAdRemove.IsSubscribing.GetValue()) : false;
			const boostInfo: string[] = [];
			for (const [nebulaType, nebula] of nebulaData.Nebulas) {
				const exp = (nebula as any).AdBoostExpirationTime ?? "null";
				const active = this.isNebulaAdBoostActive(nebula);
				const lv = nebula.Level.GetValue();
				boostInfo.push(`${utilBasic.getKeyName(ENebula, nebulaType)}(Lv${lv},boost=${active},exp=${exp})`);
			}
			logDebug(`[MOD_SYNC_DIAG] Server getGainModifierTotal(${utilBasic.getKeyName(EMod, modType)}): nebula=${nebulaMod}, awaken=${awakenMod}, total=${total}, isVip=${isVip}, boosts=[${boostInfo.join(" ")}]`);
		}

		return total;
	}

	// Nebula에서 mod 값 수집 (광고/VIP 조건부)
	getNebulaModValue(modType: EMod): number {
		const nebulaData = this.coreData.datas.nebulas;
		const vipAdRemove = this.coreData.datas.products.ProductSubscription.get(EProductSubscription.product_subscription_vip_adremove);
		const isVipAdRemove = vipAdRemove ? Boolean(vipAdRemove.IsSubscribing.GetValue()) : false;

		for (const [nebulaType, nebula] of nebulaData.Nebulas) {
			const isActive = isVipAdRemove || this.isNebulaAdBoostActive(nebula);
			const nebulaDB = this.gameDB.datas.nebulaMap.MapData.get(nebulaType);

			if (isActive && nebula.Level.GetValue() > 0) {
				if (nebulaDB && nebulaDB.Mod === modType) {
					const modValue = nebulaDB.Stat.getValue(nebula.Level.GetValue()).GetValue();
					return modValue;
				}
			}
		}
		return 0;
	}

	// Awaken Element에서 mod 값 수집 (레벨 기반, 항상 활성화)
	getAwakenElementModValue(modType: EMod): number {
		const awakenData = this.coreData.datas.player.Awaken;

		for (const [elementType, levelValue] of awakenData.AwakenModLevels) {
			const elementDB = this.gameDB.datas.awakenElementMap.MapData.get(elementType);

			if (levelValue.GetValue() > 0) {
				if (elementDB && elementDB.Mod === modType) {
					const modValue = elementDB.Stat.getValue(levelValue.GetValue()).GetValue();
					return modValue;
				}
			}
		}
		return 0;
	}

	// Nebula 광고 부스트 활성화 여부 확인
	isNebulaAdBoostActive(nebula: any): boolean {
		if (!nebula.AdBoostExpirationTime) return false;
		return new Date() < new Date(nebula.AdBoostExpirationTime);
	}

	// 스테이지 레벨로부터 적절한 EStage 타입을 반환
	getStageTypeFromLevel(stage_level: number): EStage {
		if (stage_level <= GameBalanceConfig.end_stage_tutorial) {
			// 1-20
			return EStage.stage_main_tutorial;
		} else if (stage_level <= GameBalanceConfig.end_stage_floor1) {
			// 21-100
			return EStage.stage_main_act1;
		} else if (stage_level <= GameBalanceConfig.end_stage_main) {
			// 101-1000
			return EStage.stage_main;
		} else if (stage_level >= GameBalanceConfig.start_stage_endless) {
			// 1001+
			return EStage.stage_main_endless;
		}
		throwLogicError(`getStageTypeFromLevel(): Invalid stage level: ${stage_level}`, EAPIErrorCode.INVALID_PARAMETERS);
	}

	isMainStage(stage: EStage) {
		switch (stage) {
			case EStage.stage_main_tutorial:
			case EStage.stage_main_act1:
			case EStage.stage_main:
			case EStage.stage_main_endless:
				return true;
		}

		return false;
	}

	isClearMainStage() {
		const bestStageMain = this.coreData.datas.stages.BestStages.get(EStage.stage_main).GetValue();
		if (bestStageMain > GameBalanceConfig.end_stage_main) {
			return true;
		}
		return false;
	}

	// 보스 도전 기능이 존재하는 스테이지인가
	isBossTryStage(stage: EStage) {
		switch (stage) {
			case EStage.stage_main_tutorial:
			case EStage.stage_main_act1:
			case EStage.stage_main:
			case EStage.stage_main_endless:
			case EStage.stage_rift:
				return true;
		}

		return false;
	}

	// stage_level 체크가 필요 없는 도전 스테이지인가
	// 레벨업이 입장하는 스테이지
	isNoStageLevelNeeded(stage: EStage) {
		switch (stage) {
			case EStage.stage_corruption:
			case EStage.stage_bossrush:
				return true;
		}

		return false;
	}

	// 롤백용 원본 Hash 저장 (에러 발생 시 복원용)
	private _originalStageMainHash: string | null = null;

	// 도전 스테이지(QuickClear 등)는 stageMainHash 가 의미 없으므로 호출자가 생략 가능
	createStageMainPRNG(stage: EStage, stageMainHash: string = ""): IPRNG {
		let PRNG = undefined;
		if (this.isMainStage(stage)) {
			// 원본 Hash 보존 (롤백용)
			this._originalStageMainHash = this.coreData.datas.account.HashInfo.StageMainHash;

			PRNG = new HashPRNG(this._originalStageMainHash, this.isEditorDebug);
			this.coreData.datas.account.HashInfo.StageMainHash = PRNG.createPRNG(stageMainHash);
		}

		return resolvePRNG(PRNG);
	}

	// Hash 롤백 (에러 발생 시 원본 Hash 복원)
	rollbackStageMainHash(): void {
		if (this._originalStageMainHash !== null) {
			this.coreData.datas.account.HashInfo.StageMainHash = this._originalStageMainHash;
			this._originalStageMainHash = null;
		}
	}

	// Verify -----------------------------------------------
	verify_StageLevel(stage: EStage, stage_level: number) {
		if (stage_level == 0) {
			throwLogicError("verify_StageLevel(): stage_level cannot be 0. It starts with 1.", EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 단계를 사용하지 않는 스테이지 구성인가
		// EStage.stage_greatrift || EStage.stage_corruption || EStage.stage_bossrush
		if (!this.gameDB.datas.stageMap.MapData.get(stage).HasStageLevel && stage_level != 1) {
			throwLogicError(`verify_StageLevel(): 단계가 없는 스테이지의(${stage}) stage_level(${stage_level})은 항상 1이어야 합니다.`, EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	verifyRequestParameter(stage: EStage, totalKill: number = 0, strTotalDamage: string = "0", clearTime: number = 0) {
		const stageMode = this.gameDB.datas.stageMap.MapData.get(stage).StageMode;
		// API 요청에서 모든 파라미터가 전달되며, 각 스테이지 모드별 필수값만 검증
		switch (stageMode) {
			case EStageMode.stagemode_main: //모든 일반 몬스터 그룹을 처치하면 보스 몬스터가 스폰
			case EStageMode.stagemode_groups: //일반 몬스터 그룹만 등장
			case EStageMode.stagemode_raid: //타임 오버
				// 필수값 없음
				break;
			case EStageMode.stagemode_1boss: //보스 몬스터 한 마리 등장
				// clearTime이 필수
				if (clearTime === undefined || clearTime === 0) {
					throwLogicError(`verifyRequestParameter(): clearTime(${clearTime}) 값이 없습니다.`, EAPIErrorCode.INVALID_PARAMETERS);
				}
				break;
			case EStageMode.stagemode_inc_bosses: //죽으면 점점 강해지는 보스 몬스터가 한 마리씩 등장
			case EStageMode.stagemode_inc_groups: //죽으면 점점 강해지는 일반 몬스터가 한 마리씩 등장
				// totalKill이 필수
				if (totalKill === undefined || totalKill === 0) {
					throwLogicError(`verifyRequestParameter(): totalKill(${totalKill}) 값이 없습니다.`, EAPIErrorCode.INVALID_PARAMETERS);
				}
				break;
			case EStageMode.stagemode_undying_1boss: //죽지 않는 보스 몬스터 한 마리 등장
				// strTotalDamage가 필수
				if (strTotalDamage === undefined || strTotalDamage === "0") {
					throwLogicError(`verifyRequestParameter(): strTotalDamage(${strTotalDamage}) 값이 없습니다.`, EAPIErrorCode.INVALID_PARAMETERS);
				}
				break;
			default:
				throwLogicError(`throwLogicError(): verifyRequestParameter: stageMode(${stageMode}) is not vaild`, EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	// 소탕 가능한 스테이지 인지 (이전에 클리어한 스테이지인지)
	verify_CanQuickClear(stage: EStage, stage_level: number) {
		const bestStage = this.coreData.datas.stages.BestStages.get(stage).GetValue();
		if (stage_level > bestStage) {
			throwLogicError("verify_CanQuickClear(): Unreachable Stage Entry Request", EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	// 입장권 확인
	verify_EnterTicket(stage: EStage, count: number) {
		const stageDB = this.gameDB.datas.stageMap.MapData.get(stage);
		if (this.coreData.datas.currencies.Currencies.get(stageDB.EnterTicket).Count.GetValue() * count < stageDB.Num_Ticket) {
			throwLogicError("verify_EnterTicket(): Not enough count of Currency:Enter Tickers.", EAPIErrorCode.NOT_ENOUGH_CURRENCY);
		}
	}

	// Update -----------------------------------------------
	// 최대 도달 스테이지 업데이트
	update_BestStage(stage: EStage, stage_level: number): boolean {
		const bestStage = this.coreData.datas.stages.BestStages.get(stage).GetValue();

		if (stage_level > bestStage) {
			switch (stage) {
				case EStage.stage_main_tutorial:
					this.coreData.datas.stages.BestStages.set(stage, ModValueInt.create(stage_level));
					this.coreData.datas.stages.BestStages.set(EStage.stage_main_act1, ModValueInt.create(stage_level));
					this.coreData.datas.stages.BestStages.set(EStage.stage_main, ModValueInt.create(stage_level));
					break;
				case EStage.stage_main_act1:
					this.coreData.datas.stages.BestStages.set(stage, ModValueInt.create(stage_level));
					this.coreData.datas.stages.BestStages.set(EStage.stage_main, ModValueInt.create(stage_level));
					break;
				case EStage.stage_main_endless:
					// endless는 별도로 관리
					this.coreData.datas.stages.BestStages.set(stage, ModValueInt.create(stage_level));
					break;
				default: // main
					this.coreData.datas.stages.BestStages.set(stage, ModValueInt.create(stage_level));
					break;
			}
			return true;
		}

		return false;
	}

	// 스테이지 클리어 횟수 카운팅
	update_ClearCount(stage: EStage, count: number) {
		const clearCount = this.coreData.datas.stages.StageClearCounts.get(stage);
		this.coreData.datas.stages.StageClearCounts.set(stage, ModValueInt.create(clearCount.GetValue() + count));
	}

	// 스테이지 실패 횟수 기록
	update_FailCount(stage: EStage) {
		const failCount = this.coreData.datas.stages.StageFailCounts.get(stage);
		this.coreData.datas.stages.StageFailCounts.set(stage, ModValueInt.create(failCount.GetValue() + 1));
	}

	// 스테이지 최고 가한 피해 기록
	update_BestTotalDamage(stage: EStage, totalDamage: number) {
		const oldBest = this.coreData.datas.stages.BestTotalDamages.get(stage);
		if (totalDamage > oldBest.GetValue()) {
			this.coreData.datas.stages.BestTotalDamages.set(stage, ModValueInt.create(totalDamage));
		}
	}

	// 스테이지 최고 처치 수 기록
	update_BestTotalKill(stage: EStage, newTotalKill: number) {
		const oldBest = this.coreData.datas.stages.BestTotalKills.get(stage);
		if (newTotalKill > oldBest.GetValue()) {
			this.coreData.datas.stages.BestTotalKills.set(stage, ModValueInt.create(newTotalKill));
		}
	}

	// 스테이지 최고 클리어 시간 기록
	update_BestClearTime(stage: EStage, newClerTime: number) {
		const oldBest = this.coreData.datas.stages.BestClearTimes.get(stage);
		if (newClerTime > oldBest.GetValue()) {
			this.coreData.datas.stages.BestClearTimes.set(stage, ModValueInt.create(newClerTime));
		}
	}

	// 처치 수 카운팅
	update_mainStageKillCount(stage: EStage) {
		this.coreData.datas.statistics.Monster_KillCount_Normal.Add(this.gameDB.datas.stageMap.MapData.get(stage).AllMonsterCount);
		this.coreData.datas.statistics.Monster_KillCount_Boss.Add(1);
	}

	update_killCount(killCount: number) {
		this.coreData.datas.statistics.Monster_KillCount_Normal.Add(killCount);
	}

	// 성운 공물 포인트 업데이트
	update_NebulaPoint(stage: EStage) {
		const multipler = GameBalanceConfig.nebula_offering_point_multiplier;

		currency_Add(this.coreData.datas.currencies, ECurrency.currency_neblua_offering_point, this.gameDB.datas.stageMap.MapData.get(stage).AllMonsterCount * multipler);
	}

	// 입장권 차감
	update_EnterTicker(stage: EStage, count: number) {
		const stageDB = this.gameDB.datas.stageMap.MapData.get(stage);
		currency_Subtract(this.coreData.datas.currencies, stageDB.EnterTicket, stageDB.Num_Ticket * count);
	}
}
// #endregion

export class CStage_Enter extends CStageBase {
	constructor(init?: Partial<CStage_Enter>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			stage: {
				type: "enum",
				enumObject: EStage,
			},
			stage_level: {
				type: "number",
				min: 1,
				max: 100000,
			},
			stageMainHash: {
				type: "string",
				maxLength: 100,
			},
			// 도전 스테이지에서만 동봉되는 매핑 프리셋 id (랭킹 빌드 식별/로그용)
			preset: {
				type: "enum",
				enumObject: EPreset,
				required: false,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const stage: EStage = data.stage;
		const stage_level = data.stage_level;
		const stageMainHash = data.stageMainHash;
		return await this.executeStageEnter(stage, stage_level, stageMainHash);
	}

	executeStageEnter(stage: EStage, stage_level: number, stageMainHash: string): [EResponseCode, string] {
		// 실제 스테이지 타입 확인 (레벨 기반)
		if (!this.isNoStageLevelNeeded(stage)) {
			this.verify_StageLevel(stage, stage_level);
		}

		if (this.isMainStage(stage)) {
			// 해시 검증을 먼저 수행 (early return 전에)
			if (this.coreData.datas.account.HashInfo.StageMainHash !== stageMainHash) {
				throwLogicError("executeStageEnter(): stageMainHash is invalid or mismatched Hash", EAPIErrorCode.INVALID_PARAMETERS);
			}

			// 현재 진행 스테이지와 같다면 반복 입장이기 때문에 진행 위치는 다시 계산하지 않는다.
			// 단, MOD 스냅샷은 아래에서 항상 갱신해야 하므로 여기서 early return 하지 않는다.
			const isRepeatEnter = this.coreData.datas.stages.CurrentStage_Main.GetValue() == stage_level;
			if (!isRepeatEnter) {
				// 스테이지 점핑 차단 - 아직 클리어하지 않은 앞 구간만 차단한다.
				// 이미 클리어한 스테이지로의 복귀는 허용 (소탕 verify_CanQuickClear 과 동일하게 BestStage 기준)
				const bestStage = this.coreData.datas.stages.BestStages.get(stage).GetValue();
				if (stage_level > bestStage) {
					throwLogicError("executeStageEnter(): 스테이지 점핑 불가. 클리어한 스테이지만 이동 가능", EAPIErrorCode.INVALID_PARAMETERS);
				}

				// 현재 진행 위치를 이동한 스테이지로 갱신한다.
				// BestStages 는 update_BestStage 가 최대값일 때만 갱신하므로 최고 기록은 후퇴하지 않는다.
				this.coreData.datas.stages.CurrentStage_Main = ModValueInt.create(stage_level);
			}
		} else {
			this.verify_EnterTicket(stage, 1);
		}

		// Enter 시점 MOD 스냅샷 저장 (모든 스테이지 공통, 반복 입장에서도 항상 갱신)
		// Clear까지 시간이 걸리므로 Enter 시점의 MOD 상태를 확정하여 동기화 보장
		//
		// [데모] 현재는 XP/골드/보상수량 증가 MOD를 서버에서 적용하지 않는다 (항상 0 저장).
		// 클라 CryptoValueDouble 에 Create(JSONNode) 오버로드가 없어 스냅샷을 무조건 0.0 으로 파싱한다.
		// 서버만 실제 MOD를 적용하면 스테이지 보상 검증(CompareCoreDatas)이 XP/Gold 에서 어긋난다.
		// 0 으로 덮어써야 이전에 저장된 값도 함께 초기화되므로 갱신 자체를 생략하지 않는다.
		// 클라 파싱을 고친 뒤 아래 getGainModifierTotal() 적용으로 되돌릴 것.
		const hashInfo = this.coreData.datas.account.HashInfo;
		hashInfo.StageModXp = ModValueDouble.create(0);
		hashInfo.StageModGold = ModValueDouble.create(0);
		hashInfo.StageModRewardQty = ModValueDouble.create(0);
		// hashInfo.StageModXp = ModValueDouble.create(this.getGainModifierTotal(EMod.mod_xp_gain_inc));
		// hashInfo.StageModGold = ModValueDouble.create(this.getGainModifierTotal(EMod.mod_gold_gain_inc));
		// hashInfo.StageModRewardQty = ModValueDouble.create(this.getGainModifierTotal(EMod.mod_tower_clear_reward_quantity_inc));

		return resultSuccess;
	}

}

export class CStage_EndLoop extends CStageBase {
	constructor(init?: Partial<CStage_EndLoop>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.equipments, CoreDataKey.COREDATAKEY_EQUIPMENT);

		// CacheData 를 사용할 것이므로 JsonResultData 에서는 제외
		this.excludeJson(ExcludeCoreDataKey.EQUIPMENT);
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			stage: {
				type: "enum",
				enumObject: EStage,
			},
			stage_level: {
				type: "number",
				min: 1,
				max: 100000,
			},
			stageMainHash: {
				type: "string",
				maxLength: 100,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const stage: EStage = data.stage;
		const stage_level: number = data.stage_level;
		const stageMainHash = data.stageMainHash;

		return await this.executeStageEndLoop(stage, stage_level, stageMainHash);
	}

	async executeStageEndLoop(stage: EStage, stage_level: number, stageMainHash: string): Promise<[EResponseCode, string]> {
		if (!this.isNoStageLevelNeeded(stage)) {
			this.verify_StageLevel(stage, stage_level);
		}

		if (!this.isMainStage(stage)) {
			throwLogicError("executeStageEndLoop(): Called not on the main stage", EAPIErrorCode.INVALID_PARAMETERS);
		}

		// PRNG 생성 및 해쉬 갱신
		const PRNG: IPRNG = this.createStageMainPRNG(stage, stageMainHash);

		// 보상 리스트 만들기
		const rewardList = this.createStageRewards(PRNG, stage, stage_level, 0, 0);
		// 보상 지급 처리. 각 CoreData에 적용
		await this.applyRewardDataToCoreData(rewardList);
		const stageName = utilBasic.getKeyName(EStage, stage);
		await PRNG.writeRandomLogsToFile?.(stageName + "_" + stage_level);

		await this.createInternalRequest(CPlayer_Update_CombatPower).processRequest({}, {}, this.resultData);

		// Stage 보상 동기화 검증 (rewardList 전달하여 Stage 보상만 비교)
		await new DumpCoreDatas({
			UUID: this.UUID,
			serverID: this.getServerID(),
			gameDB: this.gameDB,
			coreData: this.coreData,
			isEditorDebug: this.isEditorDebug,
		}).processRequest({ stage, stage_level, stageMainHash, stageRewardList: rewardList }, {}, this.resultData);

		this.update_ClearCount(stage, 1);

		this.update_mainStageKillCount(stage);
		this.update_NebulaPoint(stage);

		return resultSuccess;
	}
}

export class CStage_Clear extends CStageBase {
	constructor(init?: Partial<CStage_Clear>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.equipments, CoreDataKey.COREDATAKEY_EQUIPMENT);

		// CacheData 를 사용할 것이므로 JsonResultData 에서는 제외
		this.excludeJson(ExcludeCoreDataKey.EQUIPMENT);
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			stage: {
				type: "enum",
				enumObject: EStage,
			},
			stage_level: {
				type: "number",
				min: 1,
				max: 100000,
			},
			totalKill: {
				type: "number",
				min: 0,
				max: 1000000,
			},
			strTotalDamage: {
				type: "string",
				maxLength: 50,
			},
			clearTime: {
				type: "number",
				min: 0,
				max: 86400, // 24시간 (초 단위)
			},
			stageMainHash: {
				type: "string",
				maxLength: 100,
			},
			// 도전 스테이지에서만 동봉되는 매핑 프리셋 id (랭킹 빌드 식별/로그용)
			preset: {
				type: "enum",
				enumObject: EPreset,
				required: false,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const stage: EStage = data.stage;
		const stage_level = data.stage_level;
		const totalKill = data.totalKill;
		const strTotalDamage = data.strTotalDamage;
		const clearTime = data.clearTime;
		const stageMainHash = data.stageMainHash;

		return await this.executeStageClear(stage, stage_level, stageMainHash, totalKill, strTotalDamage, clearTime);
	}

	async executeStageClear(stage: EStage, stage_level: number, stageMainHash: string, totalKill: number = 0, strTotalDamage: string = "0", clearTime = 0): Promise<[EResponseCode, string]> {
		this.verifyRequestParameter(stage, totalKill, strTotalDamage, clearTime);
		if (!this.isNoStageLevelNeeded(stage)) {
			this.verify_StageLevel(stage, stage_level);
		}

		// 최고 도달 스테이지 업데이트
		const isUpdatedBestStage = this.update_BestStage(stage, stage_level);
		this.update_ClearCount(stage, 1);

		const totalDamage = utilBasic.calculateDamageToAmount(strTotalDamage);
		this.update_BestTotalDamage(stage, totalDamage);
		this.update_BestTotalKill(stage, totalKill);

		// PRNG 생성 및 해쉬 갱신
		const PRNG: IPRNG = this.createStageMainPRNG(stage, stageMainHash);

		// 보상 리스트 만들기
		const rewardList = this.createStageRewards(PRNG, stage, stage_level, totalKill, totalDamage);
		// 보상 지급 처리. 각 CoreData에 적용
		await this.applyRewardDataToCoreData(rewardList);
		const stageName = utilBasic.getKeyName(EStage, stage);
		await PRNG.writeRandomLogsToFile?.(stageName + "_" + stage_level);

		// Stage 보상 동기화 검증 (rewardList 전달하여 Stage 보상만 비교)
		await new DumpCoreDatas({
			UUID: this.UUID,
			serverID: this.getServerID(),
			gameDB: this.gameDB,
			coreData: this.coreData,
			isEditorDebug: this.isEditorDebug,
		}).processRequest({ stage, stage_level, stageMainHash, stageRewardList: rewardList }, {}, this.resultData);

		// 메인 스테이지
		if (this.isMainStage(stage)) {
			// 최고 스테이지 도달이라면 랭킹 정보 업데이트
			if (isUpdatedBestStage) {
				// UpdateRank_BestStageData 객체 생성
				const updateData: UpdateRankData_Floor = {
					// 현재 층 번호 가져오기
					floorNumber: this.getFloorFromStageLevel(stage_level),
					strNickName: this.coreData.datas.account.LoginInfo.strNickName,
					bestStageMain: stage_level, // 업데이트된 스테이지 레벨
					awakenGrade: this.coreData.datas.player.Awaken.AwakenGrade,
				};

				// UpdateRank_BestStage 함수 호출 및 await 처리
				await new CFloorRanks(this.getServerID()).updateRankData(updateData, this.UUID);

				// 100층 단위 선착순 클리어 알림 (100, 200, 300...)
				const stagesPerFloor = GameBalanceConfig.num_area_per_floor * GameBalanceConfig.num_stage_per_area; // 100
				if (stage_level % stagesPerFloor === 0) {
					const floorNumber = this.getFloorFromStageLevel(stage_level);
					const hallOfFame = new CHallOfFame_FloorFirstClears(this.getServerID());
					const [resultCode, resultMsg, rank] = await hallOfFame.recordFirstClear(
						{
							floorNumber: floorNumber,
							strNickName: this.coreData.datas.account.LoginInfo.strNickName,
							timestamp: Timestamp.now(),
							bestStage: stage_level,
						},
						this.UUID
					);

					// 선착순 5명 안에 들면 알림 발송 (채팅 + 공지)
					if (resultCode === EResponseCode.Success && rank > 0) {
						const service = new ServerEventNotificationService(this.getServerID());
						await service.sendNotification(EServerEventNotificationType.FLOOR_FIRST_CLEAR, {
							nickName: this.coreData.datas.account.LoginInfo.strNickName,
							floorNumber: floorNumber,
							rank: rank,
						});
					}
				}
			}

			this.update_mainStageKillCount(stage);
			this.update_NebulaPoint(stage);

			// 다음 스테이지로 진행
			const currentStageLevel = this.coreData.datas.stages.CurrentStage_Main.GetValue();
			let nextStageLevel = currentStageLevel + 1;

			// 1000 → 1001 점프 처리 (stage_main → stage_main_endless 전환)
			if (currentStageLevel === GameBalanceConfig.end_stage_main) {
				nextStageLevel = GameBalanceConfig.start_stage_endless;
			}

			this.coreData.datas.stages.CurrentStage_Main = ModValueInt.create(nextStageLevel);

			await this.createInternalRequest(CPlayer_Update_CombatPower).processRequest({}, {}, this.resultData);
		} // 도전 스테이지
		else {
			// 클리어 했을 때만 입장권 소진
			this.update_EnterTicker(stage, 1);
		}

		return resultSuccess;
	}

	// 스테이지에서 층 번호를 결정하는 함수
	public getFloorFromStageLevel(stage_level: number): number {
		const stages_per_floor = GameBalanceConfig.num_area_per_floor * GameBalanceConfig.num_stage_per_area; // 5 * 20 = 100
		let floorNumber = Math.ceil(stage_level / stages_per_floor);

		// 최대 층 번호를 초과하지 않도록 제한
		if (floorNumber > GameBalanceConfig.num_floor) {
			floorNumber = GameBalanceConfig.num_floor;
		}

		return floorNumber;
	}
}

export class CStage_Fail extends CStageBase {
	constructor(init?: Partial<CStage_Fail>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			stage: {
				type: "enum",
				enumObject: EStage,
			},
			stage_level: {
				type: "number",
				min: 0, // 0 값 실패 테스트를 위해 허용
				max: 100000,
			},
			stageMainHash: {
				type: "string",
				maxLength: 100,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const stage: EStage = data.stage;
		const stage_level = data.stage_level;
		const stageMainHash = data.stageMainHash;
		return await this.executeStageFail(stage, stageMainHash);
	}

	executeStageFail(stage: EStage, stageMainHash: string): [EResponseCode, string] {
		this.update_FailCount(stage);

		if (this.isMainStage(stage)) {
			let backToStage: number;
			// 되돌아갈 스테이지 레벨
			switch (stage) {
				case EStage.stage_main_tutorial: // 1 ~ 20
					// 튜토리얼은 스테이지 실패 시 -1 스테이지로 보내지 않는다.
					backToStage = this.coreData.datas.stages.CurrentStage_Main.GetValue();
					backToStage = utilBasic.clamp(backToStage, 1, GameBalanceConfig.end_stage_tutorial);
					break;
				case EStage.stage_main_act1: // 21 ~ 100
					// 실패 시 -1 스테이지로 보낸다.
					backToStage = this.coreData.datas.stages.CurrentStage_Main.GetValue() - 1;
					backToStage = utilBasic.clamp(backToStage, GameBalanceConfig.end_stage_tutorial + 1, GameBalanceConfig.end_stage_floor1);
					break;
				case EStage.stage_main: // 101 ~ 1000
					backToStage = this.coreData.datas.stages.CurrentStage_Main.GetValue() - 1;
					backToStage = utilBasic.clamp(backToStage, GameBalanceConfig.end_stage_floor1 + 1, GameBalanceConfig.end_stage_main);
					break;
				case EStage.stage_main_endless: // 1001+
					// endless에서 실패 시 처리 (예: -1 또는 유지)
					backToStage = this.coreData.datas.stages.CurrentStage_Main.GetValue() - 1;
					// 1001 아래로 내려가지 않도록
					backToStage = Math.max(backToStage, GameBalanceConfig.start_stage_endless);
					break;
			}

			this.coreData.datas.stages.CurrentStage_Main = ModValueInt.create(backToStage);

			// 해쉬 갱신용
			this.createStageMainPRNG(stage, stageMainHash);
		}

		return resultSuccess;
	}
}

export class CStage_QuickClear extends CStageBase {
	constructor(init?: Partial<CStage_QuickClear>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		//this.addToCreateCoreData(this.coreData.datas.equipment, CoreDataKey.COREDATAKEY_EQUIPMENT);
	}

	protected getValidationSchema(): ValidationSchema {
		// 소탕하기는 도전 스테이지(Rift, Corruption, Bossrush 등) 전용 — stageMainHash 불필요
		return {
			stage: {
				type: "enum",
				enumObject: EStage,
			},
			stage_level: {
				type: "number",
				min: 0, // 0 값 실패 테스트를 위해 허용
				max: 100000,
			},
			count: {
				type: "number",
				min: 1,
				max: 100,
			},
			// 도전 스테이지에서 동봉되는 매핑 프리셋 id (랭킹 빌드 식별/로그용)
			preset: {
				type: "enum",
				enumObject: EPreset,
				required: false,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const stage: EStage = data.stage;
		const stage_level = data.stage_level;
		const count = data.count;

		return await this.executeStageQuickClear(stage, stage_level, count);
	}

	// 소탕하기
	// 최적화: 배열 병합 O(n²) -> O(n) (flat() 사용)
	async executeStageQuickClear(stage: EStage, stage_level: number, count: number): Promise<[EResponseCode, string]> {
		if (!this.isNoStageLevelNeeded(stage)) {
			this.verify_StageLevel(stage, stage_level);
		}

		this.verify_CanQuickClear(stage, stage_level);

		this.verify_EnterTicket(stage, count);

		this.update_ClearCount(stage, count);
		this.update_EnterTicker(stage, count);

		// 기록된 최고 기록을 사용
		const bestTotalKill = this.coreData.datas.stages.BestTotalKills.get(stage).GetValue();
		const bestTotalDamage = this.coreData.datas.stages.BestTotalDamages.get(stage).GetValue();

		// PRNG 생성 (도전 스테이지이므로 hash 불필요 — createStageMainPRNG 내부에서 isMainStage 분기)
		const PRNG: IPRNG = this.createStageMainPRNG(stage);

		// 최적화: 배열 사전 할당 + flat() 사용 (push(...) 스프레드 연산 O(n²) 방지)
		const rewardLists: GachaRewardParameters[][] = new Array(count);
		for (let i = 0; i < count; i++) {
			rewardLists[i] = this.createStageRewards(PRNG, stage, stage_level, bestTotalKill, bestTotalDamage);
		}
		const allRewardList = rewardLists.flat();

		// 보상 지급 처리. 각 CoreData에 적용
		await this.applyRewardDataToCoreData(allRewardList);
		const stageName = utilBasic.getKeyName(EStage, stage);
		await PRNG.writeRandomLogsToFile?.(stageName + "_" + stage_level);

		return resultSuccess;
	}
}

// -----------------------------------------------
// Function
// -----------------------------------------------
export function stage_CreateCoreData(DBDatas: GameDBDatas) {
	const coreData_Stage = new CoreData_Stages();

	DBDatas.stageMap.MapData.forEach((value, key) => {
		switch (key) {
			case EStage.stage_main_tutorial:
			case EStage.stage_main_act1:
			case EStage.stage_main:
				coreData_Stage.BestStages.set(key, ModValueInt.create(1));
				break;
			case EStage.stage_main_endless:
				coreData_Stage.BestStages.set(key, ModValueInt.create(0)); // 초기값 0
				break;
			default:
				coreData_Stage.BestStages.set(key, ModValueInt.create(0));
				break;
		}

		coreData_Stage.BestTotalDamages.set(key, ModValueInt.create(0));
		coreData_Stage.BestTotalKills.set(key, ModValueInt.create(0));
		coreData_Stage.BestClearTimes.set(key, ModValueInt.create(0));

		coreData_Stage.StageClearCounts.set(key, ModValueInt.create(0));
		coreData_Stage.StageFailCounts.set(key, ModValueInt.create(0));
	});

	coreData_Stage.CurrentStage_Main = ModValueInt.create(1);

	return coreData_Stage;
}
