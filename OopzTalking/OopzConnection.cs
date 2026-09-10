using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OopzTalking;

/// <summary>
///     连接 oopz 主程序在本机暴露的 WebSocket (默认 ws://127.0.0.1:10274)。
///     oopz 会定时（约每 1~2 秒）推送一帧全量成员快照：
///     {"cmd":"members","voice":true,"members":[{"name":"Umi","talking":true,"muted":false},...]}
///     无需认证、无需订阅，连上即收；支持多客户端。
/// </summary>
public class OopzConnection: IDisposable {
    private readonly CancellationTokenSource cts = new();
    private readonly Plugin plugin;
    private Task? loopTask;
    private volatile bool connected;

    public OopzConnection(Plugin plugin) {
        this.plugin = plugin;
        this.AllUsers = new ConcurrentDictionary<string, User>();
        this.loopTask = Task.Run(this.Run);
    }

    /// <summary>成员名 → 成员状态（线程安全的并发字典）。</summary>
    internal ConcurrentDictionary<string, User> AllUsers { get; }

    public bool IsConnected => this.connected;

    /// <summary>
    ///     oopz 成员流里没有"自己"的标记，统一由 Plugin 按角色名匹配，
    ///     故这里恒为 null。
    /// </summary>
    public User? Self => null;

    public void Dispose() {
        this.cts.Cancel();
        try {
            this.loopTask?.Wait(1000);
        } catch (Exception) {
            // ignore
        }

        this.cts.Dispose();
    }

    private async Task Run() {
        var delay = TimeSpan.FromSeconds(3);
        while (!this.cts.IsCancellationRequested) {
            ClientWebSocket? ws = null;
            try {
                ws = new ClientWebSocket();
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(this.cts.Token);
                connectCts.CancelAfter(4000);
                var uri = new Uri($"ws://127.0.0.1:{this.plugin.Configuration.Port}");
                await ws.ConnectAsync(uri, connectCts.Token);
                this.connected = true;
                this.plugin.PluginLog.Information("已连接 oopz：{Uri}", uri);
                await this.ReceiveLoop(ws);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception e) {
                this.plugin.PluginLog.Debug("oopz 连接失败，稍后重连：{Error}", e.Message);
            } finally {
                this.connected = false;
                this.AllUsers.Clear();
                ws?.Dispose();
            }

            try {
                await Task.Delay(delay, this.cts.Token);
            } catch (OperationCanceledException) {
                break;
            }
        }
    }

    private async Task ReceiveLoop(ClientWebSocket ws) {
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        while (ws.State == WebSocketState.Open && !this.cts.IsCancellationRequested) {
            var seg = new ArraySegment<byte>(buffer);
            var res = await ws.ReceiveAsync(seg, this.cts.Token);
            if (res.MessageType == WebSocketMessageType.Close) {
                break;
            }

            if (res.MessageType != WebSocketMessageType.Text) {
                continue;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
            if (!res.EndOfMessage) {
                continue;
            }

            var msg = sb.ToString();
            sb.Clear();
            try {
                this.OnMessage(msg);
            } catch (Exception e) {
                this.plugin.PluginLog.Error(e, "解析 oopz 消息失败");
            }
        }
    }

    private void OnMessage(string msg) {
        using var doc = JsonDocument.Parse(msg);
        var root = doc.RootElement;
        if (!root.TryGetProperty("cmd", out var cmd) || cmd.GetString() != "members") {
            return;
        }

        // 不在语音房间时（voice=false），清空成员
        if (!root.TryGetProperty("voice", out var voice) || !voice.GetBoolean()) {
            this.AllUsers.Clear();
            return;
        }

        if (!root.TryGetProperty("members", out var members)) {
            return;
        }

        // members 是全量快照：整体换新，顺带清掉已离开房间的人
        var seen = new HashSet<string>();
        foreach (var m in members.EnumerateArray()) {
            var name = m.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(name)) {
                continue;
            }

            seen.Add(name);
            var talking = m.TryGetProperty("talking", out var t) && t.GetBoolean();
            var muted = m.TryGetProperty("muted", out var mu) && mu.GetBoolean();
            this.AllUsers[name] = new User(name, username: name, displayName: name, muted: muted, speaking: talking);
        }

        foreach (var key in this.AllUsers.Keys) {
            if (!seen.Contains(key)) {
                this.AllUsers.TryRemove(key, out _);
            }
        }
    }
}