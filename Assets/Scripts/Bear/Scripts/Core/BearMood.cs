using UnityEngine;

// The bear's three moods over the game (D-13, D-52):
// Good at the start -> Medium -> Angry near the end.
public enum BearMoodLevel
{
    Good,       // only scares the player, has no idea to kill him
    Medium,     // a realistic bear: guards its territory, drives the stranger away
    Angry       // no compromise
}

// =====================================================================
// BearMood - the bear's current mood. ONE job: keep the value.
// States read Level and pick their reactions and numbers from it
// (pattern 1 - D-52, Ideas/Rear_Pursuit_Moods.md).
//
// For now a manual switch in the Inspector - it can be changed in Play
// mode to test all three moods. Later the mood system D-13 will set it
// (grows during the game, death does not reset it); the scripts that
// read Level stay as they are.
// =====================================================================
public class BearMood : MonoBehaviour
{
    [Tooltip("Current mood. For now set by hand (also in Play mode); later by the mood system D-13.")]
    [SerializeField] private BearMoodLevel level = BearMoodLevel.Good;

    public BearMoodLevel Level => level;

    private BearMoodLevel lastLogged;

    private void Start()
    {
        lastLogged = level;
        Debug.Log("[Mood] start: " + level);
    }

    // Changed in the Inspector during Play -> one log line,
    // so the test logs show which mood was on.
    private void Update()
    {
        if (level == lastLogged)
            return;

        Debug.Log("[Mood] " + lastLogged + " -> " + level);
        lastLogged = level;
    }
}
