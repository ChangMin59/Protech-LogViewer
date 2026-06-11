using DbViewer.Models;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DbViewer
{
    public partial class MainWindow
    {
        private static void ShowInvalidDateRangeMessage(string message)
        {
            MessageBox.Show(
                message,
                "기간 조회",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
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
                ShowInvalidDateRangeMessage("시작일과 종료일을 확인하세요.");
                return;
            }

            DateTime start = DateTime.Parse(startDate);
            DateTime end = DateTime.Parse(endDate);

            if (start > end)
            {
                ShowInvalidDateRangeMessage("시작일은 종료일보다 늦을 수 없습니다.");
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
    }
}
