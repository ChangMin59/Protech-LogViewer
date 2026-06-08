namespace DbViewer.Models
{
    public class LogRow
    {
        public long Id { get; set; }

        public string Group { get; set; } = "";

        public string DTime { get; set; } = "";

        public string Type { get; set; } = "";

        public string Action { get; set; } = "";

        public string Section { get; set; } = "";

        public string Contents { get; set; } = "";

        public string Packet { get; set; } = "";
    }
}