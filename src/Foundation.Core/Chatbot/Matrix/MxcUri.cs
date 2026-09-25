namespace Foundation.Core.Chatbot.Matrix;

/// <summary>URI de média Matrix, de la forme <c>mxc://serveur/mediaId</c>.</summary>
public sealed record MxcUri(string ServerName, string MediaId)
{
    public static MxcUri Parse(string mxcUri)
    {
        if (string.IsNullOrWhiteSpace(mxcUri) || !mxcUri.StartsWith("mxc://", StringComparison.Ordinal))
        {
            throw new FormatException($"'{mxcUri}' n'est pas une URI mxc:// valide.");
        }

        var withoutScheme = mxcUri["mxc://".Length..];
        var parts = withoutScheme.Split('/', 2);
        if (parts.Length != 2)
        {
            throw new FormatException($"'{mxcUri}' n'est pas une URI mxc:// valide.");
        }

        return new MxcUri(parts[0], parts[1]);
    }
}
