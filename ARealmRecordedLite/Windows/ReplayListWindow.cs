using System;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using ARealmRecordedLite.Managers;
using ARealmRecordedLite.Utilities;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Config;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace ARealmRecordedLite.Windows;

public unsafe partial class ReplayListWindow : Window
{
    private const ImGuiWindowFlags FlagsWindow = ImGuiWindowFlags.NoDecoration    | ImGuiWindowFlags.NoMove |
                                                 ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar;

    public static AtkUnitBase* ContentsReplaySetting => GetAddonByName("ContentsReplaySetting");
    
    public ReplayListWindow() : base("ReplayListWindow##DailyRoutines", FlagsWindow)
    {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "ContentsReplaySetting", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "ContentsReplaySetting", OnAddon);
        if (IsAddonAndNodesReady(ContentsReplaySetting)) OnAddon(AddonEvent.PostSetup, null);
    }

    private static bool   NeedSort      = true;
    private static int    EditingReplay = -1;
    private static string EditingName   = string.Empty;
    private static bool   ShowPluginSettings;

    private static readonly Regex DisplayNameRegex = MyRegex();

    public override void Draw()
    {
        var addon = ContentsReplaySetting;
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.ContentsReplaySetting);

        if (agent == null || addon == null)
        {
            IsOpen = false;
            return;
        }

        if (!addon->IsVisible || (addon->Flags198 & 512) == 0) return;

        var addonW = addon->RootNode->GetWidth() * addon->Scale;
        ImGui.SetWindowPos(new Vector2(addon->X + addonW, addon->Y) + ImGuiHelpers.MainViewport.Pos);
        ImGui.SetWindowSize(new(500f * ImGuiHelpers.GlobalScale, addon->GetScaledHeight(true)));

        if (ImGui.Button(FontAwesomeIcon.SyncAlt.ToIconString()))
        {
            ReplayFileManager.GetReplayList();
            NeedSort = true;
        }
        ImGuiOm.TooltipHover("重新排序");

        ImGui.SameLine();
        if (ImGui.Button(FontAwesomeIcon.FolderOpen.ToIconString()))
            ReplayFileManager.OpenReplayFolder();
        ImGuiOm.TooltipHover("打开回放文件目录");

        ImGui.SameLine();
        if (ImGui.Button(FontAwesomeIcon.FileArchive.ToIconString()))
        {
            ReplayFileManager.ArchiveReplays();
            NeedSort = true;
        }

        ImGuiOm.TooltipHover("存档不可播放的录像");

        ImGui.SameLine();
        if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString()))
            ShowPluginSettings ^= true;

        if (Service.GameConfig.UiConfig.TryGetBool(nameof(UiConfigOption.ContentsReplayEnable), out var b) && !b)
        {
            ImGui.SameLine();
            if (ImGui.Button("任务记录禁用中, 点击开启"))
                Service.GameConfig.UiConfig.Set(nameof(UiConfigOption.ContentsReplayEnable), true);
        }

        if (!ShowPluginSettings)
            DrawReplaysTable();
        else
            DrawSetting();
    }

    public static void DrawReplaysTable()
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.ContentsReplaySetting);

        using var table = ImRaii.Table("ReplaysTable", 2, ImGuiTableFlags.Sortable     | ImGuiTableFlags.BordersInnerV  |
                                                          ImGuiTableFlags.BordersOuter | ImGuiTableFlags.SizingFixedFit |
                                                          ImGuiTableFlags.ScrollY);
        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);

        ImGui.TableSetupColumn("日期", ImGuiTableColumnFlags.PreferSortDescending);
        ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableHeadersRow();

        var sortspecs = ImGui.TableGetSortSpecs();
        if (sortspecs.SpecsDirty || NeedSort || ImGui.IsWindowAppearing())
        {
            if (sortspecs.Specs.ColumnIndex == 0)
                ReplayFileManager.ReplayList = sortspecs.Specs.SortDirection == ImGuiSortDirection.Ascending
                                      ?
                                      [
                                          .. ReplayFileManager.ReplayList.OrderByDescending(t => t.Item2.ReplayHeader.IsPlayable)
                                                 .ThenBy(t => t.Item2.ReplayHeader.Timestamp)
                                      ]
                                      :
                                      [
                                          .. ReplayFileManager.ReplayList.OrderByDescending(t => t.Item2.ReplayHeader.IsPlayable)
                                                 .ThenByDescending(t => t.Item2.ReplayHeader.Timestamp)
                                      ];
            else
                ReplayFileManager.ReplayList = sortspecs.Specs.SortDirection == ImGuiSortDirection.Ascending
                                      ? [.. ReplayFileManager.ReplayList.OrderByDescending(t => t.Item2.ReplayHeader.IsPlayable).ThenBy(t => t.Item1.Name)]
                                      : [.. ReplayFileManager.ReplayList.OrderByDescending(t => t.Item2.ReplayHeader.IsPlayable).ThenByDescending(t => t.Item1.Name)];

            sortspecs.SpecsDirty = false;
            NeedSort             = false;
        }

        for (var i = 0; i < ReplayFileManager.ReplayList.Count; i++)
        {
            var (file, replay) = ReplayFileManager.ReplayList[i];
            var header      = replay.ReplayHeader;
            var path        = file.FullName;
            var fileName    = file.Name;
            var displayName = DisplayNameRegex.Match(fileName) is { Success: true } match ? match.Groups[1].Value : fileName[..fileName.LastIndexOf('.')];
            var isPlayable  = replay.ReplayHeader.IsPlayable;
            var autoRenamed = file.Directory?.Name == "autorenamed";

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            using (ImRaii.Disabled(!isPlayable))
                ImGui.TextUnformatted(DateTimeOffset.FromUnixTimeSeconds(header.Timestamp).LocalDateTime.ToString("g"));

            ImGui.TableNextColumn();
            if (EditingReplay != i)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled), !isPlayable))
                {
                    if (ImGui.Selectable(autoRenamed ? $"- {displayName}##{path}" : $"{displayName}##{path}",
                                         path == ReplayFileManager.LastSelectedReplay && *(byte*)((nint)agent + 0x2C) == 100,
                                         ImGuiSelectableFlags.SpanAllColumns))
                        ReplayFileManager.SetDutyRecorderMenuSelection((nint)agent, path, header);
                }

                if (replay.ReplayHeader.IsCurrentFormatVersion && ImGui.IsItemHovered())
                {
                    var (pulls, longestPull) = replay.GetPullInfo();

                    ImGui.BeginTooltip();

                    using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
                    {
                        ImGui.TextUnformatted($"任务: {header.ContentFinderCondition.Name.ToDalamudString()}");
                        if ((header.Info & 4) != 0)
                        {
                            ImGui.SameLine();
                            ImGui.TextUnformatted(" ");

                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0, 1, 0, 1), FontAwesomeIcon.Check.ToIconString());
                        }

                        var foundPlayer = false;
                        ImGui.TextUnformatted("小队:");
                        foreach (var row in header.ClassJobs.OrderBy(row => row.UIPriority))
                        {
                            if (!Service.Texture.TryGetFromGameIcon(new(62100 + row.RowId), out var texture)) continue;

                            ImGui.SameLine();
                            if (!foundPlayer && row.RowId == header.LocalPlayerClassJob.RowId)
                            {
                                ImGui.TextUnformatted($"  [ ");

                                ImGui.SameLine();
                                ImGui.Image(texture.GetWrapOrEmpty().ImGuiHandle, new(ImGui.GetTextLineHeightWithSpacing()));

                                ImGui.SameLine();
                                ImGui.TextUnformatted($" ]  ");

                                foundPlayer = true;
                            }
                            else { ImGui.Image(texture.GetWrapOrEmpty().ImGuiHandle, new(ImGui.GetTextLineHeight())); }
                        }

                        ImGui.TextUnformatted($"长度: {new TimeSpan(0, 0, 0, 0, (int)header.DisplayedMS):hh':'mm':'ss}");
                        if (pulls > 1)
                        {
                            ImGui.TextUnformatted($"重试次数: {pulls}");
                            ImGui.TextUnformatted($"单次最久: {longestPull:hh':'mm':'ss}");
                        }
                    }

                    ImGui.EndTooltip();
                }

                if (ImGui.BeginPopupContextItem())
                {
                    for (byte j = 0; j < 3; j++)
                    {
                        if (!ImGui.Selectable($"复制到第 {j + 1} 槽")) continue;
                        ReplayFileManager.CopyReplayIntoSlot((nint)agent, file, header, j);
                        NeedSort = true;
                    }

                    if (ImGui.Selectable("删除"))
                    {
                        ReplayFileManager.DeleteReplay(file);
                        NeedSort = true;
                    }

                    ImGui.EndPopup();
                }

                if (!ImGui.IsItemHovered() || !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) continue;

                EditingReplay = i;
                EditingName   = fileName[..fileName.LastIndexOf('.')];
            }
            else
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputText("##SetName", ref EditingName, 64, ImGuiInputTextFlags.AutoSelectAll);

                if (ImGui.IsWindowFocused() && !ImGui.IsAnyItemActive())
                    ImGui.SetKeyboardFocusHere(-1);

                if (!ImGui.IsItemDeactivated()) continue;

                EditingReplay = -1;

                if (ImGui.IsItemDeactivatedAfterEdit())
                    ReplayFileManager.RenameReplay(file, EditingName);
            }
        }
    }

    public static void DrawSetting()
    {
        var save = false;

        save |= ImGui.Checkbox("在服务器信息栏显示录像图标", ref Service.Config.EnableRecordingIcon);
        save |= ImGui.InputInt("最大自动保存文件数", ref Service.Config.MaxAutoRenamedReplays);
        save |= ImGui.InputInt("最大回收站文件数",  ref Service.Config.MaxDeletedReplays);

        if (save)
            Service.Config.Save();
    }

    private void OnAddon(AddonEvent type, AddonArgs? args)
    {
        IsOpen = type switch
        {
            AddonEvent.PostSetup   => true,
            AddonEvent.PreFinalize => false,
            _                      => IsOpen
        };
    }

    public void Dispose() => Service.AddonLifecycle.UnregisterListener(OnAddon);
    
    [GeneratedRegex("(.+)[ _]\\d{4}\\.")]
    private static partial Regex MyRegex();
}

