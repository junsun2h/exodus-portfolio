// 이펙트 브라우저 — 썸네일 격자
// 항목이 수천 개이므로 화면에 보이는 행만 그린다. 전부 그리면 스크롤이 즉시 멈춘다.

using UnityEditor;
using UnityEngine;

namespace PX.EffectBrowser
{
    public partial class EffectBrowserWindow
    {
        #region 상수

        /// <summary>셀 사이 여백</summary>
        private const float CellPadding = 8f;

        /// <summary>셀 하단 이름 라벨 높이</summary>
        private const float CellLabelHeight = 15f;

        /// <summary>세로 스크롤바 폭</summary>
        private const float ScrollbarWidth = 15f;

        #endregion

        #region 스타일 (지연 생성 — OnEnable 시점에는 EditorStyles가 준비되지 않을 수 있다)

        private GUIStyle _cellLabelStyle;
        private GUIStyle _cellPlaceholderStyle;

        private GUIStyle CellLabelStyle
        {
            get
            {
                if (_cellLabelStyle == null)
                {
                    _cellLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        clipping = TextClipping.Clip,
                        wordWrap = false,
                    };
                }
                return _cellLabelStyle;
            }
        }

        private GUIStyle CellPlaceholderStyle
        {
            get
            {
                if (_cellPlaceholderStyle == null)
                {
                    _cellPlaceholderStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap = true,
                    };
                }
                return _cellPlaceholderStyle;
            }
        }

        #endregion

        #region 본문 레이아웃

        private void DrawBody(Rect rect)
        {
            float maxPreview = Mathf.Max(MinPreviewWidth, rect.width - 240f);
            _previewWidth = Mathf.Clamp(_previewWidth, MinPreviewWidth, maxPreview);

            float gridWidth = rect.width - _previewWidth - SplitterWidth;

            var gridRect = new Rect(rect.x, rect.y, gridWidth, rect.height);
            var splitterRect = new Rect(gridRect.xMax, rect.y, SplitterWidth, rect.height);
            var previewRect = new Rect(splitterRect.xMax, rect.y, _previewWidth, rect.height);

            HandleGridKeyboard();
            DrawGrid(gridRect);
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

        #region 격자

        private void DrawGrid(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.17f));

            if (_filtered.Count == 0)
            {
                string message = _allEntries.Count == 0
                    ? "인덱스가 비어 있습니다.\n툴바의 [인덱스 재생성]을 눌러 이펙트를 스캔하세요."
                    : "조건에 맞는 이펙트가 없습니다.\n검색어나 필터를 바꿔보세요.";
                GUI.Label(rect, message, CellPlaceholderStyle);
                return;
            }

            float cellWidth = _thumbSize + CellPadding;
            float cellHeight = _thumbSize + CellLabelHeight + CellPadding;

            int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width - ScrollbarWidth) / cellWidth));
            int rows = Mathf.CeilToInt((float)_filtered.Count / columns);

            var viewRect = new Rect(0f, 0f, rect.width - ScrollbarWidth, rows * cellHeight);
            _gridScroll = GUI.BeginScrollView(rect, _gridScroll, viewRect);

            // 화면 밖 행은 건너뛴다 (앞뒤로 한 행씩 여유를 둬서 스크롤 시 빈칸이 보이지 않게 한다)
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(_gridScroll.y / cellHeight) - 1);
            int lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((_gridScroll.y + rect.height) / cellHeight));

            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index >= _filtered.Count) break;

                    var cellRect = new Rect(
                        column * cellWidth + CellPadding * 0.5f,
                        row * cellHeight + CellPadding * 0.5f,
                        _thumbSize,
                        _thumbSize + CellLabelHeight);

                    DrawCell(_filtered[index], index, cellRect);
                }
            }

            GUI.EndScrollView();

            _gridColumns = columns;
        }

        /// <summary>키보드 이동에 필요한 현재 열 수</summary>
        private int _gridColumns = 1;

        private void DrawCell(EffectEntry entry, int index, Rect rect)
        {
            var thumbRect = new Rect(rect.x, rect.y, rect.width, rect.width);
            var labelRect = new Rect(rect.x, thumbRect.yMax, rect.width, CellLabelHeight);

            bool isSelected = ReferenceEquals(entry, _selected);

            // 배경
            EditorGUI.DrawRect(thumbRect, isSelected
                ? new Color(0.24f, 0.38f, 0.58f)
                : new Color(0.11f, 0.11f, 0.13f));

            var texture = entry.Captured ? EffectThumbnailCache.Get(entry.Guid) : null;
            if (texture != null)
            {
                var inner = new Rect(thumbRect.x + 1f, thumbRect.y + 1f, thumbRect.width - 2f, thumbRect.height - 2f);
                GUI.DrawTexture(inner, texture, ScaleMode.ScaleToFit);
            }
            else
            {
                string placeholder = entry.Captured ? "로딩…" : "미캡처";
                GUI.Label(thumbRect, placeholder, CellPlaceholderStyle);
            }

            // 게임에 편입된 이펙트는 좌상단에 표식을 남긴다
            if (entry.IsGameAsset)
            {
                var badgeRect = new Rect(thumbRect.x + 2f, thumbRect.y + 2f, 8f, 8f);
                EditorGUI.DrawRect(badgeRect, new Color(0.35f, 0.8f, 0.4f));
            }

            if (isSelected)
            {
                DrawSelectionOutline(thumbRect);
            }

            GUI.Label(labelRect, new GUIContent(entry.Name, entry.Path), CellLabelStyle);

            HandleCellInput(entry, index, rect);
        }

        private static void DrawSelectionOutline(Rect rect)
        {
            var color = new Color(0.45f, 0.68f, 1f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), color);
        }

        private void HandleCellInput(EffectEntry entry, int index, Rect rect)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0:
                    Select(entry, index);
                    if (evt.clickCount == 2)
                    {
                        PingInProject(entry);
                    }
                    evt.Use();
                    break;

                case EventType.MouseDown when evt.button == 1:
                    Select(entry, index);
                    ShowCellContextMenu(entry);
                    evt.Use();
                    break;

                case EventType.MouseDrag when evt.button == 0 && ReferenceEquals(entry, _selected):
                    StartDragToScene(entry);
                    evt.Use();
                    break;
            }
        }

        /// <summary>셀을 씬/하이어라키로 끌어다 놓을 수 있게 한다.</summary>
        private static void StartDragToScene(EffectEntry entry)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
            if (prefab == null) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { prefab };
            DragAndDrop.paths = new[] { entry.Path };
            DragAndDrop.StartDrag(entry.Name);
        }

        private void ShowCellContextMenu(EffectEntry entry)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Project에서 보기"), false, () => PingInProject(entry));
            menu.AddItem(new GUIContent("씬에 배치"), false, () => InstantiateIntoScene(entry));
            menu.AddItem(new GUIContent("에셋 경로 복사"), false, () => EditorGUIUtility.systemCopyBuffer = entry.Path);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("이 썸네일만 다시 캡처"), false, () => RecaptureSingle(entry));

            menu.ShowAsContext();
        }

        private void Select(EffectEntry entry, int index)
        {
            // 이미 선택된 항목을 다시 클릭하면 처음부터 다시 재생한다
            if (ReferenceEquals(entry, _selected))
            {
                RestartPlayback();
                Repaint();
                return;
            }

            _selected = entry;
            _selectedIndex = index;
            LoadPreviewTarget(entry);
            Repaint();
        }

        #endregion

        #region 키보드

        private void HandleGridKeyboard()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown || _filtered.Count == 0) return;

            // 검색창에 포커스가 있으면 방향키를 가로채지 않는다
            if (!string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())) return;

            int next = _selectedIndex;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow: next = _selectedIndex - 1; break;
                case KeyCode.RightArrow: next = _selectedIndex + 1; break;
                case KeyCode.UpArrow: next = _selectedIndex - _gridColumns; break;
                case KeyCode.DownArrow: next = _selectedIndex + _gridColumns; break;
                case KeyCode.Home: next = 0; break;
                case KeyCode.End: next = _filtered.Count - 1; break;
                default: return;
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
            if (_selectedIndex < 0 || _gridColumns <= 0) return;

            float cellHeight = _thumbSize + CellLabelHeight + CellPadding;
            int row = _selectedIndex / _gridColumns;
            float rowTop = row * cellHeight;

            float viewHeight = position.height - StatusBarHeight - 120f;
            if (rowTop < _gridScroll.y)
            {
                _gridScroll.y = rowTop;
            }
            else if (rowTop + cellHeight > _gridScroll.y + viewHeight)
            {
                _gridScroll.y = rowTop + cellHeight - viewHeight;
            }
        }

        #endregion

        #region 항목 조작

        private static void PingInProject(EffectEntry entry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
            if (asset == null) return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>현재 씬에 프리팹 인스턴스를 만든다. Undo로 되돌릴 수 있다.</summary>
        private static void InstantiateIntoScene(EffectEntry entry)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
            if (prefab == null) return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return;

            // 씬 뷰 카메라가 보고 있는 지점에 놓아야 바로 눈에 들어온다
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                instance.transform.position = sceneView.pivot;
            }

            Undo.RegisterCreatedObjectUndo(instance, $"이펙트 배치: {entry.Name}");
            Selection.activeGameObject = instance;
        }

        /// <summary>단일 항목의 썸네일을 즉시 다시 캡처한다.</summary>
        private void RecaptureSingle(EffectEntry entry)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Path);
            if (prefab == null) return;

            using (var renderer = new EffectPreviewRenderer())
            {
                if (!renderer.SetTarget(prefab)) return;

                float bestTime = renderer.FindBestThumbnailTime();
                renderer.SimulateTo(bestTime);
                renderer.FrameCamera();

                var texture = renderer.RenderStatic(EffectThumbnailCache.ThumbnailSize);
                if (texture == null) return;

                EffectThumbnailCache.Save(entry.Guid, texture);
                Object.DestroyImmediate(texture);

                entry.Captured = true;
                entry.ParticleSystemCount = renderer.ParticleSystemCount;
                entry.Duration = renderer.EstimatedDuration;
                entry.HasAnimator = renderer.HasAnimator;
            }

            EffectIndexer.SaveIndex(_allEntries);
            Repaint();
        }

        #endregion
    }
}
