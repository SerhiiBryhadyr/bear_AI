using System.Text;
using UnityEngine;

// The five patterns of canon 03 (03_Bear_AI_Behavior.md).
public enum BearPattern
{
    RearPursuit,    // 1 - pursuit from behind
    SideParallel,   // 2 - parallel movement on the side
    Ambush,         // 3 - ambush ahead
    PathCrossing,   // 4 - crossing the path ahead (decoy, the bear shows itself)
    Hallucination   // 5 - hallucinations
}

// =====================================================================
// BearPatternChooser - which pattern fits this player (canon 03,
// "condition of choice" 1.1 / 2.1 / 3.1 / 4.1 / 5.1). Only rules, no
// state: takes a PlayerReadout, gives every pattern a score 0..1,
// picks the best. "static" = no component in the scene.
//
//   RearPursuit   - moves steadily forward, rarely checks behind, calm
//                   camera, no panic. Being spotted halves it.
//   SideParallel  - one side is checked much less than the other
//                   ("blind side", canon 2.1). Only if the player really
//                   checked the other side (MinSideCheck seconds): one
//                   glance is not enough, and no checks at all = no
//                   blind side either (both sides are the same).
//   Ambush        - keeps one route for a long time, ignores the
//                   surroundings, little running.
//   PathCrossing  - keeps one route, medium tension; an alert player
//                   who already spotted the bear fits it (it shows itself).
//   Hallucination - high tension: chaotic camera, loops, running.
//
// Not yet: flashlight, map context (path / open ground), bearPressure,
// number of encounters - so Ambush and PathCrossing are hard to tell
// apart for now. Numbers are a first guess, calibrate on tests.
// =====================================================================
public static class BearPatternChooser
{
    // A blind side exists only if the other side was checked at least
    // this long in the window (s). Tests 2.11: one glance gave 100%.
    private const float MinSideCheck = 2f;

    public struct Result
    {
        public BearPattern Pattern;
        public float[] Scores;          // index = (int)BearPattern
        public bool BlindSideIsLeft;    // for SideParallel

        // "RearPursuit 0.78 | others: SideParallel 0.55 (blind LEFT), Ambush 0.70, ..."
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Name(Pattern)).Append(' ').Append(Scores[(int)Pattern].ToString("F2"));

            if (Pattern == BearPattern.SideParallel)
                text.Append(" (blind ").Append(BlindSideIsLeft ? "LEFT" : "RIGHT").Append(')');

            text.Append(" | others: ");
            bool first = true;

            for (int i = 0; i < Scores.Length; i++)
            {
                if (i == (int)Pattern)
                    continue;

                if (!first)
                    text.Append(", ");
                first = false;

                text.Append(Name((BearPattern)i)).Append(' ').Append(Scores[i].ToString("F2"));
                if ((BearPattern)i == BearPattern.SideParallel)
                    text.Append(" (blind ").Append(BlindSideIsLeft ? "LEFT" : "RIGHT").Append(')');
            }

            return text.ToString();
        }
    }

    public static string Name(BearPattern pattern)
    {
        return pattern.ToString();
    }

    public static Result Choose(PlayerReadout r)
    {
        float[] scores = new float[5];

        float calmCamera = 1f - Mathf.Clamp01(r.CameraAverage / 90f);
        float tension = Mathf.Clamp01(r.LookBackShare * 2f + r.ChaoticShare + r.RunShare * 0.5f + (1f - r.Straightness));
        float mediumTension = 1f - Mathf.Abs(tension - 0.5f) * 2f;
        bool spotted = r.Spotted > 0;

        // 1. Pursuit from behind.
        float rear = 0.4f * (1f - Mathf.Clamp01(r.LookBackShare / 0.15f))
                   + 0.25f * Mathf.Clamp01((r.Straightness - 0.6f) / 0.35f)
                   + 0.2f * calmCamera
                   + 0.15f * (1f - r.RunShare);
        if (spotted)
            rear *= 0.5f;

        // 2. Parallel on the side: is there a blind side?
        //    checked = seconds on the side he looks at more, unchecked = the other.
        float checkedSide = Mathf.Max(r.Left, r.Right);
        float uncheckedSide = Mathf.Min(r.Left, r.Right);
        float blindness = checkedSide >= MinSideCheck ? 1f - uncheckedSide / checkedSide : 0f;
        float sideParallel = 0.7f * blindness + 0.3f * (1f - Mathf.Clamp01(r.ChaoticShare / 0.3f));

        // 3. Ambush ahead.
        float ambush = 0.4f * Mathf.Clamp01(r.LongestStraight / 10f)
                     + 0.3f * (1f - Mathf.Clamp01((r.LookBackShare + r.SideShare) / 0.3f))
                     + 0.3f * (1f - r.RunShare);

        // 4. Crossing the path ahead (the bear shows itself).
        float crossing = 0.45f * Mathf.Clamp01(r.LongestStraight / 8f)
                       + 0.25f * mediumTension
                       + 0.3f * (spotted ? 1f : 0f);

        // 5. Hallucinations.
        float hallucination = 0.4f * Mathf.Clamp01(r.ChaoticShare / 0.3f)
                            + 0.3f * Mathf.Clamp01(r.CameraAverage / 120f)
                            + 0.2f * (1f - r.Straightness)
                            + 0.1f * r.RunShare
                            + (spotted ? 0.15f : 0f);

        scores[(int)BearPattern.RearPursuit] = Mathf.Clamp01(rear);
        scores[(int)BearPattern.SideParallel] = Mathf.Clamp01(sideParallel);
        scores[(int)BearPattern.Ambush] = Mathf.Clamp01(ambush);
        scores[(int)BearPattern.PathCrossing] = Mathf.Clamp01(crossing);
        scores[(int)BearPattern.Hallucination] = Mathf.Clamp01(hallucination);

        int best = 0;
        for (int i = 1; i < scores.Length; i++)
        {
            if (scores[i] > scores[best])
                best = i;
        }

        return new Result
        {
            Pattern = (BearPattern)best,
            Scores = scores,
            BlindSideIsLeft = r.Left <= r.Right
        };
    }
}
