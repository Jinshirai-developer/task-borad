namespace TaskApi.Authentication;

// Authentication is still required. Only email/legal setup checks are bypassed.
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowAccountSetupAttribute : Attribute;
