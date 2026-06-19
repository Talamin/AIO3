using System;
using System.Collections.Generic;
using System.Text;
using AIO3.Core.Settings;
using robotManager.Helpful;
using wManager.Wow.Helpers;

namespace AIO3.Overlay
{
    /// <summary>
    /// In-game settings overlay. Builds a movable WoW UI frame (real, clickable frames via Lua)
    /// whose contents are generated AUTOMATICALLY from a rotation's declared settings:
    /// an <see cref="IntSetting"/> renders as a value with [-]/[+] buttons, a <see cref="ToggleSetting"/>
    /// as a checkbox. Adding a setting to a rotation needs no UI code.
    ///
    /// Data flow:
    ///   C# -> Lua : seed AIO3Bridge[key] from each setting's value and build widgets.
    ///   Lua -> C# : widgets write AIO3Bridge[key]; Poll() reads it back into the Setting objects,
    ///               which the rotation reads live. Toggle with /aio3.
    /// </summary>
    internal sealed class SettingsOverlay
    {
        private readonly string _rotationName;
        private readonly IReadOnlyList<Setting> _settings;
        private bool _created;

        public SettingsOverlay(string rotationName, IReadOnlyList<Setting> settings)
        {
            _rotationName = rotationName;
            _settings = settings;
        }

        public void EnsureCreated()
        {
            if (_created || _settings.Count == 0) return;
            Lua.LuaDoString(BuildFrameLua());
            _created = true;
            Logging.Write("[AIO3] In-game settings overlay ready — type /aio3 to toggle.");
        }

        /// <summary>Mirror in-game edits into the settings. Returns true if anything changed.</summary>
        public bool Poll()
        {
            if (!_created) return false;
            bool changed = false;
            foreach (Setting s in _settings)
            {
                if (s is IntSetting i)
                {
                    int v = Lua.LuaDoString<int>(
                        $"if AIO3Bridge and AIO3Bridge['{i.Key}'] then return math.floor(AIO3Bridge['{i.Key}']) end return {i.Value}");
                    int clamped = System.Math.Max(i.Min, System.Math.Min(i.Max, v));
                    if (clamped != i.Value)
                    {
                        i.Value = clamped;
                        changed = true;
                        Logging.Write($"[AIO3] {i.Label} = {i.Value}");
                    }
                }
                else if (s is ToggleSetting t)
                {
                    bool v = Lua.LuaDoString<bool>(
                        $"if AIO3Bridge then return AIO3Bridge['{t.Key}'] == true end return {(t.Value ? "true" : "false")}");
                    if (v != t.Value)
                    {
                        t.Value = v;
                        changed = true;
                        Logging.Write($"[AIO3] {t.Label} = {t.Value}");
                    }
                }
                else if (s is ChoiceSetting c)
                {
                    string v = Lua.LuaDoString<string>(
                        $"if AIO3Bridge and AIO3Bridge['{c.Key}'] then return tostring(AIO3Bridge['{c.Key}']) end return ''");
                    if (!string.IsNullOrEmpty(v) && v != c.Value && System.Array.IndexOf(c.Options, v) >= 0)
                    {
                        c.Value = v;
                        changed = true;
                        Logging.Write($"[AIO3] {c.Label} = {c.Value}");
                    }
                }
            }
            return changed;
        }

