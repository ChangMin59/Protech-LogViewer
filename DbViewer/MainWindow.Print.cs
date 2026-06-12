using DbViewer.Models;
using DbViewer.Services.Print;
using DbViewer.Services.View;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace DbViewer
{
    public partial class MainWindow
    {
        private async Task PrintCurrentLogsAsync()
        {
            if (_isExportRunning)
            {
                return;
            }

            if (_repository == null)
            {
                MessageBox.Show(
                    "인쇄할 이력 파일이 없습니다.",
                    "인쇄",
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
                ShowPrintProgressOverlay();

                LogRepository repository = _repository;
                PrintLogRequest request = CreatePrintLogRequest(repository);

                PrintLogDocumentData documentData = await Task.Run(() =>
                    Prepare_Print_Document.Run(request)
                );

                HideFileSaveProgressOverlay();

                if (documentData.Rows.Count == 0)
                {
                    MessageBox.Show(
                        "인쇄할 이력이 없습니다.",
                        "인쇄",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    BlockPointerInputAfterModal();
                    return;
                }

                Print_Preview_Window previewWindow = new(documentData)
                {
                    Owner = this
                };
                previewWindow.ShowDialog();
                BlockPointerInputAfterModal();
            }
            catch (Exception ex)
            {
                HideFileSaveProgressOverlay();

                MessageBox.Show(
                    "이력 인쇄에 실패했습니다.\n\n" +
                    $"오류: {BuildPrintErrorMessage(ex)}",
                    "인쇄",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                BlockPointerInputAfterModal();
            }
            finally
            {
                HideFileSaveProgressOverlay();
                _isExportRunning = false;
            }
        }

        private PrintLogRequest CreatePrintLogRequest(LogRepository repository)
        {
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

            return new PrintLogRequest
            {
                Repository = repository,
                Mode = mode,
                Keyword = keyword,
                StartDate = startDate,
                EndDate = endDate,
                SelectedCategories = selectedCategories,
                CategoryDisplayNames = new Dictionary<string, string>(_categoryDisplayNames),
                SelectedCategoryRows = selectedCategoryRows,
                CachedRows = cachedRows
            };
        }

        private void ShowPrintProgressOverlay()
        {
            ShowFileSaveProgressOverlay(
                "인쇄 준비 중",
                "인쇄할 이력을 정리하고 있습니다.");
        }

        private static string BuildPrintErrorMessage(Exception ex)
        {
            if (ex.InnerException == null)
            {
                return $"{ex.GetType().Name}: {ex.Message}";
            }

            return $"{ex.GetType().Name}: {ex.Message}\n" +
                   $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        }
    }
}
