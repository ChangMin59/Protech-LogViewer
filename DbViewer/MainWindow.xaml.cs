using DbViewer.Models;
using DbViewer.Services.Common;
using DbViewer.Services.Export;
using DbViewer.Services.Render;
using DbViewer.Services.View;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DbViewer
{
    public partial class MainWindow : Window
    {
        private LogRepository? _repository;

        private List<LogRow> _currentRows = new();

        private string _currentMode = "all";
        private string _currentKeyword = "";
        private string _currentStartDate = "";
        private string _currentEndDate = "";

        private int _currentPage = 1;
        private int _pageSize = 1000;
        private int _totalCount = 0;
        private int _totalPages = 1;

        private CancellationTokenSource? _renderCts;
        private CancellationTokenSource? _countCts;
        private CancellationTokenSource? _loadCts;
        private bool _isLogContentDragging = false;
        private Point _dragStartPoint;
        private double _dragStartHorizontalOffset;
        private double _dragStartVerticalOffset;

        private readonly object _categoryCacheLock = new();

        private readonly HashSet<string> _selectedCategories = new();

        private readonly Dictionary<string, List<LogRow>> _categoryCache = new()
        {
            ["fire"] = new List<LogRow>(),
            ["alarm"] = new List<LogRow>(),
            ["relay_fault"] = new List<LogRow>(),
            ["an_fault"] = new List<LogRow>(),
            ["line_fault"] = new List<LogRow>(),
            ["output"] = new List<LogRow>(),
            ["mcc"] = new List<LogRow>(),
            ["other"] = new List<LogRow>()
        };

        private bool _categoryCacheReady = false;
        private bool _categoryCacheBuilding = false;

        private TextBlock? _dateTargetText;
        private Border? _dateTargetButton;
        private DateTime? _dbStartDate;
        private DateTime? _dbEndDate;
        private bool _suppressDateCalendarChange = false;
        private DateTime _ignoreCategoryClickUntil = DateTime.MinValue;

        private readonly object _queryCacheLock = new();

        private readonly Dictionary<string, List<LogRow>> _rowsCacheByKey = new();
        private readonly Dictionary<string, Dictionary<string, List<LogRow>>> _categoryCacheByKey = new();
        private readonly Dictionary<string, int> _totalCountCacheByKey = new();

        private string _activeRowsCacheKey = "all";

        private string _baseModeBeforeCategory = "all";
        private string _baseCacheKeyBeforeCategory = "all";
        private string _baseKeywordBeforeCategory = "";
        private string _baseStartDateBeforeCategory = "";
        private string _baseEndDateBeforeCategory = "";

        private int _countJobVersion = 0;
        private bool _isMoveMode = false;
        private bool _isMoveNavigationRunning = false;
        private bool _isRecoveryRunning = false;
        private bool _isOpeningHistory = false;
        private bool _isExportRunning = false;
        private bool _lockLargeScreenWindowSize = false;
        private int? _highlightedRowIndexInPage = null;
        private DateTime _lastPointerActionAt = DateTime.MinValue;
        private DateTime _ignorePointerUntil = DateTime.MinValue;

        private const double LogRowVisualHeight = 50.0;
        private const string MoveTargetLineTag = "MoveTargetLine";
        private const int PointerActionDebounceMilliseconds = 450;
        private const int ModalCloseInputGuardMilliseconds = 500;
        private const int LargeScreenPhysicalHeightThreshold = 1900;
        private const double LargeScreenLeftBorderOffset = 10.0;
        private const double LargeScreenRightBorderOffset = 8.0;
        private const double LargeScreenBottomOffset = -10.0;

        private readonly Dictionary<string, string> _categoryDisplayNames = new()
        {
            ["fire"] = "화재",
            ["alarm"] = "제경보",
            ["relay_fault"] = "중계기 고장",
            ["an_fault"] = "AN 고장",
            ["line_fault"] = "단선",
            ["output"] = "출력",
            ["mcc"] = "MCC",
            ["other"] = "기타"
        };

        private sealed class QueryCacheEntry
        {
            public List<LogRow> Rows { get; init; } = new();
            public Dictionary<string, List<LogRow>> CategoryCache { get; init; } = new();
            public int TotalCount => Rows.Count;
        }
        private sealed class MoveTargetResult
        {
            public long RowId { get; init; }
            public int PageNumber { get; init; }
            public int RowIndexInPage { get; init; }
        }
        public MainWindow()
        {
            InitializeComponent();

            RootGrid.Focusable = true;
            SourceInitialized += (_, _) =>
            {
                if (IsLargePhysicalScreen())
                {
                    _lockLargeScreenWindowSize = true;
                    ApplyInitialWindowSizeForLargeScreen();
                    return;
                }
            };

            StateChanged += (_, _) => KeepLargeScreenWindowNormal();

            InitializeEvents();
            InitializeDefaultText();
        }

        private void ApplyInitialWindowSizeForLargeScreen()
        {
            if (!IsLargePhysicalScreen())
            {
                return;
            }

            Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            Drawing.Rectangle fullArea = screen.Bounds;
            Drawing.Rectangle workArea = screen.WorkingArea;

            if (fullArea.Width <= 0 || fullArea.Height <= 0)
            {
                return;
            }

            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;

            Matrix transformFromDevice = PresentationSource.FromVisual(this)
                ?.CompositionTarget
                ?.TransformFromDevice
                ?? Matrix.Identity;

            Point fullTopLeft = transformFromDevice.Transform(new Point(fullArea.Left, fullArea.Top));
            Point fullBottomRight = transformFromDevice.Transform(new Point(fullArea.Right, fullArea.Bottom));
            Point workBottomRight = transformFromDevice.Transform(new Point(workArea.Right, workArea.Bottom));

            double fullWidth = fullBottomRight.X - fullTopLeft.X;
            double availableHeight = workBottomRight.Y - fullTopLeft.Y - LargeScreenBottomOffset;

            Width = fullWidth + LargeScreenLeftBorderOffset + LargeScreenRightBorderOffset;
            Height = Math.Min(fullBottomRight.Y - fullTopLeft.Y, availableHeight);
            Left = fullTopLeft.X - LargeScreenLeftBorderOffset;
            Top = fullTopLeft.Y;

            MinWidth = Width;
            MaxWidth = Width;
            MinHeight = Height;
            MaxHeight = Height;
        }

        private static bool IsLargePhysicalScreen()
        {
            return Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds.Height > LargeScreenPhysicalHeightThreshold;
        }

        private void KeepLargeScreenWindowNormal()
        {
            if (!_lockLargeScreenWindowSize)
            {
                return;
            }

            if (WindowState == WindowState.Normal)
            {
                return;
            }

            WindowState = WindowState.Normal;
        }


        private void InitializeDefaultText()
        {
            ResetCategoryCountText();

            StartDateText.Text = "";
            EndDateText.Text = "";

            _dbStartDate = null;
            _dbEndDate = null;
            UpdateDateArrowVisibility();

            DateCalendarDropdown.Visibility = Visibility.Collapsed;

            PageSizeText.Text = $"{_pageSize:N0}건";

            _highlightedRowIndexInPage = null;

            FixedTimeRowsPanel.Children.Clear();
            LogRowsPanel.Children.Clear();

            FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedEmptyCell());
            LogRowsPanel.Children.Add(Render_Log_Row.CreateEmptyRow(""));

            UpdatePageSizeDropdownStyle();
            RebuildPaginationButtons();
        }

        private static void ShowMoveTargetMissingMessage(string categoryName)
        {
            MessageBox.Show(
                $"현재 화면 아래쪽의 다음 {categoryName} 로그가 없습니다.",
                "이동 모드",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private static void ShowLoadLogsFailedMessage()
        {
            MessageBox.Show(
                "이력 내용을 불러오는 중 오류가 발생했습니다.",
                "조회 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        private async Task ToggleMoveModeAsync()
        {
            if (_repository == null)
            {
                return;
            }

            _isMoveMode = !_isMoveMode;

            ModeToggleText.Text = _isMoveMode
                ? "이동 모드"
                : "필터 모드";

            ModeToggleText.Foreground = _isMoveMode
                ? new SolidColorBrush(Color.FromRgb(255, 90, 61))
                : new SolidColorBrush(Color.FromRgb(35, 57, 93));

            // 이동 모드로 들어갈 때는 기준 화면을 항상 전체 로그로 맞춘다.
            if (_isMoveMode)
            {
                ClearMoveTargetHighlight();

                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                _currentMode = "all";
                _currentKeyword = "";
                _currentStartDate = "";
                _currentEndDate = "";
                _currentPage = 1;
                _activeRowsCacheKey = "all";

                _totalCount = await Task.Run(() => _repository.CountAllLogs());
                _totalPages = CalculateTotalPages(_totalCount);

                TotalLogCountText.Text = _totalCount.ToString("N0");

                SaveBaseQueryState();

                await LoadCurrentPageAsync(showLoading: false, resetScroll: true);

                return;
            }

            /*
             * 이동 모드에서 필터 모드로 돌아갈 때는
             * 현재 화면은 유지하고 빨간줄만 제거한다.
             */
            ClearMoveTargetHighlight();
        }

        private async Task HandleCategoryButtonAsync(string category)
        {
            if (_isMoveMode)
            {
                await MoveToNextCategoryLogAsync(category);
                return;
            }

            await ToggleCategoryAsync(category);
        }
        private async Task ToggleCategoryAsync(string category)
        {
            if (ShouldIgnoreCategoryClick())
            {
                return;
            }

            if (_repository == null)
            {
                return;
            }

            /*
             * 필터 모드로 카테고리를 선택하는 순간 이동 모드의 빨간 이동선은 남기지 않는다.
             */
            ClearMoveTargetHighlight();

            if (_currentMode != "category")
            {
                SaveBaseQueryState();
            }

            if (_selectedCategories.Contains(category))
            {
                _selectedCategories.Remove(category);
            }
            else
            {
                _selectedCategories.Add(category);
            }

            UpdateCategoryCardActiveStates();

            if (_selectedCategories.Count == 0)
            {
                await RestoreBaseQueryAfterCategoryClearAsync();
                return;
            }

            _currentMode = "category";
            _currentPage = 1;

            await LoadCategoryPageFromCacheAsync();
        }
        private async Task MoveToNextCategoryLogAsync(string category)
        {
            if (_repository == null)
            {
                return;
            }

            if (_currentRows.Count == 0)
            {
                return;
            }

            if (_isMoveNavigationRunning)
            {
                return;
            }

            _isMoveNavigationRunning = true;

            try
            {
                LogRow? anchorRow = GetLastVisibleAnchorRow();

                if (anchorRow == null)
                {
                    return;
                }

                string categoryName = _categoryDisplayNames.TryGetValue(category, out string? name)
                    ? name
                    : category;

                MoveTargetResult? result = await Task.Run(() =>
                    FindNextCategoryLogPosition(category, anchorRow)
                );

                if (result == null)
                {
                    ShowMoveTargetMissingMessage(categoryName);

                    return;
                }

                /*
                 * 이동 모드에서는 필터링하지 않는다.
                 * 현재 전체/기간/검색 조회 범위는 유지하고, 해당 카테고리의 다음 위치로만 이동한다.
                 */
                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                RestoreMoveBaseModeIfNeeded();

                double targetOffset = result.RowIndexInPage * LogRowVisualHeight;

                /*
                 * 판매용 기준 핵심:
                 * 같은 페이지에 이미 렌더링된 행이면 절대 다시 렌더링하지 않는다.
                 * 기존 빨간줄 제거 -> 새 빨간줄 추가 -> 스크롤 이동만 수행한다.
                 */
                if (result.PageNumber == _currentPage)
                {
                    ApplyMoveTargetHighlight(result.RowIndexInPage);

                    LogScrollViewer.ScrollToVerticalOffset(targetOffset);
                    FixedTimeScrollViewer.ScrollToVerticalOffset(targetOffset);

                    return;
                }

                /*
                 * 다른 페이지로 넘어갈 때만 새 페이지를 렌더링한다.
                 * 이때도 배치 렌더링을 쓰면 스크롤바가 아래로 갔다가 돌아오는 움직임이 보일 수 있으므로
                 * 이동 모드 전용 즉시 렌더링으로 한 프레임 안에서 행 생성, 빨간줄 표시, 목표 위치 이동을 끝낸다.
                 */
                ClearMoveTargetHighlight();

                _currentPage = result.PageNumber;

                await LoadCurrentPageAsync(
                    showLoading: false,
                    resetScroll: false,
                    instantRender: true,
                    targetVerticalOffset: targetOffset,
                    highlightRowIndex: result.RowIndexInPage
                );
            }
            finally
            {
                _isMoveNavigationRunning = false;
            }
        }

        private void ApplyMoveTargetHighlight(int rowIndexInPage)
        {
            ClearMoveTargetHighlight();

            if (rowIndexInPage < 0)
            {
                return;
            }

            if (rowIndexInPage >= FixedTimeRowsPanel.Children.Count)
            {
                return;
            }

            if (rowIndexInPage >= LogRowsPanel.Children.Count)
            {
                return;
            }

            AddBottomHighlightLine(FixedTimeRowsPanel.Children[rowIndexInPage]);
            AddBottomHighlightLine(LogRowsPanel.Children[rowIndexInPage]);

            _highlightedRowIndexInPage = rowIndexInPage;
        }

        private void ClearMoveTargetHighlight()
        {
            if (_highlightedRowIndexInPage == null)
            {
                return;
            }

            int index = _highlightedRowIndexInPage.Value;

            if (index >= 0 && index < FixedTimeRowsPanel.Children.Count)
            {
                RemoveBottomHighlightLine(FixedTimeRowsPanel.Children[index]);
            }

            if (index >= 0 && index < LogRowsPanel.Children.Count)
            {
                RemoveBottomHighlightLine(LogRowsPanel.Children[index]);
            }

            _highlightedRowIndexInPage = null;
        }

        private void AddBottomHighlightLine(UIElement rowElement)
        {
            if (rowElement is not Border border)
            {
                return;
            }

            if (border.Child is not Grid rootGrid)
            {
                return;
            }

            RemoveBottomHighlightLine(rowElement);

            Border line = new()
            {
                Height = 2,
                Background = new SolidColorBrush(Color.FromRgb(255, 90, 61)),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0),
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
                Tag = MoveTargetLineTag
            };

            Panel.SetZIndex(line, 999);
            rootGrid.Children.Add(line);
        }

        private void RemoveBottomHighlightLine(UIElement rowElement)
        {
            if (rowElement is not Border border)
            {
                return;
            }

            if (border.Child is not Grid grid)
            {
                return;
            }

            List<UIElement> lines = grid.Children
                .OfType<UIElement>()
                .Where(child => child is Border b && Equals(b.Tag, MoveTargetLineTag))
                .ToList();

            foreach (UIElement line in lines)
            {
                grid.Children.Remove(line);
            }
        }

        private LogRow? GetLastVisibleAnchorRow()
        {
            if (_currentRows.Count == 0)
            {
                return null;
            }

            /*
             * 이동 모드 기준:
             * 화면에 실제로 완전히 보이는 마지막 행을 기준으로 잡는다.
             *
             * 기존 방식처럼
             * (VerticalOffset + ViewportHeight - 1) / RowHeight
             * 로 계산하면, 화면 아래에 걸쳐 있거나 실제로는 잘려 있는 행까지
             * 마지막 행으로 포함될 수 있다.
             *
             * 그래서 "완전히 보이는 마지막 행" 기준으로 계산한다.
             */
            double viewportHeight = LogScrollViewer.ViewportHeight;

            if (viewportHeight <= 0)
            {
                viewportHeight = LogScrollViewer.ActualHeight;
            }

            double viewportTop = LogScrollViewer.VerticalOffset;
            double viewportBottom = viewportTop + viewportHeight;

            int lastFullyVisibleIndex = (int)Math.Floor(viewportBottom / LogRowVisualHeight) - 1;

            lastFullyVisibleIndex = Math.Clamp(
                lastFullyVisibleIndex,
                0,
                _currentRows.Count - 1
            );

            return _currentRows[lastFullyVisibleIndex];
        }
        private MoveTargetResult? FindNextCategoryLogPosition(
            string category,
            LogRow anchorRow)
        {
            if (_repository == null)
            {
                return null;
            }

            string moveMode = _currentMode == "category"
                ? _baseModeBeforeCategory
                : _currentMode;

            string moveKeyword = _currentMode == "category"
                ? _baseKeywordBeforeCategory
                : _currentKeyword;

            string moveStartDate = _currentMode == "category"
                ? _baseStartDateBeforeCategory
                : _currentStartDate;

            string moveEndDate = _currentMode == "category"
                ? _baseEndDateBeforeCategory
                : _currentEndDate;

            DateTime? filterStart = IsValidDate(moveStartDate)
                ? DateTime.Parse(moveStartDate).Date
                : null;

            DateTime? filterEnd = IsValidDate(moveEndDate)
                ? DateTime.Parse(moveEndDate).Date.AddDays(1).AddTicks(-1)
                : null;

            bool anchorFound = false;
            int scopedIndex = -1;

            foreach (LogRow row in _repository.StreamAllLogs())
            {
                if (!IsRowInMoveScope(row, moveMode, moveKeyword, filterStart, filterEnd))
                {
                    continue;
                }

                scopedIndex++;

                if (!anchorFound)
                {
                    if (row.Id == anchorRow.Id)
                    {
                        anchorFound = true;
                    }

                    continue;
                }

                string rowTag = LogClassifier.ClassifyRowTag(row, scopedIndex);
                List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);

                if (!categories.Contains(category))
                {
                    continue;
                }

                int pageNumber = scopedIndex / _pageSize + 1;
                int rowIndexInPage = scopedIndex % _pageSize;

                return new MoveTargetResult
                {
                    RowId = row.Id,
                    PageNumber = pageNumber,
                    RowIndexInPage = rowIndexInPage
                };
            }

            return null;
        }
        private bool IsRowInMoveScope(
            LogRow row,
            string mode,
            string keyword,
            DateTime? filterStart,
            DateTime? filterEnd)
        {
            if (mode == "date_cache" || mode == "date")
            {
                return IsRowInDateRange(row, filterStart, filterEnd);
            }

            if (mode == "search")
            {
                return IsRowMatchedByKeyword(row, keyword);
            }

            return true;
        }

        private static bool IsRowMatchedByKeyword(LogRow row, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            string target =
                $"{row.DTime} {row.Type} {row.Action} {row.Section} {row.Contents} {row.Packet}";

            return target.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private void RestoreMoveBaseModeIfNeeded()
        {
            if (_currentMode != "category")
            {
                return;
            }

            _currentMode = _baseModeBeforeCategory;
            _activeRowsCacheKey = _baseCacheKeyBeforeCategory;
            _currentKeyword = _baseKeywordBeforeCategory;
            _currentStartDate = _baseStartDateBeforeCategory;
            _currentEndDate = _baseEndDateBeforeCategory;
        }
        private async Task LoadCategoryPageFromCacheAsync()
        {
            List<LogRow> combinedRows = GetSelectedCategoryRowsFromCache();

            _totalCount = combinedRows.Count;
            _totalPages = CalculateTotalPages(_totalCount);

            _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

            _currentRows = combinedRows
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();

            RebuildPaginationButtons();

            if (!_categoryCacheReady && _categoryCacheBuilding && _currentRows.Count == 0)
            {
                _highlightedRowIndexInPage = null;
                FixedTimeRowsPanel.Children.Clear();
                LogRowsPanel.Children.Clear();
                FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(Render_Log_Row.CreateEmptyRow("선택한 카테고리 내용을 계산하는 중입니다..."));
                return;
            }

            await RenderRowsBatchedAsync(_currentRows);
        }

        private List<LogRow> GetSelectedCategoryRowsFromCache()
        {
            return GetSelectedCategoryRowsFromCache(_selectedCategories);
        }

        private List<LogRow> GetSelectedCategoryRowsFromCache(IEnumerable<string> selectedCategories)
        {
            lock (_categoryCacheLock)
            {
                Dictionary<long, LogRow> uniqueRows = new();

                foreach (string category in selectedCategories)
                {
                    if (!_categoryCache.ContainsKey(category))
                    {
                        continue;
                    }

                    foreach (LogRow row in _categoryCache[category])
                    {
                        if (!uniqueRows.ContainsKey(row.Id))
                        {
                            uniqueRows[row.Id] = row;
                        }
                    }
                }

                return uniqueRows.Values
                    .OrderByDescending(row => ParseSortableDate(row.DTime))
                    .ThenByDescending(row => row.Id)
                    .ToList();
            }
        }

        private async Task LoadCurrentPageAsync(
            bool showLoading = true,
            bool resetScroll = true,
            bool instantRender = false,
            double? targetVerticalOffset = null,
            int? highlightRowIndex = null)
        {
            if (_repository == null)
            {
                return;
            }

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();

            CancellationToken token = _loadCts.Token;

            try
            {

                List<LogRow> rows;

                if (_currentMode == "all")
                {
                    int page = _currentPage;
                    int pageSize = _pageSize;

                    rows = await Task.Run(() =>
                        _repository.LoadLogsPage(page, pageSize), token
                    );
                }
                else if (_currentMode == "search")
                {
                    string keyword = _currentKeyword;
                    int page = _currentPage;
                    int pageSize = _pageSize;

                    rows = await Task.Run(() =>
                        _repository.SearchLogsPage(keyword, page, pageSize), token
                    );
                }
                else if (_currentMode == "date_cache")
                {
                    string cacheKey = _activeRowsCacheKey;
                    int page = _currentPage;
                    int pageSize = _pageSize;

                    rows = await Task.Run(() =>
                        GetRowsPageFromCache(cacheKey, page, pageSize), token
                    );
                }
                else if (_currentMode == "date")
                {
                    string startDate = _currentStartDate;
                    string endDate = _currentEndDate;
                    int page = _currentPage;
                    int pageSize = _pageSize;

                    rows = await Task.Run(() =>
                        _repository.LoadLogsByDatePage(startDate, endDate, page, pageSize), token
                    );
                }
                else if (_currentMode == "category")
                {
                    List<LogRow> combinedRows = GetSelectedCategoryRowsFromCache();

                    _totalCount = combinedRows.Count;
                    _totalPages = CalculateTotalPages(_totalCount);

                    rows = combinedRows
                        .Skip((_currentPage - 1) * _pageSize)
                        .Take(_pageSize)
                        .ToList();
                }
                else
                {
                    rows = new List<LogRow>();
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                _currentRows = rows;
                _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

                RebuildPaginationButtons();

                await RenderRowsBatchedAsync(
                    _currentRows,
                    resetScroll,
                    instantRender,
                    targetVerticalOffset,
                    highlightRowIndex
                );
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                _highlightedRowIndexInPage = null;
                FixedTimeRowsPanel.Children.Clear();
                LogRowsPanel.Children.Clear();
                FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(Render_Log_Row.CreateEmptyRow("이력 내용을 불러오는 중 오류가 발생했습니다."));
                RebuildPaginationButtons();

                ShowLoadLogsFailedMessage();
            }
        }

        private async Task RenderRowsBatchedAsync(
            List<LogRow> rows,
            bool resetScroll = true,
            bool instantRender = false,
            double? targetVerticalOffset = null,
            int? highlightRowIndex = null)
        {
            _renderCts?.Cancel();
            _renderCts = new CancellationTokenSource();

            CancellationToken token = _renderCts.Token;

            _highlightedRowIndexInPage = null;

            FixedTimeRowsPanel.Children.Clear();
            LogRowsPanel.Children.Clear();

            if (resetScroll)
            {
                FixedTimeScrollViewer.ScrollToTop();
                LogScrollViewer.ScrollToTop();
                LogScrollViewer.ScrollToHorizontalOffset(0);
                HeaderHorizontalScrollViewer.ScrollToHorizontalOffset(0);
            }

            RebuildPaginationButtons();

            if (rows.Count == 0)
            {
                FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(Render_Log_Row.CreateEmptyRow("표시할 이력 내용이 없습니다."));
                return;
            }

            /*
             * 이동 모드에서 다른 페이지로 넘어갈 때는 한 프레임 안에서 새 페이지 렌더링, 빨간줄 표시, 스크롤 이동을 끝낸다.
             * 배치 렌더링처럼 중간에 await를 끼우면 ScrollViewer의 Extent가 단계적으로 변해서 스크롤바가 흔들려 보인다.
             */
            if (instantRender)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedTimeCell(rows[i], i));
                    LogRowsPanel.Children.Add(Render_Log_Row.CreateScrollableLogRow(rows[i], i));
                }

                if (highlightRowIndex.HasValue)
                {
                    ApplyMoveTargetHighlight(highlightRowIndex.Value);
                }

                if (targetVerticalOffset.HasValue)
                {
                    LogScrollViewer.ScrollToVerticalOffset(targetVerticalOffset.Value);
                    FixedTimeScrollViewer.ScrollToVerticalOffset(targetVerticalOffset.Value);
                }

                return;
            }

            int batchSize;

            if (rows.Count >= 100000)
            {
                batchSize = 5;
            }
            else if (rows.Count >= 50000)
            {
                batchSize = 8;
            }
            else if (rows.Count >= 10000)
            {
                batchSize = 12;
            }
            else if (rows.Count >= 5000)
            {
                batchSize = 20;
            }
            else
            {
                batchSize = 50;
            }

            try
            {
                for (int start = 0; start < rows.Count; start += batchSize)
                {
                    token.ThrowIfCancellationRequested();

                    int end = Math.Min(start + batchSize, rows.Count);

                    for (int i = start; i < end; i++)
                    {
                        FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedTimeCell(rows[i], i));
                        LogRowsPanel.Children.Add(Render_Log_Row.CreateScrollableLogRow(rows[i], i));
                    }

                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                    if (rows.Count >= 100000)
                    {
                        await Task.Delay(8, token);
                    }
                    else if (rows.Count >= 50000)
                    {
                        await Task.Delay(5, token);
                    }
                    else
                    {
                        await Task.Delay(1, token);
                    }
                }

                if (highlightRowIndex.HasValue)
                {
                    ApplyMoveTargetHighlight(highlightRowIndex.Value);
                }

                if (targetVerticalOffset.HasValue)
                {
                    LogScrollViewer.ScrollToVerticalOffset(targetVerticalOffset.Value);
                    FixedTimeScrollViewer.ScrollToVerticalOffset(targetVerticalOffset.Value);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void StartBackgroundCategoryCache(string startDate = "", string endDate = "")
        {
            if (_repository == null)
            {
                return;
            }

            string cacheKey = MakePeriodCacheKey(startDate, endDate);

            if (TryApplyCategoryCache(cacheKey))
            {
                return;
            }

            _countCts?.Cancel();
            _countCts = new CancellationTokenSource();

            CancellationToken token = _countCts.Token;
            LogRepository repository = _repository;
            int jobId = Interlocked.Increment(ref _countJobVersion);

            _categoryCacheReady = false;
            _categoryCacheBuilding = true;

            ClearCurrentCategoryCache();

            FireCountText.Text = "...";
            AlarmCountText.Text = "...";
            RelayErrorCountText.Text = "...";
            AnErrorCountText.Text = "...";
            LineBreakCountText.Text = "...";
            OutputCountText.Text = "...";
            MccCountText.Text = "...";
            EtcCountText.Text = "...";

            DateTime? filterStart = IsValidDate(startDate)
                ? DateTime.Parse(startDate).Date
                : null;

            DateTime? filterEnd = IsValidDate(endDate)
                ? DateTime.Parse(endDate).Date.AddDays(1).AddTicks(-1)
                : null;

            Task.Run(() =>
            {
                Dictionary<string, int> liveCounts = CreateEmptyCountMap();
                Dictionary<string, List<LogRow>> builtCategoryCache = CreateEmptyCategoryCache();
                Stopwatch uiUpdateWatch = Stopwatch.StartNew();

                int processed = 0;
                bool wasCanceled = false;

                foreach (LogRow row in repository.StreamAllLogs())
                {
                    if (token.IsCancellationRequested || jobId != _countJobVersion)
                    {
                        wasCanceled = true;
                        break;
                    }

                    if (!IsRowInDateRange(row, filterStart, filterEnd))
                    {
                        continue;
                    }

                    string rowTag = LogClassifier.ClassifyRowTag(row, processed);
                    List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);

                    foreach (string category in categories)
                    {
                        if (!builtCategoryCache.ContainsKey(category))
                        {
                            continue;
                        }

                        builtCategoryCache[category].Add(row);

                        if (liveCounts.ContainsKey(category))
                        {
                            liveCounts[category]++;
                        }
                    }

                    processed++;

                    if (processed % 1000 == 0 && uiUpdateWatch.ElapsedMilliseconds >= 120)
                    {
                        Dictionary<string, int> snapshot = new(liveCounts);
                        uiUpdateWatch.Restart();

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (token.IsCancellationRequested || jobId != _countJobVersion)
                            {
                                return;
                            }

                            UpdateCategoryCountText(snapshot);
                        }), DispatcherPriority.Background);
                    }
                }

                if (wasCanceled || token.IsCancellationRequested || jobId != _countJobVersion)
                {
                    return;
                }

                Dictionary<string, List<LogRow>> finalSnapshot = CloneCategoryCache(builtCategoryCache);

                lock (_queryCacheLock)
                {
                    _categoryCacheByKey[cacheKey] = finalSnapshot;
                    _totalCountCacheByKey[cacheKey] = processed;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested || jobId != _countJobVersion)
                    {
                        return;
                    }

                    ApplyCategoryCacheSnapshot(finalSnapshot);

                    if (_currentMode == "category" && _selectedCategories.Count > 0)
                    {
                        _ = LoadCategoryPageFromCacheAsync();
                    }
                }), DispatcherPriority.Background);
            });
        }

        private void UpdateCategoryCountTextFromCache()
        {
            lock (_categoryCacheLock)
            {
                FireCountText.Text = _categoryCache["fire"].Count.ToString("N0");
                AlarmCountText.Text = _categoryCache["alarm"].Count.ToString("N0");
                RelayErrorCountText.Text = _categoryCache["relay_fault"].Count.ToString("N0");
                AnErrorCountText.Text = _categoryCache["an_fault"].Count.ToString("N0");
                LineBreakCountText.Text = _categoryCache["line_fault"].Count.ToString("N0");
                OutputCountText.Text = _categoryCache["output"].Count.ToString("N0");
                MccCountText.Text = _categoryCache["mcc"].Count.ToString("N0");
                EtcCountText.Text = _categoryCache["other"].Count.ToString("N0");
            }
        }

        private void UpdateCategoryCountText(Dictionary<string, int> counts)
        {
            FireCountText.Text = counts.TryGetValue("fire", out int fire)
                ? fire.ToString("N0")
                : "0";

            AlarmCountText.Text = counts.TryGetValue("alarm", out int alarm)
                ? alarm.ToString("N0")
                : "0";

            RelayErrorCountText.Text = counts.TryGetValue("relay_fault", out int relayFault)
                ? relayFault.ToString("N0")
                : "0";

            AnErrorCountText.Text = counts.TryGetValue("an_fault", out int anFault)
                ? anFault.ToString("N0")
                : "0";

            LineBreakCountText.Text = counts.TryGetValue("line_fault", out int lineFault)
                ? lineFault.ToString("N0")
                : "0";

            OutputCountText.Text = counts.TryGetValue("output", out int output)
                ? output.ToString("N0")
                : "0";

            MccCountText.Text = counts.TryGetValue("mcc", out int mcc)
                ? mcc.ToString("N0")
                : "0";

            EtcCountText.Text = counts.TryGetValue("other", out int other)
                ? other.ToString("N0")
                : "0";
        }

        private async Task<List<LogRow>?> GetOrBuildRowsCacheAsync(string startDate, string endDate)
        {
            if (_repository == null)
            {
                return new List<LogRow>();
            }

            string cacheKey = MakePeriodCacheKey(startDate, endDate);

            lock (_queryCacheLock)
            {
                if (_rowsCacheByKey.TryGetValue(cacheKey, out List<LogRow>? cachedRows))
                {
                    if (_categoryCacheByKey.TryGetValue(cacheKey, out Dictionary<string, List<LogRow>>? cachedCategoryCache))
                    {
                        ApplyCategoryCacheSnapshot(cachedCategoryCache);
                    }

                    _totalCountCacheByKey[cacheKey] = cachedRows.Count;
                    return cachedRows;
                }
            }

            _countCts?.Cancel();
            _countCts = new CancellationTokenSource();

            CancellationToken token = _countCts.Token;
            int jobId = Interlocked.Increment(ref _countJobVersion);

            DateTime filterStart = DateTime.Parse(startDate).Date;
            DateTime filterEnd = DateTime.Parse(endDate).Date.AddDays(1).AddTicks(-1);

            FireCountText.Text = "...";
            AlarmCountText.Text = "...";
            RelayErrorCountText.Text = "...";
            AnErrorCountText.Text = "...";
            LineBreakCountText.Text = "...";
            OutputCountText.Text = "...";
            MccCountText.Text = "...";
            EtcCountText.Text = "...";

            LogRepository repository = _repository;

            QueryCacheEntry? builtEntry = await Task.Run(() =>
            {
                List<LogRow> rows = new();
                Dictionary<string, List<LogRow>> categoryCache = CreateEmptyCategoryCache();
                Dictionary<string, int> liveCounts = CreateEmptyCountMap();
                Stopwatch uiUpdateWatch = Stopwatch.StartNew();

                bool wasCanceled = false;

                foreach (LogRow row in repository.StreamAllLogs())
                {
                    if (token.IsCancellationRequested || jobId != _countJobVersion)
                    {
                        wasCanceled = true;
                        break;
                    }

                    if (!IsRowInDateRange(row, filterStart, filterEnd))
                    {
                        continue;
                    }

                    int filteredIndex = rows.Count;
                    string rowTag = LogClassifier.ClassifyRowTag(row, filteredIndex);
                    List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);

                    rows.Add(row);

                    foreach (string category in categories)
                    {
                        if (!categoryCache.ContainsKey(category))
                        {
                            continue;
                        }

                        categoryCache[category].Add(row);

                        if (liveCounts.ContainsKey(category))
                        {
                            liveCounts[category]++;
                        }
                    }

                    if (rows.Count % 1000 == 0 && uiUpdateWatch.ElapsedMilliseconds >= 120)
                    {
                        Dictionary<string, int> snapshot = new(liveCounts);
                        uiUpdateWatch.Restart();

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (token.IsCancellationRequested || jobId != _countJobVersion)
                            {
                                return;
                            }

                            UpdateCategoryCountText(snapshot);
                        }), DispatcherPriority.Background);
                    }
                }

                if (wasCanceled || token.IsCancellationRequested || jobId != _countJobVersion)
                {
                    return null;
                }

                return new QueryCacheEntry
                {
                    Rows = rows,
                    CategoryCache = CloneCategoryCache(categoryCache)
                };
            });

            if (builtEntry == null || token.IsCancellationRequested || jobId != _countJobVersion)
            {
                return null;
            }

            lock (_queryCacheLock)
            {
                _rowsCacheByKey[cacheKey] = builtEntry.Rows;
                _categoryCacheByKey[cacheKey] = builtEntry.CategoryCache;
                _totalCountCacheByKey[cacheKey] = builtEntry.TotalCount;
            }

            ApplyCategoryCacheSnapshot(builtEntry.CategoryCache);

            return builtEntry.Rows;
        }

        private List<LogRow> GetRowsPageFromCache(string cacheKey, int page, int pageSize)
        {
            lock (_queryCacheLock)
            {
                if (!_rowsCacheByKey.TryGetValue(cacheKey, out List<LogRow>? rows))
                {
                    return new List<LogRow>();
                }

                int safePage = Math.Max(1, page);
                int safePageSize = Math.Max(1, pageSize);

                return rows
                    .Skip((safePage - 1) * safePageSize)
                    .Take(safePageSize)
                    .ToList();
            }
        }

        private static string MakePeriodCacheKey(string startDate, string endDate)
        {
            if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
            {
                return "all";
            }

            return $"date:{startDate.Trim()}~{endDate.Trim()}";
        }

        private bool TryApplyCategoryCache(string cacheKey)
        {
            lock (_queryCacheLock)
            {
                if (!_categoryCacheByKey.TryGetValue(cacheKey, out Dictionary<string, List<LogRow>>? cachedCategoryCache))
                {
                    return false;
                }

                ApplyCategoryCacheSnapshot(cachedCategoryCache);
                return true;
            }
        }

        private void ApplyCategoryCacheSnapshot(Dictionary<string, List<LogRow>> snapshot)
        {
            lock (_categoryCacheLock)
            {
                foreach (string key in _categoryCache.Keys.ToList())
                {
                    _categoryCache[key].Clear();
                }

                foreach (KeyValuePair<string, List<LogRow>> pair in snapshot)
                {
                    if (!_categoryCache.ContainsKey(pair.Key))
                    {
                        continue;
                    }

                    _categoryCache[pair.Key].AddRange(pair.Value);
                }
            }

            _categoryCacheReady = true;
            _categoryCacheBuilding = false;

            UpdateCategoryCountTextFromCache();
        }

        private void ClearCurrentCategoryCache()
        {
            lock (_categoryCacheLock)
            {
                foreach (string key in _categoryCache.Keys.ToList())
                {
                    _categoryCache[key].Clear();
                }
            }
        }

        private void ClearQueryCaches()
        {
            lock (_queryCacheLock)
            {
                _rowsCacheByKey.Clear();
                _categoryCacheByKey.Clear();
                _totalCountCacheByKey.Clear();
            }

            _activeRowsCacheKey = "all";
            _baseModeBeforeCategory = "all";
            _baseCacheKeyBeforeCategory = "all";
            _baseKeywordBeforeCategory = "";
            _baseStartDateBeforeCategory = "";
            _baseEndDateBeforeCategory = "";
        }

        private static Dictionary<string, int> CreateEmptyCountMap()
        {
            return new Dictionary<string, int>
            {
                ["fire"] = 0,
                ["alarm"] = 0,
                ["relay_fault"] = 0,
                ["an_fault"] = 0,
                ["line_fault"] = 0,
                ["output"] = 0,
                ["mcc"] = 0,
                ["other"] = 0
            };
        }

        private static Dictionary<string, List<LogRow>> CreateEmptyCategoryCache()
        {
            return new Dictionary<string, List<LogRow>>
            {
                ["fire"] = new List<LogRow>(),
                ["alarm"] = new List<LogRow>(),
                ["relay_fault"] = new List<LogRow>(),
                ["an_fault"] = new List<LogRow>(),
                ["line_fault"] = new List<LogRow>(),
                ["output"] = new List<LogRow>(),
                ["mcc"] = new List<LogRow>(),
                ["other"] = new List<LogRow>()
            };
        }

        private static Dictionary<string, List<LogRow>> CloneCategoryCache(Dictionary<string, List<LogRow>> source)
        {
            Dictionary<string, List<LogRow>> clone = CreateEmptyCategoryCache();

            foreach (KeyValuePair<string, List<LogRow>> pair in source)
            {
                if (!clone.ContainsKey(pair.Key))
                {
                    continue;
                }

                clone[pair.Key].AddRange(pair.Value);
            }

            return clone;
        }

        private void SaveBaseQueryState()
        {
            _baseModeBeforeCategory = _currentMode;
            _baseCacheKeyBeforeCategory = _activeRowsCacheKey;
            _baseKeywordBeforeCategory = _currentKeyword;
            _baseStartDateBeforeCategory = _currentStartDate;
            _baseEndDateBeforeCategory = _currentEndDate;
        }

        private async Task RestoreBaseQueryAfterCategoryClearAsync()
        {
            _currentMode = _baseModeBeforeCategory;
            _activeRowsCacheKey = _baseCacheKeyBeforeCategory;
            _currentKeyword = _baseKeywordBeforeCategory;
            _currentStartDate = _baseStartDateBeforeCategory;
            _currentEndDate = _baseEndDateBeforeCategory;
            _currentPage = 1;

            if (_currentMode == "date_cache")
            {
                List<LogRow> rows;

                lock (_queryCacheLock)
                {
                    rows = _rowsCacheByKey.TryGetValue(_activeRowsCacheKey, out List<LogRow>? cachedRows)
                        ? cachedRows
                        : new List<LogRow>();

                    if (_categoryCacheByKey.TryGetValue(_activeRowsCacheKey, out Dictionary<string, List<LogRow>>? categoryCache))
                    {
                        ApplyCategoryCacheSnapshot(categoryCache);
                    }
                }

                _totalCount = rows.Count;
                _totalPages = CalculateTotalPages(_totalCount);
                TotalLogCountText.Text = _totalCount.ToString("N0");

                await LoadCurrentPageAsync(showLoading: false);
                return;
            }

            if (_currentMode == "search")
            {
                await LoadCurrentPageAsync(showLoading: false);
                return;
            }

            await LoadAllFirstPageAsync();
        }


        private void ResetCategoryCountText()
        {
            TotalLogCountText.Text = "0";
            FireCountText.Text = "0";
            AlarmCountText.Text = "0";
            RelayErrorCountText.Text = "0";
            AnErrorCountText.Text = "0";
            LineBreakCountText.Text = "0";
            OutputCountText.Text = "0";
            MccCountText.Text = "0";
            EtcCountText.Text = "0";

            ClearCurrentCategoryCache();

            _categoryCacheReady = false;
            _categoryCacheBuilding = false;
        }

        private void UpdateCategoryCardActiveStates()
        {
            /*
             * 전체 로그 카드 active 기준:
             * - DB가 선택되어 있어야 한다.
             * - 화재/제경보/고장/MCC 같은 카테고리가 선택되지 않은 상태여야 한다.
             * 이력 보기 직후: 전체 로그 active
             * 7일/30일/기간 조회 직후: 전체 로그 active
             * 화재 클릭: 전체 로그 inactive, 화재 active
             * 카테고리 모두 해제: 전체 로그 active 복귀
             */
            bool totalLogActive = _repository != null && _selectedCategories.Count == 0;

            SetCardActive(TotalLogButton, totalLogActive);

            SetCardActive(FireLogButton, _selectedCategories.Contains("fire"));
            SetCardActive(AlarmLogButton, _selectedCategories.Contains("alarm"));
            SetCardActive(RelayErrorLogButton, _selectedCategories.Contains("relay_fault"));
            SetCardActive(AnErrorLogButton, _selectedCategories.Contains("an_fault"));
            SetCardActive(LineBreakLogButton, _selectedCategories.Contains("line_fault"));
            SetCardActive(OutputLogButton, _selectedCategories.Contains("output"));
            SetCardActive(MccLogButton, _selectedCategories.Contains("mcc"));
            SetCardActive(EtcLogButton, _selectedCategories.Contains("other"));
        }

        private void SetCardActive(Border card, bool isActive)
        {
            if (isActive)
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 90, 61));
                card.BorderThickness = new Thickness(2);
                card.Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
            }
            else
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                card.BorderThickness = new Thickness(1);
                card.Background = new SolidColorBrush(Color.FromArgb(191, 255, 255, 255));
            }
        }

        private static bool IsValidDate(string value)
        {
            return DateTime.TryParse(value, out _);
        }

        private static bool IsRowInDateRange(
            LogRow row,
            DateTime? filterStart,
            DateTime? filterEnd)
        {
            if (!filterStart.HasValue && !filterEnd.HasValue)
            {
                return true;
            }

            if (!DateTime.TryParse(row.DTime, out DateTime rowDateTime))
            {
                return false;
            }

            if (filterStart.HasValue && rowDateTime < filterStart.Value)
            {
                return false;
            }

            if (filterEnd.HasValue && rowDateTime > filterEnd.Value)
            {
                return false;
            }

            return true;
        }

        private static DateTime ParseSortableDate(string value)
        {
            if (DateTime.TryParse(value, out DateTime result))
            {
                return result;
            }

            return DateTime.MinValue;
        }

        private static bool IsDescendantOf(DependencyObject? source, DependencyObject target)
        {
            DependencyObject? current = source;

            while (current != null)
            {
                if (ReferenceEquals(current, target))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void CancelRunningJobs()
        {
            _renderCts?.Cancel();
            _countCts?.Cancel();
            _loadCts?.Cancel();

            if (PageSizeDropdown != null)
            {
                PageSizeDropdown.Visibility = Visibility.Collapsed;
            }

            if (DateCalendarDropdown != null)
            {
                DateCalendarDropdown.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CancelRunningJobs();
            CloseTouchKeyboardProcesses();
            base.OnClosed(e);
        }
    }
}
