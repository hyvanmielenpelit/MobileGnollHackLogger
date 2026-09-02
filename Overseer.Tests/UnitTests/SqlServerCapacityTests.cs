using Overseer.Models;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class SqlServerCapacityTests
{
    private const int Express = SqlServerCapacity.ExpressEngineEdition;
    private const int Standard = 2;
    private const int Enterprise = 3;
    private const int AzureSqlDatabase = 5;

    private static SqlServerEditionInfo ExpressInfo(int? major, string? version = null, string edition = "Express Edition (64-bit)")
        => new(edition, Express, major, version, "RTM");

    private static ChatRetentionSettings PercentOnlySettings(double warning = 75, double critical = 85)
        => new()
        {
            DatabaseWarningThresholdPercent = warning,
            DatabaseCriticalThresholdPercent = critical,
            DatabaseWarningThresholdMb = 0,
            DatabaseCriticalThresholdMb = 0,
        };

    // --- Express limit detection ---

    /// <summary>
    /// The exact SERVERPROPERTY output observed on the production instance on 2026-09-02.
    /// This pins the real-world case, so a future change to the resolver that would alter
    /// what the live panel displays fails here rather than in production.
    /// </summary>
    [Fact]
    public void Resolve_ObservedProductionInstance_Resolves10GbExpress2022()
    {
        var info = new SqlServerEditionInfo("Express Edition (64-bit)", 4, 16, "16.0.1000.6", "RTM");

        var result = SqlServerCapacity.Resolve(info, configuredOverrideMb: 0);

        Assert.Equal(10240, result.MaxLimitMb);
        Assert.True(result.HasEngineSizeLimit);
        Assert.Equal("Detected", result.LimitSource);
        Assert.Equal("SQL Server 2022 Express", result.ProductLabel);
        Assert.Equal("Express Edition (64-bit)", result.EditionName);
        Assert.Equal("16.0.1000.6", result.ProductVersion);

        // The panel must keep classifying exactly as it did before this change.
        var settings = new ChatRetentionSettings();
        Assert.Equal("Normal", SqlServerCapacity.ResolveStatusLevel(7679, result, settings));
        Assert.Equal("Warning", SqlServerCapacity.ResolveStatusLevel(7680, result, settings));
        Assert.Equal("Critical", SqlServerCapacity.ResolveStatusLevel(8704, result, settings));
    }

    /// <summary>
    /// The same instance after a hypothetical in-place upgrade to SQL Server 2025 Express:
    /// no code or configuration change, a 50 GB ceiling, and the old critical point quiet.
    /// </summary>
    [Fact]
    public void Resolve_ProductionInstanceUpgradedTo2025_Resolves50GbWithNoConfigChange()
    {
        var info = new SqlServerEditionInfo("Express Edition (64-bit)", 4, 17, "17.0.1000.6", "RTM");
        var settings = new ChatRetentionSettings();

        var result = SqlServerCapacity.Resolve(info, settings.DatabaseMaxSizeMbOverride);

        Assert.Equal(51200, result.MaxLimitMb);
        Assert.Equal("Detected", result.LimitSource);
        Assert.Equal("SQL Server 2025 Express", result.ProductLabel);
        Assert.Equal("Normal", SqlServerCapacity.ResolveStatusLevel(8704, result, settings));
    }

    [Fact]
    public void Resolve_Express2022_Returns10Gb()
    {
        var result = SqlServerCapacity.Resolve(ExpressInfo(16, "16.0.4200.1"), configuredOverrideMb: 0);

        Assert.Equal(10240, result.MaxLimitMb);
        Assert.True(result.HasEngineSizeLimit);
        Assert.Equal("Detected", result.LimitSource);
        Assert.Equal("SQL Server 2022 Express", result.ProductLabel);
        Assert.Equal("Express Edition (64-bit)", result.EditionName);
        Assert.Equal("16.0.4200.1", result.ProductVersion);
    }

    [Fact]
    public void Resolve_Express2025_Returns50Gb()
    {
        var result = SqlServerCapacity.Resolve(ExpressInfo(17, "17.0.1000.6"), configuredOverrideMb: 0);

        Assert.Equal(51200, result.MaxLimitMb);
        Assert.True(result.HasEngineSizeLimit);
        Assert.Equal("Detected", result.LimitSource);
        Assert.Equal("SQL Server 2025 Express", result.ProductLabel);
    }

    [Fact]
    public void Resolve_ExpressNewerThanKnown_InheritsMostRecentLimit()
    {
        // An unrecognised newer Express must not fall back to the oldest limit.
        var result = SqlServerCapacity.Resolve(ExpressInfo(18, "18.0.100.1"), configuredOverrideMb: 0);

        Assert.Equal(51200, result.MaxLimitMb);
        Assert.Equal("Detected", result.LimitSource);
        Assert.Equal("SQL Server (major version 18) Express", result.ProductLabel);
    }

    [Theory]
    [InlineData(11, "11.0.7001.0", 10240)] // 2012
    [InlineData(13, "13.0.5026.0", 10240)] // 2016
    [InlineData(15, "15.0.4236.7", 10240)] // 2019
    public void Resolve_ExpressBetween2012And2022_Returns10Gb(int major, string version, double expected)
    {
        var result = SqlServerCapacity.Resolve(ExpressInfo(major, version), configuredOverrideMb: 0);

        Assert.Equal(expected, result.MaxLimitMb);
    }

    [Fact]
    public void Resolve_Express2008R2_Returns10Gb()
    {
        // 10.50 raised the Express cap from 4 GB to 10 GB.
        var result = SqlServerCapacity.Resolve(ExpressInfo(null, "10.50.1600.1"), configuredOverrideMb: 0);

        Assert.Equal(10240, result.MaxLimitMb);
    }

    [Fact]
    public void Resolve_Express2008_Returns4Gb()
    {
        var result = SqlServerCapacity.Resolve(ExpressInfo(null, "10.00.1600.22"), configuredOverrideMb: 0);

        Assert.Equal(4096, result.MaxLimitMb);
    }

    [Fact]
    public void Resolve_ExpressWithNullMajorVersion_ParsesProductVersionInstead()
    {
        // ProductMajorVersion is NULL before SQL Server 2014 SP2; ProductVersion always exists.
        var result = SqlServerCapacity.Resolve(ExpressInfo(null, "17.0.1000.6"), configuredOverrideMb: 0);

        Assert.Equal(51200, result.MaxLimitMb);
        Assert.Equal("Detected", result.LimitSource);
        Assert.Equal("SQL Server 2025 Express", result.ProductLabel);
    }

    [Fact]
    public void Resolve_ExpressWithNoVersionAtAll_FallsBackTo10Gb()
    {
        var result = SqlServerCapacity.Resolve(ExpressInfo(null, null), configuredOverrideMb: 0);

        Assert.Equal(10240, result.MaxLimitMb);
        Assert.True(result.HasEngineSizeLimit);
        Assert.Equal("Fallback", result.LimitSource);
    }

    // --- Non-Express editions ---

    [Fact]
    public void Resolve_Standard2022_HasNoEngineLimit()
    {
        var info = new SqlServerEditionInfo("Standard Edition (64-bit)", Standard, 16, "16.0.4200.1", "RTM");

        var result = SqlServerCapacity.Resolve(info, configuredOverrideMb: 0);

        Assert.False(result.HasEngineSizeLimit);
        Assert.Equal(0, result.MaxLimitMb);
        Assert.Equal("Detected", result.LimitSource);
        Assert.Equal("SQL Server 2022 Standard", result.ProductLabel);
    }

    [Fact]
    public void Resolve_Enterprise_HasNoEngineLimit()
    {
        var info = new SqlServerEditionInfo("Enterprise Edition: Core-based Licensing (64-bit)", Enterprise, 16, "16.0.4200.1", "RTM");

        var result = SqlServerCapacity.Resolve(info, configuredOverrideMb: 0);

        Assert.False(result.HasEngineSizeLimit);
        Assert.Equal(0, result.MaxLimitMb);
        Assert.Equal("SQL Server 2022 Enterprise", result.ProductLabel);
    }

    [Fact]
    public void Resolve_AzureSqlDatabase_LabelsWithoutMarketingYear()
    {
        var info = new SqlServerEditionInfo("SQL Azure", AzureSqlDatabase, 12, "12.0.2000.8", "RTM");

        var result = SqlServerCapacity.Resolve(info, configuredOverrideMb: 0);

        Assert.False(result.HasEngineSizeLimit);
        Assert.Equal("Azure SQL Database", result.ProductLabel);
    }

    // --- Detection failure and overrides ---

    [Fact]
    public void Resolve_NullInfo_FallsBackConservatively()
    {
        var result = SqlServerCapacity.Resolve(null, configuredOverrideMb: 0);

        Assert.Equal(10240, result.MaxLimitMb);
        Assert.True(result.HasEngineSizeLimit);
        Assert.Equal("Fallback", result.LimitSource);
        Assert.Equal("SQL Server (edition undetected)", result.ProductLabel);
    }

    [Fact]
    public void Resolve_OverrideWins_ButKeepsDetectedIdentity()
    {
        var result = SqlServerCapacity.Resolve(ExpressInfo(16, "16.0.4200.1"), configuredOverrideMb: 51200);

        Assert.Equal(51200, result.MaxLimitMb);
        Assert.Equal("Configured", result.LimitSource);
        Assert.Equal("SQL Server 2022 Express", result.ProductLabel);
    }

    [Fact]
    public void Resolve_ZeroOverride_LetsDetectionWin()
    {
        var result = SqlServerCapacity.Resolve(ExpressInfo(17, "17.0.1000.6"), configuredOverrideMb: 0);

        Assert.Equal(51200, result.MaxLimitMb);
        Assert.Equal("Detected", result.LimitSource);
    }

    [Fact]
    public void Resolve_OverrideOnNonExpress_ProvidesBudgetWithoutClaimingEngineLimit()
    {
        var info = new SqlServerEditionInfo("Standard Edition (64-bit)", Standard, 16, "16.0.4200.1", "RTM");

        var result = SqlServerCapacity.Resolve(info, configuredOverrideMb: 20480);

        Assert.Equal(20480, result.MaxLimitMb);
        Assert.Equal("Configured", result.LimitSource);
        Assert.False(result.HasEngineSizeLimit);
    }

    // --- Status classification ---

    [Theory]
    [InlineData(7679, "Normal")]
    [InlineData(7680, "Warning")]
    [InlineData(8703, "Warning")]
    [InlineData(8704, "Critical")]
    public void ResolveStatusLevel_On10Gb_MatchesLegacyAbsoluteBoundariesExactly(double allocatedMb, string expected)
    {
        // 75% and 85% of 10240 MB are exactly 7680 and 8704, the values this panel used
        // before thresholds became percentages. Behaviour must be unchanged to the megabyte.
        var capacity = SqlServerCapacity.Resolve(ExpressInfo(16, "16.0.4200.1"), 0);

        var status = SqlServerCapacity.ResolveStatusLevel(allocatedMb, capacity, PercentOnlySettings());

        Assert.Equal(expected, status);
    }

    [Fact]
    public void ResolveStatusLevel_On50Gb_DoesNotFireAtTheOld10GbCriticalPoint()
    {
        // The regression this change exists to prevent: 8704 MB is 85% of 10 GB but only
        // 17% of 50 GB, and must not raise a Critical alert on the larger instance.
        var capacity = SqlServerCapacity.Resolve(ExpressInfo(17, "17.0.1000.6"), 0);

        var status = SqlServerCapacity.ResolveStatusLevel(8704, capacity, PercentOnlySettings());

        Assert.Equal("Normal", status);
    }

    [Fact]
    public void ResolveStatusLevel_On50Gb_FiresAtTheScaledBoundary()
    {
        var capacity = SqlServerCapacity.Resolve(ExpressInfo(17, "17.0.1000.6"), 0);
        var settings = PercentOnlySettings();

        Assert.Equal("Normal", SqlServerCapacity.ResolveStatusLevel(38399, capacity, settings));
        Assert.Equal("Warning", SqlServerCapacity.ResolveStatusLevel(38400, capacity, settings));
        Assert.Equal("Critical", SqlServerCapacity.ResolveStatusLevel(43520, capacity, settings));
    }

    [Fact]
    public void ResolveStatusLevel_LegacyAbsoluteThreshold_StillFiresOnALargerLimit()
    {
        var capacity = SqlServerCapacity.Resolve(ExpressInfo(17, "17.0.1000.6"), 0);
        var settings = PercentOnlySettings();
        settings.DatabaseWarningThresholdMb = 8000;

        // 8100 MB is well under 75% of 50 GB, but over the operator's absolute ceiling.
        Assert.Equal("Warning", SqlServerCapacity.ResolveStatusLevel(8100, capacity, settings));
    }

    [Fact]
    public void ResolveStatusLevel_MoreSevereOfPercentAndAbsoluteWins()
    {
        var capacity = SqlServerCapacity.Resolve(ExpressInfo(16, "16.0.4200.1"), 0);
        var settings = PercentOnlySettings();
        settings.DatabaseCriticalThresholdMb = 5000;

        // 6000 MB is only 58% of 10 GB (Normal by percentage) but past the absolute ceiling.
        Assert.Equal("Critical", SqlServerCapacity.ResolveStatusLevel(6000, capacity, settings));

        // And the reverse: percentage is more severe than a lenient absolute setting.
        var lenient = PercentOnlySettings();
        lenient.DatabaseWarningThresholdMb = 10000;
        Assert.Equal("Critical", SqlServerCapacity.ResolveStatusLevel(9000, capacity, lenient));
    }

    [Fact]
    public void ResolveStatusLevel_InvertedPercentages_DoNotDisableTheCriticalBand()
    {
        var capacity = SqlServerCapacity.Resolve(ExpressInfo(16, "16.0.4200.1"), 0);
        var settings = PercentOnlySettings(warning: 85, critical: 50);

        // Critical is held at no lower than the warning band rather than firing at 50%.
        Assert.Equal("Normal", SqlServerCapacity.ResolveStatusLevel(6000, capacity, settings));
        Assert.Equal("Critical", SqlServerCapacity.ResolveStatusLevel(8704, capacity, settings));
        Assert.True(SqlServerCapacity.HasInvalidThresholds(settings));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 200)]
    [InlineData(150, 300)]
    public void ResolveStatusLevel_OutOfRangePercentages_FallBackToDefaults(double warning, double critical)
    {
        var capacity = SqlServerCapacity.Resolve(ExpressInfo(16, "16.0.4200.1"), 0);
        var settings = PercentOnlySettings(warning, critical);

        Assert.Equal("Normal", SqlServerCapacity.ResolveStatusLevel(7679, capacity, settings));
        Assert.Equal("Warning", SqlServerCapacity.ResolveStatusLevel(7680, capacity, settings));
        Assert.Equal("Critical", SqlServerCapacity.ResolveStatusLevel(8704, capacity, settings));
        Assert.True(SqlServerCapacity.HasInvalidThresholds(settings));
    }

    [Fact]
    public void ResolveStatusLevel_NoLimitAndNoAbsoluteThresholds_IsAlwaysNormal()
    {
        var info = new SqlServerEditionInfo("Standard Edition (64-bit)", Standard, 16, "16.0.4200.1", "RTM");
        var capacity = SqlServerCapacity.Resolve(info, configuredOverrideMb: 0);

        // No divide-by-zero, and nothing to be alarmed about.
        Assert.Equal(0, capacity.MaxLimitMb);
        Assert.Equal("Normal", SqlServerCapacity.ResolveStatusLevel(500000, capacity, PercentOnlySettings()));
    }

    [Fact]
    public void ResolveStatusLevel_NoEngineLimitButAbsoluteThresholdSet_StillFires()
    {
        var info = new SqlServerEditionInfo("Standard Edition (64-bit)", Standard, 16, "16.0.4200.1", "RTM");
        var capacity = SqlServerCapacity.Resolve(info, configuredOverrideMb: 0);
        var settings = PercentOnlySettings();
        settings.DatabaseCriticalThresholdMb = 100000;

        Assert.Equal("Critical", SqlServerCapacity.ResolveStatusLevel(120000, capacity, settings));
    }

    [Fact]
    public void HasInvalidThresholds_DefaultSettings_AreValid()
    {
        Assert.False(SqlServerCapacity.HasInvalidThresholds(new ChatRetentionSettings()));
    }

    [Fact]
    public void DefaultSettings_ReproduceTheHistoricThresholdsOn10Gb()
    {
        var settings = new ChatRetentionSettings();
        var capacity = SqlServerCapacity.Resolve(ExpressInfo(16, "16.0.4200.1"), settings.DatabaseMaxSizeMbOverride);

        Assert.Equal(10240, capacity.MaxLimitMb);
        Assert.Equal("Normal", SqlServerCapacity.ResolveStatusLevel(7679, capacity, settings));
        Assert.Equal("Warning", SqlServerCapacity.ResolveStatusLevel(7680, capacity, settings));
        Assert.Equal("Critical", SqlServerCapacity.ResolveStatusLevel(8704, capacity, settings));
    }
}
