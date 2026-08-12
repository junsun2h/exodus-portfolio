/**
 * StringKey 자동 생성 스크립트
 *
 * 기능: 원본 시트의 str_name, str_desc 값을 읽어서 stringkey 시트에 자동 추가
 * 공유 함수: findColumnIndex() → p1.gs
 */

// ========== 설정 ==========
const SK_CONFIG = {
  STRINGKEY_SHEET: 'stringkey',  // stringkey 시트 이름

  // 원본 탭 컬럼 (1-based)
  SOURCE_COLS: {
    str_name: 3,   // C열: str_name
    str_desc: 5,   // E열: str_desc
  },

  // stringkey 탭 컬럼 (1-based)
  STRINGKEY_COLS: {
    name: 2,        // B열: name (TEXTJOIN 수식)
    _src_name: 3,   // C열: _src_name (원본 name)
  }
};

// ========== 메뉴 함수 ==========

function processCurrentSheet() {
  const sheet = SpreadsheetApp.getActiveSheet();
  const sheetName = sheet.getName();

  // stringkey 시트에서는 동작하지 않음
  if (sheetName === SK_CONFIG.STRINGKEY_SHEET) {
    SpreadsheetApp.getUi().alert('⚠️ stringkey 시트에서는 사용할 수 없습니다.');
    return;
  }

  const ui = SpreadsheetApp.getUi();
  const response = ui.alert(
    '📊 StringKey 생성',
    `"${sheetName}" 시트의 str_name, str_desc 값을 읽어서\nstringkey 시트에 추가하시겠습니까?\n\n(이미 존재하는 키는 스킵됩니다)`,
    ui.ButtonSet.YES_NO
  );

  if (response !== ui.Button.YES) return;

  const result = processSheet(sheet);
  ui.alert(`✅ 처리 완료\n\n추가: ${result.created}개\n스킵 (이미 존재): ${result.skipped}개\n스킵 (빈 값): ${result.empty}개`);
}

