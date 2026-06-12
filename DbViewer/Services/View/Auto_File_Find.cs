using System;
using System.IO;

namespace DbViewer.Services.View
{
    // 프로그램 기본 이력 파일 위치를 자동으로 찾는다.
    public static class Auto_File_Find
    {
        // 현장 프로그램이 기본으로 쓰는 C:\Windows\Log.db 존재 여부를 확인한다.
        public static bool TryFindHistoryFile(out string historyFilePath)
        {
            // Windows 폴더는 PC마다 드라이브가 다를 수 있어 Environment 값으로 가져온다.
            historyFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Log.db"
            );

            // 예: C:\Windows\Log.db가 있으면 이력 보기 버튼에서 바로 열 수 있다.
            return File.Exists(historyFilePath);
        }
    }
}
