using System.IO;
using System.Text.Json;

namespace AK680V2Toolkit;

public class ProfileService
{
    public string ProfilesDir { get; }

    public ProfileService()
    {
        ProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AK680V2Toolkit", "profiles");
        Directory.CreateDirectory(ProfilesDir);
    }

    public IEnumerable<string> ListProfiles() =>
        Directory.GetFiles(ProfilesDir, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>();

    public void CreateProfile(string name)
    {
        var path = Path.Combine(ProfilesDir, name + ".json");
        if (File.Exists(path)) return;
        var obj = new { name, created = DateTime.UtcNow };
        File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string? ReadProfile(string name)
    {
        var path = Path.Combine(ProfilesDir, name + ".json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void WriteProfile(string name, string json)
    {
        var path = Path.Combine(ProfilesDir, name + ".json");
        File.WriteAllText(path, json);
    }

    public void DeleteProfile(string name)
    {
        var path = Path.Combine(ProfilesDir, name + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    public void ExportProfile(string name, string destinationPath)
    {
        var src = Path.Combine(ProfilesDir, name + ".json");
        if (File.Exists(src)) File.Copy(src, destinationPath, overwrite: true);
    }

    public void ImportProfile(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var dst = Path.Combine(ProfilesDir, name + ".json");
        File.Copy(sourcePath, dst, overwrite: true);
    }
}