using HarmonyLib;
using Verse;

namespace QuestTranslationAddon
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        public const string HarmonyId = "kuroneko2310.autotranslation.questaddon";
        public const string LogPrefix = "<color=#8ec5ff>AutoTranslation Quest Addon</color>: ";

        static Bootstrap()
        {
            new Harmony(HarmonyId).PatchAll();
            Log.Message(LogPrefix + "Harmony patches applied for RimWorld 1.6 quest title and description translation.");
        }
    }
}
