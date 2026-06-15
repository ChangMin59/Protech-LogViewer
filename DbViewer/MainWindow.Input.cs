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
        // XAML에 있는 버튼/입력/스크롤 이벤트를 실제 기능 함수에 연결한다.
        private void InitializeEvents()
        {
            // 이력 보기 버튼은 자동 탐색/파일 선택 선택창을 먼저 보여준다.
            AttachPointerAction(HistoryViewButton, ShowHistoryOpenChoice);

            // 선택창의 "파일 선택"은 사용자가 .db/.txt를 직접 고르는 흐름이다.
            AttachOverlayChoiceAction(ManualHistoryFileButton, async () =>
            {
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
                await OpenHistoryFileAsync();
            });
            // 선택창의 "자동 탐색"은 C:\Windows\Log.db를 찾는 흐름이다.
            AttachOverlayChoiceAction(AutoHistoryFileButton, async () =>
            {
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
                await OpenAutoHistoryFileAsync();
            });
            // 선택창 취소는 overlay만 닫고 기존 로그 화면은 유지한다.
            AttachOverlayChoiceAction(CancelHistoryOpenButton, () =>
            {
                HistoryOpenChoiceOverlay.Visibility = Visibility.Collapsed;
            });
            // 이동모드는 카테고리 버튼을 필터가 아니라 다음 로그 찾기로 바꾼다.
            AttachPointerAction(ModeToggleButton, ToggleMoveModeAsync);

            // 전체로그 버튼은 현재 기간/검색/카테고리 상태에 따라 전체 목록을 복원한다.
            AttachPointerAction(TotalLogButton, LoadTotalLogButtonAsync);

            // 실제 카테고리 버튼과 LogClassifier 카테고리 키를 연결한다.
            AttachCategoryButton(FireLogButton, "fire");
            AttachCategoryButton(AlarmLogButton, "alarm");
            AttachCategoryButton(RelayErrorLogButton, "relay_fault");
            AttachCategoryButton(AnErrorLogButton, "an_fault");
            AttachCategoryButton(LineBreakLogButton, "line_fault");
            AttachCategoryButton(OutputLogButton, "output");
            AttachCategoryButton(MccLogButton, "mcc");
            AttachCategoryButton(EtcLogButton, "other");

            // 검색 버튼은 Type/Action/Section/Contents/Packet 검색 첫 페이지를 불러온다.
            AttachPointerAction(SearchButton, SearchFirstPageAsync);

            // 검색 초기화, 키보드 복구, 복구 로그 이동, 저장/인쇄 버튼 연결이다.
            AttachPointerAction(SearchClearButton, ClearSearchKeywordAndReloadAllAsync);
            AttachPointerAction(Keyboard, RestartTouchKeyboardAsync);
            AttachPointerAction(RecoverMoveButton, async () => await HandleCategoryButtonAsync("recover"));
            AttachPointerAction(TxtFileSaveButton, SaveCurrentLogsToTextFileAsync);
            AttachPointerAction(PrintButton, PrintCurrentLogsAsync);
            AttachPointerAction(FileSaveButton, SaveCurrentLogsToPrintHtmlAsync);

            // 기간 프리셋 버튼 연결이다. 날짜 선택 자체도 즉시 조회를 수행한다.
            AttachPointerAction(AllPeriodButton, ApplyAllPeriodAsync);
            AttachPointerAction(OneDayButton, async () => await ApplyRecentDaysAsync(1));
            AttachPointerAction(SevenDaysButton, async () => await ApplyRecentDaysAsync(7));
            AttachPointerAction(ThirtyDaysButton, async () => await ApplyRecentDaysAsync(30));
            // 로그 표의 직접 드래그 스크롤 이벤트다.
            LogScrollViewer.PreviewMouseLeftButtonDown += LogScrollViewer_PreviewMouseLeftButtonDown;
            LogScrollViewer.PreviewMouseMove += LogScrollViewer_PreviewMouseMove;
            LogScrollViewer.PreviewMouseLeftButtonUp += LogScrollViewer_PreviewMouseLeftButtonUp;
            LogScrollViewer.MouseLeave += LogScrollViewer_MouseLeave;

            // 시작일/종료일 버튼은 같은 달력 컨트롤을 대상만 바꿔서 연다.
            AttachPointerAction(StartDateButton, () => OpenDateCalendar(StartDateButton, StartDateText));
            AttachPointerAction(EndDateButton, () => OpenDateCalendar(EndDateButton, EndDateText));

            // 달력 날짜를 선택하면 바로 기간 조회가 실행된다.
            DateCalendar.SelectedDatesChanged += (_, _) =>
            {
                ApplySelectedDateFromCalendar();
            };

            // 페이지 크기 드롭다운과 선택 가능한 건수 버튼이다.
            AttachPointerAction(PageSizeButton, TogglePageSizeDropdown);
            AttachPageSizeButton(PageSize25Button, 25);
            AttachPageSizeButton(PageSize1000Button, 1000);
            AttachPageSizeButton(PageSize5000Button, 5000);
            AttachPageSizeButton(PageSize10000Button, 10000);
            AttachPageSizeButton(PageSize50000Button, 50000);
            AttachPageSizeButton(PageSize100000Button, 100000);

            // 모달/파일창 닫힘 직후 남은 터치/마우스 이벤트가 버튼을 누르지 못하게 막는다.
            PreviewMouseLeftButtonDown += GuardPointerInputPreview;
            PreviewMouseLeftButtonUp += GuardPointerInputPreview;
            PreviewTouchDown += GuardTouchInputPreview;
            PreviewTouchUp += GuardTouchInputPreview;

            // 마우스로 검색창 바깥을 누르면 검색창 포커스와 터치 키보드를 해제한다.
            PreviewMouseLeftButtonDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                ReleaseSearchInputWhenOutsideClicked(source);
                CloseDropdownsWhenOutsideClicked(source);
            };

            // 터치도 마우스와 동일하게 검색창 바깥 클릭 처리를 한다.
            PreviewTouchDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                ReleaseSearchInputWhenOutsideClicked(source);
                CloseDropdownsWhenOutsideClicked(source);
            };

            // 일부 터치 장비에서 PreviewTouchDown이 누락될 때를 대비한 일반 TouchDown 처리다.
            TouchDown += (_, e) =>
            {
                DependencyObject? source = e.OriginalSource as DependencyObject;

                ReleaseSearchInputWhenOutsideClicked(source);
                CloseDropdownsWhenOutsideClicked(source);
            };

            // 검색창에서 Enter를 누르면 검색 버튼과 같은 동작을 한다.
            SearchKeywordTextBox.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    await SearchFirstPageAsync();
                }
            };

            // 검색창을 터치하면 Windows 터치 키보드를 띄운다.
            SearchKeywordTextBox.PreviewTouchDown += (_, _) =>
            {
                ShowTouchKeyboardForSearchBox();
            };

            // 로그 스크롤 동기화와 가상 렌더링 갱신 이벤트다.
            LogScrollViewer.ScrollChanged += LogScrollViewer_ScrollChanged;
        }

        // 카테고리 버튼을 category 키와 연결한다.
        // 예: FireLogButton -> fire, RelayErrorLogButton -> relay_fault.
        private void AttachCategoryButton(UIElement button, string category)
        {
            AttachPointerAction(
                button,
                async () => await HandleCategoryButtonAsync(category),
                handleMouse: false
            );
        }

        // 페이지 크기 버튼을 실제 pageSize 값과 연결한다.
        private void AttachPageSizeButton(UIElement button, int size)
        {
            AttachPointerAction(button, async () => await ChangePageSizeAsync(size));
        }

        // overlay 내부 선택 버튼용 동기 액션 연결이다.
        // overlay는 클릭 즉시 눌림 효과와 액션이 실행되어야 한다.
        private void AttachOverlayChoiceAction(UIElement element, Action action)
        {
            PreparePressVisual(element);

            element.PreviewMouseLeftButtonDown += (_, e) =>
            {
                // overlay 아래 버튼으로 이벤트가 내려가지 않게 막는다.
                e.Handled = true;
                PlayPressVisual(element);
                action();
            };

            element.PreviewTouchDown += (_, e) =>
            {
                // 터치도 같은 방식으로 즉시 처리한다.
                e.Handled = true;
                PlayPressVisual(element);
                action();
            };
        }

        // overlay 내부 선택 버튼용 비동기 액션 연결이다.
        private void AttachOverlayChoiceAction(UIElement element, Func<Task> action)
        {
            PreparePressVisual(element);

            element.PreviewMouseLeftButtonDown += async (_, e) =>
            {
                // 파일 열기 같은 비동기 동작 전에 overlay 이벤트를 여기서 소비한다.
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

        // 일반 버튼에 마우스/터치 동기 액션을 연결한다.
        private void AttachPointerAction(
            UIElement element,
            Action action,
            bool handleMouse = true)
        {
            PreparePressVisual(element);

            element.MouseLeftButtonUp += (_, e) =>
            {
                // handleMouse=false인 카테고리 버튼은 상위 처리와 충돌하지 않게 한다.
                e.Handled = handleMouse;
                if (ShouldIgnorePointerAction())
                {
                    // 파일창/모달 직후 또는 중복 터치로 들어온 입력은 무시한다.
                    return;
                }

                PlayPressVisual(element);
                action();
            };

            element.TouchDown += (_, e) =>
            {
                // 터치 입력은 중복 마우스 이벤트로 이어지지 않게 처리한다.
                e.Handled = true;
                if (ShouldIgnorePointerAction())
                {
                    return;
                }

                PlayPressVisual(element);
                action();
            };
        }

        // 일반 버튼에 마우스/터치 비동기 액션을 연결한다.
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
                    // 짧은 시간 안에 같은 버튼이 두 번 실행되는 것을 막는다.
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

        // 버튼 눌림 애니메이션을 위해 ScaleTransform 기본값을 준비한다.
        private static void PreparePressVisual(UIElement element)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            if (element.RenderTransform is ScaleTransform)
            {
                // 이미 준비된 버튼은 다시 덮어쓰지 않는다.
                return;
            }

            if (element.RenderTransform == null || element.RenderTransform == Transform.Identity)
            {
                // 중심 기준 1.0 배율에서 시작한다.
                element.RenderTransform = new ScaleTransform(1.0, 1.0);
            }
        }

        // 버튼을 누른 것처럼 잠깐 작아지고 투명해지는 효과를 준다.
        private static void PlayPressVisual(UIElement element)
        {
            if (element.RenderTransform is not ScaleTransform scale)
            {
                // ScaleTransform이 없는 요소는 눌림 효과를 생략한다.
                return;
            }

            scale.ScaleX = 0.97;
            scale.ScaleY = 0.97;
            element.Opacity = 0.82;

            _ = element.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(90);

                // 90ms 뒤 원래 크기/불투명도로 돌린다.
                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
                element.Opacity = 1.0;
            });
        }

        // 파일창 닫힘 직후 입력과 빠른 중복 클릭을 걸러낸다.
        private bool ShouldIgnorePointerAction()
        {
            DateTime now = DateTime.Now;

            if (now <= _ignorePointerUntil)
            {
                // 모달이 닫힌 직후 남은 터치 이벤트다.
                return true;
            }

            if ((now - _lastPointerActionAt).TotalMilliseconds < PointerActionDebounceMilliseconds)
            {
                // 같은 손가락 입력에서 마우스/터치 이벤트가 연속으로 들어온 경우다.
                return true;
            }

            _lastPointerActionAt = now;
            return false;
        }

        // 파일창/메시지박스 닫힘 직후 짧은 시간 입력을 막는다.
        private void BlockPointerInputAfterModal()
        {
            _ignorePointerUntil = DateTime.Now.AddMilliseconds(ModalCloseInputGuardMilliseconds);
        }

        // 현재 입력 차단 시간 안인지 확인한다.
        private bool IsPointerInputBlocked()
        {
            return DateTime.Now <= _ignorePointerUntil;
        }

        // 마우스 preview 단계에서 모달 직후 입력을 막는다.
        private void GuardPointerInputPreview(object sender, MouseButtonEventArgs e)
        {
            if (IsPointerInputBlocked())
            {
                e.Handled = true;
            }
        }

        // 터치 preview 단계에서 모달 직후 입력을 막는다.
        private void GuardTouchInputPreview(object? sender, TouchEventArgs e)
        {
            if (IsPointerInputBlocked())
            {
                e.Handled = true;
            }
        }

        // 검색창 바깥을 누르면 검색창 커서와 Windows 터치 키보드를 정리한다.
        private void ReleaseSearchInputWhenOutsideClicked(DependencyObject? source)
        {
            if (source == null)
            {
                return;
            }

            if (IsDescendantOf(source, SearchKeywordTextBox))
            {
                // 검색창 내부 클릭은 포커스를 유지한다.
                return;
            }

            if (!SearchKeywordTextBox.IsKeyboardFocusWithin && !SearchKeywordTextBox.IsFocused)
            {
                // 이미 포커스가 없으면 처리할 것이 없다.
                return;
            }

            // 검색창 커서를 없애고 터치 키보드를 닫는다.
            ReleaseSearchInputFocus();
            _ = CloseTouchKeyboardAsync();
        }

        // 검색창 포커스를 RootGrid로 넘겨 커서가 남아 있지 않게 한다.
        private void ReleaseSearchInputFocus()
        {
            RootGrid.Focus();
            System.Windows.Input.Keyboard.ClearFocus();
            RootGrid.Focus();
        }

        // 키보드 복구 버튼을 눌렀을 때 Windows 터치 키보드를 강제로 다시 띄운다.
        private async Task RestartTouchKeyboardAsync()
        {
            // 키보드를 띄울 대상은 검색창이므로 먼저 포커스를 준다.
            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

            // 기존 터치 키보드 프로세스를 닫는다.
            await Task.Run(() =>
            {
                CloseTouchKeyboardProcesses();
            });

            await Task.Delay(250);

            // 닫힌 뒤 다시 검색창 포커스를 회복한다.
            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

            if (!TryStartTouchKeyboard())
            {
                // TabTip/osk 둘 다 실행하지 못하면 사용자에게 알린다.
                MessageBox.Show(
                    "윈도우 터치 키보드를 실행할 수 없습니다.",
                    "키보드 복구",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            // 사용자가 키보드 닫기 버튼을 누르면 검색창 커서도 사라지게 감시한다.
            StartTouchKeyboardCloseMonitor();
        }

        // 검색창을 터치했을 때 Windows 터치 키보드를 띄운다.
        private void ShowTouchKeyboardForSearchBox()
        {
            SearchKeywordTextBox.Focus();
            SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

            _ = Dispatcher.InvokeAsync(async () =>
            {
                // WPF 포커스 적용 후 키보드를 띄우기 위해 잠깐 기다린다.
                await Task.Delay(80);

                SearchKeywordTextBox.Focus();
                SearchKeywordTextBox.CaretIndex = SearchKeywordTextBox.Text.Length;

                if (TryStartTouchKeyboard())
                {
                    // 터치 키보드가 닫히면 검색창 포커스도 해제한다.
                    StartTouchKeyboardCloseMonitor();
                }
            });
        }

        // Windows 터치 키보드 창이 닫혔는지 감시하고 닫히면 검색창 포커스를 해제한다.
        private void StartTouchKeyboardCloseMonitor()
        {
            // 이전 감시 루프가 있으면 취소한다.
            _touchKeyboardMonitorCts?.Cancel();
            _touchKeyboardMonitorCts = new CancellationTokenSource();

            CancellationToken token = _touchKeyboardMonitorCts.Token;

            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // 한 번이라도 키보드가 보인 뒤 사라졌을 때만 닫힘으로 인정한다.
                    bool keyboardWasVisible = false;
                    int hiddenChecks = 0;

                    for (int i = 0; i < 80; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            // 새 감시 루프가 시작되었거나 창이 닫히는 중이다.
                            return;
                        }

                        await Task.Delay(150, token);

                        if (!SearchKeywordTextBox.IsKeyboardFocusWithin && !SearchKeywordTextBox.IsFocused)
                        {
                            // 검색창 포커스가 이미 없으면 감시를 끝낸다.
                            return;
                        }

                        // 현재 TabTip/TextInputHost/osk 창이 보이는지 확인한다.
                        bool keyboardVisible = IsTouchKeyboardVisible();

                        if (keyboardVisible)
                        {
                            keyboardWasVisible = true;
                            hiddenChecks = 0;
                            continue;
                        }

                        if (!keyboardWasVisible)
                        {
                            // 아직 키보드가 뜬 적이 없으면 닫힘으로 판단하지 않는다.
                            continue;
                        }

                        hiddenChecks++;

                        if (hiddenChecks < 3)
                        {
                            // 순간적으로 숨김 판정이 한두 번 나오는 흔들림을 걸러낸다.
                            continue;
                        }

                        // 키보드 닫기 버튼을 누른 것으로 보고 검색창 커서를 제거한다.
                        ReleaseSearchInputFocus();
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    // 감시 취소는 정상 흐름이다.
                }
            });
        }

        // Windows 터치 키보드 실행 파일을 찾아 실행한다.
        private static bool TryStartTouchKeyboard()
        {
            // Windows 태블릿 키보드.
            string tabTipPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                "Microsoft Shared",
                "ink",
                "TabTip.exe"
            );

            // 접근성 온스크린 키보드 fallback.
            string oskPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "osk.exe"
            );

            return TryStartProcess(tabTipPath) ||
                   TryStartProcess(oskPath);
        }

        // Windows 터치 키보드 관련 프로세스를 백그라운드에서 닫는다.
        private static async Task CloseTouchKeyboardAsync()
        {
            await Task.Run(() =>
            {
                CloseTouchKeyboardProcesses();
            });
        }

        // 실제 터치 키보드 관련 프로세스들을 닫는다.
        private static void CloseTouchKeyboardProcesses()
        {
            // Windows 버전별로 키보드 프로세스 이름이 다를 수 있어 함께 정리한다.
            CloseProcessesByName("TabTip");
            CloseProcessesByName("TextInputHost");
            CloseProcessesByName("InputApp");
            CloseProcessesByName("osk");
        }

        // 현재 Windows 터치 키보드 창이 화면에 보이는지 확인한다.
        private static bool IsTouchKeyboardVisible()
        {
            bool visible = false;

            // 모든 최상위 창을 돌면서 키보드 프로세스의 보이는 창을 찾는다.
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    // 숨겨진 창은 키보드 표시로 보지 않는다.
                    return true;
                }

                GetWindowThreadProcessId(hWnd, out uint processId);

                if (processId == 0)
                {
                    // 프로세스 id가 없으면 계속 검색한다.
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
                        // 키보드 프로세스의 보이는 창을 찾았으므로 검색을 중단한다.
                        visible = true;
                        return false;
                    }
                }
                catch
                {
                    // 권한/종료 타이밍으로 프로세스를 못 읽으면 무시하고 계속 찾는다.
                }

                return true;
            }, IntPtr.Zero);

            return visible;
        }

        // 지정 이름의 프로세스를 닫는다.
        private static void CloseProcessesByName(string processName)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!process.CloseMainWindow())
                    {
                        // 정상 닫기 메시지가 실패하면 강제 종료한다.
                        process.Kill(entireProcessTree: true);
                    }

                    process.WaitForExit(1000);
                }
                catch
                {
                    // 이미 종료되었거나 권한 문제인 경우는 무시한다.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        // 지정 경로의 exe를 실행한다.
        private static bool TryStartProcess(string path)
        {
            if (!File.Exists(path))
            {
                // 해당 Windows 키보드 실행 파일이 없는 환경이다.
                return false;
            }

            try
            {
                // UseShellExecute=true로 Windows가 직접 키보드 프로그램을 실행하게 한다.
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });

                return true;
            }
            catch
            {
                // 실행 권한/정책 문제 등으로 실패하면 다른 fallback을 시도한다.
                return false;
            }
        }

        // EnumWindows 콜백 함수 형식이다.
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // Windows 최상위 창을 열거한다.
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        // 창이 화면에 보이는 상태인지 확인한다.
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // 창 핸들에서 소유 프로세스 id를 가져온다.
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
