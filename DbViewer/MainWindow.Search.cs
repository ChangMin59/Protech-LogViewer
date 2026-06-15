using DbViewer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DbViewer
{
    public partial class MainWindow
    {
        // 검색 버튼을 눌렀을 때 검색어가 포함된 로그의 첫 페이지를 보여준다.
        private async Task SearchFirstPageAsync()
        {
            if (_repository == null)
            {
                // 이력 파일이 아직 열리지 않았으면 검색할 DB가 없다.
                return;
            }

            // 검색 화면으로 바뀌면 이전 이동모드 하이라이트는 지운다.
            ClearMoveTargetHighlight();

            string keyword = SearchKeywordTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                // 빈 검색어는 전체 조회로 바꾸지 않고 아무 작업도 하지 않는다.
                return;
            }

            // 검색은 카테고리 필터와 동시에 적용하지 않으므로 선택 카테고리를 해제한다.
            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            bool searchInCurrentPeriod =
                (_currentMode == "date_cache" || _currentMode == "date") &&
                IsValidDate(_currentStartDate) &&
                IsValidDate(_currentEndDate);

            string periodStartDate = searchInCurrentPeriod ? _currentStartDate : "";
            string periodEndDate = searchInCurrentPeriod ? _currentEndDate : "";
            string periodCacheKey = searchInCurrentPeriod
                ? MakePeriodCacheKey(periodStartDate, periodEndDate)
                : "all";

            // 현재 조회 상태를 검색 모드로 기록한다.
            _currentMode = "search";
            _currentKeyword = keyword;
            _currentStartDate = periodStartDate;
            _currentEndDate = periodEndDate;
            _currentPage = 1;
            _activeRowsCacheKey = periodCacheKey;

            if (searchInCurrentPeriod)
            {
                // 기간 조회 중 검색하면 전체 DB가 아니라 해당 기간 캐시 안에서만 검색한다.
                List<LogRow>? cachedRows = await GetOrBuildRowsCacheAsync(periodStartDate, periodEndDate);

                if (cachedRows == null)
                {
                    return;
                }

                _totalCount = cachedRows.Count(row => IsRowMatchedBySearchFields(row, _currentKeyword));
            }
            else
            {
                // 전체 로그 상태에서는 기존처럼 DB 전체를 검색한다.
                _totalCount = await Task.Run(() => _repository.CountSearchLogs(_currentKeyword));
            }

            _totalPages = CalculateTotalPages(_totalCount);

            // 검색 결과 첫 페이지를 렌더링한다.
            await LoadCurrentPageAsync();
        }

        // 검색어를 지우고 검색 전 조회 조건으로 돌아간다.
        private async Task ClearSearchKeywordAndReloadAllAsync()
        {
            // 터치 키보드 사용 흐름을 위해 검색창 포커스와 커서를 유지한다.
            SearchKeywordTextBox.Text = "";
            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = 0;

            ClearMoveTargetHighlight();

            if (_repository == null)
            {
                // 열린 이력 파일이 없으면 검색창만 비운다.
                return;
            }

            // 검색어만 지우고 카테고리 선택은 해제 상태를 유지한다.
            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            bool restorePeriod =
                _currentMode == "search" &&
                IsValidDate(_currentStartDate) &&
                IsValidDate(_currentEndDate) &&
                _activeRowsCacheKey != "all";

            if (restorePeriod)
            {
                // 기간 상태에서 검색한 경우 X 버튼은 검색어만 지우고 해당 기간 전체 로그로 돌아간다.
                string startDate = _currentStartDate;
                string endDate = _currentEndDate;

                _currentMode = "date_cache";
                _currentKeyword = "";
                _currentPage = 1;
                _activeRowsCacheKey = MakePeriodCacheKey(startDate, endDate);

                List<LogRow>? cachedRows = await GetOrBuildRowsCacheAsync(startDate, endDate);

                if (cachedRows == null)
                {
                    return;
                }

                _totalCount = cachedRows.Count;
                _totalPages = CalculateTotalPages(_totalCount);
                TotalLogCountText.Text = _totalCount.ToString("N0");

                SaveBaseQueryState();

                await LoadCurrentPageAsync(showLoading: false, resetScroll: true);
                return;
            }

            // 현재 조회 상태를 전체 로그 기준으로 초기화한다.
            _currentMode = "all";
            _currentKeyword = "";
            _currentStartDate = "";
            _currentEndDate = "";
            _currentPage = 1;
            _activeRowsCacheKey = "all";

            // 전체 건수와 전체 페이지를 다시 계산한다.
            _totalCount = await Task.Run(() => _repository.CountAllLogs());
            _totalPages = CalculateTotalPages(_totalCount);

            TotalLogCountText.Text = _totalCount.ToString("N0");

            // 이후 카테고리 필터 해제/이동모드 복귀 기준이 되는 기본 조회 상태로 저장한다.
            SaveBaseQueryState();

            await LoadCurrentPageAsync(showLoading: false, resetScroll: true);
        }

        // 검색어가 실제 로그 주요 컬럼 중 하나에 들어 있는지 확인한다.
        private static bool IsRowMatchedBySearchFields(LogRow row, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            return row.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Action.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Section.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Contents.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Packet.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }
}
