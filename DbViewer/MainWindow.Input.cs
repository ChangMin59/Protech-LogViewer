using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DbViewer
{
    public partial class MainWindow
    {
        private void InitializeEvents()
        {
            AttachPointerAction(HistoryViewButton, ShowHistoryOpenChoice);

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

            AttachPointerAction(TotalLogButton, LoadAllFirstPageAsync);

            AttachCategoryButton(FireLogButton, "fire");
            AttachCategoryButton(AlarmLogButton, "alarm");
            AttachCategoryButton(RelayErrorLogButton, "relay_fault");
            AttachCategoryButton(AnErrorLogButton, "an_fault");
            AttachCategoryButton(LineBreakLogButton, "line_fault");
            AttachCategoryButton(OutputLogButton, "output");
            AttachCategoryButton(MccLogButton, "mcc");
            AttachCategoryButton(EtcLogButton, "other");

            AttachPointerAction(SearchButton, SearchFirstPageAsync);

            AttachPointerAction(SearchClearButton, ClearSearchKeywordAndReloadAllAsync);
            AttachPointerAction(Keyboard, RestartTouchKeyboardAsync);
            AttachPointerAction(RecoverMoveButton, async () => await HandleCategoryButtonAsync("recover"));
            AttachPointerAction(TxtFileSaveButton, SaveCurrentLogsToTextFileAsync);
            AttachPointerAction(PrintButton, PrintCurrentLogsAsync);
            AttachPointerAction(FileSaveButton, SaveCurrentLogsToPrintHtmlAsync);

            AttachPointerAction(PeriodSearchButton, LoadDateFirstPageAsync);
            AttachPointerAction(AllPeriodButton, ApplyAllPeriodAsync);
            AttachPointerAction(SevenDaysButton, async () => await ApplyRecentDaysAsync(7));
            AttachPointerAction(ThirtyDaysButton, async () => await ApplyRecentDaysAsync(30));
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
            AttachPageSizeButton(PageSize25Button, 25);
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

                ReleaseSearchInputWhenOutsideClicked(source);
                CloseDropdownsWhenOutsideClicked(source);
            };

            PreviewTouchDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                ReleaseSearchInputWhenOutsideClicked(source);
                CloseDropdownsWhenOutsideClicked(source);
            };

            TouchDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                ReleaseSearchInputWhenOutsideClicked(source);
                CloseDropdownsWhenOutsideClicked(source);
            };

            SearchKeywordTextBox.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    await SearchFirstPageAsync();
                }
            };

            SearchKeywordTextBox.PreviewTouchDown += (_, _) =>
            {
                ShowTouchKeyboardForSearchBox();
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
            PreparePressVisual(element);

            element.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                PlayPressVisual(element);
                action();
            };

            element.PreviewTouchDown += (_, e) =>
            {
                e.Handled = true;
                PlayPressVisual(element);
                action();
            };
        }

        private void AttachOverlayChoiceAction(UIElement element, Func<Task> action)
        {
            PreparePressVisual(element);

            element.PreviewMouseLeftButtonDown += async (_, e) =>
            {
                e.Handled = true;
                PlayPressVisual(element);
                await action();
            };

            element.PreviewTouchDown += async (_, e) =>
            {
                e.Handled = true;
                PlayPressVisual(element);
                await action();
            };
        }

        private void AttachPointerAction(
            UIElement element,
            Action action,
            bool handleMouse = true)
        {
            PreparePressVisual(element);

            element.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = handleMouse;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                PlayPressVisual(element);
                action();
            };

            element.TouchDown += (_, e) =>
            {
                e.Handled = true;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                PlayPressVisual(element);
                action();
            };
        }

        private void AttachPointerAction(
            UIElement element,
            Func<Task> action,
            bool handleMouse = true)
        {
            PreparePressVisual(element);

            element.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = handleMouse;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                PlayPressVisual(element);
                await action();
            };

            element.TouchDown += async (_, e) =>
            {
                e.Handled = true;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                PlayPressVisual(element);
                await action();
            };
        }

        private static void PreparePressVisual(UIElement element)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            if (element.RenderTransform is ScaleTransform)
            {
                return;
            }

            if (element.RenderTransform == null || element.RenderTransform == Transform.Identity)
            {
                element.RenderTransform = new ScaleTransform(1.0, 1.0);
            }
        }

        private static void PlayPressVisual(UIElement element)
        {
            if (element.RenderTransform is not ScaleTransform scale)
            {
                return;
            }

            scale.ScaleX = 0.97;
            scale.ScaleY = 0.97;
            element.Opacity = 0.82;

            _ = element.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(90);

                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
                element.Opacity = 1.0;
            });
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

        private void ReleaseSearchInputWhenOutsideClicked(DependencyObject? source)
        {
            if (source == null)
            {
                return;
            }

            if (IsDescendantOf(source, SearchKeywordTextBox))
            {
                return;
            }

            if (!SearchKeywordTextBox.IsKeyboardFocusWithin && !SearchKeywordTextBox.IsFocused)
            {
                return;
            }

            ReleaseSearchInputFocus();
            _ = CloseTouchKeyboardAsync();
        }

        private void ReleaseSearchInputFocus()
        {
            RootGrid.Focus();
            System.Windows.Input.Keyboard.ClearFocus();
            RootGrid.Focus();
        }

        private async Task RestartTouchKeyboardAsync()
        {
            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

            await Task.Run(() =>
            {
                CloseTouchKeyboardProcesses();
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

                return;
            }

            StartTouchKeyboardCloseMonitor();
        }

        private void ShowTouchKeyboardForSearchBox()
        {
            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(80);

                SearchKeywordTextBox.Focus();
                SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

                if (TryStartTouchKeyboard())
                {
                    StartTouchKeyboardCloseMonitor();
                }
            });
        }

        private void StartTouchKeyboardCloseMonitor()
        {
            _touchKeyboardMonitorCts?.Cancel();
            _touchKeyboardMonitorCts = new CancellationTokenSource();

            CancellationToken token = _touchKeyboardMonitorCts.Token;

            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    bool keyboardWasVisible = false;
                    int hiddenChecks = 0;

                    for (int i = 0; i < 80; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        await Task.Delay(150, token);

                        if (!SearchKeywordTextBox.IsKeyboardFocusWithin && !SearchKeywordTextBox.IsFocused)
                        {
                            return;
                        }

                        bool keyboardVisible = IsTouchKeyboardVisible();

                        if (keyboardVisible)
                        {
                            keyboardWasVisible = true;
                            hiddenChecks = 0;
                            continue;
                        }

                        if (!keyboardWasVisible)
                        {
                            continue;
                        }

                        hiddenChecks++;

                        if (hiddenChecks < 3)
                        {
                            continue;
                        }

                        ReleaseSearchInputFocus();
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
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

        private static async Task CloseTouchKeyboardAsync()
        {
            await Task.Run(() =>
            {
                CloseTouchKeyboardProcesses();
            });
        }

        private static void CloseTouchKeyboardProcesses()
        {
            CloseProcessesByName("TabTip");
            CloseProcessesByName("TextInputHost");
            CloseProcessesByName("InputApp");
            CloseProcessesByName("osk");
        }

        private static bool IsTouchKeyboardVisible()
        {
            bool visible = false;

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                GetWindowThreadProcessId(hWnd, out uint processId);

                if (processId == 0)
                {
                    return true;
                }

                try
                {
                    using Process process = Process.GetProcessById((int)processId);
                    string processName = process.ProcessName;

                    if (processName.Equals("TabTip", StringComparison.OrdinalIgnoreCase) ||
                        processName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                        processName.Equals("InputApp", StringComparison.OrdinalIgnoreCase) ||
                        processName.Equals("osk", StringComparison.OrdinalIgnoreCase))
                    {
                        visible = true;
                        return false;
                    }
                }
                catch
                {
                }

                return true;
            }, IntPtr.Zero);

            return visible;
        }

        private static void CloseProcessesByName(string processName)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!process.CloseMainWindow())
                    {
                        process.Kill(entireProcessTree: true);
                    }

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

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
