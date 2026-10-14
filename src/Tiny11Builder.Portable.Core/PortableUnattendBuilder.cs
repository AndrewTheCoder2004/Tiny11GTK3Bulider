using System.Xml.Linq;

namespace Tiny11Builder.Portable;

public static class PortableUnattendBuilder
{
    public static string Create(
        string architecture,
        bool bypassHardwareRequirements = true,
        bool enableLocalAccount = true,
        bool compactInstallation = true)
    {
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        XNamespace wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";

        var procArch = architecture.Equals("arm64", StringComparison.OrdinalIgnoreCase) ||
                       architecture.Equals("aarch64", StringComparison.OrdinalIgnoreCase)
            ? "arm64"
            : "amd64";

        XElement Component(string name, params object[] children) => new(
            ns + "component",
            new XAttribute("name", name),
            new XAttribute("processorArchitecture", procArch),
            new XAttribute("publicKeyToken", "31bf3856ad364e35"),
            new XAttribute("language", "neutral"),
            new XAttribute("versionScope", "nonSxS"),
            children);

        var osImage = new XElement(ns + "OSImage",
            new XElement(ns + "Compact", compactInstallation ? "true" : "false"),
            new XElement(ns + "WillShowUI", "OnError"),
            new XElement(ns + "InstallFrom",
                new XElement(ns + "MetaData",
                    new XAttribute(wcm + "action", "add"),
                    new XElement(ns + "Key", "/IMAGE/INDEX"),
                    new XElement(ns + "Value", "1"))));

        var setupChildren = new List<object>
        {
            new XElement(ns + "ImageInstall", osImage),
            new XElement(ns + "DynamicUpdate",
                new XElement(ns + "Enable", "false"),
                new XElement(ns + "WillShowUI", "OnError")),
            new XElement(ns + "UserData",
                new XElement(ns + "ProductKey",
                    new XElement(ns + "Key", "")))
        };

        if (bypassHardwareRequirements)
        {
            var bypassCommands = new (string Key, string ValueName, string Value)[]
            {
                (@"HKLM\SYSTEM\Setup\LabConfig", "BypassCPUCheck", "1"),
                (@"HKLM\SYSTEM\Setup\LabConfig", "BypassRAMCheck", "1"),
                (@"HKLM\SYSTEM\Setup\LabConfig", "BypassSecureBootCheck", "1"),
                (@"HKLM\SYSTEM\Setup\LabConfig", "BypassStorageCheck", "1"),
                (@"HKLM\SYSTEM\Setup\LabConfig", "BypassTPMCheck", "1"),
                (@"HKLM\SYSTEM\Setup\MoSetup", "AllowUpgradesWithUnsupportedTPMOrCPU", "1")
            };

            var syncCommands = new XElement(ns + "RunSynchronous");
            var order = 1;
            foreach (var (key, valName, val) in bypassCommands)
            {
                syncCommands.Add(new XElement(ns + "RunSynchronousCommand",
                    new XAttribute(wcm + "action", "add"),
                    new XElement(ns + "Order", order++),
                    new XElement(ns + "Path", $"reg add \"{key}\" /v {valName} /t REG_DWORD /d {val} /f"),
                    new XElement(ns + "Description", $"Bypass {valName} check")));
            }
            setupChildren.Add(syncCommands);
        }

        var oobeChildren = new List<object>
        {
            new XElement(ns + "HideOEMRegistrationScreen", "true")
        };

        if (enableLocalAccount)
        {
            oobeChildren.Add(new XElement(ns + "HideOnlineAccountScreens", "true"));
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "unattend",
                new XAttribute(XNamespace.Xmlns + "wcm", wcm),
                new XElement(ns + "settings",
                    new XAttribute("pass", "windowsPE"),
                    Component("Microsoft-Windows-Setup", setupChildren.ToArray())),
                new XElement(ns + "settings",
                    new XAttribute("pass", "oobeSystem"),
                    Component("Microsoft-Windows-Shell-Setup",
                        new XElement(ns + "OOBE", oobeChildren.ToArray())),
                    new XElement(ns + "component",
                        new XAttribute("name", "Microsoft-Windows-Shell-Setup"),
                        new XAttribute("processorArchitecture", procArch),
                        new XAttribute("publicKeyToken", "31bf3856ad364e35"),
                        new XAttribute("language", "neutral"),
                        new XAttribute("versionScope", "nonSxS"),
                        new XElement(ns + "ConfigureChatAutoInstall", "false")))));

        return doc.ToString();
    }
}
