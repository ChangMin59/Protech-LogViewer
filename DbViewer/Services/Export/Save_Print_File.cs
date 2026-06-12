using DbViewer.Models;
using DbViewer.Services.View;

namespace DbViewer.Services.Export
{
    // HTML 인쇄파일 저장 버튼을 눌렀을 때 필요한 현재 조회 조건이다.
    // 현재 버튼은 숨김 상태지만, 나중에 다시 쓸 수 있도록 기능은 남아 있다.
    public sealed class SavePrintFileRequest
    {
        // 실제 DB 또는 TXT 변환 임시 DB를 읽는 저장소다.
        public LogRepository Repository { get; init; } = null!;
        // 현재 조회 모드. 예: all, date, date_cache, search, category.
        public string Mode { get; init; } = "";
        // 검색 모드일 때 저장 대상 필터 키워드다.
        public string Keyword { get; init; } = "";
        // 기간 모드 시작일. 예: 2026-06-01.
        public string StartDate { get; init; } = "";
        // 기간 모드 종료일. 예: 2026-06-07.
        public string EndDate { get; init; } = "";
        // 카테고리 모드에서 선택된 내부 키 목록이다. 예: fire, relay_fault.
        public IReadOnlyList<string> SelectedCategories { get; init; } = Array.Empty<string>();
        // 내부 카테고리 키를 화면 이름으로 바꾼다. 예: fire -> 화재.
        public IReadOnlyDictionary<string, string> CategoryDisplayNames { get; init; } =
            new Dictionary<string, string>();
        // 카테고리 모드에서 이미 필터된 로그 목록이다.
        public IReadOnlyList<LogRow>? SelectedCategoryRows { get; init; }
        // 기간 캐시가 있으면 DB 재조회 없이 이 목록으로 HTML을 만든다.
        public IReadOnlyList<LogRow>? CachedRows { get; init; }
        // HTML 인쇄 파일이 저장될 폴더다.
        public string OutputDirectory { get; init; } = "";
    }

    public static class Save_Print_File
    {
        // 현재 조건의 로그를 HTML 인쇄 파일로 저장한다.
        public static List<string> Run(SavePrintFileRequest request)
        {
            // 조건에 맞는 로그 목록을 지연 실행 형태로 만든다.
            Func<IEnumerable<LogRow>> rowsFactory = GetRowsForPrintExport(request);
            // HTML 상단 조회 조건에 표시될 문구를 만든다.
            string conditionText = BuildConditionText(request);

            return PrintHtmlExporter.Export(
                rowsFactory,
                "프로테크 이력",
                conditionText,
                request.OutputDirectory
            );
        }

        // 현재 화면 모드에 따라 HTML로 내보낼 실제 로그를 선택한다.
        private static Func<IEnumerable<LogRow>> GetRowsForPrintExport(SavePrintFileRequest request)
        {
            if (request.Mode == "category")
            {
                // 예: 화재 필터 상태면 화재 로그만 HTML에 들어간다.
                IReadOnlyList<LogRow> rows = request.SelectedCategoryRows ?? Array.Empty<LogRow>();
                return () => rows;
            }

            if (request.Mode == "date_cache" && request.CachedRows != null)
            {
                // 예: 7일 기간 캐시가 있으면 캐시 목록을 그대로 쓴다.
                IReadOnlyList<LogRow> rows = request.CachedRows;
                return () => rows;
            }

            if (request.Mode == "date" || request.Mode == "date_cache")
            {
                // 캐시가 없으면 전체 로그를 순차 읽으면서 DTIME 기간 필터를 적용한다.
                DateTime? filterStart = IsValidDate(request.StartDate)
                    ? DateTime.Parse(request.StartDate).Date
                    : null;

                // 종료일은 그날 마지막 시간까지 포함한다.
                DateTime? filterEnd = IsValidDate(request.EndDate)
                    ? DateTime.Parse(request.EndDate).Date.AddDays(1).AddTicks(-1)
                    : null;

                return () => request.Repository.StreamAllLogs()
                    .Where(row => IsRowInDateRange(row, filterStart, filterEnd));
            }

            if (request.Mode == "search")
            {
                // 예: 검색어가 "중계기"면 Type/Section/Contents/Packet 중 포함된 로그만 저장한다.
                return () => request.Repository.StreamAllLogs()
                    .Where(row => IsRowMatchedBySearchFields(row, request.Keyword));
            }

            // 전체로그 상태면 모든 로그를 저장한다.
            return () => request.Repository.StreamAllLogs();
        }

        // HTML 상단 조회 조건 카드에 들어갈 문구를 만든다.
        private static string BuildConditionText(SavePrintFileRequest request)
        {
            if (request.Mode == "category")
            {
                // 내부 키를 사용자에게 보이는 이름으로 바꾼다.
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
                // 예: 검색어: MCC.
                return $"검색어: {request.Keyword}";
            }

            if (request.Mode == "date" || request.Mode == "date_cache")
            {
                // 예: 기간: 2026-06-01 ~ 2026-06-07.
                return $"기간: {request.StartDate} ~ {request.EndDate}";
            }

            // 필터가 없는 기본 상태다.
            return "전체 이력";
        }

        // 검색어가 실제 로그 주요 컬럼 중 하나에 포함되는지 확인한다.
        private static bool IsRowMatchedBySearchFields(LogRow row, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                // 검색어가 없으면 모든 로그가 저장 대상이다.
                return true;
            }

            return row.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Action.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Section.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Contents.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Packet.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        // 날짜 문자열이 파싱 가능한지 확인한다.
        private static bool IsValidDate(string value)
        {
            return DateTime.TryParse(value, out _);
        }

        // LogRow.DTime이 지정 기간 안에 있는지 확인한다.
        private static bool IsRowInDateRange(
            LogRow row,
            DateTime? filterStart,
            DateTime? filterEnd)
        {
            if (!filterStart.HasValue && !filterEnd.HasValue)
            {
                // 기간 조건이 없으면 전체 포함이다.
                return true;
            }

            if (!DateTime.TryParse(row.DTime, out DateTime rowDateTime))
            {
                // 날짜 파싱이 안 되는 로그는 기간 인쇄파일에서 제외한다.
                return false;
            }

            if (filterStart.HasValue && rowDateTime < filterStart.Value)
            {
                // 시작일보다 이전이면 제외한다.
                return false;
            }

            if (filterEnd.HasValue && rowDateTime > filterEnd.Value)
            {
                // 종료일보다 이후면 제외한다.
                return false;
            }

            return true;
        }
    }
}
