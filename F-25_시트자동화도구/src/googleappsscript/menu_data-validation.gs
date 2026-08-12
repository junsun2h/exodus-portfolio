// ===== 데이터 검증 =====

// 선택한 셀에서 중복된 값 찾기
function showDuplicatesInSelectedRange() {
  var sheet = SpreadsheetApp.getActiveSheet();
  var range = sheet.getActiveRange();
  var values = range.getValues();
  var startRow = range.getRow();

  var occurrences = {};

  for (var i = 0; i < values.length; i++) {
    for (var j = 0; j < values[i].length; j++) {
      var cellValue = values[i][j];
      if (cellValue !== "") {
        var currentRow = startRow + i;
        if (occurrences[cellValue]) {
          occurrences[cellValue].push(currentRow);
        } else {
          occurrences[cellValue] = [currentRow];
        }
      }
    }
  }

  var messageParts = [];
  for (var key in occurrences) {
    if (occurrences[key].length > 1) {
      var rows = occurrences[key];
      var uniqueRows = [];
      for (var i = 0; i < rows.length; i++) {
        if (uniqueRows.indexOf(rows[i]) === -1) {
          uniqueRows.push(rows[i]);
        }
      }
      uniqueRows.sort(function(a, b) { return a - b; });
      messageParts.push("값 '" + key + "'가(이) " + uniqueRows.join(", ") + "행에 중복되어 있습니다.");
    }
  }

  if (messageParts.length > 0) {
    SpreadsheetApp.getUi().alert("중복된 값 발견:\n" + messageParts.join("\n"));
  } else {
    SpreadsheetApp.getUi().alert("중복된 값이 없습니다.");
  }
}

// ── VALUE_IN_RANGE 위반 탐색 ─────────────────────────────────
// 전략: getDataValidations()를 1행만 읽어 열별 규칙 파악 후
//       getValues() 1회 + Set 비교로 처리 (전체 행 읽기 대비 수백 배 빠름)

const MAX_EXEC_MS = 5 * 60 * 1000;  // 5분 (6분 제한보다 1분 여유)

// 모든 탭에서 유효성 위반 오류 찾기
// 시작 탭 번호 입력 가능 (1 = 처음부터, 50 = 50번째 탭부터)
function findAllValidationViolations() {
  const ui = SpreadsheetApp.getUi();
  const ss = SpreadsheetApp.getActiveSpreadsheet();

  // 시작 탭 번호 입력 받기
  const response = ui.prompt(
    '유효성 위반 검사',
    '시작할 탭 번호를 입력하세요.\n(1 = 처음부터 / 50 = 50번째 탭부터)',
    ui.ButtonSet.OK_CANCEL
  );
  if (response.getSelectedButton() !== ui.Button.OK) return;

  const inputNum = parseInt(response.getResponseText().trim());
  const startNum = isNaN(inputNum) || inputNum < 1 ? 1 : inputNum;
  const startIdx = startNum - 1;  // 0-based

  // 1번부터 시작이면 _violations 초기화, 아니면 누적
  const SHEET_NAME = '_violations';
  let outSheet = ss.getSheetByName(SHEET_NAME);
  if (startIdx === 0) {
    if (outSheet) {
      outSheet.clearContents();
    } else {
      outSheet = ss.insertSheet(SHEET_NAME);
    }
    outSheet.getRange(1, 1, 1, 4).setValues([['시트', '셀', '열(헤더)', '현재값 → 기준범위']]);
    outSheet.getRange(1, 1, 1, 4).setFontWeight('bold');
    outSheet.setFrozenRows(1);
  } else {
    if (!outSheet) {
      outSheet = ss.insertSheet(SHEET_NAME);
      outSheet.getRange(1, 1, 1, 4).setValues([['시트', '셀', '열(헤더)', '현재값 → 기준범위']]);
      outSheet.getRange(1, 1, 1, 4).setFontWeight('bold');
      outSheet.setFrozenRows(1);
    }
  }

  const result = _scanValueInRangeViolations(startIdx);

  // 결과 기록
  if (result.violations.length > 0) {
    const rows = result.violations.map(v => {
      const m = v.match(/^(.+?)!([\w:$]+)\s+\[(.+?)\]:\s+"(.+?)"\s+→\s+(.+)$/);
      if (m) return [m[1], m[2], m[3], `"${m[4]}"  →  ${m[5]}`];
      return [v, '', '', ''];
    });
    const nextRow = outSheet.getLastRow() + 1;
    outSheet.getRange(nextRow, 1, rows.length, 4).setValues(rows);
    outSheet.autoResizeColumns(1, 4);
  }

  ss.setActiveSheet(outSheet);

  const processedCount = result.processed - startNum + 1;

  if (result.interrupted) {
    ui.alert(
      `⏱️ 시간 제한으로 중단\n\n` +
      `처리 탭: ${startNum}번 ~ ${result.processed}번 (${processedCount}개)\n` +
      `전체: ${result.totalSheets}개 중 ${result.processed}개 완료\n` +
      `이번 발견: ${result.violations.length}개\n\n` +
      `다시 실행 후 시작 탭 번호에 ${result.processed + 1} 입력`
    );
  } else {
    const totalRows = outSheet.getLastRow() - 1;
    ui.alert(
      `✅ 완료\n\n` +
      `처리 탭: ${startNum}번 ~ ${result.processed}번 (${processedCount}개)\n` +
      `총 위반 ${totalRows}개를 "_violations" 시트에 기록했습니다.`
    );
  }
}

