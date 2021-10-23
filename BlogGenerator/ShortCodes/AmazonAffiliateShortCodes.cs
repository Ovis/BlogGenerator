using System.Collections.Generic;
using System.Linq;
using System.Text;
using Statiq.Common;

namespace BlogGenerator.ShortCodes
{
    public class AmazonAffiliateShortCodes : SyncShortcode
    {
        private const string Asin = nameof(Asin);

        /// <inheritdoc />
        public override ShortcodeResult Execute(KeyValuePair<string, string>[] args, string content, IDocument document, IExecutionContext context)
        {
            IMetadataDictionary arguments = args.ToDictionary(Asin);
            arguments.RequireKeys(Asin);

            var sb = new StringBuilder();
            sb.AppendLine($"<iframe style=\"width: 120px; height: 240px;\" marginwidth=\"0\" marginheight=\"0\" scrolling=\"no\" frameborder=\"0\"")
                .AppendLine($"src=\"")
                .AppendLine($"https://rcm-fe.amazon-adsystem.com/e/cm?ref=qf_sp_asin_til&t=ovis91-22&m=amazon&o=9&p=8&l=as1&IS2=1&detail=1&asins={arguments.GetString(Asin)}&bc1=000000&amp;lt1=_blank&fc1=333333&lc1=0066c0&bg1=ffffff&f=ifr")
                .AppendLine("\">")
                .AppendLine("</iframe>");

            return sb.ToString();
        }
    }
}
