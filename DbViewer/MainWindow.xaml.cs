using DbViewer.Models;
using DbViewer.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
            AllPeriodButton.MouseLeftButtonUp += async (_, _) => await LoadAllFirstPageAsync();
            SevenDaysButton.MouseLeftButtonUp += async (_, _) => await ApplyRecentDaysAsync(7);
            ThirtyDaysButton.MouseLeftButtonUp += async (_, _) => await ApplyRecentDaysAsync(30);

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
                if (PageSizeDropdown.Visibility != Visibility.Visible)
                {
                    return;
                }

                DependencyObject? source = e.OriginalSource as DependencyObject;

                if (IsDescendantOf(source, PageSizeButton) ||
                    IsDescendantOf(source, PageSizeDropdown))
                {
                    return;
                }

                PageSizeDropdown.Visibility = Visibility.Collapsed;
            };

            TouchDown += (_, e) =>
            {
                if (PageSizeDropdown.Visibility != Visibility.Visible)
                {
                    return;
                }

                DependencyObject? source = e.OriginalSource as DependencyObject;

                if (IsDescendantOf(source, PageSizeButton) ||
                    IsDescendantOf(source, PageSizeDropdown))
                {
                    return;
                }

                PageSizeDropdown.Visibility = Visibility.Collapsed;
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

                _currentMode = "all";
                _currentKeyword = "";
                _currentStartDate = "";
                _currentEndDate = "";
                _currentPage = 1;

                TotalLogCountText.Text = _totalCount.ToString("N0");

                await LoadCurrentPageAsync(showLoading: false);

                StartBackgroundCategoryCache();
            }
            catch (Exception ex)
            {
                _repository = null;
                _currentRows.Clear();

                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                ResetCategoryCountText();

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

            _totalCount = await Task.Run(() => _repository.CountAllLogs());
            _totalPages = CalculateTotalPages(_totalCount);

            TotalLogCountText.Text = _totalCount.ToString("N0");

            await LoadCurrentPageAsync(showLoading: false);
        }

        private async Task ToggleCategoryAsync(string category)
        {
            if (_repository == null)
            {
                return;
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
                await LoadAllFirstPageAsync();
                return;
            }

            _currentMode = "category";
            _currentKeyword = "";
            _currentStartDate = "";
            _currentEndDate = "";
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

            _currentMode = "date";
            _currentStartDate = startDate;
            _currentEndDate = endDate;
            _currentKeyword = "";
            _currentPage = 1;

            _totalCount = await Task.Run(() =>
                _repository.CountDateLogs(_currentStartDate, _currentEndDate)
            );

            _totalPages = CalculateTotalPages(_totalCount);

            await LoadCurrentPageAsync();
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

            await LoadDateFirstPageAsync();
        }

        private async Task ResetAsync()
        {
            SearchKeywordTextBox.Text = "";

            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            if (_repository == null)
            {
                LogRowsPanel.Children.Clear();
                LogRowsPanel.Children.Add(CreateEmptyRow("이력 파일을 선택하세요."));
                return;
            }

            (string startDate, string endDate) range = await Task.Run(() =>
                _repository.GetLogDateRange()
            );

            StartDateText.Text = range.startDate;
            EndDateText.Text = range.endDate;

            await LoadAllFirstPageAsync();
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

            const int batchSize = 50;

            try
            {
                for (int start = 0; start < rows.Count; start += batchSize)
                {
                    token.ThrowIfCancellationRequested();

                    int end = Math.Min(start + batchSize, rows.Count);

                    for (int i = start; i < end; i++)
                    {
                        LogRowsPanel.Children.Add(CreateLogRow(rows[i], i));
                    }

                    await Task.Delay(1, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void StartBackgroundCategoryCache()
        {
            if (_repository == null)
            {
                return;
            }

            _countCts?.Cancel();
            _countCts = new CancellationTokenSource();

            CancellationToken token = _countCts.Token;
            LogRepository repository = _repository;

            _categoryCacheReady = false;
            _categoryCacheBuilding = true;

            lock (_categoryCacheLock)
            {
                foreach (string key in _categoryCache.Keys.ToList())
                {
                    _categoryCache[key].Clear();
                }
            }

            FireCountText.Text = "...";
            AlarmCountText.Text = "...";
            RelayErrorCountText.Text = "...";
            AnErrorCountText.Text = "...";
            LineBreakCountText.Text = "...";
            OutputCountText.Text = "...";
            MccCountText.Text = "...";
            EtcCountText.Text = "...";

            Task.Run(() =>
            {
                int processed = 0;

                foreach (LogRow row in repository.StreamAllLogs())
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    string rowTag = LogClassifier.ClassifyRowTag(row, processed);
                    List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);

                    lock (_categoryCacheLock)
                    {
                        foreach (string category in categories)
                        {
                            if (!_categoryCache.ContainsKey(category))
                            {
                                continue;
                            }

                            _categoryCache[category].Add(row);
                        }
                    }

                    processed++;

                    if (processed % 5000 == 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateCategoryCountTextFromCache();

                            if (_currentMode == "category" && _selectedCategories.Count > 0)
                            {
                                _ = LoadCategoryPageFromCacheAsync();
                            }
                        });
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    _categoryCacheReady = true;
                    _categoryCacheBuilding = false;

                    UpdateCategoryCountTextFromCache();

                    if (_currentMode == "category" && _selectedCategories.Count > 0)
                    {
                        _ = LoadCategoryPageFromCacheAsync();
                    }
                });
            }, token);
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

            lock (_categoryCacheLock)
            {
                foreach (string key in _categoryCache.Keys.ToList())
                {
                    _categoryCache[key].Clear();
                }
            }

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
        }

        protected override void OnClosed(EventArgs e)
        {
            CancelRunningJobs();
            base.OnClosed(e);
        }
    }
}