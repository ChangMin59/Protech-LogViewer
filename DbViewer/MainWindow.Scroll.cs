using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DbViewer
{
    public partial class MainWindow
    {
        // 화면 바깥을 누르면 페이지 크기/날짜 달력 드롭다운을 닫는다.
        private void CloseDropdownsWhenOutsideClicked(DependencyObject? source)
        {
            if (PageSizeDropdown.Visibility == Visibility.Visible)
            {
                // 페이지 크기 버튼 또는 드롭다운 내부 클릭이면 닫지 않는다.
                bool clickedPageSizeButton = IsDescendantOf(source, PageSizeButton);
                bool clickedPageSizeDropdown = IsDescendantOf(source, PageSizeDropdown);

                if (!clickedPageSizeButton && !clickedPageSizeDropdown)
                {
                    // 다른 곳을 누른 경우만 드롭다운을 닫는다.
                    PageSizeDropdown.Visibility = Visibility.Collapsed;
                }
            }

            if (DateCalendarDropdown.Visibility == Visibility.Visible)
            {
                // 시작일/종료일 버튼 또는 달력 내부 클릭이면 닫지 않는다.
                bool clickedStartDate = IsDescendantOf(source, StartDateButton);
                bool clickedEndDate = IsDescendantOf(source, EndDateButton);
                bool clickedDateCalendar = IsDescendantOf(source, DateCalendarDropdown);

                if (!clickedStartDate && !clickedEndDate && !clickedDateCalendar)
                {
                    // 달력 바깥 클릭이면 날짜 선택 드롭다운을 닫는다.
                    CloseDateCalendarDropdown();
                }
            }
        }

        // 로그 스크롤이 움직일 때 가상 행과 고정 헤더/시간축을 동기화한다.
        private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 현재 화면에 보여야 하는 25/1000/... 로그 구간만 다시 그린다.
            RenderVirtualRows();

            // 왼쪽 고정 시간 컬럼은 오른쪽 로그 영역의 세로 스크롤을 그대로 따라간다.
            FixedTimeScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            // 상단 헤더는 오른쪽 로그 영역의 가로 스크롤을 그대로 따라간다.
            HeaderHorizontalScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        // 로그 영역을 마우스/터치로 누르면 내용 드래그 스크롤을 시작한다.
        private void LogScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;

            if (IsInsideScrollBar(source))
            {
                // 실제 스크롤바를 잡은 경우는 기본 ScrollViewer 동작에 맡긴다.
                return;
            }

            // 드래그 시작 지점과 현재 스크롤 위치를 저장한다.
            _isLogContentDragging = true;
            _dragStartPoint = e.GetPosition(LogScrollViewer);
            _dragStartHorizontalOffset = LogScrollViewer.HorizontalOffset;
            _dragStartVerticalOffset = LogScrollViewer.VerticalOffset;

            LogScrollViewer.CaptureMouse();
            e.Handled = true;
        }

        // 드래그 이동량만큼 로그 영역을 가로/세로로 스크롤한다.
        private void LogScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isLogContentDragging)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                // 버튼이 놓였는데 상태만 남아 있으면 드래그를 종료한다.
                EndLogContentDrag();
                return;
            }

            Point currentPoint = e.GetPosition(LogScrollViewer);

            // 손가락/마우스 이동량보다 조금 빠르게 움직여 터치 스크롤 느낌을 맞춘다.
            double dragSpeed = 1.5;
            double deltaX = (currentPoint.X - _dragStartPoint.X) * dragSpeed;
            double deltaY = (currentPoint.Y - _dragStartPoint.Y) * dragSpeed;

            // 드래그 방향과 반대로 ScrollViewer offset을 움직인다.
            LogScrollViewer.ScrollToHorizontalOffset(_dragStartHorizontalOffset - deltaX);
            LogScrollViewer.ScrollToVerticalOffset(_dragStartVerticalOffset - deltaY);

            e.Handled = true;
        }

        // 클릭/터치가 끝나면 드래그 스크롤을 종료한다.
        private void LogScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isLogContentDragging)
            {
                return;
            }

            EndLogContentDrag();
            e.Handled = true;
        }

        // 마우스가 영역 밖으로 나갔고 버튼도 눌려 있지 않으면 드래그 상태를 정리한다.
        private void LogScrollViewer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isLogContentDragging && e.LeftButton != MouseButtonState.Pressed)
            {
                EndLogContentDrag();
            }
        }

        // 드래그 스크롤 상태와 마우스 캡처를 해제한다.
        private void EndLogContentDrag()
        {
            _isLogContentDragging = false;

            if (LogScrollViewer.IsMouseCaptured)
            {
                // 캡처를 풀어야 이후 다른 버튼 클릭이 정상 처리된다.
                LogScrollViewer.ReleaseMouseCapture();
            }
        }

        // 클릭한 위치가 ScrollBar/Thumb 내부인지 확인한다.
        private bool IsInsideScrollBar(DependencyObject? source)
        {
            DependencyObject? current = source;

            while (current != null)
            {
                if (current is ScrollBar || current is Thumb)
                {
                    // 스크롤바 클릭은 직접 드래그 스크롤로 빼앗지 않는다.
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        // 터치 스크롤 끝에서 WPF가 튕김 피드백을 내는 것을 막는다.
        private void LogScrollViewer_ManipulationBoundaryFeedback(
            object sender,
            ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }
    }
}
