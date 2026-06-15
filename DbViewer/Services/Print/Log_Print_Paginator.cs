using DbViewer.Models;
using DbViewer.Services.Common;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DbViewer.Services.Print
{
    // WPF 직접 인쇄용 페이지 생성기다.
    // 실제 LogRow 목록을 A4 세로 문서의 제목/요약/로그 테이블로 그린다.
    public sealed class Log_Print_Paginator : DocumentPaginator
    {
        // 프린터가 유효한 용지 크기를 주지 않을 때 쓰는 A4 세로 fallback 크기다.
        private const double PageFallbackWidth = 793.0;
        private const double PageFallbackHeight = 1122.0;
        // 인쇄 테이블 헤더/행/여백 치수다.
        private const double TableHeaderHeight = 22.0;
        private const double TableRowHeight = 23.0;
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
        private const double LogRowFontSize = 10.0;
        private const double LogBadgeFontSize = 8.4;
        private const double LogBadgeHeight = 14.5;
        private const double PixelsPerDip = 1.0;

        // 현장 한글 로그가 깨지지 않도록 맑은 고딕을 기본 인쇄 폰트로 쓴다.
        private static readonly FontFamily PrintFontFamily = new("Malgun Gothic");
        // 인쇄 색상표다. HTML 인쇄 디자인과 같은 계열로 맞춘다.
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

        // 반복해서 쓰는 선은 Freeze된 Pen으로 만들어 렌더링 비용을 줄인다.
        private static readonly Pen BorderPen = FrozenPen(BorderBrush, 1);
        private static readonly Pen TableOuterBorderPen = FrozenPen(TableOuterBorderBrush, 1);

        // 인쇄 요약 카드에 표시할 카테고리 순서와 색상이다.
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

        // 실제 인쇄할 로그 목록이다.
        private readonly IReadOnlyList<LogRow> _rows;
        // 보고서 제목. 예: 프로테크 이력.
        private readonly string _title;
        // 조회 조건 문구. 예: 전체 이력, 기간: 2026-06-01 ~ 2026-06-07.
        private readonly string _conditionText;
        // 화재/제경보/중계기고장 등 요약 건수다.
        private readonly Dictionary<string, int> _summaryCounts;
        private readonly DateTime _generatedAt;
        // 페이지 바깥 여백이다.
        private readonly Thickness _margin;

        private Size _pageSize;
        // 첫 페이지는 제목/요약 헤더가 있어 들어가는 로그 수가 적다.
        private int _rowsPerFirstPage;
        // 두 번째 페이지부터는 연속 헤더만 있어 더 많은 로그가 들어간다.
        private int _rowsPerPage;
        private int _pageCount;
        private double _reportHeaderHeight;

        // 인쇄 대상 로그와 조건/요약 정보를 받아 paginator를 만든다.
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
            // PageSize setter에서 프린터 용지 크기 보정과 페이지 수 계산을 같이 한다.
            PageSize = pageSize;
        }

        public override bool IsPageCountValid => true;

        public override int PageCount => _pageCount;

        public override Size PageSize
        {
            get => _pageSize;
            set
            {
                // 프린터가 0 또는 NaN 크기를 주면 A4 fallback으로 보정한다.
                _pageSize = NormalizePageSize(value);
                // 용지 크기가 바뀌면 페이지당 행 수와 전체 페이지 수도 다시 계산한다.
                RecalculatePageMetrics();
            }
        }

        public override IDocumentPaginatorSource? Source => null;

        // 지정 페이지 번호의 실제 인쇄 Visual을 만든다.
        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= _pageCount)
            {
                // 인쇄 시스템이 범위 밖 페이지를 요청하면 명확히 예외 처리한다.
                throw new ArgumentOutOfRangeException(nameof(pageNumber));
            }

            DrawingVisual visual = new();

            using (DrawingContext dc = visual.RenderOpen())
            {
                // 페이지 전체 배경을 흰색으로 채운다.
                dc.DrawRectangle(WhiteBrush, null, new Rect(0, 0, PageSize.Width, PageSize.Height));

                double x = _margin.Left;
                double y = _margin.Top;
                double contentWidth = Math.Max(320, PageSize.Width - _margin.Left - _margin.Right);

                if (pageNumber == 0)
                {
                    // 첫 페이지에는 제목/조회 조건/요약 카드가 들어간다.
                    DrawReportHeader(dc, new Rect(x, y, contentWidth, _reportHeaderHeight));
                    y += _reportHeaderHeight + HeaderTableSpacing;
                }
                else
                {
                    // 두 번째 페이지부터는 간단한 연속 페이지 헤더만 넣는다.
                    DrawContinuationHeader(dc, pageNumber, new Rect(x, y, contentWidth, ContinuationHeaderHeight));
                    y += ContinuationHeaderHeight + ContinuationHeaderSpacing;
                }

                // 현재 페이지에 해당하는 로그 테이블을 그린다.
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

        // 첫 페이지 상단의 제목/조회 조건/요약 카드 영역을 그린다.
        private void DrawReportHeader(DrawingContext dc, Rect headerRect)
        {
            // 보고서 박스와 상단 진한 라인을 그린다.
            dc.DrawRoundedRectangle(WhiteBrush, BorderPen, headerRect, 0, 0);
            dc.DrawRectangle(DarkHeaderBrush, null, new Rect(headerRect.Left, headerRect.Top, headerRect.Width, ReportHeaderTopLineHeight));

            double bodyLeft = headerRect.Left + ReportHeaderPaddingX;
            double bodyTop = headerRect.Top + ReportHeaderTopLineHeight + ReportHeaderPaddingTop;
            double bodyWidth = Math.Max(0, headerRect.Width - (ReportHeaderPaddingX * 2));

            Rect titleRect = new(bodyLeft, bodyTop, bodyWidth, ReportTitleHeight);
            // 예: 프로테크 이력.
            DrawText(dc, _title, titleRect, TitleBrush, 26, FontWeights.Black, TextAlignment.Left);

            double summaryTop = titleRect.Bottom + ReportTitleSummaryGap;
            double summaryWidth = bodyWidth;
            double cardWidth = (summaryWidth - (SummaryCardGap * 2)) / 3.0;
            double cardX = bodyLeft;
            double cardY = summaryTop;
            int cardIndex = 0;

            // 조회 조건 카드. 예: 기간: 2026-06-01 ~ 2026-06-07.
            DrawSummaryCard(
                dc,
                new Rect(cardX, cardY, cardWidth, SummaryCardHeight),
                "조회 조건",
                _conditionText);
            AdvanceSummaryPosition(ref cardIndex, ref cardX, ref cardY, bodyLeft, cardWidth);

            // 전체 건수 카드. 예: 1,234건.
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
                    // 현재 출력 조건에 없는 카테고리는 요약 카드에서 숨긴다.
                    continue;
                }

                // 예: 화재 3건, 중계기 고장 12건.
                DrawSummaryCard(
                    dc,
                    new Rect(cardX, cardY, cardWidth, SummaryCardHeight),
                    category.Label,
                    $"{count:N0}건",
                    ColorBrush(category.Color));
                AdvanceSummaryPosition(ref cardIndex, ref cardX, ref cardY, bodyLeft, cardWidth);
            }
        }

        // 2페이지 이후 상단에 현재 문서 제목과 페이지 번호를 그린다.
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

        // 요약 카드를 3열 그리드 위치로 한 칸 이동한다.
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
                // 세 번째 카드를 그린 뒤 다음 줄 첫 칸으로 이동한다.
                cardX = startX;
                cardY += SummaryCardHeight + SummaryCardGap;
                return;
            }

            // 같은 줄의 다음 칸으로 이동한다.
            cardX += cardWidth + SummaryCardGap;
        }

        // 조회 조건/전체 건수/카테고리 건수 요약 카드를 그린다.
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
                // 카테고리 카드면 왼쪽에 색상 박스를 표시한다.
                dc.DrawRoundedRectangle(
                    categoryBrush,
                    null,
                    new Rect(rect.Left + 6, rect.Top + 8, 7, 7),
                    1,
                    1);
                labelLeft += 11;
            }

            // 카드 라벨. 예: 조회 조건, 전체 건수, 화재.
            DrawText(
                dc,
                label,
                new Rect(labelLeft, rect.Top + 3, Math.Max(0, rect.Right - labelLeft - 6), 13),
                MutedTextBrush,
                8.5,
                FontWeights.ExtraBold,
                TextAlignment.Left);

            // 카드 값. 예: 전체 이력, 300,000건.
            DrawText(
                dc,
                value,
                new Rect(rect.Left + 6, rect.Top + 17, rect.Width - 12, 15),
                TitleBrush,
                9,
                FontWeights.Black,
                TextAlignment.Left);
        }

        // 현재 페이지의 로그 테이블 전체를 그린다.
        private void DrawLogTable(DrawingContext dc, int pageNumber, double x, double y, double width)
        {
            // 용지 폭에 맞춰 발생시간/구분/상태/위치/내용 컬럼 폭을 계산한다.
            double[] columnWidths = GetColumnWidths(width);
            double tableHeight = TableHeaderHeight;
            // 현재 페이지가 전체 로그 목록에서 시작하는 index다.
            int startIndex = GetStartIndexForPage(pageNumber);
            // 현재 페이지에 실제로 들어갈 로그 행 수다.
            int rowCount = GetRowCountForPage(pageNumber, startIndex);
            tableHeight += rowCount * TableRowHeight;

            dc.DrawRectangle(WhiteBrush, TableOuterBorderPen, new Rect(x, y, width, tableHeight));
            DrawTableHeader(dc, x, y, columnWidths);

            double rowY = y + TableHeaderHeight;

            for (int offset = 0; offset < rowCount; offset++)
            {
                int rowIndex = startIndex + offset;
                // 실제 LogRow 한 줄을 인쇄 테이블 행으로 그린다.
                DrawDataRow(dc, _rows[rowIndex], rowIndex, x, rowY, columnWidths);
                rowY += TableRowHeight;
            }
        }

        // 로그 테이블 헤더를 그린다.
        private static void DrawTableHeader(DrawingContext dc, double x, double y, IReadOnlyList<double> columnWidths)
        {
            // 실제 인쇄 컬럼은 화면에서 Packet을 제외한 주요 5개다.
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

        // 실제 LogRow 한 줄을 인쇄용 행으로 그린다.
        // 예: 2026-06-01 17:04:17 / 중계기고장 / 발생 / 02# 01계통 005중계기 / 중계기 통신고장.
        private static void DrawDataRow(
            DrawingContext dc,
            LogRow row,
            int rowIndex,
            double x,
            double y,
            IReadOnlyList<double> columnWidths)
        {
            // 화면과 같은 분류 기준으로 인쇄 배경/글자색/굵기를 정한다.
            string rowTag = LogClassifier.ClassifyRowTag(row, rowIndex);
            Brush rowFill = GetPrintRowFill(rowTag, rowIndex);
            Brush rowText = GetPrintRowText(rowTag);
            bool bold = IsPrintBold(rowTag);

            // 인쇄 테이블은 Packet을 제외한 5개 컬럼을 출력한다.
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
                    // 내용 셀에는 MCC/ON/OFF 배지가 앞에 붙을 수 있다.
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
                    // 발생시간/구분/상태는 가운데, 위치는 왼쪽 정렬이다.
                    TextAlignment alignment = column <= 2
                        ? TextAlignment.Center
                        : TextAlignment.Left;

                    DrawText(
                        dc,
                        values[column],
                        Deflate(rect, CellHorizontalPadding, 0),
                        rowText,
                        LogRowFontSize,
                        bold ? FontWeights.ExtraBold : FontWeights.Normal,
                        alignment);
                }

                cellX += columnWidths[column];
            }
        }

        // 인쇄 내용 셀을 그린다.
        // 배지 목록이 있으면 배지를 먼저 그리고 남은 폭에 Contents를 그린다.
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
                // badgeKey를 실제 라벨/색상으로 변환한다.
                BadgeStyle? badge = LogStyleMapper.GetBadge(badgeKey);

                if (badge == null)
                {
                    // 등록되지 않은 배지는 인쇄하지 않는다.
                    continue;
                }

                // 예: MCC, ON, OFF, 출력 ON 배지 폭을 라벨 길이에 맞춰 계산한다.
                double badgeWidth = Math.Max(34, badge.Label.Length * 6 + 12);
                Rect badgeRect = new(contentX, rect.Top + ((rect.Height - LogBadgeHeight) / 2), badgeWidth, LogBadgeHeight);
                Pen badgeBorderPen = FrozenPen(badge.Border, 1);

                dc.DrawRoundedRectangle(badge.Fill, badgeBorderPen, badgeRect, 3, 3);
                DrawText(
                    dc,
                    badge.Label,
                    Deflate(badgeRect, 3, 0),
                    badge.Text,
                    LogBadgeFontSize,
                    FontWeights.Black,
                    TextAlignment.Center);

                contentX += badgeWidth + 4;

                if (contentX >= rect.Right)
                {
                    // 배지만으로 셀 폭이 꽉 차면 내용 텍스트는 그리지 않는다.
                    return;
                }
            }

            // 배지 뒤 남은 공간에 실제 Contents 문구를 그린다.
            DrawText(
                dc,
                value ?? "",
                new Rect(contentX, rect.Top, Math.Max(0, rect.Right - contentX), rect.Height),
                textBrush,
                LogRowFontSize,
                bold ? FontWeights.ExtraBold : FontWeights.Normal,
                TextAlignment.Left);
        }

        // 현재 용지 크기와 요약 카드 수를 기준으로 페이지 수와 페이지당 로그 수를 다시 계산한다.
        private void RecalculatePageMetrics()
        {
            // 조회 조건/전체 건수 2개 + 값이 있는 카테고리 카드 수.
            int summaryCardCount = 2 + SummaryCategories.Count(category =>
                _summaryCounts.TryGetValue(category.Key, out int count) && count > 0);
            // 요약 카드는 3열로 배치한다.
            int summaryRows = Math.Max(1, (int)Math.Ceiling(summaryCardCount / 3.0));
            _reportHeaderHeight = ReportHeaderTopLineHeight +
                                  ReportHeaderPaddingTop +
                                  ReportTitleHeight +
                                  ReportTitleSummaryGap +
                                  summaryRows * SummaryCardHeight +
                                  Math.Max(0, summaryRows - 1) * SummaryCardGap +
                                  ReportHeaderPaddingBottom;

            // 실제 로그 테이블이 들어갈 수 있는 세로 높이다.
            double contentHeight = Math.Max(320, PageSize.Height - _margin.Top - _margin.Bottom);
            // 첫 페이지는 큰 헤더 때문에 가용 행 높이가 줄어든다.
            double firstPageAvailableRowsHeight =
                contentHeight - _reportHeaderHeight - HeaderTableSpacing - TableHeaderHeight;
            // 이후 페이지는 연속 헤더만 있어 더 많은 행이 들어간다.
            double followingPageAvailableRowsHeight =
                contentHeight - ContinuationHeaderHeight - ContinuationHeaderSpacing - TableHeaderHeight;

            _rowsPerFirstPage = Math.Max(1, (int)Math.Floor(firstPageAvailableRowsHeight / TableRowHeight));
            _rowsPerPage = Math.Max(1, (int)Math.Floor(followingPageAvailableRowsHeight / TableRowHeight));

            if (_rows.Count <= _rowsPerFirstPage)
            {
                // 첫 페이지에 모두 들어가면 전체 1페이지다.
                _pageCount = 1;
                return;
            }

            // 첫 페이지 이후 남은 로그를 두 번째 페이지 규칙으로 나눠 계산한다.
            int remainingRows = _rows.Count - _rowsPerFirstPage;
            _pageCount = 1 + (int)Math.Ceiling(remainingRows / (double)_rowsPerPage);
        }

        // 페이지 번호가 전체 로그 목록에서 몇 번째 index부터 시작하는지 계산한다.
        private int GetStartIndexForPage(int pageNumber)
        {
            if (pageNumber == 0)
            {
                // 첫 페이지는 0번 로그부터 시작한다.
                return 0;
            }

            // 이후 페이지는 첫 페이지에 들어간 행 수를 빼고 계산한다.
            return _rowsPerFirstPage + ((pageNumber - 1) * _rowsPerPage);
        }

        // 현재 페이지에 들어갈 실제 로그 행 수를 계산한다.
        private int GetRowCountForPage(int pageNumber, int startIndex)
        {
            if (startIndex >= _rows.Count)
            {
                // 로그가 없는 페이지 요청이면 0행이다.
                return 0;
            }

            // 첫 페이지와 이후 페이지의 최대 행 수가 다르다.
            int maxRowsForPage = pageNumber == 0
                ? _rowsPerFirstPage
                : _rowsPerPage;

            return Math.Min(maxRowsForPage, _rows.Count - startIndex);
        }

        // 용지 폭에 맞춰 인쇄 테이블 컬럼 폭을 계산한다.
        private static double[] GetColumnWidths(double tableWidth)
        {
            // A4 기준 기본 컬럼 폭이다.
            const double timeWidth = 104.0;
            const double typeWidth = 58.0;
            const double actionWidth = 44.0;
            const double sectionWidth = 185.0;
            const double minContentWidth = 90.0;

            double fixedWidth = timeWidth + typeWidth + actionWidth + sectionWidth;

            if (tableWidth >= fixedWidth + minContentWidth)
            {
                // 충분한 폭이면 내용 컬럼이 남은 폭을 모두 사용한다.
                return new[]
                {
                    timeWidth,
                    typeWidth,
                    actionWidth,
                    sectionWidth,
                    tableWidth - fixedWidth
                };
            }

            // 폭이 좁은 프린터 설정이면 고정 컬럼을 비율 축소하고 내용 최소 폭을 확보한다.
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

        // rowTag에 맞는 인쇄 행 배경색을 반환한다.
        private static Brush GetPrintRowFill(string rowTag, int rowIndex)
        {
            return rowTag switch
            {
                // 일반 로그는 흰색/연한 줄무늬다.
                "even_row" => WhiteBrush,
                "odd_row" => StripeBrush,
                // 실제 발생 로그는 계열별 연한 배경색을 사용한다.
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

        // rowTag에 맞는 인쇄 글자색을 반환한다.
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

        // 발생/고장/복구 강조 로그는 굵게 인쇄하고, 일반/배지 로그는 기본 두께로 둔다.
        private static bool IsPrintBold(string rowTag)
        {
            return rowTag is not "even_row" and not "odd_row" &&
                   !rowTag.EndsWith("_badge", StringComparison.Ordinal);
        }

        // WPF DrawingContext에 한 줄 텍스트를 그린다.
        // 셀 폭을 넘으면 말줄임 처리한다.
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
                // 폭/높이가 없으면 그릴 수 없다.
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

        // Rect 안쪽 여백을 적용한 새 Rect를 만든다.
        private static Rect Deflate(Rect rect, double horizontal, double vertical)
        {
            return new Rect(
                rect.Left + horizontal,
                rect.Top + vertical,
                Math.Max(0, rect.Width - (horizontal * 2)),
                Math.Max(0, rect.Height - (vertical * 2)));
        }

        // 프린터에서 받은 PageSize가 비정상이면 A4 fallback으로 보정한다.
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

        // HEX 문자열을 Freeze된 SolidColorBrush로 만든다.
        private static SolidColorBrush ColorBrush(string hex)
        {
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        // 반복 사용 Pen을 Freeze해서 인쇄 렌더링 비용을 줄인다.
        private static Pen FrozenPen(Brush brush, double thickness)
        {
            Pen pen = new(brush, thickness);
            pen.Freeze();
            return pen;
        }

        // 인쇄 요약 카드 하나의 정의다.
        private sealed class SummaryCategory
        {
            public string Key { get; init; } = "";
            public string Label { get; init; } = "";
            public string Color { get; init; } = "";
        }
    }
}
