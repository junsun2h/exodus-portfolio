#!/usr/bin/env python3
"""
Google Sheets 상품 데이터 자동 입력 도구

사용법:
  python upload_products.py data/packages.json
  python upload_products.py data/packages.json --dry-run
  python upload_products.py data/packages.json --backup
  python upload_products.py data/packages.json --backup --dry-run

name 컬럼 기준으로 행 매칭:
  - 존재하면 UPDATE (skip_columns 제외)
  - 없으면 INSERT (새 행 추가)
"""

import argparse
import json
import os
import sys
import time
from datetime import datetime
from pathlib import Path


# Google Sheets API rate limit: 60 write requests/min
# 1.1초 간격으로 쓰기 요청을 보내면 ~54 req/min으로 안전
_last_write_time = 0.0
WRITE_INTERVAL = 1.1  # 초

# 배치 전송 시 한 번에 보낼 최대 셀 수 (약 40~50행 분량)
BATCH_SIZE = 500


def rate_limit():
    """API 쓰기 요청 간 최소 간격을 보장"""
    global _last_write_time
    now = time.time()
    elapsed = now - _last_write_time
    if elapsed < WRITE_INTERVAL:
        time.sleep(WRITE_INTERVAL - elapsed)
    _last_write_time = time.time()

try:
    import gspread
except ImportError:
    print("오류: gspread가 설치되지 않았습니다. pip install gspread 를 실행하세요.")
    sys.exit(1)


SCRIPT_DIR = Path(__file__).parent
CONFIG_PATH = SCRIPT_DIR / "config.json"
SERVICE_ACCOUNT_PATH = SCRIPT_DIR / "service_account.json"


def load_config():
    """config.json 로드"""
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def load_json(json_path):
    """입력 JSON 데이터 로드"""
    with open(json_path, "r", encoding="utf-8") as f:
        return json.load(f)


def find_row_by_name(col_values, name):
    """name 컬럼에서 값을 찾아 1-based 행 번호 반환. 없으면 None."""
    for idx, val in enumerate(col_values):
        if val == name:
            return idx + 1  # gspread는 1-based
    return None


def map_to_row(data_dict, headers, skip_columns):
    """JSON 객체를 시트 헤더 순서에 맞게 리스트로 변환.
    skip_columns에 해당하는 컬럼은 빈 문자열로 처리."""
    row = []
    for header in headers:
        if header in skip_columns or header.startswith("_"):
            row.append("")
        else:
            value = data_dict.get(header, "")
            # None은 빈 문자열로 변환
            if value is None:
                value = ""
            row.append(value)
    return row


def resolve_formula(formula_template, row_idx, headers):
    """수식 템플릿의 플레이스홀더를 실제 값으로 치환.
    - {row} → 행 번호
    - {col:header_name} → 해당 헤더의 컬럼 문자 (예: {col:str_name} → C)
    참조 헤더가 시트에 없으면 None을 반환 (수식 삽입 건너뜀)."""
    import re
    formula = formula_template.replace("{row}", str(row_idx))
    for match in re.finditer(r"\{col:(\w+)\}", formula):
        ref_header = match.group(1)
        if ref_header in headers:
            col_letter = get_column_letter(headers.index(ref_header) + 1)
            formula = formula.replace(match.group(0), col_letter)
        else:
            return None
    return formula


def build_update_cells(data_dict, headers, skip_columns, row_idx, formula_columns=None):
    """업데이트할 셀 목록을 gspread Cell 객체 리스트로 반환.
    formula_columns에 해당하는 컬럼은 _ 접두어여도 수식을 삽입."""
    from gspread.cell import Cell

    if formula_columns is None:
        formula_columns = {}

    cells = []
    for col_idx, header in enumerate(headers, start=1):
        if header in skip_columns:
            continue
        if header in formula_columns:
            formula = resolve_formula(formula_columns[header], row_idx, headers)
            if formula is not None:
                cells.append(Cell(row=row_idx, col=col_idx, value=formula))
        elif header.startswith("_"):
            continue
        elif header in data_dict:
            value = data_dict[header]
            if value is None:
                value = ""
            cells.append(Cell(row=row_idx, col=col_idx, value=value))
    return cells


