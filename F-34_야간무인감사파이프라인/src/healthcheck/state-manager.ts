// state.json — 발견 항목의 상태 추적 (OPEN / TRIAGED / FIXED)
// fingerprint(file:line:ruleId)를 키로 사용

import * as fs from "fs";
import * as path from "path";
import { Finding, Severity } from "./types";

export type FindingStatus = "OPEN" | "TRIAGED" | "FIXED";

export interface StateEntry {
  status: FindingStatus;
  severity: Severity;
  ruleId: string;
  file: string;
  line: number;
  firstSeen: string; // YYYY-MM-DD
  lastSeen: string; // YYYY-MM-DD
  note: string;
}

export interface State {
  [fingerprint: string]: StateEntry;
}

const STATE_PATH = path.join(__dirname, "state.json");

export function loadState(): State {
  if (!fs.existsSync(STATE_PATH)) return {};
  try {
    return JSON.parse(fs.readFileSync(STATE_PATH, "utf8"));
  } catch {
    return {};
  }
}

export function saveState(state: State): void {
  fs.writeFileSync(STATE_PATH, JSON.stringify(state, null, 2), "utf8");
}

// 현재 findings 기반으로 state 갱신.
// 반환: 새로 추가된 것, FIXED로 이동한 것, 여전히 OPEN인 것
export interface StateUpdate {
  newFindings: Finding[];
  fixedFindings: StateEntry[];
  stillOpen: Finding[];
}

// 이번 실행에서 스캔된 source(스캐너) 집합 — FIXED 전환은 여기 속한 entry에만 적용한다.
// server-raw/client-raw/parity-raw 중 일부가 누락된 상태로 aggregate이 돌아도
// 미스캔 source의 과거 TRIAGED 항목이 FIXED로 잘못 전환되지 않도록 방지.
export type ScanScope = "server" | "client" | "parity";

function getScopeFromRuleId(ruleId: string): ScanScope | "other" {
  if (ruleId.startsWith("SRV")) return "server";
  if (ruleId.startsWith("CLI")) return "client";
  if (ruleId.startsWith("PARITY")) return "parity";
  return "other";
}

export function updateState(
  current: Finding[],
  state: State,
  today: string,
  activeScopes?: Set<ScanScope>,
): StateUpdate {
  const currentSet = new Set(current.map((f) => f.fingerprint));
  const newFindings: Finding[] = [];
  const fixedFindings: StateEntry[] = [];
  const stillOpen: Finding[] = [];

  // 현재 발견 처리
  for (const f of current) {
    const existing = state[f.fingerprint];
    if (existing) {
      // 기존 항목 — lastSeen 갱신
      existing.lastSeen = today;
      if (existing.status !== "FIXED") {
        stillOpen.push(f);
      }
    } else {
      // 신규
      state[f.fingerprint] = {
        status: "OPEN",
        severity: f.severity,
        ruleId: f.ruleId,
        file: f.file,
        line: f.line,
        firstSeen: today,
        lastSeen: today,
        note: "",
      };
      newFindings.push(f);
    }
  }

  // 과거 OPEN인데 이번에 보이지 않는 것 → FIXED로
  // activeScopes가 주어지면 해당 scope에 속한 것만 FIXED 전환 (부분 스캔 보호)
  for (const [fp, entry] of Object.entries(state)) {
    if (entry.status === "FIXED") continue;
    if (currentSet.has(fp)) continue;
    if (activeScopes) {
      const scope = getScopeFromRuleId(entry.ruleId);
      if (scope === "other") continue; // 알 수 없는 scope는 보존
      if (!activeScopes.has(scope)) continue; // 이번에 스캔 안 한 scope는 보존
    }
    entry.status = "FIXED";
    fixedFindings.push(entry);
  }

  return { newFindings, fixedFindings, stillOpen };
}
