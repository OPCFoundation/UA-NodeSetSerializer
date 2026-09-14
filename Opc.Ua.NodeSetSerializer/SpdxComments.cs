using System.Text;
using System.Text.RegularExpressions;
using Opc.Ua.NodeSetSerializer.Model;

namespace Opc.Ua.NodeSetSerializer;

/// <summary>
/// Moves the licence/copyright header between the two NodeSet encodings.
///
/// <para>The XML NodeSet carries it in comments at the top of the file, in one of two shapes: the
/// modern per-line SPDX directives, or a single large prose block (the one the OPC Foundation ships
/// on the Core NodeSet). JSON has no comment syntax, so the same information becomes the structured
/// <see cref="SpdxDeclaration"/> that opens the document.</para>
///
/// <para>Where a directive is absent the licence id is inferred from the prose, which is what makes
/// the Foundation's "OPC Foundation MIT License 1.00" block come across as a plain <c>MIT</c>
/// identifier rather than a custom licence reference.</para>
/// </summary>
public static class SpdxComments
{
    private static readonly Regex CommentRx =
        new(@"<!--(.*?)-->", RegexOptions.Singleline | RegexOptions.Compiled);

    // The directives may be their own line-comments or appear inside a larger block.
    private static readonly Regex CopyrightDirectiveRx =
        new(@"SPDX-FileCopyrightText:\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LicenceIdDirectiveRx =
        new(@"SPDX-License-Identifier:\s*([^\s<*]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LicenceRefDirectiveRx =
        new(@"\bLicense:\s*(https?://\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fallbacks for a header with no explicit directives.
    private static readonly Regex CopyrightLineRx =
        new(@"Copyright\b[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Ordered most-specific first (AGPL/LGPL before GPL). MIT covers the Core NodeSet, whose header
    // reads "OPC Foundation MIT License 1.00" followed by the MIT permission text.
    private static readonly (Regex Rx, string Id)[] LicenceInferences =
    {
        (new(@"\bMIT\s+License\b|Permission is hereby granted,\s+free of charge", RegexOptions.IgnoreCase | RegexOptions.Compiled), "MIT"),
        (new(@"Apache\s+License,?\s+(?:[Vv]ersion\s+)?2\.0|\bApache-2\.0\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Apache-2.0"),
        (new(@"\bBSD[\s-]*3[\s-]*Clause\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "BSD-3-Clause"),
        (new(@"\bBSD[\s-]*2[\s-]*Clause\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "BSD-2-Clause"),
        (new(@"Mozilla\s+Public\s+License,?\s+(?:[Vv]ersion\s+)?2\.0|\bMPL-2\.0\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "MPL-2.0"),
        (new(@"Affero\s+General\s+Public\s+License|\bAGPL-3\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "AGPL-3.0-only"),
        (new(@"Lesser\s+General\s+Public\s+License|\bLGPL-3\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "LGPL-3.0-only"),
        (new(@"General\s+Public\s+License,?\s+(?:[Vv]ersion\s+)?3|\bGPL-3\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "GPL-3.0-only"),
        (new(@"General\s+Public\s+License,?\s+(?:[Vv]ersion\s+)?2|\bGPL-2\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "GPL-2.0-only"),
        (new(@"\bISC\s+License\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ISC"),
    };

    /// <summary>
    /// True when the id is a custom licence reference ("LicenseRef-…") rather than a standard SPDX
    /// License List id. Only custom licences carry a <see cref="SpdxDeclaration.LicenceRef"/> URL;
    /// a standard id is self-describing, so its URL is dropped.
    /// </summary>
    public static bool IsCustomLicenceId(string? licenceId)
        => licenceId?.Trim() is { Length: > 0 } id
           && id.Contains("LicenseRef-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the header out of NodeSet XML. Returns null when the file carries no comments, or none
    /// that yield a copyright or a licence.
    /// </summary>
    public static SpdxDeclaration? Parse(string? xml)
    {
        if (String.IsNullOrEmpty(xml)) return null;

        // Only comment bodies — a "Copyright" in node content is not a header.
        var comments = new StringBuilder();

        foreach (Match match in CommentRx.Matches(xml))
        {
            comments.Append(match.Groups[1].Value).Append('\n');
        }

        var text = comments.ToString();

        if (text.Trim().Length == 0) return null;

        var licenceId = First(LicenceIdDirectiveRx, text);

        if (licenceId == null)
        {
            foreach (var (rx, id) in LicenceInferences)
            {
                if (rx.IsMatch(text)) { licenceId = id; break; }
            }
        }

        var copyrightText = First(CopyrightDirectiveRx, text) ?? First(CopyrightLineRx, text, group: 0);
        var licenceRef = IsCustomLicenceId(licenceId) ? First(LicenceRefDirectiveRx, text) : null;

        if (copyrightText == null && licenceId == null) return null;

        return new SpdxDeclaration()
        {
            CopyrightText = copyrightText,
            LicenceId = licenceId,
            LicenceRef = licenceRef
        };
    }

    /// <summary>
    /// Renders the declaration as the comment body to write at the top of a NodeSet XML file, in the
    /// directive form <see cref="Parse"/> reads back. Returns null when there is nothing to say.
    /// </summary>
    public static string? Format(SpdxDeclaration? spdx)
    {
        if (spdx == null) return null;

        var lines = new List<string>();

        if (!String.IsNullOrWhiteSpace(spdx.CopyrightText))
        {
            lines.Add($" SPDX-FileCopyrightText: {spdx.CopyrightText.Trim()} ");
        }

        if (!String.IsNullOrWhiteSpace(spdx.LicenceId))
        {
            lines.Add($" SPDX-License-Identifier: {spdx.LicenceId.Trim()} ");
        }

        if (!String.IsNullOrWhiteSpace(spdx.LicenceRef) && IsCustomLicenceId(spdx.LicenceId))
        {
            lines.Add($" License: {spdx.LicenceRef.Trim()} ");
        }

        return lines.Count > 0 ? String.Join("\n", lines) : null;
    }

    private static string? First(Regex rx, string text, int group = 1)
    {
        var match = rx.Match(text);
        return match.Success && match.Groups[group].Value.Trim() is { Length: > 0 } value ? value : null;
    }
}
