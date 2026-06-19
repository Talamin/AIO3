using System;
using System.Collections.Generic;
using System.Threading;
using robotManager.Helpful;
using wManager.Wow.Helpers;

namespace AIO3.Talents
{
    /// <summary>
    /// Spends unspent talent points to match a spec's progression codes (ported from the old AIO
    /// TalentsManager). A code is one character per talent in tree order; the codes are applied in
    /// sequence, learning points until each target rank is reached. Blocking (sleeps between points),
    /// so it should only be run out of combat. Logic touches WRobot/Lua, so it lives in Layer 0.
    /// </summary>
    internal sealed class TalentTrainer
    {
        public void Apply(IReadOnlyList<string> codes)
        {
            try
            {
                if (codes == null || codes.Count == 0) return;
                if (AvailablePoints() <= 0) return;

                int[] num = { GetNumTalents(1), GetNumTalents(2), GetNumTalents(3) };
                int total = num[0] + num[1] + num[2];
                if (total <= 0) return;

                foreach (string code in codes)
                {
                    if (code.Length != total)
                    {
                        Logging.WriteError($"[AIO3] Talent code length {code.Length} != {total} talents — skipping.");
                        return;
                    }

                    int offset = 0;
                    for (int tree = 1; tree <= 3; tree++)
                    {
                        for (int i = 0; i < num[tree - 1]; i++)
                        {
                            int target = code[offset + i] - '0';
                            if (target <= 0) continue;

                            (int cur, int max) = TalentRank(tree, i + 1);
                            if (target > max) target = max;

                            while (cur < target)
                            {
                                Learn(tree, i + 1);
                                Thread.Sleep(400 + Usefuls.Latency);

                                if (AvailablePoints() <= 0) return;

                                (int newCur, _) = TalentRank(tree, i + 1);
                                if (newCur <= cur) break; // didn't take (locked/prereq) — avoid a spin
                                cur = newCur;
                                Logging.Write($"[AIO3] Talent learned: tree {tree} #{i + 1} -> {cur}/{target}");
                            }
                        }
                        offset += num[tree - 1];
                    }
                }
            }
            catch (Exception e)
            {
                Logging.WriteError("[AIO3] talent assignment failed: " + e.Message);
            }
        }

        private static int AvailablePoints() =>
            Lua.LuaDoString<int>("local p = UnitCharacterPoints('player'); if p == nil then return 0 end return p");

        private static int GetNumTalents(int tree) =>
            Lua.LuaDoString<int>($"return GetNumTalents({tree})");

        private static (int cur, int max) TalentRank(int tree, int index)
        {
            string s = Lua.LuaDoString<string>(
                $"local _,_,_,_,cur,max = GetTalentInfo({tree},{index}); return (cur or 0)..\":\"..(max or 0)");
            string[] parts = (s ?? "").Split(':');
            int cur = 0, max = 0;
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out cur);
                int.TryParse(parts[1], out max);
            }
            return (cur, max);
        }

        private static void Learn(int tree, int index) =>
            Lua.LuaDoString($"LearnTalent({tree}, {index})");
    }
}
