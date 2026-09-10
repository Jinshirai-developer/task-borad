using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TaskApi.Data;
using TaskApi.Models;

namespace TaskApi.Services;

// The private device code stays in native-client memory. Only its hash is stored.
// The short public code identifies a request; only an authenticated, ready user may approve it.
public sealed class DesktopSignInService(AppDbContext database)
{
    public const int LifetimeSeconds = 600;
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public (string DeviceCode, string UserCode) Start()
    {
        using var write = WorkspaceWriteScope.Begin(database);
        var now = DateTime.UtcNow;
        database.DesktopSignIns.RemoveRange(database.DesktopSignIns.Where(item => item.ExpiresAt <= now));
        database.SaveChanges();
        if (database.DesktopSignIns.Count() >= 1000)
            throw new AccountOperationException(503, "ログインが混み合っています。しばらく待ってからお試しください。");
        var device = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        string code;
        string codeHash;
        do
        {
            code = string.Concat(Enumerable.Range(0, 8).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]));
            codeHash = Hash(code);
        } while (database.DesktopSignIns.Any(item => item.UserCodeHash == codeHash));
        database.DesktopSignIns.Add(new DesktopSignInRequest
        {
            DeviceCodeHash = Hash(device), UserCodeHash = codeHash,
            ExpiresAt = now.AddSeconds(LifetimeSeconds)
        });
        database.SaveChanges();
        write.Commit();
        return (device, code[..4] + "-" + code[4..]);
    }

    public void Decide(string userCode, int userId, bool approve)
    {
        var code = Regex.Replace(userCode.Trim().ToUpperInvariant(), "[- ]", "");
        if (!Regex.IsMatch(code, "^[A-HJ-NP-Z2-9]{8}$")) throw Expired();
        using var write = WorkspaceWriteScope.Begin(database);
        var hash = Hash(code);
        var request = database.DesktopSignIns.SingleOrDefault(item => item.UserCodeHash == hash);
        if (request == null || request.ExpiresAt <= DateTime.UtcNow || request.Denied || request.UserProfileId != null)
            throw Expired();
        var user = database.UserProfiles.AsNoTracking().SingleOrDefault(item => item.Id == userId);
        if (!Ready(user)) throw new AccountOperationException(403, "アカウントの確認を完了してからお試しください。");
        if (approve)
        {
            request.UserProfileId = user!.Id;
            request.SessionVersion = user.SessionVersion;
        }
        else request.Denied = true;
        database.SaveChanges();
        write.Commit();
    }

    public UserProfile? Consume(string deviceCode)
    {
        if (!Regex.IsMatch(deviceCode, "^[A-Za-z0-9_-]{43}$")) throw Expired();
        using var write = WorkspaceWriteScope.Begin(database);
        var hash = Hash(deviceCode);
        var request = database.DesktopSignIns.SingleOrDefault(item => item.DeviceCodeHash == hash);
        if (request == null || request.ExpiresAt <= DateTime.UtcNow) throw Expired();
        if (request.Denied) throw new AccountOperationException(410, "Windowsアプリへのログインを取り消しました。");
        if (request.UserProfileId == null) return null;
        var user = database.UserProfiles.AsNoTracking().SingleOrDefault(item => item.Id == request.UserProfileId);
        if (!Ready(user) || user!.SessionVersion != request.SessionVersion) throw Expired();
        database.DesktopSignIns.Remove(request);
        database.SaveChanges();
        write.Commit();
        return user;
    }

    private static bool Ready(UserProfile? user) => user != null && user.EmailConfirmed
        && !string.IsNullOrWhiteSpace(user.Email) && !AuthenticationService.RequiresTerms(user)
        && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= DateTimeOffset.UtcNow);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static AccountOperationException Expired() => new(410, "確認コードの期限が切れたか、すでに使用されています。Windowsアプリからやり直してください。");
}
