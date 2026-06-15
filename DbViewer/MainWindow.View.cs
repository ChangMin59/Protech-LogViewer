using DbViewer.Services.Render;
using DbViewer.Services.View;
using System.Threading.Tasks;
using System.Windows;

namespace DbViewer
{
    public partial class MainWindow
    {
        // 이력 보기 버튼을 눌렀을 때 자동 탐색/파일 선택 선택창을 띄운다.
        private void ShowHistoryOpenChoice()
        {
            if (DateCalendarDropdown.Visibility == Visibility.Visible)
            {
                // 선택창과 날짜 달력이 겹치지 않게 닫는다.
                CloseDateCalendarDropdown();
            }

            if (PageSizeDropdown.Visibility == Visibility.Visible)
            {
                // 페이지 크기 드롭다운도 같이 닫는다.
                PageSizeDropdown.Visibility = Visibility.Collapsed;
            }

            HistoryOpenChoiceOverlay.Visibility = Visibility.Visible;
        }

        // 사용자가 파일창에서 .db 또는 .txt 이력 파일을 직접 선택해서 연다.
        private async Task OpenHistoryFileAsync()
        {
            if (_isOpeningHistory)
            {
                // 중복 클릭으로 파일창이 여러 개 뜨는 것을 막는다.
                return;
            }

            _isOpeningHistory = true;

            // 실제 Windows 파일 선택창을 연다.
            string? historyFilePath = Select_File.SelectHistoryFilePath(this);
            BlockPointerInputAfterModal();

            try
            {
                if (historyFilePath == null)
                {
                    // 사용자가 취소하면 기존 화면 상태를 그대로 둔다.
                    return;
                }

                await OpenHistoryFileByPathAsync(historyFilePath);
            }
            finally
            {
                // 성공/실패/취소 모두 다시 열기 가능 상태로 돌린다.
                _isOpeningHistory = false;
            }
        }

        // 기본 위치인 C:\Windows\Log.db를 자동으로 찾아 연다.
        private async Task OpenAutoHistoryFileAsync()
        {
            if (_isOpeningHistory)
            {
                return;
            }

            _isOpeningHistory = true;

            if (!Auto_File_Find.TryFindHistoryFile(out string autoDbPath))
            {
                // 현장 기본 파일이 없으면 사용자에게 안내하고 기존 화면은 유지한다.
                ShowHistoryFileNotFoundMessage("파일 자동 탐색");
                BlockPointerInputAfterModal();
                _isOpeningHistory = false;

                return;
            }

            try
            {
                // 예: autoDbPath=C:\Windows\Log.db.
                await OpenHistoryFileByPathAsync(autoDbPath, "파일 자동 탐색");
            }
            finally
            {
                _isOpeningHistory = false;
            }
        }

        // 선택된 이력 파일을 실제로 열고 화면 상태를 새 DB 기준으로 초기화한다.
        private async Task OpenHistoryFileByPathAsync(string dbPath, string errorTitle = "파일 찾기")
        {
            try
            {
                // .db는 검사, .txt는 임시 DB 변환 후 검사한다.
                HistoryOpenResult result = await Task.Run(() =>
                    Open_History_File.Open(dbPath)
                );

                if (result.Status == HistoryOpenStatus.UnsupportedFileType)
                {
                    // 기존 이력이 열려 있으면 유지하고, 아무것도 없을 때만 빈 메시지로 초기화한다.
                    ResetOpenedHistoryStateIfNothingIsOpen("지원하지 않는 이력 파일 형식입니다.");
                    ShowUnsupportedHistoryFileMessage(errorTitle);
                    BlockPointerInputAfterModal();
                    return;
                }

                if (result.Status == HistoryOpenStatus.InvalidTxtFile)
                {
                    // 잘못된 TXT를 열어도 기존에 보던 정상 이력은 유지한다.
                    ResetOpenedHistoryStateIfNothingIsOpen("잘못된 텍스트 이력 파일입니다.");
                    ShowInvalidTxtHistoryFileMessage(errorTitle);
                    BlockPointerInputAfterModal();
                    return;
                }

                if (result.Status != HistoryOpenStatus.Success)
                {
                    // 깨진 DB나 Log 테이블이 없는 DB도 기존 화면을 유지한다.
                    ResetOpenedHistoryStateIfNothingIsOpen("잘못된 DB 이력 파일입니다.");
                    ShowInvalidDbHistoryFileMessage(errorTitle);
                    BlockPointerInputAfterModal();
                    return;
                }

                // 새 이력을 열기 전에 기존 비동기 작업과 이동 하이라이트를 정리한다.
                CancelRunningJobs();
                ClearMoveTargetHighlight();

                // 새 파일을 열면 기존 카테고리 필터 선택은 해제한다.
                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                // 이전 파일의 카운트/캐시는 새 DB 기준과 다르므로 초기화한다.
                ResetCategoryCountText();
                ClearQueryCaches();

                // 검사/변환이 끝난 실제 SQLite DB 경로로 repository를 만든다.
                _repository = new LogRepository(result.DbPath);

                // 파일 검사 단계에서 이미 얻은 전체 건수와 페이지 수를 사용한다.
                _totalCount = result.TotalCount;
                _totalPages = CalculateTotalPages(_totalCount);

                // DB 실제 날짜 범위를 기간 선택 UI에 표시한다.
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
                _currentPeriodPreset = PeriodPreset.All;

                TotalLogCountText.Text = _totalCount.ToString("N0");

                UpdateCategoryCardActiveStates();
                UpdatePeriodPresetButtonStates();

                // 전체 카테고리 카운트는 백그라운드에서 천천히 계산한다.
                StartBackgroundCategoryCache();

                // 새 파일의 전체 로그 첫 페이지를 화면에 렌더링한다.
                await LoadCurrentPageAsync(showLoading: false);
            }
            catch
            {
                // 예외가 나도 기존에 열려 있던 이력은 유지한다.
                ResetOpenedHistoryStateIfNothingIsOpen("잘못된 DB 이력 파일입니다.");
                ShowInvalidDbHistoryFileMessage(errorTitle);
                BlockPointerInputAfterModal();
            }
        }

