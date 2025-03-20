using ImGuiNET;

namespace ARealmRecordedLite.Utilities;

public static class ImGuiOm
{
    public static void TooltipHover(string text, float warpPos = 20f)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        ImGui.PushID($"{text}_{warpPos}");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * warpPos);
            ImGui.Text(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        ImGui.PopID();
    }
}
