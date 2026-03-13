using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class KOSpopupStop : MonoBehaviour
{
    private const string LogTag = "[KOSpopupStop] ";
    private const string SelectedManagerField = "connectivityHandler";
    private const string KnownManagerListField = "knownHandlerList";
    private const string ConnectivityDialogName = "Select Dialog";
    private const string ConnectivityDialogText = "kOS has detected that there are one or more new connectivity managers available, or that your selected manager is no longer available.  A connectivity manager determines how kOS will interact with a communications network with regard to both inter-vessel and vessel-kerbin communication and file transfer.  Please select a manager to use.";
    private const string LocalSelectionKey = "SelectedConnectivityManager";
    private const string LocalSelectionCapturedKey = "HasCapturedSelection";
    private const string LocalKnownManagersKey = "KnownConnectivityManagers";
    private const string DebugEnabledKey = "DebugEnabled";

    private const float RecheckIntervalSeconds = 0.5f;

    private bool localSettingsLoaded;
    private bool hasCapturedSelection;
    private bool debugEnabled;
    private string cachedSelectedManager = string.Empty;
    private string cachedKnownManagers = string.Empty;
    private Game? lastSeenGame;
    private float nextRecheckAt;

    private void Start()
    {
        DontDestroyOnLoad(this);
        GameEvents.onGameStatePostLoad.Add(OnGameStatePostLoad);
        GameEvents.OnGameSettingsApplied.Add(OnGameSettingsApplied);
        TryApply();
    }

    private void OnDestroy()
    {
        GameEvents.onGameStatePostLoad.Remove(OnGameStatePostLoad);
        GameEvents.OnGameSettingsApplied.Remove(OnGameSettingsApplied);
    }

    private void OnGameStatePostLoad(ConfigNode _)
    {
        nextRecheckAt = 0f;
        TryApply();
    }

    private void OnGameSettingsApplied()
    {
        nextRecheckAt = 0f;
        TryApply();
    }

    private void Update()
    {
        if (HighLogic.LoadedScene == GameScenes.MAINMENU)
        {
            lastSeenGame = null;
            nextRecheckAt = 0f;
            return;
        }

        if (HighLogic.CurrentGame != null && !ReferenceEquals(lastSeenGame, HighLogic.CurrentGame))
        {
            lastSeenGame = HighLogic.CurrentGame;
            nextRecheckAt = 0f;
            LogDebug("Detected game/session change; reapplying suppression state.");
        }

        if (HighLogic.CurrentGame != null && Time.unscaledTime >= nextRecheckAt)
        {
            nextRecheckAt = Time.unscaledTime + RecheckIntervalSeconds;
            TryApply();
        }
    }

    private void TryApply()
    {
        if (HighLogic.CurrentGame == null)
        {
            return;
        }

        EnsureLocalSettingsLoaded();

        try
        {
            Type? connectivityParamsType = FindType("kOS.Communication.kOSConnectivityParameters");
            Type? connectivityManagerType = FindType("kOS.Communication.ConnectivityManager");
            if (connectivityParamsType == null || connectivityManagerType == null)
            {
                LogDebug("kOS types not available yet; retrying later.");
                return;
            }

            PropertyInfo? instanceProperty = connectivityParamsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object? instance = instanceProperty?.GetValue(null, null);
            if (instance == null)
            {
                LogDebug("kOS connectivity parameters instance unavailable; retrying later.");
                return;
            }

            FieldInfo? selectedField = connectivityParamsType.GetField(SelectedManagerField, BindingFlags.Public | BindingFlags.Instance);
            FieldInfo? knownListField = connectivityParamsType.GetField(KnownManagerListField, BindingFlags.Public | BindingFlags.Instance);
            if (selectedField == null || knownListField == null)
            {
                LogDebug("kOS connectivity fields not found; suppressor inactive for this session.");
                return;
            }

            HashSet<string> availableManagers = GetAvailableManagers(connectivityManagerType);
            if (availableManagers.Count == 0)
            {
                LogDebug("No available kOS connectivity managers detected yet.");
                return;
            }

            string selected = (selectedField.GetValue(instance) as string) ?? string.Empty;
            string knownRaw = (knownListField.GetValue(instance) as string) ?? string.Empty;
            HashSet<string> knownManagers = ParseCsvSet(knownRaw);
            knownManagers.UnionWith(ParseCsvSet(cachedKnownManagers));
            bool selectedIsAvailable = availableManagers.Contains(selected);
            bool waitingForUserSelection = false;
            bool restoredCachedSelection = false;

            if (selectedIsAvailable)
            {
                if (!hasCapturedSelection || !string.Equals(cachedSelectedManager, selected, StringComparison.Ordinal))
                {
                    hasCapturedSelection = true;
                    cachedSelectedManager = selected;
                    SaveLocalSettings();
                    LogDebug("Captured user-selected connectivity manager: " + selected);
                }
            }
            else
            {
                if (hasCapturedSelection &&
                    !string.IsNullOrWhiteSpace(cachedSelectedManager) &&
                    availableManagers.Contains(cachedSelectedManager))
                {
                    selected = cachedSelectedManager;
                    selectedField.SetValue(instance, selected);
                    restoredCachedSelection = true;
                    LogDebug("Restored cached user-selected connectivity manager: " + selected);
                }
                else
                {
                    waitingForUserSelection = true;
                    LogDebug("Waiting for first valid user selection from kOS dialog.");
                }
            }

            knownManagers.UnionWith(availableManagers);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                knownManagers.Add(selected);
            }

            selectedIsAvailable = availableManagers.Contains(selected);

            string mergedKnown = string.Join(",", knownManagers.OrderBy(name => name, StringComparer.Ordinal));
            if (!string.Equals(knownRaw, mergedKnown, StringComparison.Ordinal))
            {
                knownListField.SetValue(instance, mergedKnown);
                LogDebug("Updated known connectivity manager list.");
            }

            if (!string.Equals(cachedKnownManagers, mergedKnown, StringComparison.Ordinal))
            {
                cachedKnownManagers = mergedKnown;
                SaveLocalSettings();
            }

            if ((restoredCachedSelection || (hasCapturedSelection && selectedIsAvailable)) &&
                !string.IsNullOrWhiteSpace(cachedSelectedManager))
            {
                DismissConnectivityPopupIfPresent();
            }

            if (waitingForUserSelection)
            {
                return;
            }

            LogDebug("Applied connectivity manager persistence stabilization pass.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogTag + "Failed to apply: " + ex.Message);
        }
    }

    private void EnsureLocalSettingsLoaded()
    {
        if (localSettingsLoaded)
        {
            return;
        }

        try
        {
            var config = KSP.IO.PluginConfiguration.CreateForType<KOSpopupStop>();
            config.load();
            hasCapturedSelection = config.GetValue(LocalSelectionCapturedKey, false);
            cachedSelectedManager = config.GetValue(LocalSelectionKey, string.Empty) ?? string.Empty;
            cachedKnownManagers = config.GetValue(LocalKnownManagersKey, string.Empty) ?? string.Empty;
            debugEnabled = config.GetValue(DebugEnabledKey, true);
            SaveLocalSettings();
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogTag + "Failed to load local settings: " + ex.Message);
            hasCapturedSelection = false;
            cachedSelectedManager = string.Empty;
            cachedKnownManagers = string.Empty;
            debugEnabled = true;
        }
        finally
        {
            localSettingsLoaded = true;
        }
    }

    private void SaveLocalSettings()
    {
        try
        {
            var config = KSP.IO.PluginConfiguration.CreateForType<KOSpopupStop>();
            config.load();
            config.SetValue(LocalSelectionCapturedKey, hasCapturedSelection);
            config.SetValue(LocalSelectionKey, cachedSelectedManager);
            config.SetValue(LocalKnownManagersKey, cachedKnownManagers);
            config.SetValue(DebugEnabledKey, debugEnabled);
            config.save();
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogTag + "Failed to save local settings: " + ex.Message);
        }
    }

    private void LogDebug(string message)
    {
        if (!debugEnabled)
        {
            return;
        }

        Debug.Log(LogTag + message);
    }

    private void DismissConnectivityPopupIfPresent()
    {
        try
        {
            PopupDialog.DismissPopup(ConnectivityDialogName);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogTag + "Failed to dismiss named popup: " + ex.Message);
        }

        ClearQueuedConnectivityDialogs();

        try
        {
            FieldInfo? dialogField = typeof(PopupDialog).GetField("dialogToDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
            if (dialogField == null)
            {
                return;
            }

            PopupDialog[] popups = Resources.FindObjectsOfTypeAll<PopupDialog>();
            foreach (PopupDialog popup in popups)
            {
                object? dialogObject = dialogField.GetValue(popup);
                if (IsConnectivityDialog(dialogObject))
                {
                    popup.Dismiss();
                    LogDebug("Dismissed active kOS connectivity popup after restoring cached state.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogTag + "Failed to dismiss popup: " + ex.Message);
        }
    }

    private void ClearQueuedConnectivityDialogs()
    {
        try
        {
            Type? settingsCheckerType = FindType("kOS.Module.kOSSettingsChecker");
            if (settingsCheckerType == null)
            {
                return;
            }

            FieldInfo? queueField = settingsCheckerType.GetField("dialogsToSpawn", BindingFlags.NonPublic | BindingFlags.Static);
            if (queueField == null)
            {
                return;
            }

            object? queueObject = queueField.GetValue(null);
            if (queueObject == null)
            {
                return;
            }

            Type queueType = queueObject.GetType();
            MethodInfo? clearMethod = queueType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? enqueueMethod = queueType.GetMethod("Enqueue", BindingFlags.Public | BindingFlags.Instance);
            if (clearMethod == null || enqueueMethod == null || queueObject is not IEnumerable enumerable)
            {
                return;
            }

            List<object> retainedDialogs = new List<object>();
            bool removedDialog = false;
            foreach (object queuedDialog in enumerable)
            {
                FieldInfo? dialogField = queuedDialog.GetType().GetField("dialog", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object? dialogObject = dialogField?.GetValue(queuedDialog);
                if (IsConnectivityDialog(dialogObject))
                {
                    removedDialog = true;
                    continue;
                }

                retainedDialogs.Add(queuedDialog);
            }

            if (!removedDialog)
            {
                return;
            }

            clearMethod.Invoke(queueObject, null);
            foreach (object retainedDialog in retainedDialogs)
            {
                enqueueMethod.Invoke(queueObject, new[] { retainedDialog });
            }

            LogDebug("Removed queued kOS connectivity popup.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogTag + "Failed to clear queued popup: " + ex.Message);
        }
    }

    private static bool IsConnectivityDialog(object? dialogObject)
    {
        if (dialogObject == null)
        {
            return false;
        }

        Type dialogType = dialogObject.GetType();
        FieldInfo? nameField = dialogType.GetField("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo? titleField = dialogType.GetField("title", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo? messageField = dialogType.GetField("message", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        string name = (nameField?.GetValue(dialogObject) as string) ?? string.Empty;
        string title = (titleField?.GetValue(dialogObject) as string) ?? string.Empty;
        string message = (messageField?.GetValue(dialogObject) as string) ?? string.Empty;

        if (string.Equals(name, ConnectivityDialogName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(message, ConnectivityDialogText, StringComparison.Ordinal))
        {
            return true;
        }

        bool messageKeywordMatch = message.IndexOf("connectivity manager", StringComparison.OrdinalIgnoreCase) >= 0
            && message.IndexOf("Please select a manager", StringComparison.OrdinalIgnoreCase) >= 0;

        bool titleNameMatch = title.IndexOf("kOS", StringComparison.OrdinalIgnoreCase) >= 0
            && name.IndexOf("Select Dialog", StringComparison.OrdinalIgnoreCase) >= 0;

        bool optionMatch = false;
        FieldInfo? optionsField = dialogType.GetField("Options", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (optionsField?.GetValue(dialogObject) is IEnumerable options)
        {
            foreach (object? option in options)
            {
                if (option == null)
                {
                    continue;
                }

                Type optionType = option.GetType();
                FieldInfo? optionTextField = optionType.GetField("OptionText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? optionType.GetField("optionText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? optionType.GetField("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                string optionText = (optionTextField?.GetValue(option) as string) ?? option.ToString() ?? string.Empty;
                if (optionText.IndexOf("CommNetConnectivityManager", StringComparison.OrdinalIgnoreCase) >= 0
                    || optionText.IndexOf("PermitAllConnectivityManager", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    optionMatch = true;
                    break;
                }
            }
        }

        return messageKeywordMatch || titleNameMatch || optionMatch;
    }

    private static Type? FindType(string fullTypeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? found = assembly.GetType(fullTypeName, false);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static HashSet<string> GetAvailableManagers(Type connectivityManagerType)
    {
        MethodInfo? getStringHashMethod = connectivityManagerType.GetMethod("GetStringHash", BindingFlags.Public | BindingFlags.Static);
        if (getStringHashMethod == null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        object? result = getStringHashMethod.Invoke(null, null);
        HashSet<string> managers = new HashSet<string>(StringComparer.Ordinal);
        if (result is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                if (item is string text && !string.IsNullOrWhiteSpace(text))
                {
                    managers.Add(text.Trim());
                }
            }
        }
        return managers;
    }

    private static HashSet<string> ParseCsvSet(string csv)
    {
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv))
        {
            return result;
        }

        foreach (string part in csv.Split(','))
        {
            string trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                result.Add(trimmed);
            }
        }
        return result;
    }
}