function deleteUnusedStringKeys() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const stringKeySheet = ss.getSheetByName(SK_CONFIG.STRINGKEY_SHEET);

  if (!stringKeySheet) {
    SpreadsheetApp.getUi().alert(`❌ "${SK_CONFIG.STRINGKEY_SHEET}" 시트를 찾을 수 없습니다.`);
    return;
  }

  const ui = SpreadsheetApp.getUi();

  const lastRow = stringKeySheet.getLastRow();
  if (lastRow < 2) {
    ui.alert('⚠️ stringkey 시트에 데이터가 없습니다.');
    return;
  }

  const stringKeyData = stringKeySheet.getRange(2, 1, lastRow - 1, stringKeySheet.getLastColumn()).getValues();
  const totalItems = stringKeyData.length;
  const sheets = ss.getSheets();

  // 1단계: 모든 시트 데이터 한 번에 읽기
  showProgressDialog(0, 100, '⏳ 모든 시트 데이터 로딩 중...');

  const allSheetData = {};
  let loadedSheetCount = 0;

  for (let s = 0; s < sheets.length; s++) {
    const sheet = sheets[s];
    const sheetName = sheet.getName();

    if (sheetName === SK_CONFIG.STRINGKEY_SHEET || sheetName.startsWith('_')) {
      continue;
    }

    allSheetData[sheetName] = sheet.getDataRange().getValues();
    loadedSheetCount++;
    Logger.log(`✅ 로드됨: ${sheetName}`);
  }

  Logger.log(`⏳ 총 ${loadedSheetCount}개 시트 로드 완료`);

  // 2단계: StringKey 사용 여부 확인
  const unusedRows = [];

  for (let i = 0; i < stringKeyData.length; i++) {
    const nameValue = stringKeyData[i][SK_CONFIG.STRINGKEY_COLS.name - 1];

    if (!nameValue || nameValue === '') {
      continue;
    }

    let isUsed = false;

    for (const sheetName in allSheetData) {
      const dataStr = allSheetData[sheetName].map(row => row.join('||')).join('\n');
      if (dataStr.includes(nameValue)) {
        isUsed = true;
        break;
      }
    }

    if (!isUsed) {
      unusedRows.push({
        row: i + 2,
        name: nameValue,
        srcName: stringKeyData[i][SK_CONFIG.STRINGKEY_COLS._src_name - 1]
      });
    }

    if ((i + 1) % 100 === 0) {
      Logger.log(`⏳ 검색 진행 중: ${i + 1}/${totalItems}`);
    }
  }

  Logger.log(`⏳ 검색 완료: 사용되지 않는 항목 ${unusedRows.length}개 발견`);

  if (unusedRows.length === 0) {
    closeProgressDialog();
    ui.alert('✅ 사용되지 않는 StringKey가 없습니다.');
    return;
  }

  const itemList = unusedRows.map((row, idx) => `${idx + 1}. Row ${row.row}: ${row.name}`).join('\n');

  const html = HtmlService.createHtmlOutput(`
    <style>
      * { box-sizing: border-box; }
      body { font-family: "Segoe UI", Arial, sans-serif; padding: 20px; margin: 0; background: #f9f9f9; }
      h2 { color: #1f2937; margin: 0 0 10px 0; }
      .summary { background: #fff3cd; border-left: 4px solid #ffc107; padding: 12px; margin: 15px 0; border-radius: 4px; }
      .summary strong { color: #d39e00; }
      .list-container { border: 1px solid #e5e7eb; border-radius: 6px; background: white; margin: 15px 0; max-height: 350px; overflow-y: auto; }
      .list-item { padding: 8px 12px; border-bottom: 1px solid #f0f0f0; font-family: monospace; font-size: 12px; }
      .list-item:last-child { border-bottom: none; }
      .list-item:hover { background: #f0f7ff; }
      textarea { display: none; position: fixed; top: -9999px; }
      .buttons { margin-top: 15px; display: flex; gap: 10px; }
      button { flex: 1; padding: 12px; border: none; border-radius: 4px; font-size: 14px; font-weight: bold; cursor: pointer; transition: background 0.2s; }
      .delete-btn { background: #ea4335; color: white; }
      .delete-btn:hover { background: #d33425; }
      .copy-btn { background: #34a853; color: white; }
      .copy-btn:hover { background: #2d7a4a; }
      .cancel-btn { background: #dadce0; color: #3c4043; }
      .cancel-btn:hover { background: #c6c6c6; }
    </style>

    <h2>⚠️ 사용되지 않는 StringKey</h2>
    <div class="summary">총 <strong>${unusedRows.length}개</strong> 항목이 발견되었습니다. 삭제하시겠습니까?</div>

    <div class="list-container">
      ${unusedRows.map((row, idx) => `<div class="list-item">${idx + 1}. Row ${row.row}: ${row.name}</div>`).join('')}
    </div>

    <textarea id="itemListText">${itemList}</textarea>

    <div class="buttons">
      <button class="delete-btn" onclick="deleteItems()">🗑️ 삭제하기</button>
      <button class="copy-btn" onclick="copyList()">📋 목록 복사</button>
      <button class="cancel-btn" onclick="cancelDelete()">✕ 취소</button>
    </div>

    <script>
      function deleteItems() {
        google.script.run.confirmDeleteStringKeys(true);
        google.script.host.close();
      }

      function copyList() {
        const textarea = document.getElementById('itemListText');
        const text = textarea.value;

        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(text).then(showCopySuccess).catch(() => fallbackCopy(text));
        } else {
          fallbackCopy(text);
        }
      }

      function fallbackCopy(text) {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        try {
          document.execCommand('copy');
          showCopySuccess();
        } catch (err) {
          alert('복사 실패. 직접 선택해서 Ctrl+C로 복사하세요.');
        }
        document.body.removeChild(ta);
      }

      function showCopySuccess() {
        const btn = event.target;
        const originalText = btn.innerText;
        btn.innerText = '✅ 복사됨!';
        btn.style.background = '#188038';
        setTimeout(() => { btn.innerText = originalText; btn.style.background = '#34a853'; }, 2000);
      }

      function cancelDelete() {
        google.script.host.close();
      }
    </script>
  `);

  // 임시 데이터 저장 (콜백에서 사용)
  tempUnusedRows = unusedRows;
  tempStringKeySheet = stringKeySheet;

  html.setWidth(600).setHeight(500);
  ui.showModalDialog(html, '삭제 확인');
}

