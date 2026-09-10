using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskApi.Models;

namespace TaskApi.Authentication;

public sealed class EmailConfirmationTokenProvider(
    IDataProtectionProvider provider, ILogger<DataProtectorTokenProvider<UserProfile>> logger)
    : DataProtectorTokenProvider<UserProfile>(provider,
        Microsoft.Extensions.Options.Options.Create(new DataProtectionTokenProviderOptions
        {
            Name = "TaskBoard.EmailConfirmation.v1",
            TokenLifespan = TimeSpan.FromHours(24)
        }), logger);
