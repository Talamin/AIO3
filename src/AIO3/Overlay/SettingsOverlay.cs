using System;
using System.Collections.Generic;
using System.Text;
using AIO3.Core.Settings;
using robotManager.Helpful;
using wManager.Wow.Helpers;

namespace AIO3.Overlay
{
    /// <summary>
    /// In-game settings overlay. Builds a movable WoW UI frame whose contents are generated
    /// automatically from a rotation's settings, grouped into tabs by <see cref="Setting.Category"/>
    /// (e.g. Spec / Rotation / General). Edits flow Lua -> C# via the AIO3Bridge table; Poll() mirrors
    /// them into the Setting objects, which the rotation reads live. Toggle with /aio3.
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
                    if (clamped != i.Value) { i.Value = clamped; changed = true; Logging.Write($"[AIO3] {i.Label} = {i.Value}"); }
                }
                else if (s is ToggleSetting t)
                {
                    bool v = Lua.LuaDoString<bool>(
                        $"if AIO3Bridge then return AIO3Bridge['{t.Key}'] == true end return {(t.Value ? "true" : "false")}");
                    if (v != t.Value) { t.Value = v; changed = true; Logging.Write($"[AIO3] {t.Label} = {t.Value}"); }
                }
                else if (s is ChoiceSetting c)
                {
                    string v = Lua.LuaDoString<string>(
                        $"if AIO3Bridge and AIO3Bridge['{c.Key}'] then return tostring(AIO3Bridge['{c.Key}']) end return ''");
                    if (!string.IsNullOrEmpty(v) && v != c.Value && Array.IndexOf(c.Options, v) >= 0)
                    {
                        c.Value = v; changed = true; Logging.Write($"[AIO3] {c.Label} = {c.Value}");
                    }
                }
            }
            return changed;
        }

        private string BuildFrameLua()
        {
            // Group settings by category, preserving first-seen order.
            var order = new List<string>();
            var byCat = new Dictionary<string, List<Setting>>();
            foreach (Setting s in _settings)
            {
                if (!byCat.TryGetValue(s.Category, out List<Setting> list))
                {
                    list = new List<Setting>();
                    byCat[s.Category] = list;
                    order.Add(s.Category);
                }
                list.Add(s);
            }

            int maxRows = 0;
            foreach (string cat in order) maxRows = System.Math.Max(maxRows, byCat[cat].Count);
            int height = 56 + maxRows * 28 + 12;
            int width = System.Math.Max(300, order.Count * 92 + 16);

            var sb = new StringBuilder();
            sb.Append("if AIO3Frame then AIO3Frame:Hide() end\n");
            sb.Append("AIO3Bridge = AIO3Bridge or {}\n");
            sb.Append("AIO3Frame = CreateFrame(\"Frame\",\"AIO3Frame\",UIParent)\n");
            sb.Append("AIO3Frame:SetWidth(").Append(width).Append(") AIO3Frame:SetHeight(").Append(height).Append(")\n");
            sb.Append("AIO3Frame:SetPoint(\"CENTER\",0,220)\n");
            sb.Append("AIO3Frame:SetBackdrop(StaticPopup1:GetBackdrop())\n");
            sb.Append("AIO3Frame:SetMovable(true) AIO3Frame:EnableMouse(true)\n");
            sb.Append("AIO3Frame:SetScript(\"OnMouseDown\",function() AIO3Frame:StartMoving() end)\n");
            sb.Append("AIO3Frame:SetScript(\"OnMouseUp\",function() AIO3Frame:StopMovingOrSizing() end)\n");
            sb.Append("local title=AIO3Frame:CreateFontString(nil,\"OVERLAY\",\"GameFontNormal\") title:SetPoint(\"TOP\",0,-7) title:SetText(\"AIO3 - ").Append(Escape(_rotationName)).Append("\")\n");
            sb.Append("local close=CreateFrame(\"Button\",nil,AIO3Frame,\"UIPanelCloseButton\") close:SetPoint(\"TOPRIGHT\",-2,-2)\n");
            sb.Append("AIO3Frame.contents={}\n");
            sb.Append("local function showTab(n) for k,c in pairs(AIO3Frame.contents) do if k==n then c:Show() else c:Hide() end end end\n");

            for (int ti = 0; ti < order.Count; ti++)
            {
                string cat = order[ti];
                string catEsc = Escape(cat);
                int tabX = 10 + ti * 92;

                sb.Append("do\n");
                sb.Append("local content=CreateFrame(\"Frame\",nil,AIO3Frame) content:SetPoint(\"TOPLEFT\",10,-50) content:SetPoint(\"BOTTOMRIGHT\",-10,10) content:Hide()\n");
                sb.Append("AIO3Frame.contents[\"").Append(catEsc).Append("\"]=content\n");
                sb.Append("local tab=CreateFrame(\"Button\",nil,AIO3Frame,\"UIPanelButtonTemplate\") tab:SetWidth(86) tab:SetHeight(22) tab:SetPoint(\"TOPLEFT\",").Append(tabX).Append(",-26) tab:SetText(\"").Append(catEsc).Append("\")\n");
                sb.Append("tab:SetScript(\"OnClick\",function() showTab(\"").Append(catEsc).Append("\") end)\n");

                List<Setting> list = byCat[cat];
                for (int r = 0; r < list.Count; r++)
                    AppendWidget(sb, list[r], -(6 + r * 28));

                sb.Append("end\n");
            }

            sb.Append("showTab(\"").Append(Escape(order[0])).Append("\")\n");
            sb.Append("SLASH_AIO31=\"/aio3\" SlashCmdList[\"AIO3\"]=function() if AIO3Frame:IsShown() then AIO3Frame:Hide() else AIO3Frame:Show() end end\n");
            return sb.ToString();
        }

        // Emits one widget into the current `content` frame (in scope) at vertical offset y.
        private static void AppendWidget(StringBuilder sb, Setting s, int y)
        {
            string key = Escape(s.Key);
            string label = Escape(s.Label);

            if (s is IntSetting i)
            {
                sb.Append("do local key=\"").Append(key).Append("\"\n");
                sb.Append("if AIO3Bridge[key]==nil then AIO3Bridge[key]=").Append(i.Value).Append(" end\n");
                sb.Append("local fs=content:CreateFontString(nil,\"OVERLAY\",\"GameFontNormal\") fs:SetPoint(\"TOPLEFT\",content,\"TOPLEFT\",4,").Append(y).Append(")\n");
                sb.Append("local function refresh() fs:SetText(\"").Append(label).Append(": \"..AIO3Bridge[key]) end refresh()\n");
                sb.Append("local minus=CreateFrame(\"Button\",nil,content,\"UIPanelButtonTemplate\") minus:SetWidth(22) minus:SetHeight(18) minus:SetPoint(\"TOPRIGHT\",content,\"TOPRIGHT\",-44,").Append(y + 1).Append(") minus:SetText(\"-\")\n");
                sb.Append("minus:SetScript(\"OnClick\",function() AIO3Bridge[key]=math.max(").Append(i.Min).Append(",AIO3Bridge[key]-").Append(i.Step).Append(") refresh() end)\n");
                sb.Append("local plus=CreateFrame(\"Button\",nil,content,\"UIPanelButtonTemplate\") plus:SetWidth(22) plus:SetHeight(18) plus:SetPoint(\"TOPRIGHT\",content,\"TOPRIGHT\",-16,").Append(y + 1).Append(") plus:SetText(\"+\")\n");
                sb.Append("plus:SetScript(\"OnClick\",function() AIO3Bridge[key]=math.min(").Append(i.Max).Append(",AIO3Bridge[key]+").Append(i.Step).Append(") refresh() end)\n");
                sb.Append("end\n");
            }
            else if (s is ToggleSetting t)
            {
                sb.Append("do local key=\"").Append(key).Append("\"\n");
                sb.Append("if AIO3Bridge[key]==nil then AIO3Bridge[key]=").Append(t.Value ? "true" : "false").Append(" end\n");
                sb.Append("local cb=CreateFrame(\"CheckButton\",nil,content,\"UICheckButtonTemplate\") cb:SetWidth(22) cb:SetHeight(22) cb:SetPoint(\"TOPLEFT\",content,\"TOPLEFT\",2,").Append(y + 2).Append(") cb:SetChecked(AIO3Bridge[key])\n");
                sb.Append("local lbl=content:CreateFontString(nil,\"OVERLAY\",\"GameFontNormal\") lbl:SetPoint(\"LEFT\",cb,\"RIGHT\",2,0) lbl:SetText(\"").Append(label).Append("\")\n");
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
                sb.Append("local btn=CreateFrame(\"Button\",nil,content,\"UIPanelButtonTemplate\") btn:SetWidth(150) btn:SetHeight(20) btn:SetPoint(\"TOPLEFT\",content,\"TOPLEFT\",4,").Append(y).Append(")\n");
                sb.Append("local function refresh() btn:SetText(\"").Append(label).Append(": \"..AIO3Bridge[key]) end refresh()\n");
                sb.Append("btn:SetScript(\"OnClick\",function() local cur=AIO3Bridge[key] local idx=1 for n=1,#opts do if opts[n]==cur then idx=n break end end idx=idx % #opts + 1 AIO3Bridge[key]=opts[idx] refresh() end)\n");
                sb.Append("end\n");
            }
        }

        private static string Escape(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
