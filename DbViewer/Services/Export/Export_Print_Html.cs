using DbViewer.Models;
using DbViewer.Services.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Media;

namespace DbViewer.Services.Export
{
    // 현재 로그 조건을 HTML 인쇄 파일로 저장한다.
    // 예: 전체 300000건이면 10000건씩 여러 HTML 파일로 나눠 만든다.
    public static class PrintHtmlExporter
    {
        // HTML 파일 하나에 담을 최대 로그 수다.
        // 너무 큰 HTML 한 파일은 브라우저/인쇄가 느려져 10000건 단위로 나눈다.
        private const int ChunkSize = 10000;

        // 로그 목록을 HTML 인쇄 파일로 내보낸다.
        public static List<string> Export(
            Func<IEnumerable<LogRow>> sourceRowsFactory,
            string title,
            string conditionText,
            string? outputDir = null)
        {
            // 저장 폴더가 없으면 실행 파일 아래 "이력 인쇄 파일" 폴더를 기본으로 쓴다.
            string finalOutputDir = outputDir ?? Path.Combine(AppContext.BaseDirectory, "이력 인쇄 파일");

            Directory.CreateDirectory(finalOutputDir);

            // 파일명 중복 방지와 보고서 출력 일시 표시에 사용한다.
            DateTime now = DateTime.Now;
            string timestamp = now.ToString("yyyyMMdd_HHmmss");
            string generatedAt = now.ToString("yyyy-MM-dd HH:mm:ss");
            // 전체 요약은 파일이 여러 개로 나뉘어도 전체 기준으로 한 번 계산한다.
            ExportSummary totalSummary = CalculateLogSummary(sourceRowsFactory);

            int totalCount = totalSummary.TotalCount;
            // 예: 25001건이면 3개 파일이 된다.
            int totalParts = Math.Max(1, (int)Math.Ceiling(totalCount / (double)ChunkSize));
            List<string> savedPaths = new();

            if (totalCount == 0)
            {
                // 인쇄 대상이 없어도 빈 테이블이 있는 HTML 1개는 만든다.
                string outputPath = UniquePrintPath(Path.Combine(
                    finalOutputDir,
                    $"인쇄파일_{timestamp}_001.html"
                ));

                WritePrintHtmlFile(
                    outputPath,
                    new List<LogRow>(),
                    title,
                    conditionText,
                    generatedAt,
                    totalCount,
                    totalSummary.Counts,
                    1,
                    totalParts,
                    0,
                    0,
                    showReportHeader: true
                );

                savedPaths.Add(outputPath);
                return savedPaths;
            }

            List<LogRow> chunkRows = new(ChunkSize);
            int partNumber = 1;
            int startNumber = 1;
            int rowNumber = 0;

            foreach (LogRow row in sourceRowsFactory())
            {
                // 실제 로그를 10000건 단위 버퍼에 담는다.
                chunkRows.Add(row);
                rowNumber++;

                if (chunkRows.Count < ChunkSize)
                {
                    continue;
                }

                savedPaths.Add(WriteChunk(
                    finalOutputDir,
                    timestamp,
                    chunkRows,
                    title,
                    conditionText,
                    generatedAt,
                    totalCount,
                    totalSummary.Counts,
                    partNumber,
                    totalParts,
                    startNumber,
                    rowNumber
                ));

                // 다음 HTML 파일 버퍼를 새로 시작한다.
                chunkRows = new List<LogRow>(ChunkSize);
                partNumber++;
                startNumber = rowNumber + 1;
            }

            if (chunkRows.Count > 0)
            {
                // 마지막 남은 로그가 10000건 미만이면 마지막 파일로 저장한다.
                savedPaths.Add(WriteChunk(
                    finalOutputDir,
                    timestamp,
                    chunkRows,
                    title,
                    conditionText,
                    generatedAt,
                    totalCount,
                    totalSummary.Counts,
                    partNumber,
                    totalParts,
                    startNumber,
                    rowNumber
                ));
            }

            return savedPaths;
        }

