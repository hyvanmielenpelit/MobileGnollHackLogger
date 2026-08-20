using System;
using System.IO;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class GitHelperTests : IDisposable
{
    private readonly string _tempRepoDir;

    public GitHelperTests()
    {
        _tempRepoDir = Path.Combine(Path.GetTempPath(), "GitHelperTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRepoDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRepoDir))
            {
                Directory.Delete(_tempRepoDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures in test temp dir
        }
    }

    [Fact]
    public void GetGitHeadSha_DirectRefFile_ReturnsSha()
    {
        var gitDir = Path.Combine(_tempRepoDir, ".git");
        var refsHeadsDir = Path.Combine(gitDir, "refs", "heads");
        Directory.CreateDirectory(refsHeadsDir);

        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/master\n");
        string expectedSha = "abcdef0123456789abcdef0123456789abcdef01";
        File.WriteAllText(Path.Combine(refsHeadsDir, "master"), expectedSha + "\n");

        string? sha = GitHelper.GetGitHeadSha(_tempRepoDir);
        Assert.Equal(expectedSha, sha);

        string branch = GitHelper.GetCurrentBranch(_tempRepoDir);
        Assert.Equal("master", branch);
    }

    [Fact]
    public void GetGitHeadSha_PackedRefs_ReturnsSha()
    {
        var gitDir = Path.Combine(_tempRepoDir, ".git");
        Directory.CreateDirectory(gitDir);

        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature-branch\n");
        string expectedSha = "1234567890abcdef1234567890abcdef12345678";
        string packedRefsContent = $@"# pack-refs with: peeled-tags
^0000000000000000000000000000000000000000
{expectedSha} refs/heads/feature-branch
deadbeefdeadbeefdeadbeefdeadbeefdeadbeef refs/heads/other
";
        File.WriteAllText(Path.Combine(gitDir, "packed-refs"), packedRefsContent);

        string? sha = GitHelper.GetGitHeadSha(_tempRepoDir);
        Assert.Equal(expectedSha, sha);

        string branch = GitHelper.GetCurrentBranch(_tempRepoDir);
        Assert.Equal("feature-branch", branch);
    }

    [Fact]
    public void GetGitHeadSha_DetachedHead_ReturnsSha()
    {
        var gitDir = Path.Combine(_tempRepoDir, ".git");
        Directory.CreateDirectory(gitDir);

        string expectedSha = "9876543210fedcba9876543210fedcba98765432";
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), expectedSha + "\n");

        string? sha = GitHelper.GetGitHeadSha(_tempRepoDir);
        Assert.Equal(expectedSha, sha);

        string branch = GitHelper.GetCurrentBranch(_tempRepoDir);
        Assert.Equal(string.Empty, branch);
    }

    [Fact]
    public void GetGitHeadSha_NonExistentRepo_ReturnsNull()
    {
        string? sha = GitHelper.GetGitHeadSha(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N")));
        Assert.Null(sha);

        string branch = GitHelper.GetCurrentBranch(Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N")));
        Assert.Equal(string.Empty, branch);
    }
}
