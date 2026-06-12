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
        // 인쇄 버튼을 눌렀을 때 현재 조건의 로그를 미리보기 창으로 보낸다.
        private async Task PrintCurrentLogsAsync()
        {
            if (_isExportRunning)
            {
                // 저장/인쇄 작업이 이미 돌고 있으면 중복 실행하지 않는다.
                return;
            }

            if (_repository == null)
            {
                // 열린 이력 DB가 없으면 인쇄할 데이터가 없다.
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
                // 인쇄 준비 중에는 이전 이동모드 하이라이트를 지운다.
                ClearMoveTargetHighlight();
                ShowPrintProgressOverlay();

                // 현재 repository와 화면 조건을 캡처한다.
                LogRepository repository = _repository;
                PrintLogRequest request = CreatePrintLogRequest(repository);

                // 전체/기간/검색/카테고리 조건에 맞는 실제 인쇄 대상 로그와 요약을 백그라운드에서 만든다.
                PrintLogDocumentData documentData = await Task.Run(() =>
                    Prepare_Print_Document.Run(request)
                );

                HideFileSaveProgressOverlay();

                if (documentData.Rows.Count == 0)
                {
                    // 현재 조건에 걸린 로그가 없으면 빈 미리보기 대신 안내만 띄운다.
                    MessageBox.Show(
                        "인쇄할 이력이 없습니다.",
                        "인쇄",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    BlockPointerInputAfterModal();
                    return;
                }

                // WPF 직접 인쇄 미리보기 창을 띄운다.
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

                // 문서 생성/미리보기/프린터 준비 중 예외를 사용자에게 보여준다.
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
                // 성공/실패 모두 진행 overlay를 닫고 다음 저장/인쇄를 허용한다.
                HideFileSaveProgressOverlay();
                _isExportRunning = false;
            }
        }

        // 현재 화면 상태를 PrintLogRequest로 캡처한다.
        // 예: 기간 모드면 StartDate/EndDate, 카테고리 모드면 SelectedCategoryRows를 넣는다.
        private PrintLogRequest CreatePrintLogRequest(LogRepository repository)
        {
            // 비동기 준비 중 화면 상태가 바뀌어도 이 인쇄 작업은 캡처 시점 기준으로 동작한다.
            string mode = _currentMode;
            string keyword = _currentKeyword;
            string startDate = _currentStartDate;
            string endDate = _currentEndDate;
            string cacheKey = _activeRowsCacheKey;
            List<string> selectedCategories = _selectedCategories.ToList();

            // 카테고리 모드면 이미 캐시에 분류된 실제 로그 목록을 복사한다.
            List<LogRow>? selectedCategoryRows = mode == "category"
                ? GetSelectedCategoryRowsFromCache(selectedCategories)
                : null;
            List<LogRow>? cachedRows = null;

            if (mode == "date_cache")
            {
                // 기간 조회 캐시가 있으면 인쇄 준비에서 DB 전체 재스캔을 피한다.
                lock (_queryCacheLock)
                {
                    if (_rowsCacheByKey.TryGetValue(cacheKey, out List<LogRow>? rows))
                    {
                        cachedRows = new List<LogRow>(rows);
                    }
                }
            }

            // Prepare_Print_Document가 이 값으로 최종 인쇄 문서를 만든다.
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

        // 인쇄 준비 overlay를 파일 저장 overlay UI로 재사용해 표시한다.
        private void ShowPrintProgressOverlay()
        {
            ShowFileSaveProgressOverlay(
                "인쇄 준비 중",
                "인쇄할 이력을 정리하고 있습니다.");
        }

        // 예외 타입과 메시지를 사용자에게 보여줄 문자열로 만든다.
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
