using System.Collections.Generic;
using System.Linq;
using System.Text;
using Statiq.Common;

namespace BlogGenerator.ShortCodes
{
    class OEmbedShortCodes: SyncShortcode
    {
        private const string Url = nameof(Url);

  
        public override ShortcodeResult Execute(KeyValuePair<string, string>[] args, string content, IDocument document, IExecutionContext context)
        {
            IMetadataDictionary arguments = args.ToDictionary(Url);
            arguments.RequireKeys(Url);

            var sb = new StringBuilder();

            sb.AppendLine($"<a href=\"")
                .AppendLine($"{arguments.GetString(Url)}")
                .AppendLine($"\" target=\"_blank\">")
                .AppendLine($"{arguments.GetString(Url)}")
                .AppendLine($"</a>");

            return sb.ToString();
        }

    }
}
