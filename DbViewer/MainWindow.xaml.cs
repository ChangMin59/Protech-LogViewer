using DbViewer.Models;
using DbViewer.Services;
using Microsoft.Win32;
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
        private int? _highlightedRowIndexInPage = null;

        private const double LogRowVisualHeight = 50.0;
        private const string MoveTargetLineTag = "MoveTargetLine";

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

            InitializeEvents();
            InitializeDefaultText();
        }

        private void InitializeEvents()
        {
            HistoryViewButton.MouseLeftButtonUp += (_, _) => ShowHistoryOpenChoice();

            ManualHistoryFileButton.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
                await OpenHistoryFileAsync();
            };

            AutoHistoryFileButton.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
                await OpenAutoHistoryFileAsync();
            };

            CancelHistoryOpenButton.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
            };

            ManualHistoryFileButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
                await OpenHistoryFileAsync();
            };

            AutoHistoryFileButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
                await OpenAutoHistoryFileAsync();
            };

            CancelHistoryOpenButton.TouchDown += (_, e) =>
            {
                e.Handled = true;
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
            };

            ModeToggleButton.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await ToggleMoveModeAsync();
            };

            ModeToggleButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await ToggleMoveModeAsync();
            };

            TotalLogButton.MouseLeftButtonUp += async (_, _) => await LoadAllFirstPageAsync();

            FireLogButton.MouseLeftButtonUp += async (_, _) => await HandleCategoryButtonAsync("fire");
            AlarmLogButton.MouseLeftButtonUp += async (_, _) => await HandleCategoryButtonAsync("alarm");
            RelayErrorLogButton.MouseLeftButtonUp += async (_, _) => await HandleCategoryButtonAsync("relay_fault");
            AnErrorLogButton.MouseLeftButtonUp += async (_, _) => await HandleCategoryButtonAsync("an_fault");
            LineBreakLogButton.MouseLeftButtonUp += async (_, _) => await HandleCategoryButtonAsync("line_fault");
            OutputLogButton.MouseLeftButtonUp += async (_, _) => await HandleCategoryButtonAsync("output");
            MccLogButton.MouseLeftButtonUp += async (_, _) => await HandleCategoryButtonAsync("mcc");
            EtcLogButton.MouseLeftButtonUp += async (_, _) => await HandleCategoryButtonAsync("other");

            FireLogButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await HandleCategoryButtonAsync("fire");
            };

            AlarmLogButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await HandleCategoryButtonAsync("alarm");
            };

            RelayErrorLogButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await HandleCategoryButtonAsync("relay_fault");
            };

            AnErrorLogButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await HandleCategoryButtonAsync("an_fault");
            };

            LineBreakLogButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await HandleCategoryButtonAsync("line_fault");
            };

            OutputLogButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await HandleCategoryButtonAsync("output");
            };

            MccLogButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await HandleCategoryButtonAsync("mcc");
            };

            EtcLogButton.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await HandleCategoryButtonAsync("other");
            };

            SearchButton.MouseLeftButtonUp += async (_, _) => await SearchFirstPageAsync();
            ResetButton.MouseLeftButtonUp += async (_, _) => await ResetAsync();

            PeriodSearchButton.MouseLeftButtonUp += async (_, _) => await LoadDateFirstPageAsync();
            AllPeriodButton.MouseLeftButtonUp += async (_, _) => await ApplyAllPeriodAsync();
            SevenDaysButton.MouseLeftButtonUp += async (_, _) => await ApplyRecentDaysAsync(7);
            ThirtyDaysButton.MouseLeftButtonUp += async (_, _) => await ApplyRecentDaysAsync(30);
            LogScrollViewer.PreviewMouseLeftButtonDown += LogScrollViewer_PreviewMouseLeftButtonDown;
            LogScrollViewer.PreviewMouseMove += LogScrollViewer_PreviewMouseMove;
            LogScrollViewer.PreviewMouseLeftButtonUp += LogScrollViewer_PreviewMouseLeftButtonUp;
            LogScrollViewer.MouseLeave += LogScrollViewer_MouseLeave;

            StartDateButton.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenDateCalendar(StartDateButton, StartDateText);
            };

            EndDateButton.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenDateCalendar(EndDateButton, EndDateText);
            };

            StartDateButton.TouchDown += (_, e) =>
            {
                e.Handled = true;
                OpenDateCalendar(StartDateButton, StartDateText);
            };

            EndDateButton.TouchDown += (_, e) =>
            {
                e.Handled = true;
                OpenDateCalendar(EndDateButton, EndDateText);
            };

            DateCalendar.SelectedDatesChanged += (_, _) =>
            {
                ApplySelectedDateFromCalendar();
            };

            PageSizeButton.MouseLeftButtonUp += (_, e) =>
            {
                TogglePageSizeDropdown();
                e.Handled = true;
            };

            PageSizeButton.TouchDown += (_, e) =>
            {
                TogglePageSizeDropdown();
                e.Handled = true;
            };

            PageSize1000Button.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(1000);
            };

            PageSize5000Button.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(5000);
            };

            PageSize10000Button.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(10000);
            };

            PageSize50000Button.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(50000);
            };

            PageSize100000Button.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(100000);
            };

            PageSize1000Button.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(1000);
            };

            PageSize5000Button.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(5000);
            };

            PageSize10000Button.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(10000);
            };

            PageSize50000Button.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(50000);
            };

            PageSize100000Button.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                await ChangePageSizeAsync(100000);
            };

            PreviewMouseLeftButtonDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                CloseDropdownsWhenOutsideClicked(source);
            };

            TouchDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                CloseDropdownsWhenOutsideClicked(source);
            };

            SearchKeywordTextBox.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    await SearchFirstPageAsync();
                }
            };

            LogScrollViewer.ScrollChanged += LogScrollViewer_ScrollChanged;
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

            FixedTimeRowsPanel.Children.Add(CreateFixedEmptyCell());
            LogRowsPanel.Children.Add(CreateEmptyRow(""));

            UpdatePageSizeDropdownStyle();
            RebuildPaginationButtons();
        }

        private void ShowHistoryOpenChoice()
        {
            if (DateCalendarDropdown.Visibility == Visibility.Visible)
            {
                CloseDateCalendarDropdown();
            }

            if (PageSizeDropdown.Visibility == Visibility.Visible)
            {
                PageSizeDropdown.Visibility = Visibility.Collapsed;
            }

            HistoryOpenChoiceOverlay.Visibility = Visibility.Visible;
        }

        private async Task OpenHistoryFileAsync()
        {
            OpenFileDialog dialog = new()
            {
                Title = "이력 DB 선택",
                Filter = "SQLite DB (*.db)|*.db|모든 파일 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await OpenHistoryFileByPathAsync(dialog.FileName);
        }

        private async Task OpenAutoHistoryFileAsync()
        {
            string autoDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Log.db"
            );

            if (!File.Exists(autoDbPath))
            {
                MessageBox.Show(
                    $"Windows 폴더에서 Log.db 파일을 찾을 수 없습니다.\n\n" +
                    $"확인 경로:\n{autoDbPath}",
                    "파일 자동 탐색",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            await OpenHistoryFileByPathAsync(autoDbPath);
        }

        private async Task OpenHistoryFileByPathAsync(string dbPath)
        {
            try
            {
                CancelRunningJobs();
                ClearMoveTargetHighlight();

                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                ResetCategoryCountText();
                ClearQueryCaches();
                SetLoadingState("이력 파일을 복사하는 중입니다...");

                string copiedDbPath = await Task.Run(() =>
                    DbCopyService.CopyDbToTemp(dbPath)
                );

                _repository = new LogRepository(copiedDbPath);

                SetLoadingState("이력 파일을 확인하는 중입니다...");

                (int totalCount, string startDate, string endDate) result = await Task.Run(() =>
                {
                    _repository.Validate();

                    int total = _repository.CountAllLogs();
                    (string start, string end) range = _repository.GetLogDateRange();

                    return (total, range.start, range.end);
                });

                _totalCount = result.totalCount;
                _totalPages = CalculateTotalPages(_totalCount);

                StartDateText.Text = result.startDate;
                EndDateText.Text = result.endDate;

                ApplyDbDateRange(result.startDate, result.endDate);
                UpdateDateArrowVisibility();

                _currentMode = "all";
                _currentKeyword = "";
                _currentStartDate = "";
                _currentEndDate = "";
                _currentPage = 1;

                TotalLogCountText.Text = _totalCount.ToString("N0");

                UpdateCategoryCardActiveStates();

                StartBackgroundCategoryCache();

                await LoadCurrentPageAsync(showLoading: false);
            }
            catch (Exception ex)
            {
                _repository = null;
                _currentRows.Clear();

                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                ResetCategoryCountText();
                ClearQueryCaches();

                StartDateText.Text = "";
                EndDateText.Text = "";
                _dbStartDate = null;
                _dbEndDate = null;
                UpdateDateArrowVisibility();
                DateCalendarDropdown.Visibility = Visibility.Collapsed;

                FixedTimeRowsPanel.Children.Clear();
                LogRowsPanel.Children.Clear();

                FixedTimeRowsPanel.Children.Add(CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(CreateEmptyRow("이력 파일을 정상적으로 읽을 수 없습니다."));

                MessageBox.Show(
                    $"이력 파일을 정상적으로 읽을 수 없습니다.\n\n" +
                    $"선택 경로:\n{dbPath}\n\n" +
                    $"가능한 원인:\n" +
                    $"- DB 파일 손상\n" +
                    $"- Log 테이블 없음\n" +
                    $"- 로그 기록 중 복사된 파일\n" +
                    $"- Log.db-wal / Log.db-shm 파일 누락\n" +
                    $"- Windows 폴더 접근 권한 문제\n\n" +
                    $"오류 내용:\n{ex.Message}",
                    "이력 보기 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async Task LoadAllFirstPageAsync()
        {
            if (_repository == null)
            {
                return;
            }

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
            StartBackgroundCategoryCache();

            await LoadCurrentPageAsync(showLoading: false);
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
                    MessageBox.Show(
                        $"현재 화면 아래쪽의 다음 {categoryName} 로그가 없습니다.",
                        "이동 모드",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

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
                Tag = "MoveTargetLine"
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
                FixedTimeRowsPanel.Children.Add(CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(CreateEmptyRow("선택한 카테고리 내용을 계산하는 중입니다..."));
                return;
            }

            await RenderRowsBatchedAsync(_currentRows);
        }

        private List<LogRow> GetSelectedCategoryRowsFromCache()
        {
            lock (_categoryCacheLock)
            {
                Dictionary<long, LogRow> uniqueRows = new();

                foreach (string category in _selectedCategories)
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

        private async Task SearchFirstPageAsync()
        {
            if (_repository == null)
            {
                return;
            }

            ClearMoveTargetHighlight();

            string keyword = SearchKeywordTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            _currentMode = "search";
            _currentKeyword = keyword;
            _currentStartDate = "";
            _currentEndDate = "";
            _currentPage = 1;

            _totalCount = await Task.Run(() => _repository.CountSearchLogs(_currentKeyword));
            _totalPages = CalculateTotalPages(_totalCount);

            await LoadCurrentPageAsync();
        }

        private async Task LoadDateFirstPageAsync()
        {
            if (_repository == null)
            {
                return;
            }

            ClearMoveTargetHighlight();

            string startDate = StartDateText.Text.Trim();
            string endDate = EndDateText.Text.Trim();

            if (!IsValidDate(startDate) || !IsValidDate(endDate))
            {
                MessageBox.Show(
                    "시작일과 종료일을 확인하세요.",
                    "기간 조회",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            DateTime start = DateTime.Parse(startDate);
            DateTime end = DateTime.Parse(endDate);

            if (start > end)
            {
                MessageBox.Show(
                    "시작일은 종료일보다 늦을 수 없습니다.",
                    "기간 조회",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            _currentMode = "date_cache";
            _currentStartDate = startDate;
            _currentEndDate = endDate;
            _currentKeyword = "";
            _currentPage = 1;
            _activeRowsCacheKey = MakePeriodCacheKey(startDate, endDate);

            SetLoadingState("선택한 기간의 이력 내용을 준비하는 중입니다...");

            List<LogRow>? cachedRows = await GetOrBuildRowsCacheAsync(startDate, endDate);

            if (cachedRows == null)
            {
                return;
            }

            _totalCount = cachedRows.Count;
            _totalPages = CalculateTotalPages(_totalCount);

            TotalLogCountText.Text = _totalCount.ToString("N0");

            SaveBaseQueryState();

            await LoadCurrentPageAsync(showLoading: false);
        }

        private async Task ApplyRecentDaysAsync(int days)
        {
            if (_repository == null)
            {
                return;
            }

            (string startDate, string endDate) range = await Task.Run(() =>
                _repository.GetLogDateRange()
            );

            if (!IsValidDate(range.endDate))
            {
                return;
            }

            DateTime end = DateTime.Parse(range.endDate);
            DateTime start = end.AddDays(-(days - 1));

            StartDateText.Text = start.ToString("yyyy-MM-dd");
            EndDateText.Text = end.ToString("yyyy-MM-dd");

            UpdateDateArrowVisibility();

            await LoadDateFirstPageAsync();
        }

        private async Task ApplyAllPeriodAsync()
        {
            if (_repository == null)
            {
                return;
            }

            /*
             * 전체 버튼은 현재 화면에 남아 있는 7일/30일 값을 쓰면 안 된다.
             * DB 안의 실제 최초 로그 날짜와 마지막 로그 날짜로 다시 돌린다.
             */
            (string startDate, string endDate) range = await Task.Run(() =>
                _repository.GetLogDateRange()
            );

            StartDateText.Text = range.startDate;
            EndDateText.Text = range.endDate;

            ApplyDbDateRange(range.startDate, range.endDate);
            UpdateDateArrowVisibility();

            await LoadAllFirstPageAsync();
        }

        private async Task ResetAsync()
        {
            SearchKeywordTextBox.Text = "";

            ClearMoveTargetHighlight();

            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            if (_repository == null)
            {
                StartDateText.Text = "";
                EndDateText.Text = "";
                _dbStartDate = null;
                _dbEndDate = null;
                UpdateDateArrowVisibility();
                DateCalendarDropdown.Visibility = Visibility.Collapsed;

                FixedTimeRowsPanel.Children.Clear();
                LogRowsPanel.Children.Clear();

                FixedTimeRowsPanel.Children.Add(CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(CreateEmptyRow("이력 파일을 선택하세요."));
                return;
            }

            (string startDate, string endDate) range = await Task.Run(() =>
                _repository.GetLogDateRange()
            );

            StartDateText.Text = range.startDate;
            EndDateText.Text = range.endDate;

            ApplyDbDateRange(range.startDate, range.endDate);
            UpdateDateArrowVisibility();

            await LoadAllFirstPageAsync();
        }

        private void OpenDateCalendar(Border targetButton, TextBlock targetText)
        {
            if (_repository == null)
            {
                return;
            }

            if (!_dbStartDate.HasValue || !_dbEndDate.HasValue)
            {
                return;
            }

            bool isOpen = DateCalendarDropdown.Visibility == Visibility.Visible;

            if (isOpen && ReferenceEquals(_dateTargetButton, targetButton))
            {
                CloseDateCalendarDropdown();
                return;
            }

            if (PageSizeDropdown.Visibility == Visibility.Visible)
            {
                PageSizeDropdown.Visibility = Visibility.Collapsed;
            }

            _dateTargetButton = targetButton;
            _dateTargetText = targetText;

            DateCalendar.DisplayDateStart = _dbStartDate;
            DateCalendar.DisplayDateEnd = _dbEndDate;

            _suppressDateCalendarChange = true;

            if (IsValidDate(targetText.Text))
            {
                DateTime selectedDate = DateTime.Parse(targetText.Text);

                DateCalendar.SelectedDate = selectedDate;
                DateCalendar.DisplayDate = selectedDate;
            }
            else
            {
                DateCalendar.SelectedDate = _dbEndDate.Value;
                DateCalendar.DisplayDate = _dbEndDate.Value;
            }

            _suppressDateCalendarChange = false;

            Point point = targetButton.TranslatePoint(
                new Point(0, targetButton.ActualHeight + 6),
                RootGrid
            );

            double x = point.X;
            double y = point.Y;

            if (x + DateCalendarDropdown.Width > RootGrid.ActualWidth)
            {
                x = RootGrid.ActualWidth - DateCalendarDropdown.Width - 12;
            }

            if (y + DateCalendarDropdown.Height > RootGrid.ActualHeight)
            {
                y = point.Y - DateCalendarDropdown.Height - targetButton.ActualHeight - 12;
            }

            DateCalendarDropdownTransform.X = Math.Max(12, x);
            DateCalendarDropdownTransform.Y = Math.Max(12, y);

            DateCalendarDropdown.Visibility = Visibility.Visible;
        }

        private async void ApplySelectedDateFromCalendar()
        {
            if (_suppressDateCalendarChange)
            {
                return;
            }

            if (_dateTargetText == null)
            {
                return;
            }

            if (!DateCalendar.SelectedDate.HasValue)
            {
                return;
            }

            string selectedDate = DateCalendar.SelectedDate.Value.ToString("yyyy-MM-dd");

            _dateTargetText.Text = selectedDate;

            /*
             * 날짜 선택 후 캘린더가 닫히면서 아래 카테고리 카드가 같이 클릭되는 문제를 막는다.
             */
            BlockCategoryClickBriefly();

            DateCalendarDropdown.Visibility = Visibility.Collapsed;

            if (IsValidDate(StartDateText.Text) && IsValidDate(EndDateText.Text))
            {
                DateTime start = DateTime.Parse(StartDateText.Text);
                DateTime end = DateTime.Parse(EndDateText.Text);

                if (start > end)
                {
                    if (ReferenceEquals(_dateTargetText, StartDateText))
                    {
                        EndDateText.Text = selectedDate;
                    }
                    else
                    {
                        StartDateText.Text = selectedDate;
                    }
                }
            }

            UpdateDateArrowVisibility();

            _dateTargetButton = null;
            _dateTargetText = null;

            /*
             * 핵심:
             * 7일/30일 버튼처럼 날짜가 바뀐 직후 바로 기간 조회를 실행한다.
             * 그래서 전체 로그 / 화재 / 제경보 / 중계기 고장 숫자가 선택 기간 기준으로 즉시 바뀐다.
             */
            if (_repository != null &&
                IsValidDate(StartDateText.Text) &&
                IsValidDate(EndDateText.Text))
            {
                await LoadDateFirstPageAsync();
            }
        }

        private void ApplyDbDateRange(string startDate, string endDate)
        {
            _dbStartDate = IsValidDate(startDate)
                ? DateTime.Parse(startDate)
                : null;

            _dbEndDate = IsValidDate(endDate)
                ? DateTime.Parse(endDate)
                : null;

            DateCalendar.DisplayDateStart = _dbStartDate;
            DateCalendar.DisplayDateEnd = _dbEndDate;

            if (_dbEndDate.HasValue)
            {
                DateCalendar.DisplayDate = _dbEndDate.Value;
            }
        }

        private void UpdateDateArrowVisibility()
        {
            bool hasStartDate = IsValidDate(StartDateText.Text);
            bool hasEndDate = IsValidDate(EndDateText.Text);

            StartDateArrowText.Visibility = hasStartDate
                ? Visibility.Visible
                : Visibility.Collapsed;

            EndDateArrowText.Visibility = hasEndDate
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void CloseDateCalendarDropdown()
        {
            DateCalendarDropdown.Visibility = Visibility.Collapsed;
            _dateTargetButton = null;
            _dateTargetText = null;
        }

        private void BlockCategoryClickBriefly()
        {
            _ignoreCategoryClickUntil = DateTime.Now.AddMilliseconds(350);
        }

        private bool ShouldIgnoreCategoryClick()
        {
            return DateTime.Now <= _ignoreCategoryClickUntil;
        }

        private void CloseDropdownsWhenOutsideClicked(DependencyObject? source)
        {
            if (PageSizeDropdown.Visibility == Visibility.Visible)
            {
                bool clickedPageSizeButton = IsDescendantOf(source, PageSizeButton);
                bool clickedPageSizeDropdown = IsDescendantOf(source, PageSizeDropdown);

                if (!clickedPageSizeButton && !clickedPageSizeDropdown)
                {
                    PageSizeDropdown.Visibility = Visibility.Collapsed;
                }
            }

            if (DateCalendarDropdown.Visibility == Visibility.Visible)
            {
                bool clickedStartDate = IsDescendantOf(source, StartDateButton);
                bool clickedEndDate = IsDescendantOf(source, EndDateButton);
                bool clickedDateCalendar = IsDescendantOf(source, DateCalendarDropdown);

                if (!clickedStartDate && !clickedEndDate && !clickedDateCalendar)
                {
                    CloseDateCalendarDropdown();
                }
            }
        }

        private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            /*
             * 오른쪽 로그 영역을 세로로 내리면
             * 왼쪽 고정 발생시간도 같은 위치로 내려간다.
             */
            FixedTimeScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);

            /*
             * 오른쪽 로그 영역을 터치로 좌우 이동하면
             * 오른쪽 헤더도 같은 위치로 이동한다.
             */
            HeaderHorizontalScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        private void LogScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;

            /*
             * 스크롤바 또는 스크롤바 손잡이(Thumb)를 클릭한 경우에는
               세로 스크롤바를 직접 클릭/드래그해서 움직일 수 있다.
             */
            if (IsInsideScrollBar(source))
            {
                return;
            }

            _isLogContentDragging = true;
            _dragStartPoint = e.GetPosition(LogScrollViewer);
            _dragStartHorizontalOffset = LogScrollViewer.HorizontalOffset;
            _dragStartVerticalOffset = LogScrollViewer.VerticalOffset;

            LogScrollViewer.CaptureMouse();
            e.Handled = true;
        }

        private void LogScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isLogContentDragging)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndLogContentDrag();
                return;
            }

            Point currentPoint = e.GetPosition(LogScrollViewer);

            double dragSpeed = 1.5;

            double deltaX = (currentPoint.X - _dragStartPoint.X) * dragSpeed;
            double deltaY = (currentPoint.Y - _dragStartPoint.Y) * dragSpeed;

            /*
             * 마우스를 왼쪽으로 끌면 오른쪽 패킷 쪽이 보인다.
             * 마우스를 위로 끌면 아래 로그가 보인다.
             */
            LogScrollViewer.ScrollToHorizontalOffset(_dragStartHorizontalOffset - deltaX);
            LogScrollViewer.ScrollToVerticalOffset(_dragStartVerticalOffset - deltaY);

            e.Handled = true;
        }

        private void LogScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isLogContentDragging)
            {
                return;
            }

            EndLogContentDrag();
            e.Handled = true;
        }

        private void LogScrollViewer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isLogContentDragging && e.LeftButton != MouseButtonState.Pressed)
            {
                EndLogContentDrag();
            }
        }

        private void EndLogContentDrag()
        {
            _isLogContentDragging = false;

            if (LogScrollViewer.IsMouseCaptured)
            {
                LogScrollViewer.ReleaseMouseCapture();
            }
        }

        private bool IsInsideScrollBar(DependencyObject? source)
        {
            DependencyObject? current = source;

            while (current != null)
            {
                if (current is ScrollBar || current is Thumb)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void LogScrollViewer_ManipulationBoundaryFeedback(
            object sender,
            ManipulationBoundaryFeedbackEventArgs e)
        {
            /*
             * 터치로 끝까지 밀었을 때 WPF 기본 튕김 효과를 막는다.
             * 키오스크/터치 모니터에서 화면 흔들림을 줄인다.
             */
            e.Handled = true;
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
                if (showLoading)
                {
                    SetLoadingState("이력 내용을 불러오는 중입니다...");
                }

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
            catch (Exception ex)
            {
                _highlightedRowIndexInPage = null;
                FixedTimeRowsPanel.Children.Clear();
                LogRowsPanel.Children.Clear();
                FixedTimeRowsPanel.Children.Add(CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(CreateEmptyRow("이력 내용을 불러오는 중 오류가 발생했습니다."));
                RebuildPaginationButtons();

                MessageBox.Show(
                    $"이력 내용을 불러오는 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "조회 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async Task MovePrevPageAsync()
        {
            if (_repository == null)
            {
                return;
            }

            if (_currentPage <= 1)
            {
                return;
            }

            ClearMoveTargetHighlight();

            _currentPage--;

            if (_currentMode == "category")
            {
                await LoadCategoryPageFromCacheAsync();
                return;
            }

            await LoadCurrentPageAsync(showLoading: false);
        }

        private async Task MoveNextPageAsync()
        {
            if (_repository == null)
            {
                return;
            }

            if (_currentPage >= _totalPages)
            {
                return;
            }

            ClearMoveTargetHighlight();

            _currentPage++;

            if (_currentMode == "category")
            {
                await LoadCategoryPageFromCacheAsync();
                return;
            }

            await LoadCurrentPageAsync(showLoading: false);
        }

        private void TogglePageSizeDropdown()
        {
            if (DateCalendarDropdown.Visibility == Visibility.Visible)
            {
                CloseDateCalendarDropdown();
            }

            PageSizeDropdown.Visibility =
                PageSizeDropdown.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            UpdatePageSizeDropdownStyle();
        }

        private async Task ChangePageSizeAsync(int size)
        {
            PageSizeDropdown.Visibility = Visibility.Collapsed;

            ClearMoveTargetHighlight();

            _pageSize = size;
            PageSizeText.Text = $"{size:N0}건";

            _currentPage = 1;
            _totalPages = CalculateTotalPages(_totalCount);

            UpdatePageSizeDropdownStyle();

            if (_currentMode == "category")
            {
                await LoadCategoryPageFromCacheAsync();
                return;
            }

            await LoadCurrentPageAsync(showLoading: false);
        }

        private void UpdatePageSizeDropdownStyle()
        {
            SetPageSizeItemStyle(PageSize1000Button, 1000);
            SetPageSizeItemStyle(PageSize5000Button, 5000);
            SetPageSizeItemStyle(PageSize10000Button, 10000);
            SetPageSizeItemStyle(PageSize50000Button, 50000);
            SetPageSizeItemStyle(PageSize100000Button, 100000);
        }

        private void SetPageSizeItemStyle(Border button, int size)
        {
            bool isSelected = _pageSize == size;

            button.Background = isSelected
                ? new SolidColorBrush(Color.FromRgb(255, 90, 67))
                : new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));

            if (button.Child is TextBlock textBlock)
            {
                textBlock.Foreground = isSelected
                    ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                    : new SolidColorBrush(Color.FromRgb(35, 57, 93));
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
                FixedTimeRowsPanel.Children.Add(CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(CreateEmptyRow("표시할 이력 내용이 없습니다."));
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
                    FixedTimeRowsPanel.Children.Add(CreateFixedTimeCell(rows[i], i));
                    LogRowsPanel.Children.Add(CreateScrollableLogRow(rows[i], i));
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
                        FixedTimeRowsPanel.Children.Add(CreateFixedTimeCell(rows[i], i));
                        LogRowsPanel.Children.Add(CreateScrollableLogRow(rows[i], i));
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

        private void SetLoadingState(string message)
        {
            _highlightedRowIndexInPage = null;

            FixedTimeRowsPanel.Children.Clear();
            LogRowsPanel.Children.Clear();

            FixedTimeRowsPanel.Children.Add(CreateFixedEmptyCell());
            LogRowsPanel.Children.Add(CreateEmptyRow(message));

            RebuildPaginationButtons();
        }

        private UIElement CreateEmptyRow(string message)
        {
            Border border = new()
            {
                Height = 58,
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(221, 232, 242)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            TextBlock text = new()
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 94, 120)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = text;
            return border;
        }

        private UIElement CreateFixedEmptyCell()
        {
            Border border = new()
            {
                Height = 58,
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 1)
            };

            return border;
        }

        private UIElement CreateFixedTimeCell(LogRow row, int index)
        {
            string rowTag = LogClassifier.ClassifyRowTag(row, index);

            Border border = new()
            {
                MinHeight = 50,
                Background = LogStyleMapper.GetRowFill(rowTag),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };

            Grid rootGrid = new()
            {
                SnapsToDevicePixels = true
            };

            TextBlock textBlock = new()
            {
                Text = row.DTime,
                ToolTip = row.DTime,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = LogStyleMapper.GetRowText(rowTag),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 6, 6, 6)
            };

            rootGrid.Children.Add(textBlock);

            border.Child = rootGrid;

            return border;
        }

        private UIElement CreateScrollableLogRow(LogRow row, int index)
        {
            string rowTag = LogClassifier.ClassifyRowTag(row, index);
            List<string> badgeKeys = LogClassifier.GetBadgeKeys(row, rowTag);

            Border border = new()
            {
                MinHeight = 50,
                Width = 930,
                Background = LogStyleMapper.GetRowFill(rowTag),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };

            /*
             * 빨간줄 Overlay용 Grid
             * 이 Grid는 컬럼이 없어야 한다.
             * 그래야 빨간줄이 행 전체 폭으로 쭉 깔린다.
             */
            Grid rootGrid = new()
            {
                SnapsToDevicePixels = true
            };

            /*
             * 실제 내용용 Grid
             * 여기만 컬럼을 가진다.
             */
            Grid contentGrid = new()
            {
                Width = 930,
                SnapsToDevicePixels = true
            };

            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });   // 구분
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });   // 상태
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });  // 위치
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });  // 내용
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });  // 패킷

            Brush textBrush = LogStyleMapper.GetRowText(rowTag);

            AddFixedWidthCell(contentGrid, row.Type, 0, true, textBrush);
            AddFixedWidthCell(contentGrid, row.Action, 1, true, textBrush);
            AddFixedWidthCell(contentGrid, row.Section, 2, false, textBrush);
            AddScrollableContentCell(contentGrid, row.Contents, 3, textBrush, badgeKeys);
            AddFixedWidthCell(contentGrid, row.Packet, 4, false, textBrush, HorizontalAlignment.Left);

            rootGrid.Children.Add(contentGrid);

            border.Child = rootGrid;

            return border;
        }

        private void AddFixedWidthCell(
            Grid grid,
            string text,
            int column,
            bool center,
            Brush textBrush,
            HorizontalAlignment? forceAlignment = null)
        {
            Border cellBorder = new()
            {
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(5, 6, 5, 6)
            };

            TextBlock textBlock = new()
            {
                Text = text,
                ToolTip = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            HorizontalAlignment finalAlignment = forceAlignment ??
                                                 (center ? HorizontalAlignment.Center : HorizontalAlignment.Left);

            textBlock.HorizontalAlignment = finalAlignment;

            textBlock.TextAlignment = finalAlignment switch
            {
                HorizontalAlignment.Right => TextAlignment.Right,
                HorizontalAlignment.Center => TextAlignment.Center,
                _ => TextAlignment.Left
            };

            cellBorder.Child = textBlock;

            Grid.SetColumn(cellBorder, column);
            grid.Children.Add(cellBorder);
        }

        private void AddScrollableContentCell(
            Grid grid,
            string text,
            int column,
            Brush textBrush,
            List<string> badgeKeys)
        {
            Border cellBorder = new()
            {
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(5, 6, 5, 6)
            };

            StackPanel panel = new()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (string badgeKey in badgeKeys)
            {
                BadgeStyle? badge = LogStyleMapper.GetBadge(badgeKey);

                if (badge == null)
                {
                    continue;
                }

                Border badgeBorder = new()
                {
                    MinWidth = 38,
                    Height = 22,
                    CornerRadius = new CornerRadius(6),
                    Background = badge.Fill,
                    BorderBrush = badge.Border,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock badgeText = new()
                {
                    Text = badge.Label,
                    Foreground = badge.Text,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                badgeBorder.Child = badgeText;
                panel.Children.Add(badgeBorder);
            }

            TextBlock contentText = new()
            {
                Text = text,
                ToolTip = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            panel.Children.Add(contentText);

            cellBorder.Child = panel;

            Grid.SetColumn(cellBorder, column);
            grid.Children.Add(cellBorder);
        }

        private void RebuildPaginationButtons()
        {
            PaginationPanel.Children.Clear();

            TotalPageInfoText.Text = $"총 {_totalPages:N0}페이지 · {_totalCount:N0}건";

            PaginationPanel.Children.Add(CreatePaginationButton(
                text: "‹ 이전",
                isActive: false,
                isEnabled: _currentPage > 1,
                onClick: async () => await MovePrevPageAsync()
            ));

            foreach (int pageNumber in GetVisiblePageNumbers())
            {
                int capturedPage = pageNumber;

                PaginationPanel.Children.Add(CreatePaginationButton(
                    text: pageNumber.ToString(),
                    isActive: pageNumber == _currentPage,
                    isEnabled: true,
                    onClick: async () =>
                    {
                        if (_currentPage == capturedPage)
                        {
                            return;
                        }

                        _currentPage = capturedPage;

                        if (_currentMode == "category")
                        {
                            await LoadCategoryPageFromCacheAsync();
                            return;
                        }

                        await LoadCurrentPageAsync(showLoading: false);
                    }
                ));
            }

            PaginationPanel.Children.Add(CreatePaginationButton(
                text: "다음 ›",
                isActive: false,
                isEnabled: _currentPage < _totalPages,
                onClick: async () => await MoveNextPageAsync()
            ));
        }

        private List<int> GetVisiblePageNumbers()
        {
            List<int> pages = new();

            const int groupSize = 5;

            int currentGroupIndex = (_currentPage - 1) / groupSize;

            int startPage = currentGroupIndex * groupSize + 1;
            int endPage = Math.Min(startPage + groupSize - 1, _totalPages);

            for (int page = startPage; page <= endPage; page++)
            {
                pages.Add(page);
            }

            return pages;
        }

        private Border CreatePaginationButton(
            string text,
            bool isActive,
            bool isEnabled,
            Func<Task> onClick)
        {
            Border button = new()
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 9, 16, 9),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = isEnabled ? Cursors.Hand : Cursors.Arrow,
                Opacity = isEnabled ? 1.0 : 0.45,
                Background = isActive
                    ? new SolidColorBrush(Color.FromRgb(255, 90, 67))
                    : new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)),
                BorderBrush = isActive
                    ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            TextBlock label = new()
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = isActive
                    ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                    : new SolidColorBrush(Color.FromRgb(35, 57, 93)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            button.Child = label;

            if (isEnabled)
            {
                button.MouseLeftButtonUp += async (_, _) => await onClick();
                button.TouchDown += async (_, e) =>
                {
                    e.Handled = true;
                    await onClick();
                };
            }

            return button;
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

        private int CalculateTotalPages(int totalCount)
        {
            return Math.Max(1, (int)Math.Ceiling(totalCount / (double)_pageSize));
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
            base.OnClosed(e);
        }
    }
}