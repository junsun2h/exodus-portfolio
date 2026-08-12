import { CallableRequest } from "firebase-functions/v2/https";
import { EResponseCode, resultSuccess } from "../../Data/Types/ResponseCodes";
import { CoreDataKey } from "../../Data/Constants/FirestorePaths";
import { ECurrency, ECurrencyTicket, EProduct, EGachaProduct, EProductPaymentType, EProductPackage, EProductPass, EProductResetType, EProductSubscription, ELogEvent, EProductType, ELogErrorCode, EMailboxFrom, EGacha, EProductPassType, EAPIErrorCode, EAchieveItem, EVipLevel } from "../../Data/Generated/CommonEnum";
import { CRequestBase } from "../Base/RequestBase";
import { GameDBDatas, GameDBFactory } from "../Factory/GameDBFactory";
import { CoreData_MailboxItem, CoreData_PurchaseInfo, CoreData_PurchaseInfo_Product, CoreData_PurchaseInfo_Subscription, CoreData_Products, CoreData_PurchaseStatistics, CoreData_PurchaseInfo_Pass, CoreData_PurchaseInfo_Package, CoreData_Currencies } from "../../Data/Generated/CoreData";
import { CoreDataLatestVersion } from "../Migration/CoreDataMigration";
import { ModValueBool, ModValueDouble, ModValueInt } from "../../Data/Shared/Types/ModValueTypes";
import { PXBigInt } from "../../Data/Shared/Types/BigIntTypes";
import * as utilBasic from "../../Utility/UtilityBasic";
import { plainToInstance } from "class-transformer";
import { GameDB_Server_Product, GameDB_Server_ProductMap, GameDB_Server_ProductPackage, GameDB_Server_ProductPackageMap, GameDB_Server_ProductPass, GameDB_Server_ProductPass_FreeItem, GameDB_Server_ProductPass_PaidItem, GameDB_Server_ProductPassMap, GameDB_Server_ProductSubscription, GameDB_Server_ProductSubscriptionMap } from "../../Data/Generated/GameDBData";
import * as GameBalanceConfig from "../../Data/Shared/Config/GameBalanceConfig";
import * as ScheduleConfig from "../../Data/Config/ScheduleConfig";
import * as DemoConfig from "../../Data/Config/DemoConfig";
import { LogEventData_MailboxItemDeliveryFailure, LogEventData_MailboxItemDeliverySuccess, LogEventData_PurchaseCancel, LogEventData_PurchaseFailed, LogEventData_PurchaseRequest, LogEventData_PurchaseSuccess } from "../LogEvent/LogEvent";
import * as uuid from "uuid";
import { CMailbox_CheckAndDeliverSubscriptionProduct } from "./Mailbox";
import { currency_Add, currency_Payment, isBigIntCurrency } from "./Currency";
import { CEquipment_GachaWithParameters } from "./Equipment";
import { CSkill_GachaWithParameters } from "./Skill";
import { CPet_GachaWithParameters } from "./Pet";
import { CAchieveItem_IsComplete } from "./AchieveItem";
import { getFirestore } from "firebase-admin/firestore";
import { throwLogicError } from "../../Utility/UtilityBasic";
import { logDebug } from "../../Utility/UtilityLogging";
import { ValidationSchema } from "../../Utility/InputValidation";
import { resolvePRNG } from "../../Data/Types/PRNGServer";
import { ServerSelector } from "../Factory/ServerSelector";
import { addOrUpdateTicket } from "../../Data/Types/TicketTypes";
import { GachaRewardParameters } from "../../Data/Types/GachaServerTypes";

// -----------------------------------------------
// Request
// -----------------------------------------------
export async function request_Product_Buy(request: CallableRequest<any>) {
	return await new CProduct_Product_Buy().handleRequest(request.data, request);
}

export async function request_ProductPackage_Buy(request: CallableRequest<any>) {
	return await new CProduct_ProductPackage_Buy().handleRequest(request.data, request);
}

export async function request_ProductSubscription_Buy(request: CallableRequest<any>) {
	return await new CProduct_ProductSubscription_Buy().handleRequest(request.data, request);
}

export async function request_ProductPass_Buy(request: CallableRequest<any>) {
	return await new CProduct_ProductPass_Buy().handleRequest(request.data, request);
}

// 무료 아이템 전체 받기
export async function request_ProductPass_ReceiveAll_FreeItem(request: CallableRequest<any>) {
	return await new CProduct_ProductPass_ReceiveAll_FreeItem().handleRequest(request.data, request);
}

// 유료 아이템 개별 받기
export async function request_ProductPass_Receive_PaidItem(request: CallableRequest<any>) {
	return await new CProduct_ProductPass_Receive_PaidItem().handleRequest(request.data, request);
}

// 무료, 유료 아이템 전체 받기
export async function request_ProductPass_ReceiveAll(request: CallableRequest<any>) {
	return await new CProduct_ProductPass_ReceiveAll().handleRequest(request.data, request);
}

// 패스 상품 리셋(출석 체크)
export async function request_ProductPass_Reset(request: CallableRequest<any>) {
	return await new CProduct_ProductPass_Reset().handleRequest(request.data, request);
}

// -----------------------------------------------
// Class
// -----------------------------------------------
export enum PurchaseProductType {
	EProduct = "EProduct",
	EProductPass = "EProductPass",
	EProductSubscription = "EProductSubscription",
	EProductPackage = "EProductPackage",
}

