using System.Text.Json;
using SecureMesh.Core;
using Microsoft.Maui.Storage;

namespace SecureMesh.App.Services;

/// <summary>
/// Owns the local user's long-term <see cref="IdentityKeyPair"/> and nickname.
/// Private key material is persisted in Android Keystore-backed
/// <see cref="SecureStorage"/>; public keys and the nickname live in
/// <see cref="Preferences"/>. The identity is seeded (generated) on first access
/// so the domain services can always be constructed; the nickname completes
/// "first launch" setup.
/// </summary>
public sealed class IdentityService
{
    private const string LegacyFileName = "identity.json";

    private IdentityKeyPair? _identity;
    private string _nickname = "";

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
    /// none exists (or the stored entry is corrupt). This is the "seed on startup" step.
    /// </summary>
    public void EnsureSeeded()
    {
        if (_identity is not null)
            return;

        if (!TryLoadFromSecureStorage() && !TryMigrateLegacyFile())
        {
            _identity = KeyGeneration.CreateIdentity();
            _nickname = "";
            IsInitialized = false;
            Persist();
        }

        // Never leave a plaintext identity.json behind.
        DeleteLegacyFile();
    }

    /// <summary>Sets (or updates) the nickname and persists the identity.</summary>
    public void SetNickname(string nickname)
    {
        EnsureSeeded();
        _nickname = (nickname ?? "").Trim();
        IsInitialized = _nickname.Length > 0;
        Persist();
    }

    private bool TryLoadFromSecureStorage()
    {
        var signingPublic = TryBase64(KeyVault.GetPreference(KeyVault.SigningPublic));
        var signingPrivate = TryBase64(KeyVault.GetSecret(KeyVault.SigningPrivate));
        var dhPublic = TryBase64(KeyVault.GetPreference(KeyVault.DhPublic));
        var dhPrivate = TryBase64(KeyVault.GetSecret(KeyVault.DhPrivate));
        var ephemeralPublic = TryBase64(KeyVault.GetPreference(KeyVault.EphemeralDhPublic));
        var ephemeralPrivate = TryBase64(KeyVault.GetSecret(KeyVault.EphemeralDhPrivate));

        if (signingPublic is null || signingPrivate is null ||
            dhPublic is null || dhPrivate is null ||
            ephemeralPublic is null || ephemeralPrivate is null)
            return false;

        if (signingPublic.Length != IdentityKeyPair.Ed25519PublicKeyBytes ||
            signingPrivate.Length != IdentityKeyPair.Ed25519PrivateKeyBytes ||
            dhPublic.Length != IdentityKeyPair.X25519PublicKeyBytes ||
            dhPrivate.Length != IdentityKeyPair.X25519PrivateKeyBytes ||
            ephemeralPublic.Length != IdentityKeyPair.X25519PublicKeyBytes ||
            ephemeralPrivate.Length != IdentityKeyPair.X25519PrivateKeyBytes)
            return false;

        try
        {
            _identity = new IdentityKeyPair(
                signingPublic, signingPrivate,
                dhPublic, dhPrivate,
                ephemeralPublic, ephemeralPrivate);
        }
        catch (ArgumentException)
        {
            return false; // corrupt entry → regenerate
        }

        _nickname = KeyVault.GetPreference(KeyVault.Nickname) ?? "";
        IsInitialized = _nickname.Length > 0;
        return true;
    }

    private bool TryMigrateLegacyFile()
    {
        var legacyPath = Path.Combine(FileSystem.AppDataDirectory, LegacyFileName);
        if (!File.Exists(legacyPath))
            return false;

        try
        {
            var dto = JsonSerializer.Deserialize<LegacyIdentityDto>(File.ReadAllText(legacyPath));
            if (dto is null)
                return false;

            var signingPublic = Convert.FromBase64String(dto.SigningPublic);
            var signingPrivate = Convert.FromBase64String(dto.SigningPrivate);
            var dhPublic = Convert.FromBase64String(dto.DhPublic);
            var dhPrivate = Convert.FromBase64String(dto.DhPrivate);

            // Ephemeral keys did not exist in the legacy format; generate fresh.
            var fresh = KeyGeneration.CreateIdentity();

            _identity = new IdentityKeyPair(
                signingPublic, signingPrivate,
                dhPublic, dhPrivate,
                fresh.EphemeralDhPublicKey, fresh.EphemeralDhPrivateKey);
            _nickname = dto.Nickname ?? "";
            IsInitialized = _nickname.Length > 0;
            Persist();
            return true;
        }
        catch
        {
            return false; // corrupt legacy file → regenerate
        }
    }

