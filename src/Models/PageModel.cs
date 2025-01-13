using BlogGenerator.Enums;

namespace BlogGenerator.Models
{
    public class PageModel : PageModelBase
    {
        public PageType PageType { get; set; }

        public string SideBarHtml { get; set; } = string.Empty;

        public IReadOnlyCollection<Article> Articles { get; set; } = Array.Empty<Article>();

        public PaginationModel Pagination { get; set; } = new();

    }

    public class PaginationModel
    {
        public int CurrentPage { get; set; }
        
        public int TotalPages { get; set; }

        public int MaxPagesToShow { get; set; }

        public string RelativeDirectoryPath { get; set; } = string.Empty;
    }
}
