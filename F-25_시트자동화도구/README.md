# F-25. StringKey 자동 생성 + Google Sheets 자동 입력 도구

> 소스 발췌: `src/` — 7개 파일

**구간** Phase 2 (2026.02.05, 02.13) | **포지션** 툴 | **AI** 협업

- **문제**: (1) 로컬라이제이션 StringKey를 손으로 만들면 오타·중복·누락이 반드시 생기고, 코드와 시트가 어긋난다. (2) 상품 설계 결과(수십 개 상품 × 수십 개 필드)를 Google Sheets에 손으로 옮기는 작업이 반복된다.
- **해결**: 두 가지 도구를 만들었다.
  - **StringKey 자동 생성** — Google Apps Script로 시트에서 StringKey를 자동 생성. 설계 문서와 구현 문서를 분리해서 남겼다.
  - **Sheets 자동 입력** — Python `gspread`로 상품 데이터를 시트에 직접 기입. 도입 전에 **"Google Sheets에 프로그래밍 방식으로 쓰는 방법" 종합 리서치를 먼저 수행**하고 방식을 선택했다.
- **기술**: Google Apps Script, Python `gspread`, Sheets API 배치 쓰기
- **정량**: 도구 2종 / 사전 리서치 문서 1종
- **근거**:
  - `Docs/인프라/완료플랜/26.02.05_StringKey-자동-생성-시스템-설계.md`, `Docs/인프라/완료플랜/26.02.05_StringKey-자동-생성-시스템.md`
  - `Docs/상품/완료플랜/26.02.13_Google-Sheets-상품-데이터-자동-입력-도구-(Python-gspread).md`
  - `Docs/상품/완료플랜/26.02.13_Google-Sheets-프로그래밍-방식-데이터-쓰기-종합-리서치.md` — 사전 조사
  - `Docs/리팩토링/완료플랜/26.02.19_패스-JSON-합본-출력-업로더-배치-최적화.md` — 후속 최적화
- **면접 포인트**: 규모는 작지만 **"도구를 만들기 전에 방식을 조사한다"**는 패턴이 여기서도 반복된다(`종합-리서치` 문서를 도구 구현과 같은 날 작성). 또한 업로더를 만든 뒤 **배치 최적화까지 후속으로 진행**(02.19)한 것은 도구를 만들고 방치하지 않았다는 증거.
- **슬라이드 자료**: 목록 슬라이드 1장 (개별 캡처 불필요)


## 수록 파일

- `Tools/googleappsscript/menu_backup-export.gs`
- `Tools/googleappsscript/menu_data-validation.gs`
- `Tools/googleappsscript/menu_enum-index.gs`
- `Tools/googleappsscript/menu_sort.gs`
- `Tools/googleappsscript/menu_stringkey.gs`
- `Tools/googleappsscript/p1.gs`
- `Tools/sheets_uploader/upload_products.py`
