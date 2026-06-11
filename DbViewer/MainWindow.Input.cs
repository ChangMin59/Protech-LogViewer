using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace DbViewer
{
    public partial class MainWindow
    {
        private void InitializeEvents()
        {
            HistoryViewButton.MouseLeftButtonUp += (_, _) => ShowHistoryOpenChoice();

            AttachPointerAction(HistoryRecoverButton, RecoverHistoryFileAsync);
            AttachOverlayChoiceAction(ManualHistoryFileButton, async () =>
            {
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
                await OpenHistoryFileAsync();
            });
            AttachOverlayChoiceAction(AutoHistoryFileButton, async () =>
            {
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
                await OpenAutoHistoryFileAsync();
            });
            AttachOverlayChoiceAction(CancelHistoryOpenButton, () =>
            {
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
            });
            AttachPointerAction(ModeToggleButton, ToggleMoveModeAsync);

            TotalLogButton.MouseLeftButtonUp += async (_, _) => await LoadAllFirstPageAsync();

            AttachCategoryButton(FireLogButton, "fire");
            AttachCategoryButton(AlarmLogButton, "alarm");
            AttachCategoryButton(RelayErrorLogButton, "relay_fault");
            AttachCategoryButton(AnErrorLogButton, "an_fault");
            AttachCategoryButton(LineBreakLogButton, "line_fault");
            AttachCategoryButton(OutputLogButton, "output");
            AttachCategoryButton(MccLogButton, "mcc");
            AttachCategoryButton(EtcLogButton, "other");

            SearchButton.MouseLeftButtonUp += async (_, _) => await SearchFirstPageAsync();

            AttachPointerAction(SearchClearButton, ClearSearchKeywordAndReloadAllAsync);
            AttachPointerAction(Keyboard, RestartTouchKeyboardAsync);
            AttachPointerAction(FileSaveButton, SaveCurrentLogsToPrintHtmlAsync);

            PeriodSearchButton.MouseLeftButtonUp += async (_, _) => await LoadDateFirstPageAsync();
            AllPeriodButton.MouseLeftButtonUp += async (_, _) => await ApplyAllPeriodAsync();
            SevenDaysButton.MouseLeftButtonUp += async (_, _) => await ApplyRecentDaysAsync(7);
            ThirtyDaysButton.MouseLeftButtonUp += async (_, _) => await ApplyRecentDaysAsync(30);
            LogScrollViewer.PreviewMouseLeftButtonDown += LogScrollViewer_PreviewMouseLeftButtonDown;
            LogScrollViewer.PreviewMouseMove += LogScrollViewer_PreviewMouseMove;
            LogScrollViewer.PreviewMouseLeftButtonUp += LogScrollViewer_PreviewMouseLeftButtonUp;
            LogScrollViewer.MouseLeave += LogScrollViewer_MouseLeave;

            AttachPointerAction(StartDateButton, () => OpenDateCalendar(StartDateButton, StartDateText));
            AttachPointerAction(EndDateButton, () => OpenDateCalendar(EndDateButton, EndDateText));

            DateCalendar.SelectedDatesChanged += (_, _) =>
            {
                ApplySelectedDateFromCalendar();
            };

            AttachPointerAction(PageSizeButton, TogglePageSizeDropdown);
            AttachPageSizeButton(PageSize1000Button, 1000);
            AttachPageSizeButton(PageSize5000Button, 5000);
            AttachPageSizeButton(PageSize10000Button, 10000);
            AttachPageSizeButton(PageSize50000Button, 50000);
            AttachPageSizeButton(PageSize100000Button, 100000);

            PreviewMouseLeftButtonDown += GuardPointerInputPreview;
            PreviewMouseLeftButtonUp += GuardPointerInputPreview;
            PreviewTouchDown += GuardTouchInputPreview;
            PreviewTouchUp += GuardTouchInputPreview;

            PreviewMouseLeftButtonDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                CloseDropdownsWhenOutsideClicked(source);
            };

            TouchDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                CloseDropdownsWhenOutsideClicked(source);
            };

            SearchKeywordTextBox.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    await SearchFirstPageAsync();
                }
            };

            LogScrollViewer.ScrollChanged += LogScrollViewer_ScrollChanged;
        }

        private void AttachCategoryButton(UIElement button, string category)
        {
            AttachPointerAction(
                button,
                async () => await HandleCategoryButtonAsync(category),
                handleMouse: false
            );
        }

        private void AttachPageSizeButton(UIElement button, int size)
        {
            AttachPointerAction(button, async () => await ChangePageSizeAsync(size));
        }

        private void AttachOverlayChoiceAction(UIElement element, Action action)
        {
            element.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                action();
            };

            element.PreviewTouchDown += (_, e) =>
            {
                e.Handled = true;
                action();
            };
        }

        private void AttachOverlayChoiceAction(UIElement element, Func<Task> action)
        {
            element.PreviewMouseLeftButtonDown += async (_, e) =>
            {
                e.Handled = true;
                await action();
            };

            element.PreviewTouchDown += async (_, e) =>
            {
                e.Handled = true;
                await action();
            };
        }

        private void AttachPointerAction(
            UIElement element,
            Action action,
            bool handleMouse = true)
        {
            element.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = handleMouse;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                action();
            };

            element.TouchDown += (_, e) =>
            {
                e.Handled = true;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                action();
            };
        }

        private void AttachPointerAction(
            UIElement element,
            Func<Task> action,
            bool handleMouse = true)
        {
            element.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = handleMouse;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                await action();
            };

            element.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                await action();
            };
        }

        private bool ShouldIgnorePointerAction()
        {
            DateTime now = DateTime.Now;

            if (now <= _ignorePointerUntil)
            {
                return true;
            }

            if ((now - _lastPointerActionAt).TotalMilliseconds < PointerActionDebounceMilliseconds)
            {
                return true;
            }

            _lastPointerActionAt = now;
            return false;
        }

        private void BlockPointerInputAfterModal()
        {
            _ignorePointerUntil = DateTime.Now.AddMilliseconds(ModalCloseInputGuardMilliseconds);
        }

        private bool IsPointerInputBlocked()
        {
            return DateTime.Now <= _ignorePointerUntil;
        }

        private void GuardPointerInputPreview(object sender, MouseButtonEventArgs e)
        {
            if (IsPointerInputBlocked())
            {
                e.Handled = true;
            }
        }

        private void GuardTouchInputPreview(object? sender, TouchEventArgs e)
        {
            if (IsPointerInputBlocked())
            {
                e.Handled = true;
            }
        }

        private async Task RestartTouchKeyboardAsync()
        {
            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

            await Task.Run(() =>
            {
                foreach (Process process in Process.GetProcessesByName("TabTip"))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(1000);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            });

            await Task.Delay(250);

            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

            if (!TryStartTouchKeyboard())
            {
                MessageBox.Show(
                    "윈도우 터치 키보드를 실행할 수 없습니다.",
                    "키보드 복구",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private static bool TryStartTouchKeyboard()
        {
            string tabTipPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                "Microsoft Shared",
                "ink",
                "TabTip.exe"
            );

            string oskPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "osk.exe"
            );

            return TryStartProcess(tabTipPath) ||
                   TryStartProcess(oskPath);
        }

        private static bool TryStartProcess(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
