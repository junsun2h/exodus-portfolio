// ===== Enum Index 관리 =====

// ── 내부 유틸 ──────────────────────────────────────────────

// 배열에서 중복된 요소 찾기
function findDuplicates(arr) {
  const seen = new Map();
  const duplicates = [];

  arr.forEach(item => {
    if (!isNaN(item) && seen.has(item)) {
      duplicates.push(item);
    } else {
      seen.set(item, true);
    }
  });

  return [...new Set(duplicates)];
}

// 시트별 Property Key
function getSheetPropertyKey() {
  const sheet = SpreadsheetApp.getActiveSheet();
  const sheetId = sheet.getSheetId();
  return `lastEnumIndex_${sheetId}`;
}

// 현재 시트의 lastEnumIndex 가져오기
function getLastEnumIndex() {
  const key = getSheetPropertyKey();
  const value = PropertiesService.getScriptProperties().getProperty(key);
  return value ? parseInt(value) : null;
}

// 현재 시트의 lastEnumIndex 저장
function setLastEnumIndex(index) {
  const key = getSheetPropertyKey();
  PropertiesService.getScriptProperties().setProperty(key, index.toString());
  const sheet = SpreadsheetApp.getActiveSheet();
  console.log(`[${sheet.getName()}] lastEnumIndex 저장: ${index} (Key: ${key})`);
}

// 그룹 추출 (name 문자열의 앞 2~3 세그먼트)
function extractGroup(name) {
  if (!name) return '';

  let cleanName = name.toString().replace(/^stringkey_/, '');
  const parts = cleanName.split('_');

  if (parts.length >= 3) {
    return parts.slice(0, 3).join('_');
  } else if (parts.length >= 2) {
    return parts.slice(0, 2).join('_');
  }

  return parts[0] || '';
}

// ── 메뉴 함수 ──────────────────────────────────────────────

