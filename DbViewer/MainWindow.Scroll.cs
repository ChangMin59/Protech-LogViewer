using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DbViewer
{
    public partial class MainWindow
    {
        private void CloseDropdownsWhenOutsideClicked(DependencyObject? source)
        {
            if (PageSizeDropdown.Visibility == Visibility.Visible)
            {
                bool clickedPageSizeButton = IsDescendantOf(source, PageSizeButton);
                bool clickedPageSizeDropdown = IsDescendantOf(source, PageSizeDropdown);

                if (!clickedPageSizeButton && !clickedPageSizeDropdown)
                {
                    PageSizeDropdown.Visibility = Visibility.Collapsed;
                }
            }

            if (DateCalendarDropdown.Visibility == Visibility.Visible)
            {
                bool clickedStartDate = IsDescendantOf(source, StartDateButton);
                bool clickedEndDate = IsDescendantOf(source, EndDateButton);
                bool clickedDateCalendar = IsDescendantOf(source, DateCalendarDropdown);

                if (!clickedStartDate && !clickedEndDate && !clickedDateCalendar)
                {
                    CloseDateCalendarDropdown();
                }
            }
        }

        private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            RenderVirtualRows();

            FixedTimeScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            HeaderHorizontalScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        private void LogScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;

            if (IsInsideScrollBar(source))
            {
                return;
            }

            _isLogContentDragging = true;
            _dragStartPoint = e.GetPosition(LogScrollViewer);
            _dragStartHorizontalOffset = LogScrollViewer.HorizontalOffset;
            _dragStartVerticalOffset = LogScrollViewer.VerticalOffset;

            LogScrollViewer.CaptureMouse();
            e.Handled = true;
        }

        private void LogScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isLogContentDragging)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndLogContentDrag();
                return;
            }

            Point currentPoint = e.GetPosition(LogScrollViewer);

            double dragSpeed = 1.5;
            double deltaX = (currentPoint.X - _dragStartPoint.X) * dragSpeed;
            double deltaY = (currentPoint.Y - _dragStartPoint.Y) * dragSpeed;

            LogScrollViewer.ScrollToHorizontalOffset(_dragStartHorizontalOffset - deltaX);
            LogScrollViewer.ScrollToVerticalOffset(_dragStartVerticalOffset - deltaY);

            e.Handled = true;
        }

        private void LogScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isLogContentDragging)
            {
                return;
            }

            EndLogContentDrag();
            e.Handled = true;
        }

        private void LogScrollViewer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isLogContentDragging && e.LeftButton != MouseButtonState.Pressed)
            {
                EndLogContentDrag();
            }
        }

        private void EndLogContentDrag()
        {
            _isLogContentDragging = false;

            if (LogScrollViewer.IsMouseCaptured)
            {
                LogScrollViewer.ReleaseMouseCapture();
            }
        }

        private bool IsInsideScrollBar(DependencyObject? source)
        {
            DependencyObject? current = source;

            while (current != null)
            {
                if (current is ScrollBar || current is Thumb)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void LogScrollViewer_ManipulationBoundaryFeedback(
            object sender,
            ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true;
        }
    }
}
