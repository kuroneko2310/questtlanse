using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using AutoTranslation;
using AutoTranslation.Services;
using RimWorld;
using Verse;

namespace QuestTranslationAddon
{
    internal static class QuestTranslationService
    {
        private static readonly ConditionalWeakTable<Quest, QuestRecord> QuestRecords =
            new ConditionalWeakTable<Quest, QuestRecord>();

        private static readonly ConcurrentDictionary<string, PendingTranslation> PendingTranslations =
            new ConcurrentDictionary<string, PendingTranslation>(StringComparer.Ordinal);

        internal static void QueueQuest(Quest quest)
        {
            if (quest == null || LanguageDatabase.activeLanguage == null ||
                LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage)
            {
                return;
            }

            var modPackageId = quest.root != null && quest.root.modContentPack != null
                ? quest.root.modContentPack.PackageId ?? string.Empty
                : string.Empty;

            if (!string.IsNullOrEmpty(modPackageId) &&
                Settings.BlackListModPackageIds != null &&
                Settings.BlackListModPackageIds.Contains(modPackageId))
            {
                return;
            }

            var languageName = LanguageDatabase.activeLanguage.LegacyFolderName ??
                               LanguageDatabase.activeLanguage.folderName ??
                               string.Empty;
            var originalName = quest.name ?? string.Empty;
            var originalDescription = quest.description.RawText ?? string.Empty;
            var record = QuestRecords.GetValue(quest, delegate(Quest ignored)
            {
                return new QuestRecord();
            });

            lock (record.SyncRoot)
            {
                if (!record.HasOriginal)
                {
                    record.OriginalName = originalName;
                    record.OriginalDescription = originalDescription;
                    record.HasOriginal = true;
                }
            }

            QueueField(
                record,
                "title",
                originalName,
                modPackageId,
                languageName,
                delegate
                {
                    return quest.name ?? string.Empty;
                },
                delegate(string translated)
                {
                    quest.name = translated;
                });

            QueueField(
                record,
                "description",
                originalDescription,
                modPackageId,
                languageName,
                delegate
                {
                    return quest.description.RawText ?? string.Empty;
                },
                delegate(string translated)
                {
                    quest.description = new TaggedString(translated);
                });
        }

        internal static bool TryGetOriginal(Quest quest, out string name, out TaggedString description)
        {
            name = null;
            description = TaggedString.Empty;

            if (quest == null)
            {
                return false;
            }

            QuestRecord record;
            if (!QuestRecords.TryGetValue(quest, out record))
            {
                return false;
            }

            lock (record.SyncRoot)
            {
                if (!record.HasOriginal)
                {
                    return false;
                }

                name = record.OriginalName;
                description = new TaggedString(record.OriginalDescription ?? string.Empty);
                return true;
            }
        }

        private static void QueueField(
            QuestRecord record,
            string fieldKind,
            string originalText,
            string modPackageId,
            string languageName,
            Func<string> getCurrentText,
            Action<string> applyTranslation)
        {
            ProtectedTextPayload payload;
            if (!ProtectedTextPayload.TryCreate(originalText, languageName, out payload))
            {
                return;
            }

            var sourceHash = ComputeStableHash(originalText);
            var scheduledKey = fieldKind + "|" + languageName + "|" + sourceHash;

            lock (record.SyncRoot)
            {
                if (!record.ScheduledFields.Add(scheduledKey))
                {
                    return;
                }
            }

            Action<string> waiter = delegate(string safeTranslation)
            {
                // A quest may be edited by another mod while the network request is in flight.
                // Only replace the exact source value that was queued.
                if (string.Equals(getCurrentText(), originalText, StringComparison.Ordinal) &&
                    !string.Equals(safeTranslation, originalText, StringComparison.Ordinal))
                {
                    applyTranslation(safeTranslation);
                }
            };

            var requestKey = modPackageId + "|" + fieldKind + "|" + languageName + "|" + sourceHash;
            var pending = PendingTranslations.GetOrAdd(requestKey, delegate(string ignored)
            {
                return new PendingTranslation();
            });

            var startRequest = false;
            lock (pending.SyncRoot)
            {
                pending.Waiters.Add(waiter);
                if (!pending.Started)
                {
                    pending.Started = true;
                    startRequest = true;
                }
            }

            if (!startRequest)
            {
                return;
            }

            // This value is appended only to Auto Translation's cache key. TranslatorManager
            // sends payload.ProtectedText, not this suffix, to the translation provider.
            var cacheDiscriminator = "|quest-addon-v2|" + fieldKind + "|" + languageName + "|" + sourceHash;

            try
            {
                TranslatorManager.Translate(
                    payload.ProtectedText,
                    cacheDiscriminator,
                    modPackageId,
                    delegate(string translatedPayload)
                    {
                        string restored;
                        if (!payload.TryRestore(translatedPayload, out restored))
                        {
                            restored = originalText;
                            Log.WarningOnce(
                                Bootstrap.LogPrefix + "Rejected an unsafe quest " + fieldKind +
                                " translation because a protected number, ID, tag, or temporary marker changed. Original text was kept.",
                                requestKey.GetHashCode());
                        }

                        CompleteRequest(requestKey, restored);
                    });
            }
            catch (Exception exception)
            {
                Log.Warning(Bootstrap.LogPrefix + "Quest " + fieldKind + " translation failed safely: " + exception.Message);
                CompleteRequest(requestKey, originalText);
            }
        }

        private static void CompleteRequest(string requestKey, string result)
        {
            PendingTranslation pending;
            if (!PendingTranslations.TryRemove(requestKey, out pending))
            {
                return;
            }

            Action<string>[] waiters;
            lock (pending.SyncRoot)
            {
                waiters = pending.Waiters.ToArray();
                pending.Waiters.Clear();
            }

            foreach (var waiter in waiters)
            {
                try
                {
                    waiter(result);
                }
                catch (Exception exception)
                {
                    Log.Warning(Bootstrap.LogPrefix + "Could not apply a safe quest translation: " + exception.Message);
                }
            }
        }

        private static string ComputeStableHash(string text)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private sealed class QuestRecord
        {
            internal readonly object SyncRoot = new object();
            internal readonly HashSet<string> ScheduledFields = new HashSet<string>(StringComparer.Ordinal);
            internal bool HasOriginal;
            internal string OriginalName;
            internal string OriginalDescription;
        }

        private sealed class PendingTranslation
        {
            internal readonly object SyncRoot = new object();
            internal readonly List<Action<string>> Waiters = new List<Action<string>>();
            internal bool Started;
        }
    }
}