export class CProductBase extends CRequestBase {
	constructor(init?: Partial<CProductBase>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.account, CoreDataKey.COREDATAKEY_ACCOUNT);
		this.addToCreateCoreData(this.coreData.datas.products, CoreDataKey.COREDATAKEY_PRODUCT);
		this.addToCreateCoreData(this.coreData.datas.currencies, CoreDataKey.COREDATAKEY_CURRENCY);
		this.addToCreateCoreData(this.coreData.datas.mailboxes, CoreDataKey.COREDATAKEY_MAILBOX);
		this.addToCreateCoreData(this.coreData.datas.tickets, CoreDataKey.COREDATAKEY_TICKET);
	}

	// 구매 가능 여부 확인 및 필요 시 구매 횟수 리셋
	async checkAndResetPurchaseAvailability(product: any, purchaseProductType: PurchaseProductType) {
		// VIP 등급 검증 (모든 상품 공통)
		const productDBData = this.getGameDBProductData(product, purchaseProductType);
		if (productDBData.VipLevel !== EVipLevel.None) {
			if (this.coreData.datas.account.VipLevel < productDBData.VipLevel) {
				throwLogicError(
					`checkAndResetPurchaseAvailability(): VIP 등급 부족. 필요: ${productDBData.VipLevel}, 현재: ${this.coreData.datas.account.VipLevel}`,
					EAPIErrorCode.ITEM_NOT_AVAILABLE
				);
			}
		}

		// 데이터 수집
		switch (purchaseProductType) {
			case PurchaseProductType.EProduct:
				{
					const purchaseInfo: CoreData_PurchaseInfo_Product = this.coreData.datas.products.Product.get(product);
					const productDBInfo: GameDB_Server_Product = this.gameDB.datas.productMap.MapData.get(product);

					const nowUtc = new Date();
					const nextResetTime = this.getNextResetTime(productDBInfo.ResetType, nowUtc);

					// 한정 구매가 가능한 상품의 구매 기한 확인
					if (productDBInfo.LimitDate && nowUtc > new Date(productDBInfo.LimitDate)) {
						// 구매 기한이 지났으므로 구매할 수 없음
						throwLogicError(`checkAndResetPurchaseAvailability(): The purchase limit date(${productDBInfo.LimitDate}) has passed.`, EAPIErrorCode.ITEM_NOT_AVAILABLE);
					}
					// 리셋 시간이 지났는지 확인하여 구매 횟수 리셋
					if (nowUtc >= nextResetTime) {
						purchaseInfo.PurchasedCount.Set(0);
					}

					// 구매 횟수 제한을 확인
					if (productDBInfo.ResetType !== EProductResetType.unlimited && purchaseInfo.PurchasedCount.GetValue() >= productDBInfo.LimitCount) {
						// 구매 횟수 제한을 초과하였으므로 구매할 수 없음
						throwLogicError(`checkAndResetPurchaseAvailability(): Purchased count(${purchaseInfo.PurchasedCount.GetValue()}) has exceeded the limit count ${productDBInfo.LimitCount}.`, EAPIErrorCode.DATABASE_ERROR);
					}
				}
				break;
			case PurchaseProductType.EProductPackage:
				{
					const purchaseInfo: CoreData_PurchaseInfo_Package = this.coreData.datas.products.ProductPackage.get(product);
					const productDBInfo: GameDB_Server_ProductPackage = this.gameDB.datas.productPackageMap.MapData.get(product);

					const nowUtc = new Date();
					const nextResetTime = this.getNextResetTime(productDBInfo.ResetType, nowUtc);

					// 한정 구매가 가능한 상품의 구매 기한 확인
					if (productDBInfo.LimitDate && nowUtc > new Date(productDBInfo.LimitDate)) {
						// 구매 기한이 지났으므로 구매할 수 없음
						throwLogicError(`checkAndResetPurchaseAvailability(): The purchase limit date(${productDBInfo.LimitDate}) has passed.`, EAPIErrorCode.ITEM_NOT_AVAILABLE);
					}
					// 리셋 시간이 지났는지 확인하여 구매 횟수 리셋
					if (nowUtc >= nextResetTime) {
						purchaseInfo.PurchasedCount.Set(0);
					}

					// 구매 횟수 제한을 확인
					if (productDBInfo.ResetType !== EProductResetType.unlimited && purchaseInfo.PurchasedCount.GetValue() >= productDBInfo.LimitCount) {
						// 구매 횟수 제한을 초과하였으므로 구매할 수 없음
						throwLogicError(`checkAndResetPurchaseAvailability(): Purchased count(${purchaseInfo.PurchasedCount.GetValue()}) has exceeded the limit count ${productDBInfo.LimitCount}.`, EAPIErrorCode.ITEM_NOT_AVAILABLE);
					}

					// TriggerAchieveItem 조건 검증 (None이 아닌 경우)
					if (productDBInfo.TriggerAchieveItem !== EAchieveItem.None) {
						await this.createInternalRequest(CAchieveItem_IsComplete).executeIsComplete(
							productDBInfo.TriggerAchieveItem,
							productDBInfo.TriggerValue
						);
					}
				}
				break;
			case PurchaseProductType.EProductPass:
				{
					// 패스 상품은 재구매하는 상품이 없다.
					// 출석 체크는 유료 재화 구매 없이 광고로 받는다.
					// 다른 패스는 다음 스텝으로 넘어가는 방식
					const productDBInfo: GameDB_Server_ProductPass = this.gameDB.datas.productPassMap.MapData.get(product);

					const nowUtc = new Date();
					// 구매가 가능한 상품의 구매 기한 확인
					if (productDBInfo.EndDate && nowUtc > new Date(productDBInfo.EndDate)) {
						// 구매 기한이 지났으므로 구매할 수 없음
						throwLogicError(`checkAndResetPurchaseAvailability(): The purchase end date(${productDBInfo.EndDate}) has passed.`, EAPIErrorCode.ITEM_NOT_AVAILABLE);
					}
				}
				break;
			case PurchaseProductType.EProductSubscription:
				{
					// 구독 상품의 PurchasedCount 리셋은 구독 기간이 끝난 후에 이루어짐
					// mailbox_CheckAndDeliverSubscriptionProduct()
					const purchaseInfo: CoreData_PurchaseInfo_Subscription = this.coreData.datas.products.ProductSubscription.get(product);
					const productDBInfo: GameDB_Server_ProductSubscription = this.gameDB.datas.productSubscriptionMap.MapData.get(product);

					// 이미 구독중인지 확인
					if (purchaseInfo.PurchasedCount.GetValue() > 0) {
						throwLogicError(`checkAndResetPurchaseAvailability(): The product is already Subscribing.`, EAPIErrorCode.ITEM_NOT_AVAILABLE);
					}

					// 구매가 가능한 상품의 구매 기한 확인
					const nowUtc = new Date();
					if (productDBInfo.LimitDate && nowUtc > new Date(productDBInfo.LimitDate)) {
						// 구매 기한이 지났으므로 구매할 수 없음
						throwLogicError(`checkAndResetPurchaseAvailability(): The purchase limit date(${productDBInfo.LimitDate}) has passed.`, EAPIErrorCode.ITEM_NOT_AVAILABLE);
					}
				}
				break;

			default:
				throwLogicError(`throwLogicError(): Invalid product type: ${purchaseProductType}`, EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	getNextResetTime(limitType: EProductResetType, currentTime: Date): Date {
		let resetTime: Date;
		switch (limitType) {
			case EProductResetType.daily:
				// 다음 날 자정 UTC 시간을 리셋 시간으로 설정
				resetTime = new Date(Date.UTC(currentTime.getUTCFullYear(), currentTime.getUTCMonth(), currentTime.getUTCDate() + 1, 0, 0));
				break;
			case EProductResetType.weekly: {
				// 다음 주 월요일 자정 UTC 시간을 리셋 시간으로 설정
				const daysUntilNextMonday = (1 - currentTime.getUTCDay() + 7) % 7;
				resetTime = new Date(Date.UTC(currentTime.getUTCFullYear(), currentTime.getUTCMonth(), currentTime.getUTCDate() + daysUntilNextMonday, 0, 0));
				break;
			}
			case EProductResetType.monthly:
				// 다음 달 1일 자정 UTC 시간을 리셋 시간으로 설정
				resetTime = new Date(Date.UTC(currentTime.getUTCFullYear(), currentTime.getUTCMonth() + 1, 1, 0, 0));
				break;
			default:
				// 무제한, 계정당 의 경우 현재 시간을 반환하여 항상 구매 가능하도록 함
				resetTime = currentTime;
		}
		return resetTime;
	}

	verifyProduct(product: number, productType: PurchaseProductType) {
		if (product === undefined) {
			throwLogicError(`verify_Product(): product is undefined`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 상품 유형에 따라 참조할 맵을 결정
		let productMap;
		switch (productType) {
			case PurchaseProductType.EProduct:
				productMap = this.gameDB.datas.productMap;
				break;
			case PurchaseProductType.EProductPass:
				productMap = this.gameDB.datas.productPassMap;
				break;
			case PurchaseProductType.EProductSubscription:
				productMap = this.gameDB.datas.productSubscriptionMap;
				break;
			case PurchaseProductType.EProductPackage:
				productMap = this.gameDB.datas.productPackageMap;
				break;
			default:
				throwLogicError(`verify_Product(): Invalid product type`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		if (!productMap.MapData.has(product)) {
			throwLogicError(`verify_Product(): ${product} is not part of the product list`, EAPIErrorCode.ITEM_NOT_AVAILABLE);
		}
	}

	// 상품 구매 과정을 처리하는 함수
	async executeProcessPurchase(product: number, productType: PurchaseProductType, count: number, transactionID: string, receipt: string): Promise<[EResponseCode, string]> {
		// 1. 상품 유효성 검증 (결제 수단을 먼저 확인해야 하므로 입력값 검사보다 앞에 위치)
		this.verifyProduct(product, productType);

		// 데모 빌드: 현금(cache) 결제 상품은 스토어 결제 없이 즉시 구매 처리
		const isInstantCachePurchase = this.isInstantCachePurchase(product, productType);
		const safeTransactionID = transactionID ?? "";
		const safeReceipt = receipt ?? "";

		// 2. 입력값 검사 (즉시 구매는 스토어 영수증이 없으므로 생략)
		if (isInstantCachePurchase) {
			logDebug(`[PRODUCT_PURCHASE] 데모 모드 즉시 구매 - 영수증 검증 생략. productType: ${productType}, product: ${product}`);
		} else {
			if (!safeTransactionID || safeTransactionID.trim() === "") {
				throwLogicError(`executeProcessPurchase(): transactionID가 비어있습니다.`, EAPIErrorCode.INVALID_PARAMETERS);
			}
			if (!safeReceipt || safeReceipt.trim() === "") {
				throwLogicError(`executeProcessPurchase(): receipt가 비어있습니다.`, EAPIErrorCode.INVALID_PARAMETERS);
			}
		}

		// 3. 상품 구매 가능 여부 확인 및 리셋
		await this.checkAndResetPurchaseAvailability(product, productType);

		// LOG: 상품 결제 요청(시작) 로그 이벤트
		this.logEvent_PurchaseRequest(product, productType, count, safeTransactionID, safeReceipt);

		// 4. 결제 재화 확인 및 차감 처리(Currency 일 때)
		await this.handlePayment(product, productType, safeTransactionID, count);

		// 5. 구글 영수증 인증 (데모 즉시 구매는 검증 대상 영수증이 없으므로 생략)
		if (!isInstantCachePurchase) {
			this.verifyReceiptWithGoogle(product, productType, safeTransactionID, safeReceipt);
		}

		// 6. purchaseInfo 업데이트
		this.initPurchaseInfo(product, productType, count);

		// 7. 결제 통계 업데이트
		this.updatePurchaseStatistics(product, productType, count);

		// 8. 상품 지급 처리(우편함, 즉시 지급)
		await this.processProductReward(product, productType, count);

		// LOG: 구매 성공 로그
		this.logEvent_PurchaseSuccess(product, productType);

		return resultSuccess;
	}

	// 스토어 영수증 관련 입력 스키마
	// 데모 빌드에서는 스토어 결제를 거치지 않으므로 필수 인자에서 제외한다
	protected getStoreReceiptValidationSchema(): ValidationSchema {
		return {
			transactionID: {
				type: "string",
				maxLength: 500,
				required: !DemoConfig.instantCachePurchase,
			},
			receipt: {
				type: "string",
				maxLength: 10000,
				required: !DemoConfig.instantCachePurchase,
			},
		};
	}

	// 데모 빌드에서 스토어 결제 없이 즉시 구매 처리할 상품인지 확인
	// 현금(cache) 결제 상품에만 적용되며, 재화(currency) 결제 상품은 정상적으로 재화를 차감한다
	protected isInstantCachePurchase(product: number, productType: PurchaseProductType): boolean {
		if (!DemoConfig.instantCachePurchase) {
			return false;
		}

		const [paymentMethod] = this.getPaymentCost(product, productType);
		return paymentMethod === EProductPaymentType.cache;
	}

	private async handlePayment(product: number, productType: PurchaseProductType, transactionID: string, count: number) {
		const [paymentMethod, paymentCurrency, paymentCost] = this.getPaymentCost(product, productType);

		if (paymentMethod === EProductPaymentType.currency) {
			const totalCost = paymentCost * count;
			currency_Payment(this.coreData.datas.currencies, paymentCurrency, totalCost);
		}

		if (paymentMethod === EProductPaymentType.cache) {
			// 데모 빌드: 스토어 트랜잭션이 존재하지 않으므로 중복 결제 검사를 생략하고 즉시 지급
			if (DemoConfig.instantCachePurchase) {
				logDebug(`[PRODUCT_PURCHASE] 데모 모드 즉시 구매 - 트랜잭션 중복 검사 생략. productType: ${productType}, product: ${product}`);
				return;
			}

			await this.checkAndSaveTransactionID(product, productType, transactionID);
		}
	}

	private async processProductReward(product: number, productType: PurchaseProductType, count: number) {
		const gameDBProductData = this.getGameDBProductData(product, productType);
		if (!gameDBProductData) {
			throwLogicError(`processProductReward(): Invalid product gameDB data`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		for (let i = 0; i < count; i++) {
			if (gameDBProductData.ToMailbox) {
				// 1. 우편함으로
				return await this.createMailItemFromPurchasedProduct(product, productType);
			}
			// 2. 즉시 지급
			else {
				// 보너스(마일리지)
				if (gameDBProductData.BonusCurrency !== ECurrency.None) {
					currency_Add(this.coreData.datas.currencies, gameDBProductData.BonusCurrency, gameDBProductData.BonusCount);
				}

				// 실제 아이템(또는 패키지, 구독 상품) 지급
				switch (productType) {
					case PurchaseProductType.EProduct: {
						const productData = this.gameDB.datas.productMap.MapData.get(product as EProduct);
						await this.processRewardItems([productData]);
						break;
					}
					case PurchaseProductType.EProductPackage: {
						const packageData = this.gameDB.datas.productPackageMap.MapData.get(product as EProductPackage);
						await this.processRewardItems(packageData.Items);
						break;
					}
					case PurchaseProductType.EProductSubscription: {
						const subscriptionData = this.gameDB.datas.productSubscriptionMap.MapData.get(product as EProductSubscription);
						await this.processRewardItems(subscriptionData.Items);
						break;
					}
					case PurchaseProductType.EProductPass:
						break;
					default:
						throwLogicError(`processProductReward(): Invalid productType: ${productType}`, EAPIErrorCode.INVALID_PARAMETERS);
				}
			}
		}
	}

	private async processRewardItems(items: any[]) {
		for (const item of items) {
			if (item.Currency === ECurrency.None) {
				throwLogicError("processGachaRewardItems(): Item currency cannot be None", EAPIErrorCode.INVALID_PARAMETERS);
			}
			await this.processRewardItem(item);
		}
	}

	async processRewardItem(item: any) {
		const gachaItem = new GachaRewardParameters({
			PRNG: resolvePRNG(undefined),
			Currency: item.Currency,
			Count: ModValueInt.create(item.Count),
			Tier: item.Tier,
			Grade: item.Grade,
			BigIntCount: new PXBigInt(),
		});

		switch (item.Currency) {
			case ECurrency.currency_equipment_weapon:
			case ECurrency.currency_equipment_armour:
			case ECurrency.currency_equipment_accessory:
				await this.requestGachaReward(gachaItem, CEquipment_GachaWithParameters);
				break;
			case ECurrency.currency_skill_spell:
			case ECurrency.currency_skill_aura:
			case ECurrency.currency_skill_rune:
				await this.requestGachaReward(gachaItem, CSkill_GachaWithParameters);
				break;
			case ECurrency.currency_pet:
				await this.requestGachaReward(gachaItem, CPet_GachaWithParameters);
				break;
			case ECurrency.currency_ticket_gacha:
			case ECurrency.currency_ticket_pick:
			case ECurrency.currency_ticket_fix: {
				// ProductTicket이 설정된 경우에만 특정 티켓으로 처리 (상품 전용)
				// 패스 아이템(Ticket 필드)은 ProductTicket이 없으므로 일반 통화로 처리
				if (item.ProductTicket !== undefined && item.ProductTicket !== ECurrencyTicket.None) {
					addOrUpdateTicket(this.coreData.datas.tickets, item.ProductTicket as ECurrencyTicket, item.Count);
				} else {
					currency_Add(this.coreData.datas.currencies, item.Currency, item.Count, item.Tier);
				}
				break;
			}
			default:
				currency_Add(this.coreData.datas.currencies, item.Currency, item.Count, item.Tier);
				break;
		}
	}

	async requestGachaReward(gachaItem: GachaRewardParameters, GachaClass: any) {
		await this.createInternalRequest(GachaClass).processRequest({ reward: gachaItem }, {}, this.resultData);
	}

	// 상품 정보를 가져오는 함수
	getPurchaseInfo(product: number, purchaseProductType: PurchaseProductType): CoreData_PurchaseInfo {
		let purchaseInfoMap;
		switch (purchaseProductType) {
			case PurchaseProductType.EProduct:
				purchaseInfoMap = this.coreData.datas.products.Product;
				break;
			case PurchaseProductType.EProductPass:
				purchaseInfoMap = this.coreData.datas.products.ProductPass;
				break;
			case PurchaseProductType.EProductSubscription:
				purchaseInfoMap = this.coreData.datas.products.ProductSubscription;
				break;
			case PurchaseProductType.EProductPackage:
				purchaseInfoMap = this.coreData.datas.products.ProductPackage;
				break;
			default:
				throwLogicError(`getPurchaseInfo(): Invalid product type: ${purchaseProductType}`, EAPIErrorCode.INVALID_PARAMETERS);
		}
		return purchaseInfoMap.get(product);
	}

	// 중복 결제 방지를 위한 트랜잭션 ID 확인 및 저장 함수
	async checkAndSaveTransactionID(product: number, productType: PurchaseProductType, transactionID: string) {
		const serverSelector = ServerSelector.getInstance();
		const serverPath = await serverSelector.getGameDataPath(this.serverID);
		const transactionIDPath = `${serverPath}/Users/UUID/${this.UUID}/TransactionID/${transactionID}`;
		const transactionIDRef = getFirestore().doc(transactionIDPath);

		const doc = await transactionIDRef.get();
		if (doc.exists) {
			// 이미 존재하는 트랜잭션 ID이므로 중복 결제로 간주
			this.logEvent_PurchaseFailed(product, productType, ELogErrorCode.INVALID_GOOGLE_RECEIPT);
			throwLogicError(`checkAndSaveTransactionID(): 중복된 트랜잭션 ID(${transactionID})입니다.`, EAPIErrorCode.DATABASE_ERROR);
		} else {
			// 트랜잭션 ID가 존재하지 않으므로 저장
			const currentDate = new Date();
			const expireAt = new Date();
			expireAt.setDate(currentDate.getDate() + ScheduleConfig.transactionID_RetentionDays); // 보관 기간 설정

			await transactionIDRef.set({
				transaction_id: transactionID,
				createdAt: currentDate,
				expireAt: expireAt, // TTL 설정을 위한 필드
			});
		}
	}

	verifyReceiptWithGoogle(product: number, productType: PurchaseProductType, transactionID: string, receipt: string) {
		// TODO: Google 영수증 검증 구현
		// 구현 시 아래 코드 활성화:
		// this.logEvent_PurchaseFailed(product, productType, ELogErrorCode.INVALID_GOOGLE_RECEIPT);
		// throwLogicError(`executeProcessPurchase(): Invalid Google receipt.`, EAPIErrorCode.INVALID_PARAMETERS);
	}

	private initPurchaseInfo(product: number, productType: PurchaseProductType, count: number) {
		switch (productType) {
			case PurchaseProductType.EProduct:
				{
					const info = this.getPurchaseInfo(product, productType) as CoreData_PurchaseInfo_Product;
					info.Product = product as EProduct;
					info.PurchasedCount.Add(count);
				}
				break;
			case PurchaseProductType.EProductPackage:
				{
					const info = this.getPurchaseInfo(product, productType) as CoreData_PurchaseInfo_Package;
					info.Product = product as EProductPackage;
					info.PurchasedCount.Add(count);
				}
				break;
			case PurchaseProductType.EProductPass:
				{
					const info = this.getPurchaseInfo(product, productType) as CoreData_PurchaseInfo_Pass;
					info.Product = product as EProductPass;
					info.PurchasedCount.Add(count);
					info.CurrentStep = ModValueInt.create(0);
					info.IsPurchased = ModValueBool.create(true);
					info.LastReceivedFreeItemIndex = ModValueInt.create(-1);
					info.ReceivedPaidItemIndexes = new Array<ModValueInt>();
					info.ReceivedFreeRewardCount = ModValueInt.create(0);
					info.ReceivedPaidRewardCount = ModValueInt.create(0);
				}
				break;
			case PurchaseProductType.EProductSubscription:
				{
					const purchaseInfo = this.getPurchaseInfo(product, productType) as CoreData_PurchaseInfo_Subscription;
					purchaseInfo.Product = product as EProductSubscription;
					purchaseInfo.PurchasedCount.Add(count);
					purchaseInfo.ReceivedRewardDays = ModValueInt.create(0);

					const totalRewardDays = this.gameDB.datas.productSubscriptionMap.MapData.get(purchaseInfo.Product).TotalRewardDays;
					const currentDate = new Date();

					// 오늘을 포함한 종료 날짜
					const subEndDate = new Date(currentDate);
					subEndDate.setDate(currentDate.getDate() + totalRewardDays - 1);
					purchaseInfo.SubscriptionStartDate = currentDate.toISOString();
					purchaseInfo.SubscriptionEndDate = subEndDate.toISOString();
					purchaseInfo.IsSubscribing = ModValueBool.create(true);

					/* 
					(날짜 예시)
					TotalRewardDays가 3일, new Date()가 '2023-10-01T00:00:00Z' 일 때
					SubscriptionStartDate 는 2023-10-01T00:00:00Z"
					SubscriptionEndDate 는 2023-10-03T00:00:00Z"
					*/
				}
				break;
			default:
				throwLogicError(`initPurchaseInfo(): Invalid product type: ${productType}`, EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	private updatePurchaseStatistics(product: number, productType: PurchaseProductType, count: number): void {
		const [PaymentMethod, PaymentCurrency, paymentCost] = this.getPaymentCost(product, productType);
		this.coreData.datas.products.PurchaseStatistics.TotalPurchaseCount.Add(count);

		// VIP 등급 산정용 누적 금액: 실결제(cache)만 집계
		if (PaymentMethod === EProductPaymentType.cache) {
			this.coreData.datas.products.PurchaseStatistics.TotalPurchaseAmount.Add(paymentCost);

			// VIP 등급 갱신. 누적 결제액 기준이므로 등급은 단조 증가해야 한다 — 계산값이 더 낮아도 강등하지 않는다.
			// GameDB 의 VipLevel 테이블이 비어 있으면 getCurrentVipLevel() 이 None 을 돌려주는데,
			// 그대로 대입하면 현금 결제 한 번에 기존 등급이 날아가고 VIP 를 요구하는 상품이 상점에서 전부 사라진다.
			const newVipLevel = this.getCurrentVipLevel();
			if (newVipLevel > this.coreData.datas.account.VipLevel) {
				this.coreData.datas.account.VipLevel = newVipLevel;
			}
		}

		if (this.coreData.datas.products.PurchaseStatistics.FirstPurchaseDate === "") this.coreData.datas.products.PurchaseStatistics.FirstPurchaseDate = new Date().toISOString();
		this.coreData.datas.products.PurchaseStatistics.LatestPurchaseDate = new Date().toISOString();
	}

	// 구매 요청 로깅
	logEvent_PurchaseRequest(product: number, productType: PurchaseProductType, count: number, transactionID: string, receipt: string) {
		const gameDBProduct = this.getGameDBProductData(product, productType);
		const logEventData: LogEventData_PurchaseRequest = {
			purchase_item_id: gameDBProduct.StringKeyName, // TODO: 구글 상품 ID
			purchase_item_name: gameDBProduct.StringKeyName,
			purchase_item_category: utilBasic.getKeyName(EProductType, gameDBProduct.ProductType),
			purchase_count: count,
			payment_type: utilBasic.getKeyName(EProductPaymentType, gameDBProduct.PaymentMethod),
			payment_amount: gameDBProduct.PaymentCost,
			google_transaction_id: transactionID,
			google_receipt: receipt,
		};
		this.logEvent.logEventAction(ELogEvent.PurchaseRequest, this.UUID, logEventData, this.coreData.datas);
	}

	// 구매 성공 로깅
	logEvent_PurchaseSuccess(product: number, productType: PurchaseProductType) {
		const gameDBProduct = this.getGameDBProductData(product, productType);
		const logEventData: LogEventData_PurchaseSuccess = this.createCommonProductLogEventData(gameDBProduct);
		this.logEvent.logEventAction(ELogEvent.PurchaseSuccess, this.UUID, logEventData, this.coreData.datas);
	}

	// 구매 취소
	logEvent_PurchaseCancel(product: number, productType: PurchaseProductType) {
		const gameDBProduct = this.getGameDBProductData(product, productType);
		const logEventData = this.createCommonProductLogEventData(gameDBProduct);
		this.logEvent.logEventAction(ELogEvent.PurchaseCancel, this.UUID, logEventData, this.coreData.datas);
	}

	// 구매 실패
	logEvent_PurchaseFailed(product: number, productType: PurchaseProductType, errorCode: ELogErrorCode) {
		const gameDBProduct = this.getGameDBProductData(product, productType);
		const logEventData: LogEventData_PurchaseFailed = {
			purchase_item_id: gameDBProduct.StringKeyName, // TODO: 구글 상품 ID
			purchase_item_name: gameDBProduct.StringKeyName,
			purchase_item_category: utilBasic.getKeyName(EProductType, gameDBProduct.ProductType),
			log_errorcode: utilBasic.getKeyName(ELogErrorCode, errorCode),
		};
		this.logEvent.logEventAction(ELogEvent.PurchaseFailed, this.UUID, logEventData, this.coreData.datas);
	}

	createCommonProductLogEventData(gameDB: GameDB_Server_Product | GameDB_Server_ProductPackage | GameDB_Server_ProductPass | GameDB_Server_ProductSubscription) {
		return {
			purchase_item_id: gameDB.StringKeyName, // TODO: 구글 상품 ID
			purchase_item_name: gameDB.StringKeyName,
			purchase_item_category: utilBasic.getKeyName(EProductType, gameDB.ProductType),
		};
	}

	// 우편함 상품 지급 성공
	logEvent_MailboxItemDeliverySuccess(product: number, productType: PurchaseProductType, mailboxItem: CoreData_MailboxItem) {
		const gameDBProduct = this.getGameDBProductData(product, productType);
		const logEventData: LogEventData_MailboxItemDeliverySuccess = {
			mailbox_item_id: mailboxItem.UID,
			purchase_item_id: gameDBProduct.StringKeyName, // TODO: 구글 상품 ID
			purchase_item_name: gameDBProduct.StringKeyName,
		};
		this.logEvent.logEventAction(ELogEvent.MailboxItemDeliverySuccess, this.UUID, logEventData, this.coreData.datas);
	}

	// 우편함 상품 지급 실패
	// 이런 케이스가 있나?
	logEvent_MailboxItemDeliveryFailure(product: number, productType: PurchaseProductType, errorCode: ELogErrorCode) {
		const gameDBProduct = this.getGameDBProductData(product, productType);
		const logEventData: LogEventData_MailboxItemDeliveryFailure = {
			purchase_item_id: gameDBProduct.StringKeyName, // TODO: 구글 상품 ID
			purchase_item_name: gameDBProduct.StringKeyName,
			log_errorcode: utilBasic.getKeyName(ELogErrorCode, errorCode),
		};
		this.logEvent.logEventAction(ELogEvent.MailboxItemDeliveryFailure, this.UUID, logEventData, this.coreData.datas);
	}

	getGameDBProductData(product: number, productType: PurchaseProductType) {
		switch (productType) {
			case PurchaseProductType.EProduct:
				return this.gameDB.datas.productMap.MapData.get(product as EProduct);
			case PurchaseProductType.EProductPass:
				return this.gameDB.datas.productPassMap.MapData.get(product as EProductPass);
			case PurchaseProductType.EProductSubscription:
				return this.gameDB.datas.productSubscriptionMap.MapData.get(product as EProductSubscription);
			case PurchaseProductType.EProductPackage:
				return this.gameDB.datas.productPackageMap.MapData.get(product as EProductPackage);
			default:
				throwLogicError(`getGameDBProductData(): Invalid product type: ${productType}`, EAPIErrorCode.INVALID_PARAMETERS);
		}
	}

	// VIP 등급 계산 (누적 실결제 금액 기준)
	private getCurrentVipLevel(): EVipLevel {
		const totalAmount = this.coreData.datas.products.PurchaseStatistics.TotalPurchaseAmount.GetValue();
		const vipLevelMap = this.gameDB.datas.vipLevelMap;
		if (!vipLevelMap?.MapData) return EVipLevel.None;

		let currentLevel: EVipLevel = EVipLevel.None;
		let currentThreshold = 0;

		for (const [, vipData] of vipLevelMap.MapData) {
			if (totalAmount >= vipData.PaymentThreshold && vipData.PaymentThreshold > currentThreshold) {
				currentLevel = vipData.VipLevel;
				currentThreshold = vipData.PaymentThreshold;
			}
		}
		return currentLevel;
	}

	// 결제 금액 가져오기
	getPaymentCost(product: number, productType: PurchaseProductType): [EProductPaymentType, ECurrency, number] {
		let productMap;
		switch (productType) {
			case PurchaseProductType.EProduct:
				productMap = this.gameDB.datas.productMap;
				break;
			case PurchaseProductType.EProductPass:
				productMap = this.gameDB.datas.productPassMap;
				break;
			case PurchaseProductType.EProductSubscription:
				productMap = this.gameDB.datas.productSubscriptionMap;
				break;
			case PurchaseProductType.EProductPackage:
				productMap = this.gameDB.datas.productPackageMap;
				break;
			default:
				throwLogicError(`getPaymentCost(): Invalid product type: ${productType}`, EAPIErrorCode.INVALID_PARAMETERS);
		}
		return [productMap.MapData.get(product).PaymentMethod, productMap.MapData.get(product).PaymentCurrency, productMap.MapData.get(product).PaymentCost];
	}

	async createMailItemFromPurchasedProduct(product: number, productType: PurchaseProductType) {
		const mailboxItem = new CoreData_MailboxItem();

		switch (productType) {
			case PurchaseProductType.EProduct:
				{
					mailboxItem.UID = uuid.v4();
					mailboxItem.From = EMailboxFrom.Product;
					mailboxItem.ProductID = product as EProduct;
					mailboxItem.PurchaseLimitDate = this.gameDB.datas.productMap.MapData.get(product as EProduct).LimitDate;
					const timeLeftToReceive = this.gameDB.datas.productMap.MapData.get(product as EProduct).TimeLeftToReceive;
					mailboxItem.TimeLeftToReceiveDate = this.addSecondsAndGetUTCString(timeLeftToReceive);

					this.coreData.datas.mailboxes.Items.set(mailboxItem.UID, mailboxItem);
				}
				break;
			case PurchaseProductType.EProductPackage:
				{
					mailboxItem.UID = uuid.v4();
					mailboxItem.From = EMailboxFrom.Product_Package;
					mailboxItem.ProductPackageID = product as EProductPackage;
					mailboxItem.PurchaseLimitDate = this.gameDB.datas.productPackageMap.MapData.get(product as EProductPackage).LimitDate;
					const timeLeftToReceive = this.gameDB.datas.productPackageMap.MapData.get(product as EProductPackage).TimeLeftToReceive;
					mailboxItem.TimeLeftToReceiveDate = this.addSecondsAndGetUTCString(timeLeftToReceive);

					this.coreData.datas.mailboxes.Items.set(mailboxItem.UID, mailboxItem);
				}
				break;
			case PurchaseProductType.EProductSubscription:
				{
					await this.createInternalRequest(CMailbox_CheckAndDeliverSubscriptionProduct).processRequest({}, {}, this.resultData);
				}
				break;
			case PurchaseProductType.EProductPass: // 패스는 우편함으로 받는 것이 아니라 바로 지급
			default: {
				throwLogicError(`createMailItemFromPurchasedProduct(): Invalid productType: ${productType}`, EAPIErrorCode.INVALID_PARAMETERS);
			}
		}

		// 우편함 지급 성공 이벤트
		this.logEvent_MailboxItemDeliverySuccess(product, productType, mailboxItem);
	}

	// 현재 시간 + timeLeftInSeconds 를 UTC 시간으로 변환
	private addSecondsAndGetUTCString(timeLeftInSeconds: number): string {
		if (timeLeftInSeconds === 0) {
			return "";
		}

		const currentTime = new Date();
		const futureTime = new Date(currentTime.getTime() + timeLeftInSeconds * 1000);
		return futureTime.toISOString();
	}
}

export class CProductPassBase extends CProductBase {
	constructor(init?: Partial<CProductPassBase>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected async verifyAndFetchProductPass(productPass: EProductPass): Promise<GameDB_Server_ProductPass> {
		// 패스 상품 검증
		this.verifyProduct(productPass, PurchaseProductType.EProductPass);

		// 패스 상품 정보 가져오기
		const gameDBProductPass = this.gameDB.datas.productPassMap.MapData.get(productPass);
		if (!gameDBProductPass) {
			throwLogicError(`verifyAndFetchProductPass(): 패스 상품(${productPass}) 정보를 찾을 수 없습니다.`, EAPIErrorCode.ITEM_NOT_AVAILABLE);
		}

		return gameDBProductPass;
	}
}

export class CProduct_Product_Buy extends CProductBase {
	constructor(init?: Partial<CProduct_Product_Buy>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			product: {
				type: "enum",
				enumObject: EProduct,
			},
			count: {
				type: "number",
				min: 1,
				max: 1000,
			},
			...this.getStoreReceiptValidationSchema(),
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const product: EProduct = data.product;
		const count: number = data.count;
		const transactionID: string = data.transactionID;
		const receipt: string = data.receipt;
		return await this.executeProcessPurchase(product, PurchaseProductType.EProduct, count, transactionID, receipt);
	}
}

export class CProduct_ProductPackage_Buy extends CProductBase {
	constructor(init?: Partial<CProduct_ProductPackage_Buy>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			productPackage: {
				type: "enum",
				enumObject: EProductPackage,
			},
			count: {
				type: "number",
				min: 1,
				max: 1000,
			},
			...this.getStoreReceiptValidationSchema(),
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const productPackage: EProductPackage = data.productPackage;
		const count: number = data.count;
		const transactionID: string = data.transactionID;
		const receipt: string = data.receipt;
		return await this.executeProcessPurchase(productPackage, PurchaseProductType.EProductPackage, count, transactionID, receipt);
	}
}

export class CProduct_ProductSubscription_Buy extends CProductBase {
	constructor(init?: Partial<CProduct_ProductSubscription_Buy>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			productSubscription: {
				type: "enum",
				enumObject: EProductSubscription,
			},
			count: {
				type: "number",
				min: 1,
				max: 1000,
			},
			...this.getStoreReceiptValidationSchema(),
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const product: EProductSubscription = data.productSubscription;
		const count: number = data.count;
		const transactionID: string = data.transactionID;
		const receipt: string = data.receipt;
		return await this.executeProcessPurchase(product, PurchaseProductType.EProductSubscription, count, transactionID, receipt);
	}
}

export class CProduct_ProductPass_Buy extends CProductBase {
	constructor(init?: Partial<CProduct_ProductPass_Buy>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			productPass: {
				type: "enum",
				enumObject: EProductPass,
			},
			count: {
				type: "number",
				min: 1,
				max: 1000,
			},
			...this.getStoreReceiptValidationSchema(),
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const product: EProductPass = data.productPass;
		const count: number = data.count;
		const transactionID: string = data.transactionID;
		const receipt: string = data.receipt;
		return await this.executeProcessPurchase(product, PurchaseProductType.EProductPass, count, transactionID, receipt);
	}
}

export abstract class CProduct_ProductPass_Receive_Base extends CProductPassBase {
	constructor(init?: Partial<CProduct_ProductPass_Receive_Base>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.products, CoreDataKey.COREDATAKEY_PRODUCT);
		this.addToCreateCoreData(this.coreData.datas.currencies, CoreDataKey.COREDATAKEY_CURRENCY);
		this.addToCreateCoreData(this.coreData.datas.player, CoreDataKey.COREDATAKEY_PLAYER);
		this.addToCreateCoreData(this.coreData.datas.statistics, CoreDataKey.COREDATAKEY_STATISTICS);
	}

	// PassType이 sequential, season인 상품의 모든 상품 아이템(유료 포함) 수령 시 다음 스텝(CurrentStep+1)으로 넘어감
	public processNextStep(gameDBProductPass: GameDB_Server_ProductPass, purchaseInfo: CoreData_PurchaseInfo_Pass) {
		// 단계가 있는 패스 상품인지 확인
		if (gameDBProductPass.ProductPassType !== EProductPassType.sequential && gameDBProductPass.ProductPassType !== EProductPassType.season) {
			return;
		}

		// 모든 아이템 수령 확인
		const allFreeItemsReceived = purchaseInfo.LastReceivedFreeItemIndex.GetValue() >= gameDBProductPass.FreeItems.length - 1;
		const allPaidItemsReceived = purchaseInfo.ReceivedPaidItemIndexes.length >= gameDBProductPass.PaidItems.length;

		if (allFreeItemsReceived && allPaidItemsReceived) {
			// 다음 스텝으로 이동
			const currentStep = purchaseInfo.CurrentStep.GetValue();
			const nextStep = currentStep + 1;

			// 같은 CategoryName 중에 CurrentSequentialStep 이 내 CurrentSequentialStep 보다 +1 큰 값이 있는지 확인
			const nextStepProductPass = Array.from(this.gameDB.datas.productPassMap.MapData.values()).find((pass) => pass.CategoryName === gameDBProductPass.CategoryName && pass.CurrentSequentialStep === nextStep + 1);
			// 시트에서 Step은 1부터 시작하므로 1을 더해줌

			// 다음 단계로 이동
			if (nextStepProductPass) {
				purchaseInfo.CurrentStep.Set(nextStep);
				purchaseInfo.IsPurchased = ModValueBool.create(false);
				// 받은 상품 목록은 리셋
				purchaseInfo.LastReceivedFreeItemIndex = ModValueInt.create(0);
				purchaseInfo.ReceivedPaidItemIndexes = [];
			}
		}
	}
}

export class CProduct_ProductPass_Receive_PaidItem extends CProduct_ProductPass_Receive_Base {
	constructor(init?: Partial<CProduct_ProductPass_Receive_PaidItem>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			productPass: {
				type: "enum",
				enumObject: EProductPass,
			},
			itemIndex: {
				type: "number",
				min: 0,
				max: 1000,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const productPass: EProductPass = data.productPass;
		const itemIndex: number = data.itemIndex;

		return await this.executePassReceivePaidItem(productPass, itemIndex);
	}

	private async executePassReceivePaidItem(productPass: EProductPass, itemIndex: number): Promise<[EResponseCode, string]> {
		const gameDBProductPass = await this.verifyAndFetchProductPass(productPass);

		const purchaseInfo = this.getPurchaseInfo(productPass, PurchaseProductType.EProductPass) as CoreData_PurchaseInfo_Pass;
		if (!purchaseInfo || !purchaseInfo.IsPurchased.GetValue()) {
			throwLogicError(`pass_Receive_PaidItem(): 패스 상품${productPass}을 구매하지 않았습니다.`, EAPIErrorCode.DATABASE_ERROR);
		}

		if (itemIndex >= gameDBProductPass.PaidItems.length) {
			throwLogicError(`pass_Receive_PaidItem(): 유효하지 않은 인덱스입니다.`, EAPIErrorCode.DATABASE_ERROR);
		}

		if (purchaseInfo.ReceivedPaidItemIndexes.some((index) => index.GetValue() === itemIndex)) {
			throwLogicError(`pass_Receive_PaidItem(): 이미 수령한 아이템입니다.`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 현재 스텝이 일치하는지 확인 (sequential, season 타입에만 적용)
		if (gameDBProductPass.ProductPassType === EProductPassType.sequential || gameDBProductPass.ProductPassType === EProductPassType.season) {
			if (purchaseInfo.CurrentStep.GetValue() + 1 !== gameDBProductPass.CurrentSequentialStep) {
				throwLogicError(`pass_Receive_PaidItem(): 상품과 단계가 맞지 않습니다.`, EAPIErrorCode.DATABASE_ERROR);
			}
		}

		await this.checkReceiveConditions(gameDBProductPass, itemIndex, purchaseInfo.ReceivedPaidItemIndexes, gameDBProductPass.PaidItems);

		await this.processRewardItem(gameDBProductPass.PaidItems[itemIndex]);

		// 수령한 아이템 인덱스 업데이트
		purchaseInfo.ReceivedPaidItemIndexes.push(ModValueInt.create(itemIndex));

		// 받은 유료 보상 개수 증가
		purchaseInfo.ReceivedPaidRewardCount.Add(1);

		// 모든 아이템 수령이라면 다음 스텝으로 이동
		this.processNextStep(gameDBProductPass, purchaseInfo);

		return resultSuccess;
	}

	protected async checkReceiveConditions(gameDBProductPass: GameDB_Server_ProductPass, itemIndex: number, receivedItemIndexes: Array<ModValueInt>, items: Array<any>) {
		// 아이템 인덱스 범위 확인
		if (itemIndex < 0 || itemIndex >= items.length) {
			throwLogicError(`checkReceiveConditions(): 유효하지 않은 아이템 인덱스(${itemIndex}) 입니다.`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 이미 수령한 아이템인지 확인
		if (receivedItemIndexes.some((index) => index.GetValue() === itemIndex)) {
			throwLogicError(`checkReceiveConditions(): 이미 수령한 아이템입니다.`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 수령 조건 계산
		const requiredValue = gameDBProductPass.PassValueStart + gameDBProductPass.PassValueIncrement * itemIndex;
		if (requiredValue > gameDBProductPass.PassValueEnd) {
			throwLogicError(`checkReceiveConditions(): 유효하지 않은 수령 조건입니다.`, EAPIErrorCode.DATABASE_ERROR);
		}
		// 실제 달성한 값 가져오기 (예: 업적 달성도)
		const params = {
			achieveItem: gameDBProductPass.RewardAcieveCondition,
			value: requiredValue,
		};
		await this.createInternalRequest(CAchieveItem_IsComplete).executeIsComplete(params.achieveItem, params.value);
	}
}

export class CProduct_ProductPass_ReceiveAll_FreeItem extends CProduct_ProductPass_Receive_Base {
	constructor(init?: Partial<CProduct_ProductPass_ReceiveAll_FreeItem>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.products, CoreDataKey.COREDATAKEY_PRODUCT);
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			productPass: {
				type: "enum",
				enumObject: EProductPass,
			},
			itemIndex: {
				type: "number",
				min: 0,
				max: 1000,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const productPass: EProductPass = data.productPass;
		const itemIndex: number = data.itemIndex;

		return await this.executePassReceiveAllFreeItem(productPass, itemIndex);
	}

	public async executePassReceiveAllFreeItem(productPass: EProductPass, itemIndex: number): Promise<[EResponseCode, string]> {
		const gameDBProductPass = await this.verifyAndFetchProductPass(productPass);

		const purchaseInfo = this.getPurchaseInfo(productPass, PurchaseProductType.EProductPass) as CoreData_PurchaseInfo_Pass;
		if (!purchaseInfo) {
			throwLogicError(`pass_ReceiveAll_FreeItem(): 패스 상품${productPass} 아이템${itemIndex}을 찾을 수 없습니다.`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 현재 스텝이 일치하는지 확인 (sequential, season 타입에만 적용)
		if (gameDBProductPass.ProductPassType === EProductPassType.sequential || gameDBProductPass.ProductPassType === EProductPassType.season) {
			if (purchaseInfo.CurrentStep.GetValue() + 1 !== gameDBProductPass.CurrentSequentialStep) {
				throwLogicError(`pass_ReceiveAll_FreeItem(): 상품과 단계가 맞지 않습니다.`, EAPIErrorCode.DATABASE_ERROR);
			}
		}

		const lastReceivedIndex = purchaseInfo.LastReceivedFreeItemIndex.GetValue();
		for (let index = lastReceivedIndex + 1; index <= itemIndex && index < gameDBProductPass.FreeItems.length; index++) {
			await this.checkReceiveConditions(gameDBProductPass, index, lastReceivedIndex, gameDBProductPass.FreeItems);

			const item = gameDBProductPass.FreeItems[index];
			await this.processRewardItem(item);

			// 수령한 아이템 인덱스 업데이트
			purchaseInfo.LastReceivedFreeItemIndex.Set(index);

			// 받은 무료 보상 개수 증가
			purchaseInfo.ReceivedFreeRewardCount.Add(1);
		}

		// 모든 아이템 수령이라면 다음 스텝으로 이동
		this.processNextStep(gameDBProductPass, purchaseInfo);

		return resultSuccess;
	}

	protected async checkReceiveConditions(gameDBProductPass: GameDB_Server_ProductPass, itemIndex: number, lastReceivedIndex: number, items: Array<any>) {
		// 아이템 인덱스 범위 확인
		if (itemIndex < 0 || itemIndex >= items.length) {
			throwLogicError(`checkReceiveConditions(): 유효하지 않은 아이템 인덱스(${itemIndex}) 입니다.`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 이미 수령한 아이템인지 확인
		if (itemIndex <= lastReceivedIndex) {
			throwLogicError(`checkReceiveConditions(): 이미 수령한 아이템입니다.`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 수령 조건 계산
		const requiredValue = gameDBProductPass.PassValueStart + gameDBProductPass.PassValueIncrement * itemIndex;
		if (requiredValue > gameDBProductPass.PassValueEnd) {
			throwLogicError(`checkReceiveConditions(): 유효하지 않은 수령 조건입니다.`, EAPIErrorCode.DATABASE_ERROR);
		}
		// 실제 달성한 값 가져오기 (예: 업적 달성도)
		const params = {
			achieveItem: gameDBProductPass.RewardAcieveCondition,
			value: requiredValue,
		};
		await this.createInternalRequest(CAchieveItem_IsComplete).executeIsComplete(params.achieveItem, params.value);
	}
}

export class CProduct_ProductPass_ReceiveAll extends CProduct_ProductPass_Receive_Base {
	constructor(init?: Partial<CProduct_ProductPass_ReceiveAll>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.products, CoreDataKey.COREDATAKEY_PRODUCT);
		this.addToCreateCoreData(this.coreData.datas.currencies, CoreDataKey.COREDATAKEY_CURRENCY);
		this.addToCreateCoreData(this.coreData.datas.player, CoreDataKey.COREDATAKEY_PLAYER);
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			productPass: {
				type: "enum",
				enumObject: EProductPass,
			},
			itemIndex: {
				type: "number",
				min: 0,
				max: 1000,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const productPass: EProductPass = data.productPass;
		const itemIndex: number = data.itemIndex; // 받아야 할 패스 아이템 인덱스. 0 ~ (RewardTotalCount-1)

		return await this.executePassReceiveAll(productPass, itemIndex);
	}

	public async executePassReceiveAll(productPass: EProductPass, itemIndex: number): Promise<[EResponseCode, string]> {
		const gameDBProductPass = await this.verifyAndFetchProductPass(productPass);

		const purchaseInfo = this.coreData.datas.products.ProductPass.get(productPass) as CoreData_PurchaseInfo_Pass;
		const lastReceivedFreeIndex = purchaseInfo.LastReceivedFreeItemIndex.GetValue();
		// Set으로 변환하여 includes() O(n) -> has() O(1) 최적화
		const receivedPaidItemIndexSet = new Set(purchaseInfo.ReceivedPaidItemIndexes.map((index) => index.GetValue()));
		const isPurchased = purchaseInfo.IsPurchased.GetValue();

		// 현재 스텝이 일치하는지 확인 (sequential, season 타입에만 적용)
		if (gameDBProductPass.ProductPassType === EProductPassType.sequential || gameDBProductPass.ProductPassType === EProductPassType.season) {
			if (purchaseInfo.CurrentStep.GetValue() + 1 !== gameDBProductPass.CurrentSequentialStep) {
				throwLogicError(`pass_ReceiveAll(): 상품과 단계가 맞지 않습니다.`, EAPIErrorCode.DATABASE_ERROR);
			}
		}

		// 무료 아이템 수령
		await this.receiveAllFreeItems(gameDBProductPass, lastReceivedFreeIndex, itemIndex, productPass);

		// 유료 아이템 수령 (구매한 경우에만)
		if (isPurchased) {
			await this.receiveAllPaidItems(gameDBProductPass, receivedPaidItemIndexSet, itemIndex, productPass);
		}

		// 모든 아이템 수령이라면 다음 스텝으로 이동
		this.processNextStep(gameDBProductPass, purchaseInfo);

		return resultSuccess;
	}

	private async receiveAllFreeItems(gameDBProductPass: GameDB_Server_ProductPass, lastReceivedFreeIndex: number, itemIndex: number, productPass: EProductPass) {
		const emptySet = new Set<number>();
		for (let index = lastReceivedFreeIndex + 1; index <= itemIndex && index < gameDBProductPass.FreeItems.length; index++) {
			await this.checkReceiveConditions(gameDBProductPass, index, emptySet, gameDBProductPass.FreeItems);

			const item = gameDBProductPass.FreeItems[index];
			await this.processRewardItem(item);

			// 수령한 아이템 인덱스 업데이트
			this.coreData.datas.products.ProductPass.get(productPass).LastReceivedFreeItemIndex.Set(index);
			// 받은 무료 보상 개수 증가
			this.coreData.datas.products.ProductPass.get(productPass).ReceivedFreeRewardCount.Add(1);
		}
	}

	private async receiveAllPaidItems(gameDBProductPass: GameDB_Server_ProductPass, receivedPaidItemIndexSet: Set<number>, itemIndex: number, productPass: EProductPass) {
		for (let index = 0; index <= itemIndex && index < gameDBProductPass.PaidItems.length; index++) {
			if (receivedPaidItemIndexSet.has(index)) continue;

			await this.checkReceiveConditions(gameDBProductPass, index, receivedPaidItemIndexSet, gameDBProductPass.PaidItems);

			const item = gameDBProductPass.PaidItems[index];
			await this.processRewardItem(item);

			// 수령한 아이템 인덱스 업데이트
			this.coreData.datas.products.ProductPass.get(productPass).ReceivedPaidItemIndexes.push(ModValueInt.create(index));

			// 받은 유료 보상 개수 증가
			this.coreData.datas.products.ProductPass.get(productPass).ReceivedPaidRewardCount.Add(1);
		}
	}

	protected async checkReceiveConditions(gameDBProductPass: GameDB_Server_ProductPass, itemIndex: number, receivedItemIndexSet: Set<number>, items: Array<any>) {
		// 아이템 인덱스 범위 확인
		if (itemIndex < 0 || itemIndex >= items.length) {
			throwLogicError(`checkReceiveConditions(): 유효하지 않은 아이템 인덱스(${itemIndex}) 입니다.`, EAPIErrorCode.INVALID_PARAMETERS);
		}

		// 이미 수령한 아이템인지 확인 (Set.has() O(1))
		if (receivedItemIndexSet.has(itemIndex)) {
			throwLogicError(`checkReceiveConditions(): 이미 수령한 아이템입니다.`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 수령 조건 계산
		const requiredValue = gameDBProductPass.PassValueStart + gameDBProductPass.PassValueIncrement * itemIndex;
		if (requiredValue > gameDBProductPass.PassValueEnd) {
			throwLogicError(`checkReceiveConditions(): 유효하지 않은 수령 조건입니다.`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 실제 달성한 값 가져오기 (예: 업적 달성도)
		const params = {
			achieveItem: gameDBProductPass.RewardAcieveCondition,
			value: requiredValue,
		};
		await this.createInternalRequest(CAchieveItem_IsComplete).executeIsComplete(params.achieveItem, params.value);
	}
}

export class CProduct_ProductPass_Reset extends CProductPassBase {
	constructor(init?: Partial<CProduct_ProductPass_Reset>) {
		super();
		Object.assign(this, init);
	}

	public async setUp() {
		await super.setUp();

		this.addToCreateCoreData(this.coreData.datas.products, CoreDataKey.COREDATAKEY_PRODUCT);
		this.addToCreateCoreData(this.coreData.datas.account, CoreDataKey.COREDATAKEY_ACCOUNT);
	}

	protected getValidationSchema(): ValidationSchema {
		return {
			productPass: {
				type: "enum",
				enumObject: EProductPass,
			},
		};
	}

	public async execute(data: any): Promise<[EResponseCode, string]> {
		const productPass: EProductPass = data.productPass;

		return await this.executePassReset(productPass);
	}

	public async executePassReset(productPass: EProductPass): Promise<[EResponseCode, string]> {
		const gameDBProductPass = await this.verifyAndFetchProductPass(productPass);

		const purchaseInfo = this.coreData.datas.products.ProductPass.get(productPass) as CoreData_PurchaseInfo_Pass;

		// 패스 상품 리셋 조건 확인
		if (gameDBProductPass.ProductPassType !== EProductPassType.attendance || purchaseInfo.LastReceivedFreeItemIndex.GetValue() < gameDBProductPass.MaxRewardPerDay - 1) {
			throwLogicError(`pass_Reset(): Product pass reset conditions not met.`, EAPIErrorCode.DATABASE_ERROR);
		}

		// 리셋 실행
		purchaseInfo.LastReceivedFreeItemIndex = ModValueInt.create(0);
		purchaseInfo.ReceivedPaidItemIndexes = [];

		// 출석 패스인 경우 AttendanceDays도 리셋
		if (gameDBProductPass.ProductPassType === EProductPassType.attendance) {
			this.coreData.datas.account.ActivityInfo.AttendanceDays = ModValueInt.create(0);
		}

		return resultSuccess;
	}
}

// -----------------------------------------------
// Function
// -----------------------------------------------
export async function product_CreateCoreData(DBDatas: GameDBDatas) {
	const coreDataMap = new CoreData_Products();

	// CoreData_PurchaseStatistics 초기화
	coreDataMap.PurchaseStatistics = initializePurchaseStatistics();

	// 각 상품 유형별 데이터 설정
	initializeProduct(coreDataMap.Product, EProduct, DBDatas.productMap);
	initializeProductPackage(coreDataMap.ProductPackage, EProductPackage, DBDatas.productPackageMap);
	initializeProductPass(coreDataMap.ProductPass, EProductPass, DBDatas.productPassMap, 1);
	initializeProductSubscription(coreDataMap.ProductSubscription, EProductSubscription, DBDatas.productSubscriptionMap);

	return coreDataMap;
}

function initializeProduct(map: Map<any, any>, enumType: any, db: any): void {
	for (const key of Object.keys(enumType)) {
		const value = enumType[key as keyof typeof enumType];
		if (value == enumType.None) continue;

		const newProductPurchaseInfo: CoreData_PurchaseInfo_Product = new CoreData_PurchaseInfo_Product();
		const dbData = db.MapData.get(value);
		newProductPurchaseInfo.PurchasedCount = ModValueInt.create(0);
		newProductPurchaseInfo.Product = value;

		map.set(value, newProductPurchaseInfo);
	}
}

function initializeProductPackage(map: Map<any, any>, enumType: any, db: any): void {
	for (const key of Object.keys(enumType)) {
		const value = enumType[key as keyof typeof enumType];
		if (value == enumType.None) continue;

		const newProductPurchaseInfo: CoreData_PurchaseInfo_Package = new CoreData_PurchaseInfo_Package();
		newProductPurchaseInfo.PurchasedCount = ModValueInt.create(0);
		newProductPurchaseInfo.Product = value;

		map.set(value, newProductPurchaseInfo);
	}
}

function initializeProductPass(map: Map<any, any>, enumType: any, db: GameDB_Server_ProductPassMap, customLimitCount: number): void {
	for (const key of Object.keys(enumType)) {
		const value = enumType[key as keyof typeof enumType];
		if (value == enumType.None) continue;

		const purchaseInfo = new CoreData_PurchaseInfo_Pass();
		purchaseInfo.Product = value;
		purchaseInfo.PurchasedCount = ModValueInt.create(0);
		purchaseInfo.CurrentStep = ModValueInt.create(0);
		purchaseInfo.IsPurchased = ModValueBool.create(false);
		purchaseInfo.LastReceivedFreeItemIndex = ModValueInt.create(-1);
		purchaseInfo.ReceivedPaidItemIndexes = new Array<ModValueInt>();
		purchaseInfo.ReceivedFreeRewardCount = ModValueInt.create(0);
		purchaseInfo.ReceivedPaidRewardCount = ModValueInt.create(0);

		map.set(value, purchaseInfo);
	}
}

function initializeProductSubscription(map: Map<any, any>, enumType: any, db: GameDB_Server_ProductSubscriptionMap): void {
	for (const key of Object.keys(enumType)) {
		const value = enumType[key as keyof typeof enumType];
		if (value == enumType.None) continue;

		//const dbData = db.MapData.get(value);
		const purchaseInfo = new CoreData_PurchaseInfo_Subscription();
		purchaseInfo.Product = value;
		purchaseInfo.PurchasedCount = ModValueInt.create(0);
		purchaseInfo.ReceivedRewardDays = ModValueInt.create(0);
		purchaseInfo.IsSubscribing = ModValueBool.create(false);
		purchaseInfo.LastRewardDate = "";

		map.set(value, purchaseInfo);
	}
}

function initializePurchaseStatistics(): CoreData_PurchaseStatistics {
	return {
		FirstPurchaseDate: "",
		LatestPurchaseDate: "",
		TotalPurchaseCount: ModValueInt.create(0),
		TotalPurchaseAmount: ModValueInt.create(0),
		TotalPurchaseAmountPercentage: ModValueDouble.create(0),
		PurchaseFrequencyInMonth: ModValueInt.create(0),
		LatestRefundRequestDate: "",
		TotalRefundCount: ModValueInt.create(0),
		TotalAdViews: ModValueInt.create(0),
	};
}

// 지불 재화 정보 얻기
export function getProductPaidCurrencyAndCost(datas: GameDBDatas, gacha: EGacha): [ECurrency, number] {
	if (datas.gachaMap.MapData.get(gacha).PaymentMethod != EProductPaymentType.currency) {
		throwLogicError("getProductPaidCurrencyAndCost(): 상품의 지불 타입이 재화가 아닙니다", EAPIErrorCode.INVALID_PARAMETERS);
	}

	const productCoreData = datas.gachaMap.MapData.get(gacha);
	return [productCoreData.PaymentCurrency, productCoreData.PaymentCost];
}

// 재화를 GachaProduct 으로 변환
// 변경 시 클라이언트 ConvertCurrencyToGachaProduct 도 같이 변경해줄 것
export function getGachaProductByCurrency(currency: ECurrency): EGachaProduct {
	switch (currency) {
		case ECurrency.currency_equipment_weapon:
		case ECurrency.currency_equipment_armour:
			return EGachaProduct.EquipmentWeaponAndArmour;
		case ECurrency.currency_equipment_accessory:
			return EGachaProduct.EquipmentAccessory;
		case ECurrency.currency_skill_spell:
		case ECurrency.currency_skill_aura:
			return EGachaProduct.SkillSpellAndAura;
		case ECurrency.currency_pet:
			return EGachaProduct.Pet;
		case ECurrency.currency_skill_rune:
			return EGachaProduct.SkillRune;
		case ECurrency.currency_equipment_reinforce:
			return EGachaProduct.EquipmentReinforce;
		case ECurrency.currency_equipment_reforging:
			return EGachaProduct.EquipmentReforging;
		case ECurrency.currency_constellation:
			return EGachaProduct.Constellation;
		default:
			throwLogicError("getGachaProductByCurrency(): convert_CurrencyToGachaProduct: Invalid currency for gacha type conversion", EAPIErrorCode.INVALID_PARAMETERS);
	}
}

export function IsGachaProduct(currencies: CoreData_Currencies, currency: ECurrency): boolean {
	if (isBigIntCurrency(currency)) {
		return currencies.BigIntCurrencies.get(currency).IsGachaProduct;
	} else {
		return currencies.Currencies.get(currency).IsGachaProduct;
	}
}
