using DbViewer.Services.Render;
using DbViewer.Services.View;
using System.Threading.Tasks;
using System.Windows;

namespace DbViewer
{
    public partial class MainWindow
    {
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
            if (_isOpeningHistory)
            {
                return;
            }

            _isOpeningHistory = true;

            string? historyFilePath = Select_File.SelectHistoryFilePath(this);
            BlockPointerInputAfterModal();

            try
            {
                if (historyFilePath == null)
                {
                    return;
                }

                await OpenHistoryFileByPathAsync(historyFilePath);
            }
            finally
            {
                _isOpeningHistory = false;
            }
        }

        private async Task OpenAutoHistoryFileAsync()
        {
            if (_isOpeningHistory)
            {
                return;
            }

            _isOpeningHistory = true;

            if (!Auto_File_Find.TryFindHistoryFile(out string autoDbPath))
            {
                ShowHistoryFileNotFoundMessage("파일 자동 탐색");
                BlockPointerInputAfterModal();
                _isOpeningHistory = false;

                return;
            }

            try
            {
                await OpenHistoryFileByPathAsync(autoDbPath, "파일 자동 탐색");
            }
            finally
            {
                _isOpeningHistory = false;
            }
        }

        private async Task OpenHistoryFileByPathAsync(string dbPath, string errorTitle = "파일 찾기")
        {
            try
            {
                CancelRunningJobs();
                ClearMoveTargetHighlight();

                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                ResetCategoryCountText();
                ClearQueryCaches();
                SetLoadingState("이력 파일을 여는 중입니다...");

                HistoryOpenResult result = await Task.Run(() =>
                    Open_History_File.Open(dbPath)
                );

                if (result.Status == HistoryOpenStatus.UnsupportedFileType)
                {
                    ShowUnsupportedHistoryFileMessage(errorTitle);
                    BlockPointerInputAfterModal();
                    return;
                }

                if (result.Status == HistoryOpenStatus.InvalidTxtFile)
                {
                    ResetOpenedHistoryState("잘못된 텍스트 이력 파일입니다.");
                    ShowInvalidTxtHistoryFileMessage(errorTitle);
                    BlockPointerInputAfterModal();
                    return;
                }

                if (result.Status != HistoryOpenStatus.Success)
                {
                    ResetOpenedHistoryState("잘못된 DB 이력 파일입니다.");
                    ShowInvalidDbHistoryFileMessage(errorTitle);
                    BlockPointerInputAfterModal();
                    return;
                }

                _repository = new LogRepository(result.DbPath);

                _totalCount = result.TotalCount;
                _totalPages = CalculateTotalPages(_totalCount);

                StartDateText.Text = result.StartDate;
                EndDateText.Text = result.EndDate;

                ApplyDbDateRange(result.StartDate, result.EndDate);
                UpdateDateArrowVisibility();

                _currentMode = "all";
                _currentKeyword = "";
                _currentStartDate = "";
                _currentEndDate = "";
                _currentPage = 1;
                _activeRowsCacheKey = "all";

                TotalLogCountText.Text = _totalCount.ToString("N0");

                UpdateCategoryCardActiveStates();

                StartBackgroundCategoryCache();

                await LoadCurrentPageAsync(showLoading: false);
            }
            catch
            {
                ResetOpenedHistoryState("잘못된 DB 이력 파일입니다.");
                ShowInvalidDbHistoryFileMessage(errorTitle);
                BlockPointerInputAfterModal();
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

        private void ResetOpenedHistoryState(string emptyRowMessage)
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

            FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedEmptyCell());
            LogRowsPanel.Children.Add(Render_Log_Row.CreateEmptyRow(emptyRowMessage));
        }

        private static void ShowInvalidDbHistoryFileMessage(string title)
        {
            MessageBox.Show(
                "잘못된 이력 파일입니다.\n\n" +
                "가능한 원인:\n" +
                "- 이력 파일이 아님\n"+
                "- 파일 손상\n",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        private static void ShowInvalidTxtHistoryFileMessage(string title)
        {
            MessageBox.Show(
                "잘못된 텍스트 이력 파일입니다.\n\n" +
                "가능한 원인:\n" +
                "- 이력 TXT 파일이 아님\n",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        private static void ShowHistoryFileNotFoundMessage(string title)
        {
            MessageBox.Show(
                "이력 파일을 찾을 수 없습니다.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        private static void ShowUnsupportedHistoryFileMessage(string title)
        {
            MessageBox.Show(
                "지원하지 않는 이력 파일 형식입니다.\n\nDB 파일(.db) 또는 텍스트 이력 파일(.txt)을 선택하세요.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
    }
}
