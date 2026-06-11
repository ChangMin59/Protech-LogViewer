using DbViewer.Services.Recovery;
using DbViewer.Services.Render;
using System.Threading.Tasks;
using System.Windows;

namespace DbViewer
{
    public partial class MainWindow
    {
        private async Task RecoverHistoryFileAsync()
        {
            if (_isRecoveryRunning)
            {
                return;
            }

            MessageBoxResult confirmResult = ShowRecoveryConfirmMessage();
            BlockPointerInputAfterModal();

            if (confirmResult != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                CancelRunningJobs();
                ClearMoveTargetHighlight();

                _selectedCategories.Clear();
                UpdateCategoryCardActiveStates();

                ShowRecoveryProgressOverlay();

                StartRecoveryResult startResult = await Start_Recovery.RunAsync();

                HideRecoveryProgressOverlay();

                if (!startResult.SourceFound || startResult.RecoveryResult == null)
                {
                    ShowRecoverySourceMissingMessage();
                    BlockPointerInputAfterModal();
                    return;
                }

                DbRecoveryResult result = startResult.RecoveryResult;

                if (!result.Success)
                {
                    ShowRecoveryFailedState(MessageBoxImage.Warning);
                    BlockPointerInputAfterModal();
                    return;
                }

                MessageBoxResult openResult = ShowRecoverySuccessMessage();
                BlockPointerInputAfterModal();

                if (openResult == MessageBoxResult.Yes)
                {
                    await OpenHistoryFileByPathAsync(result.RecoveredDbPath, "이력 복구");
                    return;
                }

                FixedTimeRowsPanel.Children.Clear();
                LogRowsPanel.Children.Clear();

                FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedEmptyCell());
                LogRowsPanel.Children.Add(Render_Log_Row.CreateEmptyRow(""));
            }
            catch
            {
                HideRecoveryProgressOverlay();
                ShowRecoveryFailedState(MessageBoxImage.Error);
                BlockPointerInputAfterModal();
            }
            finally
            {
                _isRecoveryRunning = false;
            }
        }

        private void ShowRecoveryProgressOverlay()
        {
            _isRecoveryRunning = true;
            HistoryRecoverProgressOverlay.Visibility = Visibility.Visible;
        }

        private void HideRecoveryProgressOverlay()
        {
            HistoryRecoverProgressOverlay.Visibility = Visibility.Collapsed;
        }

        private static MessageBoxResult ShowRecoveryConfirmMessage()
        {
            return MessageBox.Show(
                "이력 복구를 진행할까요?\n\n" +
                "복구 중에는 프로그램을 종료하지 마세요.",
                "이력 복구",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
        }

        private static void ShowRecoverySourceMissingMessage()
        {
            MessageBox.Show(
                "복구할 이력 파일을 찾을 수 없습니다.",
                "이력 복구",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        private void ShowRecoveryFailedState(MessageBoxImage icon, string detailMessage = "")
        {
            FixedTimeRowsPanel.Children.Clear();
            LogRowsPanel.Children.Clear();

            FixedTimeRowsPanel.Children.Add(Render_Log_Row.CreateFixedEmptyCell());
            LogRowsPanel.Children.Add(Render_Log_Row.CreateEmptyRow(""));

            string message = "이력 파일 복구에 실패했습니다.";

            if (!string.IsNullOrWhiteSpace(detailMessage))
            {
                message += "\n\n" + detailMessage;
            }

            MessageBox.Show(
                message,
                "이력 복구",
                MessageBoxButton.OK,
                icon
            );
        }

        private static MessageBoxResult ShowRecoverySuccessMessage()
        {
            return MessageBox.Show(
                "이력 파일 복구가 완료되었습니다.\n\n" +
                "복구된 이력 파일을 바로 열까요?",
                "이력 복구",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );
        }
    }
}