        // HTML 파일 한 조각을 저장하고 생성된 경로를 반환한다.
        private static string WriteChunk(
            string outputDir,
            string timestamp,
            List<LogRow> chunkRows,
            string title,
            string conditionText,
            string generatedAt,
            int totalCount,
            Dictionary<string, int> totalSummary,
            int partNumber,
            int totalParts,
            int startNumber,
            int endNumber)
        {
            // 예: 인쇄파일_20260612_101530_001.html.
            string outputPath = UniquePrintPath(Path.Combine(
                outputDir,
                $"인쇄파일_{timestamp}_{partNumber:000}.html"
            ));

            WritePrintHtmlFile(
                outputPath,
                chunkRows,
                title,
                conditionText,
                generatedAt,
                totalCount,
                totalSummary,
                partNumber,
                totalParts,
                startNumber,
                endNumber,
                showReportHeader: partNumber == 1
            );

            return outputPath;
        }

        // 실제 HTML 문서 구조를 작성한다.
        // 첫 파일은 제목/조회 조건/요약을 포함하고, 이후 파일은 연속 파일 표시만 한다.
        private static void WritePrintHtmlFile(
            string outputPath,
            List<LogRow> rows,
            string title,
            string conditionText,
            string generatedAt,
            int totalCount,
            Dictionary<string, int> totalSummary,
            int partNumber,
            int totalParts,
            int startNumber,
            int endNumber,
            bool showReportHeader)
        {
            // UTF-8 HTML로 저장해 한글 로그가 깨지지 않게 한다.
            using StreamWriter html = new(outputPath, append: false, Encoding.UTF8);

            html.WriteLine("<!doctype html>");
            html.WriteLine("<html lang=\"ko\">");
            html.WriteLine("<head>");
            html.WriteLine("<meta charset=\"utf-8\">");
            html.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.WriteLine($"<title>{Escape(title)} - {partNumber:000}</title>");
            html.WriteLine(BuildPrintStyle());
            html.WriteLine("</head>");
            html.WriteLine("<body>");
            html.WriteLine("<main class=\"report-page\">");

            if (showReportHeader)
            {
                // 첫 파일에는 보고서 제목, 조회 조건, 전체 건수, 카테고리 요약을 출력한다.
                WriteReportHeader(html, title, conditionText, generatedAt, totalCount, totalSummary);
            }
            else
            {
                // 두 번째 파일부터는 화면 확인용 연속 제목만 출력한다.
                html.WriteLine("<div class=\"continuation-title screen-only\">");
                html.WriteLine($"{Escape(title)} - {partNumber:000}/{totalParts:000} ({startNumber:N0} ~ {endNumber:N0}건)");
                html.WriteLine("</div>");
            }

            WriteLogTable(html, rows, startNumber);

            html.WriteLine("</main>");
            html.WriteLine("</body>");
            html.WriteLine("</html>");
        }

        // HTML 상단 보고서 헤더를 작성한다.
        // 예: 제목=프로테크 이력, 조회 조건=기간: 2026-06-01 ~ 2026-06-07.
        private static void WriteReportHeader(
            StreamWriter html,
            string title,
            string conditionText,
            string generatedAt,
            int totalCount,
            Dictionary<string, int> totalSummary)
        {
            html.WriteLine("<section class=\"report-header\">");
            html.WriteLine("<div class=\"report-title-row\">");
            html.WriteLine("<div class=\"title-brand\">");
            html.WriteLine($"<h1>{Escape(title)}</h1>");
            html.WriteLine("</div>");
            html.WriteLine($"<div class=\"printed-at\">출력 일시&nbsp;&nbsp;|&nbsp;&nbsp;{Escape(generatedAt)}</div>");
            html.WriteLine("</div>");

            html.WriteLine("<div class=\"summary-grid\">");
            // 현재 조회 조건 카드다.
            html.WriteLine(BuildSummaryCard("조회 조건", conditionText));
            // 실제 인쇄 대상 전체 건수 카드다.
            html.WriteLine(BuildSummaryCard("전체 건수", $"{totalCount:N0}건"));

            foreach (SummaryCategory category in GetSummaryCategories())
            {
                int count = totalSummary.TryGetValue(category.Key, out int value)
                    ? value
                    : 0;

                if (count <= 0)
                {
                    // 해당 기간/필터에 없는 카테고리는 요약 카드에서 숨긴다.
                    continue;
                }

                html.WriteLine(BuildCategorySummaryCard(
                    category.Label,
                    $"{count:N0}건",
                    GetSummaryCategoryColor(category.Key)));
            }

            html.WriteLine("</div>");
            html.WriteLine("</section>");
        }