def build_insert_cells(data_dict, headers, skip_columns, row_idx, formula_columns=None):
    """새 행 삽입용 셀 목록을 gspread Cell 객체 리스트로 반환.
    formula_columns에 해당하는 컬럼은 _ 접두어여도 수식을 삽입."""
    from gspread.cell import Cell

    if formula_columns is None:
        formula_columns = {}

    cells = []
    for col_idx, header in enumerate(headers, start=1):
        if header in skip_columns:
            continue
        if header in formula_columns:
            formula = resolve_formula(formula_columns[header], row_idx, headers)
            if formula is not None:
                cells.append(Cell(row=row_idx, col=col_idx, value=formula))
        elif header.startswith("_"):
            continue
        else:
            value = data_dict.get(header, "")
            if value is None:
                value = ""
            cells.append(Cell(row=row_idx, col=col_idx, value=value))
    return cells


def get_column_letter(col_number):
    """1-based 컬럼 번호를 Excel 스타일 문자로 변환 (1→A, 2→B, 27→AA)"""
    letter = ""
    num = col_number
    while num > 0:
        remainder = (num - 1) % 26
        letter = chr(65 + remainder) + letter
        num = (num - 1) // 26
    return letter


def find_next_empty_row(worksheet, key_col_idx):
    """key 컬럼 기준으로 다음 빈 행 번호를 반환 (1-based)."""
    col_values = worksheet.col_values(key_col_idx)
    return len(col_values) + 1


def flush_cells(ws, cells, dry_run=False):
    """수집된 셀을 BATCH_SIZE 단위로 나누어 한 번에 전송"""
    if dry_run or not cells:
        return 0
    api_calls = 0
    for i in range(0, len(cells), BATCH_SIZE):
        chunk = cells[i:i + BATCH_SIZE]
        rate_limit()
        ws.update_cells(chunk, value_input_option="USER_ENTERED")
        api_calls += 1
    return api_calls


def backup_sheet(worksheet, backup_dir):
    """시트 데이터를 JSON으로 백업"""
    backup_dir = Path(backup_dir)
    backup_dir.mkdir(parents=True, exist_ok=True)

    all_data = worksheet.get_all_records()
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    backup_path = backup_dir / f"{worksheet.title}_{timestamp}.json"

    with open(backup_path, "w", encoding="utf-8") as f:
        json.dump(all_data, f, ensure_ascii=False, indent=2)

    print(f"  백업 완료: {backup_path}")
    return backup_path


def process_products(sh, config, products, sheet_key, label, dry_run=False):
    """상품 시트 처리 (패키지, 패스 등 공통)"""
    sheet_config = config["sheets"][sheet_key]
    tab_name = sheet_config["tab_name"]
    key_column = sheet_config["key_column"]
    skip_columns = set(sheet_config["skip_columns"])
    formula_columns = {**config.get("formula_columns", {}), **sheet_config.get("formula_columns", {})}

    print(f"\n[{tab_name}] 시트 처리 시작...")
    ws = sh.worksheet(tab_name)

    headers = ws.row_values(1)
    if not headers:
        print(f"  오류: {tab_name} 시트에 헤더가 없습니다.")
        return

    # key_column 인덱스 찾기
    if key_column not in headers:
        print(f"  오류: '{key_column}' 컬럼을 찾을 수 없습니다. 헤더: {headers}")
        return

    key_col_idx = headers.index(key_column) + 1  # 1-based
    existing_names = ws.col_values(key_col_idx)

    # enum_index 자동 부여: 시트의 최대값 +1
    next_enum = None
    if "enum_index" in headers:
        enum_col_idx = headers.index("enum_index") + 1
        enum_values = ws.col_values(enum_col_idx)
        max_enum = 0
        for val in enum_values[1:]:
            try:
                max_enum = max(max_enum, int(val))
            except (ValueError, TypeError):
                continue
        next_enum = max_enum + 1
        print(f"  현재 최대 enum_index: {max_enum}, 다음 시작: {next_enum}")

    update_count = 0
    insert_count = 0
    all_cells = []
    next_row = find_next_empty_row(ws, key_col_idx)
    max_row_needed = ws.row_count

    for product in products:
        name = product.get("name", "")
        if not name:
            print(f"  경고: name이 비어있는 {label}를 건너뜁니다.")
            continue

        # enum_index가 없으면 자동 부여
        if next_enum is not None and not product.get("enum_index"):
            product["enum_index"] = next_enum
            next_enum += 1

        row_idx = find_row_by_name(existing_names, name)

        if row_idx:
            # UPDATE
            if dry_run:
                print(f"  [DRY-RUN] UPDATE 행 {row_idx}: {name}")
                # 변경될 필드 표시
                for header in headers:
                    if header in skip_columns or header.startswith("_"):
                        continue
                    if header in product:
                        print(f"    {header}: {product[header]}")
            else:
                cells = build_update_cells(product, headers, skip_columns, row_idx, formula_columns)
                all_cells.extend(cells)
                print(f"  UPDATE 행 {row_idx}: {name}")
            update_count += 1
        else:
            # INSERT
            if dry_run:
                print(f"  [DRY-RUN] INSERT 행 {next_row}: {name}")
                for header in headers:
                    if header in skip_columns or header.startswith("_"):
                        continue
                    if header in formula_columns:
                        print(f"    {header}: [수식]")
                    elif header in product and product[header] != "":
                        print(f"    {header}: {product[header]}")
            else:
                if next_row > max_row_needed:
                    max_row_needed = next_row
                cells = build_insert_cells(product, headers, skip_columns, next_row, formula_columns)
                all_cells.extend(cells)
                existing_names.append(name)
                print(f"  INSERT 행 {next_row}: {name}")
            next_row += 1
            insert_count += 1

    # 배치 전송
    if not dry_run and all_cells:
        if max_row_needed > ws.row_count:
            rate_limit()
            ws.add_rows(max_row_needed - ws.row_count)
        api_calls = flush_cells(ws, all_cells)
        print(f"  배치 전송: {api_calls}회 API 호출 ({len(all_cells)}개 셀)")

    print(f"  완료: UPDATE {update_count}건, INSERT {insert_count}건")


