using System;
using System.IO;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;

namespace IsWiX.WiXModel.Tests;

/// <summary>COM registration, security, custom tables, ODBC, signing and merge module configuration (batch 5).</summary>
public class ComAndDataTests
{
    private static WixDocument LoadCorpus(string name) => WixDocument.Load(Path.Combine(AppContext.BaseDirectory, "Corpus", name));

    [Fact]
    public void Com_registration_is_typed()
    {
        var component = LoadCorpus("com-registration.wxs").Root.Fragments.Single().ComponentGroups.Single().Components.Single();
        var typeLib = component.Files.Single().TypeLibs.Single();

        Assert.Equal((0, 1, 0), (typeLib.Language!.Value.Value, typeLib.MajorVersion!.Value.Value, typeLib.MinorVersion!.Value.Value));

        var appId = typeLib.AppIds.Single();
        Assert.False(appId.RunAsInteractiveUser!.Value.Value);

        var widget = appId.Classes.Single();
        Assert.Equal("InprocServer32 LocalServer32", widget.Context);
        Assert.Equal(ClassThreadingModel.Apartment, widget.ThreadingModel!.Value.Value);
        Assert.Equal(["Acme.Widget.1"], widget.ProgIds.Select(p => p.Id));
        Assert.Equal("Acme.Widget", widget.ProgIds.Single().ProgIds.Single().Id);
        Assert.Equal(("FFFF", "4D5A"), (widget.FileTypeMasks.Single().Mask, widget.FileTypeMasks.Single().Value));

        var widgetInterface = typeLib.Interfaces.Single();
        Assert.Equal("IWidget", widgetInterface.Name);
        Assert.Equal(7, widgetInterface.NumMethods!.Value.Value);

        var extension = component.ProgIds.Single().Extensions.Single();
        Assert.Equal("acme", extension.Id);
        Assert.Equal(("open", "\"%1\"", 1), (extension.Verbs.Single().Id, extension.Verbs.Single().Argument, extension.Verbs.Single().Sequence!.Value.Value));
        Assert.True(extension.MIMEs.Single().Default!.Value.Value);
        Assert.Equal("AcmeWidgets", component.Categories.Single().Qualifier);
    }

    [Fact]
    public void Permissions_registry_removal_and_file_extras_are_typed()
    {
        var component = Package().StandardDirectories.Single().Directories.Single().Components.First();
        var file = component.Files.Single();

        Assert.Equal("D:(A;;FA;;;BA)(A;;FR;;;BU)", file.PermissionExElements.Single().Sddl);
        Assert.Equal(("INSTALLFOLDER", "data.db.bak"), (file.CopyFiles.Single().DestinationDirectory, file.CopyFiles.Single().DestinationName));

        var permission = component.CreateFolders.Single().Permissions.Single();
        Assert.Equal("Users", permission.User);
        Assert.True(permission.GenericRead!.Value.Value);

        Assert.Equal(RemoveRegistryKeyAction.RemoveOnUninstall, component.RemoveRegistryKeys.Single().Action!.Value.Value);
        Assert.Equal("LastRun", component.RemoveRegistryValues.Single().Name);
        Assert.Equal(1048576, component.ReserveCosts.Single().RunLocal!.Value.Value);
        Assert.Equal("SharedRuntime", component.IsolateComponents.Single().Shared);

        var provides = component.ProvidesElements.Single();
        Assert.Equal("Acme.Data", provides.Key);
        Assert.Equal("Acme.Runtime", provides.RequiresElements.Single().ProviderKey);
    }

