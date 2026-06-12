using DbViewer.Models;
using DbViewer.Services.View;

namespace DbViewer.Services.Export
{
    // TXT 저장 버튼을 눌렀을 때 필요한 현재 조회 조건이다.
    // Mode는 all/date/date_cache/search/category 중 하나로 들어온다.
    public sealed class SaveTextFileRequest
    {
        // 실제 DB 또는 TXT 변환 임시 DB를 읽는 저장소다.
        public LogRepository Repository { get; init; } = null!;
        // 현재 조회 모드. 예: "date", "search", "category".
        public string Mode { get; init; } = "";
        // 검색 모드일 때 Type/Action/Section/Contents/Packet에서 찾을 키워드다.
        public string Keyword { get; init; } = "";
        // 기간 모드 시작일. 예: 2026-06-01.
        public string StartDate { get; init; } = "";
        // 기간 모드 종료일. 예: 2026-06-07.
        public string EndDate { get; init; } = "";
        // 카테고리 모드에서 이미 필터된 로그 목록이다. 예: 화재만 선택된 로그.
        public IReadOnlyList<LogRow>? SelectedCategoryRows { get; init; }
        // 기간 캐시가 있으면 DB를 다시 훑지 않고 캐시된 기간 로그를 저장한다.
        public IReadOnlyList<LogRow>? CachedRows { get; init; }
        // 사용자가 저장 파일창에서 지정한 TXT 경로다.
        public string OutputPath { get; init; } = "";
    }

    public static class Save_Text_File
    {
        // 현재 조회 조건에 맞는 로그 흐름을 만든 뒤 TXT 파일로 저장한다.
        public static string Run(SaveTextFileRequest request)
        {
            Func<IEnumerable<LogRow>> rowsFactory = GetRowsForTextExport(request);

            return TextFileExporter.Export(
                rowsFactory,
                request.OutputPath
            );
        }

        // 현재 화면 모드에 따라 저장할 실제 로그 목록을 선택한다.
        private static Func<IEnumerable<LogRow>> GetRowsForTextExport(SaveTextFileRequest request)
        {
            if (request.Mode == "category")
            {
                // 예: 화재 필터 선택 상태면 SelectedCategoryRows에 들어 있는 화재 로그만 저장한다.
                IReadOnlyList<LogRow> rows = request.SelectedCategoryRows ?? Array.Empty<LogRow>();
                return () => rows;
            }

            if (request.Mode == "date_cache" && request.CachedRows != null)
            {
                // 예: 7일/30일/사용자 기간 로그가 이미 캐시되어 있으면 그 목록을 그대로 저장한다.
                IReadOnlyList<LogRow> rows = request.CachedRows;
                return () => rows;
            }

            if (request.Mode == "date" || request.Mode == "date_cache")
            {
                // 캐시가 없으면 전체 DB를 순차로 읽으면서 DTIME 기간 조건을 적용한다.
                DateTime? filterStart = IsValidDate(request.StartDate)
                    ? DateTime.Parse(request.StartDate).Date
                    : null;

                // 종료일은 해당 날짜 23:59:59.9999999까지 포함한다.
                DateTime? filterEnd = IsValidDate(request.EndDate)
                    ? DateTime.Parse(request.EndDate).Date.AddDays(1).AddTicks(-1)
                    : null;

                return () => request.Repository.StreamAllLogs()
                    .Where(row => IsRowInDateRange(row, filterStart, filterEnd));
            }

            if (request.Mode == "search")
            {
                // 예: 검색어 "MCC"이면 Type/Action/Section/Contents/Packet 중 MCC 포함 로그만 저장한다.
                return () => request.Repository.StreamAllLogs()
                    .Where(row => IsRowMatchedBySearchFields(row, request.Keyword));
            }

            // 전체로그 상태면 DB의 모든 로그를 최신순으로 저장한다.
            return () => request.Repository.StreamAllLogs();
        }

        // 검색어가 실제 로그 주요 컬럼 중 하나에 들어 있는지 확인한다.
        private static bool IsRowMatchedBySearchFields(LogRow row, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                // 검색어가 비어 있으면 전체 저장과 동일하게 모두 포함한다.
                return true;
            }

            // 예: "중계기"는 Type=중계기고장, Section=005중계기, Contents=중계기 통신고장에 모두 매칭된다.
            return row.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Action.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Section.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Contents.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   row.Packet.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        // 문자열이 DateTime으로 해석 가능한지 확인한다.
        private static bool IsValidDate(string value)
        {
            return DateTime.TryParse(value, out _);
        }

        // LogRow의 DTIME이 사용자가 선택한 시작일/종료일 범위에 들어가는지 확인한다.
        private static bool IsRowInDateRange(
            LogRow row,
            DateTime? filterStart,
            DateTime? filterEnd)
        {
            if (!filterStart.HasValue && !filterEnd.HasValue)
            {
                // 기간 조건이 없으면 모든 로그가 저장 대상이다.
                return true;
            }

            if (!DateTime.TryParse(row.DTime, out DateTime rowDateTime))
            {
                // DTIME이 "yyyy-MM-dd HH:mm:ss"로 파싱되지 않으면 기간 저장에서 제외한다.
                return false;
            }

            if (filterStart.HasValue && rowDateTime < filterStart.Value)
            {
                // 예: 시작일이 2026-06-01인데 2026-05-31 로그면 제외한다.
                return false;
            }

            if (filterEnd.HasValue && rowDateTime > filterEnd.Value)
            {
                // 예: 종료일이 2026-06-07인데 2026-06-08 로그면 제외한다.
                return false;
            }

            return true;
        }
    }
}
