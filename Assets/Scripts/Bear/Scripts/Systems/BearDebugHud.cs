using UnityEngine;

// =====================================================================
// BearDebugHud - a line in the corner of the Game view (24.11, for
// tests only): how far the player is from the bear, its state and mood.
// Changes nothing in the AI. Untick Show (or remove the component) for
// screenshots and the build.
//
//   Bear 8.4 m from the eyes | 6.9 m from the body | Stalk | Good
//
// Yellow - closer than the close-encounter trigger (BearCloseEncounterState),
// red - closer than the boundary (BearBoundaries) when they differ.
// =====================================================================
public class BearDebugHud : MonoBehaviour
{
    [Tooltip("Show the line in the Game view.")]
    public bool show = true;
    public int fontSize = 22;
    [Tooltip("Top-left corner of the line (pixels).")]
    public Vector2 position = new Vector2(12f, 10f);

    private BearAI bearAI;
    private BearVision vision;
    private BearBodyPoints body;
    private BearMood mood;
    private BearCloseEncounterState closeEncounter;
    private BearBoundaries boundaries;
    private GUIStyle style;

    // Works on the bear itself, on any of its children, or anywhere in
    // the scene (then it finds the bear by itself).
    private void Awake()
    {
        bearAI = GetComponentInParent<BearAI>();
        if (bearAI == null)
            bearAI = FindObjectOfType<BearAI>();

        GameObject bear = bearAI != null ? bearAI.gameObject : gameObject;
        vision = bear.GetComponent<BearVision>();
        body = bear.GetComponent<BearBodyPoints>();
        mood = bear.GetComponent<BearMood>();
        closeEncounter = bear.GetComponent<BearCloseEncounterState>();
        boundaries = bear.GetComponent<BearBoundaries>();

        Debug.Log("[HUD] distance line for " + bear.name + (vision == null ? " - NO BearVision found" : ""));
    }

    private void OnGUI()
    {
        if (!show)
            return;

        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold };

        if (vision == null || vision.player == null)
        {
            style.normal.textColor = Color.red;
            GUI.Label(new Rect(position.x, position.y, Screen.width, fontSize * 2f), "BearDebugHud: bear or player not found", style);
            return;
        }

        Vector3 player = vision.player.position;
        float eyes = body != null && body.Active ? body.DistanceFromMuzzle(player) : Flat(player - vision.transform.position).magnitude;
        float bodyDistance = body != null && body.Active ? body.DistanceFromBody(player) : eyes;

        string text = "Bear " + eyes.ToString("F1") + " m from the eyes | " + bodyDistance.ToString("F1") + " m from the body";
        if (bearAI != null && bearAI.StateMachine != null)
            text += " | " + bearAI.StateMachine.CurrentState;
        if (mood != null)
            text += " | " + mood.Level;

        Color color = Color.white;
        if (closeEncounter != null && eyes <= closeEncounter.triggerDistance)
            color = Color.yellow;
        if (boundaries != null && eyes <= boundaries.boundaryDistance &&
            (closeEncounter == null || boundaries.boundaryDistance < closeEncounter.triggerDistance))
            color = Color.red;

        Rect rect = new Rect(position.x, position.y, Screen.width, fontSize * 2f);

        style.normal.textColor = Color.black;                       // shadow - readable on grass and sky
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }
}
