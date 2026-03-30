using System;
using System.Collections.Generic;

// Holds all data for a single save slot.

[Serializable]
public class SaveData
{
    public bool isEmpty = true;
    public string timestamp;
    public int roundNumber;
    public int money;
    public List<string> playerNames = new List<string>();


    // Returns a summary string for the save slot UI.
    public string GetSummary()
    {
        if (isEmpty)
        {
            return "New Game";
        }

        string players = playerNames.Count > 0
            ? string.Join(", ", playerNames)
            : "No players";

        return $"[{timestamp}] Round {roundNumber} ${money} {players}";
    }
}
