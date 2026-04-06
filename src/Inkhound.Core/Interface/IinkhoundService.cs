using System;
using Inkhound.Core.Models;

namespace Inkhound.Core.Interface;

public interface IinkhoundService
{
    public static string ServiceName = "Inkhound";
    public static abstract Task<(bool IsValid, List<string> Errors)> CheckOptionsAsync(List<OptionDefinition> options, CancellationToken ct = default);
    public bool Initialize(out List<string> errors);

}
