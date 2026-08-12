// ===== 정렬 =====

// name 열 기준 이름순 정렬
function sortByName() {
  var sheet = SpreadsheetApp.getActiveSheet();
  var lastRow = sheet.getLastRow();
  var lastCol = sheet.getLastColumn();

  if (lastRow <= 1) {
    SpreadsheetApp.getUi().alert('정렬할 데이터가 없습니다.');
    return;
  }

  var allRange = sheet.getRange(1, 1, lastRow, lastCol);
  var data = allRange.getValues();
  var formulas = allRange.getFormulas();  // 수식 별도 읽기

  var headerRow = data[0];
  var colCount = lastCol;
  var nameColIndex = -1;

  for (var i = 0; i < headerRow.length; i++) {
    if (headerRow[i] === 'name') {
      nameColIndex = i;
      break;
    }
  }
  if (nameColIndex === -1) nameColIndex = 1;

  // 정렬 전 카운팅
  var beforeTotal = lastRow - 1;
  var beforeDataCount = 0;
  var beforeEmptyCount = 0;
  for (var i = 1; i < data.length; i++) {
    var name = (data[i][nameColIndex] || '').toString().trim();
    if (name !== '') beforeDataCount++;
    else beforeEmptyCount++;
  }

  // 데이터 행 추출 (원래 시트 행 번호 포함)
  var dataRows = [];
  for (var i = 1; i < data.length; i++) {
    var name = (data[i][nameColIndex] || '').toString().trim();
    if (name !== '') {
      dataRows.push({
        values: data[i],
        formulas: formulas[i],
        originalSheetRow: i + 1  // 1-based 시트 행 번호 (수식 row 참조 업데이트용)
      });
    }
  }

  if (dataRows.length === 0) {
    SpreadsheetApp.getUi().alert('정렬할 데이터가 없습니다.');
    return;
  }

  var ui = SpreadsheetApp.getUi();
  var confirmResult = ui.alert(
    '정렬 전 카운팅 확인',
    '시트: ' + sheet.getName() + '\n\n' +
    '=== 정렬 전 현황 ===\n' +
    '전체 행 (헤더 제외): ' + beforeTotal + '행\n' +
    'name이 있는 데이터 행: ' + beforeDataCount + '개\n' +
    'name이 비어있는 행: ' + beforeEmptyCount + '개\n\n' +
    '정렬 대상: ' + dataRows.length + '개\n\n' +
    '정렬을 진행하시겠습니까?',
    ui.ButtonSet.YES_NO
  );

  if (confirmResult !== ui.Button.YES) {
    ui.alert('정렬이 취소되었습니다.');
    return;
  }

  // 정렬
  dataRows.sort(function(a, b) {
    var valA = (a.values[nameColIndex] || '').toString();
    var valB = (b.values[nameColIndex] || '').toString();
    return valA.localeCompare(valB, 'ko');
  });

  // 카테고리 구분 빈 행 삽입
  var outputRows = [];  // null = 빈 구분 행
  var prevCategory = getSortCategory(dataRows[0].values[nameColIndex]);
  outputRows.push(dataRows[0]);
  var categoryCount = 1;

  for (var i = 1; i < dataRows.length; i++) {
    var curCategory = getSortCategory(dataRows[i].values[nameColIndex]);
    if (curCategory !== prevCategory) {
      outputRows.push(null);
      categoryCount++;
    }
    outputRows.push(dataRows[i]);
    prevCategory = curCategory;
  }

  var numOutputRows = outputRows.length;

  // 기존 데이터 클리어
  if (lastRow > 1) {
    var clearRange = sheet.getRange(2, 1, lastRow - 1, colCount);
    clearRange.clearDataValidations();
    clearRange.clearContent();
  }

  // 값 일괄 쓰기
  var valMatrix = [];
  for (var i = 0; i < numOutputRows; i++) {
    var row = outputRows[i];
    valMatrix.push(row ? row.values.slice(0, colCount) : new Array(colCount).fill(''));
  }
  sheet.getRange(2, 1, numOutputRows, colCount).setValues(valMatrix);

  // 수식 복원: 열 단위로 연속 블록을 모아 일괄 쓰기
  var colFormulaMap = {};
  for (var i = 0; i < numOutputRows; i++) {
    var row = outputRows[i];
    if (!row) continue;
    var targetSheetRow = 2 + i;

    for (var j = 0; j < colCount; j++) {
      if (row.formulas[j]) {
        var updated = replaceRowInFormula(row.formulas[j], row.originalSheetRow, targetSheetRow);
        if (!colFormulaMap[j]) colFormulaMap[j] = [];
        colFormulaMap[j].push({ sheetRow: targetSheetRow, formula: updated });
      }
    }
  }

  for (var colIdx in colFormulaMap) {
    var cells = colFormulaMap[colIdx];
    var blockStart = 0;
    while (blockStart < cells.length) {
      var blockEnd = blockStart;
      while (blockEnd + 1 < cells.length &&
             cells[blockEnd + 1].sheetRow === cells[blockEnd].sheetRow + 1) {
        blockEnd++;
      }
      var block = cells.slice(blockStart, blockEnd + 1);
      var blockFormulas = block.map(function(c) { return [c.formula]; });
      sheet.getRange(block[0].sheetRow, parseInt(colIdx) + 1, block.length, 1).setFormulas(blockFormulas);
      blockStart = blockEnd + 1;
    }
  }

  // 정렬 후 검증
  var afterData = sheet.getDataRange().getValues();
  var afterDataCount = 0;
  for (var i = 1; i < afterData.length; i++) {
    if ((afterData[i][nameColIndex] || '').toString().trim() !== '') afterDataCount++;
  }

  var matchStatus = (beforeDataCount === afterDataCount) ? 'OK' : 'MISMATCH!';

  ui.alert(
    '정렬 완료!\n\n' +
    '시트: ' + sheet.getName() + '\n' +
    '정렬 기준: ' + String.fromCharCode(65 + nameColIndex) + '열 (name)\n' +
    '정렬 순서: A→Z, 가→하\n\n' +
    '=== 카운팅 비교 ===\n' +
    '정렬 전 데이터 행: ' + beforeDataCount + '개\n' +
    '정렬 후 데이터 행: ' + afterDataCount + '개\n' +
    '카운팅 검증: ' + matchStatus + '\n\n' +
    '카테고리 그룹: ' + categoryCount + '개\n' +
    '삽입된 빈 행: ' + (numOutputRows - dataRows.length) + '개'
  );
}

// 수식 내 행 번호 교체 (예: C2942 → C1500)
function replaceRowInFormula(formula, oldRow, newRow) {
  if (!formula || oldRow === newRow) return formula;
  // 열 문자 + 정확한 행 번호 (뒤에 숫자 없을 때만) 교체
  var re = new RegExp('([A-Za-z]+)(' + oldRow + ')(?!\\d)', 'g');
  return formula.replace(re, function(match, col) {
    return col + newRow;
  });
}

// 정렬용 카테고리 추출
// stringkey_awaken_grade_... → awaken
// stringkey_content_gacha_... → content
// some_other_name → some
function getSortCategory(name) {
  if (!name) return '';
  var str = name.toString().trim();

  if (str.indexOf('stringkey_') === 0) {
    str = str.substring('stringkey_'.length);
  }

  var underscoreIndex = str.indexOf('_');
  if (underscoreIndex !== -1) {
    return str.substring(0, underscoreIndex);
  }
  return str;
}
