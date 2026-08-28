using BlogGenerator.Models;
using Microsoft.Extensions.Configuration;

namespace BlogGenerator.Core;

internal static class BlogConfigurationLoader
{
    public static (SiteOption SiteOption, FeedOption FeedOption) Load(FileInfo? configFile)
    {
        var configBuilder = new ConfigurationBuilder();
        var userConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bloggen", "config.json");

        if (File.Exists(userConfigPath)) configBuilder.AddJsonFile(userConfigPath, optional: true, reloadOnChange: true);
        if (File.Exists("appsettings.json")) configBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        if (File.Exists("appsettings.Development.json")) configBuilder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
        if (configFile is { Exists: true }) configBuilder.AddJsonFile(configFile.FullName, optional: false, reloadOnChange: true);
        configBuilder.AddEnvironmentVariables("BLOGGEN_");

        var configuration = configBuilder.Build();
        var siteOption = configuration.GetSection("SiteOption").Get<SiteOption>() ?? new SiteOption();
        var feedOption = configuration.GetSection("FeedOption").Get<FeedOption>() ?? new FeedOption();

        // 従来の環境変数名との互換性を維持するため、セクション形式で取得できなかった値だけ補完する
        siteOption.SiteName = GetEnvironmentFallback(siteOption.SiteName, "BLOGGEN_SITENAME");
        siteOption.SiteUrl = GetEnvironmentFallback(siteOption.SiteUrl, "BLOGGEN_SITEURL");
        siteOption.SiteDescription = GetEnvironmentFallback(siteOption.SiteDescription, "BLOGGEN_SITEDESCRIPTION");
        siteOption.SiteAuthor = GetEnvironmentFallback(siteOption.SiteAuthor, "BLOGGEN_SITEAUTHOR");
        siteOption.SiteAuthorDescription = GetEnvironmentFallback(siteOption.SiteAuthorDescription, "BLOGGEN_SITEAUTHORDESCRIPTION");
        siteOption.AmazonAssociateTag = GetEnvironmentFallback(siteOption.AmazonAssociateTag, "BLOGGEN_AMAZONTAG");

        if (string.IsNullOrEmpty(siteOption.SiteUrl))
            throw new ArgumentException("SiteUrl is a required field. Please specify it via environment variables or a configuration file.");

        return (siteOption, feedOption);
    }

    private static string GetEnvironmentFallback(string? configuredValue, string environmentVariableName) =>
        string.IsNullOrEmpty(configuredValue)
            ? Environment.GetEnvironmentVariable(environmentVariableName) ?? string.Empty
            : configuredValue;
}
