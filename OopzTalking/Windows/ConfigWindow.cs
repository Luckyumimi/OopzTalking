using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace OopzTalking.Windows;

public sealed class ConfigWindow: Window, IDisposable {
    private readonly List<AssignmentEntry> individualAssignments;
    private readonly Plugin plugin;
    private readonly ISharedImmediateTexture previewImage;

    private string? overlayResetMsg;

    public ConfigWindow(Plugin plugin): base("Oopz Talking 设置") {
        this.plugin = plugin;
        this.individualAssignments = new List<AssignmentEntry>();
        this.ResetListToConfig();

        this.SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(700, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        // Image for preview
        var path = Path.Combine(this.plugin.PluginInterface.AssemblyLocation.Directory?.FullName!, "images");
        path = Path.Combine(path, "previewImage.png");
        this.previewImage = this.plugin.TextureProvider.GetFromFile(path);
    }

    public void Dispose() {}

    public override void Draw() {
        ImGui.Text($"感谢{(this.plugin.PluginInterface.IsTesting ? "测试" : "使用")} Oopz Talking！");

        // 大红字提醒：必须打开 oopz 的屏幕覆盖才能收到语音数据
        var warnText = "⚠ 请打开 oopz 的「屏幕覆盖」！";
        var warnFontSize = ImGui.GetFontSize() * 1.6f;
        var warnColor = ImGui.GetColorU32(new Vector4(1f, 0.2f, 0.2f, 1f));
        var warnPos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList()
            .AddText(ImGui.GetFont(), warnFontSize, warnPos, warnColor, warnText);
        ImGui.Dummy(new Vector2(ImGui.CalcTextSize(warnText).X * 1.6f, ImGui.GetFontSize() * 1.6f));
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(
                "oopz 需要在「设置 → 屏幕覆盖」里开启覆盖功能，"
                + "\n否则 oopz 不会推送语音房间数据，插件收不到谁在说话。"
            );
        }

        ImGui.Separator();
        ImGui.Text("状态：");
        ImGui.SameLine();
        if (this.plugin.Connection.IsConnected) {
            var memberCount = this.plugin.Connection.AllUsers.Count;
            if (memberCount > 0) {
                var names = string.Join("、", this.plugin.Connection.AllUsers.Keys);
                ImGui.TextColored(ImGuiColors.HealerGreen, $"已连接 oopz，语音房间 {memberCount} 人：{names}");
            } else {
                ImGui.TextColored(
                    ImGuiColors.DalamudYellow,
                    "已连接 oopz，但当前不在语音房间（进入 oopz 语音房间后自动显示）"
                );
            }
        } else {
            ImGui.TextColored(ImGuiColors.DalamudRed, "未连接 oopz。请先启动 oopz 并进入语音房间！");
        }

        ImGui.Separator();
        var showIndicators = this.plugin.Configuration.ShowIndicators;
        if (ImGui.Checkbox("显示语音活动指示灯（DelvUI 用户请关闭此项！）", ref showIndicators)) {
            this.plugin.Configuration.ShowIndicators = showIndicators;
            this.plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(
                "插件无法直接得知你是否隐藏了小队列表。"
                + "\n如果你使用 DelvUI（或其他完全替换小队列表的插件），"
                + "\n可以关闭此项以隐藏原生小队列表上的语音指示灯。"
                + "\nDelvUI 内的指示灯不受影响。"
            );
        }

        var indicatorStyle = (int)this.plugin.Configuration.IndicatorStyle;
        var indicatorStyles = Enum.GetValues(typeof(IndicatorStyle))
            .Cast<IndicatorStyle>()
            .Select(val => val.ToString())
            .ToArray();
        // 枚举值转中文显示名
        var indicatorStyleLabels = new[]
            { "ImGui 覆盖绘制", "游戏内 UI (Atk) 实验" };
        ImGui.AlignTextToFramePadding();
        ImGui.Text("指示灯样式：");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        var indicatorStyleLabel = indicatorStyle < indicatorStyleLabels.Length
            ? indicatorStyleLabels[indicatorStyle]
            : indicatorStyle.ToString();
        if (ImGui.BeginCombo("###indicatorStyle", indicatorStyleLabel.ToString())) {
            for (var i = 0; i < indicatorStyles.Length; i++) {
                if (ImGui.Selectable(indicatorStyleLabels[i], indicatorStyle == i)) {
                    indicatorStyle = i;
                    this.plugin.Configuration.IndicatorStyle = (IndicatorStyle)indicatorStyle;
                    this.plugin.Configuration.Save();
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(
                "\"Atk\" 模式使用游戏自身的 UI 框架绘制，属于实验功能！"
                + "\n如有问题请反馈。"
            );
        }

        var useRoundedCorners = this.plugin.Configuration.UseRoundedCorners;
        if (this.plugin.Configuration.IndicatorStyle == IndicatorStyle.Imgui
            && ImGui.Checkbox(
                "指示灯使用圆角（Material UI/Frost UI 用户请关闭此项！）",
                ref useRoundedCorners
            )) {
            this.plugin.Configuration.UseRoundedCorners = useRoundedCorners;
            this.plugin.Configuration.Save();
        }

        var nonXivUsersDisplayMode = (int)this.plugin.Configuration.NonXivUsersDisplayMode;
        var nonXivUsersDisplayModes = Enum.GetValues(typeof(NonXivUsersDisplayMode))
            .Cast<NonXivUsersDisplayMode>()
            .Select(val => val.ToString())
            .ToArray();
        // 枚举值转中文显示名
        var nonXivModeLabels = new[]
            { "关闭", "显示在小队列表下方", "手动定位" };
        ImGui.AlignTextToFramePadding();
        ImGui.Text("显示不在小队中的 oopz 在线用户：");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        var nonXivModeLabel = nonXivUsersDisplayMode < nonXivModeLabels.Length
            ? nonXivModeLabels[nonXivUsersDisplayMode]
            : nonXivUsersDisplayMode.ToString();
        if (ImGui.BeginCombo("###nonXivUsersDisplayMode", nonXivModeLabel.ToString())) {
            for (var i = 0; i < nonXivUsersDisplayModes.Length; i++) {
                if (ImGui.Selectable(nonXivModeLabels[i], nonXivUsersDisplayMode == i)) {
                    nonXivUsersDisplayMode = i;
                    this.plugin.Configuration.NonXivUsersDisplayMode = (NonXivUsersDisplayMode)nonXivUsersDisplayMode;
                    this.plugin.Configuration.Save();
                }
            }

            ImGui.EndCombo();
        }

        if (this.plugin.Configuration.NonXivUsersDisplayMode == NonXivUsersDisplayMode.ManuallyPositioned) {
            var nonXivUsersX = this.plugin.Configuration.NonXivUsersX;
            var nonXivUsersY = this.plugin.Configuration.NonXivUsersY;
            ImGui.Text("位置：");
            ImGui.SameLine();
            if (ImGui.DragInt("###nonXivUsersX", ref nonXivUsersX)) {
                this.plugin.Configuration.NonXivUsersX = nonXivUsersX;
                this.plugin.Configuration.Save();
            }

            ImGui.SameLine();
            if (ImGui.DragInt("###nonXivUsersY", ref nonXivUsersY)) {
                this.plugin.Configuration.NonXivUsersY = nonXivUsersY;
                this.plugin.Configuration.Save();
            }
        }

        var showNonXivUsersAlways = this.plugin.Configuration.ShowNonXivUsersAlways;
        if (ImGui.Checkbox(
                "始终显示所有不在小队中的 oopz 用户（无论是否在说话）",
                ref showNonXivUsersAlways
            )) {
            this.plugin.Configuration.ShowNonXivUsersAlways = showNonXivUsersAlways;
            this.plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered() && !showIndicators) {
            ImGui.SetTooltip(
                "由于“显示语音活动指示灯”已关闭，"
                + "\n此项设置不会生效。"
            );
        }

        var showUnmatchedUsers = this.plugin.Configuration.ShowUnmatchedUsers;
        if (ImGui.Checkbox("为未匹配到名字的用户显示方框", ref showUnmatchedUsers)) {
            this.plugin.Configuration.ShowUnmatchedUsers = showUnmatchedUsers;
            this.plugin.Configuration.Save();
        }

        ImGui.SetNextItemWidth(150);
        var oopzPort = this.plugin.Configuration.Port;
        if (ImGui.InputInt("oopz 端口", ref oopzPort)) {
            this.plugin.Configuration.Port = oopzPort;
            this.plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(
                "oopz 本地 WebSocket 端口，默认 10274。"
                + "一般不用改，除非被其他程序占用。"
            );
        }

        if (ImGui.Button("重新连接")) {
            this.plugin.ReconnectOopz();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("尝试重新连接 oopz。");
        }

        ImGui.SameLine();
        if (this.plugin.Connection.IsConnected) {
            ImGui.TextColored(ImGuiColors.HealerGreen, $"已连接 oopz（端口 {this.plugin.Configuration.Port}）");
        } else {
            ImGui.TextColored(ImGuiColors.DalamudRed, "连接失败");
        }

        if (ImGui.IsItemHovered() && !showIndicators) {
            ImGui.SetTooltip(
                "由于“显示语音活动指示灯”已关闭，"
                + "\n此项设置不会生效。"
            );
        }

        ImGui.Separator();
        ImGui.Text("屏幕覆盖：");
        if (ImGui.Button("重置屏幕覆盖位置")) {
            var error = OverlayReset.Reset();
            this.overlayResetMsg = error == null ? "已把覆盖重置回屏幕中央。" : error;
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("把 oopz 的屏幕覆盖窗口重置回屏幕中央（覆盖跑偏/丢失时用）。");
        }

        ImGui.SameLine();
        if (ImGui.Button(OverlayReset.IsHidden() ? "显示屏幕覆盖" : "隐藏屏幕覆盖")) {
            var error = OverlayReset.IsHidden() ? null : OverlayReset.Hide();
            var successMsg = OverlayReset.IsHidden() ? "已把覆盖窗口移回屏幕中央。" : "已隐藏屏幕覆盖。";
            if (OverlayReset.IsHidden() && error == null) {
                // 重新显示：即把覆盖重置回屏幕中央
                error = OverlayReset.Reset();
            }

            this.overlayResetMsg = error == null ? successMsg : error;
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(
                OverlayReset.IsHidden()
                    ? "把覆盖窗口移回屏幕中央显示。"
                    : "把覆盖窗口丢到屏幕外隐藏（不想看覆盖时用）。"
            );
        }

        if (OverlayReset.IsHidden()) {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "覆盖当前已隐藏。");
        }

        if (this.overlayResetMsg != null) {
            ImGui.TextColored(
                this.overlayResetMsg.StartsWith("已") ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed,
                this.overlayResetMsg
            );
        }

        if (this.plugin.PluginInterface.IsDev) {
            ImGui.Separator();

            ImGui.AlignTextToFramePadding();
            ImGui.Text("开发模式下这里会显示调试信息。");
            ImGui.SameLine();
            if (ImGui.Button("打开调试窗口")) {
                this.plugin.MainWindow.IsOpen = true;
            }
        }

        // Colour Assignments
        ImGui.Separator();
        if (ImGui.TreeNode("颜色设置")) {
            var colspk = this.plugin.Configuration.ColourSpeaking;
            if (this.ColourConfig("正在说话", colspk, ref colspk)) {
                this.plugin.Configuration.ColourSpeaking = colspk;
                this.plugin.Configuration.Save();
            }

            var colmuted = this.plugin.Configuration.ColourMuted;
            if (this.ColourConfig("静音", colmuted, ref colmuted)) {
                this.plugin.Configuration.ColourMuted = colmuted;
                this.plugin.Configuration.Save();
            }

            var coldeafened = this.plugin.Configuration.ColourDeafened;
            if (this.ColourConfig("聋", coldeafened, ref coldeafened)) {
                this.plugin.Configuration.ColourDeafened = coldeafened;
                this.plugin.Configuration.Save();
            }

            if (this.plugin.Configuration.ShowUnmatchedUsers) {
                var colunm = this.plugin.Configuration.ColourUnmatched;
                if (this.ColourConfig("未匹配", colunm, ref colunm)) {
                    this.plugin.Configuration.ColourUnmatched = colunm;
                    this.plugin.Configuration.Save();
                }
            }

            ImGui.TreePop();
        }

        ImGui.Separator();

        if (ImGui.TreeNode("高级手动绑定")) {
            ImGui.BulletText(
                "将游戏角色名手动绑定到 oopz 成员名。"
                + Environment.NewLine
                + "oopz 成员名就是语音房间里显示的名字，一般不用手动绑定——"
                + "只要 oopz 昵称包含角色名（名或姓一部分）就会自动匹配。"
                + Environment.NewLine
                + "下拉选择后立即生效并自动保存。"
            );
            if (ImGui.BeginTable("AssignmentTable", 3)) {
                ImGui.TableSetupColumn("角色名");
                ImGui.TableSetupColumn("oopz 成员名");
                ImGui.TableSetupColumn("操作");
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();

                // 收集候选：
                // 1) 小队成员名字（含跨服）作为角色名候选
                var partyNames = new List<string>();
                foreach (var member in this.plugin.PartyList) {
                    partyNames.Add(member.Name.TextValue);
                }

                // 2) oopz 房间成员名（显示名优先）作为 oopz 候选
                var oopzMemberNames = new List<string>();
                foreach (var user in this.plugin.Connection.AllUsers.Values) {
                    var displayName = user.DisplayName.IsNullOrEmpty() ? user.Username : user.DisplayName;
                    if (!displayName.IsNullOrEmpty() && !oopzMemberNames.Contains(displayName)) {
                        oopzMemberNames.Add(displayName);
                    }
                }

                var removedIndices = new List<int>();
                for (var i = 0; i < this.individualAssignments.Count; i++) {
                    var entry = this.individualAssignments[i];

                    // 角色名下拉：候选 = 现有已绑定角色名 + 当前小队成员（去重）
                    ImGui.TableNextColumn();
                    var charaOptions = new List<string>();
                    foreach (var existing in this.individualAssignments) {
                        if (!string.IsNullOrEmpty(existing.CharacterName)
                            && !charaOptions.Contains(existing.CharacterName)) {
                            charaOptions.Add(existing.CharacterName);
                        }
                    }

                    foreach (var name in partyNames) {
                        if (!charaOptions.Contains(name)) {
                            charaOptions.Add(name);
                        }
                    }

                    ImGui.SetNextItemWidth(150);
                    if (ImGui.BeginCombo($"###nameEntry{i}", entry.CharacterName)) {
                        for (var ci = 0; ci < charaOptions.Count; ci++) {
                            if (ImGui.Selectable(charaOptions[ci], charaOptions[ci] == entry.CharacterName)) {
                                entry.CharacterName = charaOptions[ci];
                                this.individualAssignments[i] = entry;
                                this.SaveAssignmentsNow();
                            }
                        }

                        ImGui.EndCombo();
                    }

                    // oopz 成员名下拉：候选 = 当前 oopz 房间成员
                    ImGui.TableNextColumn();
                    var oopzOptions = new List<string>();
                    foreach (var name in oopzMemberNames) {
                        if (!string.IsNullOrEmpty(name) && !oopzOptions.Contains(name)) {
                            oopzOptions.Add(name);
                        }
                    }

                    ImGui.SetNextItemWidth(200);
                    if (ImGui.BeginCombo($"###oopzNameEntry{i}", entry.OopzName)) {
                        for (var ci = 0; ci < oopzOptions.Count; ci++) {
                            if (ImGui.Selectable(oopzOptions[ci], oopzOptions[ci] == entry.OopzName)) {
                                entry.OopzName = oopzOptions[ci];
                                this.individualAssignments[i] = entry;
                                this.SaveAssignmentsNow();
                            }
                        }

                        ImGui.EndCombo();
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button("删除###deleteEntry" + i)) {
                        removedIndices.Add(i);
                    }
                }

                foreach (var index in removedIndices.OrderByDescending(x => x)) {
                    this.individualAssignments.RemoveAt(index);
                }

                if (removedIndices.Count > 0) {
                    this.SaveAssignmentsNow();
                }

                ImGui.EndTable();
            }

            // 添加：新增一行空白绑定，两个下拉都选好后自动保存
            if (ImGui.Button("添加")) {
                this.individualAssignments.Add(new AssignmentEntry());
            }

            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip("新增一行空白绑定，选好两个下拉后立即生效并自动保存。");
            }

            ImGui.SameLine();

            if (ImGui.Button("重置")) {
                this.ResetListToConfig();
            }

            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.Text("恢复到上次保存的状态");
                ImGui.EndTooltip();
            }

            ImGui.TreePop();
        }
    }

    // 即时保存：把当前内部列表整份写回配置。
    private void SaveAssignmentsNow() {
        this.plugin.Configuration.IndividualAssignments.Clear();
        foreach (var entry in this.individualAssignments) {
            if (string.IsNullOrEmpty(entry.CharacterName) && string.IsNullOrEmpty(entry.OopzName)) {
                // 跳过空行
                continue;
            }

            this.plugin.Configuration.IndividualAssignments.Add(
                new AssignmentEntry {
                    CharacterName = entry.CharacterName,
                    OopzName = entry.OopzName,
                }
            );
        }

        this.plugin.Configuration.Save();
    }

    // Represents one colour for colour configuation
    private bool ColourConfig(string label, uint colour, ref uint rcolour) {
        ImGui.Text(label);

        var newcol = ImGui.ColorConvertU32ToFloat4(colour);
        var r = ImGui.ColorEdit4("###ColourEdit_{0}".Format(label), ref newcol);
        if (r) {
            rcolour = ImGui.ColorConvertFloat4ToU32(newcol);
        }

        ImGui.SameLine();
        ImGui.Text("预览");

        // Draws a preview of what the outline will look like
        ImGui.SameLine();
        var scroll = new Vector2(ImGui.GetScrollX(), ImGui.GetScrollY());
        var preview_min = ImGui.GetWindowPos() + ImGui.GetCursorPos() - scroll;
        var preview_max = preview_min + new Vector2(25, 25);
        var img = this.previewImage.GetWrapOrDefault();
        if (img is not null) {
            ImGui.GetWindowDrawList()
                .AddImage(
                    img.Handle,
                    preview_min,
                    preview_max
                );
        }

        ImGui.GetWindowDrawList()
            .AddRect(
                preview_min,
                preview_max,
                ImGui.ColorConvertFloat4ToU32(newcol),
                7,
                ImDrawFlags.RoundCornersAll,
                2
            );
        ImGui.Text("");

        return r;
    }

    private void ResetListToConfig() {
        this.individualAssignments.Clear();
        foreach (var entry in this.plugin.Configuration.IndividualAssignments) {
            this.individualAssignments.Add(
                new AssignmentEntry {
                    CharacterName = entry.CharacterName,
                    OopzName = entry.OopzName,
                }
            );
        }
    }
}