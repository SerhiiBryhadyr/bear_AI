using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson11_BearSounds : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        bool stillBranch = false;
        string[] heard = { "Step", "Branch", "Step", "Wind", "Step", "Branch" };
        List<string> real = new List<string>();

        foreach (string sound in heard)
        {
            if (sound != "Wind")
            {
                real.Add(sound);
            }
        }
        Debug.Log("Real sounds: " + real.Count);

        real.Remove("Branch");

        foreach (string sound in real)
        {
            Debug.Log("Sound: " + sound);
        }

        if (real.Contains("Branch"))
        {
            stillBranch = true;
        }
        
        if (stillBranch)
        {
            Debug.Log("Bear: something is still there");
        }
        else
        {
            Debug.Log("Bear: all clear");
        }
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
