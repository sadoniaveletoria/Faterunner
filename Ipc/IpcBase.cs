using System;
using System.Linq;

namespace FateRunner.Ipc;

public abstract class IpcBase
{
    protected abstract string InternalName { get; }

    public bool IsAvailable
    {
        get
        {
            try
            {
                return Plugin.PluginInterface.InstalledPlugins.Any(p => p.InternalName == InternalName && p.IsLoaded);
            }
            catch { return false; }
        }
    }

    protected T Safe<T>(Func<T> f, T fallback, string what)
    {
        if (!IsAvailable) return fallback;
        try { return f(); }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"{InternalName}.{what} IPC failed: {ex.Message}");
            return fallback;
        }
    }

    protected void Safe(Action a, string what)
    {
        if (!IsAvailable) return;
        try { a(); }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"{InternalName}.{what} IPC failed: {ex.Message}");
        }
    }
}
