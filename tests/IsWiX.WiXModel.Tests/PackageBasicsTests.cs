using System;
using System.IO;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;

namespace IsWiX.WiXModel.Tests;

/// <summary>Upgrades, media, properties, package resources and merge modules (batch 2).</summary>
public class PackageBasicsTests
{
    private static WixDocument LoadCorpus(string name) => WixDocument.Load(Path.Combine(AppContext.BaseDirectory, "Corpus", name));

    [Fact]
    public void Upgrade_and_media_elements_are_typed()
    {
        var package = LoadCorpus("package-basics.wxs").Root.Packages.Single();

        var summary = package.SummaryInformationElements.Single();
        Assert.Equal("Acme Basics installer", summary.Description);

        var majorUpgrade = package.MajorUpgrades.Single();
        Assert.Equal(MajorUpgradeSchedule.AfterInstallInitialize, majorUpgrade.Schedule!.Value.Value);
        Assert.True(majorUpgrade.AllowSameVersionUpgrades!.Value.Value);
        Assert.Equal("!(loc.NewerVersionInstalled)", majorUpgrade.DowngradeErrorMessage);

        var upgrade = package.Upgrades.Single();
        var version = upgrade.UpgradeVersions.Single();
        Assert.Equal(("1.0.0", "2.0.0", "LEGACYFOUND"), (version.Minimum, version.Maximum, version.Property));
        Assert.False(version.IncludeMaximum!.Value.Value);
        Assert.True(version.OnlyDetect!.Value.Value);
        Assert.True(upgrade.Properties.Single().Secure!.Value.Value);

        var media = package.MediaElements.Single();
        Assert.Equal(1, media.Id!.Value.Value);
        Assert.Equal(CompressionLevelType.High, media.CompressionLevel!.Value.Value);
        Assert.True(media.EmbedCab!.Value.Value);
    }

    [Fact]
    public void Properties_and_custom_values_are_typed()
    {
        var package = LoadCorpus("package-basics.wxs").Root.Packages.Single();

        Assert.Equal(["ARPNOMODIFY", "INSTALLFOLDER_FROM_REGISTRY"], package.Properties.Select(p => p.Id));
        Assert.Equal("1", package.Properties.First().Value);

        var search = package.Properties.Last().RegistrySearches.Single();
        Assert.Equal(RegistrySearchRoot.HKLM, search.Root!.Value.Value);
        Assert.Equal(RegistrySearchType.Raw, search.Type!.Value.Value);
        Assert.Equal("InstallDir", search.Name);

        Assert.Equal("WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED", package.PropertyRefs.Single().Id);

        var setProperty = package.SetProperties.Single();
        Assert.Equal(("ARPINSTALLLOCATION", "[INSTALLFOLDER]", "CostFinalize"), (setProperty.Id, setProperty.Value, setProperty.After));
        Assert.Equal(SequenceType.Execute, setProperty.Sequence!.Value.Value);
        Assert.Equal(SequenceType.Both, package.SetDirectories.Single().Sequence!.Value.Value);

        var launch = package.Launches.Single();
        Assert.Equal("Installed OR VersionNT64", launch.Condition);

        Assert.Equal("Assets\\App.ico", package.Icons.Single().SourceFile);
        Assert.Equal("CustomActions", package.Binaries.Single().Id);
        Assert.True(package.WixVariables.Single().Overridable!.Value.Value);
    }

    [Fact]
    public void Merge_and_MergeRef_are_typed()
    {
        var package = LoadCorpus("package-basics.wxs").Root.Packages.Single();

        var merge = package.StandardDirectories.Single().Directories.Single().Merges.Single();
        Assert.Equal("VCRedist", merge.Id);
        Assert.Equal(0, merge.Language!.Value.Value);
        Assert.Equal(1, merge.DiskId!.Value.Value);

        var mergeRef = package.Features.Single().MergeRefs.Single();
        Assert.Equal("VCRedist", mergeRef.Id);
        Assert.True(mergeRef.Primary!.Value.Value);
    }

    [Fact]
    public void Merge_module_is_typed()
    {
        var module = LoadCorpus("merge-module.wxs").Root.Modules.Single();

        Assert.Equal(("AcmeRuntime", "1.2.3.0"), (module.Id, module.Version));
        Assert.Equal(1033, module.Language!.Value.Value);
        Assert.Equal("Acme", module.SummaryInformationElements.Single().Manufacturer);
        Assert.Equal("[INSTALLDIR]", module.Properties.Single().Value);
        Assert.Equal(
            "AcmeRuntime.dll",
            module.StandardDirectories.Single().Directories.Single().Components.Single().Files.Single().Source);
        Assert.DoesNotContain(module.Children, e => e is WixUnknownElement);
    }

    [Fact]
    public void Adding_a_property_goes_after_the_existing_properties()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "package-basics.wxs");
        var document = WixDocument.Load(path);
        var package = document.Root.Packages.Single();

        package.Add(new Property { Id = "ARPNOREPAIR", Value = "yes" });

        var expected = System.IO.File.ReadAllText(path).Replace(
            "      <RegistrySearch Root=\"HKLM\" Key=\"Software\\Acme\\Basics\" Name=\"InstallDir\" Type=\"raw\" />\n    </Property>\n",
            "      <RegistrySearch Root=\"HKLM\" Key=\"Software\\Acme\\Basics\" Name=\"InstallDir\" Type=\"raw\" />\n    </Property>\n" +
            "    <Property Id=\"ARPNOREPAIR\" Value=\"yes\" />\n",
            StringComparison.Ordinal);
        Assert.Equal(expected, document.ToString());
    }
}
