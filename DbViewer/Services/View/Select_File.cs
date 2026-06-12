using Microsoft.Win32;
using System.Windows;

namespace DbViewer.Services.View
{
    // 사용자가 직접 이력 DB/TXT 파일을 고르는 파일창 서비스다.
    public static class Select_File
    {
        // 이력 보기 버튼에서 호출되어 .db 또는 .txt 경로를 반환한다.
        public static string? SelectHistoryFilePath(Window owner)
        {
            // 현장 기본 DB와 저장 TXT를 모두 선택할 수 있게 필터를 구성한다.
            OpenFileDialog dialog = new()
            {
                Title = "이력 파일 선택",
                Filter = "이력 파일 (*.db;*.txt)|*.db;*.txt|SQLite DB (*.db)|*.db|텍스트 이력 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog(owner) != true)
            {
                // 사용자가 취소하면 호출 쪽에서 기존 화면 상태를 유지한다.
                return null;
            }

            // 예: C:\Windows\Log.db 또는 D:\이력_230124~241012.txt.
            return dialog.FileName;
        }
    }
}
