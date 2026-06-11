using System.IO;

namespace DbViewer.Services.View
{
    public enum HistoryOpenStatus
    {
        Success,
        UnsupportedFileType,
        InvalidDbFile,
        InvalidTxtFile
    }

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
        public static HistoryOpenResult Open(string historyFilePath)
        {
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

        private static HistoryOpenResult OpenDb(string dbPath)
        {
            try
            {
                string copiedDbPath = DbCopyService.CopyDbToTemp(dbPath);
                HistoryDatabaseValidationResult checkResult = HistoryDatabaseValidator.Check(copiedDbPath);

                if (!checkResult.Success)
                {
                    return new HistoryOpenResult
                    {
                        Status = HistoryOpenStatus.InvalidDbFile,
                        ErrorMessage = checkResult.ErrorMessage
                    };
                }

                return ToSuccessResult(copiedDbPath, checkResult);
            }
            catch (Exception ex)
            {
                return new HistoryOpenResult
                {
                    Status = HistoryOpenStatus.InvalidDbFile,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static HistoryOpenResult OpenTxt(string txtPath)
        {
            try
            {
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
