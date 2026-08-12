// PRNG 인터페이스 정의
// 클라이언트와 수동 동기화 필요

export interface IPRNG {
	nextRandom(): number;
	logRandAndEnum?(rand: number, e: number): void;
	logRandAndTwoEnums?(rand: number, e1: number, e2: number): void;
	writeRandomLogsToFile?(filePrefix: string): void | Promise<void>;
}
