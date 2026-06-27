namespace DataMaker.Schema;

/// <summary>
/// Canonical on-disk locations for DataMaker's per-user data. Centralizes the
/// <c>%LOCALAPPDATA%/FOBO/DataMaker</c> root that was hand-built in 24+ call
/// sites — a relocation or rename now touches one place instead of risking a
/// silent split where one component looks in a different folder.
///
/// <para>
/// Lives in <c>DataMaker.Schema</c> because it's the only assembly every
/// consumer references (Core, KeyVault, Agent, the app, the CLI tools, tests
/// all reach Schema directly or transitively). #81e.
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>On-disk filename of the encrypted SQLite database.</summary>
    public const string DatabaseFileName = "datamaker.db";

    /// <summary>On-disk filename of the install-identity document.</summary>
    public const string InstallJsonFileName = "install.json";

    /// <summary><c>%LOCALAPPDATA%/FOBO/DataMaker</c> (or the OS equivalent).</summary>
    public static string DataRoot => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FOBO", "DataMaker");

    /// <summary>A path under <see cref="DataRoot"/>, e.g. <c>InData("tokens.json")</c>.</summary>
    public static string InData(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = DataRoot;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return System.IO.Path.Combine(all);
    }

    /// <summary>Full path to the encrypted database under <see cref="DataRoot"/>.</summary>
    public static string DatabasePath => InData(DatabaseFileName);

    /// <summary>Full path to <c>install.json</c> under <see cref="DataRoot"/>.</summary>
    public static string InstallJsonPath => InData(InstallJsonFileName);
}
