using System;

namespace EternalHero.Core
{
    /// <summary>
    /// Applies a configurable, idempotent floor to an already computed attack
    /// base-damage range. Does not multiply the whole result, so original
    /// powerful skills are never reduced and the patch cannot compound.
    /// The caller must validate ownership and exclude non-damaging actions.
    /// </summary>
    public static class PurchasedAttackDamageFloor
    {
        public static bool Apply(ref int minimum,ref int maximum,
            float weaponMinimum,float weaponMaximum,float weaponFactor)
        {
            if(float.IsNaN(weaponMinimum)||float.IsInfinity(weaponMinimum)||
               float.IsNaN(weaponMaximum)||float.IsInfinity(weaponMaximum)||
               float.IsNaN(weaponFactor)||float.IsInfinity(weaponFactor))
                return false;
            double factor=Math.Max(0d,Math.Min(5d,(double)weaponFactor));
            double lo=Math.Max(0d,(double)weaponMinimum)*factor;
            double hi=Math.Max(0d,(double)weaponMaximum)*factor;
            // Guard integer conversion in case a game/mod supplies an
            // unusually large base damage outside normal item ranges.
            int floorLo=(int)Math.Round(Math.Min(int.MaxValue,lo),
                MidpointRounding.AwayFromZero);
            int floorHi=(int)Math.Round(Math.Min(int.MaxValue,hi),
                MidpointRounding.AwayFromZero);
            int previousMin=minimum,previousMax=maximum;
            minimum=Math.Max(minimum,floorLo);
            maximum=Math.Max(maximum,Math.Max(minimum,floorHi));
            return minimum!=previousMin||maximum!=previousMax;
        }
    }
}
