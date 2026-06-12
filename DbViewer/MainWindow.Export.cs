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
using System.Windows.Controls;

namespace DbViewer
{
    public partial class MainWindow
    {
        private async Task SaveCurrentLogsToTextFileAsync()
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

            string? outputPath = SelectTextOutputPath();

            if (outputPath == null)
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

                string savedPath = await Task.Run(() =>
                    Save_Text_File.Run(new SaveTextFileRequest
                    {
                        Repository = _repository,
                        Mode = mode,
                        Keyword = keyword,
                        StartDate = startDate,
                        EndDate = endDate,
                        SelectedCategoryRows = selectedCategoryRows,
                        CachedRows = cachedRows,
                        OutputPath = outputPath
                    })
                );

                HideFileSaveProgressOverlay();

                MessageBox.Show(
                    "이력 TXT 파일을 저장했습니다.\n\n" +
                    $"저장 위치:\n{savedPath}",
                    "파일 저장",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                BlockPointerInputAfterModal();
            }
            catch
            {
                HideFileSaveProgressOverlay();

                MessageBox.Show(
                    "이력 TXT 파일 저장에 실패했습니다.",
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
            ShowFileSaveProgressOverlay(
                "이력 파일 저장 중",
                "데이터가 많으면 시간이 걸릴 수 있습니다.");
        }

        private void ShowFileSaveProgressOverlay(string title, string description)
        {
            SetFileSaveProgressText(title, description);
            FileSaveProgressOverlay.Visibility = Visibility.Visible;
        }

        private void HideFileSaveProgressOverlay()
        {
            FileSaveProgressOverlay.Visibility = Visibility.Collapsed;
        }

        private void SetFileSaveProgressText(string title, string description)
        {
            if (FindName("FileSaveProgressTitleText") is TextBlock titleText)
            {
                titleText.Text = title;
            }

            if (FindName("FileSaveProgressDescriptionText") is TextBlock descriptionText)
            {
                descriptionText.Text = description;
            }
        }

        private string? SelectTextOutputPath()
        {
            SaveFileDialog dialog = new()
            {
                Title = "이력 TXT 파일 저장",
                InitialDirectory = GetExecutableDirectory(),
                FileName = BuildDefaultTextExportFileName(),
                AddExtension = true,
                DefaultExt = ".txt",
                CheckPathExists = true,
                OverwritePrompt = true,
                ValidateNames = true,
                Filter = "텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return null;
            }

            return Path.GetFullPath(dialog.FileName);
        }

        private string BuildDefaultTextExportFileName()
        {
            string dateRangeText = BuildTextExportDateRangeText();
            string categoryText = BuildTextExportCategorySuffix();

            return SanitizeFileName($"이력_{dateRangeText}{categoryText}.txt");
        }

        private string BuildTextExportDateRangeText()
        {
            string mode = _currentMode == "category"
                ? _baseModeBeforeCategory
                : _currentMode;
            string startDate = _currentMode == "category"
                ? _baseStartDateBeforeCategory
                : _currentStartDate;
            string endDate = _currentMode == "category"
                ? _baseEndDateBeforeCategory
                : _currentEndDate;

            if ((mode == "date_cache" || mode == "date") &&
                TryFormatCompactDate(startDate, out string compactStart) &&
                TryFormatCompactDate(endDate, out string compactEnd))
            {
                return $"{compactStart}~{compactEnd}";
            }

            if (_dbStartDate.HasValue && _dbEndDate.HasValue)
            {
                return $"{_dbStartDate.Value:yyMMdd}~{_dbEndDate.Value:yyMMdd}";
            }

            if (_repository != null)
            {
                try
                {
                    (string StartDate, string EndDate) range = _repository.GetLogDateRange();

                    if (TryFormatCompactDate(range.StartDate, out string dbCompactStart) &&
                        TryFormatCompactDate(range.EndDate, out string dbCompactEnd))
                    {
                        return $"{dbCompactStart}~{dbCompactEnd}";
                    }
                }
                catch
                {
                }
            }

            return DateTime.Now.ToString("yyMMdd");
        }

        private string BuildTextExportCategorySuffix()
        {
            if (_currentMode != "category" || _selectedCategories.Count == 0)
            {
                return "";
            }

            List<string> categoryNames = _selectedCategories
                .Select(category => _categoryDisplayNames.TryGetValue(category, out string? name)
                    ? name
                    : category)
                .ToList();

            return $"({string.Join("_", categoryNames)})";
        }

        private static bool TryFormatCompactDate(string value, out string compactDate)
        {
            if (DateTime.TryParse(value, out DateTime date))
            {
                compactDate = date.ToString("yyMMdd");
                return true;
            }

            compactDate = "";
            return false;
        }

        private static string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();

            foreach (char invalidChar in invalidChars)
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName;
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
