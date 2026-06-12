using System.Windows;
using System.Windows.Documents;

namespace DbViewer.Services.Print
{
    public sealed class Page_Range_Print_Paginator : DocumentPaginator
    {
        private readonly DocumentPaginator _source;
        private readonly int _startPageIndex;
        private readonly int _pageCount;

        public Page_Range_Print_Paginator(DocumentPaginator source, int pageFrom, int pageTo)
        {
            _source = source;

            int sourcePageCount = Math.Max(1, source.PageCount);
            int safeFrom = Math.Clamp(pageFrom, 1, sourcePageCount);
            int safeTo = Math.Clamp(pageTo, safeFrom, sourcePageCount);

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

        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= _pageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber));
            }

            return _source.GetPage(_startPageIndex + pageNumber);
        }
    }
}
