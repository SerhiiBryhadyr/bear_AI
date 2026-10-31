using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        float distance = 20f;
        float stepLength = 2.5f;
        int steps = 6;
        for (int i = 1; i <= steps; i++)
        {
                distance -= stepLength;
                Debug.Log("Крок " + i + ": до гравця " + distance + " м");
                if (distance <= 7.5f)
                {
                    Debug.Log("Ведмідь близько!");
                    
                }

            
        }
        Debug.Log("Ведмідь зупинився, до гравця " + distance + " м");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
