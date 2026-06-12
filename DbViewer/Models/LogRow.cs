namespace DbViewer.Models
{
    // DB Log 테이블 한 행을 화면/검색/저장/인쇄에서 공통으로 쓰는 모델이다.
    // 실제 컬럼 예: 2026-06-01 17:04:17 / 중계기고장 / 발생 / 02# 01계통 005중계기 / 중계기 통신고장 / Packet.
    public class LogRow
    {
        // Log.ID. 같은 시간 로그가 여러 개일 때 최신순 정렬 보조값으로 쓴다.
        public long Id { get; set; }

        // Log.GRP. 원본 DB 그룹값이며 필요 시 보존한다.
        public string Group { get; set; } = "";

        // Log.DTIME. 예: 2026-06-01 17:04:17.
        public string DTime { get; set; } = "";

        // Log.Type. 예: 화재, 중계기고장, AN고장, MCC, KEY, 수신기.
        public string Type { get; set; } = "";

        // Log.Action. 예: 발생, 복구, 소거, ON, OFF, 기동, 정지.
        public string Action { get; set; } = "";

        // Log.Section. 예: 02# 01계통 005중계기, 02# MCC 스위치 014번 기동.
        public string Section { get; set; } = "";

        // Log.Contents. 예: 중계기 통신고장, 지하주차장 환기휀-지하5층 기동상태.
        public string Contents { get; set; } = "";

        // Log.Packet. 예: NU..., CAU..., 비어 있으면 텍스트 기준으로 분류한다.
        public string Packet { get; set; } = "";
    }
}