// 신규 항목 enum_index 할당 및 검증
function assignNewEnumIndexAndValidate() {
  const sheet = SpreadsheetApp.getActiveSheet();
  const data = sheet.getDataRange().getValues();

  console.log('=== 스크립트 시작 ===');
  console.log(`시트 이름: ${sheet.getName()}`);
  console.log(`시트 ID: ${sheet.getSheetId()}`);
  console.log(`전체 행 수: ${data.length}`);
  console.log(`전체 열 수: ${data[0].length}`);

  if (data.length === 0) {
    SpreadsheetApp.getUi().alert('데이터가 없습니다.');
    return;
  }

  const headerRow = data[0];
  const nameColIndex = findColumnIndex(headerRow, 'name');
  const enumIndexColIndex = findColumnIndex(headerRow, 'enum_index');

  console.log('헤더 행:', headerRow);
  console.log(`name 컬럼 인덱스: ${nameColIndex}`);
  console.log(`enum_index 컬럼 인덱스: ${enumIndexColIndex}`);

  if (nameColIndex === -1 || enumIndexColIndex === -1) {
    SpreadsheetApp.getUi().alert(
      `필수 컬럼을 찾을 수 없습니다.\n\n` +
      `name 컬럼: ${nameColIndex === -1 ? '❌ 없음' : '✅ ' + (nameColIndex + 1) + '열'}\n` +
      `enum_index 컬럼: ${enumIndexColIndex === -1 ? '❌ 없음' : '✅ ' + (enumIndexColIndex + 1) + '열'}\n\n` +
      `헤더 행에 'name'과 'enum_index' 컬럼이 있는지 확인하세요.`
    );
    return;
  }

  const existingIndexes = new Map();
  let maxIndex = 0;

  console.log('\n=== 1단계: 기존 enum_index 분석 ===');

  for (let i = 1; i < data.length; i++) {
    const name = data[i][nameColIndex];
    const enumIndex = data[i][enumIndexColIndex];

    if (enumIndex && enumIndex !== '' && !isNaN(enumIndex)) {
      const index = parseInt(enumIndex);
      maxIndex = Math.max(maxIndex, index);

      if (!existingIndexes.has(index)) {
        existingIndexes.set(index, []);
      }
      existingIndexes.get(index).push({
        row: i + 1,
        name: name || '(이름 없음)'
      });
    }
  }

  console.log(`발견된 최대 enum_index: ${maxIndex}`);

  let lastIndex = getLastEnumIndex();

  console.log(`\n=== 2단계: lastEnumIndex 결정 ===`);
  console.log(`저장된 lastEnumIndex: ${lastIndex}`);

  if (!lastIndex || isNaN(lastIndex) || lastIndex < maxIndex) {
    lastIndex = maxIndex;
    console.log(`최대값 사용: ${lastIndex}`);
  }

  const newAssignments = [];
  let assignmentCount = 0;

  console.log('\n=== 3단계: 신규 항목 할당 ===');

  for (let i = 1; i < data.length; i++) {
    const name = data[i][nameColIndex];
    const enumIndex = data[i][enumIndexColIndex];

    const hasName = name && name.toString().trim() !== '';
    const hasEnumIndex = enumIndex && enumIndex !== '';

    if (hasName && !hasEnumIndex) {
      lastIndex++;

      while (existingIndexes.has(lastIndex)) {
        console.log(`  경고: ${lastIndex}는 이미 존재. 건너뜀.`);
        lastIndex++;
      }

      sheet.getRange(i + 1, enumIndexColIndex + 1).setValue(lastIndex);

      newAssignments.push({
        row: i + 1,
        name: name,
        index: lastIndex
      });

      existingIndexes.set(lastIndex, [{
        row: i + 1,
        name: name
      }]);

      assignmentCount++;
      console.log(`  ✅ Row ${i + 1}: "${name}" -> ${lastIndex} 할당`);
    }
  }

  console.log(`\n총 ${assignmentCount}개 항목 할당 완료`);

  console.log('\n=== 4단계: 중복 검증 ===');
  const duplicates = [];

  existingIndexes.forEach((items, index) => {
    if (items.length > 1) {
      duplicates.push({
        index: index,
        items: items
      });
    }
  });

  if (assignmentCount > 0) {
    setLastEnumIndex(lastIndex);
  }

  let message = `=== 처리 결과 (시트: ${sheet.getName()}) ===\n\n`;
  message += `컬럼 위치: name=${String.fromCharCode(65 + nameColIndex)}열, enum_index=${String.fromCharCode(65 + enumIndexColIndex)}열\n\n`;

  if (assignmentCount > 0) {
    message += `✅ 신규 할당: ${assignmentCount}개 항목\n`;
    message += `   마지막 enum_index: ${lastIndex}\n\n`;

    if (assignmentCount <= 10) {
      message += '할당 내역:\n';
      newAssignments.forEach(item => {
        message += `   Row ${item.row}: ${item.name} -> ${item.index}\n`;
      });
      message += '\n';
    } else {
      message += `처음 5개 할당 내역:\n`;
      for (let i = 0; i < 5; i++) {
        const item = newAssignments[i];
        message += `   Row ${item.row}: ${item.name} -> ${item.index}\n`;
      }
      message += `   ... 외 ${assignmentCount - 5}개 더\n\n`;
    }
  } else {
    message += '⚠️ 할당할 신규 항목 없음\n\n';
    message += '가능한 원인:\n';
    message += `1. name 컬럼(${String.fromCharCode(65 + nameColIndex)}열)에 데이터가 없음\n`;
    message += '2. 모든 행에 이미 enum_index가 할당되어 있음\n\n';
    message += 'Apps Script 로그를 확인하세요:\n';
    message += '확장 프로그램 > Apps Script > 실행 로그 보기';
  }

  if (duplicates.length > 0) {
    message += `\n⚠️ 중복 발견: ${duplicates.length}개 enum_index\n\n`;

    const displayCount = Math.min(duplicates.length, 5);
    for (let i = 0; i < displayCount; i++) {
      const dup = duplicates[i];
      message += `중복 Index ${dup.index}:\n`;
      dup.items.forEach(item => {
        message += `   - Row ${item.row}: ${item.name}\n`;
      });
      message += '\n';
    }

    if (duplicates.length > 5) {
      message += `... 외 ${duplicates.length - 5}개 더 있음\n`;
    }
  } else {
    message += '\n✅ 중복 없음: 모든 enum_index가 고유함';
  }

  if (assignmentCount > 0) {
    message += `\n\n✅ 이 시트의 lastEnumIndex 저장 완료: ${lastIndex}`;
  }

  SpreadsheetApp.getUi().alert(message);
  console.log('=== 처리 완료 ===');
}

