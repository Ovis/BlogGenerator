using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace BlogGenerator.MarkdigExtension.Models
{
    [XmlRoot("oembed")]
    public class EmbedResponse
    {
        [JsonPropertyName("type")]
        [XmlElement("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        [XmlElement("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [XmlElement("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("author_name")]
        [XmlElement("author_name")]
        public string AuthorName { get; set; } = string.Empty;

        [JsonPropertyName("author_url")]
        [XmlElement("author_url")]
        public string AuthorUrl { get; set; } = string.Empty;

        [JsonPropertyName("provider_name")]
        [XmlElement("provider_name")]
        public string ProviderName { get; set; } = string.Empty;

        [JsonPropertyName("provider_url")]
        [XmlElement("provider_url")]
        public string ProviderUrl { get; set; } = string.Empty;

        [JsonPropertyName("cache_age")]
        [XmlElement("cache_age")]
        public string CacheAge { get; set; } = string.Empty;

        [JsonPropertyName("thumbnail_url")]
        [XmlElement("thumbnail_url")]
        public string ThumbnailUrl { get; set; } = string.Empty;

        [JsonPropertyName("thumbnail_height")]
        [XmlElement("thumbnail_height")]
        public string ThumbnailHeight { get; set; } = string.Empty;

        [JsonPropertyName("thumbnail_width")]
        [XmlElement("thumbnail_width")]
        public string ThumbnailWidth { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        [XmlElement("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("height")]
        [XmlElement("height")]
        public string Height { get; set; } = string.Empty;

        [JsonPropertyName("width")]
        [XmlElement("width")]
        public string Width { get; set; } = string.Empty;

        [JsonPropertyName("html")]
        [XmlElement("html")]
        public string Html { get; set; } = string.Empty;
    }
}
