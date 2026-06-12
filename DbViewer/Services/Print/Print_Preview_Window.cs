using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DbViewer.Services.Print
{
    public sealed class Print_Preview_Window : Window
    {
        private static readonly Size PreviewPageSize = new(793.0, 1122.0);
        private const double PreviewRenderScale = 1.35;
        private const double PreviewDisplayZoom = 0.72;

        private readonly PrintLogDocumentData _documentData;
        private readonly Log_Print_Paginator _previewPaginator;
        private readonly Image _pageImage;
        private TextBlock _pageText = null!;
        private Button _previousButton = null!;
        private Button _nextButton = null!;

        private int _pageIndex = 0;

        public Print_Preview_Window(PrintLogDocumentData documentData)
        {
            _documentData = documentData;
            _previewPaginator = CreatePaginator(PreviewPageSize);

            Title = "프로테크 이력 인쇄 미리보기";
            Width = 1280;
            Height = 900;
            MinWidth = 900;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));

            Grid root = new();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(BuildToolbar());

            _pageImage = new Image
            {
                Width = PreviewPageSize.Width * PreviewDisplayZoom,
                Height = PreviewPageSize.Height * PreviewDisplayZoom,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            };

            Border pageFrame = new()
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Margin = new Thickness(20),
                Child = _pageImage
            };

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
            Loaded += (_, _) => RenderCurrentPage();
        }

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

            titleStack.Children.Add(new TextBlock
            {
                Text = $"{_documentData.ConditionText}   |   {_documentData.Rows.Count:N0}건",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 3, 0, 0)
            });

            _previousButton = BuildButton("이전");
            _previousButton.Click += (_, _) => MovePage(-1);
            Grid.SetColumn(_previousButton, 1);
            toolbar.Children.Add(_previousButton);

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

            _nextButton = BuildButton("다음");
            _nextButton.Click += (_, _) => MovePage(1);
            Grid.SetColumn(_nextButton, 3);
            toolbar.Children.Add(_nextButton);

            Button printButton = BuildButton("인쇄");
            printButton.Margin = new Thickness(12, 0, 0, 0);
            printButton.Click += (_, _) => PrintDocument();
            Grid.SetColumn(printButton, 4);
            toolbar.Children.Add(printButton);

            Button closeButton = BuildButton("닫기");
            closeButton.Margin = new Thickness(8, 0, 0, 0);
            closeButton.Click += (_, _) => Close();
            Grid.SetColumn(closeButton, 5);
            toolbar.Children.Add(closeButton);

            return shell;
        }

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

        private Log_Print_Paginator CreatePaginator(Size pageSize)
        {
            return new Log_Print_Paginator(
                _documentData.Rows,
                "프로테크 이력",
                _documentData.ConditionText,
                _documentData.SummaryCounts,
                pageSize);
        }

        private void MovePage(int direction)
        {
            int targetPage = Math.Clamp(
                _pageIndex + direction,
                0,
                Math.Max(0, _previewPaginator.PageCount - 1));

            if (targetPage == _pageIndex)
            {
                return;
            }

            _pageIndex = targetPage;
            RenderCurrentPage();
        }

        private void RenderCurrentPage()
        {
            DocumentPage page = _previewPaginator.GetPage(_pageIndex);

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

            _pageImage.Source = bitmap;
            _pageText.Text = $"{_pageIndex + 1:N0} / {_previewPaginator.PageCount:N0}";
            _previousButton.IsEnabled = _pageIndex > 0;
            _nextButton.IsEnabled = _pageIndex < _previewPaginator.PageCount - 1;
        }

        private void PrintDocument()
        {
            try
            {
                PrintDialog printDialog = new()
                {
                    UserPageRangeEnabled = true
                };

                if (printDialog.ShowDialog() != true)
                {
                    return;
                }

                Size pageSize = new(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
                DocumentPaginator paginator = CreatePaginator(pageSize);

                if (printDialog.PageRangeSelection == PageRangeSelection.UserPages)
                {
                    paginator = new Page_Range_Print_Paginator(
                        paginator,
                        printDialog.PageRange.PageFrom,
                        printDialog.PageRange.PageTo);
                }

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
                MessageBox.Show(
                    this,
                    "이력 인쇄에 실패했습니다.\n\n" +
                    $"오류: {BuildPrintErrorMessage(ex)}",
                    "인쇄",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

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
