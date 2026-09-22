using UnityEngine;

public struct SoundEvent
{
    public Vector3 Position;
    public SoundType Type;
    public float Loudness;

    public SoundEvent(Vector3 position, SoundType type, float loudness)
    {
        Position = position;
        Type = type;
        Loudness = loudness;
    }
}
