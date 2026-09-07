using System.Collections.Generic;

namespace PCL.Core.Minecraft.Profile.Authentication.Utils.Models;

public record XSTSProperty
{
    public string SandboxId { get; set; } = "RETAIL";
    public List<string> UserTokens { get; set; } = [];
}