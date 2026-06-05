namespace ProductHub_MVC.Models
{
    public class SystemStatusDto
    {
        public DbStatusDto DbStatus { get; set; } = new();
        public ApiStatusDto ApiStatus { get; set; } = new();
        public OllamaStatusDto OllamaStatus { get; set; } = new();
    }

    public class DbStatusDto
    {
        public bool IsOnline { get; set; }
        public long LatencyMs { get; set; }
        public string ConnectionString { get; set; } = string.Empty;
    }

    public class ApiStatusDto
    {
        public bool IsOnline { get; set; }
        public long LatencyMs { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    public class OllamaStatusDto
    {
        public bool IsOnline { get; set; }
        public long LatencyMs { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}
