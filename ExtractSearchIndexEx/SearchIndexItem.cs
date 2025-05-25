// SPDX-License-Identifier: MIT
// Copyright (c) 2025 sibber (GitHub: sibber5)

using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Sibber.Docfx.ExtractSearchIndexEx;

internal record SearchIndexItem(
    [property: JsonProperty("href")]
    [property: JsonPropertyName("href")]
    string Href,

    [property: JsonProperty("title")]
    [property: JsonPropertyName("title")]
    string Title,

    [property: JsonProperty("keywords")]
    [property: JsonPropertyName("keywords")]
    string? Keywords,

    [property: JsonProperty("summary")]
    [property: JsonPropertyName("summary")]
    string? Summary
);
