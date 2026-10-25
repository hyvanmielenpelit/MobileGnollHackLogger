using System.Text;
using Microsoft.AspNetCore.Http;
using MobileGnollHackLogger.Areas.API;

namespace MobileGnollHackLogger.Tests.Unit;

public class BonesModelTests
{
    [Fact]
    public void GetJson_ContainsCommandAndData_NeverCredentialsOrFile()
    {
        byte[] content = Encoding.ASCII.GetBytes("bonesfilecontent");
        using var stream = new MemoryStream(content);
        var model = new BonesModel
        {
            BonesFile = new FormFile(stream, 0, content.Length, "BonesFile", "uploadedbonesname"),
            Command = "SendBonesFile",
            Data = "dataValue",
            AllowedUsers = "friend1 friend2",
            VersionNumber = 9000,
            VersionCompatibilityNumber = 9000,
            Platform = "windows",
            PlatformVersion = "10.0",
            Port = "GnollHack Windows",
            PortVersion = "4.1.3",
            PortBuild = "1",
            UserName = "playerName",
            Password = "playerPassword",
            AntiForgeryToken = "sharedSecretToken"
        };

        string json = model.GetJson();

        Assert.Contains("\"Command\":\"SendBonesFile\"", json);
        Assert.Contains("\"Data\":\"dataValue\"", json);

        Assert.DoesNotContain("UserName", json);
        Assert.DoesNotContain("playerName", json);
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("playerPassword", json);
        Assert.DoesNotContain("AntiForgeryToken", json);
        Assert.DoesNotContain("sharedSecretToken", json);
        Assert.DoesNotContain("\"BonesFile\"", json);
        Assert.DoesNotContain("uploadedbonesname", json);
        Assert.DoesNotContain("bonesfilecontent", json);
    }

    [Fact]
    public void Getters_OnEmptyModel_ReturnDefaults()
    {
        var model = new BonesModel();

        Assert.Equal("Unknown", model.GetPlatform());
        Assert.Equal("", model.GetPlatformVersion());
        Assert.Equal("", model.GetPort());
        Assert.Equal("", model.GetPortVersion());
        Assert.Equal("", model.GetPortBuild());
    }

    [Fact]
    public void Getters_WithValues_ReturnThem()
    {
        var model = new BonesModel
        {
            Platform = "android",
            PlatformVersion = "14",
            Port = "GnollHack Android",
            PortVersion = "4.1.3",
            PortBuild = "7"
        };

        Assert.Equal("android", model.GetPlatform());
        Assert.Equal("14", model.GetPlatformVersion());
        Assert.Equal("GnollHack Android", model.GetPort());
        Assert.Equal("4.1.3", model.GetPortVersion());
        Assert.Equal("7", model.GetPortBuild());
    }
}
