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

            // 현재 조회 상태를 검색 모드로 기록한다.
            _currentMode = "search";
            _currentKeyword = keyword;
            _currentStartDate = "";
            _currentEndDate = "";
            _currentPage = 1;

            // 실제 검색 대상 컬럼: Type, Action, Section, Contents, Packet.
            _totalCount = await Task.Run(() => _repository.CountSearchLogs(_currentKeyword));
            _totalPages = CalculateTotalPages(_totalCount);

            // 검색 결과 첫 페이지를 렌더링한다.
            await LoadCurrentPageAsync();
        }

        // 검색어를 지우고 전체 로그 첫 페이지로 돌아간다.
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

            // 전체 로그로 돌아가므로 카테고리 선택을 모두 해제한다.
            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

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
    }
}
