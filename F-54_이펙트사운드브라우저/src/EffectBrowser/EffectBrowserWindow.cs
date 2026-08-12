// 이펙트 브라우저 — 메인 윈도우 (상태, 툴바, 필터)
// 프로젝트에 흩어진 수천 개의 파티클 이펙트를 썸네일 격자로 훑고, 고른 것을 실제로 재생해 확인한다.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PX.EffectBrowser
{
    /// <summary>
    /// 이펙트 에셋 탐색 윈도우.
    /// </summary>
    public partial class EffectBrowserWindow : EditorWindow
    {
        #region 상수 / EditorPrefs 키

        private const string PrefThumbSize = "EffectBrowser_ThumbSize";
        private const string PrefPreviewWidth = "EffectBrowser_PreviewWidth";
        private const string PrefScanRoots = "EffectBrowser_ScanRoots";
        private const string PrefIncludeNonParticle = "EffectBrowser_IncludeNonParticle";

        private const float MinThumbSize = 56f;
        private const float MaxThumbSize = 200f;
        private const float StatusBarHeight = 22f;
        private const float MinPreviewWidth = 240f;
        private const float SplitterWidth = 5f;

        /// <summary>사용처 필터 항목. "참조 중"은 설정 패널의 [사용처 분석]을 한 번 돌려야 의미가 있다.</summary>
        private static readonly string[] UsageFilterLabels = { "전체", "게임 편입", "미편입(스토어)", "참조 중" };

        #endregion

        #region 상태

        private List<EffectEntry> _allEntries = new List<EffectEntry>();
        private readonly List<EffectEntry> _filtered = new List<EffectEntry>();
        private string[] _packageNames = { "전체" };

        private string _search = string.Empty;
        private int _categoryMask;
        private int _packageIndex;
        private int _usageFilter;
        private bool _onlyUncaptured;
        private bool _filterDirty = true;

        private float _thumbSize = 112f;
        private float _previewWidth = 340f;
        private bool _draggingSplitter;
        private Vector2 _gridScroll;
        private bool _showSettings;

        private string _scanRootsText;
        private bool _includeNonParticle;

        private EffectEntry _selected;
        private int _selectedIndex = -1;

        private readonly EffectThumbnailBatcher _batcher = new EffectThumbnailBatcher();
        private string _indexTimestamp = string.Empty;

        #endregion

        #region 윈도우 수명주기

        [MenuItem("PX Editor/이펙트 브라우저")]
        public static void Open()
        {
            var window = GetWindow<EffectBrowserWindow>("이펙트 브라우저");
            window.minSize = new Vector2(720f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            _thumbSize = EditorPrefs.GetFloat(PrefThumbSize, 112f);
            _previewWidth = EditorPrefs.GetFloat(PrefPreviewWidth, 340f);
            _scanRootsText = EditorPrefs.GetString(PrefScanRoots, string.Join("\n", EffectIndexer.DefaultScanRoots));
            _includeNonParticle = EditorPrefs.GetBool(PrefIncludeNonParticle, false);

            LoadIndexFromDisk();

            _batcher.OnProgressChanged += Repaint;
            _batcher.OnFinished += OnBatchFinished;

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorPrefs.SetFloat(PrefThumbSize, _thumbSize);
            EditorPrefs.SetFloat(PrefPreviewWidth, _previewWidth);
            EditorPrefs.SetString(PrefScanRoots, _scanRootsText ?? string.Empty);
            EditorPrefs.SetBool(PrefIncludeNonParticle, _includeNonParticle);

            EditorApplication.update -= OnEditorUpdate;

            _batcher.OnProgressChanged -= Repaint;
            _batcher.OnFinished -= OnBatchFinished;
            _batcher.Stop();

            DisposePreview();
            EffectThumbnailCache.Clear();
        }

        /// <summary>배치 캡처가 끝나면 인덱스에 채워진 부가 정보를 디스크에 반영한다.</summary>
        private void OnBatchFinished()
        {
            if (_allEntries.Count > 0)
            {
                EffectIndexer.SaveIndex(_allEntries);
                _indexTimestamp = EffectIndexer.GetIndexTimestamp();
            }
            Repaint();
        }

        #endregion

        #region 인덱스

        private void LoadIndexFromDisk()
        {
            var loaded = EffectIndexer.LoadIndex();
            if (loaded != null)
            {
                _allEntries = loaded;
                _indexTimestamp = EffectIndexer.GetIndexTimestamp();
            }
            RebuildPackageList();
            _filterDirty = true;
        }

        /// <summary>스캔 폴더를 다시 훑어 인덱스를 새로 만든다.</summary>
        private void RebuildIndex()
        {
            var roots = ParseScanRoots();
            var entries = EffectIndexer.BuildIndex(roots, _includeNonParticle);
            if (entries == null)
            {
                // 사용자가 취소했거나 유효한 폴더가 없다
                return;
            }

            // 이전 인덱스에 기록된 부가 정보는 살려둔다 (썸네일 파일이 그대로 남아 있으므로)
            var previous = _allEntries.ToDictionary(e => e.Guid, e => e);
            foreach (var entry in entries)
            {
                if (!previous.TryGetValue(entry.Guid, out var old)) continue;

                entry.ParticleSystemCount = old.ParticleSystemCount;
                entry.Duration = old.Duration;
                entry.HasAnimator = old.HasAnimator;
                entry.IsReferenced = old.IsReferenced;
            }
            EffectIndexer.SyncCapturedFlags(entries);

            _allEntries = entries;
            EffectIndexer.SaveIndex(_allEntries);
            _indexTimestamp = EffectIndexer.GetIndexTimestamp();

            RebuildPackageList();
            _filterDirty = true;
            _selected = null;
            _selectedIndex = -1;
        }

        private IEnumerable<string> ParseScanRoots()
        {
            if (string.IsNullOrWhiteSpace(_scanRootsText))
            {
                return EffectIndexer.DefaultScanRoots;
            }

            return _scanRootsText
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
        }

        private void RebuildPackageList()
        {
            var packages = _allEntries
                .Select(e => e.Package)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            packages.Insert(0, "전체");
            _packageNames = packages.ToArray();

            if (_packageIndex >= _packageNames.Length)
            {
                _packageIndex = 0;
            }
        }

        #endregion

        #region 필터

        private void RebuildFilterIfNeeded()
        {
            if (!_filterDirty) return;
            _filterDirty = false;

            _filtered.Clear();

            var terms = _search
                .ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            string package = _packageIndex > 0 && _packageIndex < _packageNames.Length
                ? _packageNames[_packageIndex]
                : null;

            foreach (var entry in _allEntries)
            {
                if (package != null && !string.Equals(entry.Package, package, StringComparison.Ordinal)) continue;

                // 카테고리는 OR 매칭 — 선택한 것 중 하나라도 해당하면 통과
                if (_categoryMask != 0 && (entry.Categories & _categoryMask) == 0) continue;

                if (_usageFilter == 1 && !entry.IsGameAsset) continue;
                if (_usageFilter == 2 && entry.IsGameAsset) continue;
                if (_usageFilter == 3 && !entry.IsReferenced) continue;
                if (_onlyUncaptured && entry.Captured) continue;

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

            // 선택 항목이 필터에서 빠지면 인덱스를 다시 맞춘다
            _selectedIndex = _selected != null ? _filtered.IndexOf(_selected) : -1;
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            EffectThumbnailCache.BeginFrame();
            RebuildFilterIfNeeded();

            DrawToolbar();
            DrawCategoryBar();
            if (_showSettings)
            {
                DrawSettingsPanel();
            }

            float bodyTop = GUILayoutUtility.GetLastRect().yMax + 2f;
            float bodyHeight = position.height - bodyTop - StatusBarHeight;
            if (bodyHeight > 0f)
            {
                DrawBody(new Rect(0f, bodyTop, position.width, bodyHeight));
            }

            DrawStatusBar(new Rect(0f, position.height - StatusBarHeight, position.width, StatusBarHeight));
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(140f));
                if (EditorGUI.EndChangeCheck())
                {
                    _filterDirty = true;
                }

                EditorGUI.BeginChangeCheck();
                _packageIndex = EditorGUILayout.Popup(_packageIndex, _packageNames, EditorStyles.toolbarPopup, GUILayout.Width(190f));
                _usageFilter = EditorGUILayout.Popup(_usageFilter, UsageFilterLabels, EditorStyles.toolbarPopup, GUILayout.Width(110f));
                _onlyUncaptured = GUILayout.Toggle(_onlyUncaptured, "미캡처만", EditorStyles.toolbarButton, GUILayout.Width(70f));
                if (EditorGUI.EndChangeCheck())
                {
                    _filterDirty = true;
                }

                GUILayout.Space(8f);
                GUILayout.Label("크기", EditorStyles.miniLabel, GUILayout.Width(28f));
                _thumbSize = GUILayout.HorizontalSlider(_thumbSize, MinThumbSize, MaxThumbSize, GUILayout.Width(80f));

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_batcher.IsRunning))
                {
                    if (GUILayout.Button("인덱스 재생성", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                    {
                        RebuildIndex();
                    }

                    if (GUILayout.Button(BuildCaptureButtonLabel(), EditorStyles.toolbarButton, GUILayout.Width(130f)))
                    {
                        StartBatchCapture();
                    }
                }

                if (_batcher.IsRunning && GUILayout.Button("중단", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                {
                    _batcher.Stop();
                }

                _showSettings = GUILayout.Toggle(_showSettings, "설정", EditorStyles.toolbarButton, GUILayout.Width(44f));
            }
        }

        private string BuildCaptureButtonLabel()
        {
            int pending = _allEntries.Count(e => !e.Captured);
            return pending > 0 ? $"썸네일 캡처 ({pending:N0})" : "썸네일 재캡처";
        }

        private void DrawCategoryBar()
        {
            var categories = EffectCategoryClassifier.AllCategories;
            const float toggleWidth = 76f;
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
                            on = GUILayout.Toggle(on, EffectCategoryClassifier.GetDisplayName(category),
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
                EditorGUILayout.LabelField("스캔 폴더 (한 줄에 하나)", EditorStyles.boldLabel);
                _scanRootsText = EditorGUILayout.TextArea(_scanRootsText, GUILayout.Height(60f));

                _includeNonParticle = EditorGUILayout.ToggleLeft(
                    "파티클/트레일/라인이 없는 프리팹도 포함 (메시 전용 이펙트까지 잡히지만 노이즈가 늘어남)",
                    _includeNonParticle);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("기본값 복원", GUILayout.Width(110f)))
                    {
                        _scanRootsText = string.Join("\n", EffectIndexer.DefaultScanRoots);
                        GUI.FocusControl(null);
                    }

                    if (GUILayout.Button("사용처 분석", GUILayout.Width(110f)))
                    {
                        RunReferenceAnalysis();
                    }

                    if (GUILayout.Button("썸네일 캐시 삭제", GUILayout.Width(130f)))
                    {
                        if (EditorUtility.DisplayDialog("썸네일 캐시 삭제",
                                $"저장된 썸네일 {EffectThumbnailCache.CountOnDisk():N0}장을 모두 지웁니다.\n다시 캡처하려면 시간이 걸립니다. 계속할까요?",
                                "삭제", "취소"))
                        {
                            DeleteAllThumbnails();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"캐시 폴더: {EffectIndexer.CacheRoot}", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawStatusBar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);

            var labelRect = new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, rect.height - 4f);

            if (_batcher.IsRunning)
            {
                var barRect = new Rect(labelRect.x, labelRect.y, labelRect.width, labelRect.height);
                string info = $"썸네일 캡처 {_batcher.Processed:N0} / {_batcher.Total:N0}" +
                              (_batcher.Failed > 0 ? $" (실패 {_batcher.Failed:N0})" : string.Empty) +
                              $" · {_batcher.CurrentName}";
                EditorGUI.ProgressBar(barRect, _batcher.Progress, info);
                return;
            }

            int captured = _allEntries.Count(e => e.Captured);
            string timestamp = string.IsNullOrEmpty(_indexTimestamp) ? "없음" : _indexTimestamp;
            string status = _allEntries.Count == 0
                ? "인덱스가 없습니다 — 툴바의 [인덱스 재생성]을 눌러 시작하세요."
                : $"표시 {_filtered.Count:N0} / 전체 {_allEntries.Count:N0}개 · 썸네일 {captured:N0}장 · 인덱스 {timestamp}";

            GUI.Label(labelRect, status, EditorStyles.miniLabel);
        }

        #endregion

        #region 동작

        private void StartBatchCapture()
        {
            if (_allEntries.Count == 0)
            {
                EditorUtility.DisplayDialog("이펙트 브라우저", "먼저 [인덱스 재생성]으로 이펙트 목록을 만들어주세요.", "확인");
                return;
            }

            // 아직 캡처되지 않은 것이 있으면 그것만, 모두 캡처된 상태면 현재 필터 대상을 다시 찍는다
            var pending = _allEntries.Where(e => !e.Captured).ToList();
            bool recapture = pending.Count == 0;
            var targets = recapture ? new List<EffectEntry>(_filtered) : pending;

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("이펙트 브라우저", "캡처할 대상이 없습니다.", "확인");
                return;
            }

            string message = recapture
                ? $"현재 필터에 걸린 {targets.Count:N0}개의 썸네일을 다시 캡처합니다."
                : $"아직 썸네일이 없는 {targets.Count:N0}개를 캡처합니다.";

            // 건당 0.1~0.3초가 걸리므로 대략적인 예상 시간을 미리 알려준다
            int estimatedMinutes = Mathf.CeilToInt(targets.Count * 0.18f / 60f);
            message += $"\n\n예상 소요: 약 {estimatedMinutes}분\n진행 중에도 에디터는 계속 쓸 수 있고, 언제든 [중단]할 수 있습니다.";

            if (!EditorUtility.DisplayDialog("썸네일 배치 캡처", message, "시작", "취소"))
            {
                return;
            }

            StopPlayback();
            _batcher.Start(targets);
        }

        private void RunReferenceAnalysis()
        {
            if (_allEntries.Count == 0) return;

            int count = EffectIndexer.AnalyzeReferences(_allEntries);
            if (count < 0)
            {
                // 사용자 취소
                return;
            }

            EffectIndexer.SaveIndex(_allEntries);
            _filterDirty = true;
            ShowNotification(new GUIContent($"게임에서 참조 중인 이펙트 {count:N0}개"));
        }

        private void DeleteAllThumbnails()
        {
            EffectThumbnailCache.DeleteAllOnDisk();

            foreach (var entry in _allEntries)
            {
                entry.Captured = false;
            }
            EffectIndexer.SaveIndex(_allEntries);

            _filterDirty = true;
            Repaint();
        }

        #endregion
    }
}