function showHelp() {
  const help = `
📚 StringKey 자동 생성 도구

🔧 기능
원본 시트의 str_name(C열), str_desc(E열) 값을 읽어서
stringkey 시트에 자동으로 행을 추가합니다.

📋 사용 방법
1. StringKey를 생성할 시트 탭을 선택
2. 메뉴 "🔑 StringKey 도구" → "📊 현재 시트 StringKey 생성"
3. 확인 클릭

📝 생성 결과 (stringkey 시트)
• _src_name(C열): 원본 name 값
• name(B열): TEXTJOIN 수식
  - str_name → =TEXTJOIN("_", TRUE, "stringkey", C행, "name")
  - str_desc → =TEXTJOIN("_", TRUE, "stringkey", C행, "desc")

⚙️ 설정 (SK_CONFIG)
• SOURCE_COLS.str_name: 원본 탭의 str_name 열 (기본: C열=3)
• SOURCE_COLS.str_desc: 원본 탭의 str_desc 열 (기본: E열=5)
• STRINGKEY_COLS.name: stringkey name 열 (기본: B열=2)
• STRINGKEY_COLS._src_name: stringkey _src_name 열 (기본: C열=3)

⚠️ 주의사항
• stringkey 시트에서는 사용 불가
• KR, EN 번역은 직접 입력
• 이미 존재하는 키는 중복 추가되지 않음
  `;

  SpreadsheetApp.getUi().alert(help);
}

// ========== 내부 로직 ==========

function processSheet(sheet) {
  const headers = sheet.getRange(1, 1, 1, sheet.getLastColumn()).getValues()[0];
  const lastRow = sheet.getLastRow();

  const nameColIndex = findColumnIndex(headers, 'name');
  const strNameColIndex = findColumnIndex(headers, 'str_name');
  const strDescColIndex = findColumnIndex(headers, 'str_desc');

  if (nameColIndex === -1) {
    SpreadsheetApp.getUi().alert('❌ "name" 컬럼을 찾을 수 없습니다.');
    return { created: 0, skipped: 0, empty: 0 };
  }

  if (strNameColIndex === -1) {
    SpreadsheetApp.getUi().alert('❌ "str_name" 컬럼을 찾을 수 없습니다.');
    return { created: 0, skipped: 0, empty: 0 };
  }

  const dataRange = sheet.getRange(2, 1, lastRow - 1, sheet.getLastColumn());
  const data = dataRange.getDisplayValues();

  let created = 0;
  let skipped = 0;
  let empty = 0;

  const existingKeys = getExistingStringKeys();
  const itemsToAdd = [];

  for (let i = 0; i < data.length; i++) {
    const row = data[i];
    const name = row[nameColIndex]?.toString().trim();
    const strName = row[strNameColIndex]?.toString().trim();
    const strDesc = strDescColIndex !== -1 ? row[strDescColIndex]?.toString().trim() : '';

    if (!name) {
      empty++;
      continue;
    }

    if (strName) {
      if (existingKeys.has(strName)) {
        skipped++;
      } else {
        itemsToAdd.push({ srcName: name, type: 'name' });
        existingKeys.add(strName);
        created++;
      }
    } else {
      empty++;
      continue;
    }

    if (strDesc) {
      if (existingKeys.has(strDesc)) {
        skipped++;
      } else {
        itemsToAdd.push({ srcName: name, type: 'desc' });
        existingKeys.add(strDesc);
        created++;
      }
    }
  }

  if (itemsToAdd.length > 0) {
    appendToStringKeySheet(itemsToAdd);
  }

  return { created, skipped, empty };
}

function getExistingStringKeys() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const stringKeySheet = ss.getSheetByName(SK_CONFIG.STRINGKEY_SHEET);

  if (!stringKeySheet) return new Set();

  const lastRow = stringKeySheet.getLastRow();
  if (lastRow < 2) return new Set();

  const keys = stringKeySheet.getRange(2, SK_CONFIG.STRINGKEY_COLS.name, lastRow - 1, 1).getDisplayValues().flat();
  return new Set(keys.filter(k => k));
}

