namespace Mnemosyne.Models;

/// <summary>一条页内搜索匹配（基于 .NET 字符串的字符索引）。Value 为匹配到的原文，供正则替换展开分组引用。</summary>
public sealed record SearchMatch(int Start, int Length, string Value);
