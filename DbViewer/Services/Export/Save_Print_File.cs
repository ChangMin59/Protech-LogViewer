using DbViewer.Models;
using DbViewer.Services.View;

namespace DbViewer.Services.Export
{
    public sealed class SavePrintFileRequest
    {
        public LogRepository Repository { get; init; } = null!;
        public string Mode { get; init; } = "";
        public string Keyword { get; init; } = "";
        public string StartDate { get; init; } = "";
        public string EndDate { get; init; } = "";
        public IReadOnlyList<string> SelectedCategories { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> CategoryDisplayNames { get; init; } =
            new Dictionary<string, string>();
        public IReadOnlyList<LogRow>? SelectedCategoryRows { get; init; }
        public IReadOnlyList<LogRow>? CachedRows { get; init; }
        public string OutputDirectory { get; init; } = "";
    }

    public static class Save_Print_File
    {
        public static List<string> Run(SavePrintFileRequest request)
        {
            Func<IEnumerable<LogRow>> rowsFactory = GetRowsForPrintExport(request);
            string conditionText = BuildConditionText(request);

            return PrintHtmlExporter.Export(
                rowsFactory,
                "프로테크 이력",
                conditionText,
                request.OutputDirectory
            );
        }

        private static Func<IEnumerable<LogRow>> GetRowsForPrintExport(SavePrintFileRequest request)
        {
            if (request.Mode == "category")
            {
                IReadOnlyList<LogRow> rows = request.SelectedCategoryRows ?? Array.Empty<LogRow>();
                return () => rows;
            }

            if (request.Mode == "date_cache" && request.CachedRows != null)
            {
                IReadOnlyList<LogRow> rows = request.CachedRows;
                return () => rows;
            }

            if (request.Mode == "date" || request.Mode == "date_cache")
            {
                DateTime? filterStart = IsValidDate(request.StartDate)
                    ? DateTime.Parse(request.StartDate).Date
                    : null;

                DateTime? filterEnd = IsValidDate(request.EndDate)
                    ? DateTime.Parse(request.EndDate).Date.AddDays(1).AddTicks(-1)
                    : null;

                return () => request.Repository.StreamAllLogs()
                    .Where(row => IsRowInDateRange(row, filterStart, filterEnd));
            }

            if (request.Mode == "search")
            {
                return () => request.Repository.StreamAllLogs()
                    .Where(row => IsRowMatchedBySearchFields(row, request.Keyword));
            }

            return () => request.Repository.StreamAllLogs();
        }

        private static string BuildConditionText(SavePrintFileRequest request)
        {
            if (request.Mode == "category")
            {
                List<string> names = request.SelectedCategories
                    .Select(category => request.CategoryDisplayNames.TryGetValue(category, out string? name)
                        ? name
                        : category)
                    .ToList();

                return names.Count == 0
                    ? "카테고리: 선택 없음"
                    : $"카테고리: {string.Join(", ", names)}";
            }

            if (request.Mode == "search")
            {
                return $"검색어: {request.Keyword}";
            }

            if (request.Mode == "date" || request.Mode == "date_cache")
            {
                return $"기간: {request.StartDate} ~ {request.EndDate}";
            }

            return "전체 이력";
        }

        private static bool IsRowMatchedBySearchFields(LogRow row, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            return row.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Action.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Section.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Contents.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Packet.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidDate(string value)
        {
            return DateTime.TryParse(value, out _);
        }

        private static bool IsRowInDateRange(
            LogRow row,
            DateTime? filterStart,
            DateTime? filterEnd)
        {
            if (!filterStart.HasValue && !filterEnd.HasValue)
            {
                return true;
            }

            if (!DateTime.TryParse(row.DTime, out DateTime rowDateTime))
            {
                return false;
            }

            if (filterStart.HasValue && rowDateTime < filterStart.Value)
            {
                return false;
            }

            if (filterEnd.HasValue && rowDateTime > filterEnd.Value)
            {
                return false;
            }

            return true;
        }
    }
}
