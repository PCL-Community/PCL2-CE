using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Models;

namespace PCL.Core.Test.Minecraft;

[TestClass]
public sealed class ProfileManagementTest
{
    [TestMethod]
    public void LoadSerializeRoundTripPreservesProfileData()
    {
        var profile = new McProfile
        {
            ProfileId = "profile-1",
            ProfileType = ProfileType.Authlib,
            UserName = "Player",
            Uuid = "0123456789abcdef0123456789abcdef",
            AccessToken = "access",
            RefreshToken = "refresh",
            ClientToken = "client",
            Server = "https://example.test/api/yggdrasil/authserver",
            ServerName = "Example",
            LoginName = "login",
            Password = "password",
            Description = "description",
            RawJson = "{\"profile\":true}"
        };
        var source = new ProfileManagement<McProfile>();
        source.LoadFromString("{\"lastUsed\":0,\"profiles\":[" +
            System.Text.Json.JsonSerializer.Serialize(profile) + "]}");

        var json = source.Serialize();
        var restored = new ProfileManagement<McProfile>();
        restored.LoadFromString(json);
        var actual = restored.GetAll().Single();

        Assert.AreEqual("profile-1", actual.ProfileId);
        Assert.AreEqual(ProfileType.Authlib, actual.ProfileType);
        Assert.AreEqual("Player", actual.UserName);
        Assert.AreEqual("refresh", actual.RefreshToken);
        Assert.AreEqual("https://example.test/api/yggdrasil/authserver", actual.Server);
        Assert.AreEqual("password", actual.Password);
        Assert.AreSame(restored.Current, actual);
        Assert.AreEqual(0, restored.LastUsed);
    }

    [TestMethod]
    public void SelectionAndUpdateUseStableProfileIdentity()
    {
        var first = new McProfile { ProfileId = "first", UserName = "First", Uuid = "1" };
        var second = new McProfile { ProfileId = "second", UserName = "Second", Uuid = "2" };
        var management = new ProfileManagement<McProfile>();
        management.LoadFromString("{\"lastUsed\":-1,\"profiles\":[]}");
        management.Add(first);
        management.Add(second);
        management.Select(second);

        var updated = second.Clone();
        updated.UserName = "Renamed";
        management.Update(second, updated);

        Assert.AreSame(updated, management.Current);
        Assert.AreEqual("Renamed", management.GetAll()[1].UserName);
        Assert.AreEqual(1, management.LastUsed);
    }

    [TestMethod]
    public void DeleteSelectedProfileClearsSelectionAndLastUsed()
    {
        var profile = new McProfile { ProfileId = "profile", UserName = "Player", Uuid = "uuid" };
        var management = new ProfileManagement<McProfile>();
        management.LoadFromString("{\"lastUsed\":-1,\"profiles\":[]}");
        management.Add(profile, select: true);
        management.Delete(profile);

        Assert.IsNull(management.Current);
        Assert.AreEqual(-1, management.LastUsed);
        Assert.AreEqual(0, management.GetAll().Count);
    }

    [TestMethod]
    public void DeleteProfileBeforeSelectionKeepsSelectedProfile()
    {
        var first = new McProfile { ProfileId = "first", UserName = "First", Uuid = "1" };
        var selected = new McProfile { ProfileId = "selected", UserName = "Selected", Uuid = "2" };
        var management = new ProfileManagement<McProfile>();
        management.LoadFromString("{\"lastUsed\":-1,\"profiles\":[]}");
        management.Add(first);
        management.Add(selected, select: true);

        management.Delete(first);

        Assert.AreSame(selected, management.Current);
        Assert.AreEqual(0, management.LastUsed);
    }

    [TestMethod]
    public void UpdateRejectsDuplicateProfileIdentity()
    {
        var first = new McProfile { ProfileId = "first", UserName = "First", Uuid = "1" };
        var second = new McProfile { ProfileId = "second", UserName = "Second", Uuid = "2" };
        var management = new ProfileManagement<McProfile>();
        management.LoadFromString("{\"lastUsed\":-1,\"profiles\":[]}");
        management.Add(first);
        management.Add(second);

        var replacement = first.Clone();
        replacement.ProfileId = second.ProfileId;

        Assert.ThrowsExactly<InvalidOperationException>(() => management.Update(first, replacement));
    }
}
