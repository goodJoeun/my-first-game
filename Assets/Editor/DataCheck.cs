using UnityEditor;
using UnityEngine;

// Unity -batchmode -executeMethod DataCheck.Run  : JSON 데이터가 스키마대로 읽히는지 확인한다.
public static class DataCheck
{
    [MenuItem("Tools/Check Data")]
    public static void Run()
    {
        var repo = new DataRepository();
        repo.Load();
        Debug.Log("DATACHECK events=" + repo.Events.Count + " days=" + repo.Days.Count + " rules=" + repo.Rules.Count);

        foreach (var day in repo.Days.Values)
        {
            int customers = 0;
            foreach (var s in day.slots)
            {
                var id = s.@fixed;
                if (string.IsNullOrEmpty(id)) { customers++; continue; }
                var e = repo.Get(id);
                if (e != null && (e.truth == "human" || e.truth == "mimic")) customers++;
            }
            Debug.Log("DATACHECK day" + day.day + " customers=" + customers + " (expected " + day.difficulty.customers + ")");
        }

        foreach (var e in repo.Events.Values)
        {
            int clues = e.clues != null ? e.clues.Length : 0;
            int choices = e.choices != null ? e.choices.Length : 0;
            Debug.Log("DATACHECK " + e.id + " type=" + e.type + " truth=" + e.truth + " choices=" + choices + " clues=" + clues
                      + (e.outcomes != null && e.outcomes.sell != null ? " sell.correct=" + e.outcomes.sell.correct : ""));
        }
        Debug.Log("DATACHECK done");
    }
}
