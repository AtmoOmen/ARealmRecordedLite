using System;
using System.Numerics;
using ARealmRecordedLite.Managers;
using ARealmRecordedLite.Utilities;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace ARealmRecordedLite.Windows;

public unsafe class PlaybackControlWindow : Window
{
    private const ImGuiWindowFlags FlagsWindow = ImGuiWindowFlags.NoDecoration    | ImGuiWindowFlags.NoMove     |
                                                 ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar |
                                                 ImGuiWindowFlags.AlwaysAutoResize;

    public static AtkUnitBase* ContentsReplayPlayer => GetAddonByName("ContentsReplayPlayer");
    
    public PlaybackControlWindow() : base("PlaybackControlsWindow##DailyRoutines", FlagsWindow)
    {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "ContentsReplayPlayer", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,    "ContentsReplayPlayer", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "ContentsReplayPlayer", OnAddon);
        if (IsAddonAndNodesReady(ContentsReplayPlayer)) OnAddon(AddonEvent.PostSetup, null);
    }

    public static readonly float[] PresetSpeeds = [0.5f, 1, 2, 5, 10, 20];

    private static bool LoadingPlayback;
    private static bool LoadedPlayback = true;

    private static float LastSeek;

    public override void Draw()
    {
        var addon = ContentsReplayPlayer;
        if (!ContentsReplayModule.Instance()->InPlayback || addon == null)
        {
            IsOpen = false;

            LoadingPlayback = false;
            LoadedPlayback  = false;
            return;
        }

        if (ContentsReplayModule.Instance()->Seek != LastSeek || ContentsReplayModule.Instance()->IsPaused)
            LastSeek = ContentsReplayModule.Instance()->Seek;

        if (!LoadedPlayback)
        {
            if (ContentsReplayModule.Instance()->u0x708 != 0)
            {
                ImGui.Text("录像加载中...");
                LoadingPlayback = true;
                return;
            }

            LoadedPlayback = true;
            if (!Service.Config.EnableWaymarks) CoreManager.ToggleWaymarks();
            return;
        }

        if (!addon->IsVisible)
        {
            if (ImGui.Button(FontAwesomeIcon.Eye.ToIconString()))
                addon->Show(true, addon->ShowHideFlags);
            return;
        }

        var addonPadding = addon->Scale * 8;
        ImGui.SetWindowPos(new Vector2(addon->X, addon->Y) + new Vector2(addonPadding) - new Vector2(0, ImGui.GetWindowHeight()));

        using var tabBar = ImRaii.TabBar("###PlayerControlTabBar");
        if (!tabBar) return;

        using (var item = ImRaii.TabItem("控制"))
            if (item)
                DrawControl();

        using (var item = ImRaii.TabItem("设置"))
            if (item)
                DrawSetting();
    }

    private static void DrawControl()
    {
        if (ImGui.Button(FontAwesomeIcon.Users.ToIconString()))
            Framework.Instance()->GetUIModule()->EnterGPose();
        ImGuiOm.TooltipHover("进入集体动作");

        ImGui.SameLine();
        if (ImGui.Button(FontAwesomeIcon.Video.ToIconString()))
            Framework.Instance()->GetUIModule()->EnterIdleCam(0, Service.Targets.FocusTarget is { } focus ? focus.GameObjectId : 0xE0000000);
        ImGuiOm.TooltipHover("以当前焦点目标进入观景视角");

        ImGui.SameLine();
        var v = CoreManager.IsWaymarkVisible;
        if (ImGui.Button(v ? FontAwesomeIcon.ToggleOn.ToIconString() : FontAwesomeIcon.ToggleOff.ToIconString()))
        {
            CoreManager.ToggleWaymarks();
            Service.Config.EnableWaymarks ^= true;
            Service.Config.Save();
        }

        ImGuiOm.TooltipHover(v ? "隐藏场景标记" : "显示场景标记");

        ImGui.SameLine();
        ImGui.Button(FontAwesomeIcon.DoorOpen.ToIconString());
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ContentsReplayModule.Instance()->OverallDataOffset = long.MaxValue;
        ImGuiOm.TooltipHover("结束录像 (右键确认)");

        const int restartDelayMS     = 12_000;
        var       sliderWidth        = ImGui.GetContentRegionAvail().X;
        var       seekMS             = Math.Max((int)ContentsReplayModule.Instance()->Seek * 1000, (int)ContentsReplayModule.Instance()->chapters[0]->ms);
        var       lastStartChapterMS = ContentsReplayModule.Instance()->chapters[FFXIVReplay.ChapterArray.FindPreviousChapterType(2)]->ms;
        var       nextStartChapterMS = ContentsReplayModule.Instance()->chapters[FFXIVReplay.ChapterArray.FindNextChapterType(2)]->ms;
        if (lastStartChapterMS >= nextStartChapterMS)
            nextStartChapterMS = ContentsReplayModule.Instance()->replayHeader.totalMS;
        var currentTime = new TimeSpan(0, 0, 0, 0, (int)(seekMS - lastStartChapterMS));

        using (ImRaii.ItemWidth(sliderWidth))
        {
            using (ImRaii.Disabled(ContentsReplayModule.Instance()->IsLoadingChapter))
            using (ImRaii.PushStyle(ImGuiStyleVar.GrabMinSize, 4))
            {
                ImGui.SetNextItemWidth(250f * ImGuiHelpers.GlobalScale);
                ImGui.SliderInt($"##Time{lastStartChapterMS}", ref seekMS, (int)lastStartChapterMS, (int)nextStartChapterMS - restartDelayMS,
                                currentTime.ToString("hh':'mm':'ss"), ImGuiSliderFlags.NoInput);
            }

            if (ImGui.IsItemHovered())
            {
                var hoveredWidth   = ImGui.GetMousePos().X - ImGui.GetItemRectMin().X;
                var hoveredPercent = hoveredWidth / sliderWidth;
                if (hoveredPercent is >= 0.0f and <= 1.0f)
                {
                    var hoveredTime =
                        new TimeSpan(0, 0, 0, 0,
                                     (int)Math.Min(Math.Max((int)((nextStartChapterMS - lastStartChapterMS - restartDelayMS) * hoveredPercent), 0),
                                                   nextStartChapterMS - lastStartChapterMS));
                    ImGui.SetTooltip(hoveredTime.ToString("hh':'mm':'ss"));

                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        ReplayManager.SeekToTime((uint)hoveredTime.TotalMilliseconds + lastStartChapterMS);
                    else if (Service.Config.EnableJumpToTime && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                        ReplayManager.JumpToTime((uint)hoveredTime.TotalMilliseconds + lastStartChapterMS);
                }
            }

            var speed = ContentsReplayModule.Instance()->Speed;
            ImGui.SetNextItemWidth(250f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("##Speed", ref speed, 0.05f, 10.0f, "%.2fx", ImGuiSliderFlags.AlwaysClamp))
                ContentsReplayModule.Instance()->Speed = speed;
        }

        for (var i = 0; i < PresetSpeeds.Length; i++)
        {
            if (i != 0)
                ImGui.SameLine();

            var s = PresetSpeeds[i];
            if (ImGui.Button($"{s}x"))
                ContentsReplayModule.Instance()->Speed = s == ContentsReplayModule.Instance()->Speed ? 1 : s;
        }

        var customSpeed = Service.Config.CustomSpeedPreset;
        ImGui.SameLine(0, 4f * ImGuiHelpers.GlobalScale);
        if (ImGui.Button($"{customSpeed}x"))
            ContentsReplayModule.Instance()->Speed = customSpeed == ContentsReplayModule.Instance()->Speed ? 1 : customSpeed;
    }

    private static void DrawSetting()
    {
        var save = false;

        if (ImGui.Checkbox("隐藏自身名称 (需要重启录像)", ref Service.Config.EnableHideOwnName))
        {
            CoreManager.ReplaceLocalPlayerNamePatch.Toggle();
            save = true;
        }

        save |= ImGui.Checkbox("启用快捷章节跳转", ref Service.Config.EnableQuickLoad);

        save |= ImGui.Checkbox("启用右键精准跳转", ref Service.Config.EnableJumpToTime);
        ImGuiOm.TooltipHover("启用本项可能导致录像显示有误");

        ImGui.SameLine();
        ImGui.TextColored(new(1, 1, 0, 1), FontAwesomeIcon.ExclamationTriangle.ToIconString());

        ImGui.SetNextItemWidth(250f * ImGuiHelpers.GlobalScale);
        save |= ImGui.SliderFloat("加载速度", ref Service.Config.MaxSeekDelta, 100, 2000, "%.f%%");
        ImGuiOm.TooltipHover("修改本项可能会在部分场地存在切换的副本录像中导致问题");

        ImGui.SetNextItemWidth(250f * ImGuiHelpers.GlobalScale);
        save |= ImGui.SliderFloat("预设速度", ref Service.Config.CustomSpeedPreset, 0.05f, 60, "%.2fx", ImGuiSliderFlags.AlwaysClamp);

        if (save)
            Service.Config.Save();
    }

    private void OnAddon(AddonEvent type, AddonArgs? args)
    {
        IsOpen = type switch
        {
            AddonEvent.PostSetup   => true,
            AddonEvent.PostDraw    => true,
            AddonEvent.PreFinalize => false,
            _                      => IsOpen
        };
    }

    public void Dispose()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddon);
    }
}

