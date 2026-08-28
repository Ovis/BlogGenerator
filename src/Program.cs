using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using BlogGenerator.Core;
using BlogGenerator.Models;

namespace BlogGenerator;

public class Program
{
    public static Task<int> Main(string[] args) => MainAsync(args, TimeProvider.System);

    internal static async Task<int> MainAsync(string[] args, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var commandLineSetup = new CommandLineSetup();
        var rootCommand = commandLineSetup.CreateRootCommand();

        ConfigureScheduledCommand(commandLineSetup, timeProvider);
        ConfigureBuildCommand(rootCommand, commandLineSetup, timeProvider);

        return await rootCommand.Parse(args, new ParserConfiguration()).InvokeAsync(
            new InvocationConfiguration { Output = Console.Out, Error = Console.Error });
    }

    private static void ConfigureBuildCommand(RootCommand rootCommand, CommandLineSetup commandLineSetup, TimeProvider timeProvider)
    {
        rootCommand.SetAction(async (parseResult, _) =>
        {
            var input = parseResult.GetRequiredValue(commandLineSetup.InputOption);
            var output = parseResult.GetRequiredValue(commandLineSetup.OutputOption);
            var theme = parseResult.GetRequiredValue(commandLineSetup.ThemeOption);

            var options = new BuildOptions(
                input.FullName,
                output.FullName,
                theme.FullName,
                parseResult.GetValue(commandLineSetup.OEmbedOption),
                parseResult.GetValue(commandLineSetup.AmazonCacheOption),
                parseResult.GetValue(commandLineSetup.ConfigOption));

            return await new BlogBuildService(timeProvider).BuildAsync(options);
        });
    }

    private static void ConfigureScheduledCommand(CommandLineSetup commandLineSetup, TimeProvider timeProvider)
    {
        commandLineSetup.ScheduledCommand.SetAction((parseResult, _) =>
        {
            try
            {
                var input = parseResult.GetRequiredValue(commandLineSetup.ScheduledInputOption);
                if (!input.Exists) throw new ArgumentException($"Input directory does not exist: {input.FullName}");

                var after = ParseBoundary(parseResult.GetRequiredValue(commandLineSetup.AfterOption), "--after");
                var until = ParseBoundary(parseResult.GetRequiredValue(commandLineSetup.UntilOption), "--until");
                if (after >= until) throw new ArgumentException("--after must be earlier than --until.");

                var timeZoneId = parseResult.GetValue(commandLineSetup.TimeZoneOption);
                var timeZone = string.IsNullOrWhiteSpace(timeZoneId)
                    ? timeProvider.LocalTimeZone
                    : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                var result = new ScheduledPublicationChecker(timeZone).Check(input.FullName, after, until);

                Console.Out.WriteLine(SerializeScheduledResult(result));
                return Task.FromResult(0);
            }
            catch (ScheduledPublicationCheckException ex)
            {
                Console.Error.WriteLine($"Scheduled publication check failed with {ex.Errors.Count} error(s):");
                foreach (var error in ex.Errors)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(error.Path);
                    Console.Error.WriteLine($"  {error.Exception.Message}");
                }
                return Task.FromResult(1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return Task.FromResult(1);
            }
        });
    }

    private static DateTimeOffset ParseBoundary(string value, string optionName)
    {
        var formats = new[] { "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK" };
        if (!HasRequiredOffset(value) ||
            !DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw new ArgumentException($"{optionName} must be an ISO 8601 date/time with Z or an offset in ±HH:mm format.");
        }

        return result;
    }

    private static bool HasRequiredOffset(string value) =>
        value.EndsWith("Z", StringComparison.Ordinal) ||
        System.Text.RegularExpressions.Regex.IsMatch(value, @"[+-]\d{2}:\d{2}$");

    private static string SerializeScheduledResult(ScheduledPublicationCheckResult result)
    {
        static string Format(DateTimeOffset value, bool forceUtcZ = false)
        {
            if (forceUtcZ || value.Offset == TimeSpan.Zero)
                return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

            return value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
        }

        var dto = new
        {
            hasScheduled = result.HasScheduled,
            after = Format(result.After, true),
            until = Format(result.Until, true),
            timeZone = result.TimeZone.Id,
            count = result.Count,
            items = result.Items.Select(x => new { path = x.Path, published = Format(x.Published) }).ToArray()
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
