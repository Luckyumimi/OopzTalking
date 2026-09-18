using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Utility.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using JetBrains.Annotations;
using OopzTalking.Windows;

namespace OopzTalking;

[PublicAPI]
public sealed class Plugin: IDalamudPlugin {
    private readonly ISharedImmediateTexture deafenIcon;
    private readonly ISharedImmediateTexture muteIcon;

    internal OopzConnection Connection;
    private Stack<Action> disposeActions = new();
    internal IpcSystem IpcSystem;

    private int validSlots;
    public WindowSystem WindowSystem = new("OopzTalking");

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IGameGui gameGui,
        IPartyList partyList,
        IObjectTable objectTable,
        IClientState clientState,
        ICommandManager commandManager,
        IPlayerState playerState,
        IPluginLog pluginLog,
        INotificationManager notificationManager,
        ITextureProvider textureProvider,
        IAddonLifecycle addonLifecycle,
        IChatGui chatGui,
        IContextMenu contextMenu
    ) {
        this.PluginInterface = pluginInterface;
        this.GameGui = gameGui;
        this.PartyList = partyList;
        this.ObjectTable = objectTable;
        this.ClientState = clientState;
        this.CommandManager = commandManager;
        this.PlayerState = playerState;
        this.PluginLog = pluginLog;
        this.NotificationManager = notificationManager;
        this.TextureProvider = textureProvider;
        this.AddonLifecycle = addonLifecycle;
        this.ChatGui = chatGui;
        this.ContextMenu = contextMenu;

        this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(this.PluginInterface);

        this.ConfigWindow = new ConfigWindow(this);
        this.disposeActions.Push(() => this.ConfigWindow.Dispose());
        this.MainWindow = new MainWindow(this);
        this.disposeActions.Push(() => this.MainWindow.Dispose());

        this.WindowSystem.AddWindow(this.ConfigWindow);
        this.WindowSystem.AddWindow(this.MainWindow);
        this.disposeActions.Push(() => this.WindowSystem.RemoveAllWindows());

        this.PluginInterface.UiBuilder.Draw += this.Draw;
        this.disposeActions.Push(() => this.PluginInterface.UiBuilder.Draw -= this.Draw);
        this.PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        this.disposeActions.Push(() => this.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi);

        this.Connection = new OopzConnection(this);
        this.disposeActions.Push(() => this.Connection.Dispose());

        this.IpcSystem = new IpcSystem(this, pluginInterface);
        this.disposeActions.Push(() => this.IpcSystem.Dispose());

        this.CommandManager.AddHandler(
            "/oopz",
            new CommandInfo(this.OnCommand) {
                HelpMessage = "打开设置\n" + "/oopz port <数字> -- 设置端口",
            }
        );

        this.disposeActions.Push(() => this.CommandManager.RemoveHandler("/oopz"));

        this.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_PartyList", this.AtkDrawPartyList);
        this.disposeActions.Push(() => this.AddonLifecycle.UnregisterListener(this.AtkDrawPartyList));

        this.AddonLifecycle.RegisterListener(
            AddonEvent.PreDraw,
            ["_AllianceList1", "_AllianceList2"],
            this.AtkDrawAllianceList
        );
        this.disposeActions.Push(() => this.AddonLifecycle.UnregisterListener(this.AtkDrawAllianceList));

        // 小队列表右键菜单：oopz 成员绑定
        this.ContextMenu.OnMenuOpened += this.OnContextMenuOpened;
        this.disposeActions.Push(() => this.ContextMenu.OnMenuOpened -= this.OnContextMenuOpened);

        // Voice list activity images
        var imagesPath = Path.Combine(this.PluginInterface.AssemblyLocation.Directory?.FullName!, "images");
        this.muteIcon = this.TextureProvider.GetFromFile(Path.Combine(imagesPath, "mute.png"));
        this.deafenIcon = this.TextureProvider.GetFromFile(Path.Combine(imagesPath, "deafen.png"));

#if DEBUG
        if (pluginInterface.Reason == PluginLoadReason.Reload) {
            this.MainWindow.IsOpen = true;
        }
        // this.ConfigWindow.IsOpen = true;
#endif
        if (pluginInterface.Reason == PluginLoadReason.Installer) {
            this.ConfigWindow.IsOpen = true;
        }

        this.PluginLog.Information("Oopz Talking 已就绪！");
    }

    public Configuration Configuration { get; init; }
    internal ConfigWindow ConfigWindow { get; init; }
    internal IGameGui GameGui { get; init; }
    internal MainWindow MainWindow { get; init; }
    internal IPartyList PartyList { get; init; }
    internal IObjectTable ObjectTable { get; init; }
    internal IDalamudPluginInterface PluginInterface { get; init; }
    internal IClientState ClientState { get; init; }
    internal ICommandManager CommandManager { get; init; }
    internal IPlayerState PlayerState { get; init; }
    internal IPluginLog PluginLog { get; init; }
    internal INotificationManager NotificationManager { get; init; }
    internal ITextureProvider TextureProvider { get; init; }
    internal IAddonLifecycle AddonLifecycle { get; init; }
    internal IChatGui ChatGui { get; init; }
    internal IContextMenu ContextMenu { get; init; }

    public void Dispose() {
        foreach (var action in this.disposeActions) {
            action.Invoke();
        }
    }

    private void OnCommand(string command, string args) {
        if (args.Length != 0) {
            var arguments = args.Split(" ");
            if (arguments[0].ToLower().Equals("port")) {
                if (arguments.Length > 1) {
                    if (int.TryParse(arguments[1], out var port)) {
                        this.Configuration.Port = port;
                        this.Configuration.Save();
                        this.ChatGui.Print($"端口已设为 {port}，正在重连…", "Oopz Talking");
                        this.ReconnectOopz();
                    } else {
                        this.ChatGui.PrintError("端口不是有效数字。", "Oopz Talking");
                    }
                } else {
                    this.ChatGui.PrintError("用法：/oopz port <数字>", "Oopz Talking");
                }
            } else {
                this.ChatGui.PrintError(
                    "可用命令：\n- /oopz\n- /oopz port <数字>",
                    "Oopz Talking"
                );
            }
        } else {
            this.ConfigWindow.IsOpen = !this.ConfigWindow.IsOpen;
        }
    }

    public void OpenConfigUi() {
        this.ConfigWindow.IsOpen = true;
    }

    public void ReconnectOopz() {
        this.Connection.Dispose();
        this.Connection = new OopzConnection(this);
    }

    // 读取当前小队全部成员名（含跨服/跨世界小队），供设置界面「高级手动绑定」下拉使用。
    // 数据来源与游戏内的队伍列表一致：跨服小队走 InfoProxyCrossRealm，
    // 同服/进本走 InfoProxyCommonList，最后用 Dalamud 小队列表兜底。
    public unsafe List<string> GetPartyMemberNames() {
        var names = new List<string>();

        void Add(string? name) {
            if (string.IsNullOrEmpty(name)) {
                return;
            }

            // 空位占位符（游戏用括号占位，如 "(   )"）
            if (name.StartsWith('(')) {
                return;
            }

            if (!names.Contains(name)) {
                names.Add(name);
            }
        }

        // 1) 跨服小队 / 24 人本：自己所在的那一组
        var ipcr = InfoProxyCrossRealm.Instance();
        if (ipcr != null && (InfoProxyCrossRealm.IsCrossRealmParty() || ipcr->IsInAllianceRaid)) {
            var memberCount = ipcr->IsInAllianceRaid
                ? InfoProxyCrossRealm.GetGroupMemberCount(ipcr->LocalPlayerGroupIndex)
                : InfoProxyCrossRealm.GetPartyMemberCount();
            for (var i = 0; i < memberCount; i++) {
                var member = InfoProxyCrossRealm.GetGroupMember((uint)i);
                if (member != null) {
                    Add(member->NameString);
                }
            }
        }

        // 2) 常规小队（同服 / 进本）：游戏队伍列表数据源
        var partyInfoProxy = InfoProxyPartyMember.Instance();
        if (partyInfoProxy != null) {
            var partyMemberCount = partyInfoProxy->InfoProxyCommonList.DataSize;
            for (var i = 0; i < partyMemberCount; i++) {
                var entry = partyInfoProxy->InfoProxyCommonList.GetEntry((uint)i);
                if (entry != null) {
                    Add(entry->NameString);
                }
            }
        }

        // 3) 兜底：Dalamud 小队列表 + 自己
        foreach (var member in this.PartyList) {
            Add(member.Name.TextValue);
        }

        Add(this.PlayerState.CharacterName);

        return names;
    }

    // 当前 oopz 语音房间里的成员名（显示名优先），右键菜单和设置界面共用。
    public List<string> GetOopzMemberNames() {
        var names = new List<string>();
        foreach (var user in this.Connection.AllUsers.Values) {
            var displayName = user.DisplayName.IsNullOrEmpty() ? user.Username : user.DisplayName;
            if (!displayName.IsNullOrEmpty() && !names.Contains(displayName)) {
                names.Add(displayName);
            }
        }

        return names;
    }

    // 菜单图标：游戏内的方框字母 O（U+E07F = SeIconChar.BoxedLetterO）。
    // 之前「已绑定」标记用的 ✓(U+2713) 游戏字体里没有，会渲染成方框。
    private const SeIconChar MenuIcon = SeIconChar.BoxedLetterO;

    // 右键菜单里出现「oopz成员绑定」的界面：只要右键目标是某个玩家角色名，
    // 绑定就有意义，所以把 HUD 小队/团队列表、O 键社交面板各页、副本队员列表都算上。
    // 社交面板（O 键）里的小队页在游戏里叫 PartyMemberList，本体是 SocialList。
    private static bool IsPlayerListAddon(string? addonName) =>
        addonName is "_PartyList" or "_AllianceList1" or "_AllianceList2"
            or "PartyMemberList" or "SocialList" or "ContactList" or "FriendList"
            or "ContentMemberList";

    // 玩家列表右键菜单：加一个「oopz成员绑定」子菜单。
    // 写法参考 DailyRoutines 的 PetSizeContextMenu：外层项 IsSubmenu = true，
    // 点击时用 args.OpenSubmenu(...) 展开真正的成员列表。
    private void OnContextMenuOpened(IMenuOpenedArgs args) {
        if (!IsPlayerListAddon(args.AddonName)) {
            return;
        }

        if (args.Target is not MenuTargetDefault target) {
            return;
        }

        var characterName = GetContextMenuTargetName(target);
        if (string.IsNullOrEmpty(characterName)) {
            return;
        }

        args.AddMenuItem(
            new MenuItem {
                Name = "oopz成员绑定",
                Prefix = MenuIcon,
                IsSubmenu = true,
                UseDefaultPrefix = false,
                OnClicked = clickedArgs => clickedArgs.OpenSubmenu(
                    "oopz成员绑定",
                    this.BuildOopzBindingMenuItems(characterName)
                ),
            }
        );
    }

    // 子菜单内容：当前语音房间的成员列表；没进语音频道就给一句提示。
    private List<MenuItem> BuildOopzBindingMenuItems(string characterName) {
        var items = new List<MenuItem>();

        var members = this.GetOopzMemberNames();
        if (members.Count == 0) {
            items.Add(
                new MenuItem {
                    Name = "你还没有进入语音频道哦",
                    IsEnabled = false,
                    UseDefaultPrefix = false,
                }
            );

            return items;
        }

        foreach (var member in members) {
            var bound = this.Configuration.IndividualAssignments.Any(
                entry => entry.CharacterName == characterName && entry.OopzName == member
            );

            var item = new MenuItem {
                Name = bound ? $"{member}（已绑定，点击取消）" : member,
                UseDefaultPrefix = false,
                OnClicked = _ => this.ToggleOopzBinding(characterName, member),
            };

            if (bound) {
                // 已绑定的项前面加同一个方框 O 图标
                item.Prefix = MenuIcon;
            }

            items.Add(item);
        }

        return items;
    }

    // 点某个 oopz 成员：没绑过就绑上，绑过就取消。
    // 同一个角色可以绑多个成员，同一个成员也能被多个角色绑定。
    public void ToggleOopzBinding(string characterName, string oopzName) {
        var assignments = this.Configuration.IndividualAssignments;
        var existing = assignments.FirstOrDefault(
            entry => entry.CharacterName == characterName && entry.OopzName == oopzName
        );

        if (existing != null) {
            assignments.Remove(existing);
            this.ChatGui.Print($"已取消绑定：{characterName} ✕ {oopzName}", "Oopz Talking");
        } else {
            assignments.Add(new AssignmentEntry { CharacterName = characterName, OopzName = oopzName });
            this.ChatGui.Print($"已绑定：{characterName} → {oopzName}", "Oopz Talking");
        }

        this.Configuration.Save();

        // 设置窗口里存着一份列表副本，外部改了绑定要让它重新读一遍，
        // 否则之后在窗口里改任何一行都会用旧副本覆盖掉这里的改动。
        this.ConfigWindow.SyncAssignmentsFromConfig();
    }

    // 从右键目标里取角色名：优先角色对象，其次同区域的目标对象，最后目标名。
    // 跨服成员可能带 "@服务器" 后缀，而绑定表里只存角色名，所以去掉。
    private static string? GetContextMenuTargetName(MenuTargetDefault target) {
        var name = target.TargetCharacter?.Name;
        if (string.IsNullOrWhiteSpace(name)) {
            name = target.TargetObject?.Name?.TextValue;
        }

        if (string.IsNullOrWhiteSpace(name)) {
            name = target.TargetName;
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        var atIndex = name.IndexOf('@');
        if (atIndex > 0) {
            name = name[..atIndex];
        }

        return name.Trim();
    }

    private uint GetColour(User? user) {
        if (user == null) {
            return this.Configuration.ShowUnmatchedUsers ? this.Configuration.ColourUnmatched : 0;
        }

        if (user.Speaking.GetValueOrDefault()) {
            return this.Configuration.ColourSpeaking;
        }

        if (user.Deafened.GetValueOrDefault()) {
            return this.Configuration.ColourDeafened;
        }

        if (user.Muted.GetValueOrDefault()) {
            return this.Configuration.ColourMuted;
        }

        return 0;
    }

    private static unsafe Vector2 GetNodePosition(AtkResNode* node) {
        var pos = new Vector2(node->X, node->Y);
        var par = node->ParentNode;
        while (par != null) {
            pos *= new Vector2(par->ScaleX, par->ScaleY);
            pos += new Vector2(par->X, par->Y);
            par = par->ParentNode;
        }

        return pos;
    }

    private static unsafe Vector2 GetNodePosition(AtkComponentNode* node) {
        var pos = new Vector2(node->X, node->Y);
        var par = node->ParentNode;
        while (par != null) {
            pos *= new Vector2(par->ScaleX, par->ScaleY);
            pos += new Vector2(par->X, par->Y);
            par = par->ParentNode;
        }

        return pos;
    }

    private unsafe void AtkDrawPartyList(AddonEvent evt, AddonArgs args) {
        if (this.Configuration.IndicatorStyle != IndicatorStyle.Atk) {
            return;
        }

        var partyList = (AddonPartyList*)args.Addon.Address;
        if (partyList == null) {
            return;
        }

        for (var i = 0; i < 8; i++) {
            var partyMemberComponent = partyList->PartyMembers[i].PartyMemberComponent;
            if (partyMemberComponent == null) {
                continue;
            }

            if (partyMemberComponent->OwnerNode == null) {
                continue;
            }

            if (!partyMemberComponent->OwnerNode->IsVisible()) {
                continue;
            }

            var jobIconGlow = partyMemberComponent->GetImageNodeById(20);
            if (jobIconGlow == null) {
                continue;
            }

            if ((this.validSlots & (1 << i)) != 0) {
                jobIconGlow->ToggleVisibility(jobIconGlow->Color.RGBA != 0);
            } else { // reset the colour to normal
                jobIconGlow->Color.RGBA = 0xffffffff;
            }
        }
    }

    private unsafe void AtkDrawAllianceList(AddonEvent evt, AddonArgs args) {
        if (this.Configuration.IndicatorStyle != IndicatorStyle.Atk) {
            return;
        }

        var allianceList = (AtkUnitBase*)args.Addon.Address;
        if (allianceList == null) {
            return;
        }

        var offset = allianceList->NameString.EndsWith('1') ? 8 : 16;
        for (var i = 0; i < 8; i++) {
            var memberNode = allianceList->GetComponentNodeById((uint)i + 3);
            if (memberNode == null) {
                continue;
            }

            if (!memberNode->IsVisible()) {
                continue;
            }

            var componentNode = memberNode->GetComponent();
            if (componentNode == null) {
                continue;
            }

            var jobIconGlow = componentNode->GetImageNodeById(9);
            if (jobIconGlow == null) {
                continue;
            }

            if ((this.validSlots & (1 << (i + offset))) != 0) {
                jobIconGlow->ToggleVisibility(jobIconGlow->Color.RGBA != 0);
            } else { // reset the colour to normal
                jobIconGlow->Color.RGBA = 0xffffffff;
            }
        }
    }

    private void Draw() {
        this.WindowSystem.Draw();
        if (this.Connection.IsConnected || this.ConfigWindow.IsOpen) {
            this.DrawOverlay();
        } else {
            this.validSlots = 0;
        }
    }

    private unsafe void DrawIndicator(ImDrawListPtr drawList, AddonPartyList* partyAddon, int idx, User? user) {
        if (!this.Configuration.ShowIndicators) {
            return;
        }

        var classJobIcon = partyAddon->PartyMembers[idx].ClassJobIcon;
        if (classJobIcon == null) { // this seems like it's null sometimes? set up cwp via pf, exception on join
            return;
        }

        var colNode = &classJobIcon->AtkResNode;

        if (this.Configuration.IndicatorStyle == IndicatorStyle.Imgui) {
            var indicatorStart = GetNodePosition(colNode);
            var scale = partyAddon->AtkUnitBase.Scale;
            var indicatorSize = new Vector2(colNode->Width, colNode->Height) * scale;
            var indicatorMin = indicatorStart + ImGui.GetMainViewport().Pos;
            var indicatorMax = indicatorStart + indicatorSize + ImGui.GetMainViewport().Pos;
            var cornerStyle = this.Configuration.UseRoundedCorners
                ? ImDrawFlags.RoundCornersAll
                : ImDrawFlags.RoundCornersNone;
            drawList.AddRect(
                indicatorMin,
                indicatorMax,
                this.GetColour(user),
                7 * scale,
                cornerStyle,
                (3 * scale) - 1
            );
        } else if (this.Configuration.IndicatorStyle == IndicatorStyle.Atk) {
            var colour = this.GetColour(user);
            var jobIconGlowNode = partyAddon->PartyMembers[idx].PartyMemberComponent->GetImageNodeById(20);
            jobIconGlowNode->Color.RGBA = colour;
            // visibility will be handled in predraw
        }
    }

    private unsafe void DrawIndicatorAlliance(ImDrawListPtr drawList, AtkUnitBase* allianceAddon, int idx, User? user) {
        if (!this.Configuration.ShowIndicators) {
            return;
        }

        var nodePtr = allianceAddon->GetComponentNodeById((uint)idx + 3);
        var comp = nodePtr->Component;
        var gridNode = comp->UldManager.SearchNodeById(4);

        if (this.Configuration.IndicatorStyle == IndicatorStyle.Imgui) {
            var indicatorStart = GetNodePosition(gridNode);
            var scale = allianceAddon->Scale;
            var indicatorSize = new Vector2(gridNode->Width, gridNode->Height) * scale;
            var indicatorMin = indicatorStart + ImGui.GetMainViewport().Pos;
            var indicatorMax = indicatorStart + indicatorSize + ImGui.GetMainViewport().Pos;
            var cornerStyle = this.Configuration.UseRoundedCorners
                ? ImDrawFlags.RoundCornersAll
                : ImDrawFlags.RoundCornersNone;
            drawList.AddRect(
                indicatorMin,
                indicatorMax,
                this.GetColour(user),
                7 * scale,
                cornerStyle,
                (3 * scale) - 1
            );
        } else if (this.Configuration.IndicatorStyle == IndicatorStyle.Atk) {
            var colour = this.GetColour(user);
            var jobIconGlowNode = comp->UldManager.SearchNodeById(9);
            jobIconGlowNode->Color.RGBA = colour;
            // visibility will be handled in predraw
        }
    }

    private unsafe void DrawOverlay() {
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos);
        ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
        var validSlots = 0;
        if (ImGui.Begin(
                "##OopzTalkingOverlay",
                ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoMouseInputs
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoNav
            )) {
            try {
                var knownUsers = new HashSet<User>();
                ImGui.PushClipRect(
                    ImGui.GetMainViewport().Pos,
                    ImGui.GetMainViewport().Pos + ImGui.GetMainViewport().Size,
                    false
                );
                var drawList = ImGui.GetWindowDrawList();
                var partyAddon = (AddonPartyList*)this.GameGui.GetAddonByName("_PartyList").Address;
                var shouldDrawParty = (nint)partyAddon != nint.Zero
                    && partyAddon->AtkUnitBase.IsVisible // false only if hidden by being in solo
                    && (partyAddon->AtkUnitBase.VisibilityFlags & 1) == 0 // hidden by user (HUD Layout)
                    && (partyAddon->AtkUnitBase.VisibilityFlags & 4) == 0 // hidden by game
                    && !this.GameGui.GameUiHidden; // entire UI hidden
                if (shouldDrawParty) {
                    var ipcr = InfoProxyCrossRealm.Instance();
                    if (ipcr->IsInAllianceRaid) {
                        var memberCount = InfoProxyCrossRealm.GetGroupMemberCount(ipcr->LocalPlayerGroupIndex);
                        for (var i = 0; i < memberCount; i++) {
                            var member = InfoProxyCrossRealm.GetGroupMember((uint)i);
                            var user = this.XivToOopz(member->NameString);
                            this.DrawIndicator(drawList, partyAddon, i, user);
                            validSlots |= 1 << i;
                            if (user != null) {
                                knownUsers.Add(user);
                            }
                        }
                    } else {
                        var memberCount = InfoProxyCrossRealm.GetPartyMemberCount();
                        for (var i = 0; i < memberCount; i++) {
                            var member = InfoProxyCrossRealm.GetGroupMember((uint)i);
                            var user = this.XivToOopz(member->NameString);
                            this.DrawIndicator(drawList, partyAddon, i, user);
                            validSlots |= 1 << i;
                            if (user != null) {
                                knownUsers.Add(user);
                            }
                        }
                    }

                    if (this.PartyList.Length == 0 && !InfoProxyCrossRealm.IsCrossRealmParty()) {
                        // only enter solo mode if nobody's in *our* full party AND there are no cross-realm shenanigans afoot
                        var node = partyAddon->AtkUnitBase.UldManager.SearchNodeById(10);
                        if (node != null && node->IsVisible()) {
                            var self = this.XivToOopz(this.PlayerState.CharacterName);
                            this.DrawIndicator(drawList, partyAddon, 0, self);
                            validSlots |= 1;
                            if (self != null) {
                                knownUsers.Add(self);
                            }
                        }
                    }

                    if (this.PartyList.Length > 0) {
                        // regular party (or cross-world party in an instance, which works out the same)
                        var agentHud = AgentHUD.Instance();
                        var partyMemberCount = Math.Min(this.PartyList.Length, agentHud->PartyMemberCount);
                        var partyMemberList = agentHud->PartyMembers; // length 10
                        for (var i = 0; i < partyMemberCount; i++) {
                            var partyMember = partyMemberList[i];
                            var user = this.XivToOopz(partyMember.Name.ToString());
                            this.DrawIndicator(drawList, partyAddon, i, user);
                            validSlots |= 1 << i;
                            if (user != null) {
                                knownUsers.Add(user);
                            }
                        }
                    }

                    // do we need to draw indicators for other alliances?
                    var allianceWindow1 = (AtkUnitBase*)this.GameGui.GetAddonByName("_AllianceList1").Address;
                    var allianceWindow2 = (AtkUnitBase*)this.GameGui.GetAddonByName("_AllianceList2").Address;
                    var allianceWindow1Visible =
                        (nint)allianceWindow1 != nint.Zero
                        && allianceWindow1->IsVisible
                        && (allianceWindow1->VisibilityFlags & 1) == 0
                        && (allianceWindow1->VisibilityFlags & 4) == 0;
                    var allianceWindow2Visible =
                        (nint)allianceWindow2 != nint.Zero
                        && allianceWindow2->IsVisible
                        && (allianceWindow2->VisibilityFlags & 1) == 0
                        && (allianceWindow2->VisibilityFlags & 4) == 0;
                    if (ipcr->IsInAllianceRaid && allianceWindow1Visible && allianceWindow2Visible) {
                        var allianceWindowNumber = 1;
                        for (byte group = 0; group < ipcr->GroupCount; group++) {
                            var groupIndex = InfoProxyCrossRealm.GetGroupIndex(group);
                            if (groupIndex == ipcr->LocalPlayerGroupIndex) {
                                continue;
                            }

                            var groupManager = GroupManager.Instance();
                            var groupMemberCount = InfoProxyCrossRealm.GetGroupMemberCount(group);
                            for (var memberIdx = 0; memberIdx < groupMemberCount; memberIdx++) {
                                var member = groupManager->MainGroup.GetAllianceMemberByGroupAndIndex(
                                    allianceWindowNumber - 1,
                                    memberIdx
                                );
                                if (member is null) {
                                    continue;
                                }

                                var name = member->NameString;
                                if (name == "") {
                                    continue;
                                }

                                var user = this.XivToOopz(name);
                                validSlots |= 1 << (memberIdx + (8 * allianceWindowNumber));
                                if (allianceWindowNumber == 1) {
                                    this.DrawIndicatorAlliance(drawList, allianceWindow1, memberIdx, user);
                                    if (user != null) {
                                        knownUsers.Add(user);
                                    }
                                } else if (allianceWindowNumber == 2) {
                                    this.DrawIndicatorAlliance(drawList, allianceWindow2, memberIdx, user);
                                    if (user != null) {
                                        knownUsers.Add(user);
                                    }
                                } else {
                                    this.PluginLog.Error(
                                        $"bad alliance window {allianceWindowNumber}, are you doing DRS or something"
                                    );
                                }
                            }

                            allianceWindowNumber++;
                        }
                    }

                    // who else is talking?
                    if (this.Configuration.NonXivUsersDisplayMode != NonXivUsersDisplayMode.Off) {
                        var pos = this.GetNonXivUsersPos(partyAddon);

                        if (pos != null) {
                            var position = pos.Value;
                            var leftColor = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.75f));
                            var rightColor = ImGui.GetColorU32(new Vector4(0, 0, 0, 0));
                            var textColorSpeaking = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
                            var textColorPassive = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.5f));
                            var textColorDeafened = this.Configuration.ColourDeafened;
                            var textColorMuted = this.Configuration.ColourMuted;
                            var textPadding = new Vector2(8, 2);
                            var muteIconImg = this.muteIcon.GetWrapOrDefault();
                            var deafenIconImg = this.deafenIcon.GetWrapOrDefault();

                            foreach (var user in this.Connection.AllUsers.Values) {
                                if (!knownUsers.Contains(user)
                                    && (user.Speaking.GetValueOrDefault(false)
                                        || this.Configuration.ShowNonXivUsersAlways)) {
                                    var size = ImGui.CalcTextSize(user.DisplayName);
                                    var width = size.WithY(0);
                                    var imgSize = size.WithX(size.Y);
                                    var midPoint = position.WithX(position.X + (170 * partyAddon->AtkUnitBase.Scale));
                                    var rightEdge = midPoint.WithX(midPoint.X + (80 * partyAddon->AtkUnitBase.Scale));
                                    drawList.AddRectFilled(
                                        position,
                                        midPoint.WithY(midPoint.Y + size.Y + 4),
                                        leftColor
                                    );
                                    drawList.AddRectFilledMultiColor(
                                        midPoint,
                                        rightEdge.WithY(rightEdge.Y + size.Y + 4),
                                        leftColor,
                                        rightColor,
                                        rightColor,
                                        leftColor
                                    );
                                    var textColor = textColorPassive;

                                    if (user.Deafened.GetValueOrDefault()) {
                                        textColor = textColorDeafened;
                                    } else if (user.Muted.GetValueOrDefault()) {
                                        textColor = textColorMuted;
                                    } else if (user.Speaking.GetValueOrDefault(false)) {
                                        textColor = textColorSpeaking;
                                    }

                                    if (user.Deafened.GetValueOrDefault() && deafenIconImg is not null) {
                                        var start = position + textPadding + width + new Vector2(3, 0);
                                        drawList.AddImage(
                                            deafenIconImg.Handle,
                                            start,
                                            start + imgSize
                                        );
                                    } else if (user.Muted.GetValueOrDefault() && muteIconImg is not null) {
                                        var start = position + textPadding + width + new Vector2(3, 0);
                                        drawList.AddImage(
                                            muteIconImg.Handle,
                                            start,
                                            start + imgSize
                                        );
                                    }

                                    drawList.AddText(position + textPadding, textColor, user.DisplayName);
                                    position.Y += size.Y + 5;
                                }
                            }

                            if (this.ConfigWindow.IsOpen) {
                                foreach (var s in new[]
                                    { "其他人说话时会显示在这里…", "…（当前无人说话）" }) {
                                    var size = ImGui.CalcTextSize(s);
                                    var midPoint = position.WithX(position.X + (170 * partyAddon->AtkUnitBase.Scale));
                                    var rightEdge = midPoint.WithX(midPoint.X + (80 * partyAddon->AtkUnitBase.Scale));
                                    drawList.AddRectFilled(
                                        position,
                                        midPoint.WithY(midPoint.Y + size.Y + 4),
                                        leftColor
                                    );
                                    drawList.AddRectFilledMultiColor(
                                        midPoint,
                                        rightEdge.WithY(rightEdge.Y + size.Y + 4),
                                        leftColor,
                                        rightColor,
                                        rightColor,
                                        leftColor
                                    );
                                    drawList.AddText(position + textPadding, textColorSpeaking, s);
                                    position.Y += size.Y + 5;
                                }
                            }
                        }
                    }
                }
            } finally {
                this.validSlots = validSlots;
                ImGui.PopClipRect();
                ImGui.End();
            }
        }
    }

    /// <summary>
    ///     把游戏内角色名匹配到 oopz 成员。
    ///     优先完全一致；其次名字包含关系（第一/最后一个字也行）；最后查手动绑定表。
    /// </summary>
    public User? XivToOopz(string name, string? world = null) {
        if (name == this.PlayerState.CharacterName) {
            var self = this.Connection.Self;
            if (self != null) {
                return self;
            }

            // self 在 oopz 里的昵称不一定等于角色名，继续走下面的匹配
        }

        // 手动绑定：角色名 -> oopz 成员名
        foreach (var user in this.Connection.AllUsers.Values) {
            var oopzName = user.DisplayName.IsNullOrEmpty() ? user.Username : user.DisplayName;
            if (oopzName == null) {
                continue;
            }

            foreach (var individualEntry in this.Configuration.IndividualAssignments) {
                if (individualEntry.CharacterName == name && individualEntry.OopzName == oopzName) {
                    return user;
                }
            }
        }

        foreach (var user in this.Connection.AllUsers.Values) {
            var oopzName = user.DisplayName.IsNullOrEmpty() ? user.Username : user.DisplayName;
            if (oopzName == null) {
                continue;
            }

            if (oopzName == name || oopzName.ToLowerInvariant().Contains(name.ToLowerInvariant())) {
                return user;
            }

            var split = name.Split(' ');
            if (split.Length != 2) {
                // e.g. your chocobo (and also just "everything probably" when ClientStructs is out of date post-patch)
                return null;
            }

            oopzName = oopzName.ToLowerInvariant();
            if (oopzName.Contains(split[0].ToLowerInvariant())
                || oopzName.Contains(split[1].ToLowerInvariant())) {
                return user;
            }
        }

        return null;
    }

    private unsafe Vector2? GetNonXivUsersPos(AddonPartyList* partyAddon) {
        switch (this.Configuration.NonXivUsersDisplayMode) {
            case NonXivUsersDisplayMode.Off:
                return null;
            case NonXivUsersDisplayMode.BelowPartyList: {
                Vector2? pos = null;
                var lastGoodNode = (AtkComponentNode*)null;

                AddonPartyList.PartyListMemberStruct[] members =
                    [..partyAddon->PartyMembers, ..partyAddon->TrustMembers, partyAddon->Chocobo, partyAddon->Pet];

                foreach (var member in members) {
                    if (member.PartyMemberComponent == null) {
                        continue;
                    }

                    var node = member.PartyMemberComponent->OwnerNode;
                    if (node == null) {
                        continue;
                    }

                    if (node->IsVisible == null) {
                        this.PluginLog.Warning($"what. {node->NodeId}");
                        continue;
                    }

                    if (!node->IsVisible()) {
                        continue;
                    }

                    lastGoodNode = node;
                    var nodePos = GetNodePosition(node);
                    if (pos == null || nodePos.Y > pos.Value.Y) {
                        pos = nodePos;
                    }
                }

                if (lastGoodNode != null) {
                    var position = pos ?? new Vector2(0, 0);
                    position.X += 27 * partyAddon->AtkUnitBase.Scale;
                    position.Y += (lastGoodNode->Height - 10) * partyAddon->AtkUnitBase.Scale;
                    return position;
                }

                return null;
            }
            case NonXivUsersDisplayMode.ManuallyPositioned:
                return new Vector2(this.Configuration.NonXivUsersX, this.Configuration.NonXivUsersY);
            default:
                throw new ArgumentOutOfRangeException(
                    null,
                    $"unknown config value for NonXivUsersDisplayMode: {this.Configuration.NonXivUsersDisplayMode}"
                );
        }
    }
}