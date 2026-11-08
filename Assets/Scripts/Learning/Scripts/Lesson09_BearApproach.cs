using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson09_BearApproach : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        float distance = 30f;
        float stepLenght = 0f;
        float stopDistance = 6f;
        int step = 0;
        int playerTurnsAtStep = 100;
        bool noticed = false;
        int maxSteps = 20;

        while (distance > stopDistance )
        {
            step++;
            distance -= stepLenght;
            Debug.Log("Step: " + step + "Distance: " + distance);
            if (step == playerTurnsAtStep)
            {
                noticed = true;
                Debug.Log("The player noticed the bear!"); break;
            }
            else if (step >= maxSteps)
            {
                Debug.Log("Guard: " + step); break;
            }
        }
        if (noticed == true)
        {
            Debug.Log("Bear stopd at: " + distance + "m");
        }
        else if (distance <= stopDistance)
        {
            Debug.Log("Bear at: " + distance + "m");
        }
        else
        {
            Debug.Log("Bear stopped at: " + distance + "m");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
