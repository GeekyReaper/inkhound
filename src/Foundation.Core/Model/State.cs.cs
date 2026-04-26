using System;

namespace Foundation.Core.Model;

public enum EState { NOTINIT, INVALID, OK, WARNING, ERROR }
public class StateService
{
    public EState State { get; set; } = EState.NOTINIT;
    public DateTime LastRefresh { get; private set; } = DateTime.UtcNow;

    public string ServiceName { get; set; } = "unknow";

    public List<string> Infos { get; } = new();

    public void Refresh()
    {
        LastRefresh = DateTime.UtcNow;
        Infos.Clear();
    }
}
public class StateServiceManager
{
    public List<StateService> stateServices { get; } = new();
    public DateTime Date { get; set; } = DateTime.UtcNow;

    public EState GlobalState { get; set; } = EState.NOTINIT;

    public void Init()
    {
        Date = DateTime.UtcNow;
        stateServices.Clear();
    }
}

