using HarmonyLib;
using RimWorld;
using Verse;

namespace QuestTranslationAddon
{
    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.Add))]
    internal static class QuestManagerAddPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Quest quest)
        {
            QuestTranslationService.QueueQuest(quest);
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.ExposeData))]
    internal static class QuestManagerExposeDataPatch
    {
        [HarmonyPostfix]
        private static void Postfix(QuestManager __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance == null)
            {
                return;
            }

            foreach (var quest in __instance.QuestsListForReading)
            {
                QuestTranslationService.QueueQuest(quest);
            }
        }
    }

    /// <summary>
    /// Runtime translations are deliberately not written into save files. This keeps the
    /// original quest text available when the player changes language or removes the add-on.
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.ExposeData))]
    internal static class QuestExposeDataPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Quest __instance, out SaveState __state)
        {
            __state = default(SaveState);

            if (Scribe.mode != LoadSaveMode.Saving || __instance == null)
            {
                return;
            }

            string originalName;
            TaggedString originalDescription;
            if (!QuestTranslationService.TryGetOriginal(__instance, out originalName, out originalDescription))
            {
                return;
            }

            __state.Replaced = true;
            __state.RuntimeName = __instance.name;
            __state.RuntimeDescription = __instance.description;

            __instance.name = originalName;
            __instance.description = originalDescription;
        }

        [HarmonyPostfix]
        private static void Postfix(Quest __instance, SaveState __state)
        {
            if (!__state.Replaced || __instance == null)
            {
                return;
            }

            __instance.name = __state.RuntimeName;
            __instance.description = __state.RuntimeDescription;
        }

        private struct SaveState
        {
            internal bool Replaced;
            internal string RuntimeName;
            internal TaggedString RuntimeDescription;
        }
    }
}
