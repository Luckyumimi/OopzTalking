using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace OopzTalking.Windows;

public sealed class MainWindow: Window, IDisposable {
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin): base("Oopz Talking 调试") {
        this.plugin = plugin;

        this.SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(300, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() {}

    private static unsafe string PtrToString(byte* ptr, int maxLen = int.MaxValue) {
        var len = 0;
        while (len < maxLen && ptr[len] != '\0') {
            len++;
        }

        return Marshal.PtrToStringUTF8((nint)ptr, len);
    }

    public override unsafe void Draw() {
        ImGui.TextUnformatted(
            $"oopz 连接：{(this.plugin.Connection.IsConnected ? "已连接" : "未连接")} "
            + $"(端口 {this.plugin.Configuration.Port})"
        );

        ImGui.Text("GroupManager 小队");
        foreach (var member in GroupManager.Instance()->MainGroup.PartyMembers) {
            ImGui.TextUnformatted($"{member.NameString} {member.HomeWorld}"); // {member.X}");
        }

        ImGui.Text("GroupManager 团队");
        for (var group = 0; group < 6; group++) {
            for (var idx = 0; idx < 8; idx++) {
                var partyMember = GroupManager.Instance()->MainGroup.GetAllianceMemberByGroupAndIndex(group, idx);
                if (partyMember == null) {
                    continue;
                }

                var name = !partyMember->Name.IsEmpty ? partyMember->NameString : "(null)";
                ImGui.TextUnformatted($"[{group}][{idx}] = {name}");
            }
        }

        ImGui.TextUnformatted($"[0] idx {InfoProxyCrossRealm.GetGroupIndex(0)}");
        ImGui.TextUnformatted($"[1] idx {InfoProxyCrossRealm.GetGroupIndex(1)}");
        ImGui.TextUnformatted($"[2] idx {InfoProxyCrossRealm.GetGroupIndex(2)}");
        ImGui.TextUnformatted($"[0] 成员 {InfoProxyCrossRealm.GetGroupMemberCount(0)}");
        ImGui.TextUnformatted($"[1] 成员 {InfoProxyCrossRealm.GetGroupMemberCount(1)}");
        ImGui.TextUnformatted($"[2] 成员 {InfoProxyCrossRealm.GetGroupMemberCount(2)}");
        ImGui.TextUnformatted($"跨服小队员数 {InfoProxyCrossRealm.GetPartyMemberCount()}");
        ImGui.TextUnformatted($"组数 {InfoProxyCrossRealm.Instance()->GroupCount}");

        ImGui.Text($"小队 (共 {this.plugin.PartyList.Length} 人)：");
        foreach (var partyMember in this.plugin.PartyList) {
            ImGui.TextUnformatted($"{partyMember.Name.TextValue}");
        }

        ImGui.Text("oopz 用户：");
        foreach (var user in this.plugin.Connection.AllUsers.Values) {
            var muted = user.Muted.GetValueOrDefault();
            var deafened = user.Deafened.GetValueOrDefault();
            var speaking = user.Speaking.GetValueOrDefault();
            var displayName = user.DisplayName.IsNullOrEmpty() ? user.Username : user.DisplayName;
            ImGui.TextUnformatted(
                $"{displayName}: {(muted ? "" : "未")}静音，{(deafened ? "" : "未")}聋{(speaking ? "，正在说话" : "")}"
            );
            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip($"{user.Username}");
            }
        }

        ImGui.TextUnformatted("AgentHUD 信息：");
        var agentHud = AgentHUD.Instance();
        var partyMemberCount = agentHud->PartyMemberCount;
        var partyMemberList = agentHud->PartyMembers;
        for (var i = 0; i < partyMemberCount; i++) {
            var partyMember = partyMemberList[i];
            var name = partyMember.Name.HasValue ? partyMember.Name.ToString() : "(null)";
            ImGui.TextUnformatted($"    [{i}] = {name}{(partyMember.Object == null ? " (BattleChara 为空)" : "")}");
        }

        var module = InfoModule.Instance();
        if (module != null) {
            ImGui.Text("跨服信息代理：");
            var cwProxy = module->GetInfoProxyById(InfoProxyId.CrossRealmParty);
            var cwMemberCount = InfoProxyCrossRealm.GetPartyMemberCount();
            ImGui.TextUnformatted($"cwMemberCount = {cwMemberCount}");
            for (uint i = 0; i < cwMemberCount; i++) {
                var member = InfoProxyCrossRealm.GetGroupMember(i);
                var name = !member->Name.IsEmpty ? member->NameString : "(null)";
                var idx = member->MemberIndex;
                var groupIdx = member->GroupIndex;
                ImGui.TextUnformatted(
                    $"    [{i}] = {name}, idx {idx} (组 idx {groupIdx}, 职业 {member->ClassJobId})"
                );
            }
        }

        ImGui.Separator();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog)) {
            this.plugin.OpenConfigUi();
        }
    }
}