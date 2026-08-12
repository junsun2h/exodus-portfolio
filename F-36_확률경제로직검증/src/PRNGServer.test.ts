import "reflect-metadata";
import { CTestBase } from "../../Base/TestBase";
import sinon from "sinon";
import * as fs from "fs";
import * as path from "path";
import * as SecretManager from "../../../src/Utility/SecretManager";

const chai = require("chai");
const assert = chai.assert;

// HashPRNG를 테스트하기 위해 getAccountHashSecret stub 필요
let secretStub: sinon.SinonStub;

describe("API : PRNGServer", () => {
	let test = new CTestBase();

	before(async function () {
		test.before();
		// generateHash() 내부에서 getAccountHashSecret()을 호출하므로 stub 처리
		secretStub = sinon.stub(SecretManager, "getAccountHashSecret").returns("a".repeat(64));
	});
	after(() => {
		test.after();
		secretStub.restore();
	});

	describe("Tests", () => {
		before(async function () { await test.describe_before(); });
		beforeEach(async function () { await test.describe_beforeEach(); });
		afterEach(async function () { await test.describe_afterEach(); });
		after(async function () { await test.describe_after(); });

		// writeRandomLogsToFile - 로깅 비활성화 시 즉시 반환
		it("writeRandomLogsToFile: 로깅 비활성화 시 파일을 생성하지 않는다", async () => {
			const { HashPRNG } = await import("../../../src/Data/Types/PRNGServer");

			const prng = new HashPRNG("abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890", false);

			const mkdirSpy = sinon.spy(fs.promises, "mkdir");
			const writeFileSpy = sinon.spy(fs.promises, "writeFile");

			await prng.writeRandomLogsToFile("test_prefix");

			// 로깅이 비활성화되어 있으므로 파일 시스템 호출이 없어야 한다
			assert.isFalse(mkdirSpy.called, "mkdir가 호출되지 않아야 한다");
			assert.isFalse(writeFileSpy.called, "writeFile이 호출되지 않아야 한다");

			mkdirSpy.restore();
			writeFileSpy.restore();
		});

		// writeRandomLogsToFile - 로깅 활성화 시 파일 생성
		it("writeRandomLogsToFile: 로깅 활성화 시 디버그 로그를 JSON 파일로 기록한다", async () => {
			const { HashPRNG } = await import("../../../src/Data/Types/PRNGServer");

			const hash = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
			const prng = new HashPRNG(hash, true);

			// createPRNG으로 PRNG 초기화
			const newHash = prng.createPRNG(hash);
			assert.isString(newHash);

			// 난수 생성 및 로그 기록
			const randVal = prng.nextRandom();
			prng.logRandAndEnum(randVal, 5);
			prng.logRandAndTwoEnums(randVal, 3, 7);

			// fs 호출을 stub하여 실제 파일 생성 방지
			const existsSyncStub = sinon.stub(fs, "existsSync").returns(false);
			const mkdirStub = sinon.stub(fs.promises, "mkdir").resolves(undefined);
			const writeFileStub = sinon.stub(fs.promises, "writeFile").resolves();

			await prng.writeRandomLogsToFile("gacha_equipment");

			// writeFile이 호출되었는지 확인
			assert.isTrue(writeFileStub.calledOnce, "writeFile이 1회 호출되어야 한다");

			// 파일명에 해시 앞 8글자와 prefix가 포함되어야 한다
			const writtenPath = writeFileStub.firstCall.args[0] as string;
			assert.include(path.basename(writtenPath), "abcdef12", "파일명에 해시 앞 8글자가 포함되어야 한다");
			assert.include(path.basename(writtenPath), "server_gacha_equipment", "파일명에 prefix가 포함되어야 한다");
			assert.isTrue(writtenPath.endsWith(".json"), "확장자는 .json이어야 한다");

			// 기록된 JSON 내용 검증
			const writtenContent = JSON.parse(writeFileStub.firstCall.args[1] as string);
			assert.isArray(writtenContent.randomValues, "randomValues는 배열이어야 한다");
			assert.lengthOf(writtenContent.randomValues, 2, "logRandAndEnum + logRandAndTwoEnums = 2건");

			// 디렉토리 생성이 호출되었는지 확인 (existsSync가 false를 반환하므로)
			assert.isTrue(mkdirStub.calledOnce, "mkdir가 1회 호출되어야 한다");

			existsSyncStub.restore();
			mkdirStub.restore();
			writeFileStub.restore();
		});

		// writeRandomLogsToFile - 디렉토리가 이미 존재할 때
		it("writeRandomLogsToFile: 디렉토리가 이미 존재하면 mkdir를 호출하지 않는다", async () => {
			const { HashPRNG } = await import("../../../src/Data/Types/PRNGServer");

			const hash = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
			const prng = new HashPRNG(hash, true);

			prng.createPRNG(hash);

			const existsSyncStub = sinon.stub(fs, "existsSync").returns(true);
			const mkdirStub = sinon.stub(fs.promises, "mkdir").resolves(undefined);
			const writeFileStub = sinon.stub(fs.promises, "writeFile").resolves();

			await prng.writeRandomLogsToFile("test");

			// 디렉토리가 이미 존재하므로 mkdir 호출 안됨
			assert.isFalse(mkdirStub.called, "디렉토리가 존재하면 mkdir를 호출하지 않아야 한다");
			assert.isTrue(writeFileStub.calledOnce, "writeFile은 호출되어야 한다");

			existsSyncStub.restore();
			mkdirStub.restore();
			writeFileStub.restore();
		});

		// writeRandomLogsToFile - 파일 쓰기 실패 시 에러 throw
		it("실패: writeRandomLogsToFile에서 파일 쓰기 실패 시 throwLogicError가 발생한다", async () => {
			const { HashPRNG } = await import("../../../src/Data/Types/PRNGServer");

			const hash = "deadbeef1234567890abcdef1234567890abcdef1234567890abcdef12345678";
			const prng = new HashPRNG(hash, true);

			prng.createPRNG(hash);

			const existsSyncStub = sinon.stub(fs, "existsSync").returns(true);
			const writeFileStub = sinon.stub(fs.promises, "writeFile").rejects(new Error("disk full"));

			try {
				await prng.writeRandomLogsToFile("fail_test");
				assert.fail("에러가 발생해야 한다");
			} catch (e: any) {
				assert.include(e.message, "writeRandomLogsToFile()", "에러 메시지에 함수명이 포함되어야 한다");
			}

			existsSyncStub.restore();
			writeFileStub.restore();
		});

		// writeRandomLogsToFile - 로그가 비어있을 때도 정상 동작
		it("writeRandomLogsToFile: 로그가 비어있어도 빈 배열로 JSON 파일을 생성한다", async () => {
			const { HashPRNG } = await import("../../../src/Data/Types/PRNGServer");

			const hash = "ffffffff1234567890abcdef1234567890abcdef1234567890abcdef12345678";
			const prng = new HashPRNG(hash, true);

			prng.createPRNG(hash);
			// 난수를 뽑지 않고 바로 파일 기록

			const existsSyncStub = sinon.stub(fs, "existsSync").returns(true);
			const writeFileStub = sinon.stub(fs.promises, "writeFile").resolves();

			await prng.writeRandomLogsToFile("empty");

			assert.isTrue(writeFileStub.calledOnce);
			const writtenContent = JSON.parse(writeFileStub.firstCall.args[1] as string);
			assert.isArray(writtenContent.randomValues);
			assert.lengthOf(writtenContent.randomValues, 0, "로그가 비어있으면 빈 배열이어야 한다");

			existsSyncStub.restore();
			writeFileStub.restore();
		});
	});
});
