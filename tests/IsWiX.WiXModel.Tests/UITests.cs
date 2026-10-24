using System;
using System.IO;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;

namespace IsWiX.WiXModel.Tests;

/// <summary>Dialogs, controls, events, text and embedded UI, plus the remaining MSI elements.</summary>
public class UITests
{
    private static WixDocument LoadCorpus(string name) => WixDocument.Load(Path.Combine(AppContext.BaseDirectory, "Corpus", name));

    private static UI LoadUI() => LoadCorpus("custom-ui.wxs").Root.Fragments.Single().UIs.Single();

    [Fact]
    public void Ui_resources_are_typed()
    {
        var ui = LoadUI();

        var style = ui.TextStyles.Single();
        Assert.Equal(("Tahoma", "12"), (style.FaceName, style.Size));
        Assert.Equal(128, style.Blue!.Value.Value);
        Assert.True(style.Bold!.Value.Value);
        Assert.Equal("bytes", ui.UITexts.Single().Value);
        Assert.Equal("Service: [1]", ui.ProgressTexts.Single().Template);
        Assert.Equal(25001, ui.Errors.Single().Id!.Value.Value);
    }

    [Fact]
    public void Dialog_and_controls_are_typed()
    {
        var dialog = LoadUI().Dialogs.Single();
        Assert.Equal((370, 270), (dialog.Width!.Value.Value, dialog.Height!.Value.Value));
        Assert.True(dialog.NoMinimize!.Value.Value);

        var controls = dialog.Controls.ToDictionary(c => c.Id!);
        Assert.Equal(["Title", "Mode", "Scope", "Logo", "Progress", "Next"], dialog.Controls.Select(c => c.Id));
        Assert.Equal(("Text", "{\\AcmeTitle}Settings"), (controls["Title"].Type, controls["Title"].Text));
        Assert.True(controls["Title"].Transparent!.Value.Value);

        Assert.Equal(["Test", "Production"], controls["Mode"].ComboBoxes.Single().ListItems.Select(i => i.Text));
        Assert.Equal(["machine", "user"], controls["Scope"].RadioButtonGroups.Single().RadioButtons.Select(r => r.Value));
        Assert.Equal(20, controls["Scope"].RadioButtonGroups.Single().RadioButtons.Last().Y!.Value.Value);
        Assert.Equal(ControlIconSize.Size32, controls["Logo"].IconSize!.Value.Value);
        Assert.Equal(("SetProgress", "Progress"), (controls["Progress"].SubscribeElements.Single().Event, controls["Progress"].SubscribeElements.Single().Attribute));

        var next = controls["Next"];
        Assert.True(next.Default!.Value.Value);
        Assert.Equal(
            [("SpawnDialog", "InvalidModeDlg", "NOT ACMEMODE"), ("NewDialog", "VerifyReadyDlg", "ACMEMODE")],
            next.PublishElements.Select(p => (p.Event, p.Value, p.Condition)));
    }

    [Fact]
    public void Ui_level_events_lists_billboards_and_sequences_are_typed()
    {
        var ui = LoadUI();

        var publish = ui.PublishElements.Single();
        Assert.Equal(("InstallDirDlg", "Next", "AcmeSettingsDlg", "5"), (publish.Dialog, publish.Control, publish.Value, publish.Order));
        Assert.Equal("VerifyReadyDlg", ui.DialogRefs.Single().Id);
        Assert.Equal("docs", ui.ListBoxes.Single().ListItems.Single().Value);
        Assert.Equal("GridIcon", ui.ListViews.Single().ListItems.Single().Icon);

        var billboard = ui.BillboardActions.Single().Billboards.Single();
        Assert.Equal(("Welcome", "Main"), (billboard.Id, billboard.Feature));
        Assert.Equal("Thanks", billboard.Controls.Single().Id);

        var embedded = ui.EmbeddedUIs.Single();
        Assert.False(embedded.IgnoreFatalExit!.Value.Value);
        Assert.Equal("strings.xml", embedded.EmbeddedUIResources.Single().Name);

        var show = ui.InstallUISequences.Single().ShowElements.Single();
        Assert.Equal(("AcmeSettingsDlg", "CostFinalize", "NOT Installed"), (show.Dialog, show.After, show.Condition));
    }

    [Fact]
    public void Adding_a_control_with_events_matches_the_dialog_layout()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "custom-ui.wxs");
        var document = WixDocument.Load(path);
        var dialog = document.Root.Fragments.Single().UIs.Single().Dialogs.Single();

        var cancel = new Control { Id = "Cancel", Type = "PushButton", X = 304, Y = 243, Width = 56, Height = 17, Cancel = true, Text = "!(loc.WixUICancel)" };
        cancel.Add(new Publish { Event = "SpawnDialog", Value = "CancelDlg" });
        dialog.Add(cancel);

        var expected = System.IO.File.ReadAllText(path).Replace(
            "        </Control>\n      </Dialog>",
            "        </Control>\n" +
            "        <Control Id=\"Cancel\" Type=\"PushButton\" X=\"304\" Y=\"243\" Width=\"56\" Height=\"17\" Cancel=\"yes\" Text=\"!(loc.WixUICancel)\">\n" +
            "          <Publish Event=\"SpawnDialog\" Value=\"CancelDlg\" />\n" +
            "        </Control>\n" +
            "      </Dialog>",
            StringComparison.Ordinal);
        Assert.Equal(expected, document.ToString());
    }

    [Fact]
    public void Embedded_chainer_and_instance_transforms_are_typed()
    {
        var package = LoadCorpus("chainer-and-instances.wxs").Root.Packages.Single();

        var chainer = package.EmbeddedChainers.Single();
        Assert.Equal(("AcmeChainerBinary", "/quiet", "NOT Installed"), (chainer.BinarySource, chainer.CommandLine, chainer.Condition));
        Assert.Equal("SharedChainer", package.EmbeddedChainerRefs.Single().Id);

        var transforms = package.InstanceTransformsElements.Single();
        Assert.Equal("ACMEINSTANCE", transforms.Property);
        Assert.Equal([("Blue", "*"), ("Green", "4C3B2A19-0807-4655-A443-3322110FFEDD")], transforms.Instances.Select(i => (i.Id, i.ProductCode)));
    }

    [Fact]
    public void Every_element_in_the_new_fixtures_is_typed()
    {
        foreach (var name in new[] { "custom-ui.wxs", "chainer-and-instances.wxs" })
        {
            Assert.Empty(LoadCorpus(name).Xml.Root!.Descendants().Select(WixElement.Wrap).OfType<WixUnknownElement>());
        }
    }
}
