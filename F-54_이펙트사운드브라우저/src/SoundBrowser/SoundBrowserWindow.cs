// 사운드 브라우저 — 메인 윈도우 (상태, 툴바, 필터)
// 프로젝트에 흩어진 오디오 클립을 한 목록으로 모아 훑고, 고른 것을 그 자리에서 들어본다.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PX.SoundBrowser
{
    /// <summary>
    /// 오디오 클립 탐색 윈도우.
    /// </summary>
    public partial class SoundBrowserWindow : EditorWindow
    {
        #region 상수 / EditorPrefs 키

        private const string PrefRowHeight = "SoundBrowser_RowHeight";
        private const string PrefPreviewWidth = "SoundBrowser_PreviewWidth";
        private const string PrefScanRoots = "SoundBrowser_ScanRoots";
        private const string PrefExcludes = "SoundBrowser_Excludes";
        private const string PrefAutoPlay = "SoundBrowser_AutoPlay";
        private const string PrefLoop = "SoundBrowser_Loop";
        private const string PrefSortMode = "SoundBrowser_SortMode";
        private const string PrefSortDescending = "SoundBrowser_SortDescending";

        private const float MinRowHeight = 18f;
        private const float MaxRowHeight = 48f;
        private const float StatusBarHeight = 22f;
        private const float MinPreviewWidth = 260f;
        private const float SplitterWidth = 5f;

        /// <summary>사용처 필터 항목. "참조 중"은 설정 패널의 [사용처 분석]을 한 번 돌려야 의미가 있다.</summary>
        private static readonly string[] UsageFilterLabels = { "전체", "게임 편입", "미편입(스토어)", "참조 중" };

        /// <summary>정렬 기준</summary>
        private enum SortMode
        {
            Name,
            Length,
            FileSize,
            Folder,
            Loudness,
        }

        private static readonly string[] SortLabels = { "이름순", "길이순", "용량순", "폴더순", "음량순" };

        #endregion

        #region 상태

        private List<SoundEntry> _allEntries = new List<SoundEntry>();
        private readonly List<SoundEntry> _filtered = new List<SoundEntry>();

        private string _search = string.Empty;
        private int _categoryMask;
        private string _folderFilter;
        private int _usageFilter;
        private bool _onlyUnanalyzed;
        private bool _filterDirty = true;

        private SortMode _sortMode = SortMode.Name;
        private bool _sortDescending;

        private float _rowHeight = 26f;
        private float _previewWidth = 340f;

        /// <summary>툴바·분류 바 아래에서 본문이 시작되는 y. Repaint 때만 갱신한다</summary>
        private float _bodyTop = 60f;

        private bool _draggingSplitter;
        private Vector2 _listScroll;
        private bool _showSettings;
        private bool _showCategories = true;

        private string _scanRootsText;
        private string _excludesText;

        private SoundEntry _selected;
        private int _selectedIndex = -1;

        private readonly SoundAnalysisBatcher _batcher = new SoundAnalysisBatcher();
        private readonly SoundPreviewPlayer _player = new SoundPreviewPlayer();
        private string _indexTimestamp = string.Empty;
        private bool _autoPlay = true;

        #endregion

        #region 윈도우 수명주기

        [MenuItem("PX Editor/사운드 브라우저")]
        public static void Open()
        {
            var window = GetWindow<SoundBrowserWindow>("사운드 브라우저");
            window.minSize = new Vector2(760f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            _rowHeight = EditorPrefs.GetFloat(PrefRowHeight, 26f);
            _previewWidth = EditorPrefs.GetFloat(PrefPreviewWidth, 340f);
            _scanRootsText = EditorPrefs.GetString(PrefScanRoots, string.Join("\n", SoundIndexer.DefaultScanRoots));
            _excludesText = EditorPrefs.GetString(PrefExcludes, string.Empty);
            _autoPlay = EditorPrefs.GetBool(PrefAutoPlay, true);
            _player.Loop = EditorPrefs.GetBool(PrefLoop, false);
            _sortMode = (SortMode)EditorPrefs.GetInt(PrefSortMode, (int)SortMode.Name);
            _sortDescending = EditorPrefs.GetBool(PrefSortDescending, false);

            LoadIndexFromDisk();
            RestoreSelectionAfterReload();

            _batcher.OnProgressChanged += Repaint;
            _batcher.OnFinished += OnBatchFinished;

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorPrefs.SetFloat(PrefRowHeight, _rowHeight);
            EditorPrefs.SetFloat(PrefPreviewWidth, _previewWidth);
            EditorPrefs.SetString(PrefScanRoots, _scanRootsText ?? string.Empty);
            EditorPrefs.SetString(PrefExcludes, _excludesText ?? string.Empty);
            EditorPrefs.SetBool(PrefAutoPlay, _autoPlay);
            EditorPrefs.SetBool(PrefLoop, _player.Loop);
            EditorPrefs.SetInt(PrefSortMode, (int)_sortMode);
            EditorPrefs.SetBool(PrefSortDescending, _sortDescending);

            EditorApplication.update -= OnEditorUpdate;

            _batcher.OnProgressChanged -= Repaint;
            _batcher.OnFinished -= OnBatchFinished;
            _batcher.Stop();

            _player.Dispose();
            DisposePreviewWaveform();
            SoundWaveformCache.Clear();
        }

        /// <summary>배치 분석이 끝나면 채워진 파형 정보를 디스크에 반영한다.</summary>
        private void OnBatchFinished()
        {
            if (_allEntries.Count > 0)
            {
                SoundIndexer.SaveIndex(_allEntries);
                _indexTimestamp = SoundIndexer.GetIndexTimestamp();
            }
            _filterDirty = true;
            Repaint();
        }

        /// <summary>재생 중에는 진행 커서를 움직여야 하므로 계속 다시 그린다.</summary>
        private void OnEditorUpdate()
        {
            if (_batcher.IsRunning) return;

            if (_player.IsPlaying || _player.IsPaused)
            {
                Repaint();
            }
        }

        #endregion

        #region 인덱스

        private void LoadIndexFromDisk()
        {
            var loaded = SoundIndexer.LoadIndex();
            if (loaded != null)
            {
                _allEntries = loaded;
                _indexTimestamp = SoundIndexer.GetIndexTimestamp();
            }
            _filterDirty = true;
        }

        /// <summary>
        /// 도메인 리로드 뒤에는 선택 항목이 새로 읽은 인덱스의 인스턴스와 다른 객체가 된다.
        /// GUID로 같은 항목을 다시 찾아줘야 목록의 선택 표시가 유지된다.
        /// </summary>
        private void RestoreSelectionAfterReload()
        {
            if (_selected == null) return;

            string guid = _selected.Guid;
            _selected = _allEntries.FirstOrDefault(e => string.Equals(e.Guid, guid, StringComparison.Ordinal));

            // 클립과 파형은 그릴 때 EnsureSelectionResources가 다시 만든다
            _selectedIndex = -1;
            _selectedClip = null;
            DisposePreviewWaveform();
        }

        /// <summary>스캔 폴더를 다시 훑어 인덱스를 새로 만든다.</summary>
        private void RebuildIndex()
        {
            var entries = SoundIndexer.BuildIndex(ParseLines(_scanRootsText, SoundIndexer.DefaultScanRoots), ParseLines(_excludesText, null));
            if (entries == null)
            {
                // 사용자가 취소했거나 유효한 폴더가 없다
                return;
            }

            // 이전 인덱스의 파형 분석 결과는 살려둔다 — 다시 뽑으려면 시간이 오래 걸린다
            var previous = new Dictionary<string, SoundEntry>(StringComparer.Ordinal);
            foreach (var entry in _allEntries)
            {
                if (!string.IsNullOrEmpty(entry.Guid))
                {
                    previous[entry.Guid] = entry;
                }
            }

            foreach (var entry in entries)
            {
                if (!previous.TryGetValue(entry.Guid, out var old)) continue;

                entry.Analyzed = old.Analyzed;
                entry.Waveform = old.Waveform;
                entry.PeakLevel = old.PeakLevel;
                entry.RmsLevel = old.RmsLevel;
                entry.IsReferenced = old.IsReferenced;
            }

            _allEntries = entries;
            SoundIndexer.SaveIndex(_allEntries);
            _indexTimestamp = SoundIndexer.GetIndexTimestamp();

            SoundWaveformCache.Clear();
            ClearSelection();
            _filterDirty = true;
        }

        private static IEnumerable<string> ParseLines(string text, string[] fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback ?? Array.Empty<string>();
            }

            return text
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
        }

        #endregion

        #region 필터 / 정렬

        private void RebuildFilterIfNeeded()
        {
            if (!_filterDirty) return;
            _filterDirty = false;

            _filtered.Clear();

            var terms = _search
                .ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var entry in _allEntries)
            {
                if (!MatchesFolder(entry)) continue;

                // 카테고리는 OR 매칭 — 선택한 것 중 하나라도 해당하면 통과
                if (_categoryMask != 0 && (entry.Categories & _categoryMask) == 0) continue;

                if (_usageFilter == 1 && !entry.IsGameAsset) continue;
                if (_usageFilter == 2 && entry.IsGameAsset) continue;
                if (_usageFilter == 3 && !entry.IsReferenced) continue;
                if (_onlyUnanalyzed && entry.Analyzed) continue;

                bool matchesSearch = true;
                foreach (var term in terms)
                {
                    if (entry.SearchKey != null && entry.SearchKey.Contains(term)) continue;
                    matchesSearch = false;
                    break;
                }
                if (!matchesSearch) continue;

                _filtered.Add(entry);
            }

            SortFiltered();

            // 선택 항목이 필터에서 빠지면 인덱스를 다시 맞춘다
            _selectedIndex = _selected != null ? _filtered.IndexOf(_selected) : -1;
        }

        private bool MatchesFolder(SoundEntry entry)
        {
            if (string.IsNullOrEmpty(_folderFilter)) return true;
            if (string.IsNullOrEmpty(entry.Folder)) return false;

            return entry.Folder.Equals(_folderFilter, StringComparison.OrdinalIgnoreCase)
                   || entry.Folder.StartsWith(_folderFilter + "/", StringComparison.OrdinalIgnoreCase);
        }

        private void SortFiltered()
        {
            Comparison<SoundEntry> comparison;
            switch (_sortMode)
            {
                case SortMode.Length:
                    comparison = (a, b) => a.Length.CompareTo(b.Length);
                    break;
                case SortMode.FileSize:
                    comparison = (a, b) => a.FileSize.CompareTo(b.FileSize);
                    break;
                case SortMode.Folder:
                    comparison = (a, b) =>
                    {
                        int folder = string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
                        return folder != 0 ? folder : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    };
                    break;
                case SortMode.Loudness:
                    comparison = (a, b) => a.RmsLevel.CompareTo(b.RmsLevel);
                    break;
                default:
                    comparison = (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    break;
            }

            _filtered.Sort(comparison);
            if (_sortDescending)
            {
                _filtered.Reverse();
            }
        }

        /// <summary>헤더 클릭 정렬 — 같은 기준을 다시 누르면 방향만 뒤집는다.</summary>
        private void SetSort(SortMode mode)
        {
            if (_sortMode == mode)
            {
                _sortDescending = !_sortDescending;
            }
            else
            {
                _sortMode = mode;
                _sortDescending = false;
            }
            _filterDirty = true;
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            SoundWaveformCache.BeginFrame();
            RebuildFilterIfNeeded();

            DrawToolbar();
            if (_showCategories)
            {
                DrawCategoryBar();
            }
            if (_showSettings)
            {
                DrawSettingsPanel();
            }

            // Layout 이벤트에서는 GetLastRect가 더미 값을 주므로, Repaint 때 잰 값을 그대로 재사용한다.
            // 두 이벤트가 다른 y를 쓰면 프리뷰 패널의 GUILayout 요소가 엉뚱한 위치에 얹힌다.
            if (Event.current.type == EventType.Repaint)
            {
                _bodyTop = GUILayoutUtility.GetLastRect().yMax + 2f;
            }

            float bodyHeight = position.height - _bodyTop - StatusBarHeight;
            if (bodyHeight > 0f)
            {
                DrawBody(new Rect(0f, _bodyTop, position.width, bodyHeight));
            }

            DrawStatusBar(new Rect(0f, position.height - StatusBarHeight, position.width, StatusBarHeight));
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
                if (EditorGUI.EndChangeCheck())
                {
                    _filterDirty = true;
                }

                if (GUILayout.Button(BuildFolderButtonLabel(), EditorStyles.toolbarDropDown, GUILayout.Width(180f)))
                {
                    ShowFolderMenu();
                }

                EditorGUI.BeginChangeCheck();
                _usageFilter = EditorGUILayout.Popup(_usageFilter, UsageFilterLabels, EditorStyles.toolbarPopup, GUILayout.Width(100f));
                _onlyUnanalyzed = GUILayout.Toggle(_onlyUnanalyzed, "미분석만", EditorStyles.toolbarButton, GUILayout.Width(64f));
                if (EditorGUI.EndChangeCheck())
                {
                    _filterDirty = true;
                }

                EditorGUI.BeginChangeCheck();
                int sortIndex = EditorGUILayout.Popup((int)_sortMode, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(70f));
                if (EditorGUI.EndChangeCheck())
                {
                    _sortMode = (SortMode)sortIndex;
                    _filterDirty = true;
                }

                if (GUILayout.Button(_sortDescending ? "▼" : "▲", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                {
                    _sortDescending = !_sortDescending;
                    _filterDirty = true;
                }

                GUILayout.FlexibleSpace();

                _autoPlay = GUILayout.Toggle(_autoPlay, "자동 재생", EditorStyles.toolbarButton, GUILayout.Width(66f));

                using (new EditorGUI.DisabledScope(_batcher.IsRunning))
                {
                    if (GUILayout.Button("인덱스 재생성", EditorStyles.toolbarButton, GUILayout.Width(88f)))
                    {
                        RebuildIndex();
                    }

                    if (GUILayout.Button(BuildAnalyzeButtonLabel(), EditorStyles.toolbarButton, GUILayout.Width(112f)))
                    {
                        StartBatchAnalysis();
                    }
                }

                if (_batcher.IsRunning && GUILayout.Button("중단", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                {
                    _batcher.Stop();
                }

                _showCategories = GUILayout.Toggle(_showCategories, "분류", EditorStyles.toolbarButton, GUILayout.Width(38f));
                _showSettings = GUILayout.Toggle(_showSettings, "설정", EditorStyles.toolbarButton, GUILayout.Width(38f));
            }
        }

        private string BuildAnalyzeButtonLabel()
        {
            int pending = _allEntries.Count(e => !e.Analyzed);
            return pending > 0 ? $"파형 분석 ({pending:N0})" : "파형 재분석";
        }

        private string BuildFolderButtonLabel()
        {
            if (string.IsNullOrEmpty(_folderFilter)) return "폴더: 전체";

            const string prefix = "Assets/";
            string relative = _folderFilter.StartsWith(prefix, StringComparison.Ordinal)
                ? _folderFilter.Substring(prefix.Length)
                : _folderFilter;

            // 경로가 길면 뒤쪽(더 구체적인 부분)을 남긴다
            return relative.Length > 24 ? "폴더: …" + relative.Substring(relative.Length - 23) : "폴더: " + relative;
        }

        /// <summary>
        /// 폴더 필터 메뉴. 경로에 '/'가 들어가면 GenericMenu가 알아서 계층을 만들어 준다.
        /// 하위 폴더가 있는 항목은 그대로 두면 서브메뉴가 되어 고를 수 없으므로 "전체" 항목을 따로 넣는다.
        /// </summary>
        private void ShowFolderMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("전체"), string.IsNullOrEmpty(_folderFilter), () =>
            {
                _folderFilter = null;
                _filterDirty = true;
            });
            menu.AddSeparator(string.Empty);

            // 각 폴더의 하위 포함 클립 수를 세어 메뉴에 함께 보여준다
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in _allEntries)
            {
                if (string.IsNullOrEmpty(entry.Folder)) continue;

                string folder = entry.Folder;
                while (!string.IsNullOrEmpty(folder))
                {
                    counts.TryGetValue(folder, out int count);
                    counts[folder] = count + 1;

                    int slash = folder.LastIndexOf('/');
                    if (slash <= 0) break;
                    folder = folder.Substring(0, slash);
                }
            }

            var folders = counts.Keys.Where(f => f.StartsWith("Assets/", StringComparison.Ordinal)).ToList();
            folders.Sort(StringComparer.OrdinalIgnoreCase);

            var withChildren = new HashSet<string>(StringComparer.Ordinal);
            foreach (var folder in folders)
            {
                int slash = folder.LastIndexOf('/');
                if (slash > 0)
                {
                    withChildren.Add(folder.Substring(0, slash));
                }
            }

            foreach (var folder in folders)
            {
                string relative = folder.Substring("Assets/".Length);
                string label = withChildren.Contains(folder)
                    ? $"{relative}/＊ 이 폴더 전체 ({counts[folder]:N0})"
                    : $"{relative} ({counts[folder]:N0})";

                string captured = folder;
                menu.AddItem(new GUIContent(label), string.Equals(_folderFilter, folder, StringComparison.Ordinal), () =>
                {
                    _folderFilter = captured;
                    _filterDirty = true;
                    Repaint();
                });
            }

            menu.ShowAsContext();
        }

        private void DrawCategoryBar()
        {
            var categories = SoundCategoryClassifier.AllCategories;
            const float toggleWidth = 72f;
            float available = position.width - 90f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(available / toggleWidth));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < categories.Length; i += perRow)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (i == 0)
                        {
                            EditorGUI.BeginChangeCheck();
                            bool all = _categoryMask == 0;
                            all = GUILayout.Toggle(all, "전체", EditorStyles.miniButton, GUILayout.Width(46f));
                            if (EditorGUI.EndChangeCheck() && all)
                            {
                                _categoryMask = 0;
                                _filterDirty = true;
                            }
                        }
                        else
                        {
                            GUILayout.Space(50f);
                        }

                        int end = Mathf.Min(i + perRow, categories.Length);
                        for (int j = i; j < end; j++)
                        {
                            var category = categories[j];
                            int bit = (int)category;
                            bool on = (_categoryMask & bit) != 0;

                            EditorGUI.BeginChangeCheck();
                            on = GUILayout.Toggle(on, SoundCategoryClassifier.GetDisplayName(category),
                                EditorStyles.miniButton, GUILayout.Width(toggleWidth));
                            if (EditorGUI.EndChangeCheck())
                            {
                                _categoryMask = on ? _categoryMask | bit : _categoryMask & ~bit;
                                _filterDirty = true;
                            }
                        }

                        GUILayout.FlexibleSpace();
                    }
                }
            }
        }

        private void DrawSettingsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField("스캔 폴더 (한 줄에 하나)", EditorStyles.boldLabel);
                        _scanRootsText = EditorGUILayout.TextArea(_scanRootsText, GUILayout.Height(46f));
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField("제외할 경로 조각 (한 줄에 하나)", EditorStyles.boldLabel);
                        _excludesText = EditorGUILayout.TextArea(_excludesText, GUILayout.Height(46f));
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("행 높이", EditorStyles.miniLabel, GUILayout.Width(44f));
                    _rowHeight = GUILayout.HorizontalSlider(_rowHeight, MinRowHeight, MaxRowHeight, GUILayout.Width(90f));

                    GUILayout.Space(12f);

                    if (GUILayout.Button("기본값 복원", GUILayout.Width(90f)))
                    {
                        _scanRootsText = string.Join("\n", SoundIndexer.DefaultScanRoots);
                        _excludesText = string.Empty;
                        GUI.FocusControl(null);
                    }

                    if (GUILayout.Button("사용처 분석", GUILayout.Width(90f)))
                    {
                        RunReferenceAnalysis();
                    }

                    if (GUILayout.Button("인덱스 삭제", GUILayout.Width(90f)))
                    {
                        if (EditorUtility.DisplayDialog("인덱스 삭제",
                                "저장된 인덱스(파형 분석 결과 포함)를 지웁니다.\n다시 만들려면 시간이 걸립니다. 계속할까요?",
                                "삭제", "취소"))
                        {
                            DeleteIndex();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"캐시: {SoundIndexer.CacheRoot}", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawStatusBar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);

            var labelRect = new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, rect.height - 4f);

            if (_batcher.IsRunning)
            {
                string info = $"파형 분석 {_batcher.Processed:N0} / {_batcher.Total:N0}" +
                              (_batcher.Failed > 0 ? $" (실패 {_batcher.Failed:N0})" : string.Empty) +
                              $" · {_batcher.CurrentName}";
                EditorGUI.ProgressBar(labelRect, _batcher.Progress, info);
                return;
            }

            if (_allEntries.Count == 0)
            {
                GUI.Label(labelRect, "인덱스가 없습니다 — 툴바의 [인덱스 재생성]을 눌러 시작하세요.", EditorStyles.miniLabel);
                return;
            }

            int analyzed = _allEntries.Count(e => e.Analyzed);
            double totalSeconds = _filtered.Sum(e => (double)e.Length);
            string timestamp = string.IsNullOrEmpty(_indexTimestamp) ? "없음" : _indexTimestamp;

            string status = $"표시 {_filtered.Count:N0} / 전체 {_allEntries.Count:N0}개 · " +
                            $"합계 {FormatDuration(totalSeconds)} · 파형 {analyzed:N0}개 분석됨 · 인덱스 {timestamp}";

            if (!SoundPreviewPlayer.IsAvailable)
            {
                status = "에디터 재생 API를 찾지 못했습니다 (재생 불가) · " + status;
            }

            GUI.Label(labelRect, status, EditorStyles.miniLabel);
        }

        #endregion

        #region 동작

        private void StartBatchAnalysis()
        {
            if (_allEntries.Count == 0)
            {
                EditorUtility.DisplayDialog("사운드 브라우저", "먼저 [인덱스 재생성]으로 사운드 목록을 만들어주세요.", "확인");
                return;
            }

            // 아직 분석되지 않은 것이 있으면 그것만, 모두 끝났으면 현재 필터 대상을 다시 분석한다
            var pending = _allEntries.Where(e => !e.Analyzed).ToList();
            bool reanalyze = pending.Count == 0;
            var targets = reanalyze ? new List<SoundEntry>(_filtered) : pending;

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("사운드 브라우저", "분석할 대상이 없습니다.", "확인");
                return;
            }

            if (reanalyze)
            {
                bool proceed = EditorUtility.DisplayDialog("파형 재분석",
                    $"현재 필터에 걸린 {targets.Count:N0}개의 파형을 다시 뽑습니다.\n\n" +
                    "진행 중에도 에디터는 계속 쓸 수 있고, 언제든 [중단]할 수 있습니다.",
                    "시작", "취소");
                if (!proceed) return;

                foreach (var entry in targets)
                {
                    entry.Analyzed = false;
                }
            }

            _player.StopPlayback();
            _batcher.Start(targets);
        }

        private void RunReferenceAnalysis()
        {
            if (_allEntries.Count == 0) return;

            int count = SoundIndexer.AnalyzeReferences(_allEntries);
            if (count < 0)
            {
                // 사용자 취소
                return;
            }

            SoundIndexer.SaveIndex(_allEntries);
            _filterDirty = true;
            ShowNotification(new GUIContent($"게임에서 참조 중인 사운드 {count:N0}개"));
        }

        private void DeleteIndex()
        {
            _batcher.Stop();
            _player.Stop();

            SoundIndexer.DeleteIndex();
            SoundWaveformCache.Clear();

            _allEntries.Clear();
            _indexTimestamp = string.Empty;
            ClearSelection();
            _filterDirty = true;
            Repaint();
        }

        private void ClearSelection()
        {
            _selected = null;
            _selectedIndex = -1;
            _selectedClip = null;
            DisposePreviewWaveform();
        }

        #endregion

        #region 표시 형식

        /// <summary>초를 "1:23" 또는 "0.42초" 형태로 바꾼다.</summary>
        private static string FormatLength(float seconds)
        {
            if (seconds <= 0f) return "-";
            if (seconds < 10f) return $"{seconds:0.00}s";
            if (seconds < 60f) return $"{seconds:0.0}s";

            int minutes = (int)(seconds / 60f);
            int remainder = Mathf.RoundToInt(seconds - minutes * 60f);
            if (remainder == 60)
            {
                minutes++;
                remainder = 0;
            }
            return $"{minutes}:{remainder:00}";
        }

        /// <summary>합계 재생 시간을 사람이 읽기 좋게 바꾼다.</summary>
        private static string FormatDuration(double seconds)
        {
            if (seconds < 60.0) return $"{seconds:0}초";

            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1.0
                ? $"{(int)span.TotalHours}시간 {span.Minutes}분"
                : $"{span.Minutes}분 {span.Seconds}초";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "-";
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#}KB";
            return $"{bytes / (1024.0 * 1024.0):0.##}MB";
        }

        #endregion
    }
}
