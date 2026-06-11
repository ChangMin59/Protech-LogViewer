using DbViewer.Models;
using DbViewer.Services.Export;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace DbViewer
{
    public partial class MainWindow
    {
        private async Task SaveCurrentLogsToPrintHtmlAsync()
        {
            if (_isExportRunning)
            {
                return;
            }

            if (_repository == null)
            {
                MessageBox.Show(
                    "저장할 이력 파일이 없습니다.",
                    "파일 저장",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                BlockPointerInputAfterModal();
                return;
            }

            _isExportRunning = true;

            try
            {
                ClearMoveTargetHighlight();

                string mode = _currentMode;
                string keyword = _currentKeyword;
                string startDate = _currentStartDate;
                string endDate = _currentEndDate;
                string cacheKey = _activeRowsCacheKey;
                List<string> selectedCategories = _selectedCategories.ToList();
                List<LogRow>? selectedCategoryRows = mode == "category"
                    ? GetSelectedCategoryRowsFromCache(selectedCategories)
                    : null;
                List<LogRow>? cachedRows = null;

                if (mode == "date_cache")
                {
                    lock (_queryCacheLock)
                    {
                        if (_rowsCacheByKey.TryGetValue(cacheKey, out List<LogRow>? rows))
                        {
                            cachedRows = new List<LogRow>(rows);
                        }
                    }
                }

                List<string> savedPaths = await Task.Run(() =>
                    Save_Print_File.Run(new SavePrintFileRequest
                    {
                        Repository = _repository,
                        Mode = mode,
                        Keyword = keyword,
                        StartDate = startDate,
                        EndDate = endDate,
                        SelectedCategories = selectedCategories,
                        CategoryDisplayNames = _categoryDisplayNames,
                        SelectedCategoryRows = selectedCategoryRows,
                        CachedRows = cachedRows
                    })
                );

                await LoadCurrentPageAsync(showLoading: false, resetScroll: false);

                string outputDir = Path.GetDirectoryName(savedPaths.FirstOrDefault() ?? "") ?? "";

                MessageBox.Show(
                    "이력 인쇄 파일을 저장했습니다.\n\n" +
                    $"저장 위치:\n{outputDir}\n\n" +
                    $"생성 파일: {savedPaths.Count:N0}개",
                    "파일 저장",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                BlockPointerInputAfterModal();
            }
            catch
            {
                await LoadCurrentPageAsync(showLoading: false, resetScroll: false);

                MessageBox.Show(
                    "이력 인쇄 파일 저장에 실패했습니다.",
                    "파일 저장",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                BlockPointerInputAfterModal();
            }
            finally
            {
                _isExportRunning = false;
            }
        }
    }
}