// 모든 탭에서 중복된 enum_index 찾기
function checkDuplicatesInAllSheets() {
  const spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
  const sheets = spreadsheet.getSheets();
  let message = '';
  let duplicatesFound = false;

  sheets.forEach(sheet => {
    const data = sheet.getDataRange().getValues();
    let enumIndexColumn = data[0].findIndex(cell => cell === 'enum_index');

    if (enumIndexColumn !== -1) {
      const enumIndexValues = data.map(row => row[enumIndexColumn]).slice(1).filter(value => value !== "" && value !== null && value !== undefined);
      const duplicates = findDuplicates(enumIndexValues);

      if (duplicates.length > 0) {
        duplicatesFound = true;
        message += `시트 "${sheet.getName()}"에서 중복된 'enum_index' 숫자 발견: ${duplicates.join(', ')}\n`;
      }
    }
  });

  if (!duplicatesFound) {
    message = "모든 시트에서 중복된 'enum_index' 숫자 없음.";
  }

  SpreadsheetApp.getUi().alert(message);
}

// 현재 탭에서 중복된 enum_index 찾기
function checkDuplicateInActiveSheet() {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getActiveSheet();
  const data = sheet.getDataRange().getValues();
  let enumIndexColumn = -1;

  if (data.length > 0) {
    enumIndexColumn = data[0].findIndex(cell => cell === 'enum_index');
  }

  if (enumIndexColumn !== -1) {
    const enumIndexValues = data.map(row => row[enumIndexColumn]).slice(1).filter(value => value !== "" && value !== null && value !== undefined);
    const duplicates = findDuplicates(enumIndexValues);

    const ui = SpreadsheetApp.getUi();
    let message = '';
    if (duplicates.length > 0) {
      message = `활성 시트 "${sheet.getName()}"에서 중복된 'enum_index' 숫자 발견: ${duplicates.join(', ')}`;
    } else {
      message = `활성 시트 "${sheet.getName()}"에서 중복된 'enum_index' 숫자 없음.`;
    }
    ui.alert(message);
  } else {
    SpreadsheetApp.getUi().alert("'enum_index' 열을 찾을 수 없습니다.");
  }
}

// 현재 시트 상태 확인
function checkCurrentStatus() {
  const sheet = SpreadsheetApp.getActiveSheet();
  const data = sheet.getDataRange().getValues();

  if (data.length === 0) {
    SpreadsheetApp.getUi().alert('데이터가 없습니다.');
    return;
  }

  const headerRow = data[0];
  const nameColIndex = findColumnIndex(headerRow, 'name');
  const enumIndexColIndex = findColumnIndex(headerRow, 'enum_index');

  console.log('=== 상태 확인 시작 ===');
  console.log(`시트: ${sheet.getName()} (ID: ${sheet.getSheetId()})`);
  console.log(`전체 행: ${data.length}, 전체 열: ${data[0].length}`);
  console.log(`name 컬럼: ${nameColIndex}, enum_index 컬럼: ${enumIndexColIndex}`);

  if (nameColIndex === -1 || enumIndexColIndex === -1) {
    SpreadsheetApp.getUi().alert(
      `필수 컬럼을 찾을 수 없습니다.\n\n` +
      `name 컬럼: ${nameColIndex === -1 ? '❌ 없음' : '✅ 찾음'}\n` +
      `enum_index 컬럼: ${enumIndexColIndex === -1 ? '❌ 없음' : '✅ 찾음'}`
    );
    return;
  }

  let totalRows = 0;
  let hasName = 0;
  let hasEnumIndex = 0;
  let emptyEnumIndex = 0;
  let maxIndex = 0;

  for (let i = 1; i < data.length; i++) {
    const name = data[i][nameColIndex];
    const enumIndex = data[i][enumIndexColIndex];

    totalRows++;

    if (name && name.toString().trim() !== '') {
      hasName++;

      if (enumIndex && enumIndex !== '') {
        hasEnumIndex++;
        maxIndex = Math.max(maxIndex, parseInt(enumIndex) || 0);
      } else {
        emptyEnumIndex++;
      }
    }
  }

  const savedLastIndex = getLastEnumIndex();

  const message = `=== 현재 상태 (시트: ${sheet.getName()}) ===\n\n` +
                  `컬럼 위치:\n` +
                  `  name: ${String.fromCharCode(65 + nameColIndex)}열\n` +
                  `  enum_index: ${String.fromCharCode(65 + enumIndexColIndex)}열\n\n` +
                  `데이터 상태:\n` +
                  `  총 데이터 행: ${totalRows}\n` +
                  `  name이 있는 행: ${hasName}\n` +
                  `  enum_index가 있는 행: ${hasEnumIndex}\n` +
                  `  enum_index가 비어있는 행: ${emptyEnumIndex}\n` +
                  `  현재 최대 enum_index: ${maxIndex}\n\n` +
                  `저장된 값:\n` +
                  `  이 시트의 lastEnumIndex: ${savedLastIndex || '없음'}`;

  SpreadsheetApp.getUi().alert(message);
  console.log(`Property Key: ${getSheetPropertyKey()}`);
}