        // 로그 테이블을 작성한다.
        // 실제 출력 컬럼: 발생시간 / 구분 / 상태 / 위치 / 내용.
        private static void WriteLogTable(StreamWriter html, List<LogRow> rows, int startNumber)
        {
            html.WriteLine("<table class=\"log-table\">");
            html.WriteLine("<colgroup>");
            html.WriteLine("<col class=\"col-time\">");
            html.WriteLine("<col class=\"col-type\">");
            html.WriteLine("<col class=\"col-action\">");
            html.WriteLine("<col class=\"col-section\">");
            html.WriteLine("<col class=\"col-content\">");
            html.WriteLine("</colgroup>");
            html.WriteLine("<thead>");
            html.WriteLine("<tr>");
            html.WriteLine("<th>발생시간</th>");
            html.WriteLine("<th>구분</th>");
            html.WriteLine("<th>상태</th>");
            html.WriteLine("<th>위치</th>");
            html.WriteLine("<th>내용</th>");
            html.WriteLine("</tr>");
            html.WriteLine("</thead>");
            html.WriteLine("<tbody>");

            for (int index = 0; index < rows.Count; index++)
            {
                LogRow row = rows[index];
                // 파일이 분할되어도 rowTag 짝/홀 계산과 요약 기준이 전체 순번에 맞게 이어진다.
                int globalIndex = startNumber > 0
                    ? startNumber - 1 + index
                    : index;

                // 화면과 같은 LogClassifier 기준으로 HTML 행 색상 클래스를 만든다.
                string rowTag = LogClassifier.ClassifyRowTag(row, globalIndex);
                string rowClass = $"print-{rowTag.Replace('_', '-')}";
                // 예: MCC 기동 로그면 내용 앞에 MCC/ON 배지를 붙인다.
                List<string> badgeKeys = LogClassifier.GetBadgeKeys(row, rowTag);

                html.WriteLine($"<tr class=\"{rowClass}\">");
                WriteCell(html, "cell-time", row.DTime);
                WriteCell(html, "cell-type", row.Type);
                WriteCell(html, "cell-action", row.Action);
                WriteCell(html, "cell-section", row.Section);
                WriteContentCell(html, row.Contents, badgeKeys);
                html.WriteLine("</tr>");
            }

            html.WriteLine("</tbody>");
            html.WriteLine("</table>");
        }

        // 일반 텍스트 셀을 HTML 이스케이프해서 작성한다.
        private static void WriteCell(StreamWriter html, string cssClass, string value)
        {
            html.WriteLine($"<td class=\"{cssClass}\">{Escape(value)}</td>");
        }

        // 내용 셀에는 배지를 먼저 쓰고 실제 Contents 문구를 이어서 쓴다.
        private static void WriteContentCell(StreamWriter html, string value, List<string> badgeKeys)
        {
            html.Write("<td class=\"cell-content\">");

            foreach (string badgeKey in badgeKeys)
            {
                // 예: mcc_badge -> <span>MCC</span>.
                html.Write(BuildPrintBadge(badgeKey));
            }

            html.Write(Escape(value));
            html.WriteLine("</td>");
        }

