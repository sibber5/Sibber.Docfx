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
    Fields = 8,
    Events = 16,
    All = Types | Methods | Properties | Fields | Events,
}
