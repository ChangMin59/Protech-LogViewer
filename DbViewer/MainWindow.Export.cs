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
        // "파일 저장" 버튼을 눌렀을 때 현재 조건의 로그를 TXT 파일로 저장한다.
        private async Task SaveCurrentLogsToTextFileAsync()
        {
            if (_isExportRunning)
            {
                // 저장/인쇄가 이미 진행 중이면 중복 실행하지 않는다.
                return;
            }

            if (_repository == null)
            {
                // 열린 이력 DB가 없으면 저장할 로그가 없다.
                MessageBox.Show(
                    "저장할 이력 파일이 없습니다.",
                    "파일 저장",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                BlockPointerInputAfterModal();
                return;
            }

            // 저장 파일창을 열고 기본 파일명은 현재 로그 기간/카테고리 기준으로 만든다.
            string? outputPath = SelectTextOutputPath();

            if (outputPath == null)
            {
                // 사용자가 취소하면 기존 화면 상태만 유지한다.
                BlockPointerInputAfterModal();
                return;
            }

            _isExportRunning = true;

            try
            {
                // 저장 중 이동 하이라이트를 지우고 진행 overlay를 띄운다.
                ClearMoveTargetHighlight();
                ShowFileSaveProgressOverlay();

                // 비동기 저장 중 화면 상태가 바뀌어도 저장 대상은 클릭 시점 기준으로 고정한다.
                string mode = _currentMode;
                string keyword = _currentKeyword;
                string startDate = _currentStartDate;
                string endDate = _currentEndDate;
                string cacheKey = _activeRowsCacheKey;
                List<string> selectedCategories = _selectedCategories.ToList();
                // 카테고리 모드면 선택된 카테고리 로그만 저장한다.
                List<LogRow>? selectedCategoryRows = mode == "category"
                    ? GetSelectedCategoryRowsFromCache(selectedCategories)
                    : null;
                List<LogRow>? cachedRows = null;

                if (mode == "date_cache")
                {
                    // 기간 조회 캐시가 있으면 전체 DB를 다시 훑지 않고 캐시 목록으로 저장한다.
                    lock (_queryCacheLock)
                    {
                        if (_rowsCacheByKey.TryGetValue(cacheKey, out List<LogRow>? rows))
                        {
                            cachedRows = new List<LogRow>(rows);
                        }
                    }
                }

                // 실제 TXT 저장은 백그라운드에서 실행해 UI 멈춤을 줄인다.
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

                // 저장 성공 위치를 사용자에게 보여준다.
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

                // 저장 실패는 간단 안내로 처리한다. 파일 권한/디스크 오류 등이 원인일 수 있다.
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
                // 성공/실패 모두 overlay와 작업 상태를 초기화한다.
                HideFileSaveProgressOverlay();
                _isExportRunning = false;
            }
        }

        // 숨겨둔 HTML 인쇄파일 저장 기능이다.
        // 현재는 직접 인쇄를 쓰지만 나중에 HTML 파일 저장이 필요하면 이 흐름을 다시 쓸 수 있다.
        private async Task SaveCurrentLogsToPrintHtmlAsync()
        {
            if (_isExportRunning)
            {
                return;
            }

            if (_repository == null)
            {
                // 열린 이력이 없으면 HTML로 저장할 데이터가 없다.
                MessageBox.Show(
                    "저장할 이력 파일이 없습니다.",
                    "파일 저장",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                BlockPointerInputAfterModal();
                return;
            }

            // HTML 파일들을 담을 폴더명을 선택한다.
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

                // 현재 조회 조건을 저장 클릭 시점 기준으로 캡처한다.
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
                    // 기간 캐시가 있으면 그 목록을 HTML 생성에 넘긴다.
                    lock (_queryCacheLock)
                    {
                        if (_rowsCacheByKey.TryGetValue(cacheKey, out List<LogRow>? rows))
                        {
                            cachedRows = new List<LogRow>(rows);
                        }
                    }
                }

                // 10000건 단위 HTML 생성은 백그라운드에서 수행한다.
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

                // 저장 중 바뀐 overlay/상태를 정리하고 현재 페이지를 다시 그린다.
                await LoadCurrentPageAsync(showLoading: false, resetScroll: false);

                string outputDir = Path.GetDirectoryName(savedPaths.FirstOrDefault() ?? "") ?? "";

                // 생성된 파일 개수를 안내한다.
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

                // 실패해도 현재 로그 화면은 다시 그려서 사용 가능 상태로 돌린다.
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

        // 기본 파일 저장 진행 overlay를 띄운다.
        private void ShowFileSaveProgressOverlay()
        {
            ShowFileSaveProgressOverlay(
                "이력 파일 저장 중",
                "데이터가 많으면 시간이 걸릴 수 있습니다.");
        }

        // 지정한 제목/설명으로 진행 overlay를 띄운다.
        private void ShowFileSaveProgressOverlay(string title, string description)
        {
            SetFileSaveProgressText(title, description);
            FileSaveProgressOverlay.Visibility = Visibility.Visible;
        }

        // 파일 저장/인쇄 준비 overlay를 숨긴다.
        private void HideFileSaveProgressOverlay()
        {
            FileSaveProgressOverlay.Visibility = Visibility.Collapsed;
        }

        // overlay 텍스트를 이름으로 찾아 안전하게 바꾼다.
        // XAML 이름 연결이 늦는 경우를 피하려고 FindName을 사용한다.
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

        // TXT 저장 파일창을 열고 저장 경로를 반환한다.
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
                // 사용자가 취소한 경우다.
                return null;
            }

            // 상대 경로가 들어와도 절대 경로로 통일한다.
            return Path.GetFullPath(dialog.FileName);
        }

        // TXT 저장 기본 파일명을 만든다.
        // 예: 이력_230124~241012.txt, 화재 필터면 이력_230124~241012(화재).txt.
        private string BuildDefaultTextExportFileName()
        {
            string dateRangeText = BuildTextExportDateRangeText();
            string categoryText = BuildTextExportCategorySuffix();

            return SanitizeFileName($"이력_{dateRangeText}{categoryText}.txt");
        }

        // 저장 파일명에 들어갈 기간 문자열을 만든다.
        private string BuildTextExportDateRangeText()
        {
            // 카테고리 모드에서는 필터 전 기본 조회 조건의 기간을 사용한다.
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
                // 예: 2023-01-24~2024-10-12 -> 230124~241012.
                return $"{compactStart}~{compactEnd}";
            }

            if (_dbStartDate.HasValue && _dbEndDate.HasValue)
            {
                // 전체로그 상태면 DB 전체 날짜 범위를 파일명에 쓴다.
                return $"{_dbStartDate.Value:yyMMdd}~{_dbEndDate.Value:yyMMdd}";
            }

            if (_repository != null)
            {
                try
                {
                    // UI 날짜 범위가 없으면 DB에서 실제 날짜 범위를 다시 읽는다.
                    (string StartDate, string EndDate) range = _repository.GetLogDateRange();

                    if (TryFormatCompactDate(range.StartDate, out string dbCompactStart) &&
                        TryFormatCompactDate(range.EndDate, out string dbCompactEnd))
                    {
                        return $"{dbCompactStart}~{dbCompactEnd}";
                    }
                }
                catch
                {
                    // 날짜 범위 조회 실패 시 아래 현재 날짜 fallback을 사용한다.
                }
            }

            // 열린 DB 날짜를 알 수 없으면 오늘 날짜만 파일명에 넣는다.
            return DateTime.Now.ToString("yyMMdd");
        }

        // 카테고리 필터 저장일 때 파일명 뒤에 카테고리 이름을 붙인다.
        private string BuildTextExportCategorySuffix()
        {
            if (_currentMode != "category" || _selectedCategories.Count == 0)
            {
                // 전체/기간/검색 저장은 카테고리 suffix를 붙이지 않는다.
                return "";
            }

            // 예: fire -> 화재, relay_fault -> 중계기 고장.
            List<string> categoryNames = _selectedCategories
                .Select(category => _categoryDisplayNames.TryGetValue(category, out string? name)
                    ? name
                    : category)
                .ToList();

            return $"({string.Join("_", categoryNames)})";
        }

        // yyyy-MM-dd 문자열을 yyMMdd 파일명 형식으로 바꾼다.
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

        // Windows 파일명에 사용할 수 없는 문자는 _로 바꾼다.
        private static string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();

            foreach (char invalidChar in invalidChars)
            {
                // 예: /, \, :, *, ?, ", <, >, |.
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName;
        }

        // HTML 인쇄파일을 저장할 폴더명을 선택한다.
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
                // 사용자가 취소한 경우다.
                return null;
            }

            string outputDirectory = Path.GetFullPath(dialog.FileName);

            if (File.Exists(outputDirectory))
            {
                // 폴더로 만들 이름과 같은 파일이 이미 있으면 생성할 수 없다.
                MessageBox.Show(
                    "같은 이름의 파일이 이미 있습니다.\n\n다른 폴더명을 입력하세요.",
                    "파일 저장",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return null;
            }

            // 선택한 이름을 폴더로 만들고 그 안에 HTML 파일들을 생성한다.
            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        // 실행 파일이 있는 폴더를 구한다.
        // 저장 파일창의 기본 위치로 사용한다.
        private static string GetExecutableDirectory()
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;

            if (!string.IsNullOrWhiteSpace(exePath))
            {
                string? exeDirectory = Path.GetDirectoryName(exePath);

                if (!string.IsNullOrWhiteSpace(exeDirectory))
                {
                    // 실제 exe로 실행 중이면 exe 폴더를 우선한다.
                    return exeDirectory;
                }
            }

            // 개발 실행처럼 exe 경로를 못 구하면 AppContext.BaseDirectory를 쓴다.
            return AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
    }
}
