using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EternalHero.Core;

namespace EternalHero.Plugin
{
    // Safe best-effort capture of primitive appearance choices.
    // Does not touch combat stats, equipment, references or Unity textures.
    internal static class HeroIdentity
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly string[] Containers =
            { "self", "UnitData", "PlayableUnitData", "CharacterData", "Customization", "Appearance" };
        private static readonly string[] Attributes =
        {
            "Gender", "GenderId", "RaceId", "HairColorId", "HairStyleId", "HairId",
            "SkinColorId", "SkinTone", "FaceId", "FaceIndex", "PortraitId",
            "BodyType", "EyeColorId", "EyesId", "HeadId", "Race", "Hairstyle"
        };

        private static object Get(object target, string member)
        {
            if (target == null) return null;
            try
            {
                var p = target.GetType().GetProperty(member, Flags);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
                var f = target.GetType().GetField(member, Flags);
                return f?.GetValue(target);
            }
            catch { return null; }
        }

        private static object Container(object hero, string container) =>
            container == "self" ? hero : Get(hero, container);

        private static bool Scalar(Type t) =>
            t == typeof(string) || t == typeof(int) || t == typeof(bool) || t.IsEnum;

        private static bool TryRead(object target, string field, out string data)
        {
            data = null;
            if (target == null) return false;
            try
            {
                var p = target.GetType().GetProperty(field, Flags);
                if (p != null && p.CanRead && p.CanWrite &&
                    p.GetIndexParameters().Length == 0 && Scalar(p.PropertyType))
                {
                    object v = p.GetValue(target, null);
                    if (v == null) return false;
                    data = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                    return !string.IsNullOrWhiteSpace(data);
                }
                var f = target.GetType().GetField(field, Flags);
                if (f != null && !f.IsInitOnly && !f.IsLiteral && Scalar(f.FieldType))
                {
                    object v = f.GetValue(target);
                    if (v == null) return false;
                    data = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                    return !string.IsNullOrWhiteSpace(data);
                }
            }
            catch { }
            return false;
        }

        private static bool TryWrite(object target, string member, string value)
        {
            if (target == null) return false;
            try
            {
                var p = target.GetType().GetProperty(member, Flags);
                if (p != null && p.CanWrite && p.CanRead && Scalar(p.PropertyType) &&
                    p.GetIndexParameters().Length == 0)
                {
                    p.SetValue(target, Parse(value, p.PropertyType), null);
                    return true;
                }
                var f = target.GetType().GetField(member, Flags);
                if (f != null && !f.IsInitOnly && !f.IsLiteral && Scalar(f.FieldType))
                {
                    f.SetValue(target, Parse(value, f.FieldType));
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static object Parse(string value, Type type)
        {
            if (type.IsEnum) return Enum.Parse(type, value);
            if (type == typeof(int)) return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(bool)) return bool.Parse(value);
            return value;
        }

        public static int CaptureOrRestore(HeroProfile profile, object hero, out bool captured)
        {
            captured = false;
            if (profile == null || hero == null) return 0;
            if (profile.Appearance == null) profile.Appearance = new Dictionary<string, string>();
            if (profile.Appearance.Count == 0)
            {
                var snapshot = new Dictionary<string, string>();
                foreach (var container in Containers)
                {
                    var target = Container(hero, container);
                    foreach (var attribute in Attributes)
                    {
                        if (TryRead(target, attribute, out string val))
                            snapshot[container + "/" + attribute] = val;
                    }
                }
                if (snapshot.Count == 0) return 0;
                profile.Appearance = snapshot;
                captured = true;
                return snapshot.Count;
            }
            int restored = 0;
            foreach (var entry in profile.Appearance)
            {
                var parts = entry.Key.Split('/');
                if (parts.Length != 2 || !Containers.Contains(parts[0]) ||
                    !Attributes.Contains(parts[1])) continue;
                if (TryWrite(Container(hero, parts[0]), parts[1], entry.Value)) restored++;
            }
            return restored;
        }
    }
}