        private string BuildFrameLua()
        {
            int rows = _settings.Count;
            int height = 50 + rows * 28 + 16;

            var sb = new StringBuilder();
            // Rebuild fresh every load: WoW UI frames survive a WRobot product restart, so a stale
            // panel would otherwise persist (and not pick up new settings) until the user typed /reload.
            sb.Append("if AIO3Frame then AIO3Frame:Hide() end\n");
            sb.Append("AIO3Bridge = AIO3Bridge or {}\n");
            sb.Append("AIO3Frame = CreateFrame(\"Frame\", \"AIO3Frame\", UIParent)\n");
            sb.Append("AIO3Frame:SetWidth(300) AIO3Frame:SetHeight(").Append(height).Append(")\n");
            sb.Append("AIO3Frame:SetPoint(\"CENTER\", 0, 220)\n");
            sb.Append("AIO3Frame:SetBackdrop(StaticPopup1:GetBackdrop())\n");
            sb.Append("AIO3Frame:SetMovable(true) AIO3Frame:EnableMouse(true)\n");
            sb.Append("AIO3Frame:SetScript(\"OnMouseDown\", function() AIO3Frame:StartMoving() end)\n");
            sb.Append("AIO3Frame:SetScript(\"OnMouseUp\", function() AIO3Frame:StopMovingOrSizing() end)\n");
            sb.Append("local title = AIO3Frame:CreateFontString(nil, \"OVERLAY\", \"GameFontNormalLarge\")\n");
            sb.Append("title:SetPoint(\"TOP\", 0, -12)\n");
            sb.Append("title:SetText(\"AIO3 - ").Append(Escape(_rotationName)).Append("\")\n");
            sb.Append("local close = CreateFrame(\"Button\", nil, AIO3Frame, \"UIPanelCloseButton\")\n");
            sb.Append("close:SetPoint(\"TOPRIGHT\", -4, -4)\n");

            for (int idx = 0; idx < rows; idx++)
            {
                Setting s = _settings[idx];
                int y = -(46 + idx * 28);
                string key = Escape(s.Key);
                string label = Escape(s.Label);

                if (s is IntSetting i)
                {
                    sb.Append("do local key=\"").Append(key).Append("\"\n");
                    sb.Append("if AIO3Bridge[key]==nil then AIO3Bridge[key]=").Append(i.Value).Append(" end\n");
                    sb.Append("local fs=AIO3Frame:CreateFontString(nil,\"OVERLAY\",\"GameFontNormal\")\n");
                    sb.Append("fs:SetPoint(\"TOPLEFT\",16,").Append(y).Append(")\n");
                    sb.Append("local function refresh() fs:SetText(\"").Append(label).Append(": \"..AIO3Bridge[key]) end refresh()\n");
                    sb.Append("local minus=CreateFrame(\"Button\",nil,AIO3Frame,\"UIPanelButtonTemplate\") minus:SetWidth(24) minus:SetHeight(20) minus:SetPoint(\"TOPRIGHT\",-48,").Append(y + 2).Append(") minus:SetText(\"-\")\n");
                    sb.Append("minus:SetScript(\"OnClick\",function() AIO3Bridge[key]=math.max(").Append(i.Min).Append(",AIO3Bridge[key]-").Append(i.Step).Append(") refresh() end)\n");
                    sb.Append("local plus=CreateFrame(\"Button\",nil,AIO3Frame,\"UIPanelButtonTemplate\") plus:SetWidth(24) plus:SetHeight(20) plus:SetPoint(\"TOPRIGHT\",-16,").Append(y + 2).Append(") plus:SetText(\"+\")\n");
                    sb.Append("plus:SetScript(\"OnClick\",function() AIO3Bridge[key]=math.min(").Append(i.Max).Append(",AIO3Bridge[key]+").Append(i.Step).Append(") refresh() end)\n");
                    sb.Append("end\n");
                }
                else if (s is ToggleSetting t)
                {
                    sb.Append("do local key=\"").Append(key).Append("\"\n");
                    sb.Append("if AIO3Bridge[key]==nil then AIO3Bridge[key]=").Append(t.Value ? "true" : "false").Append(" end\n");
                    sb.Append("local cb=CreateFrame(\"CheckButton\",nil,AIO3Frame,\"UICheckButtonTemplate\")\n");
                    sb.Append("cb:SetWidth(24) cb:SetHeight(24) cb:SetPoint(\"TOPLEFT\",14,").Append(y + 4).Append(")\n");
                    sb.Append("cb:SetChecked(AIO3Bridge[key])\n");
                    sb.Append("local lbl=AIO3Frame:CreateFontString(nil,\"OVERLAY\",\"GameFontNormal\") lbl:SetPoint(\"LEFT\",cb,\"RIGHT\",2,0)\n");
                    sb.Append("lbl:SetText(\"").Append(label).Append("\")\n");
                    sb.Append("cb:SetScript(\"OnClick\",function() AIO3Bridge[key]=cb:GetChecked() and true or false end)\n");
                    sb.Append("end\n");
                }
                else if (s is ChoiceSetting c)
                {
                    sb.Append("do local key=\"").Append(key).Append("\"\n");
                    sb.Append("if AIO3Bridge[key]==nil then AIO3Bridge[key]=\"").Append(Escape(c.Value)).Append("\" end\n");
                    sb.Append("local opts={");
                    for (int o = 0; o < c.Options.Length; o++)
                    {
                        if (o > 0) sb.Append(',');
                        sb.Append('"').Append(Escape(c.Options[o])).Append('"');
                    }
                    sb.Append("}\n");
                    sb.Append("local btn=CreateFrame(\"Button\",nil,AIO3Frame,\"UIPanelButtonTemplate\") btn:SetWidth(150) btn:SetHeight(22) btn:SetPoint(\"TOPLEFT\",16,").Append(y).Append(")\n");
                    sb.Append("local function refresh() btn:SetText(\"").Append(label).Append(": \"..AIO3Bridge[key]) end refresh()\n");
                    sb.Append("btn:SetScript(\"OnClick\",function() local cur=AIO3Bridge[key] local idx=1 for i=1,#opts do if opts[i]==cur then idx=i break end end idx=idx % #opts + 1 AIO3Bridge[key]=opts[idx] refresh() end)\n");
                    sb.Append("end\n");
                }
            }

            sb.Append("SLASH_AIO31=\"/aio3\" SlashCmdList[\"AIO3\"]=function() if AIO3Frame:IsShown() then AIO3Frame:Hide() else AIO3Frame:Show() end end\n");
            return sb.ToString();
        }

        private static string Escape(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
