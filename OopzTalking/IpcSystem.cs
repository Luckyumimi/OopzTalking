using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace OopzTalking;

public class IpcSystem: IDisposable {
    private readonly ICallGateProvider<string, int> cgGetUserState;
    private readonly Plugin plugin;

    public IpcSystem(Plugin plugin, IDalamudPluginInterface pluginInterface) {
        this.plugin = plugin;

        this.cgGetUserState = pluginInterface.GetIpcProvider<string, int>("OopzTalking.GetUserState");
        this.cgGetUserState.RegisterFunc(this.GetUserState);

        plugin.PluginLog.Verbose("[IPC] Firing OopzTalking.Available.");
        var cgAvailable = pluginInterface.GetIpcProvider<bool>("OopzTalking.Available");
        cgAvailable.SendMessage();
    }

    public void Dispose() {
        this.cgGetUserState.UnregisterFunc();
    }

    /// <remarks>
    ///     Must be called from the main thread.
    /// </remarks>
    private int GetUserState(string name) {
        var user = this.plugin.XivToOopz(name);
        var state = user?.State ?? UserState.None;

        return (int)state;
    }
}