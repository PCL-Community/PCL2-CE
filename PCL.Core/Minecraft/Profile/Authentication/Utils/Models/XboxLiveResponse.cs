using System;
using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Profile.Authentication.Utils.Models;

public record XboxLiveResponse
{
    public required DateTime IssueInstant { get; set; }
    public required DateTime NotAfter { get; set; }
    public required string Token { get; set; }
    [JsonPropertyName("DisplayClaims")] public XboxDisplayClaims? DisplayClaims { get; set; }
}
