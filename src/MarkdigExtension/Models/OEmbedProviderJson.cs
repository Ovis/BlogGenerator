using System.Text.Json.Serialization;

namespace BlogGenerator.MarkdigExtension.Models
{
    public class OEmbedProviderJson
    {
        [JsonPropertyName("provider_name")]
        public string ProviderName { get; set; } = string.Empty;
        [JsonPropertyName("provider_url")]
        public string ProviderUrl { get; set; } = string.Empty;
        [JsonPropertyName("endpoints")]
        public List<Endpoint> EndPoints { get; set; } = new();
    }

    public class Endpoint
    {
        [JsonPropertyName("schemes")]
        public List<string> Schemes { get; set; } = [];

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("discovery")]
        public bool Discovery { get; set; } = false;

        [JsonPropertyName("formats")]
        public string[] Formats { get; set; } = [];
    }
}
