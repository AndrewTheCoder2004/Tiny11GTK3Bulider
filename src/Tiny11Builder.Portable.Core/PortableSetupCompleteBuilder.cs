using System.Text;

namespace Tiny11Builder.Portable;

public static class PortableSetupCompleteBuilder
{
    public static string Create(
        bool bypassHardwareRequirements = true,
        bool enableLocalAccount = true,
        bool disableSponsoredApps = true,
        bool disableTelemetry = true,
        bool disableCopilot = true,
        bool disableWidgets = true,
        bool disableBitLockerDeviceEncryption = true,
        bool disableReservedStorage = true,
        bool removeScheduledTasks = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine(":: Tiny11 Builder SetupComplete Post-Install Configuration");
        sb.AppendLine(":: Runs automatically as NT AUTHORITY\\SYSTEM at the end of Windows installation.");
        sb.AppendLine();

        if (bypassHardwareRequirements)
        {
            sb.AppendLine(":: Hardware Requirements Bypasses");
            sb.AppendLine("reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassCPUCheck /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassRAMCheck /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassSecureBootCheck /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassStorageCheck /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassTPMCheck /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SYSTEM\\Setup\\MoSetup\" /v AllowUpgradesWithUnsupportedTPMOrCPU /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine();
        }

        if (enableLocalAccount)
        {
            sb.AppendLine(":: Enable Local Accounts & Bypass OOBE Microsoft Account requirement");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\OOBE\" /v BypassNRO /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine();
        }

        if (disableSponsoredApps)
        {
            sb.AppendLine(":: Disable Sponsored Apps & Consumer Cloud Content");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent\" /v DisableWindowsConsumerFeatures /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent\" /v DisableConsumerAccountStateContent /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent\" /v DisableCloudOptimizedContent /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\PushToInstall\" /v DisablePushToInstall /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\MRT\" /v DontOfferThroughWUAU /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine();
        }

        if (disableTelemetry)
        {
            sb.AppendLine(":: Disable Telemetry & Diagnostic Data Collection");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\" /v AllowTelemetry /t REG_DWORD /d 0 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo\" /v DisabledByGroupPolicy /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("sc config dmwappushservice start= disabled >nul 2>&1");
            sb.AppendLine("sc stop dmwappushservice >nul 2>&1");
            sb.AppendLine();
        }

        if (disableCopilot)
        {
            sb.AppendLine(":: Disable Windows Copilot and Recall");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsCopilot\" /v TurnOffWindowsCopilot /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI\" /v DisableAIDataAnalysis /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Edge\" /v HubsSidebarEnabled /t REG_DWORD /d 0 /f >nul 2>&1");
            sb.AppendLine();
        }

        if (disableWidgets)
        {
            sb.AppendLine(":: Disable Widgets, Chat and Teams Auto-Install");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Dsh\" /v AllowNewsAndInterests /t REG_DWORD /d 0 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Chat\" /v ChatIcon /t REG_DWORD /d 3 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Teams\" /v DisableInstallation /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Mail\" /v PreventRun /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine();
        }

        if (disableBitLockerDeviceEncryption)
        {
            sb.AppendLine(":: Disable Automatic Device Encryption");
            sb.AppendLine("reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\BitLocker\" /v PreventDeviceEncryption /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine();
        }

        if (disableReservedStorage)
        {
            sb.AppendLine(":: Disable Reserved Storage");
            sb.AppendLine("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ReserveManager\" /v ShippedWithReserves /t REG_DWORD /d 0 /f >nul 2>&1");
            sb.AppendLine();
        }

        // Apply debloat settings to Default User profile (inherited by all future users)
        sb.AppendLine(":: Apply settings to Default User profile");
        sb.AppendLine("reg load HKLM\\TEMP_DEFAULT \"%SYSTEMDRIVE%\\Users\\Default\\NTUSER.DAT\" >nul 2>&1");
        sb.AppendLine("if %errorlevel% equ 0 (");

        if (bypassHardwareRequirements)
        {
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Control Panel\\UnsupportedHardwareNotificationCache\" /v SV1 /t REG_DWORD /d 0 /f >nul 2>&1");
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Control Panel\\UnsupportedHardwareNotificationCache\" /v SV2 /t REG_DWORD /d 0 /f >nul 2>&1");
        }

        if (disableSponsoredApps)
        {
            var contentKeys = new[]
            {
                "ContentDeliveryAllowed", "OemPreInstalledAppsEnabled", "PreInstalledAppsEnabled",
                "SilentInstalledAppsEnabled", "SoftLandingEnabled", "SubscribedContentEnabled",
                "SystemPaneSuggestionsEnabled", "FeatureManagementEnabled", "PreInstalledAppsEverEnabled",
                "SubscribedContent-310093Enabled", "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled",
                "SubscribedContent-338393Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled"
            };
            foreach (var k in contentKeys)
            {
                sb.AppendLine($"    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager\" /v {k} /t REG_DWORD /d 0 /f >nul 2>&1");
            }
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\" /v ShowSyncProviderNotifications /t REG_DWORD /d 0 /f >nul 2>&1");
        }

        if (disableTelemetry)
        {
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo\" /v Enabled /t REG_DWORD /d 0 /f >nul 2>&1");
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy\" /v TailoredExperiencesWithDiagnosticDataEnabled /t REG_DWORD /d 0 /f >nul 2>&1");
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\Speech_OneCore\\Settings\\OnlineSpeechPrivacy\" /v HasAccepted /t REG_DWORD /d 0 /f >nul 2>&1");
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\InputPersonalization\" /v RestrictImplicitInkCollection /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\InputPersonalization\" /v RestrictImplicitTextCollection /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\Personalization\\Settings\" /v AcceptedPrivacyPolicy /t REG_DWORD /d 0 /f >nul 2>&1");
        }

        if (disableCopilot)
        {
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Policies\\Microsoft\\Windows\\WindowsCopilot\" /v TurnOffWindowsCopilot /t REG_DWORD /d 1 /f >nul 2>&1");
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\" /v ShowCopilotButton /t REG_DWORD /d 0 /f >nul 2>&1");
        }

        if (disableWidgets)
        {
            sb.AppendLine("    reg add \"HKLM\\TEMP_DEFAULT\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\" /v TaskbarMn /t REG_DWORD /d 0 /f >nul 2>&1");
        }

        sb.AppendLine("    reg unload HKLM\\TEMP_DEFAULT >nul 2>&1");
        sb.AppendLine(")");
        sb.AppendLine();

        if (removeScheduledTasks)
        {
            sb.AppendLine(":: Remove telemetry and appraiser scheduled tasks");
            sb.AppendLine("del /f /q \"%WINDIR%\\System32\\Tasks\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\" >nul 2>&1");
            sb.AppendLine("del /f /q \"%WINDIR%\\System32\\Tasks\\Microsoft\\Windows\\Application Experience\\ProgramDataUpdater\" >nul 2>&1");
            sb.AppendLine("del /f /q \"%WINDIR%\\System32\\Tasks\\Microsoft\\Windows\\Chkdsk\\Proxy\" >nul 2>&1");
            sb.AppendLine("del /f /q \"%WINDIR%\\System32\\Tasks\\Microsoft\\Windows\\Windows Error Reporting\\QueueReporting\" >nul 2>&1");
            sb.AppendLine("rmdir /s /q \"%WINDIR%\\System32\\Tasks\\Microsoft\\Windows\\Customer Experience Improvement Program\" >nul 2>&1");
            sb.AppendLine();
        }

        sb.AppendLine("exit /b 0");
        return sb.ToString();
    }
}
