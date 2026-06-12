using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DbViewer.Services.Print
{
    // 인쇄 버튼 클릭 후 사용자에게 보여주는 WPF 인쇄 미리보기 창이다.
    // DocumentViewer 대신 Log_Print_Paginator 결과를 이미지로 렌더링해 보여준다.
    public sealed class Print_Preview_Window : Window
    {
        // A4 세로 비율 미리보기 크기다.
        private static readonly Size PreviewPageSize = new(793.0, 1122.0);
        // 실제 렌더링은 조금 크게 해서 글자가 흐려지지 않게 한다.
        private const double PreviewRenderScale = 1.35;
        // 화면 안에 들어오도록 표시 크기는 줄인다.
        private const double PreviewDisplayZoom = 0.72;

        // Prepare_Print_Document에서 만든 실제 인쇄 대상 로그/조건/요약 데이터다.
        private readonly PrintLogDocumentData _documentData;
        // 미리보기용 고정 A4 크기 paginator다.
        private readonly Log_Print_Paginator _previewPaginator;
        // 현재 페이지를 비트맵으로 렌더링해 보여주는 이미지 컨트롤이다.
        private readonly Image _pageImage;
        private TextBlock _pageText = null!;
        private Button _previousButton = null!;
        private Button _nextButton = null!;

        // 현재 보고 있는 미리보기 페이지 index다. 0부터 시작한다.
        private int _pageIndex = 0;

        // 인쇄 대상 데이터를 받아 미리보기 창 UI를 구성한다.
        public Print_Preview_Window(PrintLogDocumentData documentData)
        {
            _documentData = documentData;
            // 미리보기는 프린터가 없어도 A4 기준으로 먼저 보여준다.
            _previewPaginator = CreatePaginator(PreviewPageSize);

            Title = "프로테크 이력 인쇄 미리보기";
            Width = 1280;
            Height = 900;
            MinWidth = 900;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));

            // 위쪽 툴바와 아래쪽 페이지 미리보기 영역으로 나눈다.
            Grid root = new();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(BuildToolbar());

            // paginator가 만든 페이지 Visual을 bitmap으로 변환해 여기에 표시한다.
            _pageImage = new Image
            {
                Width = PreviewPageSize.Width * PreviewDisplayZoom,
                Height = PreviewPageSize.Height * PreviewDisplayZoom,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            };

            // 흰 A4 용지처럼 보이도록 테두리 프레임을 둔다.
            Border pageFrame = new()
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Margin = new Thickness(20),
                Child = _pageImage
            };

            // 페이지가 창보다 크면 스크롤로 확인한다.
            ScrollViewer previewScroll = new()
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Content = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Children = { pageFrame }
                }
            };
            Grid.SetRow(previewScroll, 1);
            root.Children.Add(previewScroll);

            Content = root;
            // 창이 열린 뒤 첫 페이지를 렌더링한다.
            Loaded += (_, _) => RenderCurrentPage();
        }

        // 상단 툴바를 만든다.
        // 구성: 제목/조회조건/이전/페이지/다음/인쇄/닫기.
        private FrameworkElement BuildToolbar()
        {
            Border shell = new()
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 10, 14, 10)
            };

            Grid toolbar = new();
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            shell.Child = toolbar;

            // 왼쪽에는 인쇄 제목과 현재 조건/건수를 표시한다.
            StackPanel titleStack = new()
            {
                Orientation = Orientation.Vertical
            };
            toolbar.Children.Add(titleStack);

            titleStack.Children.Add(new TextBlock
            {
                Text = "프로테크 이력 인쇄 미리보기",
                FontSize = 20,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            });

            // 예: "기간: 2026-06-01 ~ 2026-06-07 | 1,234건".
            titleStack.Children.Add(new TextBlock
            {
                Text = $"{_documentData.ConditionText}   |   {_documentData.Rows.Count:N0}건",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 3, 0, 0)
            });

            // 이전 페이지 버튼.
            _previousButton = BuildButton("이전");
            _previousButton.Click += (_, _) => MovePage(-1);
            Grid.SetColumn(_previousButton, 1);
            toolbar.Children.Add(_previousButton);

            // 현재 페이지 / 전체 페이지 표시다.
            _pageText = new TextBlock
            {
                Width = 120,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(_pageText, 2);
            toolbar.Children.Add(_pageText);

            // 다음 페이지 버튼.
            _nextButton = BuildButton("다음");
            _nextButton.Click += (_, _) => MovePage(1);
            Grid.SetColumn(_nextButton, 3);
            toolbar.Children.Add(_nextButton);

            // 실제 Windows 인쇄 대화상자를 여는 버튼이다.
            Button printButton = BuildButton("인쇄");
            printButton.Margin = new Thickness(12, 0, 0, 0);
            printButton.Click += (_, _) => PrintDocument();
            Grid.SetColumn(printButton, 4);
            toolbar.Children.Add(printButton);

            // 미리보기 창을 닫는다.
            Button closeButton = BuildButton("닫기");
            closeButton.Margin = new Thickness(8, 0, 0, 0);
            closeButton.Click += (_, _) => Close();
            Grid.SetColumn(closeButton, 5);
            toolbar.Children.Add(closeButton);

            return shell;
        }

        // 툴바 버튼의 공통 스타일을 만든다.
        private static Button BuildButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 82,
                Height = 38,
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(16, 32, 68)),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1)
            };
        }

        // 지정한 페이지 크기로 실제 인쇄 페이지 생성기를 만든다.
        private Log_Print_Paginator CreatePaginator(Size pageSize)
        {
            return new Log_Print_Paginator(
                _documentData.Rows,
                "프로테크 이력",
                _documentData.ConditionText,
                _documentData.SummaryCounts,
                pageSize);
        }

        // 이전/다음 버튼으로 미리보기 페이지를 이동한다.
        private void MovePage(int direction)
        {
            // 이동 대상은 0부터 마지막 페이지 사이로 제한한다.
            int targetPage = Math.Clamp(
                _pageIndex + direction,
                0,
                Math.Max(0, _previewPaginator.PageCount - 1));

            if (targetPage == _pageIndex)
            {
                // 첫 페이지에서 이전, 마지막 페이지에서 다음을 누른 경우다.
                return;
            }

            _pageIndex = targetPage;
            RenderCurrentPage();
        }

        // 현재 페이지를 DrawingVisual에서 Bitmap으로 변환해 화면에 표시한다.
        private void RenderCurrentPage()
        {
            // Paginator의 실제 페이지 Visual을 가져온다.
            DocumentPage page = _previewPaginator.GetPage(_pageIndex);

            // 확대 렌더링으로 글자 선명도를 확보한다.
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(PreviewPageSize.Width * PreviewRenderScale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(PreviewPageSize.Height * PreviewRenderScale));

            RenderTargetBitmap bitmap = new(
                pixelWidth,
                pixelHeight,
                96.0 * PreviewRenderScale,
                96.0 * PreviewRenderScale,
                PixelFormats.Pbgra32);

            bitmap.Render(page.Visual);
            bitmap.Freeze();

            // 화면 이미지와 페이지 표시/버튼 활성 상태를 갱신한다.
            _pageImage.Source = bitmap;
            _pageText.Text = $"{_pageIndex + 1:N0} / {_previewPaginator.PageCount:N0}";
            _previousButton.IsEnabled = _pageIndex > 0;
            _nextButton.IsEnabled = _pageIndex < _previewPaginator.PageCount - 1;
        }

        // Windows 인쇄 대화상자를 열고 사용자가 선택한 프린터/페이지 범위로 출력한다.
        private void PrintDocument()
        {
            try
            {
                // UserPageRangeEnabled=true라서 1~1 같은 페이지 지정 인쇄가 가능하다.
                PrintDialog printDialog = new()
                {
                    UserPageRangeEnabled = true
                };

                if (printDialog.ShowDialog() != true)
                {
                    // 사용자가 인쇄창을 취소한 경우다.
                    return;
                }

                // 실제 프린터의 출력 가능 영역 크기에 맞춰 paginator를 다시 만든다.
                Size pageSize = new(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
                DocumentPaginator paginator = CreatePaginator(pageSize);

                if (printDialog.PageRangeSelection == PageRangeSelection.UserPages)
                {
                    // 예: 사용자가 1페이지만 지정하면 Page_Range_Print_Paginator가 해당 페이지만 넘긴다.
                    paginator = new Page_Range_Print_Paginator(
                        paginator,
                        printDialog.PageRange.PageFrom,
                        printDialog.PageRange.PageTo);
                }

                // 실제 인쇄 작업을 Windows 인쇄 큐로 보낸다.
                printDialog.PrintDocument(paginator, "프로테크 이력");

                MessageBox.Show(
                    this,
                    "인쇄 작업을 보냈습니다.",
                    "인쇄",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // 프린터/드라이버/PDF 저장 실패 등은 사용자에게 상세 예외명과 함께 표시한다.
                MessageBox.Show(
                    this,
                    "이력 인쇄에 실패했습니다.\n\n" +
                    $"오류: {BuildPrintErrorMessage(ex)}",
                    "인쇄",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // 사용자에게 보여줄 인쇄 오류 메시지를 만든다.
        private static string BuildPrintErrorMessage(Exception ex)
        {
            if (ex.InnerException == null)
            {
                return $"{ex.GetType().Name}: {ex.Message}";
            }

            return $"{ex.GetType().Name}: {ex.Message}\n" +
                   $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        }
    }
}