        // 같은 이름의 인쇄 HTML이 이미 있으면 _1, _2를 붙여 새 경로를 만든다.
        private static string UniquePrintPath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            string directory = Path.GetDirectoryName(path) ?? "";
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            for (int index = 1; index < 1000; index++)
            {
                // 예: 인쇄파일_..._001_1.html.
                string candidate = Path.Combine(directory, $"{stem}_{index}{extension}");

                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException($"인쇄 파일명을 만들 수 없습니다: {path}");
        }

        // HTML 전체 요약 계산 결과다.
        private sealed class ExportSummary
        {
            public int TotalCount { get; init; }
            public Dictionary<string, int> Counts { get; init; } = new();
        }

        // 전체 로그를 한 번 훑어 HTML 헤더에 보여줄 총 건수와 카테고리 건수를 계산한다.
        private static ExportSummary CalculateLogSummary(Func<IEnumerable<LogRow>> sourceRowsFactory)
        {
            // HTML 요약 카드에 표시할 주요 카테고리만 계산한다.
            Dictionary<string, int> summary = new()
            {
                ["fire"] = 0,
                ["alarm"] = 0,
                ["relay_fault"] = 0,
                ["an_fault"] = 0,
                ["line_fault"] = 0,
                ["output"] = 0,
                ["mcc"] = 0
            };

            int index = 0;

            foreach (LogRow row in sourceRowsFactory())
            {
                // 실제 로그를 화면과 같은 기준으로 분류한다.
                string rowTag = LogClassifier.ClassifyRowTag(row, index);
                List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);

                foreach (string category in categories)
                {
                    if (!summary.ContainsKey(category))
                    {
                        // 요약 카드 대상이 아닌 카테고리는 건너뛴다.
                        continue;
                    }

                    summary[category]++;
                }

                index++;
            }

            return new ExportSummary
            {
                TotalCount = index,
                Counts = summary
            };
        }

        // HTML 요약 카드에 표시할 카테고리 이름이다.
        private sealed class SummaryCategory
        {
            public string Key { get; init; } = "";
            public string Label { get; init; } = "";
        }

        // 요약 카드 왼쪽 색상 막대 색이다.
        private static readonly Dictionary<string, string> SummaryCategoryColors = new()
        {
            ["fire"] = "#DC2626",
            ["alarm"] = "#2563EB",
            ["relay_fault"] = "#7C3AED",
            ["an_fault"] = "#EA580C",
            ["line_fault"] = "#A16207",
            ["output"] = "#16A34A",
            ["mcc"] = "#334155"
        };

        // HTML 보고서에 표시할 카테고리 순서를 고정한다.
        private static SummaryCategory[] GetSummaryCategories()
        {
            return new[]
            {
                new SummaryCategory { Key = "fire", Label = "화재" },
                new SummaryCategory { Key = "alarm", Label = "제경보" },
                new SummaryCategory { Key = "relay_fault", Label = "중계기 고장" },
                new SummaryCategory { Key = "an_fault", Label = "AN고장" },
                new SummaryCategory { Key = "line_fault", Label = "단선" },
                new SummaryCategory { Key = "output", Label = "출력" },
                new SummaryCategory { Key = "mcc", Label = "MCC" }
            };
        }

        // 카테고리 키에 맞는 요약 카드 색상을 찾는다.
        private static string GetSummaryCategoryColor(string categoryKey)
        {
            return SummaryCategoryColors.TryGetValue(categoryKey, out string? color)
                ? color
                : "#334155";
        }

        // 일반 요약 카드를 만든다.
        private static string BuildSummaryCard(string label, string value)
        {
            return
                "<div class=\"summary-card\">\n" +
                $"  <div class=\"summary-label\">{Escape(label)}</div>\n" +
                $"  <div class=\"summary-value\">{Escape(value)}</div>\n" +
                "</div>";
        }

        // 카테고리 색상 박스가 포함된 요약 카드를 만든다.
        private static string BuildCategorySummaryCard(string label, string value, string color)
        {
            return
                "<div class=\"summary-card\">\n" +
                "  <div class=\"summary-label summary-label-category\">\n" +
                $"    <span class=\"summary-color-box\" style=\"background:{Escape(color)};\"></span>\n" +
                $"    <span>{Escape(label)}</span>\n" +
                "  </div>\n" +
                $"  <div class=\"summary-value\">{Escape(value)}</div>\n" +
                "</div>";
        }

