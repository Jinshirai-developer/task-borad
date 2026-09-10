using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskApi.Models;

namespace TaskApi.Authentication;

// Legacy PBKDF2 hashes are upgraded only after the correct password is provided.
public sealed class LegacyPasswordHasher(IOptions<PasswordHasherOptions> options)
    : PasswordHasher<UserProfile>(options)
{
    public override PasswordVerificationResult VerifyHashedPassword(
        UserProfile user, string hashedPassword, string providedPassword)
    {
        if (!hashedPassword.Contains('.'))
        {
            try { return base.VerifyHashedPassword(user, hashedPassword, providedPassword); }
            catch (FormatException) { return PasswordVerificationResult.Failed; }
        }

        var parts = hashedPassword.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)
            || iterations is < 1 or > 2_000_000)
            return PasswordVerificationResult.Failed;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            if (salt.Length != 16 || expected.Length != 32)
                return PasswordVerificationResult.Failed;
            var actual = Rfc2898DeriveBytes.Pbkdf2(providedPassword, salt, iterations,
                HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }
        catch (FormatException) { return PasswordVerificationResult.Failed; }
    }
}