function appendToStringKeySheet(items) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const stringKeySheet = ss.getSheetByName(SK_CONFIG.STRINGKEY_SHEET);

  if (!stringKeySheet) {
    SpreadsheetApp.getUi().alert(`❌ "${SK_CONFIG.STRINGKEY_SHEET}" 시트를 찾을 수 없습니다.`);
    return;
  }

  const startRow = stringKeySheet.getLastRow() + 1;
  const srcNameColLetter = getColumnLetter(SK_CONFIG.STRINGKEY_COLS._src_name);

  for (let i = 0; i < items.length; i++) {
    const item = items[i];
    const currentRow = startRow + i;

    stringKeySheet.getRange(currentRow, SK_CONFIG.STRINGKEY_COLS._src_name).setValue(item.srcName);

    const formula = `=TEXTJOIN("_", TRUE, "stringkey", ${srcNameColLetter}${currentRow}, "${item.type}")`;
    stringKeySheet.getRange(currentRow, SK_CONFIG.STRINGKEY_COLS.name).setFormula(formula);
  }
}

function getColumnLetter(columnNumber) {
  let letter = '';
  let num = columnNumber;
  while (num > 0) {
    const remainder = (num - 1) % 26;
    letter = String.fromCharCode(65 + remainder) + letter;
    num = Math.floor((num - 1) / 26);
  }
  return letter;
}

// ========== 진행 다이얼로그 ==========

function showProgressDialog(progress, total, message) {
  const html = HtmlService.createHtmlOutput(
    '<style>' +
    'body { font-family: "Segoe UI", Arial, sans-serif; padding: 20px; text-align: center; background: #f9f9f9; margin: 0; } ' +
    'h2 { color: #1f2937; margin: 0 0 15px 0; font-size: 18px; } ' +
    '.progress-container { width: 100%; background: #e5e7eb; border-radius: 10px; margin: 15px 0; height: 40px; overflow: hidden; } ' +
    '.progress-bar { width: ' + progress + '%; height: 100%; background: linear-gradient(90deg, #4285f4, #1967d2); border-radius: 10px; display: flex; align-items: center; justify-content: center; color: white; font-weight: bold; font-size: 12px; } ' +
    '.progress-text { margin-top: 10px; font-size: 20px; font-weight: bold; color: #4285f4; } ' +
    '.message { margin-top: 12px; font-size: 14px; color: #374151; word-wrap: break-word; min-height: 20px; } ' +
    '.details { margin-top: 10px; font-size: 12px; color: #9ca3af; } ' +
    '</style>' +
    '<h2>⏳ StringKey 처리 중...</h2>' +
    '<div class="progress-container"><div class="progress-bar">' + progress + '%</div></div>' +
    '<div class="progress-text">' + progress + '%</div>' +
    '<div class="message">' + message + '</div>' +
    '<div class="details">총 ' + total + '개 항목 처리 중...</div>'
  ).setWidth(350).setHeight(200);

  SpreadsheetApp.getUi().showModelessDialog(html, '⏳ 처리 진행 상황');
}

function closeProgressDialog() {
  // Modeless Dialog는 자동으로 닫힘
}

// ========== 삭제 콜백 ==========

// 전역: 임시 데이터 저장
var tempUnusedRows = [];
var tempStringKeySheet = null;

function confirmDeleteStringKeys(shouldDelete) {
  if (!shouldDelete || tempUnusedRows.length === 0 || !tempStringKeySheet) {
    return;
  }

  const deletedCount = tempUnusedRows.length;

  // 뒤에서부터 삭제 (행 번호가 밀리지 않도록)
  Logger.log(`🗑️ 삭제 시작: ${deletedCount}개 항목`);

  for (let i = tempUnusedRows.length - 1; i >= 0; i--) {
    tempStringKeySheet.deleteRow(tempUnusedRows[i].row);

    if ((tempUnusedRows.length - i) % 10 === 0) {
      Logger.log(`🗑️ 삭제 중: ${tempUnusedRows.length - i}/${tempUnusedRows.length}`);
    }
  }

  Logger.log(`✅ 삭제 완료: ${deletedCount}개 항목 삭제됨`);

  tempUnusedRows = [];
  tempStringKeySheet = null;

  SpreadsheetApp.getUi().alert(`✅ 삭제 완료\n\n삭제된 항목: ${deletedCount}개\n\n📋 상세 로그는 Apps Script 콘솔 확인`);
}

// ========== 수식 복구 ==========
// 정렬 등으로 수식이 값으로 덮인 경우 복구
// - name 열: _name/_desc 접미사 기준으로 TEXTJOIN 수식 재설정
// - EN 열: KR 열 기준으로 GOOGLETRANSLATE 수식 재설정

