using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DNRun.Packaging;

/// <summary>Where a property declaration lives inside the raw text of a project file.</summary>
internal sealed record PropertyLocation(
    string Name,
    string Value,
    int ValueStart,
    int ValueEnd,
    bool HasCondition,
    bool SelfClosing);

/// <summary>
/// A .csproj or Directory.Build.props opened for surgical, text-level property edits.
///
/// Deliberately not an XDocument round-trip: re-serializing rewrites the whole file — attribute
/// quoting, self-closing tags, blank lines, comments, the XML declaration — and turns a one-line
/// version bump into an unreviewable diff. Instead the document is parsed only to *locate*
/// properties, which is the part a regex gets wrong (a legacy
/// <c>&lt;PackageReference&gt;&lt;Version&gt;</c> is not a project version), and the new value is
/// spliced into the original characters.
/// </summary>
internal sealed class MsBuildFile
{
    private readonly Encoding _encoding;
    private readonly string _newLine;
    private string _text;

    // Reading one version touches several properties, and listing a repository touches several
    // files; parsing once per edit rather than once per lookup keeps that linear.
    private XElement? _root;
    private bool _rootIsStale = true;

    private MsBuildFile(string path, string text, Encoding encoding, string newLine)
    {
        Path = path;
        _text = text;
        _encoding = encoding;
        _newLine = newLine;
    }

    public string Path { get; }

    public string Text => _text;

    public static bool TryLoad(string path, out MsBuildFile? file, out string? error)
    {
        file = null;
        error = null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(hasBom ? bytes.AsSpan(3) : bytes);
            var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            file = new MsBuildFile(path, text, new UTF8Encoding(hasBom), newLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = $"{path} could not be read ({ex.Message})";
            return false;
        }
    }

    /// <summary>Parses the file only to check it is well-formed XML.</summary>
    public bool IsWellFormed => Parse() is not null;

