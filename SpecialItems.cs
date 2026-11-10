namespace DesktopGroups;

// Shell locations that can be added as items. Their targets are shell: links that Windows opens.
public static class SpecialItems
{
    public const string ThisPcTarget = "shell:MyComputerFolder";
    public const string RecycleBinTarget = "shell:RecycleBinFolder";
    public const string ThisPcName = "מחשב";
    public const string RecycleBinName = "סל מחזור";

    // Returns the special-folder id used to look up the icon, or null for ordinary paths.
    public static int? GetSpecialFolderId(string target) => target switch
    {
        ThisPcTarget => CSIDL_DRIVES,
        RecycleBinTarget => CSIDL_BITBUCKET,
        _ => null
    };

    private const int CSIDL_DRIVES = 0x0011;
    private const int CSIDL_BITBUCKET = 0x000A;
}
