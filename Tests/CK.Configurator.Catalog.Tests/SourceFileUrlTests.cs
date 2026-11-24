using NUnit.Framework;
using Shouldly;

namespace CK.Configurator.Catalog.Tests;

[TestFixture]
public class SourceFileUrlTests
{
    const string _sha = "eb544ad1c23729e914a699eccc64168ef70657bb";

    [TestCase( "https://raw.githubusercontent.com/signature-opensource/CK-AppIdentity-Transport/" + _sha + "/CK.AppIdentity.CKey/CKeyFeatureDriver.cs",
               SourceHostKind.GitHub, "https://github.com/signature-opensource/CK-AppIdentity-Transport",
               "https://github.com/signature-opensource/CK-AppIdentity-Transport/blob/v4.0.0-0.uc.2/CK.AppIdentity.CKey/CKeyFeatureDriver.cs",
               "https://github.com/signature-opensource/CK-AppIdentity-Transport/blob/" + _sha + "/CK.AppIdentity.CKey/CKeyFeatureDriver.cs" )]
    [TestCase( "https://github.acme.com/raw/team/Repo/" + _sha + "/Src/A.cs",
               SourceHostKind.GitHub, "https://github.acme.com/team/Repo",
               "https://github.acme.com/team/Repo/blob/v1.0.0/Src/A.cs",
               "https://github.acme.com/team/Repo/blob/" + _sha + "/Src/A.cs" )]
    [TestCase( "https://gitlab.com/group/sub/Repo/-/raw/" + _sha + "/Src/A.cs",
               SourceHostKind.GitLab, "https://gitlab.com/group/sub/Repo",
               "https://gitlab.com/group/sub/Repo/-/blob/v1.0.0/Src/A.cs",
               "https://gitlab.com/group/sub/Repo/-/blob/" + _sha + "/Src/A.cs" )]
    [TestCase( "https://gitlab.acme.com/group/Repo/raw/" + _sha + "/Src/A.cs",
               SourceHostKind.GitLab, "https://gitlab.acme.com/group/Repo",
               "https://gitlab.acme.com/group/Repo/-/blob/v1.0.0/Src/A.cs",
               "https://gitlab.acme.com/group/Repo/-/blob/" + _sha + "/Src/A.cs" )]
    [TestCase( "https://gitea.acme.com/owner/Repo/raw/commit/" + _sha + "/Src/A.cs",
               SourceHostKind.Gitea, "https://gitea.acme.com/owner/Repo",
               "https://gitea.acme.com/owner/Repo/src/tag/v1.0.0/Src/A.cs",
               "https://gitea.acme.com/owner/Repo/src/commit/" + _sha + "/Src/A.cs" )]
    public void raw_urls_give_the_pages_at_the_tag_and_at_the_commit( string raw, SourceHostKind host, string repository, string atTag, string atCommit )
    {
        SourceFileUrl.TryParse( raw, out var f ).ShouldBeTrue();
        f.Host.ShouldBe( host );
        f.Repository.ShouldBe( repository );
        f.Commit.ShouldBe( _sha );
        f.GetPageUrl( atTag.Contains( "v4." ) ? "v4.0.0-0.uc.2" : "v1.0.0" ).ShouldBe( atTag );
        f.GetPageUrl().ShouldBe( atCommit );
    }

    [TestCase( null )]
    [TestCase( "not an url" )]
    [TestCase( "https://example.com/some/file.cs" )]
    [TestCase( "https://raw.githubusercontent.com/o/r/main/file.cs" )]
    public void unknown_urls_are_rejected( string? raw )
    {
        SourceFileUrl.TryParse( raw, out var f ).ShouldBeFalse();
        f.ShouldBeNull();
    }
}