function restoreStringKeyFormulas() {
  const sheet = SpreadsheetApp.getActiveSheet();

  if (sheet.getName() !== SK_CONFIG.STRINGKEY_SHEET) {
    SpreadsheetApp.getUi().alert(`⚠️ "${SK_CONFIG.STRINGKEY_SHEET}" 시트에서만 사용할 수 있습니다.`);
    return;
  }

  const lastRow = sheet.getLastRow();
  if (lastRow < 2) {
    SpreadsheetApp.getUi().alert('⚠️ 데이터가 없습니다.');
    return;
  }

  const data = sheet.getRange(1, 1, lastRow, sheet.getLastColumn()).getValues();
  const headers = data[0];

  const nameColIndex    = findColumnIndex(headers, 'name');
  const srcNameColIndex = findColumnIndex(headers, '_src_name');
  const krColIndex      = findColumnIndex(headers, 'KR');
  const enColIndex      = findColumnIndex(headers, 'EN');

  if (nameColIndex === -1 || srcNameColIndex === -1) {
    SpreadsheetApp.getUi().alert('❌ name 또는 _src_name 컬럼을 찾을 수 없습니다.');
    return;
  }

  const srcNameColLetter = getColumnLetter(srcNameColIndex + 1);
  const krColLetter      = krColIndex !== -1 ? getColumnLetter(krColIndex + 1) : null;
  const nameColLetter    = getColumnLetter(nameColIndex + 1);
  const enColLetter      = enColIndex !== -1 ? getColumnLetter(enColIndex + 1) : null;

  const ui = SpreadsheetApp.getUi();
  let info = `시트: ${sheet.getName()}\n\n복구 항목:\n`;
  info += `• name(${nameColLetter}열) → TEXTJOIN 수식 (_name / _desc 기준)\n`;
  if (enColIndex !== -1 && krColLetter) {
    info += `• EN(${enColLetter}열) → GOOGLETRANSLATE(${krColLetter}열, "ko", "en")\n`;
  } else {
    info += `• EN 열 또는 KR 열을 찾을 수 없어 GOOGLETRANSLATE 복구는 건너뜁니다.\n`;
  }
  info += `\n대상 행: ${lastRow - 1}개\n수식 복구를 진행하시겠습니까?`;

  if (ui.alert('🔧 수식 복구', info, ui.ButtonSet.YES_NO) !== ui.Button.YES) return;

  // name 열 TEXTJOIN 수식 복구
  let nameCount = 0;
  const nameFormulas = [];
  for (let i = 1; i < lastRow; i++) {
    const nameValue = (data[i][nameColIndex] || '').toString().trim();
    const row = i + 1;

    if (nameValue.endsWith('_name')) {
      nameFormulas.push([`=TEXTJOIN("_", TRUE, "stringkey", ${srcNameColLetter}${row}, "name")`]);
      nameCount++;
    } else if (nameValue.endsWith('_desc')) {
      nameFormulas.push([`=TEXTJOIN("_", TRUE, "stringkey", ${srcNameColLetter}${row}, "desc")`]);
      nameCount++;
    } else {
      nameFormulas.push(['']);  // 빈 행 (이미 비어있으므로 클리어 무방)
    }
  }
  sheet.getRange(2, nameColIndex + 1, lastRow - 1, 1).setFormulas(nameFormulas);

  // EN 열 GOOGLETRANSLATE 수식 복구
  let enCount = 0;
  if (enColIndex !== -1 && krColLetter) {
    const enFormulas = [];
    for (let i = 1; i < lastRow; i++) {
      const srcName = (data[i][srcNameColIndex] || '').toString().trim();
      const row = i + 1;

      if (srcName) {
        enFormulas.push([`=GOOGLETRANSLATE(${krColLetter}${row}, "ko", "en")`]);
        enCount++;
      } else {
        enFormulas.push(['']);
      }
    }
    sheet.getRange(2, enColIndex + 1, lastRow - 1, 1).setFormulas(enFormulas);
  }

  ui.alert(`✅ 수식 복구 완료\n\n• name TEXTJOIN: ${nameCount}개\n• EN GOOGLETRANSLATE: ${enCount}개`);
}
