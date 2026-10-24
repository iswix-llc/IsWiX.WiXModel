using System;
using System.IO;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>Fragments, directory references, component groups, features and wildcard files (batch 1).</summary>
public class StructureTests
{
    private static WixDocument LoadCorpus(string name) => WixDocument.Load(Path.Combine(AppContext.BaseDirectory, "Corpus", name));

    [Fact]
    public void IsWiX_component_group_is_typed_end_to_end()
    {
        var fragment = LoadCorpus("iswix-component-group.wxs").Root.Fragments.Single();

        var standardDirectory = fragment.StandardDirectories.Single();
        Assert.Equal(StandardDirectoryType.ProgramFiles6432Folder, standardDirectory.Id!.Value.Value);
        Assert.Equal("Acme App", standardDirectory.Directories.Single().Name);

        var group = fragment.ComponentGroups.Single();
        Assert.Equal("INSTALLLOCATION", group.Directory);
        Assert.Equal(["owf1A2B3C4D5E6F", "owd9F8E7D6C5B4A"], group.Components.Select(c => c.Id));
        Assert.Equal("SharedRuntime", group.ComponentRefs.Single().Id);
        Assert.Equal("Plugins", group.ComponentGroupRefs.Single().Id);

        var folder = group.Components.Last();
        Assert.Equal("Data", folder.Subdirectory);
        Assert.Single(folder.CreateFolders);
        Assert.Equal(InstallUninstallType.Uninstall, folder.RemoveFolders.Single().On!.Value.Value);
        Assert.Equal(InstallUninstallType.Both, folder.RemoveFiles.Single().On!.Value.Value);
        Assert.Equal("*.log", folder.RemoveFiles.Single().Name);
        Assert.DoesNotContain(fragment.Children.Concat(group.Children), e => e is WixUnknownElement);
    }

    [Fact]
    public void Feature_tree_and_wildcard_files_are_typed()
    {
        var document = LoadCorpus("features-and-files.wxs");
        var package = document.Root.Packages.Single();

        var complete = package.Features.Single();
        Assert.Equal("expand", complete.Display);
        Assert.Equal(FeatureInstallDefault.Local, complete.InstallDefault!.Value.Value);
        Assert.Equal("Product", complete.ComponentGroupRefs.Single().Id);
        Assert.False(complete.FeatureRefs.Single().IgnoreParent!.Value.Value);
        Assert.Equal("Optional", complete.FeatureGroupRefs.Single().Id);

        var docs = complete.Features.Single();
        Assert.Equal(2, docs.Level!.Value.Value);
        Assert.True(docs.AllowAbsent!.Value.Value);

        var files = docs.FilesElements.Single();
        Assert.Equal("docs\\**", files.Include);
        Assert.Equal(["docs\\**\\*.tmp", "docs\\drafts\\**"], files.Excludes.Select(e => e.Files));

        Assert.Equal("readme.txt", package.DirectoryRefs.Single().Components.Single().Files.Single().Source);

        var samples = document.Root.Fragments.Single().FeatureGroups.Single().Features.Single();
        Assert.Equal(FeatureTypicalDefault.Install, samples.TypicalDefault!.Value.Value);
        Assert.Equal(1000, samples.Level!.Value.Value);
    }

    [Fact]
    public void File_and_Files_children_have_distinct_accessors()
    {
        Assert.Equal(typeof(System.Collections.Generic.IEnumerable<File>), typeof(ComponentGroup).GetProperty("Files")!.PropertyType);
        Assert.Equal(typeof(System.Collections.Generic.IEnumerable<Files>), typeof(ComponentGroup).GetProperty("FilesElements")!.PropertyType);
    }

    [Fact]
    public void Building_a_component_group_fragment_in_code()
    {
        var document = WixDocument.Create();
        var fragment = document.Root.Add(new Fragment());
        fragment.Add(new StandardDirectory { Id = StandardDirectoryType.ProgramFiles6432Folder })
            .Add(new Directory { Id = "INSTALLLOCATION", Name = "Acme App" });
        var group = fragment.Add(new ComponentGroup { Id = "Product", Directory = "INSTALLLOCATION" });
        group.Add(new Component()).Add(new File { Source = "App.exe" });
        var files = group.Add(new Files { Include = "plugins\\**" });
        files.Add(new Exclude { Files = "plugins\\**\\*.pdb" });

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\r\n" +
            "    <Fragment>\r\n" +
            "        <StandardDirectory Id=\"ProgramFiles6432Folder\">\r\n" +
            "            <Directory Id=\"INSTALLLOCATION\" Name=\"Acme App\" />\r\n" +
            "        </StandardDirectory>\r\n" +
            "        <ComponentGroup Id=\"Product\" Directory=\"INSTALLLOCATION\">\r\n" +
            "            <Component>\r\n" +
            "                <File Source=\"App.exe\" />\r\n" +
            "            </Component>\r\n" +
            "            <Files Include=\"plugins\\**\">\r\n" +
            "                <Exclude Files=\"plugins\\**\\*.pdb\" />\r\n" +
            "            </Files>\r\n" +
            "        </ComponentGroup>\r\n" +
            "    </Fragment>\r\n" +
            "</Wix>\r\n",
            document.ToString());
    }

    [Fact]
    public void Adding_a_component_to_an_existing_group_keeps_the_rest_of_the_file()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "iswix-component-group.wxs");
        var document = WixDocument.Load(path);
        var group = document.Root.Fragments.Single().ComponentGroups.Single();

        group.Add(new Component { Id = "owfNEW", Guid = "3F2504E0-4F89-11D3-9A0C-0305E82C3303" })
            .Add(new File { Id = "owfNEW", Source = "$(var.SourceDir)\\New.dll", KeyPath = true });

        var expected = Lf(System.IO.File.ReadAllText(path)).Replace(
            "      </Component>\n      <ComponentRef",
            "      </Component>\n" +
            "      <Component Id=\"owfNEW\" Guid=\"3F2504E0-4F89-11D3-9A0C-0305E82C3303\">\n" +
            "        <File Id=\"owfNEW\" Source=\"$(var.SourceDir)\\New.dll\" KeyPath=\"yes\" />\n" +
            "      </Component>\n" +
            "      <ComponentRef",
            StringComparison.Ordinal);
        Assert.Equal(expected, document.ToString());
    }
}
