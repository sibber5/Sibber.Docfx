// SPDX-License-Identifier: MIT
// Copyright (c) 2025 sibber (GitHub: sibber5)

namespace Sibber.Docfx.ExtractSearchIndexEx;

[Flags]
public enum SearchScopes
{
    None = 0,
    Types = 1,
    Methods = 2,
    Properties = 4,
    Events = 8,
    Fields = 16,
    EnumValues = 32,
    All = Types | Methods | Properties | Events | Fields | EnumValues,
}