def process_product_items(sh, config, products, sheet_key, label, dry_run=False):
    """상품 아이템 시트 처리 (패키지 아이템, 패스 아이템 등 공통)"""
    sheet_config = config["sheets"][sheet_key]
    tab_name = sheet_config["tab_name"]
    key_column = sheet_config["key_column"]
    parent_key_column = sheet_config["parent_key_column"]
    skip_columns = set(sheet_config["skip_columns"])
    formula_columns = {**config.get("formula_columns", {}), **sheet_config.get("formula_columns", {})}
    ticket_mappings = sheet_config.get("ticket_mappings", [])

    # ticket ↔ currency 자동 매핑용 상수
    ticket_currencies = {"currency_ticket_gacha", "currency_ticket_pick", "currency_ticket_fix"}
    ticket_currency_map = {
        "gacha_": "currency_ticket_gacha",
        "pick_": "currency_ticket_pick",
        "fix_": "currency_ticket_fix",
    }

    print(f"\n[{tab_name}] 시트 처리 시작...")
    ws = sh.worksheet(tab_name)

    headers = ws.row_values(1)
    if not headers:
        print(f"  오류: {tab_name} 시트에 헤더가 없습니다.")
        return

    if key_column not in headers:
        print(f"  오류: '{key_column}' 컬럼을 찾을 수 없습니다.")
        return

    key_col_idx = headers.index(key_column) + 1
    existing_names = ws.col_values(key_col_idx)

    # parent_key로 기존 아이템 행 번호도 조회
    parent_col_idx = None
    if parent_key_column and parent_key_column in headers:
        parent_col_idx = headers.index(parent_key_column) + 1

    update_count = 0
    insert_count = 0
    next_insert_row = find_next_empty_row(ws, key_col_idx)
    had_previous_inserts = False
    all_cells = []
    max_row_needed = ws.row_count

    for product in products:
        product_name = product.get("name", "")
        items = product.get("items", [])
        if not items:
            continue

        print(f"  {label} '{product_name}'의 아이템 {len(items)}개 처리 중...")

        product_has_inserts = False

        for item in items:
            # 부모 FK 자동 설정
            item_data = dict(item)
            if parent_key_column:
                item_data[parent_key_column] = product_name

            # ticket ↔ currency 자동 매핑 (config의 ticket_mappings 기반)
            for mapping in ticket_mappings:
                currency_col = mapping["currency_col"]
                ticket_col = mapping["ticket_col"]
                ticket = item_data.get(ticket_col, "")
                currency = item_data.get(currency_col, "")

                if ticket and not currency:
                    for prefix, cur in ticket_currency_map.items():
                        if ticket.startswith(prefix):
                            item_data[currency_col] = cur
                            break
                elif currency and currency not in ticket_currencies:
                    item_data[ticket_col] = ""

            item_name = item_data.get("name", "")
            if not item_name:
                print("    경고: name이 비어있는 아이템을 건너뜁니다.")
                continue

            row_idx = find_row_by_name(existing_names, item_name)

            if row_idx:
                # UPDATE
                if dry_run:
                    print(f"    [DRY-RUN] UPDATE 행 {row_idx}: {item_name}")
                    for header in headers:
                        if header in skip_columns or header.startswith("_"):
                            continue
                        if header in formula_columns:
                            print(f"      {header}: [수식]")
                        elif header in item_data:
                            print(f"      {header}: {item_data[header]}")
                else:
                    cells = build_update_cells(item_data, headers, skip_columns, row_idx, formula_columns)
                    all_cells.extend(cells)
                    print(f"    UPDATE 행 {row_idx}: {item_name}")
                update_count += 1
            else:
                # INSERT: 상품 구분용 빈 행 삽입
                if not product_has_inserts and had_previous_inserts:
                    if dry_run:
                        print(f"    [DRY-RUN] 행 {next_insert_row}: (빈 행 - {label} 구분)")
                    next_insert_row += 1

                if not dry_run:
                    if next_insert_row > max_row_needed:
                        max_row_needed = next_insert_row
                    cells = build_insert_cells(item_data, headers, skip_columns, next_insert_row, formula_columns)
                    all_cells.extend(cells)
                    existing_names.append(item_name)
                    print(f"    INSERT 행 {next_insert_row}: {item_name}")
                else:
                    print(f"    [DRY-RUN] INSERT 행 {next_insert_row}: {item_name}")
                    for header in headers:
                        if header in skip_columns or header.startswith("_"):
                            continue
                        if header in formula_columns:
                            print(f"      {header}: [수식]")
                        elif header in item_data and item_data[header] != "":
                            print(f"      {header}: {item_data[header]}")

                next_insert_row += 1
                product_has_inserts = True
                insert_count += 1

        if product_has_inserts:
            had_previous_inserts = True

    # 배치 전송
    if not dry_run and all_cells:
        if max_row_needed > ws.row_count:
            rate_limit()
            ws.add_rows(max_row_needed - ws.row_count)
        api_calls = flush_cells(ws, all_cells)
        print(f"  배치 전송: {api_calls}회 API 호출 ({len(all_cells)}개 셀)")

    print(f"  완료: UPDATE {update_count}건, INSERT {insert_count}건")


