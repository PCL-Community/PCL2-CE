using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.IdentityModel.Yggdrasil;


public record Profile
{
    /// <summary>
    /// UUID
    /// </summary>
    [JsonPropertyName("id")] public required string Id { get; init; }
    /// <summary>
    /// 档案名称
    /// </summary>
    [JsonPropertyName("name")] public required string Name { get; init; }
    /// <summary>
    /// 属性信息
    /// </summary>
    [JsonPropertyName("properties")] public PlayerProperty[]? Properties { get; init; }
}

public record YggdrasilLegacyUser
{
    /// <summary>
    /// UUID
    /// </summary>
    [JsonPropertyName("id")] public required string Id { get; init; }
    /// <summary>
    /// 属性信息
    /// </summary>
    [JsonPropertyName("properties")] public PlayerProperty[]? Properties { get; init; }
}

public record PlayerProperty
{
    /// <summary>
    /// 属性名称
    /// </summary>
    [JsonPropertyName("name")] public required string Name { get; init; }
    /// <summary>
    /// 属性值
    /// </summary>
    [JsonPropertyName("value")] public required string Value { get; init; }
    /// <summary>
    /// 数字签名
    /// </summary>
    [JsonPropertyName("signature")] public string? Signature { get; init; }
}

public record PlayerTextureProperty
{
    /// <summary>
    /// Unix 时间戳
    /// </summary>
    [JsonPropertyName("timestamp")] public required long Timestamp { get; init; }
    /// <summary>
    /// 所有者的 UUID
    /// </summary>
    [JsonPropertyName("profileId")] public required string ProfileId { get; init; }
    /// <summary>
    /// 所有者名称
    /// </summary>
    [JsonPropertyName("profileName")] public required string ProfileName { get; init; }
    /// <summary>
    /// 材质信息
    /// </summary>
    [JsonPropertyName("textures")] public required PlayerTextures Textures { get; init; }
}

public record PlayerTextures
{
    /// <summary>
    /// 皮肤
    /// </summary>
    [JsonPropertyName("skin")] public PlayerTexture? Skin { get; init; }
    /// <summary>
    /// 披风
    /// </summary>
    [JsonPropertyName("cape")] public PlayerTexture? Cape { get; init; }
}

public record PlayerTexture
{
    /// <summary>
    /// 材质地址
    /// </summary>
    [JsonPropertyName("url")] public required string Url { get; init; }
    /// <summary>
    /// 元数据
    /// </summary>
    [JsonPropertyName("metadata")] public PlayerTextureMetadata? Metadata { get; init; }
}

public record PlayerTextureMetadata
{
    /// <summary>
    /// 模型信息 （e.g. Steven -> default, Alex -> Slim）
    /// </summary>
    [JsonPropertyName("model")] public string? Model { get; init; }
}
