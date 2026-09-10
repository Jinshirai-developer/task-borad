namespace TaskBoard.Windows.Core;

public static class ServerAddress
{
    public static Uri Parse(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.AbsolutePath != "/" && uri.AbsolutePath != "/index.html")
            || !(uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)))
            throw new ArgumentException("接続先のHTTPS URLを入力してください。開発用のlocalhostのみHTTPを利用できます。");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public static bool SameOrigin(Uri origin, Uri target) => origin.Scheme == target.Scheme
        && origin.IdnHost == target.IdnHost && origin.Port == target.Port && string.IsNullOrEmpty(target.UserInfo);

    public static bool IsWebLink(Uri uri) => (uri.Scheme == "https" || uri.Scheme == "http") && string.IsNullOrEmpty(uri.UserInfo);
}
