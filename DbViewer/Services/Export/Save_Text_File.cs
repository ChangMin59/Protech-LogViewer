using DbViewer.Models;
using DbViewer.Services.View;

namespace DbViewer.Services.Export
{
    public sealed class SaveTextFileRequest
    {
        public LogRepository Repository { get; init; } = null!;
        public string Mode { get; init; } = "";
        public string Keyword { get; init; } = "";
        public string StartDate { get; init; } = "";
        public string EndDate { get; init; } = "";
        public IReadOnlyList<LogRow>? SelectedCategoryRows { get; init; }
        public IReadOnlyList<LogRow>? CachedRows { get; init; }
        public string OutputPath { get; init; } = "";
    }

    public static class Save_Text_File
    {
        public static string Run(SaveTextFileRequest request)
        {
            Func<IEnumerable<LogRow>> rowsFactory = GetRowsForTextExport(request);

            return TextFileExporter.Export(
                rowsFactory,
                request.OutputPath
            );
        }

        private static Func<IEnumerable<LogRow>> GetRowsForTextExport(SaveTextFileRequest request)
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