        // 화면 배지 스타일을 HTML span으로 변환한다.
        private static string BuildPrintBadge(string badgeKey)
        {
            BadgeStyle? badge = LogStyleMapper.GetBadge(badgeKey);

            if (badge == null)
            {
                // 등록되지 않은 배지는 HTML에 출력하지 않는다.
                return "";
            }

            return
                "<span class=\"log-badge\" " +
                $"style=\"background:{BrushToCss(badge.Fill)};" +
                $"border-color:{BrushToCss(badge.Border)};" +
                $"color:{BrushToCss(badge.Text)};\">" +
                $"{Escape(badge.Label)}</span>";
        }

        // WPF Brush 색상을 HTML CSS 색상으로 변환한다.
        private static string BrushToCss(Brush brush)
        {
            if (brush is not SolidColorBrush solid)
            {
                // SolidColorBrush가 아니면 안전하게 투명 처리한다.
                return "transparent";
            }

            Color color = solid.Color;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        // 실제 로그 내용에 <, >, & 같은 문자가 있어도 HTML이 깨지지 않게 이스케이프한다.
        private static string Escape(string? value)
        {
            return WebUtility.HtmlEncode(value ?? "");
        }

        // HTML 인쇄 디자인 CSS다.
        // WPF 직접 인쇄와 같은 색상 계열로 화재/제경보/고장/복구 로그를 표현한다.
        private static string BuildPrintStyle()
        {
            return """
<style>
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; }
body {
  color: #111827;
  background: #F3F4F6;
  font-family: "Malgun Gothic", "맑은 고딕", Arial, sans-serif;
  font-size: 13px;
}
.report-page { width: 100%; min-height: 100vh; padding: 18px; background: #FFFFFF; }
.report-header {
  margin-bottom: 12px;
  padding: 12px 14px 10px 14px;
  border: 1px solid #CBD5E1;
  border-top: 5px solid #1E293B;
  border-radius: 8px;
  background: linear-gradient(180deg, #FFFFFF 0%, #F8FAFC 100%);
}
.report-title-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 14px;
  margin-bottom: 10px;
}
.title-brand { display: flex; align-items: center; gap: 12px; }
h1 { margin: 0; color: #0F172A; font-size: 30px; font-weight: 900; }
.printed-at {
  min-width: 190px;
  padding-top: 5px;
  color: #334155;
  font-size: 12px;
  font-weight: 700;
  text-align: right;
  white-space: nowrap;
}
.summary-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 7px; }
.summary-card {
  min-height: 48px;
  padding: 8px 10px;
  border: 1px solid #CBD5E1;
  border-radius: 7px;
  background: #FFFFFF;
}
.summary-label { margin-bottom: 5px; color: #64748B; font-size: 11px; font-weight: 800; }
.summary-label-category { display: flex; align-items: center; gap: 6px; }
.summary-color-box { display: inline-block; width: 10px; height: 10px; border-radius: 2px; flex: 0 0 auto; }
.summary-value { color: #0F172A; font-size: 13px; font-weight: 900; line-height: 1.35; word-break: break-all; }
.continuation-title {
  margin-bottom: 8px;
  padding: 7px 9px;
  border: 1px solid #CBD5E1;
  background: #F8FAFC;
  color: #334155;
  font-size: 12px;
  font-weight: 800;
}
.log-table {
  width: 100%;
  border-collapse: collapse;
  table-layout: fixed;
  background: #FFFFFF;
  border: 1px solid #94A3B8;
}
.col-time { width: 128px; }
.col-type { width: 72px; }
.col-action { width: 58px; }
.col-section { width: 220px; }
.col-content { width: auto; }
.log-table th, .log-table td {
  border: 1px solid #CBD5E1;
  padding: 5px 6px;
  vertical-align: top;
  line-height: 1.35;
}
.log-table th {
  color: #FFFFFF;
  background: #1E293B;
  font-size: 11px;
  font-weight: 900;
  text-align: center;
  white-space: nowrap;
}
.log-table td {
  color: #111827;
  font-size: 10.5px;
  word-break: keep-all;
  overflow-wrap: normal;
  white-space: nowrap;
}
.cell-time, .cell-type, .cell-action { text-align: center; white-space: nowrap; }
.cell-section, .cell-content { text-align: left; white-space: nowrap; }
.print-even-row { background: #FFFFFF; }
.print-odd-row { background: #F8FAFC; }
.print-fire-row { background: #FEF2F2; }
.print-fire-row td, .print-fire-text td, .print-fire-strong-text td { color: #7F1D1D; font-weight: 800; }
.print-fire-soft-row { background: #FFF1F2; }
.print-alarm-row { background: #EFF6FF; }
.print-alarm-row td, .print-alarm-text td { color: #1D4ED8; font-weight: 800; }
.print-line-fault-row { background: #FEFCE8; }
.print-line-fault-row td, .print-line-fault-text td { color: #854D0E; font-weight: 800; }
.print-fault-row, .print-fault-soft-row { background: #FFF7ED; }
.print-fault-row td, .print-fault-text td, .print-fault-soft-row td, .print-fault-soft-text td { color: #7C2D12; font-weight: 800; }
.print-receiver-fault-row, .print-relay-fault-row { background: #FAF5FF; }
.print-receiver-fault-row td, .print-receiver-fault-text td, .print-relay-fault-row td, .print-relay-fault-text td { color: #6B21A8; font-weight: 800; }
.print-panel-fault-row { background: #ECFEFF; }
.print-panel-fault-row td, .print-panel-fault-text td { color: #155E75; font-weight: 800; }
.print-recover-row { background: #F0FDF4; }
.print-recover-row td { color: #14532D; font-weight: 800; }
.print-mcc-badge td, .print-on-badge td, .print-off-badge td,
.print-output-on-badge td, .print-output-off-badge td, .print-key-badge td { color: #111827; }
.log-badge {
  display: inline-block;
  min-width: 34px;
  margin-right: 6px;
  padding: 1px 6px 2px;
  border: 1px solid;
  border-radius: 4px;
  font-size: 8.5px;
  font-weight: 900;
  line-height: 1.2;
  text-align: center;
  vertical-align: middle;
}
@page { size: A4 portrait; margin: 8mm; }
@media print {
  * { print-color-adjust: exact; -webkit-print-color-adjust: exact; }
  body { background: #FFFFFF; font-size: 9px; }
  .report-page { padding: 0; }
  .report-header { margin-bottom: 8px; padding: 8px 10px; border-radius: 0; border-top-width: 4px; page-break-after: avoid; }
  h1 { font-size: 30px; }
  .printed-at { min-width: auto; font-size: 9px; }
  .summary-grid { grid-template-columns: repeat(3, 1fr); gap: 4px; }
  .summary-card { min-height: 36px; padding: 4px 6px; border-radius: 4px; }
  .summary-label { margin-bottom: 2px; font-size: 8.5px; }
  .summary-label-category { gap: 4px; }
  .summary-color-box { width: 7px; height: 7px; border-radius: 1px; }
  .summary-value { font-size: 9px; }
  .screen-only { display: none; }
  thead { display: table-header-group; }
  tr { page-break-inside: avoid; }
  .log-table th, .log-table td { padding: 3px 4px; line-height: 1.32; }
  .log-table th { font-size: 8.6px; }
  .log-table td { font-size: 8.3px; white-space: nowrap; }
  .log-badge { min-width: 28px; margin-right: 4px; padding: 1px 4px; border-radius: 3px; font-size: 7px; }
  .col-time { width: 104px; }
  .col-type { width: 58px; }
  .col-action { width: 44px; }
  .col-section { width: 185px; }
  .col-content { width: auto; }
}
</style>
""";
        }
    }
}
