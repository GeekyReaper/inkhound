using Foundation.Core;

namespace Inkhound.Core.ApiTokens;

public class ApiTokenService : BaseService<ApiTokenOptions>
{
    public override string GetServiceName() => "ApiToken";

    public bool Enabled => Options.Enabled;
}
