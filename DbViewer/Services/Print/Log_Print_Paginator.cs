using DbViewer.Models;
using DbViewer.Services.Common;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DbViewer.Services.Print
{
    public sealed class Log_Print_Paginator : DocumentPaginator
    {
        private const double PageFallbackWidth = 793.0;
        private const double PageFallbackHeight = 1122.0;
        private const double TableHeaderHeight = 22.0;
        private const double TableRowHeight = 19.0;
        private const double HeaderTableSpacing = 8.0;
        private const double ContinuationHeaderHeight = 26.0;
        private const double ContinuationHeaderSpacing = 6.0;
        private const double ReportHeaderTopLineHeight = 5.0;
        private const double ReportHeaderPaddingX = 10.0;
        private const double ReportHeaderPaddingTop = 8.0;
        private const double ReportHeaderPaddingBottom = 8.0;
        private const double ReportTitleHeight = 36.0;
        private const double ReportTitleSummaryGap = 6.0;
        private const double SummaryCardHeight = 36.0;
        private const double SummaryCardGap = 4.0;
        private const double CellHorizontalPadding = 4.0;
        private const double PixelsPerDip = 1.0;

        private static readonly FontFamily PrintFontFamily = new("Malgun Gothic");
        private static readonly Brush WhiteBrush = ColorBrush("#FFFFFF");
        private static readonly Brush PageTextBrush = ColorBrush("#111827");
        private static readonly Brush TitleBrush = ColorBrush("#0F172A");
        private static readonly Brush SubtleTextBrush = ColorBrush("#334155");
        private static readonly Brush MutedTextBrush = ColorBrush("#64748B");
        private static readonly Brush DarkHeaderBrush = ColorBrush("#1E293B");
        private static readonly Brush BorderBrush = ColorBrush("#CBD5E1");
        private static readonly Brush TableOuterBorderBrush = ColorBrush("#94A3B8");
        private static readonly Brush StripeBrush = ColorBrush("#F8FAFC");
        private static readonly Brush FireFillBrush = ColorBrush("#FEF2F2");
        private static readonly Brush FireSoftFillBrush = ColorBrush("#FFF1F2");
        private static readonly Brush AlarmFillBrush = ColorBrush("#EFF6FF");
        private static readonly Brush LineFaultFillBrush = ColorBrush("#FEFCE8");
        private static readonly Brush FaultFillBrush = ColorBrush("#FFF7ED");
        private static readonly Brush ReceiverRelayFillBrush = ColorBrush("#FAF5FF");
        private static readonly Brush PanelFillBrush = ColorBrush("#ECFEFF");
        private static readonly Brush RecoverFillBrush = ColorBrush("#F0FDF4");
        private static readonly Brush FireTextBrush = ColorBrush("#7F1D1D");
        private static readonly Brush AlarmTextBrush = ColorBrush("#1D4ED8");
        private static readonly Brush LineFaultTextBrush = ColorBrush("#854D0E");
        private static readonly Brush FaultTextBrush = ColorBrush("#7C2D12");
        private static readonly Brush ReceiverRelayTextBrush = ColorBrush("#6B21A8");
        private static readonly Brush PanelTextBrush = ColorBrush("#155E75");
        private static readonly Brush RecoverTextBrush = ColorBrush("#14532D");

        private static readonly Pen BorderPen = FrozenPen(BorderBrush, 1);
        private static readonly Pen TableOuterBorderPen = FrozenPen(TableOuterBorderBrush, 1);

        private static readonly SummaryCategory[] SummaryCategories =
        {
            new() { Key = "fire", Label = "화재", Color = "#DC2626" },
            new() { Key = "alarm", Label = "제경보", Color = "#2563EB" },
            new() { Key = "relay_fault", Label = "중계기 고장", Color = "#7C3AED" },
            new() { Key = "an_fault", Label = "AN고장", Color = "#EA580C" },
            new() { Key = "line_fault", Label = "단선", Color = "#A16207" },
            new() { Key = "output", Label = "출력", Color = "#16A34A" },
            new() { Key = "mcc", Label = "MCC", Color = "#334155" }
        };

        private readonly IReadOnlyList<LogRow> _rows;
        private readonly string _title;
        private readonly string _conditionText;
        private readonly Dictionary<string, int> _summaryCounts;
        private readonly DateTime _generatedAt;
        private readonly Thickness _margin;

        private Size _pageSize;
        private int _rowsPerFirstPage;
        private int _rowsPerPage;
        private int _pageCount;
        private double _reportHeaderHeight;

        public Log_Print_Paginator(
            IReadOnlyList<LogRow> rows,
            string title,
            string conditionText,
            Dictionary<string, int> summaryCounts,
            Size pageSize)
        {
            _rows = rows;
            _title = title;
            _conditionText = conditionText;
            _summaryCounts = summaryCounts;
            _generatedAt = DateTime.Now;
            _margin = new Thickness(18);
            PageSize = pageSize;
        }

        public override bool IsPageCountValid => true;

        public override int PageCount => _pageCount;

        public override Size PageSize
        {
            get => _pageSize;
            set
            {
                _pageSize = NormalizePageSize(value);
                RecalculatePageMetrics();
            }
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= _pageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber));
            }

            DrawingVisual visual = new();

            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(WhiteBrush, null, new Rect(0, 0, PageSize.Width, PageSize.Height));

                double x = _margin.Left;
                double y = _margin.Top;
                double contentWidth = Math.Max(320, PageSize.Width - _margin.Left - _margin.Right);

                if (pageNumber == 0)
                {
                    DrawReportHeader(dc, new Rect(x, y, contentWidth, _reportHeaderHeight));
                    y += _reportHeaderHeight + HeaderTableSpacing;
                }
                else
                {
                    DrawContinuationHeader(dc, pageNumber, new Rect(x, y, contentWidth, ContinuationHeaderHeight));
                    y += ContinuationHeaderHeight + ContinuationHeaderSpacing;
                }

                DrawLogTable(dc, pageNumber, x, y, contentWidth);
            }

            Rect pageRect = new(new Point(0, 0), PageSize);
            Rect contentRect = new(
                _margin.Left,
                _margin.Top,
                Math.Max(0, PageSize.Width - _margin.Left - _margin.Right),
                Math.Max(0, PageSize.Height - _margin.Top - _margin.Bottom));

            return new DocumentPage(visual, PageSize, pageRect, contentRect);
        }

        private void DrawReportHeader(DrawingContext dc, Rect headerRect)
        {
            dc.DrawRoundedRectangle(WhiteBrush, BorderPen, headerRect, 0, 0);
            dc.DrawRectangle(DarkHeaderBrush, null, new Rect(headerRect.Left, headerRect.Top, headerRect.Width, ReportHeaderTopLineHeight));

            double bodyLeft = headerRect.Left + ReportHeaderPaddingX;
            double bodyTop = headerRect.Top + ReportHeaderTopLineHeight + ReportHeaderPaddingTop;
            double bodyWidth = Math.Max(0, headerRect.Width - (ReportHeaderPaddingX * 2));

            Rect titleRect = new(bodyLeft, bodyTop, bodyWidth, ReportTitleHeight);
            DrawText(dc, _title, titleRect, TitleBrush, 26, FontWeights.Black, TextAlignment.Left);

            double summaryTop = titleRect.Bottom + ReportTitleSummaryGap;
            double summaryWidth = bodyWidth;
            double cardWidth = (summaryWidth - (SummaryCardGap * 2)) / 3.0;
            double cardX = bodyLeft;
            double cardY = summaryTop;
            int cardIndex = 0;

            DrawSummaryCard(
                dc,
                new Rect(cardX, cardY, cardWidth, SummaryCardHeight),
                "조회 조건",
                _conditionText);
            AdvanceSummaryPosition(ref cardIndex, ref cardX, ref cardY, bodyLeft, cardWidth);

            DrawSummaryCard(
                dc,
                new Rect(cardX, cardY, cardWidth, SummaryCardHeight),
                "전체 건수",
                $"{_rows.Count:N0}건");
            AdvanceSummaryPosition(ref cardIndex, ref cardX, ref cardY, bodyLeft, cardWidth);

            foreach (SummaryCategory category in SummaryCategories)
            {
                int count = _summaryCounts.TryGetValue(category.Key, out int value)
                    ? value
                    : 0;

                if (count <= 0)
                {
                    continue;
                }

                DrawSummaryCard(
                    dc,
                    new Rect(cardX, cardY, cardWidth, SummaryCardHeight),
                    category.Label,
                    $"{count:N0}건",
                    ColorBrush(category.Color));
                AdvanceSummaryPosition(ref cardIndex, ref cardX, ref cardY, bodyLeft, cardWidth);
            }
        }

        private void DrawContinuationHeader(DrawingContext dc, int pageNumber, Rect headerRect)
        {
            dc.DrawRectangle(StripeBrush, BorderPen, headerRect);

            DrawText(
                dc,
                _title,
                new Rect(headerRect.Left + 8, headerRect.Top, Math.Max(0, headerRect.Width - 170), headerRect.Height),
                SubtleTextBrush,
                11,
                FontWeights.ExtraBold,
                TextAlignment.Left);

            DrawText(
                dc,
                $"{pageNumber + 1:N0} / {_pageCount:N0} 페이지",
                new Rect(headerRect.Right - 150, headerRect.Top, 142, headerRect.Height),
                SubtleTextBrush,
                9,
                FontWeights.Bold,
                TextAlignment.Right);
        }

        private static void AdvanceSummaryPosition(
            ref int cardIndex,
            ref double cardX,
            ref double cardY,
            double startX,
            double cardWidth)
        {
            cardIndex++;

            if (cardIndex % 3 == 0)
            {
                cardX = startX;
                cardY += SummaryCardHeight + SummaryCardGap;
                return;
            }

            cardX += cardWidth + SummaryCardGap;
        }

        private static void DrawSummaryCard(
            DrawingContext dc,
            Rect rect,
            string label,
            string value,
            Brush? categoryBrush = null)
        {
            dc.DrawRoundedRectangle(WhiteBrush, BorderPen, rect, 4, 4);

            double labelLeft = rect.Left + 6;

            if (categoryBrush != null)
            {
                dc.DrawRoundedRectangle(
                    categoryBrush,
                    null,
                    new Rect(rect.Left + 6, rect.Top + 8, 7, 7),
                    1,
                    1);
                labelLeft += 11;
            }

            DrawText(
                dc,
                label,
                new Rect(labelLeft, rect.Top + 3, Math.Max(0, rect.Right - labelLeft - 6), 13),
                MutedTextBrush,
                8.5,
                FontWeights.ExtraBold,
                TextAlignment.Left);

            DrawText(
                dc,
                value,
                new Rect(rect.Left + 6, rect.Top + 17, rect.Width - 12, 15),
                TitleBrush,
                9,
                FontWeights.Black,
                TextAlignment.Left);
        }

        private void DrawLogTable(DrawingContext dc, int pageNumber, double x, double y, double width)
        {
            double[] columnWidths = GetColumnWidths(width);
            double tableHeight = TableHeaderHeight;
            int startIndex = GetStartIndexForPage(pageNumber);
            int rowCount = GetRowCountForPage(pageNumber, startIndex);
            tableHeight += rowCount * TableRowHeight;

            dc.DrawRectangle(WhiteBrush, TableOuterBorderPen, new Rect(x, y, width, tableHeight));
            DrawTableHeader(dc, x, y, columnWidths);

            double rowY = y + TableHeaderHeight;

            for (int offset = 0; offset < rowCount; offset++)
            {
                int rowIndex = startIndex + offset;
                DrawDataRow(dc, _rows[rowIndex], rowIndex, x, rowY, columnWidths);
                rowY += TableRowHeight;
            }
        }

        private static void DrawTableHeader(DrawingContext dc, double x, double y, IReadOnlyList<double> columnWidths)
        {
            string[] labels = { "발생시간", "구분", "상태", "위치", "내용" };
            double cellX = x;

            for (int column = 0; column < columnWidths.Count; column++)
            {
                Rect rect = new(cellX, y, columnWidths[column], TableHeaderHeight);
                dc.DrawRectangle(DarkHeaderBrush, BorderPen, rect);
                DrawText(dc, labels[column], Deflate(rect, CellHorizontalPadding, 0), WhiteBrush, 8.6, FontWeights.Black, TextAlignment.Center);
                cellX += columnWidths[column];
            }
        }

        private static void DrawDataRow(
            DrawingContext dc,
            LogRow row,
            int rowIndex,
            double x,
            double y,
            IReadOnlyList<double> columnWidths)
        {
            string rowTag = LogClassifier.ClassifyRowTag(row, rowIndex);
            Brush rowFill = GetPrintRowFill(rowTag, rowIndex);
            Brush rowText = GetPrintRowText(rowTag);
            bool bold = IsPrintBold(rowTag);

            string[] values =
            {
                row.DTime ?? "",
                row.Type ?? "",
                row.Action ?? "",
                row.Section ?? "",
                row.Contents ?? ""
            };

            double cellX = x;

            for (int column = 0; column < columnWidths.Count; column++)
            {
                Rect rect = new(cellX, y, columnWidths[column], TableRowHeight);
                dc.DrawRectangle(rowFill, BorderPen, rect);

                if (column == 4)
                {
                    DrawContentCell(
                        dc,
                        Deflate(rect, CellHorizontalPadding, 0),
                        values[column],
                        LogClassifier.GetBadgeKeys(row, rowTag),
                        rowText,
                        bold);
                }
                else
                {
                    TextAlignment alignment = column <= 2
                        ? TextAlignment.Center
                        : TextAlignment.Left;

                    DrawText(
                        dc,
                        values[column],
                        Deflate(rect, CellHorizontalPadding, 0),
                        rowText,
                        8.3,
                        bold ? FontWeights.ExtraBold : FontWeights.Normal,
                        alignment);
                }

                cellX += columnWidths[column];
            }
        }

        private static void DrawContentCell(
            DrawingContext dc,
            Rect rect,
            string value,
            IReadOnlyList<string> badgeKeys,
            Brush textBrush,
            bool bold)
        {
            double contentX = rect.Left;

            foreach (string badgeKey in badgeKeys)
            {
                BadgeStyle? badge = LogStyleMapper.GetBadge(badgeKey);

                if (badge == null)
                {
                    continue;
                }

                double badgeWidth = Math.Max(28, badge.Label.Length * 5 + 10);
                Rect badgeRect = new(contentX, rect.Top + ((rect.Height - 12) / 2), badgeWidth, 12);
                Pen badgeBorderPen = FrozenPen(badge.Border, 1);

                dc.DrawRoundedRectangle(badge.Fill, badgeBorderPen, badgeRect, 3, 3);
                DrawText(
                    dc,
                    badge.Label,
                    Deflate(badgeRect, 3, 0),
                    badge.Text,
                    7,
                    FontWeights.Black,
                    TextAlignment.Center);

                contentX += badgeWidth + 4;

                if (contentX >= rect.Right)
                {
                    return;
                }
            }

            DrawText(
                dc,
                value ?? "",
                new Rect(contentX, rect.Top, Math.Max(0, rect.Right - contentX), rect.Height),
                textBrush,
                8.3,
                bold ? FontWeights.ExtraBold : FontWeights.Normal,
                TextAlignment.Left);
        }

        private void RecalculatePageMetrics()
        {
            int summaryCardCount = 2 + SummaryCategories.Count(category =>
                _summaryCounts.TryGetValue(category.Key, out int count) && count > 0);
            int summaryRows = Math.Max(1, (int)Math.Ceiling(summaryCardCount / 3.0));
            _reportHeaderHeight = ReportHeaderTopLineHeight +
                                  ReportHeaderPaddingTop +
                                  ReportTitleHeight +
                                  ReportTitleSummaryGap +
                                  summaryRows * SummaryCardHeight +
                                  Math.Max(0, summaryRows - 1) * SummaryCardGap +
                                  ReportHeaderPaddingBottom;

            double contentHeight = Math.Max(320, PageSize.Height - _margin.Top - _margin.Bottom);
            double firstPageAvailableRowsHeight =
                contentHeight - _reportHeaderHeight - HeaderTableSpacing - TableHeaderHeight;
            double followingPageAvailableRowsHeight =
                contentHeight - ContinuationHeaderHeight - ContinuationHeaderSpacing - TableHeaderHeight;

            _rowsPerFirstPage = Math.Max(1, (int)Math.Floor(firstPageAvailableRowsHeight / TableRowHeight));
            _rowsPerPage = Math.Max(1, (int)Math.Floor(followingPageAvailableRowsHeight / TableRowHeight));

            if (_rows.Count <= _rowsPerFirstPage)
            {
                _pageCount = 1;
                return;
            }

            int remainingRows = _rows.Count - _rowsPerFirstPage;
            _pageCount = 1 + (int)Math.Ceiling(remainingRows / (double)_rowsPerPage);
        }

        private int GetStartIndexForPage(int pageNumber)
        {
            if (pageNumber == 0)
            {
                return 0;
            }

            return _rowsPerFirstPage + ((pageNumber - 1) * _rowsPerPage);
        }

        private int GetRowCountForPage(int pageNumber, int startIndex)
        {
            if (startIndex >= _rows.Count)
            {
                return 0;
            }

            int maxRowsForPage = pageNumber == 0
                ? _rowsPerFirstPage
                : _rowsPerPage;

            return Math.Min(maxRowsForPage, _rows.Count - startIndex);
        }

        private static double[] GetColumnWidths(double tableWidth)
        {
            const double timeWidth = 104.0;
            const double typeWidth = 58.0;
            const double actionWidth = 44.0;
            const double sectionWidth = 185.0;
            const double minContentWidth = 90.0;

            double fixedWidth = timeWidth + typeWidth + actionWidth + sectionWidth;

            if (tableWidth >= fixedWidth + minContentWidth)
            {
                return new[]
                {
                    timeWidth,
                    typeWidth,
                    actionWidth,
                    sectionWidth,
                    tableWidth - fixedWidth
                };
            }

            double availableFixedWidth = Math.Max(160.0, tableWidth - minContentWidth);
            double scale = availableFixedWidth / fixedWidth;
            double scaledTime = timeWidth * scale;
            double scaledType = typeWidth * scale;
            double scaledAction = actionWidth * scale;
            double scaledSection = sectionWidth * scale;
            double scaledContent = Math.Max(60.0, tableWidth - scaledTime - scaledType - scaledAction - scaledSection);

            return new[]
            {
                scaledTime,
                scaledType,
                scaledAction,
                scaledSection,
                scaledContent
            };
        }

        private static Brush GetPrintRowFill(string rowTag, int rowIndex)
        {
            return rowTag switch
            {
                "even_row" => WhiteBrush,
                "odd_row" => StripeBrush,
                "fire_row" => FireFillBrush,
                "fire_soft_row" => FireSoftFillBrush,
                "alarm_row" => AlarmFillBrush,
                "line_fault_row" => LineFaultFillBrush,
                "fault_row" or "fault_soft_row" => FaultFillBrush,
                "receiver_fault_row" or "relay_fault_row" => ReceiverRelayFillBrush,
                "panel_fault_row" => PanelFillBrush,
                "recover_row" => RecoverFillBrush,
                _ => WhiteBrush
            };
        }

        private static Brush GetPrintRowText(string rowTag)
        {
            if (rowTag.StartsWith("fire", StringComparison.Ordinal))
            {
                return FireTextBrush;
            }

            if (rowTag.StartsWith("alarm", StringComparison.Ordinal))
            {
                return AlarmTextBrush;
            }

            if (rowTag.StartsWith("line_fault", StringComparison.Ordinal))
            {
                return LineFaultTextBrush;
            }

            if (rowTag.StartsWith("fault", StringComparison.Ordinal))
            {
                return FaultTextBrush;
            }

            if (rowTag.StartsWith("receiver_fault", StringComparison.Ordinal) ||
                rowTag.StartsWith("relay_fault", StringComparison.Ordinal))
            {
                return ReceiverRelayTextBrush;
            }

            if (rowTag.StartsWith("panel_fault", StringComparison.Ordinal))
            {
                return PanelTextBrush;
            }

            if (rowTag == "recover_row")
            {
                return RecoverTextBrush;
            }

            return PageTextBrush;
        }

        private static bool IsPrintBold(string rowTag)
        {
            return rowTag is not "even_row" and not "odd_row" &&
                   !rowTag.EndsWith("_badge", StringComparison.Ordinal);
        }

        private static void DrawText(
            DrawingContext dc,
            string? text,
            Rect rect,
            Brush brush,
            double fontSize,
            FontWeight fontWeight,
            TextAlignment textAlignment)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            FormattedText formattedText = new(
                text ?? "",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(PrintFontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal),
                fontSize,
                brush,
                PixelsPerDip)
            {
                MaxTextWidth = rect.Width,
                MaxTextHeight = rect.Height,
                MaxLineCount = 1,
                TextAlignment = textAlignment,
                Trimming = TextTrimming.CharacterEllipsis
            };

            double y = rect.Top + Math.Max(0, (rect.Height - formattedText.Height) / 2.0);
            dc.DrawText(formattedText, new Point(rect.Left, y));
        }

        private static Rect Deflate(Rect rect, double horizontal, double vertical)
        {
            return new Rect(
                rect.Left + horizontal,
                rect.Top + vertical,
                Math.Max(0, rect.Width - (horizontal * 2)),
                Math.Max(0, rect.Height - (vertical * 2)));
        }

        private static Size NormalizePageSize(Size pageSize)
        {
            double width = double.IsFinite(pageSize.Width) && pageSize.Width > 0
                ? pageSize.Width
                : PageFallbackWidth;
            double height = double.IsFinite(pageSize.Height) && pageSize.Height > 0
                ? pageSize.Height
                : PageFallbackHeight;

            return new Size(width, height);
        }

        private static SolidColorBrush ColorBrush(string hex)
        {
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Brush brush, double thickness)
        {
            Pen pen = new(brush, thickness);
            pen.Freeze();
            return pen;
        }

        private sealed class SummaryCategory
        {
            public string Key { get; init; } = "";
            public string Label { get; init; } = "";
            public string Color { get; init; } = "";
        }
    }
}
