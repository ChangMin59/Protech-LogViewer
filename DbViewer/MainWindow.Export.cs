using DbViewer.Models;
using DbViewer.Services.Export;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            string? outputDirectory = SelectPrintOutputDirectory();

            if (outputDirectory == null)
            {
                BlockPointerInputAfterModal();
                return;
            }

            _isExportRunning = true;

            try
            {
                ClearMoveTargetHighlight();
                ShowFileSaveProgressOverlay();

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
                        CachedRows = cachedRows,
                        OutputDirectory = outputDirectory
                    })
                );

                HideFileSaveProgressOverlay();

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
                HideFileSaveProgressOverlay();

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
                HideFileSaveProgressOverlay();
                _isExportRunning = false;
            }
        }

        private void ShowFileSaveProgressOverlay()
        {
            FileSaveProgressOverlay.Visibility = Visibility.Visible;
        }

        private void HideFileSaveProgressOverlay()
        {
            FileSaveProgressOverlay.Visibility = Visibility.Collapsed;
        }

        private string? SelectPrintOutputDirectory()
        {
            SaveFileDialog dialog = new()
            {
                Title = "이력 인쇄 파일 저장",
                InitialDirectory = GetExecutableDirectory(),
                FileName = "이력 인쇄 파일",
                AddExtension = false,
                CheckFileExists = false,
                CheckPathExists = true,
                OverwritePrompt = false,
                ValidateNames = true,
                Filter = "폴더명|*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return null;
            }

            string outputDirectory = Path.GetFullPath(dialog.FileName);

            if (File.Exists(outputDirectory))
            {
                MessageBox.Show(
                    "같은 이름의 파일이 이미 있습니다.\n\n다른 폴더명을 입력하세요.",
                    "파일 저장",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return null;
            }

            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        private static string GetExecutableDirectory()
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;

            if (!string.IsNullOrWhiteSpace(exePath))
            {
                string? exeDirectory = Path.GetDirectoryName(exePath);

                if (!string.IsNullOrWhiteSpace(exeDirectory))
                {
                    return exeDirectory;
                }
            }

            return AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
    }
}