def process_stringkeys(sh, config, packages, dry_run=False):
    """stringkey 시트 처리 - 패키지의 str_name, str_desc 다국어 문자열 등록.
    enum_index는 기존 시트의 최대값 +1 부터 자동 부여."""
    from gspread.cell import Cell

    stringkey_config = config["sheets"].get("stringkey")
    if not stringkey_config:
        print("\n[stringkey] 설정이 없어 건너뜁니다.")
        return

    tab_name = stringkey_config["tab_name"]
    src_name_column = stringkey_config["src_name_column"]
    enum_index_column = stringkey_config["enum_index_column"]
    name_formula_template = stringkey_config["name_formula"]
    locale_columns = stringkey_config["locale_columns"]

    print(f"\n[{tab_name}] 시트 처리 시작...")
    ws = sh.worksheet(tab_name)

    headers = ws.row_values(1)
    if not headers:
        print(f"  오류: {tab_name} 시트에 헤더가 없습니다.")
        return

    # name 컬럼 (display values로 기존 키 확인)
    if "name" not in headers:
        print(f"  오류: 'name' 컬럼을 찾을 수 없습니다. 헤더: {headers}")
        return

    name_col_idx = headers.index("name") + 1
    existing_names = ws.col_values(name_col_idx)  # 수식 결과(display values)

    # _src_name 컬럼 인덱스
    if src_name_column not in headers:
        print(f"  오류: '{src_name_column}' 컬럼을 찾을 수 없습니다.")
        return
    src_name_col_idx = headers.index(src_name_column) + 1
    src_col_letter = get_column_letter(src_name_col_idx)

    # enum_index 컬럼에서 최대값 구하기
    if enum_index_column not in headers:
        print(f"  오류: '{enum_index_column}' 컬럼을 찾을 수 없습니다.")
        return
    enum_col_idx = headers.index(enum_index_column) + 1
    enum_values = ws.col_values(enum_col_idx)
    max_enum = 0
    for val in enum_values[1:]:  # 헤더 제외
        try:
            max_enum = max(max_enum, int(val))
        except (ValueError, TypeError):
            continue

    next_enum = max_enum + 1
    print(f"  현재 최대 enum_index: {max_enum}, 다음 시작: {next_enum}")

    update_count = 0
    insert_count = 0
    all_cells = []
    next_row = find_next_empty_row(ws, name_col_idx)
    max_row_needed = ws.row_count

    for pkg in packages:
        pkg_name = pkg.get("name", "")
        if not pkg_name:
            continue

        # name, desc 각각 엔트리 생성
        entries = []

        str_name_kr = pkg.get("str_name_kr", "")
        str_name_en = pkg.get("str_name_en", "")
        if str_name_kr or str_name_en:
            entries.append({
                "type": "name",
                "expected_name": f"stringkey_{pkg_name}_name",
                "KR": str_name_kr,
                "EN": str_name_en,
            })

        str_desc_kr = pkg.get("str_desc_kr", "")
        str_desc_en = pkg.get("str_desc_en", "")
        if str_desc_kr or str_desc_en:
            entries.append({
                "type": "desc",
                "expected_name": f"stringkey_{pkg_name}_desc",
                "KR": str_desc_kr,
                "EN": str_desc_en,
            })

        for entry in entries:
            row_idx = find_row_by_name(existing_names, entry["expected_name"])

            if row_idx:
                # UPDATE - 로케일 컬럼만 갱신
                if dry_run:
                    print(f"  [DRY-RUN] UPDATE 행 {row_idx}: {entry['expected_name']}")
                    for locale in locale_columns:
                        if entry.get(locale):
                            print(f"    {locale}: {entry[locale]}")
                else:
                    cells = []
                    for locale in locale_columns:
                        if locale in headers:
                            col_idx = headers.index(locale) + 1
                            cells.append(Cell(row=row_idx, col=col_idx, value=entry.get(locale, "")))
                    all_cells.extend(cells)
                    print(f"  UPDATE 행 {row_idx}: {entry['expected_name']}")
                update_count += 1
            else:
                # INSERT
                if dry_run:
                    print(f"  [DRY-RUN] INSERT 행 {next_row}: {entry['expected_name']}")
                    print(f"    _src_name: {pkg_name}")
                    print(f"    name: [수식] → {entry['expected_name']}")
                    print(f"    enum_index: {next_enum}")
                    for locale in locale_columns:
                        if entry.get(locale):
                            print(f"    {locale}: {entry[locale]}")
                else:
                    cells = []
                    # name: TEXTJOIN 수식
                    formula = name_formula_template.replace(
                        "{src_col}", src_col_letter
                    ).replace(
                        "{row}", str(next_row)
                    ).replace(
                        "{type}", entry["type"]
                    )
                    cells.append(Cell(row=next_row, col=name_col_idx, value=formula))
                    # _src_name
                    cells.append(Cell(row=next_row, col=src_name_col_idx, value=pkg_name))
                    # enum_index
                    cells.append(Cell(row=next_row, col=enum_col_idx, value=next_enum))
                    # 로케일 컬럼
                    for locale in locale_columns:
                        if locale in headers:
                            col_idx = headers.index(locale) + 1
                            cells.append(Cell(row=next_row, col=col_idx, value=entry.get(locale, "")))

                    if next_row > max_row_needed:
                        max_row_needed = next_row

                    all_cells.extend(cells)
                    existing_names.append(entry["expected_name"])
                    print(f"  INSERT 행 {next_row}: {entry['expected_name']} (enum_index: {next_enum})")

                next_row += 1
                next_enum += 1
                insert_count += 1

    # 배치 전송
    if not dry_run and all_cells:
        if max_row_needed > ws.row_count:
            rate_limit()
            ws.add_rows(max_row_needed - ws.row_count)
        api_calls = flush_cells(ws, all_cells)
        print(f"  배치 전송: {api_calls}회 API 호출 ({len(all_cells)}개 셀)")

    print(f"  완료: UPDATE {update_count}건, INSERT {insert_count}건")


