using System.Collections.Generic;

namespace ProductHub_MVC.Models
{
    public class AiSearchResponse
    {
        public string Answer { get; set; } = string.Empty;
        public List<ProductCardDto> ProductCards { get; set; } = new();
        public List<string> Images { get; set; } = new();
        public List<SourceDto> Sources { get; set; } = new();
        public List<string> RelatedProducts { get; set; } = new();
    }

    public class ProductCardDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public class SourceDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