// ── 내부 로직 ────────────────────────────────────────────────
// startIdx: 시작할 시트 인덱스 (0-based)
function _scanValueInRangeViolations(startIdx) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const EXCLUDED_SHEETS = new Set(['stringkey']);
  const allSheets = ss.getSheets().filter(s =>
    !s.getName().startsWith('_') && !EXCLUDED_SHEETS.has(s.getName())
  );
  const rangeCache = new Map();
  const violations = [];
  const EXCLUDED_HEADERS = new Set(['str_name', 'str_desc']);

  const totalSheets = allSheets.length;
  const startTime = Date.now();

  const sheets = allSheets.slice(startIdx);
  ss.toast(`${startIdx} / ${totalSheets} 시트`, '유효성 검사 중...', 60);

  let i = 0;  // 현재 순번 (continue 포함 모든 시트 카운트)
  for (const sheet of sheets) {
    const sheetName = sheet.getName();
    const currentNum = startIdx + i + 1;  // 1-based 탭 번호

    // 시간 체크: 루프 시작에서 (처리 전)
    if (Date.now() - startTime > MAX_EXEC_MS) {
      return {
        violations,
        processed: currentNum - 1,  // 마지막으로 완료한 탭 번호
        totalSheets,
        interrupted: true
      };
    }
    i++;

    let lastRow, lastCol;
    try {
      lastRow = sheet.getLastRow();
      lastCol = sheet.getLastColumn();
    } catch (e) {
      continue;
    }

    if (lastRow < 2 || lastCol < 1) continue;

    // 헤더 읽기
    let headers;
    try {
      headers = sheet.getRange(1, 1, 1, lastCol).getValues()[0]
                     .map(v => v.toString().trim());
    } catch (e) {
      continue;
    }

    // ① 데이터 행(최대 6행까지 스캔)에서 유효성 규칙 있는 첫 행 찾기
    let scanRow = null;
    try {
      for (let r = 2; r <= Math.min(6, lastRow); r++) {
        const rowRules = sheet.getRange(r, 1, 1, lastCol).getDataValidations()[0];
        if (rowRules.some(rule => rule !== null)) {
          scanRow = rowRules;
          break;
        }
      }
    } catch (e) {
      continue;
    }

    if (!scanRow) continue;

    // ② VALUE_IN_RANGE 열 추출 + 유효값 캐시 로드
    const rangeCols = [];
    for (let j = 0; j < scanRow.length; j++) {
      const rule = scanRow[j];
      if (!rule) continue;
      if (rule.getCriteriaType() !== SpreadsheetApp.DataValidationCriteria.VALUE_IN_RANGE) continue;

      const header = headers[j] || '';
      if (header.startsWith('_') || EXCLUDED_HEADERS.has(header)) continue;

      try {
        const ref = rule.getCriteriaValues()[0];
        const refSheet = ref.getSheet();

        // 열 전체 참조($B$2:$B)를 실제 마지막 행으로 잘라냄
        const refLastRow = Math.min(ref.getLastRow(), refSheet.getLastRow());
        const numRows = Math.max(1, refLastRow - ref.getRow() + 1);
        const clampedRef = refSheet.getRange(ref.getRow(), ref.getColumn(), numRows, ref.getNumColumns());
        const rangeKey = refSheet.getName() + '!' + clampedRef.getA1Notation();

        if (!rangeCache.has(rangeKey)) {
          const flat = clampedRef.getValues().flat().map(v => v.toString()).filter(v => v !== '');
          rangeCache.set(rangeKey, new Set(flat));
        }

        rangeCols.push({ colIndex: j, rangeKey, header });
      } catch (e) {
        // 참조 범위 접근 실패 시 건너뜀
      }
    }

    if (rangeCols.length === 0) continue;

    // ③ 시트 전체 값 읽기
    let values;
    try {
      values = sheet.getRange(2, 1, lastRow - 1, lastCol).getValues();
    } catch (e) {
      continue;
    }

    // ④ VALUE_IN_RANGE 열만 비교
    for (const { colIndex, rangeKey, header } of rangeCols) {
      const validSet = rangeCache.get(rangeKey);
      for (let i = 0; i < values.length; i++) {
        const val = values[i][colIndex];
        if (val === '' || val === null || val === undefined) continue;
        if (!validSet.has(val.toString())) {
          const cellA1 = sheet.getRange(i + 2, colIndex + 1).getA1Notation();
          violations.push(`${sheetName}!${cellA1} [${header}]: "${val}"  →  ${rangeKey}`);
        }
      }
    }

    ss.toast(`${currentNum} / ${totalSheets} 시트`, '유효성 검사 중...', 60);
  }

  ss.toast(`완료 (${totalSheets} 시트)`, '유효성 검사', 5);
  return { violations, processed: startIdx + i, totalSheets, interrupted: false };
}

// 모든 탭에서 N/A 오류 찾기
function findAllErrors() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheets = ss.getSheets();
  var errorTypes = ["#ERROR!", "#REF!", "#N/A"];
  var errorCells = [];

  for (var s = 0; s < sheets.length; s++) {
    var sheet = sheets[s];

    if (sheet.getName().startsWith("_")) {
      continue;
    }

    var range = sheet.getDataRange();
    var values = range.getValues();

    for (var i = 0; i < values.length; i++) {
      for (var j = 0; j < values[i].length; j++) {
        if (errorTypes.includes(values[i][j])) {
          var cellRef = sheet.getName() + "!" + range.getCell(i + 1, j + 1).getA1Notation();
          errorCells.push(cellRef + ": " + values[i][j]);
        }
      }
    }
  }

  if (errorCells.length > 0) {
    SpreadsheetApp.getUi().alert("Found errors in the following locations:\n" + errorCells.join('\n'));
  } else {
    SpreadsheetApp.getUi().alert("No errors found.");
  }
}