        // 전체 기간/전체 로그 첫 페이지를 불러온다.
        private async Task LoadAllFirstPageAsync()
        {
            if (_repository == null)
            {
                return;
            }

            ClearMoveTargetHighlight();

            // 전체 로그는 카테고리 필터를 해제한다.
            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            // 현재 조회 상태를 전체 로그로 바꾼다.
            _currentMode = "all";
            _currentKeyword = "";
            _currentStartDate = "";
            _currentEndDate = "";
            _currentPage = 1;
            _activeRowsCacheKey = "all";
            _currentPeriodPreset = PeriodPreset.All;

            // 실제 DB 전체 건수를 다시 읽어 페이지 수를 계산한다.
            _totalCount = await Task.Run(() => _repository.CountAllLogs());
            _totalPages = CalculateTotalPages(_totalCount);

            TotalLogCountText.Text = _totalCount.ToString("N0");
            UpdatePeriodPresetButtonStates();

            // 전체 로그 상태를 카테고리 해제/이동모드 기준 상태로 저장한다.
            SaveBaseQueryState();
            StartBackgroundCategoryCache();

            await LoadCurrentPageAsync(showLoading: false);
        }

        // 상단 "전체로그" 버튼 클릭 처리다.
        // 기간/검색 상태에서는 그 조건의 전체 로그로, 카테고리 상태에서는 이전 기본 조건으로 돌아간다.
        private async Task LoadTotalLogButtonAsync()
        {
            if (_repository == null)
            {
                return;
            }

            ClearMoveTargetHighlight();

            if (_currentMode == "category")
            {
                // 화재/제경보 필터를 보고 있었다면 필터만 풀고 이전 기간/검색/전체 조건으로 복귀한다.
                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                await RestoreBaseQueryAfterCategoryClearAsync();
                return;
            }

            if (_currentMode == "date_cache" ||
                _currentMode == "date" ||
                _currentMode == "search")
            {
                // 기간/검색 상태에서 전체로그 버튼은 DB 전체가 아니라 현재 조건 안의 전체 목록 첫 페이지를 의미한다.
                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                _currentPage = 1;
                await LoadCurrentPageAsync(showLoading: false);
                return;
            }

            await LoadAllFirstPageAsync();
        }

        // 열린 이력 상태를 완전히 비우고 빈 안내 행을 표시한다.
        private void ResetOpenedHistoryState(string emptyRowMessage)
        {
            // DB 연결과 현재 화면 로그를 모두 지운다.
            _repository = null;
            _currentRows.Clear();

            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            ResetCategoryCountText();
            ClearQueryCaches();

            // 기간 UI도 빈 상태로 돌린다.
            StartDateText.Text = "";
            EndDateText.Text = "";
            _dbStartDate = null;
            _dbEndDate = null;
            _currentPeriodPreset = PeriodPreset.All;
            UpdateDateArrowVisibility();
            UpdatePeriodPresetButtonStates();
            DateCalendarDropdown.Visibility = Visibility.Collapsed;

            ShowLogEmptyRow(emptyRowMessage);
        }

        // 아직 열린 정상 이력이 없을 때만 빈 상태로 초기화한다.
        // 이미 정상 이력을 보고 있다면 잘못된 파일 열기 실패 후에도 기존 로그를 유지한다.
        private void ResetOpenedHistoryStateIfNothingIsOpen(string emptyRowMessage)
        {
            if (_repository != null)
            {
                return;
            }

            ResetOpenedHistoryState(emptyRowMessage);
        }

        // 잘못된 DB 파일 안내 팝업이다.
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

        // 잘못된 TXT 이력 파일 안내 팝업이다.
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

        // 자동 탐색에서 C:\Windows\Log.db를 찾지 못했을 때 안내한다.
        private static void ShowHistoryFileNotFoundMessage(string title)
        {
            MessageBox.Show(
                "이력 파일을 찾을 수 없습니다.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        // .db/.txt가 아닌 파일을 선택했을 때 안내한다.
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