    private void Persist()
    {
        if (_identity is null)
            return;

        KeyVault.SetSecret(KeyVault.SigningPrivate, Convert.ToBase64String(_identity.SigningPrivateKey));
        KeyVault.SetSecret(KeyVault.DhPrivate, Convert.ToBase64String(_identity.DhPrivateKey));
        KeyVault.SetSecret(KeyVault.EphemeralDhPrivate, Convert.ToBase64String(_identity.EphemeralDhPrivateKey));

        KeyVault.SetPreference(KeyVault.SigningPublic, Convert.ToBase64String(_identity.SigningPublicKey));
        KeyVault.SetPreference(KeyVault.DhPublic, Convert.ToBase64String(_identity.DhPublicKey));
        KeyVault.SetPreference(KeyVault.EphemeralDhPublic, Convert.ToBase64String(_identity.EphemeralDhPublicKey));
        KeyVault.SetPreference(KeyVault.Nickname, _nickname);
    }

    private static void DeleteLegacyFile()
    {
        try
        {
            var legacyPath = Path.Combine(FileSystem.AppDataDirectory, LegacyFileName);
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
        catch
        {
            // Best-effort cleanup; not fatal.
        }
    }

    private static byte[]? TryBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class LegacyIdentityDto
    {
        public string? Nickname { get; set; }
        public string SigningPublic { get; set; } = "";
        public string SigningPrivate { get; set; } = "";
        public string DhPublic { get; set; } = "";
        public string DhPrivate { get; set; } = "";
    }
}

/// <summary>
/// Central place for the SecureStorage/Preferences keys and synchronous
/// (startup-time) access to them. SecureStorage is Keystore-backed on Android;
/// its async API is bridged synchronously here because the seeding path
/// (<see cref="IdentityService.EnsureSeeded"/>) is synchronous.
/// </summary>
internal static class KeyVault
{
    public const string SigningPrivate = "identity.signing.private";
    public const string DhPrivate = "identity.dh.private";
    public const string EphemeralDhPrivate = "identity.ephemeral_dh.private";
    public const string SigningPublic = "identity.signing.public";
    public const string DhPublic = "identity.dh.public";
    public const string EphemeralDhPublic = "identity.ephemeral_dh.public";
    public const string Nickname = "identity.nickname";
    public const string DbPassword = "db.password";

    public static string? GetSecret(string key) =>
        Task.Run(async () => await SecureStorage.Default.GetAsync(key)).GetAwaiter().GetResult();

    public static void SetSecret(string key, string value) =>
        Task.Run(async () => await SecureStorage.Default.SetAsync(key, value)).GetAwaiter().GetResult();

    public static void RemoveSecret(string key) => SecureStorage.Default.Remove(key);

    public static string? GetPreference(string key) =>
        Preferences.Default.Get(key, (string?)null);

    public static void SetPreference(string key, string value) =>
        Preferences.Default.Set(key, value);

    public static void RemovePreference(string key) => Preferences.Default.Remove(key);

    public static void WipeSecrets()
    {
        RemoveSecret(SigningPrivate);
        RemoveSecret(DhPrivate);
        RemoveSecret(EphemeralDhPrivate);
        RemoveSecret(DbPassword);
    }

    public static void WipePreferences()
    {
        RemovePreference(SigningPublic);
        RemovePreference(DhPublic);
        RemovePreference(EphemeralDhPublic);
        RemovePreference(Nickname);
    }
}
