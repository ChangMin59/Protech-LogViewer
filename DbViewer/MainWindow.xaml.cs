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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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

        private sealed class QueryCacheEntry
        {
            public List<LogRow> Rows { get; init; } = new();
            public Dictionary<string, List<LogRow>> CategoryCache { get; init; } = new();
            public int TotalCount => Rows.Count;
        }

        public MainWindow()
        {
            InitializeComponent();

            InitializeEvents();
            InitializeDefaultText();
        }

        private void InitializeEvents()
        {
            HistoryViewButton.MouseLeftButtonUp += async (_, _) => await OpenHistoryFileAsync();

            TotalLogButton.MouseLeftButtonUp += async (_, _) => await LoadAllFirstPageAsync();

            FireLogButton.MouseLeftButtonUp += async (_, _) => await ToggleCategoryAsync("fire");
            AlarmLogButton.MouseLeftButtonUp += async (_, _) => await ToggleCategoryAsync("alarm");
            RelayErrorLogButton.MouseLeftButtonUp += async (_, _) => await ToggleCategoryAsync("relay_fault");
            AnErrorLogButton.MouseLeftButtonUp += async (_, _) => await ToggleCategoryAsync("an_fault");
            LineBreakLogButton.MouseLeftButtonUp += async (_, _) => await ToggleCategoryAsync("line_fault");
            OutputLogButton.MouseLeftButtonUp += async (_, _) => await ToggleCategoryAsync("output");
            MccLogButton.MouseLeftButtonUp += async (_, _) => await ToggleCategoryAsync("mcc");
            EtcLogButton.MouseLeftButtonUp += async (_, _) => await ToggleCategoryAsync("other");

            SearchButton.MouseLeftButtonUp += async (_, _) => await SearchFirstPageAsync();
            ResetButton.MouseLeftButtonUp += async (_, _) => await ResetAsync();

            PeriodSearchButton.MouseLeftButtonUp += async (_, _) => await LoadDateFirstPageAsync();
            AllPeriodButton.MouseLeftButtonUp += async (_, _) => await ApplyAllPeriodAsync();
            SevenDaysButton.MouseLeftButtonUp += async (_, _) => await ApplyRecentDaysAsync(7);
            ThirtyDaysButton.MouseLeftButtonUp += async (_, _) => await ApplyRecentDaysAsync(30);

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

            LogRowsPanel.Children.Clear();
            LogRowsPanel.Children.Add(CreateEmptyRow("이력 파일을 선택하세요."));

            UpdatePageSizeDropdownStyle();
            RebuildPaginationButtons();
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

            try
            {
                CancelRunningJobs();

                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                ResetCategoryCountText();
                ClearQueryCaches();
                SetLoadingState("이력 파일을 복사하는 중입니다...");

                string copiedDbPath = await Task.Run(() =>
                    DbCopyService.CopyDbToTemp(dialog.FileName)
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

                LogRowsPanel.Children.Clear();
                LogRowsPanel.Children.Add(CreateEmptyRow("이력 파일을 정상적으로 읽을 수 없습니다."));

                MessageBox.Show(
                    $"이력 파일을 정상적으로 읽을 수 없습니다.\n\n" +
                    $"가능한 원인:\n" +
                    $"- DB 파일 손상\n" +
                    $"- Log 테이블 없음\n" +
                    $"- 로그 기록 중 복사된 파일\n" +
                    $"- Log.db-wal / Log.db-shm 파일 누락\n\n" +
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
                LogRowsPanel.Children.Clear();
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

                LogRowsPanel.Children.Clear();
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

        private async Task LoadCurrentPageAsync(bool showLoading = true)
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

                await RenderRowsBatchedAsync(_currentRows);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogRowsPanel.Children.Clear();
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

        private async Task RenderRowsBatchedAsync(List<LogRow> rows)
        {
            _renderCts?.Cancel();
            _renderCts = new CancellationTokenSource();

            CancellationToken token = _renderCts.Token;

            LogRowsPanel.Children.Clear();
            LogScrollViewer.ScrollToTop();

            RebuildPaginationButtons();

            if (rows.Count == 0)
            {
                LogRowsPanel.Children.Add(CreateEmptyRow("표시할 이력 내용이 없습니다."));
                return;
            }

            /*
             * 핵심:
             * 100,000건은 WPF UIElement를 100,000개 만드는 작업이다.
             * batch가 20이어도 UI 스레드가 계속 바쁘면 카테고리 숫자 갱신이 밀린다.
             *
             * 그래서 표시 건수가 클수록 batch를 더 줄인다.
             */
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
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    int end = Math.Min(start + batchSize, rows.Count);

                    for (int i = start; i < end; i++)
                    {
                        LogRowsPanel.Children.Add(CreateLogRow(rows[i], i));
                    }

                    /*
                     * Background만 주면 렌더링 작업이 계속 이어질 수 있다.
                     * ApplicationIdle까지 내려서 카운트 갱신, 클릭, 레이아웃 계산이 끼어들 시간을 준다.
                     */
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                    /*
                     * 100,000건에서는 1ms도 부족하다.
                     * 너무 빠르게 Add를 반복하면 카운트 갱신이 화면에 반영되기 전에
                     * 다음 행 렌더링이 계속 들어간다.
                     */
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
            LogRowsPanel.Children.Clear();
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

        private UIElement CreateLogRow(LogRow row, int index)
        {
            string rowTag = LogClassifier.ClassifyRowTag(row, index);
            List<string> badgeKeys = LogClassifier.GetBadgeKeys(row, rowTag);

            Border border = new()
            {
                MinHeight = 50,
                Background = LogStyleMapper.GetRowFill(rowTag),
                BorderBrush = LogStyleMapper.GridLineBrush(),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0)
            };

            Grid grid = new();

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            Brush textBrush = LogStyleMapper.GetRowText(rowTag);

            AddCell(grid, row.DTime, 0, true, textBrush);
            AddCell(grid, row.Type, 1, true, textBrush);
            AddCell(grid, row.Action, 2, true, textBrush);
            AddCell(grid, row.Section, 3, false, textBrush);
            AddContentCell(grid, row.Contents, 4, textBrush, badgeKeys);
            AddCell(grid, row.Packet, 5, false, textBrush, HorizontalAlignment.Right);

            border.Child = grid;
            return border;
        }

        private void AddCell(
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
                Padding = column == 5
                    ? new Thickness(8, 6, 14, 6)
                    : new Thickness(8, 6, 8, 6)
            };

            TextBlock textBlock = new()
            {
                Text = text,
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

        private void AddContentCell(
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
                Padding = new Thickness(8, 6, 8, 6)
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