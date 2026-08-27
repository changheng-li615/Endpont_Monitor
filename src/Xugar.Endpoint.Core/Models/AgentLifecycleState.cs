namespace Xugar.Endpoint.Core.Models;

public sealed class AgentLifecycleState
{
    private readonly object _gate = new();
    private bool _runtimeStarted;
    private bool _windowVisible;
    private bool _exitRequested;

    public bool RuntimeStarted
    {
        get { lock (_gate) return _runtimeStarted; }
    }

    public bool WindowVisible
    {
        get { lock (_gate) return _windowVisible; }
    }

    public bool ExitRequested
    {
        get { lock (_gate) return _exitRequested; }
    }

    public bool TryStartRuntime(bool startInBackground)
    {
        lock (_gate)
        {
            if (_runtimeStarted || _exitRequested)
            {
                return false;
            }

            _runtimeStarted = true;
            _windowVisible = !startInBackground;
            return true;
        }
    }

    public void ShowWindow()
    {
        lock (_gate)
        {
            if (!_exitRequested)
            {
                _windowVisible = true;
            }
        }
    }

    public void HideWindow()
    {
        lock (_gate)
        {
            _windowVisible = false;
        }
    }

    public void RequestExit()
    {
        lock (_gate)
        {
            _exitRequested = true;
            _windowVisible = false;
        }
    }
}
