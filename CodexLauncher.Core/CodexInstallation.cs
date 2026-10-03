using System.Xml.Linq;

namespace CodexLauncher.Core;

public sealed record CodexInstallation(string PackageFamilyName, string InstallLocation, string Aumid)
{
    public static string BuildAumid(string packageFamilyName, string manifestXml)
    {
        var applications = XDocument.Parse(manifestXml).Descendants()
            .Where(element => element.Name.LocalName == "Application").ToArray();
        var app = applications.FirstOrDefault(element =>
            (string?)element.Attribute("Id") == "App"
            || ((string?)element.Attribute("Executable"))?.EndsWith("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) == true);
        var id = (string?)app?.Attribute("Id") ?? throw new InvalidDataException("未找到 Codex 桌面应用入口。");
        return packageFamilyName + "!" + id;
    }
}