    [Fact]
    public void Custom_tables_are_typed()
    {
        var package = Package();
        var table = package.CustomTables.Single();

        var key = table.Columns.First();
        Assert.Equal(ColumnType.String, key.Type!.Value.Value);
        Assert.True(key.PrimaryKey!.Value.Value);
        Assert.Equal(72, key.Width!.Value.Value);
        Assert.Equal(ColumnCategory.Identifier, key.Category!.Value.Value);
        Assert.Equal(ColumnModularize.Property, table.Columns.Last().Modularize!.Value.Value);

        Assert.Equal([("Key", "Mode"), ("Value", "production")], table.Rows.Single().DataElements.Select(d => (d.Column, d.Value)));
        Assert.Equal("Region", package.CustomTableRefs.Single().Rows.Single().DataElements.Single().Value);
        Assert.Equal("LockPermissions", package.EnsureTables.Single().Id);
    }

    [Fact]
    public void Odbc_signing_compliance_and_package_references_are_typed()
    {
        var package = Package();
        var odbc = package.StandardDirectories.Single().Directories.Single().Components.Last();

        Assert.Equal("neutral", odbc.Files.Single().AssemblyNames.Single().Value);
        var driver = odbc.ODBCDrivers.Single();
        Assert.Equal(("Acme ODBC Driver", "AcmeOdbcDll"), (driver.Name, driver.File));
        Assert.Equal(ODBCDataSourceRegistration.Machine, driver.ODBCDataSources.Single().Registration!.Value.Value);
        Assert.Equal("Acme Translator", odbc.ODBCTranslators.Single().Name);

        Assert.Equal("MediaSigner", package.MediaElements.Single().DigitalSignatures.Single().DigitalCertificates.Single().Id);
        Assert.Equal("PackageSigner", package.PackageCertificatesElements.Single().DigitalCertificates.Single().Id);
        Assert.Equal("regid.2026-10.com.acme", package.SoftwareTags.Single().Regid);
        Assert.True(package.RequiresElements.Single().IncludeMinimum!.Value.Value);
        Assert.Equal("WixUI_InstallDir", package.UIRefs.Single().Id);
        Assert.Equal("\\\\symbols\\acme", package.SymbolPaths.Single().Path);
        Assert.Equal("LegacyAcme", package.ComplianceChecks.Single().DirectorySearches.Single().Id);
        Assert.Equal("DataFile", package.SFPCatalogs.Single().SFPFiles.Single().Id);

        var level = package.Features.Single().Levels.Single();
        Assert.Equal((0, "NOT VersionNT64"), (level.Value!.Value.Value, level.Condition));
        Assert.Equal("production", package.StandardDirectories.Single().Directories.Single().Merges.Single().ConfigurationDataElements.Single().Value);
    }

    [Fact]
    public void Merge_module_configuration_is_typed()
    {
        var module = LoadCorpus("merge-module-configuration.wxs").Root.Modules.Single();

        var configuration = module.Configurations.Single();
        Assert.Equal(("TargetMode", "test"), (configuration.Name, configuration.DefaultValue));
        Assert.Equal(ConfigurationFormat.Text, configuration.Format!.Value.Value);
        Assert.Equal(("Property", "ACMEMODE", "Value", "[=TargetMode]"), (module.Substitutions.Single().Table, module.Substitutions.Single().Row, module.Substitutions.Single().Column, module.Substitutions.Single().Value));
        Assert.Equal(1033, module.Dependencies.Single().RequiredLanguage!.Value.Value);
        Assert.Equal(1033, module.Exclusions.Single().ExcludeLanguage!.Value.Value);
        Assert.Equal("Shortcut", module.IgnoreTables.Single().Id);
        Assert.DoesNotContain(module.Children, e => e is WixUnknownElement);
    }

    [Fact]
    public void Every_element_in_the_new_fixtures_is_typed()
    {
        foreach (var name in new[] { "com-registration.wxs", "security-data-and-tables.wxs", "merge-module-configuration.wxs" })
        {
            var unknown = LoadCorpus(name).Xml.Root!.Descendants().Select(WixElement.Wrap).OfType<WixUnknownElement>().Select(e => e.Xml.Name.LocalName);
            Assert.Empty(unknown);
        }
    }

    private static Package Package() => LoadCorpus("security-data-and-tables.wxs").Root.Packages.Single();
}
