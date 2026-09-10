namespace TaskApi.Authentication;

// The sole cookie/CSRF exemption for a signed server-to-server Stripe notification.
[AttributeUsage(AttributeTargets.Method)]
public sealed class StripeWebhookAttribute : Attribute;
