using System.IO;

namespace DbViewer.Services.View
{
    // 사용자가 선택한 이력 파일을 열었을 때 화면 쪽에 돌려줄 상태값이다.
    public enum HistoryOpenStatus
    {
        Success,
        UnsupportedFileType,
        InvalidDbFile,
        InvalidTxtFile
    }

    // 실제 이력 파일 열기 결과를 담는다.
    // DB/TXT 모두 최종적으로는 Log 테이블이 있는 SQLite DB 경로와 기간/건수를 반환한다.
    public sealed class HistoryOpenResult
    {
        public HistoryOpenStatus Status { get; init; }
        public string DbPath { get; init; } = "";
        public int TotalCount { get; init; }
        public string StartDate { get; init; } = "";
        public string EndDate { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
    }

    public static class Open_History_File
    {
        // 사용자가 선택한 파일 확장자에 따라 .db는 바로 검사하고 .txt는 임시 DB로 변환해서 연다.
        // 예: C:\Windows\Log.db -> OpenDb, 이력_230124~241012.txt -> OpenTxt.
        public static HistoryOpenResult Open(string historyFilePath)
        {
            // 확장자는 대소문자가 섞여도 같은 파일 형식으로 처리한다.
            string extension = Path.GetExtension(historyFilePath).ToLowerInvariant();

            return extension switch
            {
                ".db" => OpenDb(historyFilePath),
                ".txt" => OpenTxt(historyFilePath),
                _ => new HistoryOpenResult
                {
                    Status = HistoryOpenStatus.UnsupportedFileType,
                    ErrorMessage = "지원하지 않는 이력 파일 형식입니다."
                }
            };
        }

        // 실제 SQLite 이력 DB 파일을 빠르게 검사한 뒤 열기 성공 정보를 만든다.
        // 필수 구조: Log 테이블 + ID/GRP/DTIME/Type/Action/Section/Contents/Packet 컬럼.
        private static HistoryOpenResult OpenDb(string dbPath)
        {
            try
            {
                // 깨진 DB, Log 테이블 누락, 컬럼 누락을 먼저 걸러서 화면에 "잘못된 이력 파일"을 띄운다.
                HistoryDatabaseValidationResult checkResult = HistoryDatabaseValidator.Check(dbPath);

                if (!checkResult.Success)
                {
                    return new HistoryOpenResult
                    {
                        Status = HistoryOpenStatus.InvalidDbFile,
                        ErrorMessage = checkResult.ErrorMessage
                    };
                }

                return ToSuccessResult(dbPath, checkResult);
            }
            catch (Exception ex)
            {
                // SQLite 예외나 파일 접근 예외도 DB 열기 실패로 통일한다.
                return new HistoryOpenResult
                {
                    Status = HistoryOpenStatus.InvalidDbFile,
                    ErrorMessage = ex.Message
                };
            }
        }

        // TXT 이력 파일을 읽어 Log 테이블 구조의 임시 DB로 바꾼 뒤 검사한다.
        // 예: "2026-06-01 17:04:17     중계기고장     발생     02#..." 형식을 LogRow로 변환한다.
        private static HistoryOpenResult OpenTxt(string txtPath)
        {
            try
            {
                // 화면과 검색/필터 로직은 DB 기준이라 TXT도 임시 SQLite DB로 맞춘다.
                TxtHistoryConvertResult convertResult = TxtHistoryConverter.ConvertToTempDb(txtPath);

                if (!convertResult.Success)
                {
                    return new HistoryOpenResult
                    {
                        Status = HistoryOpenStatus.InvalidTxtFile,
                        ErrorMessage = convertResult.ErrorMessage
                    };
                }

                HistoryDatabaseValidationResult checkResult =
                    HistoryDatabaseValidator.Check(convertResult.DbPath);

                if (!checkResult.Success)
                {
                    // TXT 변환은 됐지만 Log 테이블 형식이 맞지 않으면 TXT 파일 오류로 사용자에게 보여준다.
                    return new HistoryOpenResult
                    {
                        Status = HistoryOpenStatus.InvalidTxtFile,
                        ErrorMessage = checkResult.ErrorMessage
                    };
                }

                return ToSuccessResult(convertResult.DbPath, checkResult);
            }
            catch (Exception ex)
            {
                return new HistoryOpenResult
                {
                    Status = HistoryOpenStatus.InvalidTxtFile,
                    ErrorMessage = ex.Message
                };
            }
        }

        // 유효한 DB에서 읽은 총 건수와 실제 날짜 범위를 화면 상태로 넘긴다.
        private static HistoryOpenResult ToSuccessResult(
            string dbPath,
            HistoryDatabaseValidationResult checkResult)
        {
            return new HistoryOpenResult
            {
                Status = HistoryOpenStatus.Success,
                DbPath = dbPath,
                TotalCount = checkResult.TotalCount,
                StartDate = checkResult.StartDate,
                EndDate = checkResult.EndDate
            };
        }
    }
}
