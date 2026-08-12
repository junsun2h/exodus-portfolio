// ===== 백업 및 내보내기 =====

// 구글 시트 백업
function backupSpreadSheet() {
  var sourceSpreadsheetId = '1tNqK45dbSMZqlodnVkTSnwW6DLYgfzC6eqQxWtwTGMM';
  var destinationFolderId = '1PQTZafKbbZFyvq6TB5d4F0PMbru1T3Xb';

  var today = new Date();
  var year = today.getFullYear();
  var month = ('0' + (today.getMonth() + 1)).slice(-2);
  var day = ('0' + today.getDate()).slice(-2);
  var formattedDate = year + '.' + month + '.' + day;

  var userContent = Browser.inputBox('백업 파일 이름에 추가할 내용을 입력하세요:', Browser.Buttons.OK_CANCEL);

  var sourceSpreadsheet = SpreadsheetApp.openById(sourceSpreadsheetId);
  var sourceSpreadsheetName = sourceSpreadsheet.getName();

  var newSpreadsheetName;
  if (userContent == 'cancel') {
    newSpreadsheetName = sourceSpreadsheetName + '_' + formattedDate;
  } else {
    newSpreadsheetName = sourceSpreadsheetName + '_' + formattedDate + '_' + userContent;
  }

  var newSpreadsheet = sourceSpreadsheet.copy(newSpreadsheetName);
  var file = DriveApp.getFileById(newSpreadsheet.getId());
  var folder = DriveApp.getFolderById(destinationFolderId);
  file.moveTo(folder);

  SpreadsheetApp.getUi().alert('스프레드시트가 성공적으로 복사되어 이동되었습니다: ' + newSpreadsheetName);
}

// 모든 탭을 CSV로 ZIP 압축하여 다운로드
function exportAllSheetsAsZIP() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheets = ss.getSheets();
  var blobs = [];

  var validSheets = sheets.filter(function(sheet) {
    return !sheet.getName().startsWith('_');
  });

  var totalSheets = validSheets.length;
  var currentSheet = 0;

  showProgressSidebar(0, totalSheets, 'Starting...');

  validSheets.forEach(function(sheet) {
    currentSheet++;
    var sheetName = sheet.getName();

    var progress = Math.round((currentSheet / totalSheets) * 100);
    showProgressSidebar(progress, totalSheets, 'Processing: ' + sheetName);

    Logger.log('[' + currentSheet + '/' + totalSheets + '] Processing: ' + sheetName);

    var data = sheet.getDataRange().getValues();
    var csvContent = convertArrayToCSV(data);

    var blob = Utilities.newBlob('\uFEFF' + csvContent, 'text/csv; charset=utf-8', sheetName + '.csv');
    blobs.push(blob);
  });

  showProgressSidebar(100, totalSheets, 'Creating ZIP file...');
  Logger.log('Creating ZIP file...');

  var zipBlob = Utilities.zip(blobs, ss.getName() + ' - All Sheets.zip');

  showProgressSidebar(100, totalSheets, 'Preparing download...');
  var file = DriveApp.createFile(zipBlob);
  var downloadUrl = file.getDownloadUrl();

  var html = HtmlService.createHtmlOutput(
    '<h3>CSV Export Complete!</h3>' +
    '<p><strong>' + totalSheets + ' sheets</strong> exported to ZIP file</p>' +
    '<p><a href="' + downloadUrl + '" target="_blank" style="font-size:16px; padding:10px 20px; background:#4285f4; color:white; text-decoration:none; border-radius:4px; display:inline-block;">Download ZIP File</a></p>' +
    '<p style="margin-top:20px; font-size:12px; color:#666;">Or copy this URL:</p>' +
    '<input type="text" value="' + downloadUrl + '" style="width:100%; padding:5px;" onclick="this.select();" />' +
    '<script>window.open("' + downloadUrl + '", "_blank");</script>'
  ).setWidth(600).setHeight(250);

  SpreadsheetApp.getUi().showModalDialog(html, 'Download Ready');

  file.setTrashed(true);

  Logger.log('Export completed! ' + totalSheets + ' sheets exported.');
  Logger.log('Temporary file removed from Drive');
}

// ── 내부 유틸 ──────────────────────────────────────────────

function showProgressSidebar(progress, totalSheets, message) {
  var html = HtmlService.createHtmlOutput(
    '<style>' +
    'body { font-family: Arial, sans-serif; padding: 20px; text-align: center; }' +
    '.progress-container { width: 100%; background: #f0f0f0; border-radius: 10px; margin: 20px 0; }' +
    '.progress-bar { width: ' + progress + '%; height: 30px; background: #4285f4; border-radius: 10px; transition: width 0.3s; }' +
    '.progress-text { margin-top: 10px; font-size: 18px; font-weight: bold; color: #4285f4; }' +
    '.message { margin-top: 15px; font-size: 14px; color: #666; word-wrap: break-word; }' +
    '</style>' +
    '<h2>CSV Export Progress</h2>' +
    '<div class="progress-container"><div class="progress-bar"></div></div>' +
    '<div class="progress-text">' + progress + '%</div>' +
    '<div class="message">' + message + '</div>' +
    '<p style="font-size: 12px; color: #999; margin-top: 30px;">Processing ' + totalSheets + ' sheets...</p>'
  ).setTitle('Export Progress');

  SpreadsheetApp.getUi().showSidebar(html);
}

function convertArrayToCSV(data) {
  var csv = '';

  data.forEach(function(row) {
    var processedRow = row.map(function(cell) {
      var value = cell.toString();

      if (value.includes(',') || value.includes('"') || value.includes('\n')) {
        value = '"' + value.replace(/"/g, '""') + '"';
      }

      return value;
    });

    csv += processedRow.join(',') + '\n';
  });

  return csv;
}
