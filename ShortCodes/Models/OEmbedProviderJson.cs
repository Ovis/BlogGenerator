using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlogGenerator.ShortCodes.Models
{
    public class OEmbedProviderJson
    {
        [JsonPropertyName("provider_name")]
        public string ProviderName { get; set; }
        [JsonPropertyName("provider_url")]
        public string ProviderUrl { get; set; }
        [JsonPropertyName("endpoints")]
        public List<Endpoint> EndPoints { get; set; }
    }

    public class Endpoint
    {
        [JsonPropertyName("schemes")]
        public List<string> Schemes { get; set; } = new List<string>();
        [JsonPropertyName("url")]
        public string Url { get; set; }
        [JsonPropertyName("discovery")]
        public bool Discovery { get; set; }
        [JsonPropertyName("formats")]
        public string[] Formats { get; set; }
    }
}