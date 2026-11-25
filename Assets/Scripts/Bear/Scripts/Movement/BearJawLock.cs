using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// BearJawLock - keeps the bear's jaw and tongue closed (24.11).
//
// The model's mouth is badly skinned: when a clip moves RigJaw, the
// muzzle stretches and "stitches" instead of opening (a known problem
// of this rig - Bear_Animations.md). Every frame, after the animation,
// this puts the jaw and tongue back to the pose they had before the
// game started - the model's own pose, as in the Scene outside Play
// (mouth closed).
//
// Untick Locked in Play to see the clips as they are.
// Later (Steam): re-skin the mouth in Blender - then the bear can roar
// with an open mouth and this script goes away.
// =====================================================================
public class BearJawLock : MonoBehaviour
{
    [Tooltip("Keep the jaw and tongue still. Untick in Play to compare with the clips as they are.")]
    public bool locked = true;

    [Tooltip("Bones kept still (names in the bear's skeleton).")]
    public string[] boneNames = { "RigJaw", "RigTongue_1", "RigTongue_2", "RigTongue_3" };

    private readonly List<Transform> bones = new List<Transform>();
    private readonly List<Quaternion> rotations = new List<Quaternion>();
    private readonly List<Vector3> positions = new List<Vector3>();

    // Before the Animator moves anything: remember the closed pose.
    private void Awake()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);

        foreach (string boneName in boneNames)
        {
            Transform bone = System.Array.Find(all, t => t.name == boneName);
            if (bone == null)
            {
                Debug.LogWarning("BearJawLock: bone " + boneName + " not found in the bear's skeleton.");
                continue;
            }

            bones.Add(bone);
            rotations.Add(bone.localRotation);
            positions.Add(bone.localPosition);
        }
    }

    // After the animation of this frame: back to the closed pose.
    private void LateUpdate()
    {
        if (!locked)
            return;

        for (int i = 0; i < bones.Count; i++)
        {
            bones[i].localRotation = rotations[i];
            bones[i].localPosition = positions[i];
        }
    }
}
