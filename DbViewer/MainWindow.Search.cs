using System.Threading.Tasks;

namespace DbViewer
{
    public partial class MainWindow
    {
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

        private async Task ClearSearchKeywordAndReloadAllAsync()
        {
            SearchKeywordTextBox.Text = "";
            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = 0;

            ClearMoveTargetHighlight();

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

            await LoadCurrentPageAsync(showLoading: false, resetScroll: true);
        }
    }
}