    /// <summary>
    /// Every declaration of <paramref name="propertyName"/> that sits directly inside a
    /// PropertyGroup, in document order. Anything under an ItemGroup is excluded by construction.
    /// </summary>
    public IReadOnlyList<PropertyLocation> Find(string propertyName)
    {
        var root = Parse();
        if (root is null)
        {
            return [];
        }

        var lineStarts = BuildLineStarts(_text);
        var found = new List<PropertyLocation>();

        foreach (var element in root.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (element.Parent is null
                || !string.Equals(element.Parent.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var location = Locate(element, lineStarts);
            if (location is not null)
            {
                found.Add(location);
            }
        }

        return found;
    }

    /// <summary>
    /// The declaration a bump should rewrite. MSBuild lets the last one win, but a conditional
    /// declaration may not apply at all, so an unconditional one is preferred when both exist.
    /// </summary>
    public PropertyLocation? FindEffective(string propertyName)
    {
        var all = Find(propertyName);
        return all.LastOrDefault(p => !p.HasCondition) ?? all.LastOrDefault();
    }

    public string? Read(string propertyName)
    {
        var value = FindEffective(propertyName)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Replaces the effective declaration's value. Returns false when the property is absent.</summary>
    public bool TrySet(string propertyName, string value)
    {
        var location = FindEffective(propertyName);
        if (location is null)
        {
            return false;
        }

        var replacement = location.SelfClosing
            ? $">{Escape(value)}</{location.Name}>"
            : Escape(value);

        Replace(string.Concat(
            _text.AsSpan(0, location.ValueStart),
            replacement,
            _text.AsSpan(location.ValueEnd)));

        return true;
    }

    /// <summary>Sets the property, adding it to the first unconditional PropertyGroup when missing.</summary>
    public bool SetOrInsert(string propertyName, string value) =>
        TrySet(propertyName, value) || TryInsert(propertyName, value);

    /// <summary>Adds a property on the line after an existing one, keeping related settings together.</summary>
    public bool TryInsertAfter(string anchorProperty, string propertyName, string value)
    {
        var anchor = FindEffective(anchorProperty);
        if (anchor is null)
        {
            return false;
        }

        var lineStart = StartOfLine(_text, anchor.ValueStart);
        var lineEnd = EndOfLineIncludingBreak(_text, anchor.ValueEnd);
        var indent = ReadIndent(_text, lineStart);

        Replace(_text.Insert(lineEnd, indent + $"<{propertyName}>{Escape(value)}</{propertyName}>" + _newLine));
        return true;
    }

    /// <summary>Writes through a temp file in the same directory: an interrupted bump must not truncate a project.</summary>
    public bool Save(out string? error)
    {
        error = null;

        var full = System.IO.Path.GetFullPath(Path);
        var directory = System.IO.Path.GetDirectoryName(full)!;
        var temp = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(full)}.{Environment.ProcessId}.tmp");

        try
        {
            var body = new UTF8Encoding(false).GetBytes(_text);
            var preamble = _encoding.GetPreamble();

            using (var stream = File.Create(temp))
            {
                stream.Write(preamble);
                stream.Write(body);
            }

            File.Move(temp, full, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"{Path} could not be written ({ex.Message})";
            return false;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private XElement? Parse()
    {
        if (!_rootIsStale)
        {
            return _root;
        }

        try
        {
            _root = XDocument.Parse(_text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace).Root;
        }
        catch (XmlException)
        {
            _root = null;
        }

        _rootIsStale = false;
        return _root;
    }

    /// <summary>Every write invalidates both the cached parse and the offsets taken from it.</summary>
    private void Replace(string text)
    {
        _text = text;
        _rootIsStale = true;
    }

    private bool TryInsert(string propertyName, string value)
    {
        var root = Parse();
        if (root is null)
        {
            return false;
        }

        var lineStarts = BuildLineStarts(_text);

        var group = root.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase)
            && e.Attribute("Condition") is null);

        if (group is null)
        {
            return TryInsertNewGroup(root, propertyName, value, lineStarts);
        }

        var groupStart = FindElementStart(_text, group, lineStarts);
        if (groupStart < 0)
        {
            return false;
        }

        var closeIndex = FindClosingTag(_text, StartTagEnd(_text, groupStart) + 1, "PropertyGroup");
        if (closeIndex < 0)
        {
            return false;
        }

        var closeLineStart = StartOfLine(_text, closeIndex);
        var indent = ReadIndent(_text, closeLineStart) + "  ";

        // Match the indentation of the properties already in the group when there are any.
        var firstChild = group.Elements().FirstOrDefault();
        if (firstChild is not null)
        {
            var childStart = FindElementStart(_text, firstChild, lineStarts);
            if (childStart >= 0)
            {
                indent = ReadIndent(_text, StartOfLine(_text, childStart));
            }
        }

        Replace(_text.Insert(closeLineStart, indent + $"<{propertyName}>{Escape(value)}</{propertyName}>" + _newLine));
        return true;
    }

    private bool TryInsertNewGroup(XElement root, string propertyName, string value, int[] lineStarts)
    {
        var start = FindElementStart(_text, root, lineStarts);
        if (start < 0)
        {
            return false;
        }

        var tagEnd = StartTagEnd(_text, start);
        if (tagEnd < 0 || _text[tagEnd - 1] == '/')
        {
            return false;
        }

        var insertion = _newLine
            + _newLine + "  <PropertyGroup>"
            + _newLine + $"    <{propertyName}>{Escape(value)}</{propertyName}>"
            + _newLine + "  </PropertyGroup>"
            + _newLine;

        Replace(_text.Insert(tagEnd + 1, insertion));
        return true;
    }

    private PropertyLocation? Locate(XElement element, int[] lineStarts)
    {
        var name = element.Name.LocalName;
        var start = FindElementStart(_text, element, lineStarts);
        if (start < 0)
        {
            return null;
        }

        var tagEnd = StartTagEnd(_text, start);
        if (tagEnd < 0)
        {
            return null;
        }

        // A condition on the enclosing PropertyGroup (or on a Choose/When around it) gates the
        // property just as firmly as one on the property itself.
        var hasCondition = element.AncestorsAndSelf().Any(e => e.Attribute("Condition") is not null);

        if (_text[tagEnd - 1] == '/')
        {
            // <Version /> — the replacement has to rewrite the tag itself, so the span starts at
            // the first character that is not part of the name or an attribute.
            var spanStart = tagEnd - 1;
            while (spanStart > start && char.IsWhiteSpace(_text[spanStart - 1]))
            {
                spanStart--;
            }

            return new PropertyLocation(name, string.Empty, spanStart, tagEnd + 1, hasCondition, SelfClosing: true);
        }

        var closeIndex = FindClosingTag(_text, tagEnd + 1, name);
        if (closeIndex < 0)
        {
            return null;
        }

        return new PropertyLocation(
            name,
            _text[(tagEnd + 1)..closeIndex],
            tagEnd + 1,
            closeIndex,
            hasCondition,
            SelfClosing: false);
    }

    /// <summary>Index of the "&lt;/" that closes <paramref name="name"/>, skipping nested elements of other names.</summary>
    private static int FindClosingTag(string text, int from, string name)
    {
        var depth = 0;
        var index = from;

        while (index < text.Length)
        {
            var next = text.IndexOf('<', index);
            if (next < 0)
            {
                return -1;
            }

            if (MatchesName(text, next + 1, "/" + name) && depth == 0)
            {
                return next;
            }

            if (MatchesName(text, next + 1, name))
            {
                var tagEnd = StartTagEnd(text, next);
                if (tagEnd > 0 && text[tagEnd - 1] != '/')
                {
                    depth++;
                }
            }
            else if (MatchesName(text, next + 1, "/" + name))
            {
                depth--;
            }

            index = next + 1;
        }

        return -1;
    }

    private static bool MatchesName(string text, int index, string name)
    {
        if (index + name.Length > text.Length
            || string.Compare(text, index, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        // "<Versioning>" must not match "<Version".
        var after = index + name.Length;
        return after >= text.Length || !(char.IsLetterOrDigit(text[after]) || text[after] is '_' or '-' or '.');
    }

    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    /// <summary>
    /// Offset of the '&lt;' opening <paramref name="element"/>. XML line info reports a 1-based
    /// line and column whose exact anchor (the '&lt;' or the name just after it) is not worth
    /// depending on, so the column is treated as a hint and the '&lt;' is found by scanning back.
    /// </summary>
    private static int FindElementStart(string text, XElement element, int[] lineStarts)
    {
        if (element is not IXmlLineInfo info || !info.HasLineInfo())
        {
            return -1;
        }

        var line = info.LineNumber - 1;
        if (line < 0 || line >= lineStarts.Length)
        {
            return -1;
        }

        var hint = Math.Min(lineStarts[line] + Math.Max(0, info.LinePosition - 1), text.Length - 1);

        for (var i = hint; i >= 0 && i >= hint - 2; i--)
        {
            if (text[i] == '<' && MatchesName(text, i + 1, element.Name.LocalName))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Index of the '&gt;' closing a start tag, skipping quoted attribute values.</summary>
    private static int StartTagEnd(string text, int startTagIndex)
    {
        var quote = '\0';

        for (var i = startTagIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private static int StartOfLine(string text, int index)
    {
        var i = Math.Clamp(index, 0, Math.Max(0, text.Length - 1));
        while (i > 0 && text[i - 1] != '\n')
        {
            i--;
        }

        return i;
    }

    private static int EndOfLineIncludingBreak(string text, int index)
    {
        var i = index;
        while (i < text.Length && text[i] != '\n')
        {
            i++;
        }

        return i < text.Length ? i + 1 : text.Length;
    }

    private static string ReadIndent(string text, int lineStart)
    {
        var end = lineStart;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text[lineStart..end];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless.
        }
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