// ── 디버그 / 내부 도구 ──────────────────────────────────────

// enum_index 전체 재정렬 (메뉴 미노출)
function redistributeEnumIndex() {
  const sheet = SpreadsheetApp.getActiveSheet();
  const data = sheet.getDataRange().getValues();

  if (data.length === 0 || data[0].length < 5) {
    SpreadsheetApp.getUi().alert('데이터 형식이 올바르지 않습니다.');
    return;
  }

  const START_INDEX = 10000;
  const GROUP_INCREMENT = 1000;
  const EMPTY_ROW_INCREMENT = 100;
  const ITEM_INCREMENT = 1;

  let currentIndex = START_INDEX;
  let previousGroup = '';
  let wasEmptyRow = false;
  let updatedCount = 0;

  for (let i = 1; i < data.length; i++) {
    const name = data[i][1];

    if (!name || name.toString().trim() === '') {
      wasEmptyRow = true;
      continue;
    }

    const group = extractGroup(name);

    if (previousGroup === '') {
      currentIndex = START_INDEX;
    } else if (group !== previousGroup) {
      currentIndex = Math.ceil(currentIndex / GROUP_INCREMENT) * GROUP_INCREMENT + GROUP_INCREMENT;
    } else if (wasEmptyRow) {
      currentIndex = Math.ceil(currentIndex / EMPTY_ROW_INCREMENT) * EMPTY_ROW_INCREMENT + EMPTY_ROW_INCREMENT;
    } else {
      currentIndex += ITEM_INCREMENT;
    }

    sheet.getRange(i + 1, 4).setValue(currentIndex);
    updatedCount++;

    previousGroup = group;
    wasEmptyRow = false;
  }

  PropertiesService.getScriptProperties().setProperty('lastEnumIndex', currentIndex.toString());

  const message = `재정렬 완료!\n\n` +
                  `- 처리된 항목: ${updatedCount}개\n` +
                  `- 시작 인덱스: ${START_INDEX}\n` +
                  `- 마지막 인덱스: ${currentIndex}\n` +
                  `- Script Properties 저장 완료`;

  SpreadsheetApp.getUi().alert(message);
}

// 현재 데이터 분석 (디버그용)
function analyzeCurrentData() {
  const sheet = SpreadsheetApp.getActiveSheet();
  const data = sheet.getDataRange().getValues();

  const groups = new Map();
  let emptyRows = 0;

  for (let i = 1; i < data.length; i++) {
    const name = data[i][1];

    if (!name || name.toString().trim() === '') {
      emptyRows++;
      continue;
    }

    const group = extractGroup(name);
    if (!groups.has(group)) {
      groups.set(group, []);
    }
    groups.get(group).push({
      row: i + 1,
      name: name,
      currentIndex: data[i][3]
    });
  }

  console.log('=== 데이터 분석 결과 ===');
  console.log(`총 행 수: ${data.length - 1}`);
  console.log(`빈 행 수: ${emptyRows}`);
  console.log(`그룹 수: ${groups.size}`);

  groups.forEach((items, groupName) => {
    console.log(`${groupName}: ${items.length}개`);
  });
}

// 모든 시트의 저장된 값 확인 (디버그용)
function debugShowAllSheetProperties() {
  const spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
  const sheets = spreadsheet.getSheets();
  const properties = PropertiesService.getScriptProperties();

  let message = '=== 모든 시트의 lastEnumIndex ===\n\n';

  sheets.forEach(sheet => {
    const sheetId = sheet.getSheetId();
    const key = `lastEnumIndex_${sheetId}`;
    const value = properties.getProperty(key);

    message += `시트: ${sheet.getName()}\n`;
    message += `  ID: ${sheetId}\n`;
    message += `  저장된 값: ${value || '없음'}\n\n`;
  });

  SpreadsheetApp.getUi().alert(message);
}
