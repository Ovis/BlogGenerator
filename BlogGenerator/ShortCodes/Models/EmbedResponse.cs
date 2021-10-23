using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace BlogGenerator.ShortCodes.Models
{
    [XmlRoot("oembed")]
    public class EmbedResponse
    {
        [JsonPropertyName("type")]
        [XmlElement("type")]
        public string Type { get; set; }
        [JsonPropertyName("version")]
        [XmlElement("version")]
        public string Version { get; set; }
        [JsonPropertyName("title")]
        [XmlElement("title")]
        public string Title { get; set; }
        [JsonPropertyName("author_name")]
        [XmlElement("author_name")]
        public string AuthorName { get; set; }
        [JsonPropertyName("author_url")]
        [XmlElement("author_url")]
        public string AuthorUrl { get; set; }
        [JsonPropertyName("provider_name")]
        [XmlElement("provider_name")]
        public string ProviderName { get; set; }
        [JsonPropertyName("provider_url")]
        [XmlElement("providerprovider_url_name")]
        public string ProviderUrl { get; set; }
        [JsonPropertyName("cache_age")]
        [XmlElement("cache_age")]
        public string CacheAge { get; set; }
        [JsonPropertyName("thumbnail_url")]
        [XmlElement("thumbnail_url")]
        public string ThumbnailUrl { get; set; }
        [JsonPropertyName("thumbnail_height")]
        [XmlElement("thumbnail_height")]
        public string ThumbnailHeight { get; set; }
        [JsonPropertyName("thumbnail_width")]
        [XmlElement("thumbnail_width")]
        public string ThumbnailWidth { get; set; }

        [JsonPropertyName("url")]
        [XmlElement("url")]
        public string Url { get; set; }
        [JsonPropertyName("height")]
        [XmlElement("height")]
        public string Height { get; set; }
        [JsonPropertyName("width")]
        [XmlElement("width")]
        public string Width { get; set; }

        [JsonPropertyName("html")]
        [XmlElement("html")]
        public string Html { get; set; }
    }
}
