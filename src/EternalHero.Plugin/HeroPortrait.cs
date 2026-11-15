using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using EternalHero.Core;
using HarmonyLib;

namespace EternalHero.Plugin
{
    internal static class HeroPortrait
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Portrait code is produced by the game's own customization panel.
        // Never attempt to invent a portrait asset path or write arbitrary texture files.
        public static bool TryApply(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null || string.IsNullOrWhiteSpace(profile.PortraitCode)) return false;
            try
            {
                Type generator = null;
                Type api = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    catch { continue; }
                    generator = generator ?? types.FirstOrDefault(t => t.Name == "CodeGenerator" &&
                        t.GetMethods(Static).Any(m => m.Name == "TryDecode"));
                    api = api ?? types.FirstOrDefault(t => t.Name == "PortraitAPIManager" &&
                        t.GetMethods(Static).Any(m => m.Name == "GeneratePortrait"));
                    if (generator != null && api != null) break;
                }
                var decode = generator?.GetMethods(Static).FirstOrDefault(m =>
                    m.Name == "TryDecode" && m.GetParameters().Length == 2 &&
                    m.GetParameters()[0].ParameterType == typeof(string) &&
                    m.GetParameters()[1].ParameterType.IsByRef);
                if (decode == null) { log.LogWarning("Eternal Hero: portrait code decoder not found"); return false; }
                var codeType = decode.GetParameters()[1].ParameterType.GetElementType();
                object[] args = { profile.PortraitCode, Activator.CreateInstance(codeType) };
                if (!(decode.Invoke(null, args) is bool valid) || !valid)
                {
                    log.LogWarning("Eternal Hero: saved portrait code was not decoded (length=" +
                        profile.PortraitCode.Length + ")");
                    return false;
                }
                var generatorMethod = api?.GetMethods(Static).FirstOrDefault(m =>
                    m.Name == "GeneratePortrait" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == codeType);
                if (generatorMethod == null) { log.LogWarning("Eternal Hero: portrait renderer missing"); return false; }
                var portrait = generatorMethod.Invoke(null, new[] { args[1] });
                if (portrait == null) return false;
                bool result = GameBridge.Set(hero, "PortraitCodeData", GameBridge.Get(portrait, "Code"));
                result |= GameBridge.Set(hero, "PortraitSprite", GameBridge.Get(portrait, "Front"));
                result |= GameBridge.Set(hero, "PortraitBackgroundSprite", GameBridge.Get(portrait, "Back"));
                if (profile.HeroGender == "Female" || profile.HeroGender == "Male")
                    GameBridge.Set(hero, "Gender", profile.HeroGender);
                if (!string.IsNullOrWhiteSpace(profile.HeroFaceId))
                    GameBridge.Set(hero, "FaceId", profile.HeroFaceId);
                if (result) GameBridge.RefreshHeroPresentation(hero, log);
                else log.LogWarning("Eternal Hero: portrait generated but no writable hero fields found.");
                log.LogInfo("Eternal Hero portrait restored from native game code: " + result);
                return result;
            }
            catch (Exception ex)
            {
                log.LogWarning("Eternal Hero portrait restore: " + ex.GetBaseException().Message);
                return false;
            }
        }
    }
}