def main():
    parser = argparse.ArgumentParser(
        description="Google Sheets 상품 데이터 자동 입력 도구"
    )
    parser.add_argument(
        "json_path",
        help="입력 JSON 파일 경로"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="실제 쓰기 없이 변경 예정 내용만 출력"
    )
    parser.add_argument(
        "--backup",
        action="store_true",
        help="업로드 전 현재 시트 데이터를 JSON으로 백업"
    )
    parser.add_argument(
        "--config",
        default=str(CONFIG_PATH),
        help=f"config.json 경로 (기본값: {CONFIG_PATH})"
    )
    parser.add_argument(
        "--service-account",
        default=str(SERVICE_ACCOUNT_PATH),
        help=f"서비스 계정 JSON 경로 (기본값: {SERVICE_ACCOUNT_PATH})"
    )

    args = parser.parse_args()

    # 설정 로드
    config_path = Path(args.config)
    if not config_path.exists():
        print(f"오류: 설정 파일을 찾을 수 없습니다: {config_path}")
        sys.exit(1)

    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)

    # 입력 데이터 로드
    json_path = Path(args.json_path)
    if not json_path.exists():
        print(f"오류: 입력 파일을 찾을 수 없습니다: {json_path}")
        sys.exit(1)

    data = load_json(json_path)
    packages = data.get("packages", [])
    passes = data.get("passes", [])
    if not packages and not passes:
        print("오류: packages 또는 passes 배열이 필요합니다.")
        sys.exit(1)

    if packages:
        print(f"입력 데이터: {len(packages)}개 패키지")
    if passes:
        print(f"입력 데이터: {len(passes)}개 패스")

    # 서비스 계정 인증
    sa_path = Path(args.service_account)
    if not sa_path.exists():
        print(f"오류: 서비스 계정 키 파일을 찾을 수 없습니다: {sa_path}")
        print("GCP Console에서 서비스 계정을 생성하고 JSON 키를 다운로드하세요.")
        sys.exit(1)

    gc = gspread.service_account(filename=str(sa_path))
    spreadsheet_id = config["spreadsheet_id"]

    if spreadsheet_id == "구글시트_ID_여기에":
        print("오류: config.json의 spreadsheet_id를 실제 구글 시트 ID로 설정하세요.")
        sys.exit(1)

    sh = gc.open_by_key(spreadsheet_id)
    print(f"스프레드시트 연결: {sh.title}")

    if args.dry_run:
        print("\n=== DRY-RUN 모드 (실제 쓰기 없음) ===")

    # 백업
    if args.backup and not args.dry_run:
        print("\n시트 데이터 백업 중...")
        backup_dir = SCRIPT_DIR / "backups"
        for sheet_key in config["sheets"]:
            sheet_config = config["sheets"][sheet_key]
            try:
                ws = sh.worksheet(sheet_config["tab_name"])
                backup_sheet(ws, backup_dir)
            except gspread.exceptions.WorksheetNotFound:
                print(f"  경고: '{sheet_config['tab_name']}' 시트를 찾을 수 없어 백업을 건너뜁니다.")

    # 1. stringkey 처리 (모든 상품의 다국어 문자열 등록)
    all_products = packages + passes
    if all_products:
        process_stringkeys(sh, config, all_products, dry_run=args.dry_run)

    # 2. 패키지 처리
    if packages:
        process_products(sh, config, packages, "package", "패키지", dry_run=args.dry_run)
        process_product_items(sh, config, packages, "package_item", "패키지", dry_run=args.dry_run)

    # 3. 패스 처리
    if passes:
        # sequential_step이 있는 패스는 pass_type을 sequential로 자동 설정
        for p in passes:
            if p.get("sequential_step", "") not in ("", None):
                if p.get("pass_type", "") != "sequential":
                    print(f"  [자동설정] {p['name']}: pass_type → sequential (sequential_step={p['sequential_step']})")
                    p["pass_type"] = "sequential"
        process_products(sh, config, passes, "pass", "패스", dry_run=args.dry_run)
        process_product_items(sh, config, passes, "pass_item", "패스", dry_run=args.dry_run)

    # 요약
    parts = []
    if packages:
        total_pkg_items = sum(len(p.get("items", [])) for p in packages)
        parts.append(f"{len(packages)}개 패키지({total_pkg_items}개 아이템)")
    if passes:
        total_pass_items = sum(len(p.get("items", [])) for p in passes)
        parts.append(f"{len(passes)}개 패스({total_pass_items}개 아이템)")
    summary = ", ".join(parts)
    print(f"\n{'[DRY-RUN] ' if args.dry_run else ''}완료: {summary}, stringkey 처리")


if __name__ == "__main__":
    main()
