namespace BlogGenerator.Models
{
    public class SideBarModel : PageModelBase
    {
        public IReadOnlyCollection<Article> Articles { get; set; } = [];
    }
}
