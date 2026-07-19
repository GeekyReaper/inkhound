using System.Net;

namespace Foundation.Core.Model;

public record ProxyEndpoint(string Host, int Port, string? Username, string? Password)
{
    public WebProxy ToWebProxy()
    {
        var proxy = new WebProxy(Host, Port);
        if (!string.IsNullOrEmpty(Username))
            proxy.Credentials = new NetworkCredential(Username, Password);
        return proxy;
    }
}
