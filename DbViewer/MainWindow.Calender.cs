using DbViewer.Models;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DbViewer
{
    public partial class MainWindow
    {
        // 기간 선택이 잘못됐을 때 사용자에게 안내한다.
        private static void ShowInvalidDateRangeMessage(string message)
        {
            MessageBox.Show(
                message,
                "기간 조회",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        // 시작일/종료일 텍스트 기준으로 해당 기간 로그 첫 페이지를 보여준다.
        private async Task LoadDateFirstPageAsync(PeriodPreset periodPreset = PeriodPreset.Custom)
        {
            if (_repository == null)
            {
                // 이력 DB가 열리지 않았으면 기간 조회할 데이터가 없다.
                return;
            }

            ClearMoveTargetHighlight();

            // 실제 입력 형식은 yyyy-MM-dd다.
            string startDate = StartDateText.Text.Trim();
            string endDate = EndDateText.Text.Trim();

            if (!IsValidDate(startDate) || !IsValidDate(endDate))
            {
                // 예: 2026-06-01 형식이 아니면 조회하지 않는다.
                ShowInvalidDateRangeMessage("시작일과 종료일을 확인하세요.");
                return;
            }

            DateTime start = DateTime.Parse(startDate);
            DateTime end = DateTime.Parse(endDate);

            if (start > end)
            {
                // 시작일이 종료일보다 늦으면 기간 조건이 성립하지 않는다.
                ShowInvalidDateRangeMessage("시작일은 종료일보다 늦을 수 없습니다.");
                return;
            }

            // 조회가 오래 걸려도 사용자가 선택한 기간 프리셋은 즉시 표시한다.
            _currentPeriodPreset = periodPreset;
            UpdatePeriodPresetButtonStates();

            // 기간 조회는 화재/제경보 카테고리 필터와 동시에 유지하지 않는다.
            _selectedCategories.Clear();
            UpdateCategoryCardActiveStates();

            // 기간 조회는 캐시 기반 모드로 전환한다.
            _currentMode = "date_cache";
            _currentStartDate = startDate;
            _currentEndDate = endDate;
            _currentKeyword = "";
            _currentPage = 1;
            _activeRowsCacheKey = MakePeriodCacheKey(startDate, endDate);

            // 예: 2026-06-01~2026-06-07 기간의 전체 LogRow를 캐시에 만든다.
            List<LogRow>? cachedRows = await GetOrBuildRowsCacheAsync(startDate, endDate);

            if (cachedRows == null)
            {
                // 캐시 생성 중 취소되었거나 실패한 경우다.
                return;
            }

            // 기간 내 실제 로그 수를 기준으로 페이지 수를 계산한다.
            _totalCount = cachedRows.Count;
            _totalPages = CalculateTotalPages(_totalCount);

            TotalLogCountText.Text = _totalCount.ToString("N0");

            // 카테고리 필터 해제/이동모드 복귀 기준을 이 기간 조회로 저장한다.
            SaveBaseQueryState();

            await LoadCurrentPageAsync(showLoading: false);
        }

        // 최근 N일 버튼을 적용한다.
        // 예: 7일 버튼은 DB 최신 날짜를 기준으로 최신일 포함 7일 범위를 만든다.
        private async Task ApplyRecentDaysAsync(int days)
        {
            if (_repository == null)
            {
                return;
            }

            PeriodPreset periodPreset = days switch
            {
                1 => PeriodPreset.OneDay,
                7 => PeriodPreset.SevenDays,
                30 => PeriodPreset.ThirtyDays,
                _ => PeriodPreset.Custom
            };

            // DB 날짜 범위를 읽기 전 먼저 버튼 선택 상태를 보여준다.
            _currentPeriodPreset = periodPreset;
            UpdatePeriodPresetButtonStates();

            // DB 실제 날짜 범위에서 최신 날짜를 가져온다.
            (string startDate, string endDate) range = await Task.Run(() =>
                _repository.GetLogDateRange()
            );

            if (!IsValidDate(range.endDate))
            {
                // DB에 유효한 DTIME이 없으면 적용하지 않는다.
                return;
            }

            // 예: 최신일 2026-06-30, days=7이면 2026-06-24~2026-06-30.
            DateTime end = DateTime.Parse(range.endDate);
            DateTime start = end.AddDays(-(days - 1));

            StartDateText.Text = start.ToString("yyyy-MM-dd");
            EndDateText.Text = end.ToString("yyyy-MM-dd");

            UpdateDateArrowVisibility();

            // 날짜 텍스트를 바꾼 뒤 실제 기간 조회를 실행한다.
            await LoadDateFirstPageAsync(periodPreset);
        }

        // 전체 버튼을 눌렀을 때 DB 전체 기간과 전체 로그 첫 페이지로 돌아간다.
        private async Task ApplyAllPeriodAsync()
        {
            if (_repository == null)
            {
                return;
            }

            // DB 날짜 범위를 읽기 전 먼저 전체 버튼 선택 상태를 보여준다.
            _currentPeriodPreset = PeriodPreset.All;
            UpdatePeriodPresetButtonStates();

            // DB 안의 가장 오래된 날짜와 최신 날짜를 읽는다.
            (string startDate, string endDate) range = await Task.Run(() =>
                _repository.GetLogDateRange()
            );

            StartDateText.Text = range.startDate;
            EndDateText.Text = range.endDate;

            ApplyDbDateRange(range.startDate, range.endDate);
            UpdateDateArrowVisibility();

            // "전체" 기간 버튼은 기간 캐시가 아니라 전체 로그 모드로 돌아간다.
            await LoadAllFirstPageAsync();
        }

        // 시작일/종료일 버튼을 누르면 해당 버튼 아래에 달력 드롭다운을 연다.
        private void OpenDateCalendar(Border targetButton, TextBlock targetText)
        {
            if (_repository == null)
            {
                // DB가 없으면 선택 가능한 날짜 범위도 없다.
                return;
            }

            if (!_dbStartDate.HasValue || !_dbEndDate.HasValue)
            {
                // DB 날짜 범위가 아직 계산되지 않았으면 달력을 열지 않는다.
                return;
            }

            bool isOpen = DateCalendarDropdown.Visibility == Visibility.Visible;

            if (isOpen && ReferenceEquals(_dateTargetButton, targetButton))
            {
                // 같은 날짜 버튼을 다시 누르면 달력을 닫는다.
                CloseDateCalendarDropdown();
                return;
            }

            if (PageSizeDropdown.Visibility == Visibility.Visible)
            {
                // 페이지 크기 드롭다운과 달력을 동시에 열지 않는다.
                PageSizeDropdown.Visibility = Visibility.Collapsed;
            }

            // 어떤 날짜 텍스트를 바꿀지 기억한다.
            _dateTargetButton = targetButton;
            _dateTargetText = targetText;

            // 달력에서 선택 가능한 범위를 실제 DB 날짜 안으로 제한한다.
            DateCalendar.DisplayDateStart = _dbStartDate;
            DateCalendar.DisplayDateEnd = _dbEndDate;

            // SelectedDate 설정 중 SelectionChanged가 조회를 실행하지 않게 막는다.
            _suppressDateCalendarChange = true;

            if (IsValidDate(targetText.Text))
            {
                // 현재 텍스트 날짜가 유효하면 그 날짜를 선택 상태로 연다.
                DateTime selectedDate = DateTime.Parse(targetText.Text);

                DateCalendar.SelectedDate = selectedDate;
                DateCalendar.DisplayDate = selectedDate;
            }
            else
            {
                // 날짜가 비어 있으면 DB 최신 날짜를 기본 선택으로 둔다.
                DateCalendar.SelectedDate = _dbEndDate.Value;
                DateCalendar.DisplayDate = _dbEndDate.Value;
            }

            _suppressDateCalendarChange = false;

            // 버튼 아래 위치를 RootGrid 기준 좌표로 계산한다.
            Point point = targetButton.TranslatePoint(
                new Point(0, targetButton.ActualHeight + 6),
                RootGrid
            );

            double x = point.X;
            double y = point.Y;

            if (x + DateCalendarDropdown.Width > RootGrid.ActualWidth)
            {
                // 오른쪽 화면 밖으로 나가면 안쪽으로 당긴다.
                x = RootGrid.ActualWidth - DateCalendarDropdown.Width - 12;
            }

            if (y + DateCalendarDropdown.Height > RootGrid.ActualHeight)
            {
                // 아래쪽 화면 밖으로 나가면 버튼 위쪽으로 띄운다.
                y = point.Y - DateCalendarDropdown.Height - targetButton.ActualHeight - 12;
            }

            DateCalendarDropdownTransform.X = Math.Max(12, x);
            DateCalendarDropdownTransform.Y = Math.Max(12, y);

            DateCalendarDropdown.Visibility = Visibility.Visible;
        }

        // 달력에서 날짜를 선택하면 시작일/종료일 텍스트를 바꾸고 바로 기간 조회를 실행한다.
        private async void ApplySelectedDateFromCalendar()
        {
            if (_suppressDateCalendarChange)
            {
                // 코드로 SelectedDate를 설정하는 중이면 무시한다.
                return;
            }

            if (_dateTargetText == null)
            {
                // 어느 텍스트를 바꿔야 할지 없으면 무시한다.
                return;
            }

            if (!DateCalendar.SelectedDate.HasValue)
            {
                // 선택된 날짜가 없으면 무시한다.
                return;
            }

            string selectedDate = DateCalendar.SelectedDate.Value.ToString("yyyy-MM-dd");

            // 사용자가 누른 시작일 또는 종료일 텍스트에 선택 날짜를 넣는다.
            _dateTargetText.Text = selectedDate;
            // 달력 닫힘 직후 아래 카테고리 버튼 클릭이 같이 먹히는 것을 짧게 막는다.
            BlockCategoryClickBriefly();

            DateCalendarDropdown.Visibility = Visibility.Collapsed;

            if (IsValidDate(StartDateText.Text) && IsValidDate(EndDateText.Text))
            {
                DateTime start = DateTime.Parse(StartDateText.Text);
                DateTime end = DateTime.Parse(EndDateText.Text);

                if (start > end)
                {
                    // 시작일이 종료일보다 뒤가 되면 반대쪽 날짜도 같은 날짜로 맞춰 유효 기간을 만든다.
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
                // 날짜 선택 즉시 기간 조회를 다시 실행한다.
                await LoadDateFirstPageAsync();
            }
        }

        // DB 전체 날짜 범위를 달력 선택 가능 범위로 적용한다.
        private void ApplyDbDateRange(string startDate, string endDate)
        {
            // 예: DB 시작일=2023-01-24, 종료일=2024-10-12.
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
                // 달력을 열었을 때 기본 표시 월은 최신 로그 날짜 기준으로 둔다.
                DateCalendar.DisplayDate = _dbEndDate.Value;
            }
        }

        // 시작일/종료일 텍스트가 유효할 때만 아래 화살표를 보여준다.
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

        // 날짜 달력 드롭다운을 닫고 대상 버튼/텍스트 정보를 초기화한다.
        private void CloseDateCalendarDropdown()
        {
            DateCalendarDropdown.Visibility = Visibility.Collapsed;
            _dateTargetButton = null;
            _dateTargetText = null;
        }

        // 달력 선택 직후 카테고리 버튼이 실수로 눌리는 것을 잠깐 막는다.
        private void BlockCategoryClickBriefly()
        {
            _ignoreCategoryClickUntil = DateTime.Now.AddMilliseconds(350);
        }

        // 현재 시점이 카테고리 클릭 차단 시간 안인지 확인한다.
        private bool ShouldIgnoreCategoryClick()
        {
            return DateTime.Now <= _ignoreCategoryClickUntil;
        }
    }
}
