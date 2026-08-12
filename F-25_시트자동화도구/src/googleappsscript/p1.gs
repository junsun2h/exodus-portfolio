// ===== 공유 유틸리티 =====
// 모든 기능 파일에서 공통으로 사용

// 헤더에서 컬럼 인덱스 찾기 (null-safe, 대소문자 무시)
function findColumnIndex(headers, columnName) {
  for (let i = 0; i < headers.length; i++) {
    if (headers[i] && headers[i].toString().toLowerCase() === columnName.toLowerCase()) {
      return i;
    }
  }
  return -1;
}

// 텍스트 정리
function cleanText(text) {
  return text.replace(/\s*\{value[12]\}%?\s*/g, ' ').replace(/\s+/g, ' ').trim();
}

// ===== 메인 메뉴 =====
function onOpen() {
  var ui = SpreadsheetApp.getUi();

  ui.createMenu('P1 메뉴')
      .addItem('현재 시트 상태 확인', 'checkCurrentStatus')
      .addSeparator()

      // 📋 데이터 검증 (서브메뉴)
      .addSubMenu(ui.createMenu('📋 데이터 검증')
          .addItem('선택한 셀에서 중복 찾기', 'showDuplicatesInSelectedRange')
          .addItem('모든 탭에서 N/A 오류 찾기', 'findAllErrors')
          .addItem('모든 탭에서 유효성 위반 오류 찾기', 'findAllValidationViolations'))

      .addSeparator()

      // 🔢 Enum Index 관리 (서브메뉴)
      .addSubMenu(ui.createMenu('🔢 Enum Index 관리')
          .addItem('신규 항목 enum_index 할당 및 검증', 'assignNewEnumIndexAndValidate')
          .addItem('모든 탭에서 중복된 enum_index 찾기', 'checkDuplicatesInAllSheets')
          .addItem('현재 탭에서 중복된 enum_index 찾기', 'checkDuplicateInActiveSheet'))

      .addSeparator()

      // 🔤 정렬 (서브메뉴)
      .addSubMenu(ui.createMenu('🔤 정렬')
          .addItem('name 열 기준 이름순 정렬', 'sortByName'))

      .addSeparator()

      // 💾 백업 및 내보내기 (서브메뉴)
      .addSubMenu(ui.createMenu('💾 백업 및 내보내기')
          .addItem('구글 시트 백업', 'backupSpreadSheet')
          .addItem('모든 시트 ZIP으로 내보내기', 'exportAllSheetsAsZIP'))

      .addSeparator()

      // 🔑 StringKey 도구 (서브메뉴)
      .addSubMenu(ui.createMenu('🔑 StringKey 도구')
          .addItem('📊 현재 시트 StringKey 생성', 'processCurrentSheet')
          .addItem('🗑️ 사용되지 않는 StringKey 삭제', 'deleteUnusedStringKeys')
          .addSeparator()
          .addItem('🔧 stringkey 수식 복구', 'restoreStringKeyFormulas')
          .addSeparator()
          .addItem('ℹ️ 사용 방법', 'showHelp'))

      .addToUi();
}
