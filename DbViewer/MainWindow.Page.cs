using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DbViewer
{
    public partial class MainWindow
    {
        // 이전 페이지 버튼을 눌렀을 때 현재 조건의 이전 로그 페이지를 불러온다.
        private async Task MovePrevPageAsync()
        {
            if (_repository == null)
            {
                // 열린 이력 DB가 없으면 페이지 이동할 데이터가 없다.
                return;
            }

            if (_currentPage <= 1)
            {
                // 이미 1페이지면 더 이전으로 이동하지 않는다.
                return;
            }

            // 페이지가 바뀌면 이동모드 하이라이트 위치도 더 이상 유효하지 않다.
            ClearMoveTargetHighlight();

            _currentPage--;

            if (_currentMode == "category")
            {
                // 화재/제경보 같은 카테고리 필터는 DB 재조회가 아니라 캐시 목록에서 페이지를 자른다.
                await LoadCategoryPageFromCacheAsync();
                return;
            }

            // 전체/기간/검색 모드는 현재 모드에 맞게 DB 또는 캐시에서 페이지를 다시 읽는다.
            await LoadCurrentPageAsync(showLoading: false);
        }

        // 다음 페이지 버튼을 눌렀을 때 현재 조건의 다음 로그 페이지를 불러온다.
        private async Task MoveNextPageAsync()
        {
            if (_repository == null)
            {
                // 열린 이력 DB가 없으면 이동하지 않는다.
                return;
            }

            if (_currentPage >= _totalPages)
            {
                // 마지막 페이지면 더 이동하지 않는다.
                return;
            }

            ClearMoveTargetHighlight();

            _currentPage++;

            if (_currentMode == "category")
            {
                // 카테고리 필터는 이미 분류된 로그 목록에서 다음 페이지를 보여준다.
                await LoadCategoryPageFromCacheAsync();
                return;
            }

            await LoadCurrentPageAsync(showLoading: false);
        }

        // 페이지 크기 선택 드롭다운을 열거나 닫는다.
        private void TogglePageSizeDropdown()
        {
            if (DateCalendarDropdown.Visibility == Visibility.Visible)
            {
                // 날짜 달력과 페이지 크기 드롭다운이 동시에 열리지 않게 한다.
                CloseDateCalendarDropdown();
            }

            PageSizeDropdown.Visibility =
                PageSizeDropdown.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            UpdatePageSizeDropdownStyle();
        }

        // 한 화면에 보여줄 로그 건수를 바꾼다.
        // 예: 25건, 1000건, 5000건, 10000건, 50000건, 100000건.
        private async Task ChangePageSizeAsync(int size)
        {
            PageSizeDropdown.Visibility = Visibility.Collapsed;

            ClearMoveTargetHighlight();

            // 실제 페이지 크기와 버튼 표시 문구를 같이 갱신한다.
            _pageSize = size;
            PageSizeText.Text = $"{size:N0}건";

            // 페이지 크기가 바뀌면 항상 1페이지부터 다시 보여준다.
            _currentPage = 1;
            _totalPages = CalculateTotalPages(_totalCount);

            UpdatePageSizeDropdownStyle();

            if (_currentMode == "category")
            {
                // 카테고리 모드는 선택된 로그 캐시에서 새 pageSize만큼 잘라 보여준다.
                await LoadCategoryPageFromCacheAsync();
                return;
            }

            // 전체/기간/검색 모드는 현재 조건의 첫 페이지를 다시 렌더링한다.
            await LoadCurrentPageAsync(showLoading: false);
        }

        // 현재 선택된 페이지 크기 버튼 색상을 갱신한다.
        private void UpdatePageSizeDropdownStyle()
        {
            SetPageSizeItemStyle(PageSize25Button, 25);
            SetPageSizeItemStyle(PageSize1000Button, 1000);
            SetPageSizeItemStyle(PageSize5000Button, 5000);
            SetPageSizeItemStyle(PageSize10000Button, 10000);
            SetPageSizeItemStyle(PageSize50000Button, 50000);
            SetPageSizeItemStyle(PageSize100000Button, 100000);
        }

        // 특정 페이지 크기 버튼의 선택/비선택 스타일을 적용한다.
        private void SetPageSizeItemStyle(Border button, int size)
        {
            bool isSelected = _pageSize == size;

            // 선택된 크기는 주황색 배경, 나머지는 투명 배경이다.
            button.Background = isSelected
                ? new SolidColorBrush(Color.FromRgb(255, 90, 67))
                : new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));

            if (button.Child is TextBlock textBlock)
            {
                // 선택된 항목은 흰 글자, 나머지는 진한 남색 글자다.
                textBlock.Foreground = isSelected
                    ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                    : new SolidColorBrush(Color.FromRgb(35, 57, 93));
            }
        }

        // 하단 페이지 버튼들을 현재 페이지/전체 페이지 기준으로 다시 만든다.
        private void RebuildPaginationButtons()
        {
            PaginationPanel.Children.Clear();

            // 예: 총 12,000페이지 · 300,000건.
            TotalPageInfoText.Text = $"총 {_totalPages:N0}페이지 · {_totalCount:N0}건";

            List<int> visiblePageNumbers = GetVisiblePageNumbers();
            int firstVisiblePage = visiblePageNumbers.Count > 0
                ? visiblePageNumbers[0]
                : 1;
            int lastVisiblePage = visiblePageNumbers.Count > 0
                ? visiblePageNumbers[^1]
                : 1;

            if (firstVisiblePage > 1)
            {
                // 현재 5개 페이지 그룹이 첫 그룹이 아니면 처음 버튼을 보여준다.
                PaginationPanel.Children.Add(CreatePaginationButton(
                    text: "«",
                    isActive: false,
                    isEnabled: true,
                    toolTip: "처음",
                    onClick: async () => await MoveFirstPageAsync()
                ));
            }

            // 이전 버튼은 1페이지에서는 비활성화된다.
            PaginationPanel.Children.Add(CreatePaginationButton(
                text: "‹",
                isActive: false,
                isEnabled: _currentPage > 1,
                toolTip: "이전",
                onClick: async () => await MovePrevPageAsync()
            ));

            foreach (int pageNumber in visiblePageNumbers)
            {
                int capturedPage = pageNumber;

                // 현재 페이지 그룹의 숫자 버튼을 만든다.
                PaginationPanel.Children.Add(CreatePaginationButton(
                    text: pageNumber.ToString(),
                    isActive: pageNumber == _currentPage,
                    isEnabled: true,
                    toolTip: null,
                    onClick: async () =>
                    {
                        if (_currentPage == capturedPage)
                        {
                            // 이미 보고 있는 페이지를 다시 누르면 아무 작업도 하지 않는다.
                            return;
                        }

                        _currentPage = capturedPage;

                        if (_currentMode == "category")
                        {
                            // 카테고리 필터는 캐시에서 해당 페이지를 자른다.
                            await LoadCategoryPageFromCacheAsync();
                            return;
                        }

                        await LoadCurrentPageAsync(showLoading: false);
                    }
                ));
            }

            // 다음 버튼은 마지막 페이지에서는 비활성화된다.
            PaginationPanel.Children.Add(CreatePaginationButton(
                text: "›",
                isActive: false,
                isEnabled: _currentPage < _totalPages,
                toolTip: "다음",
                onClick: async () => await MoveNextPageAsync()
            ));

            if (lastVisiblePage < _totalPages)
            {
                // 현재 5개 페이지 그룹 뒤에 페이지가 더 있으면 끝 버튼을 보여준다.
                PaginationPanel.Children.Add(CreatePaginationButton(
                    text: "»",
                    isActive: false,
                    isEnabled: true,
                    toolTip: "끝",
                    onClick: async () => await MoveLastPageAsync()
                ));
            }

            KeepPaginationAwayFromPageInfo();
        }

        // 페이지 버튼은 전체 하단 중앙에 두고, 오른쪽 페이지 정보 영역과 겹칠 때만 왼쪽으로 피한다.
        private void KeepPaginationAwayFromPageInfo()
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                const double minimumGap = 18.0;

                PaginationPanelTransform.X = 0;
                RootGrid.UpdateLayout();

                if (PaginationPanel.ActualWidth <= 0 || TotalPageInfoText.ActualWidth <= 0)
                {
                    return;
                }

                double paginationRight = PaginationPanel
                    .TranslatePoint(new Point(PaginationPanel.ActualWidth, 0), RootGrid)
                    .X;
                double pageInfoLeft = TotalPageInfoText
                    .TranslatePoint(new Point(0, 0), RootGrid)
                    .X;
                double overlap = paginationRight + minimumGap - pageInfoLeft;

                if (overlap <= 0)
                {
                    return;
                }

                PaginationPanelTransform.X = -overlap;
            }), DispatcherPriority.Loaded);
        }

        // 첫 페이지로 이동한다.
        private async Task MoveFirstPageAsync()
        {
            if (_repository == null || _currentPage <= 1)
            {
                return;
            }

            ClearMoveTargetHighlight();
            _currentPage = 1;

            if (_currentMode == "category")
            {
                await LoadCategoryPageFromCacheAsync();
                return;
            }

            await LoadCurrentPageAsync(showLoading: false);
        }

        // 마지막 페이지로 이동한다.
        private async Task MoveLastPageAsync()
        {
            if (_repository == null || _currentPage >= _totalPages)
            {
                return;
            }

            ClearMoveTargetHighlight();
            _currentPage = _totalPages;

            if (_currentMode == "category")
            {
                await LoadCategoryPageFromCacheAsync();
                return;
            }

            await LoadCurrentPageAsync(showLoading: false);
        }

        // 현재 페이지가 속한 5개 단위 페이지 번호 그룹을 반환한다.
        // 예: 현재 7페이지면 6,7,8,9,10을 보여준다.
        private List<int> GetVisiblePageNumbers()
        {
            List<int> pages = new();

            const int groupSize = 5;

            // 1~5, 6~10, 11~15 식으로 그룹을 나눈다.
            int currentGroupIndex = (_currentPage - 1) / groupSize;

            int startPage = currentGroupIndex * groupSize + 1;
            int endPage = Math.Min(startPage + groupSize - 1, _totalPages);

            for (int page = startPage; page <= endPage; page++)
            {
                pages.Add(page);
            }

            return pages;
        }

        // 하단 페이지 버튼 UI를 만든다.
        private Border CreatePaginationButton(
            string text,
            bool isActive,
            bool isEnabled,
            string? toolTip,
            Func<Task> onClick)
        {
            bool isNavigationButton = text is "«" or "‹" or "›" or "»";

            // 현재 페이지는 주황색, 일반 버튼은 반투명 흰색으로 보인다.
            Border button = new()
            {
                Width = isNavigationButton ? 40 : double.NaN,
                MinWidth = isNavigationButton ? 40 : 42,
                Height = 40,
                CornerRadius = new CornerRadius(12),
                Padding = isNavigationButton
                    ? new Thickness(0)
                    : new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = isEnabled ? Cursors.Hand : Cursors.Arrow,
                Opacity = isEnabled ? 1.0 : 0.45,
                ToolTip = toolTip,
                Background = isActive
                    ? new SolidColorBrush(Color.FromRgb(255, 90, 67))
                    : new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)),
                BorderBrush = isActive
                    ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            // 버튼 텍스트다. 예: "‹ 이전", "1", "다음 ›".
            TextBlock label = new()
            {
                Text = text,
                FontSize = 15,
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
                // 마우스 클릭과 터치 입력 모두 같은 페이지 이동 함수를 호출한다.
                button.MouseLeftButtonUp += async (_, e) =>
                {
                    e.Handled = true;

                    if (ShouldIgnorePointerAction())
                    {
                        return;
                    }

                    await onClick();
                };

                button.TouchDown += async (_, e) =>
                {
                    e.Handled = true;

                    if (ShouldIgnorePointerAction())
                    {
                        return;
                    }

                    await onClick();
                };
            }

            return button;
        }

        // 전체 건수와 현재 pageSize를 기준으로 총 페이지 수를 계산한다.
        private int CalculateTotalPages(int totalCount)
        {
            // 로그가 0건이어도 화면은 최소 1페이지로 표시한다.
            return Math.Max(1, (int)Math.Ceiling(totalCount / (double)_pageSize));
        }
    }
}
