using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PCL.Core.Minecraft.Profile.Models;
using PCL.Core.Utils;

namespace PCL.Core.Minecraft.Profile;

public sealed class ProfileManagement<TProfileModel>
    where TProfileModel : SafeProfile
{
    private readonly object _syncLock = new();
    private ProfileJson<TProfileModel> _profiles = new();
    private TProfileModel? _current;

    public bool IsLoaded { get; private set; }
    public int LastUsed { get { lock (_syncLock) return _profiles.LastUsed; } }
    public TProfileModel? Current { get { lock (_syncLock) return _current; } }

    public IReadOnlyList<TProfileModel> GetAll()
    {
        lock (_syncLock)
            return _profiles.Profiles.ToArray();
    }

    public void Add(TProfileModel profile, bool select = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_syncLock)
        {
            _EnsureLoaded();
            if (!string.IsNullOrWhiteSpace(profile.ProfileId) &&
                _profiles.Profiles.Any(p => p.ProfileId == profile.ProfileId))
                throw new InvalidOperationException($"A profile with id '{profile.ProfileId}' already exists.");
            _profiles.Profiles.Add(profile);
            if (select) _SelectUnsafe(profile);
        }
    }

    public void Delete(TProfileModel profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_syncLock)
        {
            _EnsureLoaded();
            var index = _profiles.Profiles.IndexOf(profile);
            if (index < 0 && !string.IsNullOrWhiteSpace(profile.ProfileId))
                index = _profiles.Profiles.FindIndex(p => p.ProfileId == profile.ProfileId);
            if (index < 0) return;
            var removed = _profiles.Profiles[index];
            var wasCurrent = ReferenceEquals(_current, removed) ||
                             (!string.IsNullOrWhiteSpace(removed.ProfileId) && _current?.ProfileId == removed.ProfileId);
            _profiles.Profiles.RemoveAt(index);
            if (wasCurrent)
            {
                _current = null;
                _profiles.LastUsed = -1;
            }
            else if (index < _profiles.LastUsed)
            {
                _profiles.LastUsed--;
            }
            _NormalizeLastUsedUnsafe();
        }
    }

    public void Update(TProfileModel origin, TProfileModel current)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(current);
        lock (_syncLock)
        {
            _EnsureLoaded();
            var index = _profiles.Profiles.IndexOf(origin);
            if (index < 0 && !string.IsNullOrWhiteSpace(origin.ProfileId))
                index = _profiles.Profiles.FindIndex(p => p.ProfileId == origin.ProfileId);
            if (index < 0) throw new KeyNotFoundException("The source profile does not exist.");
            if (string.IsNullOrWhiteSpace(current.ProfileId)) current.ProfileId = origin.ProfileId;
            if (!string.IsNullOrWhiteSpace(current.ProfileId) &&
                _profiles.Profiles.Where((_, profileIndex) => profileIndex != index)
                    .Any(profile => profile.ProfileId == current.ProfileId))
                throw new InvalidOperationException($"A profile with id '{current.ProfileId}' already exists.");
            _profiles.Profiles[index] = current;
            if (ReferenceEquals(_current, origin) || _current?.ProfileId == origin.ProfileId)
                _current = current;
        }
    }

    public void Select(TProfileModel? profile)
    {
        lock (_syncLock)
        {
            _EnsureLoaded();
            if (profile is null)
            {
                _current = null;
                _profiles.LastUsed = -1;
                return;
            }
            var index = _profiles.Profiles.IndexOf(profile);
            if (index < 0 && !string.IsNullOrWhiteSpace(profile.ProfileId))
                index = _profiles.Profiles.FindIndex(p => p.ProfileId == profile.ProfileId);
            if (index < 0) throw new KeyNotFoundException("The selected profile does not exist.");
            _SelectUnsafe(_profiles.Profiles[index]);
        }
    }

    public void SelectAt(int index)
    {
        lock (_syncLock)
        {
            _EnsureLoaded();
            if (index < 0 || index >= _profiles.Profiles.Count)
            {
                _current = null;
                _profiles.LastUsed = -1;
                return;
            }
            _SelectUnsafe(_profiles.Profiles[index]);
        }
    }

    public void LoadFromString(string profiles)
    {
        lock (_syncLock)
        {
            var parsed = string.IsNullOrWhiteSpace(profiles)
                ? new ProfileJson<TProfileModel>()
                : JsonSerializer.Deserialize<ProfileJson<TProfileModel>>(profiles, JsonCompat.SerializerOptions)
                  ?? new ProfileJson<TProfileModel>();
            parsed.Profiles ??= [];
            var duplicateIds = parsed.Profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.ProfileId))
                .GroupBy(profile => profile.ProfileId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateIds is not null)
                throw new InvalidDataException($"Duplicate profile id: {duplicateIds.Key}");
            _profiles = parsed;
            IsLoaded = true;
            _NormalizeLastUsedUnsafe();
            _current = _profiles.LastUsed >= 0 ? _profiles.Profiles[_profiles.LastUsed] : null;
        }
    }

    public string Serialize()
    {
        lock (_syncLock)
        {
            _EnsureLoaded();
            return JsonSerializer.Serialize(_profiles, JsonCompat.SerializerOptions);
        }
    }

    public void Clear()
    {
        lock (_syncLock)
        {
            _EnsureLoaded();
            _profiles = new ProfileJson<TProfileModel>();
            _current = null;
        }
    }

    private void _SelectUnsafe(TProfileModel profile)
    {
        _current = profile;
        _profiles.LastUsed = _profiles.Profiles.IndexOf(profile);
    }

    private void _NormalizeLastUsedUnsafe()
    {
        if (_profiles.Profiles.Count == 0)
        {
            _profiles.LastUsed = -1;
            return;
        }
        if (_profiles.LastUsed < 0 || _profiles.LastUsed >= _profiles.Profiles.Count)
            _profiles.LastUsed = -1;
    }

    private void _EnsureLoaded()
    {
        if (!IsLoaded) throw new InvalidOperationException("The profile list has not been loaded.");
    }
}
