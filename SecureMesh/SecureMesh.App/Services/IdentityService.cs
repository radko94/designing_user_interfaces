using System.Text.Json;
using SecureMesh.Core;
using Microsoft.Maui.Storage;

namespace SecureMesh.App.Services;

/// <summary>
/// Owns the local user's long-term <see cref="IdentityKeyPair"/> and nickname,
/// persisting them to a JSON file in the app data directory. The identity is
/// seeded (generated) on first access so the domain services can always be
/// constructed; the nickname completes "first launch" setup.
/// </summary>
public sealed class IdentityService
{
    private const string FileName = "identity.json";

    private readonly string _path;
    private IdentityKeyPair? _identity;
    private string _nickname = "";

    public IdentityService()
    {
        _path = Path.Combine(FileSystem.AppDataDirectory, FileName);
    }

    /// <summary>True once the user has supplied a nickname (setup complete).</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>The user's chosen display name (empty before first launch setup).</summary>
    public string Nickname => _nickname;

    /// <summary>The local identity, loading or creating + persisting it on first access.</summary>
    public IdentityKeyPair Identity
    {
        get
        {
            EnsureSeeded();
            return _identity!;
        }
    }

    /// <summary>
    /// Loads the persisted identity, or generates and persists a fresh one when
    /// none exists (or the file is corrupt). This is the "seed on startup" step.
    /// </summary>
    public void EnsureSeeded()
    {
        if (_identity is not null)
            return;

        if (File.Exists(_path))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<IdentityDto>(File.ReadAllText(_path));
                if (dto is not null)
                {
                    var identity = new IdentityKeyPair(
                        Convert.FromBase64String(dto.SigningPublic),
                        Convert.FromBase64String(dto.SigningPrivate),
                        Convert.FromBase64String(dto.DhPublic),
                        Convert.FromBase64String(dto.DhPrivate));

                    _identity = identity;
                    _nickname = dto.Nickname ?? "";
                    IsInitialized = _nickname.Length > 0;
                    return;
                }
            }
            catch
            {
                // Corrupt or incompatible file: fall through and regenerate.
            }
        }

        _identity = KeyGeneration.CreateIdentity();
        _nickname = "";
        IsInitialized = false;
        Save();
    }

    /// <summary>Sets (or updates) the nickname and persists the identity.</summary>
    public void SetNickname(string nickname)
    {
        EnsureSeeded();
        _nickname = (nickname ?? "").Trim();
        IsInitialized = _nickname.Length > 0;
        Save();
    }

    private void Save()
    {
        if (_identity is null)
            return;

        var dto = new IdentityDto
        {
            Nickname = _nickname,
            SigningPublic = Convert.ToBase64String(_identity.SigningPublicKey),
            SigningPrivate = Convert.ToBase64String(_identity.SigningPrivateKey),
            DhPublic = Convert.ToBase64String(_identity.DhPublicKey),
            DhPrivate = Convert.ToBase64String(_identity.DhPrivateKey),
        };

        File.WriteAllText(_path, JsonSerializer.Serialize(dto));
    }

    private sealed class IdentityDto
    {
        public string? Nickname { get; set; }
        public string SigningPublic { get; set; } = "";
        public string SigningPrivate { get; set; } = "";
        public string DhPublic { get; set; } = "";
        public string DhPrivate { get; set; } = "";
    }
}
