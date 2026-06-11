using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DbViewer
{
    public partial class MainWindow
    {
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

        private int CalculateTotalPages(int totalCount)
        {
            return Math.Max(1, (int)Math.Ceiling(totalCount / (double)_pageSize));
        }
    }
}
