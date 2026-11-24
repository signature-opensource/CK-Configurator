using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.Configurator.Catalog;

/// <summary>
/// The Git hosts whose source file URLs are known.
/// </summary>
public enum SourceHostKind
{
    /// <summary>
    /// GitHub (github.com or a GitHub Enterprise server).
    /// </summary>
    GitHub,

    /// <summary>
    /// GitLab (gitlab.com or a self-hosted server).
    /// </summary>
    GitLab,

    /// <summary>
    /// Gitea (and its forks: Forgejo, Codeberg).
    /// </summary>
    Gitea
}

/// <summary>
/// A source file in a commit of a repository, read from the raw URL that SourceLink writes in a PDB
/// (<c>Microsoft.SourceLink.GitHub</c>, <c>.GitLab</c>, <c>.Gitea</c>): the page of the file is derived from it, at the
/// commit or at a tag.
/// <list type="bullet">
///   <item>GitHub: <c>https://raw.githubusercontent.com/{owner}/{repo}/{sha}/{path}</c> (Enterprise:
///   <c>https://{host}/raw/{owner}/{repo}/{sha}/{path}</c>); the page is <c>{repository}/blob/{ref}/{path}</c>.</item>
///   <item>GitLab: <c>{repository}/-/raw/{sha}/{path}</c> (older: <c>{repository}/raw/{sha}/{path}</c>); the page is
///   <c>{repository}/-/blob/{ref}/{path}</c>.</item>
///   <item>Gitea: <c>{repository}/raw/commit/{sha}/{path}</c>; the page is <c>{repository}/src/commit/{sha}/{path}</c> or
///   <c>{repository}/src/tag/{tag}/{path}</c>.</item>
/// </list>
/// </summary>
/// <param name="Host">The kind of host.</param>
/// <param name="Repository">The URL of the repository ("https://github.com/signature-opensource/CK-AppIdentity-Transport").</param>
/// <param name="Commit">The commit SHA.</param>
/// <param name="Path">The path of the file in the repository (URL encoded, '/' separated).</param>
public sealed record SourceFileUrl( SourceHostKind Host, string Repository, string Commit, string Path )
{
    /// <summary>
    /// Gets the URL of the page of the file at a tag, or at its <see cref="Commit"/>.
    /// </summary>
    /// <param name="tag">The tag (that must designate the <see cref="Commit"/>). Null for the commit.</param>
    /// <returns>The URL of the page of the file.</returns>
    public string GetPageUrl( string? tag = null )
    {
        var r = tag != null ? Uri.EscapeDataString( tag ) : Commit;
        return Host switch
        {
            SourceHostKind.GitLab => $"{Repository}/-/blob/{r}/{Path}",
            SourceHostKind.Gitea => tag != null ? $"{Repository}/src/tag/{r}/{Path}" : $"{Repository}/src/commit/{r}/{Path}",
            _ => $"{Repository}/blob/{r}/{Path}"
        };
    }

    /// <summary>
    /// Reads a raw source file URL (see <see cref="SourceFileUrl"/>).
    /// </summary>
    /// <param name="rawUrl">The raw URL.</param>
    /// <param name="result">The source file on success.</param>
    /// <returns>True when the URL is a known raw URL, false otherwise.</returns>
    public static bool TryParse( string? rawUrl, [NotNullWhen( true )] out SourceFileUrl? result )
    {
        result = null;
        if( !Uri.TryCreate( rawUrl, UriKind.Absolute, out var uri ) ) return false;
        var s = uri.AbsolutePath.Trim( '/' ).Split( '/' );
        var root = uri.GetLeftPart( UriPartial.Authority );
        if( uri.Host.Equals( "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase ) )
        {
            // {owner}/{repo}/{sha}/{path}
            return s.Length > 3
                   && Create( SourceHostKind.GitHub, $"https://github.com/{s[0]}/{s[1]}", s, 2, out result );
        }
        for( int i = 1; i < s.Length - 2; ++i )
        {
            if( s[i] == "-" && s[i + 1] == "raw" )
            {
                return Create( SourceHostKind.GitLab, Join( root, s, i ), s, i + 2, out result );
            }
            if( s[i] == "raw" && s[i + 1] == "commit" )
            {
                return Create( SourceHostKind.Gitea, Join( root, s, i ), s, i + 2, out result );
            }
        }
        if( s.Length > 4 && s[0] == "raw" )
        {
            // GitHub Enterprise: raw/{owner}/{repo}/{sha}/{path}
            return Create( SourceHostKind.GitHub, $"{root}/{s[1]}/{s[2]}", s, 3, out result );
        }
        int r = Array.IndexOf( s, "raw", 2 );
        // The older GitLab URLs: {repository}/raw/{sha}/{path}
        return r > 0 && Create( SourceHostKind.GitLab, Join( root, s, r ), s, r + 1, out result );

        static string Join( string root, string[] s, int count ) => root + "/" + string.Join( '/', s.Take( count ) );

        static bool Create( SourceHostKind host, string repository, string[] s, int sha, out SourceFileUrl? result )
        {
            result = null;
            if( sha + 1 >= s.Length || !IsSha( s[sha] ) ) return false;
            result = new SourceFileUrl( host, repository, s[sha], string.Join( '/', s.Skip( sha + 1 ) ) );
            return true;
        }

        static bool IsSha( string s ) => s.Length is 40 or 64 && s.All( char.IsAsciiHexDigit );
    }
}
