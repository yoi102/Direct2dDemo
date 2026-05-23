namespace Direct2dDemo.Shared;

public class DeferAction : IDisposable
{

    private Action _action;
    private DeferAction(Action action)
    {
        _action = action;
    }

    public static DeferAction Create(Action action)
    {
        var scope = new DeferAction(action);
        return scope;
    }

    public void Dispose()
    {
        _action.Invoke();
    }
}
