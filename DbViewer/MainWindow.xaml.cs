using DbViewer.Models;
using DbViewer.Services.Common;
using DbViewer.Services.Export;
using DbViewer.Services.Render;
using DbViewer.Services.View;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DbViewer
{
    // 메인 화면 로직이다.
    // 실제 로그 흐름: 이력 DB/TXT 열기 -> LogRow 페이지 로딩 -> 화면 렌더링 -> 필터/이동/저장/인쇄.
    public partial class MainWindow : Window
    {
        // 현재 열린 이력 DB 저장소다. null이면 아직 이력 파일이 열리지 않은 상태다.
        private LogRepository? _repository;

        // 현재 화면 페이지에 표시 중인 실제 로그 목록이다.
        // 예: pageSize=25이면 최신순 25개 LogRow가 들어간다.
        private List<LogRow> _currentRows = new();

        // 현재 조회 모드다. all/search/date/date_cache/category 중 하나로 흐름을 구분한다.
        private string _currentMode = "all";
        // 검색 모드에서 실제로 사용 중인 검색어다.
        private string _currentKeyword = "";
        // 기간 모드 시작일이다. 예: 2026-06-01.
        private string _currentStartDate = "";
        // 기간 모드 종료일이다. 예: 2026-06-07.
        private string _currentEndDate = "";

        // 현재 페이지 번호다. 화면 기준 1부터 시작한다.
        private int _currentPage = 1;
        // 한 페이지에 표시할 로그 수다. 기본은 1080x1920 기준 25개다.
        private int _pageSize = 25;
        // 현재 조회 조건의 전체 로그 건수다.
        private int _totalCount = 0;
        // 현재 조회 조건과 pageSize 기준 전체 페이지 수다.
        private int _totalPages = 1;

        // 렌더링/카운트/페이지 로딩/터치 키보드 감시 작업 취소용 토큰이다.
        private CancellationTokenSource? _renderCts;
        private CancellationTokenSource? _countCts;
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _touchKeyboardMonitorCts;
        // 로그 표를 손가락/마우스로 직접 끌어 스크롤 중인지 표시한다.
        private bool _isLogContentDragging = false;
        private Point _dragStartPoint;
        private double _dragStartHorizontalOffset;
        private double _dragStartVerticalOffset;

        // 카테고리 캐시는 백그라운드 계산과 UI 필터가 함께 접근하므로 lock으로 보호한다.
        private readonly object _categoryCacheLock = new();

        // 현재 선택된 카테고리 키 목록이다. 예: fire, relay_fault.
        private readonly HashSet<string> _selectedCategories = new();

        // 현재 조회 범위에서 카테고리별 실제 LogRow를 모아 둔 캐시다.
        private readonly Dictionary<string, List<LogRow>> _categoryCache = new()
        {
            ["fire"] = new List<LogRow>(),
            ["alarm"] = new List<LogRow>(),
            ["relay_fault"] = new List<LogRow>(),
            ["an_fault"] = new List<LogRow>(),
            ["line_fault"] = new List<LogRow>(),
            ["output"] = new List<LogRow>(),
            ["mcc"] = new List<LogRow>(),
            ["other"] = new List<LogRow>()
        };

        // 카테고리 캐시가 완성됐는지/계산 중인지 나타낸다.
        private bool _categoryCacheReady = false;
        private bool _categoryCacheBuilding = false;

        // 날짜 달력 드롭다운이 바꿀 대상 텍스트/버튼이다.
        private TextBlock? _dateTargetText;
        private Border? _dateTargetButton;
        private DateTime? _dbStartDate;
        private DateTime? _dbEndDate;
        // 코드로 달력 날짜를 설정하는 동안 SelectionChanged가 조회를 실행하지 않게 막는다.
        private bool _suppressDateCalendarChange = false;
        // 달력 닫힘 직후 카테고리 버튼 오동작 방지 시간이다.
        private DateTime _ignoreCategoryClickUntil = DateTime.MinValue;

        // 기간 로그 캐시와 카테고리 캐시는 비동기 작업과 UI가 함께 접근하므로 lock으로 보호한다.
        private readonly object _queryCacheLock = new();

        // 기간별 실제 LogRow 전체 캐시다. 예: date:2026-06-01~2026-06-07.
        private readonly Dictionary<string, List<LogRow>> _rowsCacheByKey = new();
        // 같은 기간별 카테고리 캐시다.
        private readonly Dictionary<string, Dictionary<string, List<LogRow>>> _categoryCacheByKey = new();
        // 기간별 총 건수 캐시다.
        private readonly Dictionary<string, int> _totalCountCacheByKey = new();

        // 현재 화면이 사용하는 기간 캐시 키다. 전체 로그는 all.
        private string _activeRowsCacheKey = "all";

        // 카테고리 필터 진입 전 기본 조회 상태다.
        // 카테고리를 해제하거나 이동모드에서 전체로 돌아갈 때 이 값으로 복원한다.
        private string _baseModeBeforeCategory = "all";
        private string _baseCacheKeyBeforeCategory = "all";
        private string _baseKeywordBeforeCategory = "";
        private string _baseStartDateBeforeCategory = "";
        private string _baseEndDateBeforeCategory = "";

        // 오래된 백그라운드 카운트 작업이 최신 UI를 덮어쓰지 못하게 하는 버전값이다.
        private int _countJobVersion = 0;
        // 이동모드 여부다. true면 카테고리 버튼이 필터가 아니라 다음 로그 이동으로 동작한다.
        private bool _isMoveMode = false;
        // 이동모드 탐색 중 중복 클릭을 막는 플래그다.
        private bool _isMoveNavigationRunning = false;
        // 파일 열기 중 중복 파일창을 막는 플래그다.
        private bool _isOpeningHistory = false;
        // 파일 저장/인쇄 준비 중 중복 작업을 막는 플래그다.
        private bool _isExportRunning = false;
        // 물리 높이 1900px 초과 화면에서 창 위치/크기를 고정할지 여부다.
        private bool _lockLargeScreenWindowSize = false;
        private bool _largeScreenFixedBoundsReady = false;
        private HwndSource? _largeScreenHwndSource;
        private int _largeScreenDeviceLeft = 0;
        private int _largeScreenDeviceTop = 0;
        private int _largeScreenDeviceWidth = 0;
        private int _largeScreenDeviceHeight = 0;
        // 이동모드에서 빨간 밑줄이 표시된 현재 페이지 내 row index다.
        private int? _highlightedRowIndexInPage = null;
        // 현재 로그 표가 가상 렌더링 중인지 여부다.
        private bool _isVirtualRenderingActive = false;
        // 현재 실제로 UI에 생성된 가상 행 index 범위다.
        private int _virtualStartIndex = -1;
        private int _virtualEndIndexExclusive = -1;
        // 빠른 중복 클릭과 모달 닫힘 직후 입력을 막기 위한 시간값이다.
        private DateTime _lastPointerActionAt = DateTime.MinValue;
        private DateTime _ignorePointerUntil = DateTime.MinValue;

        // 실제 로그 행 높이다. 가상 스크롤 위치 계산과 25개 표시 기준에 사용한다.
        private const double LogRowVisualHeight = 50.0;
        // 화면 위/아래로 미리 렌더링해 스크롤할 때 빈 구간이 보이지 않게 하는 여유 행 수다.
        private const int VirtualizationBufferRows = 18;
        private const string MoveTargetLineTag = "MoveTargetLine";
        private const int PointerActionDebounceMilliseconds = 450;
        private const int ModalCloseInputGuardMilliseconds = 500;
        private const int LargeScreenPhysicalHeightThreshold = 1900;
        private const double LargeScreenLeftBorderOffset = 10.0;
        private const double LargeScreenRightBorderOffset = 8.0;
        private const double LargeScreenBottomOffset = -10.0;
        private const int WmWindowPositionChanging = 0x0046;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;

        // 내부 카테고리 키를 사용자에게 보이는 이름으로 바꾸는 표다.
        private readonly Dictionary<string, string> _categoryDisplayNames = new()
        {
            ["fire"] = "화재",
            ["alarm"] = "제경보",
            ["relay_fault"] = "중계기 고장",
            ["an_fault"] = "AN 고장",
            ["line_fault"] = "단선",
            ["output"] = "출력",
            ["mcc"] = "MCC",
            ["recover"] = "시스템 복구",
            ["other"] = "기타"
        };

        // 기간 캐시 하나에 저장되는 실제 로그 목록과 카테고리 캐시 묶음이다.
        private sealed class QueryCacheEntry
        {
            public List<LogRow> Rows { get; init; } = new();
            public Dictionary<string, List<LogRow>> CategoryCache { get; init; } = new();
            public int TotalCount => Rows.Count;
        }
        // 이동모드에서 찾은 다음 대상 로그 위치다.
        private sealed class MoveTargetResult
        {
            // 실제 DB Log.ID.
            public long RowId { get; init; }
            // 이동해야 할 페이지 번호. 1부터 시작한다.
            public int PageNumber { get; init; }
            // 해당 페이지 안의 행 index. 0부터 시작한다.
            public int RowIndexInPage { get; init; }
        }
        // 메인 창을 초기화하고 이벤트/기본 화면/대형 화면 고정을 설정한다.
        public MainWindow()
        {
            InitializeComponent();

            // 검색창 포커스 해제용으로 RootGrid가 포커스를 받을 수 있게 한다.
            RootGrid.Focusable = true;
            // 둥근 테두리 바깥으로 로그 행이 넘치지 않게 크기 변경 때 Clip을 갱신한다.
            LogTableClipGrid.SizeChanged += (_, _) => UpdateLogTableRoundedClip();

            SourceInitialized += (_, _) =>
            {
                // Win32 메시지 hook을 연결해 대형 화면에서 창 이동/크기 변경을 막는다.
                _largeScreenHwndSource = PresentationSource.FromVisual(this) as HwndSource;
                _largeScreenHwndSource?.AddHook(LargeScreenWindowProc);

                if (IsLargePhysicalScreen())
                {
                    // 물리 높이 1900px 초과면 해당 해상도에 맞춘 고정 창 크기를 적용한다.
                    _lockLargeScreenWindowSize = true;
                    ApplyInitialWindowSizeForLargeScreen();
                    return;
                }
            };

            Closed += (_, _) =>
            {
                // 창 종료 시 Win32 hook 참조를 정리한다.
                _largeScreenHwndSource?.RemoveHook(LargeScreenWindowProc);
                _largeScreenHwndSource = null;
            };

            // 대형 화면 모드에서는 최소화/최대화 상태로 바뀌어도 Normal로 되돌린다.
            StateChanged += (_, _) => KeepLargeScreenWindowNormal();

            // 모든 버튼/터치/스크롤 이벤트를 연결한다.
            InitializeEvents();
            // 카운트/페이지/빈 로그 안내 등 초기 화면 텍스트를 세팅한다.
            InitializeDefaultText();
        }

        // 로그 표 내부 Grid를 바깥 둥근 Border 모양에 맞춰 잘라낸다.
        private void UpdateLogTableRoundedClip()
        {
            if (LogTableClipGrid.ActualWidth <= 0 || LogTableClipGrid.ActualHeight <= 0)
            {
                // 아직 레이아웃 크기가 계산되지 않았으면 Clip을 만들 수 없다.
                return;
            }

            LogTableClipGrid.Clip = new RectangleGeometry(
                new Rect(0, 0, LogTableClipGrid.ActualWidth, LogTableClipGrid.ActualHeight),
                LogTableContainer.CornerRadius.TopLeft,
                LogTableContainer.CornerRadius.TopLeft);
        }

        // 물리 높이 1900px 초과 화면에서 창을 화면 전체에 맞춰 고정 크기로 설정한다.
        private void ApplyInitialWindowSizeForLargeScreen()
        {
            if (!IsLargePhysicalScreen())
            {
                // 일반 해상도에서는 기존 WPF 크기 설정을 사용한다.
                return;
            }

            // 현재 커서가 있는 모니터를 기준으로 창 위치/크기를 계산한다.
            Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            Drawing.Rectangle fullArea = screen.Bounds;
            Drawing.Rectangle workArea = screen.WorkingArea;

            if (fullArea.Width <= 0 || fullArea.Height <= 0)
            {
                // 비정상 모니터 정보면 적용하지 않는다.
                return;
            }

            // 창 상태를 Normal/NoResize로 고정한다.
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;

            // Win32 픽셀 좌표를 WPF DIP 좌표로 변환한다.
            Matrix transformFromDevice = PresentationSource.FromVisual(this)
                ?.CompositionTarget
                ?.TransformFromDevice
                ?? Matrix.Identity;

            Point fullTopLeft = transformFromDevice.Transform(new Point(fullArea.Left, fullArea.Top));
            Point fullBottomRight = transformFromDevice.Transform(new Point(fullArea.Right, fullArea.Bottom));
            Point workBottomRight = transformFromDevice.Transform(new Point(workArea.Right, workArea.Bottom));

            // 현장 화면 여백 보정을 적용해 좌우/아래가 어긋나지 않게 맞춘다.
            double fullWidth = fullBottomRight.X - fullTopLeft.X;
            double availableHeight = workBottomRight.Y - fullTopLeft.Y - LargeScreenBottomOffset;
            double fixedWidth = fullWidth + LargeScreenLeftBorderOffset + LargeScreenRightBorderOffset;
            double fixedHeight = Math.Min(fullBottomRight.Y - fullTopLeft.Y, availableHeight);
            double fixedLeft = fullTopLeft.X - LargeScreenLeftBorderOffset;
            double fixedTop = fullTopLeft.Y;

            // Win32 hook에서 강제로 되돌릴 device pixel bounds를 저장한다.
            SaveLargeScreenFixedDeviceBounds(fixedLeft, fixedTop, fixedWidth, fixedHeight);

            // 실제 WPF 창 위치/크기를 적용한다.
            Width = fixedWidth;
            Height = fixedHeight;
            Left = fixedLeft;
            Top = fixedTop;

            MinWidth = Width;
            MaxWidth = Width;
            MinHeight = Height;
            MaxHeight = Height;
        }

        // 대형 화면 고정용 WPF 좌표를 Win32 device pixel 좌표로 저장한다.
        private void SaveLargeScreenFixedDeviceBounds(
            double left,
            double top,
            double width,
            double height)
        {
            // WPF DIP 좌표를 실제 모니터 픽셀 좌표로 변환한다.
            Matrix transformToDevice = PresentationSource.FromVisual(this)
                ?.CompositionTarget
                ?.TransformToDevice
                ?? Matrix.Identity;

            Point deviceTopLeft = transformToDevice.Transform(new Point(left, top));
            Point deviceBottomRight = transformToDevice.Transform(new Point(left + width, top + height));

            // WM_WINDOWPOSCHANGING에서 사용할 정수 좌표/크기다.
            _largeScreenDeviceLeft = (int)Math.Round(deviceTopLeft.X);
            _largeScreenDeviceTop = (int)Math.Round(deviceTopLeft.Y);
            _largeScreenDeviceWidth = (int)Math.Round(deviceBottomRight.X - deviceTopLeft.X);
            _largeScreenDeviceHeight = (int)Math.Round(deviceBottomRight.Y - deviceTopLeft.Y);
            _largeScreenFixedBoundsReady = true;
        }

        private nint LargeScreenWindowProc(
            nint hwnd,
            int msg,
            nint wParam,
            nint lParam,
            ref bool handled)
        {
            if (msg != WmWindowPositionChanging ||
                !_lockLargeScreenWindowSize ||
                !_largeScreenFixedBoundsReady ||
                lParam == nint.Zero)
            {
                // 창 위치/크기 변경 메시지가 아니거나 대형 화면 고정 상태가 아니면 건드리지 않는다.
                return nint.Zero;
            }

            // Windows가 적용하려는 위치/크기를 읽어 고정 좌표로 덮어쓴다.
            WindowPosition windowPosition = Marshal.PtrToStructure<WindowPosition>(lParam);
            windowPosition.x = _largeScreenDeviceLeft;
            windowPosition.y = _largeScreenDeviceTop;
            windowPosition.cx = _largeScreenDeviceWidth;
            windowPosition.cy = _largeScreenDeviceHeight;
            windowPosition.flags &= ~(SwpNoMove | SwpNoSize);

            Marshal.StructureToPtr(windowPosition, lParam, false);
            return nint.Zero;
        }

        // WM_WINDOWPOSCHANGING 메시지의 Win32 구조체다.
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosition
        {
            public nint hwnd;
            public nint hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        // 현재 모니터의 물리 높이가 1900px 초과인지 확인한다.
        private static bool IsLargePhysicalScreen()
        {
            return Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds.Height > LargeScreenPhysicalHeightThreshold;
        }

        // 대형 화면 고정 모드에서 창 상태가 Normal이 아니면 즉시 되돌린다.
        private void KeepLargeScreenWindowNormal()
        {
            if (!_lockLargeScreenWindowSize)
            {
                // 일반 화면에서는 창 상태를 건드리지 않는다.
                return;
            }

            if (WindowState == WindowState.Normal)
            {
                // 이미 Normal이면 처리할 것이 없다.
                return;
            }

            WindowState = WindowState.Normal;
        }


        // 프로그램 시작 직후 화면 기본 텍스트/카운트/빈 로그 행을 초기화한다.
        private void InitializeDefaultText()
        {
            ResetCategoryCountText();

            // 이력 파일이 열리기 전에는 기간 텍스트가 비어 있다.
            StartDateText.Text = "";
            EndDateText.Text = "";

            _dbStartDate = null;
            _dbEndDate = null;
            UpdateDateArrowVisibility();

            DateCalendarDropdown.Visibility = Visibility.Collapsed;

            // 기본 페이지 크기는 25건이다.
            PageSizeText.Text = $"{_pageSize:N0}건";

            // 로그 표에는 빈 안내 행만 표시한다.
            ShowLogEmptyRow("");
            UpdateRecoverMoveButtonVisibility();

            UpdatePageSizeDropdownStyle();
            RebuildPaginationButtons();
        }

        // 이동모드에서 아래쪽 다음 로그가 없을 때 안내한다.
        private static void ShowMoveTargetMissingMessage(string categoryName)
        {
            MessageBox.Show(
                $"현재 화면 아래쪽의 다음 {categoryName} 로그가 없습니다.",
                "이동 모드",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        // DB 조회/페이지 로딩 실패 안내다.
        private static void ShowLoadLogsFailedMessage()
        {
            MessageBox.Show(
                "이력 내용을 불러오는 중 오류가 발생했습니다.",
                "조회 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        // 필터 모드와 이동 모드를 전환한다.
        // 이동 모드에서는 카테고리 버튼이 필터가 아니라 현재 화면 아래쪽 다음 로그 찾기로 동작한다.
        private async Task ToggleMoveModeAsync()
        {
            if (_repository == null)
            {
                // 열린 이력이 없으면 이동할 로그가 없다.
                return;
            }

            // 현재 모드 상태를 반전한다.
            _isMoveMode = !_isMoveMode;

            // 버튼 텍스트/색상을 이동 모드 상태에 맞춘다.
            ModeToggleText.Text = _isMoveMode
                ? "이동 모드"
                : "필터 모드";

            ModeToggleText.Foreground = _isMoveMode
                ? new SolidColorBrush(Color.FromRgb(255, 90, 61))
                : new SolidColorBrush(Color.FromRgb(35, 57, 93));

            UpdateRecoverMoveButtonVisibility();

            if (_isMoveMode)
            {
                ClearMoveTargetHighlight();

                if (_currentMode == "category")
                {
                    // 카테고리 필터 상태에서 이동모드로 들어가면 필터를 풀고 이전 기본 조회 범위로 돌아간다.
                    _selectedCategories.Clear();
                    UpdateCategoryCardActiveStates();

                    await RestoreBaseQueryAfterCategoryClearAsync();
                    return;
                }

                // 전체/기간/검색 화면에서 이동모드로 들어가면 현재 화면 위치/조건은 유지한다.
                SaveBaseQueryState();

                return;
            }

            /*
             * 이동 모드에서 필터 모드로 돌아갈 때는
             * 현재 화면은 유지하고 빨간줄만 제거한다.
             */
            ClearMoveTargetHighlight();
        }

        // 이동 모드일 때만 시스템 복구 이동 버튼을 보여준다.
        private void UpdateRecoverMoveButtonVisibility()
        {
            RecoverMoveButton.Visibility = _isMoveMode
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // 카테고리 버튼 클릭을 필터 또는 이동모드 동작으로 분기한다.
        // 예: 필터 모드 화재 클릭 -> 화재 목록, 이동 모드 화재 클릭 -> 다음 화재 위치로 이동.
        private async Task HandleCategoryButtonAsync(string category)
        {
            if (IsCategoryEmptyForCurrentScope(category))
            {
                // 현재 기간/검색 범위에 해당 카테고리 로그가 0건이면 클릭 동작을 막고 안내한다.
                ShowEmptyCategoryMessage(category);
                BlockPointerInputAfterModal();
                return;
            }

            if (_isMoveMode)
            {
                // 이동 모드면 필터하지 않고 다음 로그로 이동한다.
                await MoveToNextCategoryLogAsync(category);
                return;
            }

            // 필터 모드면 카테고리 선택/해제를 적용한다.
            await ToggleCategoryAsync(category);
        }

        // 현재 카테고리 캐시 기준으로 해당 카테고리 로그가 0건인지 확인한다.
        private bool IsCategoryEmptyForCurrentScope(string category)
        {
            lock (_categoryCacheLock)
            {
                return _categoryCacheReady &&
                       _categoryCache.TryGetValue(category, out List<LogRow>? rows) &&
                       rows.Count == 0;
            }
        }

        // 현재 조회 조건에 해당 카테고리 로그가 없음을 안내한다.
        private void ShowEmptyCategoryMessage(string category)
        {
            string categoryName = _categoryDisplayNames.TryGetValue(category, out string? name)
                ? name
                : category;

            MessageBox.Show(
                $"현재 조회 조건에 {categoryName} 로그가 없습니다.",
                "이력 조회",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        // 필터 모드에서 카테고리 선택/해제를 처리한다.
        private async Task ToggleCategoryAsync(string category)
        {
            if (ShouldIgnoreCategoryClick())
            {
                // 달력 닫힘 직후 잘못 들어온 클릭은 무시한다.
                return;
            }

            if (_repository == null)
            {
                // 열린 이력이 없으면 필터할 수 없다.
                return;
            }

            /*
             * 필터 모드로 카테고리를 선택하는 순간 이동 모드의 빨간 이동선은 남기지 않는다.
             */
            ClearMoveTargetHighlight();

            if (_currentMode != "category")
            {
                // 카테고리 필터에 들어가기 전의 전체/기간/검색 조건을 저장한다.
                SaveBaseQueryState();
            }

            if (_selectedCategories.Contains(category))
            {
                // 이미 선택된 카테고리를 다시 누르면 해제한다.
                _selectedCategories.Remove(category);
            }
            else
            {
                // 새 카테고리를 추가 선택한다. 여러 카테고리 동시 선택 가능.
                _selectedCategories.Add(category);
            }

            UpdateCategoryCardActiveStates();

            if (_selectedCategories.Count == 0)
            {
                // 모든 카테고리를 해제하면 필터 전 기본 조회 상태로 돌아간다.
                await RestoreBaseQueryAfterCategoryClearAsync();
                return;
            }

            // 선택된 카테고리 로그 목록의 첫 페이지를 보여준다.
            _currentMode = "category";
            _currentPage = 1;

            await LoadCategoryPageFromCacheAsync();
        }
        // 이동 모드에서 현재 화면 아래쪽의 다음 카테고리 로그 위치로 이동한다.
        private async Task MoveToNextCategoryLogAsync(string category)
        {
            if (_repository == null)
            {
                return;
            }

            if (_currentRows.Count == 0)
            {
                // 현재 페이지에 표시 중인 로그가 없으면 기준점이 없다.
                return;
            }

            if (_isMoveNavigationRunning)
            {
                // 이동 중에는 다른 이동 버튼 클릭을 막는다.
                return;
            }

            _isMoveNavigationRunning = true;

            try
            {
                string categoryName = _categoryDisplayNames.TryGetValue(category, out string? name)
                    ? name
                    : category;

                ShowMoveProgressOverlay(categoryName);

                // 현재 화면에서 완전히 보이는 마지막 로그를 기준점으로 잡는다.
                LogRow? anchorRow = GetLastVisibleAnchorRow();

                if (anchorRow == null)
                {
                    return;
                }

                // DB 전체를 현재 조회 범위 기준으로 훑어 다음 대상 위치를 찾는다.
                MoveTargetResult? result = await Task.Run(() =>
                    FindNextCategoryLogPosition(category, anchorRow)
                );

                if (result == null)
                {
                    ShowMoveTargetMissingMessage(categoryName);

                    return;
                }

                /*
                 * 이동 모드에서는 필터링하지 않는다.
                 * 현재 전체/기간/검색 조회 범위는 유지하고, 해당 카테고리의 다음 위치로만 이동한다.
                 */
                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                RestoreMoveBaseModeIfNeeded();

                double targetOffset = CalculateMoveTargetBottomOffset(result.RowIndexInPage);

                /*
                 * 판매용 기준 핵심:
                 * 같은 페이지에 이미 렌더링된 행이면 절대 다시 렌더링하지 않는다.
                 * 기존 빨간줄 제거 -> 새 빨간줄 추가 -> 스크롤 이동만 수행한다.
                 */
                if (result.PageNumber == _currentPage)
                {
                    // 같은 페이지면 새로 로딩하지 않고 빨간줄과 스크롤 위치만 갱신한다.
                    ApplyMoveTargetHighlight(result.RowIndexInPage);

                    LogScrollViewer.ScrollToVerticalOffset(targetOffset);
                    FixedTimeScrollViewer.ScrollToVerticalOffset(targetOffset);

                    return;
                }

                /*
                 * 다른 페이지로 넘어갈 때만 새 페이지를 렌더링한다.
                 * 이때도 배치 렌더링을 쓰면 스크롤바가 아래로 갔다가 돌아오는 움직임이 보일 수 있으므로
                 * 이동 모드 전용 즉시 렌더링으로 한 프레임 안에서 행 생성, 빨간줄 표시, 목표 위치 이동을 끝낸다.
                 */
                ClearMoveTargetHighlight();

                _currentPage = result.PageNumber;

                // 다른 페이지면 목표 페이지를 즉시 렌더링하고 목표 로그가 화면 맨 아래에 오게 스크롤한다.
                await LoadCurrentPageAsync(
                    showLoading: false,
                    resetScroll: false,
                    instantRender: true,
                    targetVerticalOffset: targetOffset,
                    highlightRowIndex: result.RowIndexInPage
                );
            }
            finally
            {
                // 성공/실패 모두 이동 overlay를 닫고 다음 이동을 허용한다.
                HideMoveProgressOverlay();
                _isMoveNavigationRunning = false;
            }
        }

        // 이동 중 overlay를 띄워 사용자가 중복 클릭하지 못하게 한다.
        private void ShowMoveProgressOverlay(string categoryName)
        {
            MoveProgressDescriptionText.Text = $"다음 {categoryName} 로그를 찾고 있습니다.";
            MoveProgressOverlay.Visibility = Visibility.Visible;
        }

        // 이동 완료/실패 후 overlay를 숨긴다.
        private void HideMoveProgressOverlay()
        {
            MoveProgressOverlay.Visibility = Visibility.Collapsed;
        }

        // 이동 대상 행에 빨간 밑줄 하이라이트를 적용한다.
        private void ApplyMoveTargetHighlight(int rowIndexInPage)
        {
            ClearMoveTargetHighlight();

            if (rowIndexInPage < 0)
            {
                // 음수 index는 유효하지 않다.
                return;
            }

            if (rowIndexInPage >= _currentRows.Count)
            {
                // 현재 페이지 행 범위를 벗어나면 표시할 수 없다.
                return;
            }

            // 가상 렌더링 중이면 실제 생성된 행일 때만 즉시 줄을 붙인다.
            _highlightedRowIndexInPage = rowIndexInPage;
            AddMoveTargetHighlightIfRendered(rowIndexInPage);
        }

        // 가상 렌더링된 실제 행이 있으면 이동 대상 빨간 밑줄을 붙인다.
        private void AddMoveTargetHighlightIfRendered(int rowIndexInPage)
        {
            // 화면 밖 행은 아직 UI로 생성되지 않았을 수 있다.
            int? childIndex = GetRenderedChildIndex(rowIndexInPage);

            if (!childIndex.HasValue)
            {
                // 현재 렌더링 범위 밖이면 다음 RenderVirtualRows 때 다시 붙인다.
                return;
            }

            int index = childIndex.Value;

            if (index < 0 || index >= FixedTimeRowsPanel.Children.Count)
            {
                return;
            }

            if (index >= LogRowsPanel.Children.Count)
            {
                return;
            }

            AddBottomHighlightLine(FixedTimeRowsPanel.Children[index]);
            AddBottomHighlightLine(LogRowsPanel.Children[index]);
        }

        // 현재 페이지 row index를 실제 Panel.Children index로 변환한다.
        private int? GetRenderedChildIndex(int rowIndexInPage)
        {
            if (!_isVirtualRenderingActive)
            {
                // 일반 렌더링에서는 row index와 child index가 같다.
                return rowIndexInPage;
            }

            if (rowIndexInPage < _virtualStartIndex ||
                rowIndexInPage >= _virtualEndIndexExclusive)
            {
                // 가상 렌더링 범위 밖 행은 아직 생성된 UI가 없다.
                return null;
            }

            // 가상 렌더링은 앞쪽 spacer가 1개 있어서 +1 보정한다.
            return rowIndexInPage - _virtualStartIndex + 1;
        }

        // 현재 이동 대상 빨간 밑줄을 제거한다.
        private void ClearMoveTargetHighlight()
        {
            if (_highlightedRowIndexInPage == null)
            {
                // 표시 중인 하이라이트가 없다.
                return;
            }

            int index = _highlightedRowIndexInPage.Value;
            int? childIndex = GetRenderedChildIndex(index);

            if (childIndex.HasValue &&
                childIndex.Value >= 0 &&
                childIndex.Value < FixedTimeRowsPanel.Children.Count)
            {
                RemoveBottomHighlightLine(FixedTimeRowsPanel.Children[childIndex.Value]);
            }

            if (childIndex.HasValue &&
                childIndex.Value >= 0 &&
                childIndex.Value < LogRowsPanel.Children.Count)
            {
                RemoveBottomHighlightLine(LogRowsPanel.Children[childIndex.Value]);
            }

            _highlightedRowIndexInPage = null;
        }

        // 실제 행 UI 아래쪽에 빨간 2px 라인을 추가한다.
        private void AddBottomHighlightLine(UIElement rowElement)
        {
            if (rowElement is not Border border)
            {
                // 예상한 로그 행 구조가 아니면 무시한다.
                return;
            }

            if (border.Child is not Grid rootGrid)
            {
                // Render_Log_Row는 Border 안에 Grid를 둔다.
                return;
            }

            // 중복 라인이 생기지 않도록 기존 이동 라인을 먼저 제거한다.
            RemoveBottomHighlightLine(rowElement);

            Border line = new()
            {
                Height = 2,
                Background = new SolidColorBrush(Color.FromRgb(255, 90, 61)),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0),
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
                Tag = MoveTargetLineTag
            };

            Panel.SetZIndex(line, 999);
            rootGrid.Children.Add(line);
        }

        // 행 UI에서 이동모드 빨간 라인을 제거한다.
        private void RemoveBottomHighlightLine(UIElement rowElement)
        {
            if (rowElement is not Border border)
            {
                return;
            }

            if (border.Child is not Grid grid)
            {
                return;
            }

            // Tag가 MoveTargetLine인 Border만 찾아 제거한다.
            List<UIElement> lines = grid.Children
                .OfType<UIElement>()
                .Where(child => child is Border b && Equals(b.Tag, MoveTargetLineTag))
                .ToList();

            foreach (UIElement line in lines)
            {
                grid.Children.Remove(line);
            }
        }

        // 현재 화면에 완전히 보이는 마지막 로그를 이동모드 기준점으로 잡는다.
        private LogRow? GetLastVisibleAnchorRow()
        {
            if (_currentRows.Count == 0)
            {
                return null;
            }

            /*
             * 이동 모드 기준:
             * 화면에 실제로 완전히 보이는 마지막 행을 기준으로 잡는다.
             *
             * 기존 방식처럼
             * (VerticalOffset + ViewportHeight - 1) / RowHeight
             * 로 계산하면, 화면 아래에 걸쳐 있거나 실제로는 잘려 있는 행까지
             * 마지막 행으로 포함될 수 있다.
             *
             * 그래서 "완전히 보이는 마지막 행" 기준으로 계산한다.
             */
            double viewportHeight = LogScrollViewer.ViewportHeight;

            if (viewportHeight <= 0)
            {
                viewportHeight = LogScrollViewer.ActualHeight;
            }

            double viewportTop = LogScrollViewer.VerticalOffset;
            double viewportBottom = viewportTop + viewportHeight;

            // 화면 아래에 걸친 행은 제외하고 완전히 보이는 마지막 행 index를 계산한다.
            int lastFullyVisibleIndex = (int)Math.Floor(viewportBottom / LogRowVisualHeight) - 1;

            // 계산된 index가 현재 페이지 범위를 벗어나지 않게 보정한다.
            lastFullyVisibleIndex = Math.Clamp(
                lastFullyVisibleIndex,
                0,
                _currentRows.Count - 1
            );

            return _currentRows[lastFullyVisibleIndex];
        }
        // 기준 로그 이후 다음 카테고리 로그의 페이지 번호와 페이지 안 위치를 찾는다.
        private MoveTargetResult? FindNextCategoryLogPosition(
            string category,
            LogRow anchorRow)
        {
            if (_repository == null)
            {
                // 열린 DB가 없으면 탐색할 수 없다.
                return null;
            }

            // 카테고리 모드에서 이동모드를 쓰는 경우 필터 전 기본 조회 범위를 기준으로 찾는다.
            string moveMode = _currentMode == "category"
                ? _baseModeBeforeCategory
                : _currentMode;

            string moveKeyword = _currentMode == "category"
                ? _baseKeywordBeforeCategory
                : _currentKeyword;

            string moveStartDate = _currentMode == "category"
                ? _baseStartDateBeforeCategory
                : _currentStartDate;

            string moveEndDate = _currentMode == "category"
                ? _baseEndDateBeforeCategory
                : _currentEndDate;

            DateTime? filterStart = IsValidDate(moveStartDate)
                ? DateTime.Parse(moveStartDate).Date
                : null;

            DateTime? filterEnd = IsValidDate(moveEndDate)
                ? DateTime.Parse(moveEndDate).Date.AddDays(1).AddTicks(-1)
                : null;

            bool anchorFound = false;
            int scopedIndex = -1;
            MoveTargetResult? lastConsecutiveTarget = null;

            // DB를 최신순으로 훑으면서 현재 조회 범위에 포함되는 로그만 scopedIndex를 올린다.
            foreach (LogRow row in _repository.StreamAllLogs())
            {
                if (!IsRowInMoveScope(row, moveMode, moveKeyword, filterStart, filterEnd))
                {
                    continue;
                }

                scopedIndex++;

                if (!anchorFound)
                {
                    if (row.Id == anchorRow.Id)
                    {
                        // 현재 화면의 기준 로그를 찾은 뒤부터 다음 대상을 찾는다.
                        anchorFound = true;
                    }

                    continue;
                }

                string rowTag = LogClassifier.ClassifyRowTag(row, scopedIndex);
                List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);
                bool matchesCategory = categories.Contains(category);

                if (!matchesCategory)
                {
                    if (lastConsecutiveTarget != null)
                    {
                        // 같은 카테고리 연속 묶음이 끝났으므로 묶음의 마지막 로그로 이동한다.
                        return lastConsecutiveTarget;
                    }

                    continue;
                }

                int pageNumber = scopedIndex / _pageSize + 1;
                int rowIndexInPage = scopedIndex % _pageSize;

                // 연속된 같은 카테고리는 계속 갱신해 마지막 로그를 보관한다.
                lastConsecutiveTarget = new MoveTargetResult
                {
                    RowId = row.Id,
                    PageNumber = pageNumber,
                    RowIndexInPage = rowIndexInPage
                };
            }

            // 파일 끝까지 연속 묶음이 이어졌다면 마지막 대상을 반환한다.
            return lastConsecutiveTarget;
        }

        // 이동 대상 로그가 화면 맨 아래에 오도록 세로 스크롤 offset을 계산한다.
        private double CalculateMoveTargetBottomOffset(int rowIndexInPage)
        {
            double viewportHeight = LogScrollViewer.ViewportHeight;

            if (viewportHeight <= 0)
            {
                // 초기 레이아웃 전에는 ActualHeight를 대신 사용한다.
                viewportHeight = LogScrollViewer.ActualHeight;
            }

            // 대상 행의 아래쪽 위치다.
            double targetRowBottom = (rowIndexInPage + 1) * LogRowVisualHeight;

            return Math.Max(0, targetRowBottom - viewportHeight);
        }
        // 이동모드 탐색에서 현재 로그가 전체/기간/검색 범위 안에 있는지 확인한다.
        private bool IsRowInMoveScope(
            LogRow row,
            string mode,
            string keyword,
            DateTime? filterStart,
            DateTime? filterEnd)
        {
            if (mode == "date_cache" || mode == "date")
            {
                // 기간 조회 중이면 그 기간 안에서만 다음 로그를 찾는다.
                return IsRowInDateRange(row, filterStart, filterEnd);
            }

            if (mode == "search")
            {
                // 검색 중이면 검색 결과 안에서만 다음 로그를 찾는다.
                return IsRowMatchedByKeyword(row, keyword);
            }

            // 전체 로그 상태면 모든 로그가 탐색 범위다.
            return true;
        }

        // 검색어가 실제 로그 표시값 중 하나에 포함되는지 확인한다.
        private static bool IsRowMatchedByKeyword(LogRow row, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                // 검색어가 없으면 전체 포함이다.
                return true;
            }

            // 화면에 보이는 값과 Packet을 한 문자열로 묶어 검색한다.
            string target =
                $"{row.DTime} {row.Type} {row.Action} {row.Section} {row.Contents} {row.Packet}";

            return target.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        // 이동모드에서 category 상태라면 필터 전 기본 조회 상태로 되돌린다.
        private void RestoreMoveBaseModeIfNeeded()
        {
            if (_currentMode != "category")
            {
                // 이미 전체/기간/검색이면 복원할 필요가 없다.
                return;
            }

            _currentMode = _baseModeBeforeCategory;
            _activeRowsCacheKey = _baseCacheKeyBeforeCategory;
            _currentKeyword = _baseKeywordBeforeCategory;
            _currentStartDate = _baseStartDateBeforeCategory;
            _currentEndDate = _baseEndDateBeforeCategory;
        }
        // 선택된 카테고리 캐시에서 현재 페이지에 보여줄 로그를 만든다.
        private async Task LoadCategoryPageFromCacheAsync()
        {
            // 여러 카테고리를 합치고 중복 Log.ID를 제거한 목록이다.
            List<LogRow> combinedRows = GetSelectedCategoryRowsFromCache();

            // 카테고리 필터 결과 기준 총 건수/페이지 수를 갱신한다.
            _totalCount = combinedRows.Count;
            _totalPages = CalculateTotalPages(_totalCount);

            _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

            _currentRows = combinedRows
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();

            RebuildPaginationButtons();

            if (!_categoryCacheReady && _categoryCacheBuilding && _currentRows.Count == 0)
            {
                // 캐시 계산이 끝나기 전이면 빈 결과가 아니라 계산 중 안내를 보여준다.
                ShowLogEmptyRow("선택한 카테고리 내용을 계산하는 중입니다...");
                return;
            }

            // 선택 카테고리 페이지를 렌더링한다.
            await RenderRowsBatchedAsync(_currentRows);
        }

        // 현재 선택된 카테고리 기준으로 로그를 가져온다.
        private List<LogRow> GetSelectedCategoryRowsFromCache()
        {
            return GetSelectedCategoryRowsFromCache(_selectedCategories);
        }

        // 지정된 카테고리 목록에 속한 로그를 합치고 중복 제거 후 최신순 정렬한다.
        private List<LogRow> GetSelectedCategoryRowsFromCache(IEnumerable<string> selectedCategories)
        {
            lock (_categoryCacheLock)
            {
                // MCC+ON처럼 한 로그가 여러 카테고리에 들어갈 수 있어 ID 기준으로 중복 제거한다.
                Dictionary<long, LogRow> uniqueRows = new();

                foreach (string category in selectedCategories)
                {
                    if (!_categoryCache.ContainsKey(category))
                    {
                        // 알 수 없는 카테고리는 무시한다.
                        continue;
                    }

                    foreach (LogRow row in _categoryCache[category])
                    {
                        if (!uniqueRows.ContainsKey(row.Id))
                        {
                            uniqueRows[row.Id] = row;
                        }
                    }
                }

                // DB 정렬과 맞게 DTIME DESC, ID DESC 순서로 돌려준다.
                return uniqueRows.Values
                    .OrderByDescending(row => ParseSortableDate(row.DTime))
                    .ThenByDescending(row => row.Id)
                    .ToList();
            }
        }

        // 현재 모드/페이지 기준으로 실제 로그를 불러오고 화면에 렌더링한다.
        private async Task LoadCurrentPageAsync(
            bool showLoading = true,
            bool resetScroll = true,
            bool instantRender = false,
            double? targetVerticalOffset = null,
            int? highlightRowIndex = null)
        {
            if (_repository == null)
            {
                // 열린 이력 DB가 없으면 로딩할 데이터가 없다.
                return;
            }

            // 이전 페이지 로딩 작업이 있으면 취소하고 최신 요청만 살린다.
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();

            CancellationToken token = _loadCts.Token;

            try
            {

                List<LogRow> rows;

                if (_currentMode == "all")
                {
                    // 전체 로그: DB에서 현재 page/pageSize만 읽는다.
                    int page = _currentPage;
                    int pageSize = _pageSize;

                    rows = await Task.Run(() =>
                        _repository.LoadLogsPage(page, pageSize), token
                    );
                }
                else if (_currentMode == "search")
                {
                    // 검색 로그: DB에서 검색 조건에 맞는 현재 페이지만 읽는다.
                    string keyword = _currentKeyword;
                    int page = _currentPage;
                    int pageSize = _pageSize;

                    rows = await Task.Run(() =>
                        _repository.SearchLogsPage(keyword, page, pageSize), token
                    );
                }
                else if (_currentMode == "date_cache")
                {
                    // 기간 캐시 로그: 이미 만들어진 기간 목록에서 현재 페이지만 자른다.
                    string cacheKey = _activeRowsCacheKey;
                    int page = _currentPage;
                    int pageSize = _pageSize;

                    rows = await Task.Run(() =>
                        GetRowsPageFromCache(cacheKey, page, pageSize), token
                    );
                }
                else if (_currentMode == "date")
                {
                    // 직접 기간 조회 모드: DB에서 날짜 범위와 페이지 조건으로 읽는다.
                    string startDate = _currentStartDate;
                    string endDate = _currentEndDate;
                    int page = _currentPage;
                    int pageSize = _pageSize;

                    rows = await Task.Run(() =>
                        _repository.LoadLogsByDatePage(startDate, endDate, page, pageSize), token
                    );
                }
                else if (_currentMode == "category")
                {
                    // 카테고리 필터: 캐시된 카테고리 로그 목록에서 현재 페이지만 자른다.
                    List<LogRow> combinedRows = GetSelectedCategoryRowsFromCache();

                    _totalCount = combinedRows.Count;
                    _totalPages = CalculateTotalPages(_totalCount);

                    rows = combinedRows
                        .Skip((_currentPage - 1) * _pageSize)
                        .Take(_pageSize)
                        .ToList();
                }
                else
                {
                    // 알 수 없는 모드는 빈 결과로 처리한다.
                    rows = new List<LogRow>();
                }

                if (token.IsCancellationRequested)
                {
                    // 더 최신 로딩 요청이 들어오면 현재 결과를 화면에 반영하지 않는다.
                    return;
                }

                // 현재 페이지 로그와 페이지 버튼을 갱신한다.
                _currentRows = rows;
                _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

                RebuildPaginationButtons();

                await RenderRowsBatchedAsync(
                    _currentRows,
                    resetScroll,
                    instantRender,
                    targetVerticalOffset,
                    highlightRowIndex
                );
            }
            catch (OperationCanceledException)
            {
                // 최신 요청으로 교체된 정상 취소다.
            }
            catch
            {
                // DB 읽기 실패 등은 빈 행과 오류 팝업으로 안내한다.
                ShowLogEmptyRow("이력 내용을 불러오는 중 오류가 발생했습니다.");
                RebuildPaginationButtons();

                ShowLoadLogsFailedMessage();
            }
        }

        // 현재 페이지 LogRow 목록을 화면에 렌더링한다.
        // 실제 UI는 가상화로 보이는 행 주변만 생성한다.
        private async Task RenderRowsBatchedAsync(
            List<LogRow> rows,
            bool resetScroll = true,
            bool instantRender = false,
            double? targetVerticalOffset = null,
            int? highlightRowIndex = null)
        {
            // 이전 렌더링 작업을 취소하고 최신 렌더링만 유지한다.
            _renderCts?.Cancel();
            _renderCts = new CancellationTokenSource();

            CancellationToken token = _renderCts.Token;

            // 이동모드로 들어온 경우 목표 행 index를 저장한다.
            _highlightedRowIndexInPage = highlightRowIndex;

            if (resetScroll)
            {
                // 일반 페이지 전환은 맨 위/왼쪽부터 보여준다.
                FixedTimeScrollViewer.ScrollToTop();
                LogScrollViewer.ScrollToTop();
                LogScrollViewer.ScrollToHorizontalOffset(0);
                HeaderHorizontalScrollViewer.ScrollToHorizontalOffset(0);
            }

            RebuildPaginationButtons();

            if (rows.Count == 0)
            {
                // 현재 조건에 표시할 로그가 없으면 빈 안내 행을 보여준다.
                ShowLogEmptyRow("표시할 이력 내용이 없습니다.");
                return;
            }

            try
            {
                token.ThrowIfCancellationRequested();

                // 가상 렌더링을 켜고 현재 viewport 주변 행만 즉시 그린다.
                ActivateVirtualRendering();
                RenderVirtualRows(force: true);

                await Dispatcher.Yield(DispatcherPriority.Loaded);
                token.ThrowIfCancellationRequested();

                if (targetVerticalOffset.HasValue)
                {
                    // 이동모드에서는 목표 행이 화면 맨 아래에 오도록 스크롤한다.
                    LogScrollViewer.ScrollToVerticalOffset(targetVerticalOffset.Value);
                    FixedTimeScrollViewer.ScrollToVerticalOffset(targetVerticalOffset.Value);
                    RenderVirtualRows(force: true);
                }
            }
            catch (OperationCanceledException)
            {
                // 새 렌더링 요청으로 교체된 정상 취소다.
            }
        }

        // 가상 렌더링 상태를 시작한다.
        private void ActivateVirtualRendering()
        {
            _isVirtualRenderingActive = true;
            _virtualStartIndex = -1;
            _virtualEndIndexExclusive = -1;
        }

        // 가상 렌더링 상태와 이동 하이라이트를 해제한다.
        private void DeactivateVirtualRendering()
        {
            _isVirtualRenderingActive = false;
            _virtualStartIndex = -1;
            _virtualEndIndexExclusive = -1;
            _highlightedRowIndexInPage = null;
        }

        // 로그가 없을 때 왼쪽 시간 빈 셀과 오른쪽 안내 행을 표시한다.
        private void ShowLogEmptyRow(string message)
        {
            DeactivateVirtualRendering();

            // 기존 실제 로그 행을 모두 지운다.
            FixedTimeRowsPanel.Children.Clear();
            LogRowsPanel.Children.Clear();

            FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedEmptyCell());
            LogRowsPanel.Children.Add(Render_Log_Row.CreateEmptyRow(message));
        }

        // 현재 스크롤 위치 기준으로 보여야 하는 로그 행만 실제 UI로 만든다.
        private void RenderVirtualRows(bool force = false)
        {
            if (!_isVirtualRenderingActive || _currentRows.Count == 0)
            {
                // 가상 렌더링이 꺼졌거나 표시할 행이 없으면 작업하지 않는다.
                return;
            }

            double viewportHeight = LogScrollViewer.ViewportHeight;

            if (viewportHeight <= 0)
            {
                // 초기 레이아웃 전에는 ActualHeight를 대신 쓴다.
                viewportHeight = LogScrollViewer.ActualHeight;
            }

            if (viewportHeight <= 0)
            {
                // 그래도 높이가 없으면 기본 12행 높이로 계산한다.
                viewportHeight = LogRowVisualHeight * 12;
            }

            // 현재 스크롤 위치에서 첫 번째로 보이는 로그 index를 계산한다.
            double verticalOffset = Math.Max(0, LogScrollViewer.VerticalOffset);
            int firstVisibleIndex = (int)Math.Floor(verticalOffset / LogRowVisualHeight);
            int visibleCount = (int)Math.Ceiling(viewportHeight / LogRowVisualHeight) + 1;

            // 위/아래 버퍼를 포함해 실제 생성할 index 범위를 계산한다.
            int startIndex = Math.Max(0, firstVisibleIndex - VirtualizationBufferRows);
            int endIndexExclusive = Math.Min(
                _currentRows.Count,
                firstVisibleIndex + visibleCount + VirtualizationBufferRows
            );

            if (!force &&
                startIndex == _virtualStartIndex &&
                endIndexExclusive == _virtualEndIndexExclusive)
            {
                // 생성 범위가 변하지 않았으면 다시 그리지 않는다.
                return;
            }

            // 이번에 실제 렌더링한 범위를 저장한다.
            _virtualStartIndex = startIndex;
            _virtualEndIndexExclusive = endIndexExclusive;

            FixedTimeRowsPanel.Children.Clear();
            LogRowsPanel.Children.Clear();

            // 실제 전체 높이를 유지하기 위해 화면 위/아래를 spacer로 채운다.
            double topSpacerHeight = startIndex * LogRowVisualHeight;
            double bottomSpacerHeight = (_currentRows.Count - endIndexExclusive) * LogRowVisualHeight;

            FixedTimeRowsPanel.Children.Add(CreateVirtualSpacer(topSpacerHeight));
            LogRowsPanel.Children.Add(CreateVirtualSpacer(topSpacerHeight));

            for (int i = startIndex; i < endIndexExclusive; i++)
            {
                // 왼쪽 고정 시간 행과 오른쪽 로그 행을 같은 index로 나란히 생성한다.
                FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedTimeCell(_currentRows[i], i));
                LogRowsPanel.Children.Add(Render_Log_Row.CreateScrollableLogRow(_currentRows[i], i));
            }

            FixedTimeRowsPanel.Children.Add(CreateVirtualSpacer(bottomSpacerHeight));
            LogRowsPanel.Children.Add(CreateVirtualSpacer(bottomSpacerHeight));

            if (_highlightedRowIndexInPage.HasValue)
            {
                // 이동 대상 행이 이번 렌더 범위 안에 들어오면 빨간줄을 붙인다.
                AddMoveTargetHighlightIfRendered(_highlightedRowIndexInPage.Value);
            }
        }

        // 가상 렌더링에서 실제 행이 없는 구간의 높이를 대신 채우는 spacer다.
        private static Border CreateVirtualSpacer(double height)
        {
            return new Border
            {
                Height = Math.Max(0, height),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
        }

        // 현재 전체/기간 조회 범위의 카테고리별 로그 목록과 카운트를 백그라운드에서 만든다.
        private void StartBackgroundCategoryCache(string startDate = "", string endDate = "")
        {
            if (_repository == null)
            {
                // 열린 DB가 없으면 캐시를 만들 수 없다.
                return;
            }

            string cacheKey = MakePeriodCacheKey(startDate, endDate);

            if (TryApplyCategoryCache(cacheKey))
            {
                // 이미 같은 전체/기간 캐시가 있으면 즉시 적용하고 끝낸다.
                return;
            }

            // 기존 카운트 작업을 취소하고 새 작업을 시작한다.
            _countCts?.Cancel();
            _countCts = new CancellationTokenSource();

            CancellationToken token = _countCts.Token;
            LogRepository repository = _repository;
            int jobId = Interlocked.Increment(ref _countJobVersion);

            // UI에는 계산 중임을 표시한다.
            _categoryCacheReady = false;
            _categoryCacheBuilding = true;

            ClearCurrentCategoryCache();

            FireCountText.Text = "...";
            AlarmCountText.Text = "...";
            RelayErrorCountText.Text = "...";
            AnErrorCountText.Text = "...";
            LineBreakCountText.Text = "...";
            OutputCountText.Text = "...";
            MccCountText.Text = "...";
            EtcCountText.Text = "...";

            // 기간 캐시인 경우 해당 날짜 범위 안의 로그만 카운트한다.
            DateTime? filterStart = IsValidDate(startDate)
                ? DateTime.Parse(startDate).Date
                : null;

            DateTime? filterEnd = IsValidDate(endDate)
                ? DateTime.Parse(endDate).Date.AddDays(1).AddTicks(-1)
                : null;

            Task.Run(() =>
            {
                // liveCounts는 UI에 중간 숫자를 보여주는 용도, builtCategoryCache는 최종 필터용 목록이다.
                Dictionary<string, int> liveCounts = CreateEmptyCountMap();
                Dictionary<string, List<LogRow>> builtCategoryCache = CreateEmptyCategoryCache();
                Stopwatch uiUpdateWatch = Stopwatch.StartNew();

                int processed = 0;
                bool wasCanceled = false;

                // 전체 DB를 최신순으로 스트리밍해 30~60만 건도 한 번에 UI에 올리지 않는다.
                foreach (LogRow row in repository.StreamAllLogs())
                {
                    if (token.IsCancellationRequested || jobId != _countJobVersion)
                    {
                        // 새 파일/새 조건이 들어오면 오래된 작업은 결과를 버린다.
                        wasCanceled = true;
                        break;
                    }

                    if (!IsRowInDateRange(row, filterStart, filterEnd))
                    {
                        // 기간 범위 밖 로그는 카운트/필터 캐시에 포함하지 않는다.
                        continue;
                    }

                    // 화면 필터와 같은 LogClassifier 기준으로 카테고리를 계산한다.
                    string rowTag = LogClassifier.ClassifyRowTag(row, processed);
                    List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);

                    foreach (string category in categories)
                    {
                        if (!builtCategoryCache.ContainsKey(category))
                        {
                            // 화면에 없는 카테고리는 캐시에 넣지 않는다.
                            continue;
                        }

                        builtCategoryCache[category].Add(row);

                        if (liveCounts.ContainsKey(category))
                        {
                            liveCounts[category]++;
                        }
                    }

                    processed++;

                    if (processed % 1000 == 0 && uiUpdateWatch.ElapsedMilliseconds >= 120)
                    {
                        // 1000건 단위, 최소 120ms 간격으로만 UI 숫자를 갱신해 렉을 줄인다.
                        Dictionary<string, int> snapshot = new(liveCounts);
                        uiUpdateWatch.Restart();

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (token.IsCancellationRequested || jobId != _countJobVersion)
                            {
                                return;
                            }

                            UpdateCategoryCountText(snapshot);
                        }), DispatcherPriority.Background);
                    }
                }

                if (wasCanceled || token.IsCancellationRequested || jobId != _countJobVersion)
                {
                    // 취소된 작업 결과는 캐시에 저장하지 않는다.
                    return;
                }

                // 최종 카테고리 목록은 UI 적용 전에 복사본으로 만든다.
                Dictionary<string, List<LogRow>> finalSnapshot = CloneCategoryCache(builtCategoryCache);

                lock (_queryCacheLock)
                {
                    // 같은 기간 조회에서 다시 쓰기 위해 카테고리 캐시를 저장한다.
                    _categoryCacheByKey[cacheKey] = finalSnapshot;
                    _totalCountCacheByKey[cacheKey] = processed;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested || jobId != _countJobVersion)
                    {
                        return;
                    }

                    ApplyCategoryCacheSnapshot(finalSnapshot);

                    if (_currentMode == "category" && _selectedCategories.Count > 0)
                    {
                        // 사용자가 계산 중 카테고리를 눌렀다면 캐시 완료 후 해당 페이지를 다시 보여준다.
                        _ = LoadCategoryPageFromCacheAsync();
                    }
                }), DispatcherPriority.Background);
            });
        }

        // 현재 _categoryCache에 들어 있는 실제 로그 수를 카드 숫자로 반영한다.
        private void UpdateCategoryCountTextFromCache()
        {
            lock (_categoryCacheLock)
            {
                FireCountText.Text = _categoryCache["fire"].Count.ToString("N0");
                AlarmCountText.Text = _categoryCache["alarm"].Count.ToString("N0");
                RelayErrorCountText.Text = _categoryCache["relay_fault"].Count.ToString("N0");
                AnErrorCountText.Text = _categoryCache["an_fault"].Count.ToString("N0");
                LineBreakCountText.Text = _categoryCache["line_fault"].Count.ToString("N0");
                OutputCountText.Text = _categoryCache["output"].Count.ToString("N0");
                MccCountText.Text = _categoryCache["mcc"].Count.ToString("N0");
                EtcCountText.Text = _categoryCache["other"].Count.ToString("N0");
            }
        }

        // 백그라운드 계산 중간 snapshot 숫자를 카드에 표시한다.
        private void UpdateCategoryCountText(Dictionary<string, int> counts)
        {
            FireCountText.Text = counts.TryGetValue("fire", out int fire)
                ? fire.ToString("N0")
                : "0";

            AlarmCountText.Text = counts.TryGetValue("alarm", out int alarm)
                ? alarm.ToString("N0")
                : "0";

            RelayErrorCountText.Text = counts.TryGetValue("relay_fault", out int relayFault)
                ? relayFault.ToString("N0")
                : "0";

            AnErrorCountText.Text = counts.TryGetValue("an_fault", out int anFault)
                ? anFault.ToString("N0")
                : "0";

            LineBreakCountText.Text = counts.TryGetValue("line_fault", out int lineFault)
                ? lineFault.ToString("N0")
                : "0";

            OutputCountText.Text = counts.TryGetValue("output", out int output)
                ? output.ToString("N0")
                : "0";

            MccCountText.Text = counts.TryGetValue("mcc", out int mcc)
                ? mcc.ToString("N0")
                : "0";

            EtcCountText.Text = counts.TryGetValue("other", out int other)
                ? other.ToString("N0")
                : "0";
        }

        // 기간 조회 로그 전체 목록과 카테고리 캐시를 만들거나 기존 캐시를 반환한다.
        // 예: 2026-06-01~2026-06-07 기간 로그를 한 번만 스캔해 이후 필터/저장/인쇄에 재사용한다.
        private async Task<List<LogRow>?> GetOrBuildRowsCacheAsync(string startDate, string endDate)
        {
            if (_repository == null)
            {
                // 열린 DB가 없으면 빈 목록으로 처리한다.
                return new List<LogRow>();
            }

            string cacheKey = MakePeriodCacheKey(startDate, endDate);

            lock (_queryCacheLock)
            {
                if (_rowsCacheByKey.TryGetValue(cacheKey, out List<LogRow>? cachedRows))
                {
                    // 이미 만든 기간 로그 캐시가 있으면 DB를 다시 읽지 않는다.
                    if (_categoryCacheByKey.TryGetValue(cacheKey, out Dictionary<string, List<LogRow>>? cachedCategoryCache))
                    {
                        ApplyCategoryCacheSnapshot(cachedCategoryCache);
                    }

                    _totalCountCacheByKey[cacheKey] = cachedRows.Count;
                    return cachedRows;
                }
            }

            _countCts?.Cancel();
            _countCts = new CancellationTokenSource();

            // 새 기간 캐시 작업 버전을 발급해 오래된 작업 결과를 막는다.
            CancellationToken token = _countCts.Token;
            int jobId = Interlocked.Increment(ref _countJobVersion);

            DateTime filterStart = DateTime.Parse(startDate).Date;
            DateTime filterEnd = DateTime.Parse(endDate).Date.AddDays(1).AddTicks(-1);

            // 기간 캐시 계산 중 카테고리 카드 숫자를 진행 상태로 보여준다.
            FireCountText.Text = "...";
            AlarmCountText.Text = "...";
            RelayErrorCountText.Text = "...";
            AnErrorCountText.Text = "...";
            LineBreakCountText.Text = "...";
            OutputCountText.Text = "...";
            MccCountText.Text = "...";
            EtcCountText.Text = "...";

            LogRepository repository = _repository;

            QueryCacheEntry? builtEntry = await Task.Run(() =>
            {
                // rows는 실제 기간 로그 전체 목록, categoryCache는 그 목록의 카테고리별 분류다.
                List<LogRow> rows = new();
                Dictionary<string, List<LogRow>> categoryCache = CreateEmptyCategoryCache();
                Dictionary<string, int> liveCounts = CreateEmptyCountMap();
                Stopwatch uiUpdateWatch = Stopwatch.StartNew();

                bool wasCanceled = false;

                // 전체 DB를 스트리밍하면서 선택 기간에 포함되는 로그만 rows에 담는다.
                foreach (LogRow row in repository.StreamAllLogs())
                {
                    if (token.IsCancellationRequested || jobId != _countJobVersion)
                    {
                        // 새 조회 조건이 들어오면 현재 계산을 중단한다.
                        wasCanceled = true;
                        break;
                    }

                    if (!IsRowInDateRange(row, filterStart, filterEnd))
                    {
                        // 선택 기간 밖의 로그는 건너뛴다.
                        continue;
                    }

                    // 기간 안에서의 index 기준으로 rowTag를 계산한다.
                    int filteredIndex = rows.Count;
                    string rowTag = LogClassifier.ClassifyRowTag(row, filteredIndex);
                    List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);

                    rows.Add(row);

                    foreach (string category in categories)
                    {
                        if (!categoryCache.ContainsKey(category))
                        {
                            // 화면에 없는 카테고리는 저장하지 않는다.
                            continue;
                        }

                        categoryCache[category].Add(row);

                        if (liveCounts.ContainsKey(category))
                        {
                            liveCounts[category]++;
                        }
                    }

                    if (rows.Count % 1000 == 0 && uiUpdateWatch.ElapsedMilliseconds >= 120)
                    {
                        // 대용량 DB에서도 UI가 멈추지 않게 중간 숫자 갱신을 제한한다.
                        Dictionary<string, int> snapshot = new(liveCounts);
                        uiUpdateWatch.Restart();

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (token.IsCancellationRequested || jobId != _countJobVersion)
                            {
                                return;
                            }

                            UpdateCategoryCountText(snapshot);
                        }), DispatcherPriority.Background);
                    }
                }

                if (wasCanceled || token.IsCancellationRequested || jobId != _countJobVersion)
                {
                    // 취소된 기간 캐시는 사용하지 않는다.
                    return null;
                }

                // 최종 기간 로그 목록과 카테고리 캐시를 묶어서 반환한다.
                return new QueryCacheEntry
                {
                    Rows = rows,
                    CategoryCache = CloneCategoryCache(categoryCache)
                };
            });

            if (builtEntry == null || token.IsCancellationRequested || jobId != _countJobVersion)
            {
                // 계산 취소 또는 새 조건 진입 시 호출자에게 실패를 알린다.
                return null;
            }

            lock (_queryCacheLock)
            {
                // 같은 기간을 다시 열 때 재사용하도록 캐시에 저장한다.
                _rowsCacheByKey[cacheKey] = builtEntry.Rows;
                _categoryCacheByKey[cacheKey] = builtEntry.CategoryCache;
                _totalCountCacheByKey[cacheKey] = builtEntry.TotalCount;
            }

            ApplyCategoryCacheSnapshot(builtEntry.CategoryCache);

            return builtEntry.Rows;
        }

        // 기간 캐시 목록에서 현재 page/pageSize에 해당하는 로그만 잘라온다.
        private List<LogRow> GetRowsPageFromCache(string cacheKey, int page, int pageSize)
        {
            lock (_queryCacheLock)
            {
                if (!_rowsCacheByKey.TryGetValue(cacheKey, out List<LogRow>? rows))
                {
                    // 캐시가 없으면 빈 페이지로 처리한다.
                    return new List<LogRow>();
                }

                int safePage = Math.Max(1, page);
                int safePageSize = Math.Max(1, pageSize);

                // 예: page=2, pageSize=25이면 캐시 목록의 26~50번째 로그다.
                return rows
                    .Skip((safePage - 1) * safePageSize)
                    .Take(safePageSize)
                    .ToList();
            }
        }

        // 기간 캐시 키를 만든다.
        // 예: start/end가 있으면 date:2026-06-01~2026-06-07, 없으면 all.
        private static string MakePeriodCacheKey(string startDate, string endDate)
        {
            if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
            {
                return "all";
            }

            return $"date:{startDate.Trim()}~{endDate.Trim()}";
        }

        // 이미 만들어진 카테고리 캐시가 있으면 현재 UI 캐시에 적용한다.
        private bool TryApplyCategoryCache(string cacheKey)
        {
            lock (_queryCacheLock)
            {
                if (!_categoryCacheByKey.TryGetValue(cacheKey, out Dictionary<string, List<LogRow>>? cachedCategoryCache))
                {
                    // 아직 이 기간/전체 조건의 캐시가 없다.
                    return false;
                }

                ApplyCategoryCacheSnapshot(cachedCategoryCache);
                return true;
            }
        }

        // 백그라운드에서 만든 카테고리 캐시 snapshot을 현재 UI 캐시에 적용한다.
        private void ApplyCategoryCacheSnapshot(Dictionary<string, List<LogRow>> snapshot)
        {
            lock (_categoryCacheLock)
            {
                foreach (string key in _categoryCache.Keys.ToList())
                {
                    // 기존 카테고리 목록을 먼저 비운다.
                    _categoryCache[key].Clear();
                }

                foreach (KeyValuePair<string, List<LogRow>> pair in snapshot)
                {
                    if (!_categoryCache.ContainsKey(pair.Key))
                    {
                        // 알 수 없는 카테고리는 무시한다.
                        continue;
                    }

                    _categoryCache[pair.Key].AddRange(pair.Value);
                }
            }

            _categoryCacheReady = true;
            _categoryCacheBuilding = false;

            // 실제 카테고리 카드 숫자를 최종 값으로 갱신한다.
            UpdateCategoryCountTextFromCache();
        }

        // 현재 카테고리 캐시 목록을 모두 비운다.
        private void ClearCurrentCategoryCache()
        {
            lock (_categoryCacheLock)
            {
                foreach (string key in _categoryCache.Keys.ToList())
                {
                    _categoryCache[key].Clear();
                }
            }
        }

        // 새 이력 파일을 열 때 기간/카테고리 캐시와 기본 상태를 초기화한다.
        private void ClearQueryCaches()
        {
            lock (_queryCacheLock)
            {
                _rowsCacheByKey.Clear();
                _categoryCacheByKey.Clear();
                _totalCountCacheByKey.Clear();
            }

            _activeRowsCacheKey = "all";
            _baseModeBeforeCategory = "all";
            _baseCacheKeyBeforeCategory = "all";
            _baseKeywordBeforeCategory = "";
            _baseStartDateBeforeCategory = "";
            _baseEndDateBeforeCategory = "";
        }

        // 카테고리 카드 숫자 계산용 빈 카운트 맵을 만든다.
        private static Dictionary<string, int> CreateEmptyCountMap()
        {
            return new Dictionary<string, int>
            {
                ["fire"] = 0,
                ["alarm"] = 0,
                ["relay_fault"] = 0,
                ["an_fault"] = 0,
                ["line_fault"] = 0,
                ["output"] = 0,
                ["mcc"] = 0,
                ["other"] = 0
            };
        }

        // 카테고리별 LogRow 목록을 담을 빈 캐시를 만든다.
        private static Dictionary<string, List<LogRow>> CreateEmptyCategoryCache()
        {
            return new Dictionary<string, List<LogRow>>
            {
                ["fire"] = new List<LogRow>(),
                ["alarm"] = new List<LogRow>(),
                ["relay_fault"] = new List<LogRow>(),
                ["an_fault"] = new List<LogRow>(),
                ["line_fault"] = new List<LogRow>(),
                ["output"] = new List<LogRow>(),
                ["mcc"] = new List<LogRow>(),
                ["other"] = new List<LogRow>()
            };
        }

        // 백그라운드에서 만든 카테고리 캐시를 UI 적용용으로 복사한다.
        private static Dictionary<string, List<LogRow>> CloneCategoryCache(Dictionary<string, List<LogRow>> source)
        {
            Dictionary<string, List<LogRow>> clone = CreateEmptyCategoryCache();

            foreach (KeyValuePair<string, List<LogRow>> pair in source)
            {
                if (!clone.ContainsKey(pair.Key))
                {
                    // 알 수 없는 카테고리는 복사하지 않는다.
                    continue;
                }

                clone[pair.Key].AddRange(pair.Value);
            }

            return clone;
        }

        // 카테고리 필터 진입 전의 현재 조회 조건을 저장한다.
        private void SaveBaseQueryState()
        {
            _baseModeBeforeCategory = _currentMode;
            _baseCacheKeyBeforeCategory = _activeRowsCacheKey;
            _baseKeywordBeforeCategory = _currentKeyword;
            _baseStartDateBeforeCategory = _currentStartDate;
            _baseEndDateBeforeCategory = _currentEndDate;
        }

        // 카테고리 필터를 모두 해제했을 때 필터 전 전체/기간/검색 상태로 복원한다.
        private async Task RestoreBaseQueryAfterCategoryClearAsync()
        {
            // SaveBaseQueryState에 저장된 기준 상태를 현재 상태로 되돌린다.
            _currentMode = _baseModeBeforeCategory;
            _activeRowsCacheKey = _baseCacheKeyBeforeCategory;
            _currentKeyword = _baseKeywordBeforeCategory;
            _currentStartDate = _baseStartDateBeforeCategory;
            _currentEndDate = _baseEndDateBeforeCategory;
            _currentPage = 1;

            if (_currentMode == "date_cache")
            {
                // 기간 조회였으면 이미 만든 기간 캐시에서 건수/카테고리 캐시를 복원한다.
                List<LogRow> rows;

                lock (_queryCacheLock)
                {
                    rows = _rowsCacheByKey.TryGetValue(_activeRowsCacheKey, out List<LogRow>? cachedRows)
                        ? cachedRows
                        : new List<LogRow>();

                    if (_categoryCacheByKey.TryGetValue(_activeRowsCacheKey, out Dictionary<string, List<LogRow>>? categoryCache))
                    {
                        ApplyCategoryCacheSnapshot(categoryCache);
                    }
                }

                _totalCount = rows.Count;
                _totalPages = CalculateTotalPages(_totalCount);
                TotalLogCountText.Text = _totalCount.ToString("N0");

                await LoadCurrentPageAsync(showLoading: false);
                return;
            }

            if (_currentMode == "search")
            {
                // 검색 상태였으면 검색 결과 첫 페이지로 돌아간다.
                await LoadCurrentPageAsync(showLoading: false);
                return;
            }

            // 그 외에는 전체 로그 첫 페이지로 돌아간다.
            await LoadAllFirstPageAsync();
        }


        // 카테고리 카드 숫자와 캐시 상태를 초기값으로 되돌린다.
        private void ResetCategoryCountText()
        {
            TotalLogCountText.Text = "0";
            FireCountText.Text = "0";
            AlarmCountText.Text = "0";
            RelayErrorCountText.Text = "0";
            AnErrorCountText.Text = "0";
            LineBreakCountText.Text = "0";
            OutputCountText.Text = "0";
            MccCountText.Text = "0";
            EtcCountText.Text = "0";

            ClearCurrentCategoryCache();

            // 아직 카테고리 캐시가 만들어지지 않은 상태로 표시한다.
            _categoryCacheReady = false;
            _categoryCacheBuilding = false;
        }

        // 전체로그/카테고리 카드의 active 테두리 상태를 갱신한다.
        private void UpdateCategoryCardActiveStates()
        {
            /*
             * 전체 로그 카드 active 기준:
             * - DB가 선택되어 있어야 한다.
             * - 화재/제경보/고장/MCC 같은 카테고리가 선택되지 않은 상태여야 한다.
             * 이력 보기 직후: 전체 로그 active
             * 7일/30일/기간 조회 직후: 전체 로그 active
             * 화재 클릭: 전체 로그 inactive, 화재 active
             * 카테고리 모두 해제: 전체 로그 active 복귀
             */
            bool totalLogActive = _repository != null && _selectedCategories.Count == 0;

            SetCardActive(TotalLogButton, totalLogActive);

            SetCardActive(FireLogButton, _selectedCategories.Contains("fire"));
            SetCardActive(AlarmLogButton, _selectedCategories.Contains("alarm"));
            SetCardActive(RelayErrorLogButton, _selectedCategories.Contains("relay_fault"));
            SetCardActive(AnErrorLogButton, _selectedCategories.Contains("an_fault"));
            SetCardActive(LineBreakLogButton, _selectedCategories.Contains("line_fault"));
            SetCardActive(OutputLogButton, _selectedCategories.Contains("output"));
            SetCardActive(MccLogButton, _selectedCategories.Contains("mcc"));
            SetCardActive(EtcLogButton, _selectedCategories.Contains("other"));
        }

        // 카드 active 여부에 따라 테두리/배경 스타일을 적용한다.
        private void SetCardActive(Border card, bool isActive)
        {
            if (isActive)
            {
                // 선택된 카드는 주황색 두꺼운 테두리로 표시한다.
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 90, 61));
                card.BorderThickness = new Thickness(2);
                card.Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
            }
            else
            {
                // 선택되지 않은 카드는 기본 흰색 테두리로 둔다.
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                card.BorderThickness = new Thickness(1);
                card.Background = new SolidColorBrush(Color.FromArgb(191, 255, 255, 255));
            }
        }

        // 날짜 문자열이 파싱 가능한지 확인한다.
        private static bool IsValidDate(string value)
        {
            return DateTime.TryParse(value, out _);
        }

        // LogRow.DTime이 현재 기간 필터 범위에 들어가는지 확인한다.
        private static bool IsRowInDateRange(
            LogRow row,
            DateTime? filterStart,
            DateTime? filterEnd)
        {
            if (!filterStart.HasValue && !filterEnd.HasValue)
            {
                // 기간 조건이 없으면 모든 로그를 포함한다.
                return true;
            }

            if (!DateTime.TryParse(row.DTime, out DateTime rowDateTime))
            {
                // 날짜 형식이 깨진 로그는 기간 조회에서 제외한다.
                return false;
            }

            if (filterStart.HasValue && rowDateTime < filterStart.Value)
            {
                // 시작일 이전 로그는 제외한다.
                return false;
            }

            if (filterEnd.HasValue && rowDateTime > filterEnd.Value)
            {
                // 종료일 이후 로그는 제외한다.
                return false;
            }

            return true;
        }

        // 최신순 정렬에 사용할 DateTime 값을 만든다.
        private static DateTime ParseSortableDate(string value)
        {
            if (DateTime.TryParse(value, out DateTime result))
            {
                return result;
            }

            // 파싱 안 되는 날짜는 정렬에서 가장 오래된 값으로 보낸다.
            return DateTime.MinValue;
        }

        // 클릭 원본이 특정 UI 요소의 자식인지 확인한다.
        // 드롭다운/검색창 바깥 클릭 판단에 사용한다.
        private static bool IsDescendantOf(DependencyObject? source, DependencyObject target)
        {
            DependencyObject? current = source;

            while (current != null)
            {
                if (ReferenceEquals(current, target))
                {
                    // 자기 자신 또는 부모 중 target을 찾았다.
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        // 현재 진행 중인 렌더링/카운트/로딩/키보드 감시 작업을 모두 취소한다.
        private void CancelRunningJobs()
        {
            _renderCts?.Cancel();
            _countCts?.Cancel();
            _loadCts?.Cancel();
            _touchKeyboardMonitorCts?.Cancel();

            if (PageSizeDropdown != null)
            {
                // 작업 취소 시 열려 있는 페이지 크기 드롭다운도 닫는다.
                PageSizeDropdown.Visibility = Visibility.Collapsed;
            }

            if (DateCalendarDropdown != null)
            {
                // 날짜 달력도 닫아 화면 상태를 정리한다.
                DateCalendarDropdown.Visibility = Visibility.Collapsed;
            }
        }

        // 프로그램 창이 닫힐 때 백그라운드 작업과 Windows 터치 키보드를 정리한다.
        protected override void OnClosed(EventArgs e)
        {
            CancelRunningJobs();
            // 프로그램 종료 시 TabTip/osk 등 터치 키보드 프로세스를 닫는다.
            CloseTouchKeyboardProcesses();
            base.OnClosed(e);
        }
    }
}
