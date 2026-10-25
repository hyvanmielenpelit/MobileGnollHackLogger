using MobileGnollHackLogger.Areas.API;

namespace MobileGnollHackLogger.Tests.Unit;

public class BonesFileNamesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "BonesFileNamesTests", "bones");

    [Theory]
    [InlineData("bon-2D0.15")]
    [InlineData("bon3-1QBar.n")]
    [InlineData("bon-0M0.T")]
    public void IsValid_GnollHackBonesName_ReturnsTrue(string fileName)
    {
        Assert.True(BonesFileNames.IsValid(fileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a..b")]
    [InlineData("..\\x")]
    [InlineData("../x")]
    [InlineData("sub\\x")]
    [InlineData("sub/x")]
    [InlineData("C:\\x")]
    [InlineData("C:x")]
    [InlineData(".hidden")]
    [InlineData("-x")]
    [InlineData(" ")]
    [InlineData("bon-2D0 15")]
    public void IsValid_UnsafeOrMalformedName_ReturnsFalse(string? fileName)
    {
        Assert.False(BonesFileNames.IsValid(fileName));
    }

    [Fact]
    public void IsValid_64Characters_ReturnsTrue()
    {
        Assert.True(BonesFileNames.IsValid(new string('a', 64)));
    }

    [Fact]
    public void IsValid_65Characters_ReturnsFalse()
    {
        Assert.False(BonesFileNames.IsValid(new string('a', 65)));
    }

    [Fact]
    public void IsInsideDirectory_FileDirectlyInsideRoot_ReturnsTrue()
    {
        Assert.True(BonesFileNames.IsInsideDirectory(Path.Combine(Root, "f"), Root));
    }

    [Fact]
    public void IsInsideDirectory_FileTwoLevelsInsideRoot_ReturnsTrue()
    {
        Assert.True(BonesFileNames.IsInsideDirectory(Path.Combine(Root, "player", "sub", "f"), Root));
    }

    [Fact]
    public void IsInsideDirectory_RootItself_ReturnsFalse()
    {
        Assert.False(BonesFileNames.IsInsideDirectory(Root, Root));
        Assert.False(BonesFileNames.IsInsideDirectory(Root + Path.DirectorySeparatorChar, Root));
    }

    [Fact]
    public void IsInsideDirectory_SiblingSharingRootPrefix_ReturnsFalse()
    {
        Assert.False(BonesFileNames.IsInsideDirectory(Path.Combine(Root + "2", "f"), Root));
    }

    [Fact]
    public void IsInsideDirectory_PathEscapingWithParentSegment_ReturnsFalse()
    {
        Assert.False(BonesFileNames.IsInsideDirectory(Path.Combine(Root, "..", "f"), Root));
    }

    [Fact]
    public void IsInsideDirectory_DifferentCase_ReturnsTrue()
    {
        Assert.True(BonesFileNames.IsInsideDirectory(Path.Combine(Root.ToUpperInvariant(), "f"), Root));
    }

    [Fact]
    public void IsInsideDirectory_RootWithTrailingSeparator_BehavesLikeRootWithout()
    {
        string rootWithSeparator = Root + Path.DirectorySeparatorChar;

        Assert.True(BonesFileNames.IsInsideDirectory(Path.Combine(Root, "f"), rootWithSeparator));
        Assert.True(BonesFileNames.IsInsideDirectory(Path.Combine(Root, "player", "sub", "f"), rootWithSeparator));
        Assert.False(BonesFileNames.IsInsideDirectory(Root, rootWithSeparator));
        Assert.False(BonesFileNames.IsInsideDirectory(Path.Combine(Root + "2", "f"), rootWithSeparator));
        Assert.False(BonesFileNames.IsInsideDirectory(Path.Combine(Root, "..", "f"), rootWithSeparator));
    }
}
