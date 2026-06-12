using System.Windows;
using System.Windows.Documents;

namespace DbViewer.Services.Print
{
    // 인쇄 대화상자에서 사용자가 지정한 페이지 범위만 실제 프린터로 넘기는 래퍼다.
    // 예: 전체 10페이지 중 1~1을 선택하면 원본 paginator의 0번째 페이지만 출력한다.
    public sealed class Page_Range_Print_Paginator : DocumentPaginator
    {
        private readonly DocumentPaginator _source;
        private readonly int _startPageIndex;
        private readonly int _pageCount;

        // pageFrom/pageTo는 인쇄창 기준 1부터 시작하는 페이지 번호다.
        public Page_Range_Print_Paginator(DocumentPaginator source, int pageFrom, int pageTo)
        {
            _source = source;

            // 사용자가 범위를 잘못 넣어도 실제 페이지 수 안으로 보정한다.
            int sourcePageCount = Math.Max(1, source.PageCount);
            int safeFrom = Math.Clamp(pageFrom, 1, sourcePageCount);
            int safeTo = Math.Clamp(pageTo, safeFrom, sourcePageCount);

            // DocumentPaginator.GetPage는 0부터 시작하므로 1을 뺀다.
            _startPageIndex = safeFrom - 1;
            _pageCount = safeTo - safeFrom + 1;
            PageSize = source.PageSize;
        }

        public override bool IsPageCountValid => true;

        public override int PageCount => _pageCount;

        public override Size PageSize
        {
            get => _source.PageSize;
            set => _source.PageSize = value;
        }

        public override IDocumentPaginatorSource? Source => null;

        // 요청받은 범위 내 pageNumber를 원본 paginator의 실제 페이지 번호로 변환한다.
        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= _pageCount)
            {
                // 인쇄 시스템이 범위 밖 페이지를 요청하면 명확히 예외 처리한다.
                throw new ArgumentOutOfRangeException(nameof(pageNumber));
            }

            return _source.GetPage(_startPageIndex + pageNumber);
        }
    }
}
