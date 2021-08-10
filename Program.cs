using System.Threading.Tasks;
using BlogGenerator.ShortCodes;
using Statiq.App;
using Statiq.Web;

namespace BlogGenerator
{
    public class Program
  {
    public static async Task<int> Main(string[] args) =>
      await Bootstrapper
        .Factory
        .CreateWeb(args)
        .AddShortcode<AmazonAffiliateShortCodes>("AmazonAffiliate")
        .AddShortcode<OEmbedShortCodes>("EmbedLink")
        .RunAsync();
  }
}