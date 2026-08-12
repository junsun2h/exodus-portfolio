// 사운드 브라우저 — 목록 뷰
// 항목이 수백~수천 개이므로 화면에 보이는 행만 그린다. 전부 그리면 스크롤이 즉시 멈춘다.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PX.SoundBrowser
{
    public partial class SoundBrowserWindow
    {
        #region 상수

        private const float HeaderHeight = 18f;
        private const float ScrollbarWidth = 15f;
        private const float ColumnGap = 6f;
        private const float PlayColumnWidth = 22f;
        private const float LengthColumnWidth = 52f;
        private const float SpecColumnWidth = 74f;
        private const float SizeColumnWidth = 58f;

        #endregion

        #region 스타일 (지연 생성 — OnEnable 시점에는 EditorStyles가 준비되지 않을 수 있다)

        private GUIStyle _rowLabelStyle;
        private GUIStyle _rowRightStyle;
        private GUIStyle _rowDimStyle;
        private GUIStyle _rowFolderStyle;
        private GUIStyle _placeholderStyle;

        private GUIStyle RowLabelStyle => _rowLabelStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            wordWrap = false,
            padding = new RectOffset(2, 2, 0, 0),
        };

        private GUIStyle RowRightStyle => _rowRightStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            clipping = TextClipping.Clip,
            wordWrap = false,
        };

        private GUIStyle RowDimStyle => _rowDimStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            wordWrap = false,
            normal = { textColor = new Color(0.55f, 0.55f, 0.57f) },
        };

        /// <summary>
        /// 폴더 경로용 — 오른쪽으로 붙여 그린다.
        /// 경로가 길면 앞쪽이 잘리는데, 뒤쪽(구체적인 폴더명)이 훨씬 알아보기 쉽다.
        /// </summary>
        private GUIStyle RowFolderStyle => _rowFolderStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            clipping = TextClipping.Clip,
            wordWrap = false,
            normal = { textColor = new Color(0.55f, 0.55f, 0.57f) },
        };

        private GUIStyle PlaceholderStyle => _placeholderStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
        };

        #endregion

        #region 컬럼 레이아웃

        /// <summary>한 행의 컬럼 사각형들</summary>
        private struct RowColumns
        {
            public Rect Play;
            public Rect Name;
            public Rect Wave;
            public Rect Length;
            public Rect Spec;
            public Rect Size;
            public Rect Folder;
        }

        /// <summary>
        /// 주어진 폭에 맞춰 컬럼을 배치한다.
        /// 창이 좁아지면 폴더 → 파형 순으로 자리를 내주고, 이름이 마지막까지 남는다.
        /// </summary>
        private static RowColumns LayoutColumns(Rect row)
        {
            float fixedWidth = PlayColumnWidth + LengthColumnWidth + SpecColumnWidth + SizeColumnWidth + ColumnGap * 5f;
            float flexible = Mathf.Max(0f, row.width - fixedWidth);

            float waveWidth = Mathf.Clamp(flexible * 0.34f, 0f, 300f);
            if (flexible - waveWidth < 120f)
            {
                waveWidth = Mathf.Max(0f, flexible - 120f);
            }

            float nameWidth = Mathf.Max(90f, (flexible - waveWidth) * 0.55f);
            float folderWidth = Mathf.Max(0f, flexible - waveWidth - nameWidth);

            var columns = new RowColumns();
            float x = row.x;

            columns.Play = new Rect(x, row.y, PlayColumnWidth, row.height);
            x += PlayColumnWidth + ColumnGap;

            columns.Name = new Rect(x, row.y, nameWidth, row.height);
            x += nameWidth + ColumnGap;

            columns.Wave = new Rect(x, row.y, waveWidth, row.height);
            x += waveWidth + ColumnGap;

            columns.Length = new Rect(x, row.y, LengthColumnWidth, row.height);
            x += LengthColumnWidth + ColumnGap;

            columns.Spec = new Rect(x, row.y, SpecColumnWidth, row.height);
            x += SpecColumnWidth + ColumnGap;

            columns.Size = new Rect(x, row.y, SizeColumnWidth, row.height);
            x += SizeColumnWidth + ColumnGap;

            columns.Folder = new Rect(x, row.y, folderWidth, row.height);
            return columns;
        }

        #endregion

        #region 본문 레이아웃

        private void DrawBody(Rect rect)
        {
            float maxPreview = Mathf.Max(MinPreviewWidth, rect.width - 320f);
            _previewWidth = Mathf.Clamp(_previewWidth, MinPreviewWidth, maxPreview);

            float listWidth = rect.width - _previewWidth - SplitterWidth;

            var listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            var splitterRect = new Rect(listRect.xMax, rect.y, SplitterWidth, rect.height);
            var previewRect = new Rect(splitterRect.xMax, rect.y, _previewWidth, rect.height);

            HandleListKeyboard();
            DrawList(listRect);
            DrawSplitter(splitterRect);
            DrawPreviewPanel(previewRect);
        }

        private void DrawSplitter(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.MouseDown when rect.Contains(evt.mousePosition) && evt.button == 0:
                    _draggingSplitter = true;
                    evt.Use();
                    break;

                case EventType.MouseDrag when _draggingSplitter:
                    _previewWidth -= evt.delta.x;
                    Repaint();
                    evt.Use();
                    break;

                case EventType.MouseUp when _draggingSplitter:
                    _draggingSplitter = false;
                    evt.Use();
                    break;
            }
        }

        #endregion

        #region 목록

        private void DrawList(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.17f));

            var headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            DrawHeader(headerRect);

            var contentRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - HeaderHeight);

            if (_filtered.Count == 0)
            {
                string message = _allEntries.Count == 0
                    ? "인덱스가 비어 있습니다.\n툴바의 [인덱스 재생성]을 눌러 사운드를 스캔하세요."
                    : "조건에 맞는 사운드가 없습니다.\n검색어나 필터를 바꿔보세요.";
                GUI.Label(contentRect, message, PlaceholderStyle);
                return;
            }

            float rowHeight = Mathf.Round(_rowHeight);
            var viewRect = new Rect(0f, 0f, contentRect.width - ScrollbarWidth, _filtered.Count * rowHeight);
            _listScroll = GUI.BeginScrollView(contentRect, _listScroll, viewRect);

            // 화면 밖 행은 건너뛴다 (위아래로 한 행씩 여유를 둬서 스크롤 시 빈칸이 보이지 않게 한다)
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(_listScroll.y / rowHeight) - 1);
            int lastRow = Mathf.Min(_filtered.Count - 1, Mathf.CeilToInt((_listScroll.y + contentRect.height) / rowHeight));

            for (int index = firstRow; index <= lastRow; index++)
            {
                var rowRect = new Rect(0f, index * rowHeight, viewRect.width, rowHeight);
                DrawRow(_filtered[index], index, rowRect);
            }

            GUI.EndScrollView();
        }

        private void DrawHeader(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);

            var inner = new Rect(rect.x, rect.y, rect.width - ScrollbarWidth, rect.height);
            var columns = LayoutColumns(inner);

            DrawHeaderLabel(columns.Name, "이름", SortMode.Name);
            if (columns.Wave.width > 30f)
            {
                GUI.Label(columns.Wave, "파형", EditorStyles.miniLabel);
            }
            DrawHeaderLabel(columns.Length, "길이", SortMode.Length, true);
            GUI.Label(columns.Spec, "채널/주파수", RowRightStyle);
            DrawHeaderLabel(columns.Size, "용량", SortMode.FileSize, true);
            if (columns.Folder.width > 40f)
            {
                DrawHeaderLabel(columns.Folder, "폴더", SortMode.Folder, true);
            }
        }

        private void DrawHeaderLabel(Rect rect, string label, SortMode mode, bool rightAligned = false)
        {
            string suffix = _sortMode == mode ? (_sortDescending ? " ▼" : " ▲") : string.Empty;
            var style = rightAligned ? RowRightStyle : EditorStyles.miniLabel;

            if (GUI.Button(rect, label + suffix, style))
            {
                SetSort(mode);
            }
        }

        private void DrawRow(SoundEntry entry, int index, Rect rect)
        {
            bool isSelected = ReferenceEquals(entry, _selected);
            bool isPlaying = _player.IsPlaying && string.Equals(_player.CurrentGuid, entry.Guid, StringComparison.Ordinal);

            // 배경 — 줄무늬로 행을 구분하고, 선택/재생 상태를 색으로 덧씌운다
            Color background;
            if (isSelected)
            {
                background = new Color(0.24f, 0.38f, 0.58f);
            }
            else if (isPlaying)
            {
                background = new Color(0.20f, 0.34f, 0.24f);
            }
            else
            {
                background = (index & 1) == 0
                    ? new Color(0.200f, 0.200f, 0.210f)
                    : new Color(0.183f, 0.183f, 0.193f);
            }
            EditorGUI.DrawRect(rect, background);

            var columns = LayoutColumns(rect);

            DrawPlayButton(entry, columns.Play, isPlaying);
            DrawNameColumn(entry, columns.Name);
            DrawWaveColumn(entry, columns.Wave, isSelected, isPlaying);

            GUI.Label(columns.Length, FormatLength(entry.Length), RowRightStyle);
            GUI.Label(columns.Spec, FormatSpec(entry), RowRightStyle);
            GUI.Label(columns.Size, FormatSize(entry.FileSize), RowRightStyle);

            if (columns.Folder.width > 20f)
            {
                GUI.Label(columns.Folder, new GUIContent(entry.ShortFolder, entry.Path), RowFolderStyle);
            }

            HandleRowInput(entry, index, rect, columns);
        }

        private void DrawPlayButton(SoundEntry entry, Rect rect, bool isPlaying)
        {
            var buttonRect = new Rect(rect.x, rect.y + (rect.height - 16f) * 0.5f, rect.width, 16f);
            if (!GUI.Button(buttonRect, isPlaying ? "■" : "▶", EditorStyles.miniButton)) return;

            if (isPlaying)
            {
                _player.StopPlayback();
            }
            else
            {
                SelectAndPlay(entry, _filtered.IndexOf(entry), 0f);
            }
            Repaint();
        }

        private void DrawNameColumn(SoundEntry entry, Rect rect)
        {
            var textRect = rect;

            // 게임에 편입된 사운드는 왼쪽에 표식을 남긴다
            if (entry.IsGameAsset)
            {
                var badgeRect = new Rect(rect.x, rect.y + rect.height * 0.5f - 3f, 6f, 6f);
                EditorGUI.DrawRect(badgeRect, new Color(0.35f, 0.8f, 0.4f));
                textRect = new Rect(rect.x + 9f, rect.y, rect.width - 9f, rect.height);
            }

            GUI.Label(textRect, new GUIContent(entry.Name, entry.Path), RowLabelStyle);
        }

        private void DrawWaveColumn(SoundEntry entry, Rect rect, bool isSelected, bool isPlaying)
        {
            if (rect.width < 20f) return;

            var waveRect = new Rect(rect.x, rect.y + 2f, rect.width, rect.height - 4f);

            var texture = SoundWaveformCache.Get(entry);
            if (texture == null)
            {
                if (!entry.Analyzed)
                {
                    GUI.Label(waveRect, "미분석", RowDimStyle);
                }
                else if (string.IsNullOrEmpty(entry.Waveform))
                {
                    GUI.Label(waveRect, "파형 없음", RowDimStyle);
                }
                return;
            }

            var previous = GUI.color;
            GUI.color = isPlaying
                ? new Color(0.55f, 0.92f, 0.62f)
                : isSelected
                    ? new Color(0.72f, 0.86f, 1f)
                    : new Color(0.46f, 0.60f, 0.74f);
            GUI.DrawTexture(waveRect, texture, ScaleMode.StretchToFill, true);
            GUI.color = previous;

            // 재생 중인 행에는 진행 위치를 세로선으로 표시한다
            if (isPlaying)
            {
                float progress = _player.NormalizedPosition;
                var cursorRect = new Rect(waveRect.x + waveRect.width * progress, waveRect.y, 1f, waveRect.height);
                EditorGUI.DrawRect(cursorRect, new Color(1f, 1f, 1f, 0.8f));
            }
        }

        private static string FormatSpec(SoundEntry entry)
        {
            if (entry.Frequency <= 0) return "-";

            string channels = entry.Channels == 1 ? "1ch" : $"{entry.Channels}ch";
            return $"{channels} {entry.Frequency / 1000f:0.#}k";
        }

        #endregion

        #region 입력

        private void HandleRowInput(SoundEntry entry, int index, Rect rect, RowColumns columns)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0:
                    // 파형을 직접 누르면 그 지점부터 듣는다
                    if (columns.Wave.width > 20f && columns.Wave.Contains(evt.mousePosition))
                    {
                        float normalized = Mathf.Clamp01((evt.mousePosition.x - columns.Wave.x) / columns.Wave.width);
                        SelectAndPlay(entry, index, normalized);
                    }
                    else
                    {
                        Select(entry, index);
                        if (evt.clickCount == 2)
                        {
                            PingInProject(entry);
                        }
                    }
                    evt.Use();
                    break;

                case EventType.MouseDown when evt.button == 1:
                    Select(entry, index);
                    ShowRowContextMenu(entry);
                    evt.Use();
                    break;

                case EventType.MouseDrag when evt.button == 0 && ReferenceEquals(entry, _selected):
                    StartDrag(entry);
                    evt.Use();
                    break;
            }
        }

        /// <summary>클립을 씬/인스펙터로 끌어다 놓을 수 있게 한다.</summary>
        private static void StartDrag(SoundEntry entry)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.Path);
            if (clip == null) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[] { clip };
            DragAndDrop.paths = new[] { entry.Path };
            DragAndDrop.StartDrag(entry.Name);
        }

        private void ShowRowContextMenu(SoundEntry entry)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("재생"), false, () => SelectAndPlay(entry, _filtered.IndexOf(entry), 0f));
            menu.AddItem(new GUIContent("Project에서 보기"), false, () => PingInProject(entry));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("에셋 경로 복사"), false, () => EditorGUIUtility.systemCopyBuffer = entry.Path);
            menu.AddItem(new GUIContent("이름 복사"), false, () => EditorGUIUtility.systemCopyBuffer = entry.Name);
            menu.AddItem(new GUIContent("이 폴더만 보기"), false, () =>
            {
                _folderFilter = entry.Folder;
                _filterDirty = true;
                Repaint();
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("탐색기에서 열기"), false, () => RevealInExplorer(entry));
            menu.AddItem(new GUIContent("이 항목만 파형 재분석"), false, () => ReanalyzeSingle(entry));

            menu.ShowAsContext();
        }

        private void HandleListKeyboard()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown || _filtered.Count == 0) return;

            // 검색창에 포커스가 있으면 방향키를 가로채지 않는다
            if (!string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())) return;

            if (evt.keyCode == KeyCode.Space)
            {
                TogglePlayback();
                evt.Use();
                return;
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                if (_selected != null)
                {
                    PingInProject(_selected);
                }
                evt.Use();
                return;
            }

            int visibleRows = Mathf.Max(1, Mathf.FloorToInt((position.height - HeaderHeight - StatusBarHeight - 80f) / Mathf.Max(1f, _rowHeight)));
            int next = _selectedIndex;

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow: next = _selectedIndex - 1; break;
                case KeyCode.DownArrow: next = _selectedIndex + 1; break;
                case KeyCode.PageUp: next = _selectedIndex - visibleRows; break;
                case KeyCode.PageDown: next = _selectedIndex + visibleRows; break;
                case KeyCode.Home: next = 0; break;
                case KeyCode.End: next = _filtered.Count - 1; break;
                default: return;
            }

            // 아직 아무것도 고르지 않았으면 첫 항목부터 시작한다
            if (_selectedIndex < 0)
            {
                next = 0;
            }

            next = Mathf.Clamp(next, 0, _filtered.Count - 1);
            if (next != _selectedIndex)
            {
                Select(_filtered[next], next);
                ScrollToSelection();
            }
            evt.Use();
        }

        /// <summary>선택 항목이 화면 밖으로 나가면 스크롤을 맞춘다.</summary>
        private void ScrollToSelection()
        {
            if (_selectedIndex < 0) return;

            float rowHeight = Mathf.Round(_rowHeight);
            float rowTop = _selectedIndex * rowHeight;
            float viewHeight = position.height - HeaderHeight - StatusBarHeight - 90f;

            if (rowTop < _listScroll.y)
            {
                _listScroll.y = rowTop;
            }
            else if (rowTop + rowHeight > _listScroll.y + viewHeight)
            {
                _listScroll.y = rowTop + rowHeight - viewHeight;
            }
        }

        #endregion

        #region 항목 조작

        private static void PingInProject(SoundEntry entry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.Path);
            if (asset == null) return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static void RevealInExplorer(SoundEntry entry)
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (projectRoot == null) return;

                var fullPath = Path.Combine(projectRoot, entry.Path);
                if (File.Exists(fullPath))
                {
                    EditorUtility.RevealInFinder(fullPath);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[SoundBrowser] 탐색기 열기 실패: {e.Message}");
            }
        }

        /// <summary>단일 항목의 파형을 즉시 다시 뽑는다.</summary>
        private void ReanalyzeSingle(SoundEntry entry)
        {
            SoundAnalysisBatcher.AnalyzeSingle(entry);
            SoundIndexer.SaveIndex(_allEntries);

            if (ReferenceEquals(entry, _selected))
            {
                BuildPreviewWaveform(entry);
            }
            Repaint();
        }

        #endregion
    }
}
