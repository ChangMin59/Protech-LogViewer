using DbViewer.Models;
using DbViewer.Services.Common;
using DbViewer.Services.View;

namespace DbViewer.Services.Print
{
    // 인쇄 버튼을 눌렀을 때 현재 화면 조건을 문서 생성용으로 넘기는 요청값이다.
    public sealed class PrintLogRequest
    {
        // 실제 DB 또는 TXT 변환 임시 DB를 읽는 저장소다.
        public LogRepository Repository { get; init; } = null!;
        // 현재 조회 모드. 예: all, date, date_cache, search, category.
        public string Mode { get; init; } = "";
        // 검색 모드에서 Type/Action/Section/Contents/Packet에 적용할 키워드다.
        public string Keyword { get; init; } = "";
        // 기간 모드 시작일. 예: 2026-06-01.
        public string StartDate { get; init; } = "";
        // 기간 모드 종료일. 예: 2026-06-07.
        public string EndDate { get; init; } = "";
        // 카테고리 모드에서 선택된 내부 키 목록이다. 예: fire, relay_fault.
        public IReadOnlyList<string> SelectedCategories { get; init; } = Array.Empty<string>();
        // 내부 카테고리 키를 화면 이름으로 바꾼다. 예: relay_fault -> 중계기 고장.
        public IReadOnlyDictionary<string, string> CategoryDisplayNames { get; init; } =
            new Dictionary<string, string>();
        // 카테고리 모드에서 이미 필터된 실제 로그 목록이다.
        public IReadOnlyList<LogRow>? SelectedCategoryRows { get; init; }
        // 기간 캐시가 있으면 인쇄 전 DB 전체 재스캔을 피한다.
        public IReadOnlyList<LogRow>? CachedRows { get; init; }
    }

    // 미리보기/인쇄에 필요한 최종 문서 데이터다.
    public sealed class PrintLogDocumentData
    {
        // 실제 인쇄될 로그 목록이다.
        public IReadOnlyList<LogRow> Rows { get; init; } = Array.Empty<LogRow>();
        // 인쇄 상단 조회 조건 문구다. 예: "전체 이력", "기간: 2026-06-01 ~ 2026-06-07".
        public string ConditionText { get; init; } = "";
        // 요약 카드에 표시할 카테고리별 건수다.
        public Dictionary<string, int> SummaryCounts { get; init; } = new();
    }

    public static class Prepare_Print_Document
    {
        // 현재 조건에 맞는 로그를 모으고, 인쇄 제목 아래 표시할 조건/요약 건수를 계산한다.
        public static PrintLogDocumentData Run(PrintLogRequest request)
        {
            // Paginator는 페이지별 인덱스 접근이 필요하므로 인쇄 대상은 List로 확정한다.
            List<LogRow> rows = GetRowsForPrint(request).ToList();

            return new PrintLogDocumentData
            {
                Rows = rows,
                ConditionText = BuildConditionText(request),
                SummaryCounts = CalculateLogSummary(rows)
            };
        }

        // 현재 화면 모드에 따라 실제 인쇄할 로그를 선택한다.
        private static IEnumerable<LogRow> GetRowsForPrint(PrintLogRequest request)
        {
            if (request.Mode == "category")
            {
                // 예: 화재 필터 상태에서 인쇄하면 화재 로그만 인쇄한다.
                return request.SelectedCategoryRows ?? Array.Empty<LogRow>();
            }

            if (request.Mode == "date_cache" && request.CachedRows != null)
            {
                // 예: 기간 조회 캐시가 있으면 그 기간 로그 목록을 그대로 인쇄한다.
                return request.CachedRows;
            }

            if (request.Mode == "date" || request.Mode == "date_cache")
            {
                // 캐시가 없으면 전체 DB를 스트리밍하면서 DTIME 기간 조건을 적용한다.
                DateTime? filterStart = IsValidDate(request.StartDate)
                    ? DateTime.Parse(request.StartDate).Date
                    : null;

                // 종료일은 해당 날짜의 마지막 틱까지 포함한다.
                DateTime? filterEnd = IsValidDate(request.EndDate)
                    ? DateTime.Parse(request.EndDate).Date.AddDays(1).AddTicks(-1)
                    : null;

                return request.Repository.StreamAllLogs()
                    .Where(row => IsRowInDateRange(row, filterStart, filterEnd));
            }

            if (request.Mode == "search")
            {
                // 예: 검색어 "MCC"면 Type/Section/Contents/Packet에 MCC가 포함된 로그만 인쇄한다.
                return request.Repository.StreamAllLogs()
                    .Where(row => IsRowMatchedBySearchFields(row, request.Keyword));
            }

            // 전체로그 상태면 모든 로그를 최신순으로 인쇄한다.
            return request.Repository.StreamAllLogs();
        }

        // 인쇄 상단 조회 조건 카드에 들어갈 문구를 만든다.
        private static string BuildConditionText(PrintLogRequest request)
        {
            if (request.Mode == "category")
            {
                // 내부 키를 화면 표시 이름으로 바꾼다.
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
                // 예: 검색어: 중계기.
                return $"검색어: {request.Keyword}";
            }

            if (request.Mode == "date" || request.Mode == "date_cache")
            {
                // 예: 기간: 2026-06-01 ~ 2026-06-07.
                return $"기간: {request.StartDate} ~ {request.EndDate}";
            }

            // 기본 전체 인쇄 상태다.
            return "전체 이력";
        }

        // 인쇄 상단 요약 카드에 들어갈 카테고리별 건수를 계산한다.
        private static Dictionary<string, int> CalculateLogSummary(IReadOnlyList<LogRow> rows)
        {
            // 인쇄에서 보여줄 주요 카테고리만 요약한다.
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

            for (int index = 0; index < rows.Count; index++)
            {
                LogRow row = rows[index];
                // 화면 분류와 동일한 기준으로 rowTag를 만든다.
                string rowTag = LogClassifier.ClassifyRowTag(row, index);
                // MCC처럼 중복 카운트될 수 있는 카테고리까지 가져온다.
                List<string> categories = LogClassifier.GetCategoryKeys(row, rowTag);

                foreach (string category in categories)
                {
                    if (!summary.ContainsKey(category))
                    {
                        // 수신기고장/중계반고장 등 인쇄 요약에 표시하지 않는 카테고리는 건너뛴다.
                        continue;
                    }

                    summary[category]++;
                }
            }

            return summary;
        }

        // 검색어가 실제 로그 주요 컬럼 중 하나에 포함되는지 확인한다.
        private static bool IsRowMatchedBySearchFields(LogRow row, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                // 빈 검색어는 모든 로그 포함이다.
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

        // LogRow.DTime이 선택 기간 안에 들어가는지 확인한다.
        private static bool IsRowInDateRange(
            LogRow row,
            DateTime? filterStart,
            DateTime? filterEnd)
        {
            if (!filterStart.HasValue && !filterEnd.HasValue)
            {
                // 기간 조건이 없으면 모두 포함한다.
                return true;
            }

            if (!DateTime.TryParse(row.DTime, out DateTime rowDateTime))
            {
                // DTIME이 깨진 로그는 기간 인쇄에서 제외한다.
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
    }
}
