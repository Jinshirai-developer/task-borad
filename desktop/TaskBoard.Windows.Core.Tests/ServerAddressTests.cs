using TaskBoard.Windows.Core;

namespace TaskBoard.Windows.Core.Tests;

public sealed class ServerAddressTests
{
    [Theory]
    [InlineData("https://tasks.example.com", "https://tasks.example.com/")]
    [InlineData(" https://tasks.example.com/index.html ", "https://tasks.example.com/")]
    [InlineData("http://localhost:5097/", "http://localhost:5097/")]
    [InlineData("http://127.0.0.1:5097", "http://127.0.0.1:5097/")]
    public void ValidOrigin_IsNormalized(string input, string expected) => Assert.Equal(expected, ServerAddress.Parse(input).AbsoluteUri);

    [Theory]
    [InlineData("http://tasks.example.com")]
    [InlineData("https://user:password@tasks.example.com")]
    [InlineData("https://tasks.example.com/path")]
    [InlineData("https://tasks.example.com/?token=secret")]
    [InlineData("https://tasks.example.com/#secret")]
    [InlineData("file:///C:/Windows/system.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("")]
    public void UnsafeOrUnsupportedAddress_IsRejected(string input) => Assert.Throws<ArgumentException>(() => ServerAddress.Parse(input));

    [Theory]
    [InlineData("https://tasks.example.com/login.html", true)]
    [InlineData("https://tasks.example.com:444/login.html", false)]
    [InlineData("http://tasks.example.com/login.html", false)]
    [InlineData("https://tasks.example.com.attacker.example/", false)]
    [InlineData("https://attacker.example@tasks.example.com/", false)]
    public void Navigation_UsesExactOrigin(string destination, bool expected) =>
        Assert.Equal(expected, ServerAddress.SameOrigin(new Uri("https://tasks.example.com/"), new Uri(destination)));
}
