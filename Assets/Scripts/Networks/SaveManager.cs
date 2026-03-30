using System.IO;
using UnityEngine;


public static class SaveManager
{
    public const int SlotCount = 3;
    const string SaveFileName = "slot_{0}.json";

    static string GetPath(int slot)
    {
        return Path.Combine(
            Application.persistentDataPath,
            string.Format(SaveFileName, slot)
        );
    }

    /// Loads one save slot.
    public static SaveData Load(int slot)
    {
        string path = GetPath(slot);

        if (!File.Exists(path))
        {
            return new SaveData();
        }

        string json = File.ReadAllText(path);
        return JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
    }


    // Saves one slot to disk.
    public static void Save(int slot, SaveData data)
    {
        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(GetPath(slot), json);
    }

    // Deletes one slot file.
    public static void Delete(int slot)
    {
        string path = GetPath(slot);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
