using System.Windows;
using PCL.Core.Utils.Validate;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Models;

namespace PCL;

public partial class PageLoginOffline
{
    public PageLoginOffline()
    {
        // Handles
        InitializeComponent();
        BtnBack.Click += BtnBack_Click;
        RadioUuidCustom.Check += RadioUuid_Checked;
        RadioUuidStandard.Check += RadioUuid_Checked;
        RadioUuidLegacy.Check += RadioUuid_Checked;
        BtnLogin.Click += BtnLogin_Click;
    }

    private void BtnBack_Click(object sender, EventArgs e)
    {
        ProfileService.IsCreatingProfile = false;
        ModBase.RunInUi(() => ModMain.frmLaunchLeft.RefreshPage(true));
    }

    private void RadioUuid_Checked(object sender, ModBase.RouteEventArgs e)
    {
        if (RadioUuidCustom.Checked)
        {
            TextUuidTitle.Visibility = Visibility.Visible;
            TextUuid.Visibility = Visibility.Visible;
        }
        else
        {
            TextUuidTitle.Visibility = Visibility.Collapsed;
            TextUuid.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnLogin_Click(object sender, EventArgs e)
    {
        // 玩家 ID 输入检查
        var username = TextName.Text;
        var usernameValidateResult = new RegexValidator("^[A-z0-9_]{3,16}$").Validate(username);
        if (!usernameValidateResult.IsValid)
                if (ModMain.MyMsgBox(
                        Lang.Text("Launch.Account.Offline.InvalidPlayerId.Message"),
                        Lang.Text("Launch.Account.Offline.InvalidPlayerId.Title"), Lang.Text("Common.Action.Continue"), Lang.Text("Common.Action.Cancel"), isWarn: true, forceWait: true) == 2)
                return;
        // UUID
        string userUuid = null;
        if (RadioUuidCustom.Checked)
        {
            // 自定义输入检查
            var uuidInput = TextUuid.Text.Replace("-", "");
            var uuidValidateResult = new RegexValidator("^[a-fA-F0-9]{32}$").Validate(uuidInput);
            if (RadioUuidCustom.Checked && !uuidValidateResult.IsValid)
            {
                HintService.Hint(Lang.Text("Launch.Account.Offline.InvalidUuid", uuidValidateResult), HintType.Error);
                return;
            }

            userUuid = uuidInput;
        }
        else if (RadioUuidLegacy.Checked)
        {
            userUuid = ProfileUi.GetOfflineUuid(username, isLegacy: true);
        }
        else
        {
            userUuid = ProfileUi.GetOfflineUuid(username);
        }

        // 创建档案
        var newProfile = new McProfile
        {
            ProfileType = ProfileType.Offline,
            Uuid = userUuid,
            UserName = username,
            Description = "",
            AccessToken = userUuid,
            ClientToken = userUuid,
            ProfileId = Guid.NewGuid().ToString("N")
        };
        ProfileService.Add(newProfile);
        ProfileService.IsCreatingProfile = false;
        HintService.Hint(Lang.Text("Launch.Account.Profile.Created"), HintType.Success);
        ModBase.RunInUi(() => ModMain.frmLaunchLeft.RefreshPage(true));
    }
}
