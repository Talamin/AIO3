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
        private int _lastAliveCheck;

        public SettingsOverlay(string rotationName, IReadOnlyList<Setting> settings)
        {
            _rotationName = rotationName;
            _settings = settings;
        }

        public void EnsureCreated()
        {
            if (_settings.Count == 0) return;

            if (!_created)
            {
                // (Re)build the frame. Each widget seeds the freshly-reset bridge from the CURRENT C# value
                // (defaults + persisted + any live edits), so a rebuild after a reload restores the real
                // values rather than defaults — no settings are lost.
                Lua.LuaDoString(BuildFrameLua());
                _created = true;
                Logging.Write("[AIO3] In-game settings overlay ready — type /aio3 to toggle.");
                return;
            }

            // A WoW UI reload (reconnect / /reload) wipes our Lua frame + bridge while WRobot keeps running,
            // leaving the overlay gone and the bridge empty. WRobot's C# side is untouched, so detect the
            // wipe (throttled — a Lua probe is ~15-40ms) and rebuild from the live C# values next pass.
            if (unchecked(Environment.TickCount - _lastAliveCheck) < 1500) return;
            _lastAliveCheck = Environment.TickCount;
            if (!Lua.LuaDoString<bool>("return AIO3Frame ~= nil"))
            {
                _created = false; // gone → the next EnsureCreated rebuilds it
                Logging.Write("[AIO3] Overlay was wiped by a UI reload — rebuilding.");
            }
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

        // Per-widget vertical footprint (px) used to lay rows out and size the frame.
        private const int ToggleHeight = 26;
        private const int ChoiceHeight = 30;
        private const int SliderHeight = 46;

        private static int WidgetHeight(Setting s) =>
            s is IntSetting ? SliderHeight : s is ChoiceSetting ? ChoiceHeight : ToggleHeight;

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

            // Frame is sized to the tallest tab. 28 = top+bottom padding inside the content inset.
            int maxContent = 0;
            foreach (string cat in order)
            {
                int h = 28;
                foreach (Setting s in byCat[cat]) h += WidgetHeight(s);
                maxContent = System.Math.Max(maxContent, h);
            }
            int height = maxContent + 60;                                  // 60 = header banner + tab row
            int width = System.Math.Max(360, order.Count * 92 + 16);

            var sb = new StringBuilder();
            sb.Append("if AIO3Frame then AIO3Frame:Hide() end\n");
            // Start the C#<->Lua bridge fresh each time the frame is (re)built. The frame is built once per
            // fightclass load, right AFTER settings are loaded from persistence, so the C# Setting objects
            // are the source of truth here — each widget below seeds its bridge key from them. Reusing a
            // stale bridge (it is a Lua global that survives fightclass reloads / class switches, and the
            // keys are shared across classes) made a Paladin inherit the Warrior's last values and the first
            // Poll() then wrote those back over the real defaults. Resetting kills that.
            sb.Append("AIO3Bridge = {}\n");
            sb.Append("AIO3Frame = CreateFrame(\"Frame\",\"AIO3Frame\",UIParent)\n");
            sb.Append("AIO3Frame:SetWidth(").Append(width).Append(") AIO3Frame:SetHeight(").Append(height).Append(")\n");
            sb.Append("AIO3Frame:SetPoint(\"CENTER\",0,220)\n");
            sb.Append("AIO3Frame:SetClampedToScreen(true)\n");
            // Dark, slim panel: tooltip background + border, dark fill, subtle gold-tinted border.
            sb.Append("AIO3Frame:SetBackdrop({bgFile=\"Interface\\\\Tooltips\\\\UI-Tooltip-Background\",edgeFile=\"Interface\\\\Tooltips\\\\UI-Tooltip-Border\",tile=true,tileSize=16,edgeSize=16,insets={left=4,right=4,top=4,bottom=4}})\n");
            sb.Append("AIO3Frame:SetBackdropColor(0.05,0.06,0.09,0.95)\n");
            sb.Append("AIO3Frame:SetBackdropBorderColor(0.85,0.7,0.35,1)\n");
            sb.Append("AIO3Frame:SetMovable(true) AIO3Frame:EnableMouse(true)\n");
            sb.Append("AIO3Frame:SetScript(\"OnMouseDown\",function() AIO3Frame:StartMoving() end)\n");
            sb.Append("AIO3Frame:SetScript(\"OnMouseUp\",function() AIO3Frame:StopMovingOrSizing() end)\n");
            // Gold ornate header banner with the title inscribed on it.
            sb.Append("local header=AIO3Frame:CreateTexture(nil,\"ARTWORK\") header:SetTexture(\"Interface\\\\DialogFrame\\\\UI-DialogBox-Header\") header:SetWidth(230) header:SetHeight(64) header:SetPoint(\"TOP\",0,12)\n");
            sb.Append("local title=AIO3Frame:CreateFontString(nil,\"OVERLAY\",\"GameFontNormal\") title:SetPoint(\"TOP\",header,\"TOP\",0,-15) title:SetText(\"AIO3 - ").Append(Escape(_rotationName)).Append("\")\n");
            sb.Append("local close=CreateFrame(\"Button\",nil,AIO3Frame,\"UIPanelCloseButton\") close:SetPoint(\"TOPRIGHT\",-2,-2)\n");
            sb.Append("AIO3Frame.contents={} AIO3Frame.tabs={}\n");
            sb.Append("local function showTab(n)\n");
            sb.Append("for k,c in pairs(AIO3Frame.contents) do if k==n then c:Show() else c:Hide() end end\n");
            sb.Append("for k,t in pairs(AIO3Frame.tabs) do if k==n then t:LockHighlight() else t:UnlockHighlight() end end\n");
            sb.Append("end\n");

            for (int ti = 0; ti < order.Count; ti++)
            {
                string cat = order[ti];
                string catEsc = Escape(cat);
                int tabX = 10 + ti * 92;

                sb.Append("do\n");
                // Recessed content panel for this tab.
                sb.Append("local content=CreateFrame(\"Frame\",nil,AIO3Frame) content:SetPoint(\"TOPLEFT\",10,-50) content:SetPoint(\"BOTTOMRIGHT\",-10,10) content:Hide()\n");
                sb.Append("content:SetBackdrop({bgFile=\"Interface\\\\Tooltips\\\\UI-Tooltip-Background\",edgeFile=\"Interface\\\\Tooltips\\\\UI-Tooltip-Border\",tile=true,tileSize=16,edgeSize=16,insets={left=3,right=3,top=3,bottom=3}})\n");
                sb.Append("content:SetBackdropColor(0,0,0,0.35) content:SetBackdropBorderColor(0.4,0.4,0.45,0.8)\n");
                sb.Append("AIO3Frame.contents[\"").Append(catEsc).Append("\"]=content\n");
                sb.Append("local tab=CreateFrame(\"Button\",nil,AIO3Frame,\"UIPanelButtonTemplate\") tab:SetWidth(86) tab:SetHeight(22) tab:SetPoint(\"TOPLEFT\",").Append(tabX).Append(",-26) tab:SetText(\"").Append(catEsc).Append("\")\n");
                sb.Append("AIO3Frame.tabs[\"").Append(catEsc).Append("\"]=tab\n");
                sb.Append("tab:SetScript(\"OnClick\",function() showTab(\"").Append(catEsc).Append("\") end)\n");

                int y = -14;
                foreach (Setting s in byCat[cat])
                {
                    AppendWidget(sb, s, y);
                    y -= WidgetHeight(s);
                }

                sb.Append("end\n");
            }

            sb.Append("showTab(\"").Append(Escape(order[0])).Append("\")\n");

            // Start minimized; open via the minimap button (or /aio3). The minimap button is a standard
            // Blizzard Button parented to the Minimap — an icon on the ring, draggable, with a tooltip,
            // toggling the panel. Created once (guarded) so a product reload doesn't stack duplicates.
            sb.Append(@"
AIO3Frame:Hide()
local function AIO3Toggle() if AIO3Frame:IsShown() then AIO3Frame:Hide() else AIO3Frame:Show() end end
if Minimap and not AIO3MinimapButton then
  local mb = CreateFrame(""Button"", ""AIO3MinimapButton"", Minimap)
  mb:SetFrameStrata(""MEDIUM"") mb:SetFrameLevel(8) mb:SetWidth(31) mb:SetHeight(31)
  mb:RegisterForClicks(""LeftButtonUp"") mb:RegisterForDrag(""LeftButton"")
  local icon = mb:CreateTexture(nil, ""BACKGROUND"")
  icon:SetTexture([[Interface\Icons\INV_Misc_Gear_01]])
  icon:SetWidth(20) icon:SetHeight(20) icon:SetPoint(""CENTER"", 0, 1)
  icon:SetTexCoord(0.07,0.93,0.07,0.93)
  local border = mb:CreateTexture(nil, ""OVERLAY"")
  border:SetWidth(53) border:SetHeight(53) border:SetPoint(""TOPLEFT"")
  border:SetTexture([[Interface\Minimap\MiniMap-TrackingBorder]])
  local angle = 200
  local function place() local a = math.rad(angle) mb:SetPoint(""CENTER"", Minimap, ""CENTER"", 80*math.cos(a), 80*math.sin(a)) end
  place()
  mb:SetScript(""OnDragStart"", function() mb:SetScript(""OnUpdate"", function()
    local mx, my = Minimap:GetCenter() local px, py = GetCursorPosition() local s = Minimap:GetEffectiveScale()
    angle = math.deg(math.atan2(py/s - my, px/s - mx)) place() end) end)
  mb:SetScript(""OnDragStop"", function() mb:SetScript(""OnUpdate"", nil) end)
  mb:SetScript(""OnEnter"", function() GameTooltip:SetOwner(mb, ""ANCHOR_LEFT"") GameTooltip:AddLine(""AIO3"") GameTooltip:AddLine(""Click to open settings"", 1, 1, 1) GameTooltip:Show() end)
  mb:SetScript(""OnLeave"", function() GameTooltip:Hide() end)
  mb:SetScript(""OnClick"", AIO3Toggle)
end
");
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
                // Native Blizzard slider (OptionsSliderTemplate) — its $parentText/Low/High regions are
                // addressed via the generated global name. Label + live value sit above the bar.
                string name = "AIO3_sld_" + key;
                sb.Append("do local key=\"").Append(key).Append("\"\n");
                sb.Append("if AIO3Bridge[key]==nil then AIO3Bridge[key]=").Append(i.Value).Append(" end\n");
                sb.Append("local sld=CreateFrame(\"Slider\",\"").Append(name).Append("\",content,\"OptionsSliderTemplate\")\n");
                sb.Append("sld:SetWidth(200) sld:SetHeight(16) sld:SetPoint(\"TOPLEFT\",content,\"TOPLEFT\",16,").Append(y - 18).Append(")\n");
                sb.Append("sld:SetMinMaxValues(").Append(i.Min).Append(',').Append(i.Max).Append(") sld:SetValueStep(").Append(i.Step).Append(")\n");
                // Min/max end labels (nil-guarded) and clear the template's own centred text — we draw our own.
                sb.Append("local lo=getglobal(\"").Append(name).Append("Low\") if lo then lo:SetText(\"").Append(i.Min).Append("\") end\n");
                sb.Append("local hi=getglobal(\"").Append(name).Append("High\") if hi then hi:SetText(\"").Append(i.Max).Append("\") end\n");
                sb.Append("local tx=getglobal(\"").Append(name).Append("Text\") if tx then tx:SetText(\"\") end\n");
                // Our own descriptive label above the slider (the template's $parentText is unreliable here).
                sb.Append("local lbl=content:CreateFontString(nil,\"OVERLAY\",\"GameFontHighlightSmall\") lbl:SetPoint(\"BOTTOMLEFT\",sld,\"TOPLEFT\",0,2)\n");
                sb.Append("local function refresh() lbl:SetText(\"").Append(label).Append(": \"..AIO3Bridge[key]) end\n");
                sb.Append("sld:SetScript(\"OnValueChanged\",function(self) local v=math.floor(self:GetValue()+0.5) AIO3Bridge[key]=v refresh() end)\n");
                sb.Append("sld:SetValue(AIO3Bridge[key]) refresh()\n");
                sb.Append("end\n");
            }
            else if (s is ToggleSetting t)
            {
                sb.Append("do local key=\"").Append(key).Append("\"\n");
                sb.Append("if AIO3Bridge[key]==nil then AIO3Bridge[key]=").Append(t.Value ? "true" : "false").Append(" end\n");
                sb.Append("local cb=CreateFrame(\"CheckButton\",nil,content,\"UICheckButtonTemplate\") cb:SetWidth(22) cb:SetHeight(22) cb:SetPoint(\"TOPLEFT\",content,\"TOPLEFT\",14,").Append(y + 2).Append(") cb:SetChecked(AIO3Bridge[key])\n");
                sb.Append("local lbl=content:CreateFontString(nil,\"OVERLAY\",\"GameFontHighlight\") lbl:SetPoint(\"LEFT\",cb,\"RIGHT\",4,0) lbl:SetText(\"").Append(label).Append("\")\n");
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
                sb.Append("local btn=CreateFrame(\"Button\",nil,content,\"UIPanelButtonTemplate\") btn:SetWidth(170) btn:SetHeight(22) btn:SetPoint(\"TOPLEFT\",content,\"TOPLEFT\",14,").Append(y).Append(")\n");
                sb.Append("local function refresh() btn:SetText(\"").Append(label).Append(": \"..AIO3Bridge[key]) end refresh()\n");
                sb.Append("btn:SetScript(\"OnClick\",function() local cur=AIO3Bridge[key] local idx=1 for n=1,#opts do if opts[n]==cur then idx=n break end end idx=idx % #opts + 1 AIO3Bridge[key]=opts[idx] refresh() end)\n");
                sb.Append("end\n");
            }
        }

        private static string Escape(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
