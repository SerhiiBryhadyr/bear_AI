using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson12_BearReact : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        React("Step", 5f);
        React("Branch", 21f);

        string[] sounds = { "Step", "Branch", "Step", "Wind" };
        float[] distances = { 3f, 12f, 25f, 20f };
//dlatego ze lepsze znam for niz foreach
        for (int i = 0; i < sounds.Length; i++)
        {
            React(sounds[i], distances[i]);
        }

    }


    void React(string sound, float distance)
    {
        if (distance < 5)
        {
            Debug.Log(sound + " at " + distance + " meters. Bear knows");
        }
        else if (distance <= 20)
        {
            Debug.Log(sound + " at " + distance + " meters. Investigate");
        }
        else
        {
            Debug.Log(sound + " at " + distance + " meters. Ignore");
        }
    }


    // Update is called once per frame
    void Update()
    {
        
    }
}
