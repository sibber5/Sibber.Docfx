// SPDX-License-Identifier: MIT
// Copyright (c) 2025 sibber (GitHub: sibber5)
// Modifications made to the original work.

// Original work copyright notice:
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copyright (c) .NET Foundation and Contributors
// Original work: https://github.com/dotnet/docfx/blob/44383167ece82d4deb7c2062de1a2e34b32607e9/src/Docfx.Build/PostProcessors/ExtractSearchIndex.cs

using System.Collections.Immutable;
using System.ComponentModel;
using System.Composition;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Docfx.Common;
using Docfx.Plugins;
using HtmlAgilityPack;

namespace Sibber.Docfx.ExtractSearchIndexEx;

[Export(nameof(ExtractSearchIndexEx), typeof(IPostProcessor))]
public partial class ExtractSearchIndexEx : IPostProcessor
{
    [GeneratedRegex(@"(\s*\n\s*)|\s+")]
    private static partial Regex s_regexWhiteSpace();

    [GeneratedRegex(@"[a-z0-9]+|[A-Z0-9]+[a-z0-9]*|[0-9]+")]
    private static partial Regex s_regexCase();

    private static readonly HashSet<string> s_htmlInlineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "area", "del", "ins", "link", "map", "meta", "abbr", "audio", "b", "bdo", "button", "canvas", "cite", "code", "command", "data",
        "datalist", "dfn", "em", "embed", "i", "iframe", "img", "input", "kbd", "keygen", "label", "mark", "math", "meter", "noscript", "object",
        "output", "picture", "progress", "q", "ruby", "samp", "script", "select", "small", "span", "strong", "sub", "sup", "svg", "textarea", "time",
        "var", "video", "wbr",
    };

    public string Name => nameof(ExtractSearchIndexEx);
    public const string IndexFileName = "index.json";

    internal bool UseMetadata { get; set; } = false;
    internal bool UseMetadataTitle { get; set; } = true;

    // [mod] added the next 2 lines:
    internal SearchScopes SearchScopes { get; set; } = SearchScopes.All;
    internal bool StripSiteNameFromTitle { get; set; }

    public ImmutableDictionary<string, object> PrepareMetadata(ImmutableDictionary<string, object> metadata)
    {
        if (!metadata.ContainsKey("_enableSearch"))
        {
            metadata = metadata.Add("_enableSearch", true);
        }

        UseMetadata = metadata.TryGetValue("_searchIndexUseMetadata", out object? useMetadataObject) && (bool)useMetadataObject;
        UseMetadataTitle = !metadata.TryGetValue("_searchIndexUseMetadataTitle", out object? useMetadataTitleObject) || (bool)useMetadataTitleObject;

        // [mod] added the next section:
        if (metadata.TryGetValue("_searchIndexScopes", out object? searchScopesObj) && searchScopesObj is IEnumerable<string?> searchScopes)
        {
            SearchScopes = SearchScopes.None;
            foreach (string? scopeStr in searchScopes)
            {
                if (Enum.TryParse(scopeStr, true, out SearchScopes scope))
                {
                    SearchScopes |= scope;
                }
                else
                {
                    throw new InvalidEnumArgumentException($"Invalid scope: {scopeStr}.");
                }
            }
        }

        StripSiteNameFromTitle = metadata.TryGetValue("_searchIndexStripSiteNameFromTitle", out object? stripSiteNameObj) && (bool)stripSiteNameObj;
        // end section

        //Logger.LogInfo($"{Name}: {nameof(UseMetadata)} = {UseMetadata}, {nameof(UseMetadataTitle)} = {UseMetadataTitle}");
        return metadata;
    }

    public Manifest Process(Manifest manifest, string outputFolder, CancellationToken cancellationToken = default)
    {
        if (outputFolder == null)
        {
            throw new ArgumentNullException(nameof(outputFolder), "Base directory can not be null");
        }

        var indexData = new SortedDictionary<string, SearchIndexItem>();
        var indexDataFilePath = Path.Combine(outputFolder, IndexFileName);
        var htmlFiles = (from item in manifest.Files ?? Enumerable.Empty<ManifestItem>()
                         from output in item.Output
                         where item.Type != "Toc" && output.Key.Equals(".html", StringComparison.OrdinalIgnoreCase)
                         select (output.Value.RelativePath, item.Metadata)).ToList();

        if (htmlFiles.Count == 0)
        {
            return manifest;
        }

        //Logger.LogInfo($"Extracting index data from {htmlFiles.Count} html files");
        foreach ((string relativePath, Dictionary<string, object> metadata) in htmlFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = Path.Combine(outputFolder, relativePath);
            var html = new HtmlDocument();
            //Logger.LogDiagnostic($"Extracting index data from {filePath}");

            if (EnvironmentContext.FileAbstractLayer.Exists(filePath))
            {
                try
                {
                    using var stream = EnvironmentContext.FileAbstractLayer.OpenRead(filePath);
                    html.Load(stream, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{nameof(ExtractSearchIndexEx)}] Warning: Can't load content from {filePath}: {ex.Message}");
                    //Logger.LogWarning($"Warning: Can't load content from {filePath}: {ex.Message}");
                    continue;
                }

                var indexItems = ExtractItems(html, relativePath, metadata);
                foreach (var item in indexItems)
                {
                    indexData[item.Href] = item;
                }
            }
        }

        // [mod] added the next 2 lines:
        if (!EnvironmentContext.FileAbstractLayer.Exists(indexDataFilePath)) throw new InvalidOperationException("index.json not found. Make sure ExtractSearchIndex successfully finishes.");
        File.Delete(EnvironmentContext.FileAbstractLayer.GetPhysicalPath(indexDataFilePath));
        JsonUtility.Serialize(indexDataFilePath, indexData, indented: true);

        // [mod] don't add index_ex.json to manifest as resource file, since it's meant to overwrite index.json after all the post processors run
        //var manifestItem = new ManifestItem
        //{
        //    Type = "Resource",
        //};
        //manifestItem.Output.Add("resource", new OutputFileInfo
        //{
        //    RelativePath = PathUtility.MakeRelativePath(outputFolder, indexDataFilePath),
        //});
        //manifest.Files?.Add(manifestItem);

        return manifest;
    }

    // [mod] modified the following method:
    internal IEnumerable<SearchIndexItem> ExtractItems(HtmlDocument html, string href, Dictionary<string, object>? metadata = null)
    {
        if (SearchScopes == SearchScopes.None) yield break;

        if (html.DocumentNode.SelectNodes("/html/head/meta[@name='searchOption' and @content='noindex']") != null)
        {
            yield break;
        }

        string htmlTitle = ExtractTitleFromHtml(html);
        bool isEnum = htmlTitle.AsSpan().StartsWith("Enum");

        // Select content between the data-searchable class tag
        var nodes = html.DocumentNode.SelectNodes("//*[contains(@class,'data-searchable')]") ?? Enumerable.Empty<HtmlNode>();
        // Select content between the article tag, unless it's an enum because its article tag isn't split up into sections so they won't be parsed; Enum value parsing has a special case.
        if (!isEnum)
        {
            var articleNodes = html.DocumentNode.SelectNodes("//article");
            if (articleNodes is not null) nodes = nodes.Union(articleNodes).ToList();
        }

        bool isMRef = metadata != null && metadata.TryGetValue("IsMRef", out var isMRefMetadata) && (bool)isMRefMetadata;
        bool useMetadata = UseMetadata && isMRef;
        bool useMetadataForTitle = UseMetadataTitle && useMetadata && metadata?["Title"] is not null;

        string typeTitle = useMetadataForTitle ? (string)metadata?["Title"]! : htmlTitle;

        if (SearchScopes.HasFlag(SearchScopes.Types))
        {
            yield return ParseType();
        }

        // if no other flags are set
        if (SearchScopes == SearchScopes.Types) yield break;

        if (isEnum && SearchScopes.HasFlag(SearchScopes.EnumValues))
        {
            foreach (var item in ParseEnumValues()) yield return item;
        }

        // ReSharper disable once PossibleMultipleEnumeration (node collection is not deferred)
        foreach (HtmlNode node in nodes)
        {
            foreach (var item in ParseOtherIndexItems(node)) yield return item;
        }

        SearchIndexItem ParseType()
        {
            string? typeSummary;
            string? typeKeywords = null;
            if (useMetadata)
            {
                var htmlSummary = (string?)metadata?["Summary"];
                if (!string.IsNullOrEmpty(htmlSummary))
                {
                    var htmlDocument = new HtmlDocument();
                    htmlDocument.LoadHtml(htmlSummary);
                    var htmlRootNode = htmlDocument.DocumentNode.FirstChild;
                    var summaryBuilder = new StringBuilder();
                    ExtractTextFromNode(htmlRootNode, summaryBuilder);
                    typeSummary = NormalizeSummary(summaryBuilder.ToString());
                }
                else
                {
                    typeSummary = NormalizeSummary(html.DocumentNode.SelectSingleNode("//head/meta[@name='description']")?.GetAttributeValue("content", null));
                }

                typeKeywords = GetKeywordsForTitle(typeTitle);
            }
            else
            {
                var contentBuilder = new StringBuilder();
                // ReSharper disable once PossibleMultipleEnumeration (node collection is not deferred)
                foreach (var node in nodes)
                {
                    ExtractTextFromNode(node, contentBuilder);
                }

                typeSummary = NormalizeSummary(contentBuilder.ToString());
            }

            return new(href, typeTitle, typeKeywords, typeSummary);
        }

        IEnumerable<SearchIndexItem> ParseEnumValues()
        {
            var valueNodes = html.DocumentNode.SelectSingleNode("//article/h2[@id='fields']/following-sibling::dl[@class='parameters']").ChildNodes;

            string curHref = "";
            string curTitle = "";
            string? curKeywords = null;
            string? curSummary = null;

            bool isParsingField = false;
            foreach (var node in valueNodes)
            {
                if (node.Name == "dt" && !string.IsNullOrEmpty(node.Id))
                {
                    if (isParsingField) yield return new(curHref, curTitle, curKeywords, curSummary);

                    curHref = $"{href}#{node.Id}";
                    curTitle = GetTitleForSearchIndexItem("enum values", node, typeTitle, useMetadataForTitle);
                    curKeywords = useMetadata ? GetKeywordsForTitle(curTitle) : null;
                    curSummary = null;

                    isParsingField = true;
                    continue;
                }

                if (isParsingField && node.Name == "dd")
                {
                    curSummary = NormalizeSummary(node.InnerText);
                }
            }

            if (isParsingField) yield return new(curHref, curTitle, curKeywords, curSummary);
        }

        IEnumerable<SearchIndexItem> ParseOtherIndexItems(HtmlNode node)
        {
            string? sectionId = null;

            bool isParsing = false;

            string curHref ="";
            string curTitle = "";
            string? curKeywords = null;
            string? curSummary = null;

            foreach (HtmlNode c in node.ChildNodes)
            {
                if (c.Name == "h2" && c.HasClass("section"))
                {
                    if (isParsing)
                    {
                        yield return new(curHref, curTitle, curKeywords, curSummary);
                        isParsing = false;
                    }

                    sectionId = c.Id;
                    continue;
                }

                if ((SearchScopes.HasFlag(SearchScopes.Methods) && sectionId == "methods")
                    || (SearchScopes.HasFlag(SearchScopes.Properties) && sectionId == "properties")
                    || (SearchScopes.HasFlag(SearchScopes.Fields) && sectionId == "fields")
                    || (SearchScopes.HasFlag(SearchScopes.Events) && sectionId == "events"))
                {
                    if (c.Name == "h3")
                    {
                        if (isParsing) yield return new(curHref, curTitle, curKeywords, curSummary);

                        curHref = $"{href}#{c.Id}";
                        curTitle = GetTitleForSearchIndexItem(sectionId, c, typeTitle, useMetadataForTitle);
                        curKeywords = useMetadata ? GetKeywordsForTitle(curTitle) : null;
                        curSummary = null;

                        isParsing = true;
                        continue;
                    }

                    if (isParsing && c.HasClass("summary"))
                    {
                        curSummary = NormalizeSummary(c.InnerText);

                        yield return new(curHref, curTitle, curKeywords, curSummary);
                        isParsing = false;

                        // If in the future we want to parse other sections, make sure to add the following line:
                        // continue;
                    }
                }
            }

            if (isParsing) yield return new(curHref, curTitle, curKeywords, curSummary);
        }

        string? NormalizeSummary(string? s)
        {
            if (string.IsNullOrEmpty(s)) return null;

            var res = NormalizeContent(s);
            int trailingDot = res.LastIndexOf('.');
            return trailingDot == -1 ? res.ToString() : res[..trailingDot].TrimEnd().ToString();
        }

        static string GetKeywordsForTitle(string title)
        {
            int end = title.AsSpan().LastIndexOf('|') - 1;
            if (end > -1) title = title[..end];
            return string.Join(' ', title.Split(' ').Select(word => string.Join(' ', GetStemAggregations(word.Split('.')[^1]))));
        }
    }

    // [mod] added method:
    private string GetTitleForSearchIndexItem(string sectionId, HtmlNode node, string typeTitle, bool usedMetadataForTitle)
    {
        var memberName = NormalizeContent(node.InnerText);
        // if (sectionId is "properties" or "fields" or "enum value")
        // {
        //     int memberNameEnd = memberName.IndexOf('=');
        //     if (memberNameEnd != -1) memberName = memberName[..memberNameEnd].TrimEnd();
        // }

        StringBuilder titleBuilder = new();
        var titleSpan = typeTitle.AsSpan();
        if (!usedMetadataForTitle)
        {
            titleBuilder.Append(sectionId switch
            {
                "methods" => "Method ",
                "properties" => "Property ",
                "fields" => "Field ",
                "events" => "Event ",
                "enum values" => "Enum Value ",
                _ => throw new NotSupportedException($"Unsupported search scope: {sectionId}."),
            });

            int typeStart = titleSpan.IndexOf(' ') + 1;
            int typeEnd = titleSpan[typeStart..].IndexOf(' ');
            if (typeEnd == -1)
            {
                if (StripSiteNameFromTitle)
                {
                    typeEnd = titleSpan.Length;
                }
                else
                {
                    typeEnd = titleSpan.LastIndexOf('|');
                    while (typeEnd > 0 && char.IsWhiteSpace(titleSpan[typeEnd - 1])) typeEnd--;
                }
            }
            else
            {
                typeEnd += typeStart;
            }

            titleBuilder.Append(titleSpan[typeStart..typeEnd]);
            titleBuilder.Append('.');
            titleBuilder.Append(memberName);
            titleBuilder.Append(titleSpan[typeEnd..]);
        }
        else
        {
            titleBuilder.Append(titleSpan);
            titleBuilder.Append('.');
            titleBuilder.Append(memberName);
        }

        return titleBuilder.ToString();
    }

    private string ExtractTitleFromHtml(HtmlDocument html)
    {
        var titleNode = html.DocumentNode.SelectSingleNode("//head/title");
        // [mod] modified the next line:
        var originalTitle = titleNode?.InnerText ?? html.DocumentNode.SelectSingleNode("//head/meta[@name='title']")?.GetAttributeValue("content", null);

        // [mod] modified the next section (until end of method):
        var title = NormalizeContent(originalTitle);
        int stripIdx = StripSiteNameFromTitle ? title.LastIndexOf('|') : -1;
        return (stripIdx == -1 ? title : title[..stripIdx]).TrimEnd().ToString();
    }

    private ReadOnlySpan<char> NormalizeContent(string? str, bool keepNewLines = false)
    {
        if (string.IsNullOrEmpty(str))
        {
            return string.Empty;
        }

        str = WebUtility.HtmlDecode(str);
        // [mod] modified the next line:
        return (keepNewLines
            ? s_regexWhiteSpace().Replace(str, m => m.Groups[1].Success ? "\n" : " ")
            : s_regexWhiteSpace().Replace(str, " "))
            .AsSpan().Trim();
    }

    private static string[] GetStems(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return [string.Empty];
        }

        str = WebUtility.HtmlDecode(str);
        return s_regexCase().Matches(str).Select(m => m.Value).ToArray();
    }

    private static List<string> GetStemAggregations(string str)
    {
        var stems = GetStems(str);

        var results = new List<string>();
        Aggregate(stems, [], results, 0);
        return results;

        static void Aggregate(string[] input, List<string> current, List<string> results, int index)
        {
            if (index == input.Length)
            {
                return;
            }

            for (int i = index; i < input.Length; i++)
            {
                current.Add(input[i]);
                results.Add(string.Join(string.Empty, current));
                Aggregate(input, current, results, i + 1);
                current.RemoveAt(current.Count - 1);
            }
        }
    }

    private static void ExtractTextFromNode(HtmlNode? node, StringBuilder contentBuilder)
    {
        if (node == null)
        {
            return;
        }

        if (node.NodeType is HtmlNodeType.Text or HtmlNodeType.Comment)
        {
            contentBuilder.Append(node.InnerText);
            return;
        }

        if (node.NodeType is HtmlNodeType.Element or HtmlNodeType.Document)
        {
            var isBlock = !s_htmlInlineTags.Contains(node.Name);
            if (isBlock)
                contentBuilder.Append(' ');

            foreach (var childNode in node.ChildNodes)
                ExtractTextFromNode(childNode, contentBuilder);

            if (isBlock)
                contentBuilder.Append(' ');
        }
    }
}
