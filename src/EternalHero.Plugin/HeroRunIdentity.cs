using System;
using System.Linq;
using System.Reflection;

namespace EternalHero.Plugin
{
    internal static class HeroRunIdentity
    {
        // We only trust an existing identifier provided by the game.
        // This is provisional until tested with both a new run and save reload.
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static string TryGetStableHeroId(object hero)
        {
            if (hero == null) return null;
            var objects = new[]
            {
                hero, Member(hero, "UnitData"), Member(hero, "PlayableUnitData"),
                Member(hero, "Controller"), Member(hero, "Unit")
            }.Where(x => x != null);
            foreach (var obj in objects)
                foreach (var key in new[] { "Guid", "GUID", "UniqueId", "UniqueID", "UnitGuid",
                                            "UnitId", "UnitID", "Identifier", "Id" })
                {
                    var raw = Member(obj, key);
                    if (raw == null) continue;
                    var val = raw.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    if (Guid.TryParse(val, out var guid) && guid != Guid.Empty)
                        return "unit-guid:" + guid.ToString("D");
                    if (val.Length >= 16 && val.Length <= 120 &&
                        val.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
                        return "unit-id:" + val;
                }
            return null;
        }

        private static object Member(object obj, string member)
        {
            if (obj == null) return null;
            try
            {
                var p = obj.GetType().GetProperty(member, Flags);
                if (p != null && p.GetIndexParameters().Length == 0)
                    return p.GetValue(obj, null);
                return obj.GetType().GetField(member, Flags)?.GetValue(obj);
            }
            catch { return null; }
        }
    }
}
